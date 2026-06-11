using System;
using System.Collections.Generic;
using System.IO;

namespace FreedomGuardian
{
    /// <summary>
    /// Runtime configuration. Stored as a tiny key=value file under
    /// %ProgramData%\FreedomGuardian\config.ini so it can be edited without a
    /// rebuild. Sensible defaults are baked in, so the file is optional.
    /// </summary>
    public sealed class Config
    {
        // Service identities.
        public string GuardianServiceName = "FreedomGuardian";
        public string WatchServiceName = "FreedomGuardianWatch";

        // What we keep alive. FreedomBlocker.exe is the parent; it spawns
        // FreedomProxy.exe itself, so guarding the Blocker is enough.
        public string FreedomExePath = @"C:\Program Files (x86)\Freedom\FreedomBlocker.exe";
        public string FreedomProcessName = "FreedomBlocker"; // no .exe

        // The autostart Run value Freedom installs for the interactive user.
        public string RunKeyName = "Freedom";

        // Monitor cadence and the deliberate unlock cooldown (the escape hatch).
        public int PollIntervalSeconds = 3;
        public double CooldownHours = 2.0;

        public TimeSpan PollInterval { get { return TimeSpan.FromSeconds(Math.Max(1, PollIntervalSeconds)); } }
        public TimeSpan Cooldown { get { return TimeSpan.FromHours(Math.Max(0, CooldownHours)); } }

        // ---- Well-known paths -------------------------------------------------

        public static string DataDir
        {
            get
            {
                string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                return Path.Combine(pd, "FreedomGuardian");
            }
        }

        public static string ConfigPath { get { return Path.Combine(DataDir, "config.ini"); } }

        /// <summary>User/admin-writable flag created by the `unlock` verb.</summary>
        public static string UnlockRequestPath { get { return Path.Combine(DataDir, "unlock.request"); } }

        /// <summary>
        /// Authoritative cooldown clock, written ONLY by the SYSTEM service the
        /// first time it observes a request. Its folder ACL is locked to SYSTEM
        /// by the installer so the start time cannot be back-dated.
        /// </summary>
        public static string UnlockGrantedPath { get { return Path.Combine(DataDir, "secure", "unlock.granted"); } }

        public static string SecureDir { get { return Path.Combine(DataDir, "secure"); } }

        public static string LogPath { get { return Path.Combine(DataDir, "guardian.log"); } }

        // ---- Load / save ------------------------------------------------------

        public static Config Load()
        {
            var c = new Config();
            try
            {
                if (File.Exists(ConfigPath))
                {
                    foreach (var raw in File.ReadAllLines(ConfigPath))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = line.Substring(0, eq).Trim();
                        string val = line.Substring(eq + 1).Trim();
                        c.Apply(key, val);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Config.Load failed, using defaults: " + ex.Message);
            }
            return c;
        }

        private void Apply(string key, string val)
        {
            switch (key.ToLowerInvariant())
            {
                case "guardianservicename": GuardianServiceName = val; break;
                case "watchservicename": WatchServiceName = val; break;
                case "freedomexepath": FreedomExePath = val; break;
                case "freedomprocessname": FreedomProcessName = val; break;
                case "runkeyname": RunKeyName = val; break;
                case "pollintervalseconds": int.TryParse(val, out PollIntervalSeconds); break;
                case "cooldownhours": double.TryParse(val, out CooldownHours); break;
            }
        }

        public string ToFileContents()
        {
            var lines = new List<string>
            {
                "# FreedomGuardian configuration",
                "GuardianServiceName=" + GuardianServiceName,
                "WatchServiceName=" + WatchServiceName,
                "FreedomExePath=" + FreedomExePath,
                "FreedomProcessName=" + FreedomProcessName,
                "RunKeyName=" + RunKeyName,
                "PollIntervalSeconds=" + PollIntervalSeconds,
                "CooldownHours=" + CooldownHours,
            };
            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }
    }
}
