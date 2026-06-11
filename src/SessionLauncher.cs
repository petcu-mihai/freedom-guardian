using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace FreedomGuardian
{
    /// <summary>
    /// Launches a GUI process into the interactive (console) session from a
    /// SYSTEM service running in session 0. This is the standard
    /// WTSQueryUserToken -> DuplicateTokenEx -> CreateEnvironmentBlock ->
    /// CreateProcessAsUser flow. Also exposes the active user's SID so the
    /// run-key healer can target the right HKEY_USERS hive.
    /// </summary>
    public static class SessionLauncher
    {
        // ---- P/Invoke ---------------------------------------------------------

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool DuplicateTokenEx(
            IntPtr hExistingToken, uint dwDesiredAccess, IntPtr lpTokenAttributes,
            int impersonationLevel, int tokenType, out IntPtr phNewToken);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

        [DllImport("userenv.dll", SetLastError = true)]
        private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcessAsUser(
            IntPtr hToken, string lpApplicationName, string lpCommandLine,
            IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles,
            uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(
            IntPtr TokenHandle, int TokenInformationClass, IntPtr TokenInformation,
            int TokenInformationLength, out int ReturnLength);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool ConvertSidToStringSid(IntPtr pSid, out IntPtr ptrSid);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        // CharSet.Unicode is REQUIRED: CreateProcessAsUser is bound to the ...W
        // entry point, so lpDesktop ("winsta0\\default") must be marshaled as
        // Unicode. Without this the desktop name is passed as ANSI bytes, the
        // launched GUI process cannot attach to the interactive desktop, and it
        // dies instantly with STATUS_DLL_INIT_FAILED (0xC0000142).
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        private const int SecurityImpersonation = 2;
        private const int TokenPrimary = 1;
        private const uint MAXIMUM_ALLOWED = 0x02000000;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        private const uint CREATE_NEW_CONSOLE = 0x00000010;
        private const int TokenUser = 1;
        private const uint INVALID_SESSION = 0xFFFFFFFF;

        // ---- Public API -------------------------------------------------------

        /// <summary>
        /// Launch <paramref name="exePath"/> in the active console session.
        /// Returns false (without throwing) if no user is logged on.
        /// </summary>
        public static bool LaunchInActiveSession(string exePath)
        {
            uint sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == INVALID_SESSION) return false; // no interactive session

            IntPtr userToken = IntPtr.Zero, dupToken = IntPtr.Zero, env = IntPtr.Zero;
            try
            {
                if (!WTSQueryUserToken(sessionId, out userToken))
                    return false; // no user on this session (e.g. at logon screen)

                if (!DuplicateTokenEx(userToken, MAXIMUM_ALLOWED, IntPtr.Zero,
                        SecurityImpersonation, TokenPrimary, out dupToken))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx failed");

                if (!CreateEnvironmentBlock(out env, dupToken, false))
                    env = IntPtr.Zero; // non-fatal; launch with inherited env

                var si = new STARTUPINFO();
                si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
                si.lpDesktop = @"winsta0\default";

                PROCESS_INFORMATION pi;
                string workDir = Path.GetDirectoryName(exePath);

                bool ok = CreateProcessAsUser(
                    dupToken, exePath, null, IntPtr.Zero, IntPtr.Zero, false,
                    CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE,
                    env, workDir, ref si, out pi);

                if (!ok)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser failed");

                if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
                if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
                return true;
            }
            finally
            {
                if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
                if (dupToken != IntPtr.Zero) CloseHandle(dupToken);
                if (userToken != IntPtr.Zero) CloseHandle(userToken);
            }
        }

        /// <summary>SID string (e.g. "S-1-5-21-...") of the active console user, or null.</summary>
        public static string GetActiveUserSid()
        {
            uint sessionId = WTSGetActiveConsoleSessionId();
            if (sessionId == INVALID_SESSION) return null;

            IntPtr userToken = IntPtr.Zero, infoBuf = IntPtr.Zero, sidStr = IntPtr.Zero;
            try
            {
                if (!WTSQueryUserToken(sessionId, out userToken)) return null;

                int len;
                GetTokenInformation(userToken, TokenUser, IntPtr.Zero, 0, out len);
                if (len <= 0) return null;
                infoBuf = Marshal.AllocHGlobal(len);
                if (!GetTokenInformation(userToken, TokenUser, infoBuf, len, out len)) return null;

                // TOKEN_USER { SID_AND_ATTRIBUTES { PSID Sid; ... } } -> first ptr is the SID.
                IntPtr pSid = Marshal.ReadIntPtr(infoBuf);
                if (!ConvertSidToStringSid(pSid, out sidStr)) return null;
                return Marshal.PtrToStringUni(sidStr);
            }
            catch (Exception ex)
            {
                Log.Write("GetActiveUserSid failed: " + ex.Message);
                return null;
            }
            finally
            {
                if (sidStr != IntPtr.Zero) LocalFree(sidStr);
                if (infoBuf != IntPtr.Zero) Marshal.FreeHGlobal(infoBuf);
                if (userToken != IntPtr.Zero) CloseHandle(userToken);
            }
        }
    }
}
