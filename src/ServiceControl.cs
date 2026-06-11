using System;
using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;

namespace FreedomGuardian
{
    /// <summary>
    /// Thin wrapper over sc.exe + the registry for the service self-healing and
    /// hardening operations. Runs in the SYSTEM context of the services, so it
    /// has the rights to reconfigure both peers.
    /// </summary>
    public static class ServiceControl
    {
        // Service SECURITY_INFORMATION descriptors (SDDL).
        // Lockdown: Administrators keep query+start but LOSE stop (WP),
        // change-config (DC), delete (SD) and ownership (WD/WO). SYSTEM keeps
        // FULL control (incl. SD/WD/WO) so the service can self-heal the pair
        // AND restore normal permissions on stand-down -- without that, the
        // DACL would be unrecoverable except by taking ownership.
        public const string LockdownSddl =
            "D:(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCLCSWRPLOCRRC;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)";

        // Windows' standard default service security descriptor (restored on uninstall).
        public const string DefaultSddl =
            "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)";

        public const int StartAuto = 2;
        public const int StartManual = 3;
        public const int StartDisabled = 4;

        // ---- sc.exe runner ---------------------------------------------------

        public static int RunSc(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo("sc.exe", arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (Exception ex)
            {
                Log.Write("sc " + arguments + " failed: " + ex.Message);
                return -1;
            }
        }

        // ---- Queries ---------------------------------------------------------

        public static int GetStartType(string serviceName)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\" + serviceName))
                {
                    if (key == null) return -1;
                    object v = key.GetValue("Start");
                    return v == null ? -1 : Convert.ToInt32(v);
                }
            }
            catch { return -1; }
        }

        public static bool ServiceExists(string serviceName)
        {
            try
            {
                foreach (var s in ServiceController.GetServices())
                    if (string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { }
            return false;
        }

        public static bool IsRunning(string serviceName)
        {
            try
            {
                using (var sc = new ServiceController(serviceName))
                    return sc.Status == ServiceControllerStatus.Running
                        || sc.Status == ServiceControllerStatus.StartPending;
            }
            catch { return false; }
        }

        // ---- Mutations -------------------------------------------------------

        public static void EnsureAutoStart(string serviceName)
        {
            if (GetStartType(serviceName) != StartAuto)
            {
                Log.Write("Reverting start type to auto for " + serviceName);
                RunSc("config \"" + serviceName + "\" start= auto");
            }
        }

        public static void StartIfStopped(string serviceName)
        {
            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    if (sc.Status == ServiceControllerStatus.Stopped
                        || sc.Status == ServiceControllerStatus.Paused)
                    {
                        Log.Write("Starting service " + serviceName);
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("StartIfStopped(" + serviceName + ") failed: " + ex.Message);
            }
        }

        /// <summary>Keep a peer service both Automatic and Running.</summary>
        public static void EnsureRunningAndAuto(string serviceName)
        {
            if (!ServiceExists(serviceName)) return;
            EnsureAutoStart(serviceName);
            if (!IsRunning(serviceName)) StartIfStopped(serviceName);
        }

        public static void DisableService(string serviceName)
        {
            RunSc("config \"" + serviceName + "\" start= disabled");
        }

        public static void StopService(string serviceName)
        {
            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    if (sc.CanStop && sc.Status != ServiceControllerStatus.Stopped)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("StopService(" + serviceName + ") failed: " + ex.Message);
            }
        }

        // ---- Installer-time hardening ----------------------------------------

        public static void ConfigureFailureRecovery(string serviceName)
        {
            // Restart on crash; never reset the failure counter. Also restart on
            // non-crash stops (failureflag 1) as a backstop to the DACL lockdown.
            RunSc("failure \"" + serviceName + "\" reset= 0 actions= restart/2000/restart/2000/restart/2000");
            RunSc("failureflag \"" + serviceName + "\" 1");
        }

        public static void ApplyLockdown(string serviceName)
        {
            RunSc("sdset \"" + serviceName + "\" \"" + LockdownSddl + "\"");
        }

        public static void RestoreDefaultSecurity(string serviceName)
        {
            RunSc("sdset \"" + serviceName + "\" \"" + DefaultSddl + "\"");
        }
    }
}
