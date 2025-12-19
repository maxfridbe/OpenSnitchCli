using System.Collections.Concurrent;
using System.Net;

namespace OpenSnitchTUI
{
    public class DnsManager
    {
        private readonly ConcurrentDictionary<string, string> _cache = new();
        private readonly string _cacheFilePath = "dns.cache.txt";
        private readonly ConcurrentDictionary<string, bool> _pendingLookups = new();

        public DnsManager()
        {
            LoadCache();
            _ = PeriodicallySaveCache();
        }

        public string GetDisplayName(string ip, string? daemonProvidedHost = null)
        {
            if (string.IsNullOrEmpty(ip)) return "";

            // If the daemon already provided a meaningful hostname, use it and cache it.
            if (!string.IsNullOrEmpty(daemonProvidedHost) && daemonProvidedHost != ip)
            {
                _cache[ip] = daemonProvidedHost;
                return daemonProvidedHost;
            }
            
            if (_cache.TryGetValue(ip, out var hostname))
            {
                return hostname; 
            }

            ResolveInBackground(ip);
            return ip; 
        }

        private void ResolveInBackground(string ip)
        {
            if (_pendingLookups.ContainsKey(ip)) return;
            if (!IPAddress.TryParse(ip, out var address)) return;

            _pendingLookups.TryAdd(ip, true);

            Task.Run(async () =>
            {
                try
                {
                    // Use local system DNS for reverse lookup
                    var entry = await Dns.GetHostEntryAsync(address);
                    string hostname = entry.HostName;

                    if (!string.IsNullOrEmpty(hostname) && hostname != ip)
                    {
                        _cache[ip] = hostname;
                    }
                    else
                    {
                        _cache[ip] = ip; // Cache IP on no record
                    }
                }
                catch
                {
                    _cache[ip] = ip; // Cache IP on failure
                }
                finally
                {
                    _pendingLookups.TryRemove(ip, out _);
                }
            });
        }

        private void LoadCache()
        {
            if (!File.Exists(_cacheFilePath)) return;
            try
            {
                var lines = File.ReadAllLines(_cacheFilePath);
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length == 2) _cache[parts[0]] = parts[1];
                }
            }
            catch {}
        }

        private async Task PeriodicallySaveCache()
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                SaveCache();
            }
        }

        private void SaveCache()
        {
            try
            {
                var lines = _cache.Select(kvp => $"{kvp.Key}|{kvp.Value}");
                File.WriteAllLines(_cacheFilePath, lines);
            }
            catch {}
        }
    }
}