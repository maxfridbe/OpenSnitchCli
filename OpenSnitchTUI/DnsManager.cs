using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json; // Required for GetFromJsonAsync

namespace OpenSnitchTUI
{
    public class DnsManager
    {
        private readonly ConcurrentDictionary<string, string> _cache = new();
        private static readonly HttpClient _httpClient = new() 
        { 
            BaseAddress = new Uri("https://cloudflare-dns.com/dns-query") 
        };
        private readonly string _cacheFilePath = "dns.cache.txt";
        private readonly ConcurrentDictionary<string, bool> _pendingLookups = new();

        public DnsManager()
        {
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/dns-json");
            LoadCache();
            _ = PeriodicallySaveCache();
        }

        public string GetDisplayName(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return "";
            
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
                    string reverseName = GetReverseDnsName(address);
                    // Type 12 is PTR
                    var response = await _httpClient.GetFromJsonAsync<DohResponse>($"?name={reverseName}&type=PTR");

                    string? hostname = null;
                    if (response?.Answer != null && response.Answer.Count > 0)
                    {
                        // The 'data' field in PTR record is the hostname
                        hostname = response.Answer[0].Data?.TrimEnd('.');
                    }

                    if (!string.IsNullOrEmpty(hostname))
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

        private string GetReverseDnsName(IPAddress ip)
        {
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return string.Join(".", ip.ToString().Split('.').Reverse()) + ".in-addr.arpa";
            }
            else
            {
                // IPv6 expansion is complex to do manually perfectly, 
                // but usually: expand to full hex, reverse nibbles, join with dots, + .ip6.arpa
                // For this CLI, IPv4 is 99% of cases. 
                // Let's rely on standard library if possible? No built-in reverser.
                // Simple implementation:
                var bytes = ip.GetAddressBytes();
                var hex = Convert.ToHexString(bytes); // Uppercase hex
                // Reverse nibbles
                var reversed = string.Join(".", hex.Reverse());
                return reversed + ".ip6.arpa";
            }
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

    // JSON models for Cloudflare DoH
    public class DohResponse
    {
        [JsonPropertyName("Answer")]
        public List<DohAnswer>? Answer { get; set; }
    }

    public class DohAnswer
    {
        [JsonPropertyName("data")]
        public string? Data { get; set; }
    }
}
