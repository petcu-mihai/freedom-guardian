using System;
using Microsoft.Win32;

namespace FreedomGuardian
{
    /// <summary>
    /// Restores Freedom's own autostart Run value if the user deletes it.
    /// Because the service runs as SYSTEM, "HKCU" is the wrong hive; we write to
    /// the active user's hive via HKEY_USERS\&lt;sid&gt; instead.
    /// </summary>
    public static class RunKeyHealer
    {
        private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void Heal(Config cfg)
        {
            try
            {
                string sid = SessionLauncher.GetActiveUserSid();
                if (string.IsNullOrEmpty(sid)) return; // nobody logged on

                using (var run = Registry.Users.OpenSubKey(sid + "\\" + RunSubKey, writable: true))
                {
                    if (run == null) return; // hive not loaded
                    string desired = "\"" + cfg.FreedomExePath + "\"";
                    object current = run.GetValue(cfg.RunKeyName);
                    if (current == null || !string.Equals(current.ToString(), desired, StringComparison.OrdinalIgnoreCase))
                    {
                        run.SetValue(cfg.RunKeyName, desired, RegistryValueKind.String);
                        Log.Write("Restored Run key '" + cfg.RunKeyName + "' for " + sid);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("RunKeyHealer.Heal failed: " + ex.Message);
            }
        }
    }
}
