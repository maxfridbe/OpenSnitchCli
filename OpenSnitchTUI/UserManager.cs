using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenSnitchTUI
{
    public class UserManager
    {
        private readonly ConcurrentDictionary<string, string> _cache = new();

        public UserManager()
        {
        }

        public string GetUser(string uidString)
        {
            if (string.IsNullOrEmpty(uidString)) return "";
            if (!uint.TryParse(uidString, out var uid)) return uidString; // Not a valid UID

            if (_cache.TryGetValue(uidString, out var user))
            {
                return user;
            }

            user = ResolveUser(uid);
            _cache[uidString] = user;
            return user;
        }

        private string ResolveUser(uint uid)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    IntPtr ptr = getpwuid(uid);
                    if (ptr != IntPtr.Zero)
                    {
                        var passwd = Marshal.PtrToStructure<Passwd>(ptr);
                        var name = Marshal.PtrToStringAnsi(passwd.pw_name);
                        if (!string.IsNullOrEmpty(name))
                        {
                            return name;
                        }
                    }
                }
            }
            catch 
            {
                // Fallback or ignore
            }

            return $"UID:{uid}";
        }

        [DllImport("libc", SetLastError = true)]
        private static extern IntPtr getpwuid(uint uid);

        [StructLayout(LayoutKind.Sequential)]
        private struct Passwd
        {
            public IntPtr pw_name;   // char *pw_name
            public IntPtr pw_passwd; // char *pw_passwd
            public uint pw_uid;      // uid_t pw_uid
            public uint pw_gid;      // gid_t pw_gid
            public IntPtr pw_gecos;  // char *pw_gecos
            public IntPtr pw_dir;    // char *pw_dir
            public IntPtr pw_shell;  // char *pw_shell
        }
    }
}