using System;
using System.Globalization;
using System.IO;

namespace FreedomGuardian
{
    /// <summary>
    /// The deliberate-friction escape hatch.
    ///
    /// Two-file design so the cooldown cannot be trivially defeated:
    ///   * unlock.request  - a flag the user creates (via the `unlock` verb).
    ///   * secure\unlock.granted - the authoritative start time, written ONCE by
    ///     the SYSTEM service the first time it sees a request. Its folder is
    ///     ACL-locked to SYSTEM by the installer, so the timestamp cannot be
    ///     back-dated to shorten the wait.
    ///
    /// State machine: Locked -> (user requests) -> Pending -> (cooldown elapses)
    /// -> Unlocked. `relock` clears both files at any time.
    /// </summary>
    public static class UnlockStore
    {
        public enum State { Locked, Pending, Unlocked }

        // ---- CLI side --------------------------------------------------------

        /// <summary>Create the request flag (idempotent).</summary>
        public static void RequestUnlock()
        {
            Directory.CreateDirectory(Config.DataDir);
            if (!File.Exists(Config.UnlockRequestPath))
                File.WriteAllText(Config.UnlockRequestPath, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        }

        /// <summary>Cancel any pending/elapsed unlock and re-arm protection.</summary>
        public static void Relock()
        {
            SafeDelete(Config.UnlockRequestPath);
            SafeDelete(Config.UnlockGrantedPath);
        }

        // ---- Service side ----------------------------------------------------

        /// <summary>
        /// Called from the SYSTEM service. If a request exists but no granted
        /// clock has been stamped yet, stamp it now (authoritative start).
        /// </summary>
        public static void StampGrantIfRequested()
        {
            try
            {
                if (File.Exists(Config.UnlockRequestPath) && !File.Exists(Config.UnlockGrantedPath))
                {
                    Directory.CreateDirectory(Config.SecureDir);
                    File.WriteAllText(Config.UnlockGrantedPath, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                    Log.Write("Unlock requested; cooldown clock started.");
                }
            }
            catch (Exception ex)
            {
                Log.Write("StampGrantIfRequested failed: " + ex.Message);
            }
        }

        public static DateTime? GrantedAtUtc()
        {
            try
            {
                if (!File.Exists(Config.UnlockGrantedPath)) return null;
                string s = File.ReadAllText(Config.UnlockGrantedPath).Trim();
                DateTime dt;
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt))
                    return dt.ToUniversalTime();
            }
            catch { }
            return null;
        }

        /// <summary>True once the cooldown has fully elapsed since the grant.</summary>
        public static bool IsUnlockElapsed(TimeSpan cooldown)
        {
            DateTime? g = GrantedAtUtc();
            if (g == null) return false;
            return DateTime.UtcNow - g.Value >= cooldown;
        }

        public static State GetState(TimeSpan cooldown, out TimeSpan remaining)
        {
            remaining = TimeSpan.Zero;
            DateTime? g = GrantedAtUtc();
            bool requested = File.Exists(Config.UnlockRequestPath);

            if (g == null)
                return requested ? State.Pending : State.Locked; // request seen but service hasn't stamped yet

            TimeSpan elapsed = DateTime.UtcNow - g.Value;
            if (elapsed >= cooldown) return State.Unlocked;
            remaining = cooldown - elapsed;
            return State.Pending;
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
