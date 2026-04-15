using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Storage;

namespace Task_Flyout.Services
{
    public class IconPackInfo
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public int IconCount { get; set; }
    }

    /// <summary>
    /// Manages user-imported weather icon packs (Breezy-style drawable_filter.xml + PNGs).
    /// Default pack is the built-in Win11 emoji rendering; imported packs live under
    /// LocalFolder/IconPacks/{id}/ with a filters.json plus copied PNG drawables.
    /// </summary>
    public class IconPackService
    {
        public const string BuiltInEmojiId = "win11-emoji";

        public static IconPackService Instance { get; } = new IconPackService();

        private readonly string _rootDir;
        private Dictionary<string, string>? _activeFilter;
        private string? _activeDrawableDir;
        private string? _loadedPackId;

        private IconPackService()
        {
            _rootDir = Path.Combine(ApplicationData.Current.LocalFolder.Path, "IconPacks");
            Directory.CreateDirectory(_rootDir);
        }

        public string ActivePackId
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherIconPack"] as string ?? BuiltInEmojiId;
            set
            {
                ApplicationData.Current.LocalSettings.Values["WeatherIconPack"] = value;
                _loadedPackId = null;
                _activeFilter = null;
            }
        }

        public bool IsBuiltInActive => ActivePackId == BuiltInEmojiId;

        public List<IconPackInfo> ListInstalledPacks()
        {
            var result = new List<IconPackInfo>();
            if (!Directory.Exists(_rootDir)) return result;

            foreach (var dir in Directory.GetDirectories(_rootDir))
            {
                var filtersPath = Path.Combine(dir, "filters.json");
                if (!File.Exists(filtersPath)) continue;
                var id = Path.GetFileName(dir);
                var drawableDir = Path.Combine(dir, "drawable");
                int count = Directory.Exists(drawableDir) ? Directory.GetFiles(drawableDir, "*.png").Length : 0;
                result.Add(new IconPackInfo
                {
                    Id = id,
                    DisplayName = ReadDisplayName(dir, id),
                    FolderPath = dir,
                    IconCount = count
                });
            }
            return result;
        }

        private static string ReadDisplayName(string packDir, string fallback)
        {
            var namePath = Path.Combine(packDir, "name.txt");
            if (File.Exists(namePath))
            {
                try { return File.ReadAllText(namePath).Trim(); }
                catch { }
            }
            return fallback;
        }

        public void DeletePack(string id)
        {
            if (string.IsNullOrEmpty(id) || id == BuiltInEmojiId) return;
            var dir = Path.Combine(_rootDir, id);
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
            if (ActivePackId == id) ActivePackId = BuiltInEmojiId;
        }

        /// <summary>
        /// Import an icon pack from a user-provided .zip (a GitHub source archive of a
        /// Breezy-style icon provider). Returns the new pack id, or null if parsing failed.
        /// </summary>
        public async Task<string?> ImportFromZipAsync(StorageFile zipFile, string? displayName = null)
        {
            if (zipFile == null) return null;

            var tempDir = Path.Combine(Path.GetTempPath(), "tf-iconpack-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                using (var stream = await zipFile.OpenStreamForReadAsync())
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    // Locate drawable_filter.xml anywhere in the archive.
                    var filterEntry = archive.Entries.FirstOrDefault(e =>
                        string.Equals(Path.GetFileName(e.FullName), "drawable_filter.xml", StringComparison.OrdinalIgnoreCase));
                    if (filterEntry == null) return null;

                    // Parse <item name=".." value=".." /> pairs.
                    var filter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    using (var fs = filterEntry.Open())
                    {
                        var doc = XDocument.Load(fs);
                        foreach (var item in doc.Descendants("item"))
                        {
                            var name = item.Attribute("name")?.Value;
                            var value = item.Attribute("value")?.Value;
                            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
                                filter[name!] = value!;
                        }
                    }
                    if (filter.Count == 0) return null;

                    // Collect all needed drawable resource names (values), then find matching PNGs.
                    var needed = new HashSet<string>(filter.Values, StringComparer.OrdinalIgnoreCase);
                    var drawableOut = Path.Combine(tempDir, "drawable");
                    Directory.CreateDirectory(drawableOut);

                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                        var fileName = Path.GetFileName(entry.FullName);
                        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;

                        // Only care about files that sit inside a "drawable" folder (res/drawable, drawable-xxhdpi, etc.)
                        var parent = Path.GetFileName(Path.GetDirectoryName(entry.FullName.Replace('\\', '/')) ?? "");
                        if (!parent.StartsWith("drawable", StringComparison.OrdinalIgnoreCase)) continue;

                        var baseName = Path.GetFileNameWithoutExtension(fileName);
                        if (!needed.Contains(baseName)) continue;

                        var outPath = Path.Combine(drawableOut, baseName + ".png");
                        if (File.Exists(outPath)) continue; // first win; avoid density duplicates
                        using var outStream = File.Create(outPath);
                        using var inStream = entry.Open();
                        await inStream.CopyToAsync(outStream);
                    }

                    // Write filters.json (simple key=value lines keep it dependency-free).
                    var filtersJson = BuildFiltersJson(filter);
                    File.WriteAllText(Path.Combine(tempDir, "filters.json"), filtersJson);

                    // Determine id / display name.
                    var id = SanitizeId(displayName ?? Path.GetFileNameWithoutExtension(zipFile.Name));
                    if (string.IsNullOrWhiteSpace(id)) id = "pack-" + DateTime.Now.Ticks;

                    var finalDir = Path.Combine(_rootDir, id);
                    if (Directory.Exists(finalDir)) Directory.Delete(finalDir, recursive: true);
                    Directory.Move(tempDir, finalDir);
                    tempDir = ""; // consumed

                    File.WriteAllText(Path.Combine(finalDir, "name.txt"), displayName ?? id);
                    return id;
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }
            }
        }

        private static string BuildFiltersJson(Dictionary<string, string> filter)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            bool first = true;
            foreach (var kv in filter)
            {
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("  \"").Append(JsonEscape(kv.Key)).Append("\": \"").Append(JsonEscape(kv.Value)).Append('"');
            }
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private static string JsonEscape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string SanitizeId(string raw)
        {
            var chars = raw.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray();
            return new string(chars).Trim('-').ToLowerInvariant();
        }

        private void EnsureLoaded()
        {
            var id = ActivePackId;
            if (_loadedPackId == id && _activeFilter != null) return;
            _loadedPackId = id;
            _activeFilter = null;
            _activeDrawableDir = null;

            if (id == BuiltInEmojiId) return;
            var packDir = Path.Combine(_rootDir, id);
            var filtersPath = Path.Combine(packDir, "filters.json");
            var drawableDir = Path.Combine(packDir, "drawable");
            if (!File.Exists(filtersPath) || !Directory.Exists(drawableDir)) return;

            try
            {
                var filter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(filtersPath))
                {
                    var trimmed = line.Trim().TrimEnd(',');
                    if (!trimmed.StartsWith("\"")) continue;
                    var parts = trimmed.Split(new[] { "\":" }, 2, StringSplitOptions.None);
                    if (parts.Length != 2) continue;
                    var key = parts[0].Trim().Trim('"');
                    var val = parts[1].Trim().Trim('"');
                    if (!string.IsNullOrEmpty(key)) filter[key] = val;
                }
                _activeFilter = filter;
                _activeDrawableDir = drawableDir;
            }
            catch { }
        }

        /// <summary>
        /// Resolves a weather code to a file:/// URI for the active icon pack's main drawable,
        /// or null if the built-in emoji pack is active or no drawable matches.
        /// </summary>
        public string? TryResolveBitmapUri(int weatherCode, bool isDay, bool isOpenMeteo)
        {
            if (IsBuiltInActive) return null;
            EnsureLoaded();
            if (_activeFilter == null || _activeDrawableDir == null) return null;

            var key = WeatherCodeToSemanticKey(weatherCode, isDay, isOpenMeteo);
            if (key == null) return null;

            if (!_activeFilter.TryGetValue(key, out var resName))
            {
                // Fallback: try opposite day/night (some packs only define one).
                var alt = key.Replace("_day", "_night");
                if (alt == key) alt = key.Replace("_night", "_day");
                if (!_activeFilter.TryGetValue(alt, out resName)) return null;
            }

            var path = Path.Combine(_activeDrawableDir, resName + ".png");
            if (!File.Exists(path)) return null;
            return new Uri(path).AbsoluteUri;
        }

        public string? TryResolveBitmapUri(string wttrCode, bool isDay)
        {
            if (!int.TryParse(wttrCode, out var c)) return null;
            return TryResolveBitmapUri(c, isDay, isOpenMeteo: false);
        }

        /// <summary>
        /// Resolves a weather code to up to four stacked PNG layers from the active icon pack:
        /// the main drawable plus 0..3 rain overlay drawables depending on rain intensity.
        /// Returns an empty array if the built-in pack is active or no main drawable matches.
        /// Array index 0 is the base, 1..3 are overlays to be drawn on top in order.
        /// </summary>
        public string[] TryResolveBitmapLayers(int weatherCode, bool isDay, bool isOpenMeteo)
        {
            var main = TryResolveBitmapUri(weatherCode, isDay, isOpenMeteo);
            if (main == null) return Array.Empty<string>();

            int intensity = GetRainIntensity(weatherCode, isOpenMeteo);
            if (intensity <= 0 || _activeFilter == null || _activeDrawableDir == null)
                return new[] { main };

            var layers = new List<string> { main };
            string dayNight = isDay ? "day" : "night";
            for (int i = 1; i <= intensity; i++)
            {
                var key = $"weather_rain_{dayNight}_{i}";
                if (!_activeFilter.TryGetValue(key, out var resName)) continue;
                var path = Path.Combine(_activeDrawableDir, resName + ".png");
                if (!File.Exists(path)) continue;
                var uri = new Uri(path).AbsoluteUri;
                if (!layers.Contains(uri)) // filter often reuses the main drawable for _1
                    layers.Add(uri);
            }
            return layers.ToArray();
        }

        /// <summary>
        /// Classifies rain intensity: 0 = not rain / drizzle, 1 = light, 2 = moderate, 3 = heavy.
        /// Used to decide how many overlay layers to stack on the base rain drawable.
        /// </summary>
        public static int GetRainIntensity(int code, bool isOpenMeteo)
        {
            if (isOpenMeteo)
            {
                return code switch
                {
                    51 or 53 or 55 => 1,                 // drizzle light/moderate/dense
                    56 or 57 => 1,                        // freezing drizzle
                    61 or 80 => 1,                        // slight rain / slight shower
                    63 or 81 or 66 => 2,                  // moderate rain / shower / freezing
                    65 or 82 or 67 => 3,                  // heavy rain / violent shower
                    _ => 0
                };
            }
            // wttr.in codes
            return code switch
            {
                176 or 263 or 266 or 293 => 1,            // patchy / light rain
                296 or 299 or 353 or 356 => 2,            // light-to-moderate rain / shower
                302 or 305 or 308 or 359 or 314 => 3,     // heavy rain / heavy shower
                _ => 0
            };
        }

        /// <summary>
        /// Map a wttr.in or Open-Meteo weather code to a Breezy-style semantic key
        /// (e.g. "weather_rain_day"). Returns null for unknown codes.
        /// </summary>
        public static string? WeatherCodeToSemanticKey(int code, bool isDay, bool isOpenMeteo)
        {
            string suffix = isDay ? "day" : "night";
            string? kind = null;

            if (isOpenMeteo)
            {
                kind = code switch
                {
                    0 or 1 => "clear",
                    2 => "partly_cloudy",
                    3 => "cloudy",
                    45 or 48 => "fog",
                    51 or 53 or 55 or 56 or 57 => "rain",
                    61 or 63 or 65 or 80 or 81 or 82 => "rain",
                    66 or 67 => "sleet",
                    71 or 73 or 75 or 85 or 86 => "snow",
                    77 => "snow",
                    95 or 96 or 99 => "thunderstorm",
                    _ => null
                };
            }
            else
            {
                kind = code switch
                {
                    113 => "clear",
                    116 => "partly_cloudy",
                    119 or 122 => "cloudy",
                    143 or 248 or 260 => "fog",
                    176 or 263 or 266 or 293 or 296 or 299 or 302 or 305 or 308 or 356 or 359 => "rain",
                    179 or 227 or 323 or 326 or 329 or 332 or 335 or 338 or 368 or 371 => "snow",
                    200 or 386 or 389 or 392 or 395 => "thunderstorm",
                    182 or 185 or 281 or 284 or 311 or 314 or 317 or 350 or 362 or 365 or 374 or 377 => "sleet",
                    _ => null
                };
            }

            if (kind == null) return null;
            return $"weather_{kind}_{suffix}";
        }
    }
}
