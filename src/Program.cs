using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;

namespace FreedomGuardian
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string verb = (args.Length > 0 ? args[0] : "help").ToLowerInvariant();

            switch (verb)
            {
                case "guardian":
                    ServiceBase.Run(new GuardianService());
                    return 0;

                case "watch":
                    ServiceBase.Run(new WatchService());
                    return 0;

                case "unlock":
                    return DoUnlock();

                case "relock":
                    return DoRelock();

                case "status":
                    return DoStatus();

                case "console":
                    return RunConsole();

                default:
                    PrintUsage();
                    return 0;
            }
        }

        // ---- CLI verbs -------------------------------------------------------

        private static int DoUnlock()
        {
            var cfg = Config.Load();
            UnlockStore.RequestUnlock();
            Console.WriteLine("Unlock requested. Freedom protection will be released after the");
            Console.WriteLine("cooldown of {0} hour(s). The clock is started by the service, not by you.", cfg.CooldownHours);
            Console.WriteLine("Run \"FreedomGuardian status\" to see time remaining, or \"relock\" to cancel.");
            return 0;
        }

        private static int DoRelock()
        {
            UnlockStore.Relock();
            Console.WriteLine("Relocked. Any pending unlock has been cancelled and protection re-armed.");
            return 0;
        }

        private static int DoStatus()
        {
            var cfg = Config.Load();
            TimeSpan remaining;
            var state = UnlockStore.GetState(cfg.Cooldown, out remaining);

            Console.WriteLine("Freedom Guardian status");
            Console.WriteLine("  Lock state      : " + state);
            if (state == UnlockStore.State.Pending && remaining > TimeSpan.Zero)
                Console.WriteLine("  Time remaining  : " + FormatSpan(remaining));

            PrintService("  Guardian service", cfg.GuardianServiceName);
            PrintService("  Watch service   ", cfg.WatchServiceName);

            bool freedom = Process.GetProcessesByName(cfg.FreedomProcessName).Length > 0;
            Console.WriteLine("  Freedom running : " + (freedom ? "yes" : "no"));
            return 0;
        }

        private static int RunConsole()
        {
            var cfg = Config.Load();
            var stop = new ManualResetEvent(false);
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; stop.Set(); };
            Console.WriteLine("Running Guardian loop in console mode. Press Ctrl+C to stop.");
            GuardianWorker.Run(cfg, stop, () => stop.Set());
            return 0;
        }

        // ---- helpers ---------------------------------------------------------

        private static void PrintService(string label, string name)
        {
            if (!ServiceControl.ServiceExists(name))
            {
                Console.WriteLine(label + ": not installed");
                return;
            }
            int start = ServiceControl.GetStartType(name);
            string startText = start == ServiceControl.StartAuto ? "auto"
                : start == ServiceControl.StartManual ? "manual"
                : start == ServiceControl.StartDisabled ? "disabled" : "unknown";
            Console.WriteLine(label + ": " + (ServiceControl.IsRunning(name) ? "running" : "stopped")
                + " (" + startText + ")");
        }

        private static string FormatSpan(TimeSpan t)
        {
            return string.Format("{0:00}h {1:00}m {2:00}s", (int)t.TotalHours, t.Minutes, t.Seconds);
        }

        private static void PrintUsage()
        {
            Console.WriteLine("FreedomGuardian - tamper-resistant watchdog for the Freedom blocker.");
            Console.WriteLine();
            Console.WriteLine("Usage: FreedomGuardian <verb>");
            Console.WriteLine("  status    Show lock state, service health and whether Freedom is running");
            Console.WriteLine("  unlock    Request release of protection (takes effect after the cooldown)");
            Console.WriteLine("  relock    Cancel a pending unlock and re-arm protection");
            Console.WriteLine("  console   Run the guardian loop in the foreground (debugging)");
            Console.WriteLine("  guardian  (service entry point - used by the Service Control Manager)");
            Console.WriteLine("  watch     (service entry point - used by the Service Control Manager)");
            Console.WriteLine();
            Console.WriteLine("Install/uninstall with the bundled install.ps1 / uninstall.ps1 (run as admin).");
        }
    }

    // ---- Service hosts -------------------------------------------------------

    internal sealed class GuardianService : ServiceBase
    {
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private Thread _thread;

        public GuardianService()
        {
            ServiceName = Config.Load().GuardianServiceName;
            CanStop = true;
            CanShutdown = true;
            AutoLog = false;
        }

        protected override void OnStart(string[] args)
        {
            _stop.Reset();
            var cfg = Config.Load();
            _thread = new Thread(() => GuardianWorker.Run(cfg, _stop, () => this.Stop())) { IsBackground = true };
            _thread.Start();
        }

        protected override void OnStop()
        {
            _stop.Set();
            if (_thread != null) _thread.Join(TimeSpan.FromSeconds(20));
        }
    }

    internal sealed class WatchService : ServiceBase
    {
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private Thread _thread;

        public WatchService()
        {
            ServiceName = Config.Load().WatchServiceName;
            CanStop = true;
            CanShutdown = true;
            AutoLog = false;
        }

        protected override void OnStart(string[] args)
        {
            _stop.Reset();
            var cfg = Config.Load();
            _thread = new Thread(() => WatchWorker.Run(cfg, _stop, () => this.Stop())) { IsBackground = true };
            _thread.Start();
        }

        protected override void OnStop()
        {
            _stop.Set();
            if (_thread != null) _thread.Join(TimeSpan.FromSeconds(20));
        }
    }
}
