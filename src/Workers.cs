using System;
using System.Diagnostics;
using System.Threading;

namespace FreedomGuardian
{
    /// <summary>
    /// The Guardian loop: keeps Freedom alive, heals its autostart, guards the
    /// Watch peer, reverts tampering with its own start type, and honours the
    /// unlock cooldown by standing the whole system down when it elapses.
    /// </summary>
    public static class GuardianWorker
    {
        public static void Run(Config cfg, ManualResetEvent stop, Action requestStop)
        {
            Log.Write("Guardian worker started (poll " + cfg.PollIntervalSeconds + "s, cooldown " + cfg.CooldownHours + "h).");
            do
            {
                try { Tick(cfg, requestStop); }
                catch (Exception ex) { Log.Write("Guardian tick error: " + ex.Message); }
            }
            while (!stop.WaitOne(cfg.PollInterval));
            Log.Write("Guardian worker stopped.");
        }

        private static void Tick(Config cfg, Action requestStop)
        {
            // The SYSTEM service owns the authoritative cooldown clock.
            UnlockStore.StampGrantIfRequested();

            if (UnlockStore.IsUnlockElapsed(cfg.Cooldown))
            {
                StandDown(cfg);
                requestStop();
                return;
            }

            // Enforcement layers.
            EnsureFreedomRunning(cfg);
            RunKeyHealer.Heal(cfg);
            ServiceControl.EnsureRunningAndAuto(cfg.WatchServiceName);
            ServiceControl.EnsureAutoStart(cfg.GuardianServiceName);
        }

        private static void EnsureFreedomRunning(Config cfg)
        {
            try
            {
                if (Process.GetProcessesByName(cfg.FreedomProcessName).Length == 0)
                {
                    bool launched = SessionLauncher.LaunchInActiveSession(cfg.FreedomExePath);
                    Log.Write(launched
                        ? "Relaunched " + cfg.FreedomProcessName
                        : "No active session to relaunch " + cfg.FreedomProcessName);
                }
            }
            catch (Exception ex)
            {
                Log.Write("EnsureFreedomRunning failed: " + ex.Message);
            }
        }

        private static void StandDown(Config cfg)
        {
            Log.Write("Unlock cooldown elapsed - standing down both services.");
            // Restore normal admin permissions (we run as SYSTEM, so we hold
            // WRITE_DAC) so the pair can be cleanly uninstalled afterwards.
            ServiceControl.RestoreDefaultSecurity(cfg.GuardianServiceName);
            ServiceControl.RestoreDefaultSecurity(cfg.WatchServiceName);
            ServiceControl.DisableService(cfg.GuardianServiceName);
            ServiceControl.DisableService(cfg.WatchServiceName);
            ServiceControl.StopService(cfg.WatchServiceName);
            // Guardian stops itself gracefully via requestStop() so SCM recovery
            // is not triggered.
        }
    }

    /// <summary>
    /// The Watch loop (the paired-respawn layer): its only job is to keep the
    /// Guardian service Running + Automatic, and to stand down when unlocked.
    /// </summary>
    public static class WatchWorker
    {
        public static void Run(Config cfg, ManualResetEvent stop, Action requestStop)
        {
            Log.Write("Watch worker started.");
            do
            {
                try { Tick(cfg, requestStop); }
                catch (Exception ex) { Log.Write("Watch tick error: " + ex.Message); }
            }
            while (!stop.WaitOne(cfg.PollInterval));
            Log.Write("Watch worker stopped.");
        }

        private static void Tick(Config cfg, Action requestStop)
        {
            if (UnlockStore.IsUnlockElapsed(cfg.Cooldown))
            {
                ServiceControl.DisableService(cfg.WatchServiceName);
                requestStop();
                return;
            }

            ServiceControl.EnsureRunningAndAuto(cfg.GuardianServiceName);
            ServiceControl.EnsureAutoStart(cfg.WatchServiceName);
        }
    }
}
