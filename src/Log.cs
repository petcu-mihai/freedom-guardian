using System;
using System.IO;

namespace FreedomGuardian
{
    /// <summary>Best-effort file logger. Never throws.</summary>
    public static class Log
    {
        private static readonly object Gate = new object();

        public static void Write(string message)
        {
            try
            {
                string dir = Config.DataDir;
                Directory.CreateDirectory(dir);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine;
                lock (Gate)
                {
                    File.AppendAllText(Config.LogPath, line);
                }
            }
            catch
            {
                // Logging must never break the service.
            }
        }
    }
}
