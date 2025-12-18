using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenSnitchTUI
{
    public class DescriptionManager
    {
        private static readonly Lazy<DescriptionManager> _instance = new(() => new DescriptionManager());
        public static DescriptionManager Instance => _instance.Value;

        private readonly Dictionary<string, string> _descriptions = new();

        private DescriptionManager()
        {
            LoadDescriptions();
        }

        private void LoadDescriptions()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "OpenSnitchTUI.descriptions.txt";

                using (Stream? stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (StreamReader reader = new StreamReader(stream))
                        {
                            string? line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                var parts = line.Split('|', 2);
                                if (parts.Length == 2)
                                {
                                    _descriptions[parts[0].Trim()] = parts[1].Trim();
                                }
                            }
                        }
                    }
                    else
                    {
                        // Fallback to file system if resource not found (development)
                        var assemblyLocation = assembly.Location;
                        var directory = Path.GetDirectoryName(assemblyLocation);
                        var filePath = Path.Combine(directory ?? "", "descriptions.txt");
                        if (File.Exists(filePath))
                        {
                            var lines = File.ReadAllLines(filePath);
                            foreach (var line in lines)
                            {
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                var parts = line.Split('|', 2);
                                if (parts.Length == 2)
                                {
                                    _descriptions[parts[0].Trim()] = parts[1].Trim();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading descriptions: {ex.Message}");
            }
        }

        public string GetDescription(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";

            // Direct lookup
            if (_descriptions.TryGetValue(path, out var desc))
            {
                return desc;
            }

            // Look for basename if path is absolute
            if (path.StartsWith("/"))
            {
                var fileName = Path.GetFileName(path);
                var binPath = "/usr/bin/" + fileName;
                if (_descriptions.TryGetValue(binPath, out desc))
                {
                    return desc;
                }
                
                var sbinPath = "/usr/sbin/" + fileName;
                if (_descriptions.TryGetValue(sbinPath, out desc))
                {
                    return desc;
                }
            }

            return "";
        }
    }
}
