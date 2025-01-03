using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Collections.ObjectModel;

namespace RetroUI
{
    public class RomScanner
    {
        private readonly Dictionary<string, string> systemExtensions = new Dictionary<string, string>
        {
            { ".nes", "Nintendo Entertainment System" },
            { ".smc", "Super Nintendo" },
            { ".sfc", "Super Nintendo" },
            { ".n64", "Nintendo 64" },
            { ".z64", "Nintendo 64" },
            { ".v64", "Nintendo 64" },
            { ".gb", "Game Boy" },
            { ".gbc", "Game Boy Color" },
            { ".gba", "Game Boy Advance" },
            { ".md", "Sega Genesis" },
            { ".gen", "Sega Genesis" },
            { ".sms", "Sega Master System" },
            { ".iso", "PlayStation" },
            { ".bin", "PlayStation" }
        };

        public class RomScanResult
        {
            public string SystemName { get; set; }
            public List<(string Name, string Path)> Roms { get; set; }
        }

        public List<RomScanResult> ScanRomFolder(string folderPath)
        {
            var systems = new Dictionary<string, List<(string Name, string Path)>>();

            try
            {
                var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                    .Where(file => systemExtensions.Keys.Contains(Path.GetExtension(file).ToLower()));

                foreach (var file in files)
                {
                    var extension = Path.GetExtension(file).ToLower();
                    if (systemExtensions.TryGetValue(extension, out string systemName))
                    {
                        if (!systems.ContainsKey(systemName))
                        {
                            systems[systemName] = new List<(string Name, string Path)>();
                        }

                        systems[systemName].Add((
                            Name: Path.GetFileNameWithoutExtension(file),
                            Path: file
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning ROMs: {ex.Message}");
            }

            return systems.Select(kvp => new RomScanResult 
            { 
                SystemName = kvp.Key, 
                Roms = kvp.Value 
            })
            .OrderBy(s => s.SystemName)
            .ToList();
        }
    }
} 