using System;
using System.IO;
using System.Linq;

namespace OpenSnitchCli.Services
{
    public static class ProcessInfoHelper
    {
        public static (bool IsContainer, string Type, string Details, bool IsDaemon) GetProcessContext(int pid)
        {
            bool isDaemon = false;
            try
            {
                if (!Directory.Exists($"/proc/{pid}")) return (false, "", "", false);

                string[] cgroups;
                try { cgroups = File.ReadAllLines($"/proc/{pid}/cgroup"); }
                catch { return (false, "", "", false); }

                foreach (var line in cgroups)
                {
                    if (line.Contains(".service")) isDaemon = true;

                    // Docker / Kubernetes
                    if (line.Contains("docker") || line.Contains("kubepods")) 
                    {
                        var id = ExtractId(line, "docker-", ".scope") ?? ExtractId(line, "docker/", "");
                        return (true, "Docker", id ?? "Unknown", isDaemon);
                    }
                    
                    // Podman
                    if (line.Contains("libpod") || line.Contains("podman")) 
                    {
                        var id = ExtractId(line, "libpod-", ".scope");
                        return (true, "Podman", id ?? "Unknown", isDaemon);
                    }

                    // Flatpak
                    if (line.Contains("flatpak")) 
                    {
                         var id = ExtractFlatpakId(line);
                         return (true, "Flatpak", id ?? "Unknown", isDaemon);
                    }

                    // Snap
                    if (line.Contains("snap."))
                    {
                        return (true, "Snap", ExtractSnapId(line), isDaemon);
                    }
                }

                try 
                {
                    var initNet = File.ResolveLinkTarget("/proc/1/ns/net", true)?.FullName;
                    var procNet = File.ResolveLinkTarget($"/proc/{pid}/ns/net", true)?.FullName;
                    
                    if (initNet != null && procNet != null && initNet != procNet)
                    {
                        return (true, "Namespace", "Network Isolated", isDaemon);
                    }
                }
                catch {}
            }
            catch {}

            return (false, "Host", "", isDaemon);
        }

        private static string? ExtractId(string line, string prefix, string suffix)
        {
            var parts = line.Split(':');
            if (parts.Length < 3) return null;
            var path = parts[2];
            
            int pIndex = path.IndexOf(prefix);
            if (pIndex != -1)
            {
                var start = pIndex + prefix.Length;
                var end = string.IsNullOrEmpty(suffix) ? path.Length : path.IndexOf(suffix, start);
                if (end != -1) return path.Substring(start, end - start);
            }
            return null;
        }

        private static string? ExtractFlatpakId(string line)
        {
            // example: 0::/user.slice/user-1000.slice/user@1000.service/app.slice/app-flatpak-org.signal.Signal-1234.scope
            if (line.Contains("app-flatpak-"))
            {
                var start = line.IndexOf("app-flatpak-") + "app-flatpak-".Length;
                var end = line.IndexOf("-", start); // Find next dash which usually separates PID or random string
                if (end != -1) return line.Substring(start, end - start);
            }
            // Fallback for simpler paths
            if (line.Contains("/flatpak/app/"))
            {
                var parts = line.Split('/');
                var idx = Array.IndexOf(parts, "app");
                if (idx != -1 && idx + 1 < parts.Length) return parts[idx+1];
            }
            return null;
        }

        private static string ExtractSnapId(string line)
        {
             return "Snap";
        }
    }
}