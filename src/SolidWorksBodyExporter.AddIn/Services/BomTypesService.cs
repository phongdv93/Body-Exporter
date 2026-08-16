using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Newtonsoft.Json;
using SolidWorksBodyExporter.AddIn.Models;
using SolidWorksBodyExporter.AddIn.Services.Api;

namespace SolidWorksBodyExporter.AddIn.Services
{
    public static class BomTypesService
    {
        private static readonly object Gate = new object();
        private static BomTypesFile _cache;
        private static readonly string[] CustomPaletteBg =
        {
            "#FFE0F2FE", "#FFFCE7F3", "#FFECFDF5", "#FFFEF3C7", "#FFEDE9FE"
        };
        private static readonly string[] CustomPaletteFg =
        {
            "#FF0369A1", "#FFBE185D", "#FF047857", "#FFB45309", "#FF6D28D9"
        };

        public static string GetTypesPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SolidWorksBodyExporter",
                "bom-types.json");
        }

        public static BomTypesFile Load()
        {
            lock (Gate)
            {
                if (_cache != null)
                {
                    return Clone(_cache);
                }

                EnsureFile();
                try
                {
                    var json = File.ReadAllText(GetTypesPath(), Encoding.UTF8);
                    var file = JsonConvert.DeserializeObject<BomTypesFile>(json);
                    _cache = MergeWithDefaults(file);
                }
                catch
                {
                    _cache = BomTypesFile.CreateDefaults();
                }

                return Clone(_cache);
            }
        }

        public static void Save(BomTypesFile file)
        {
            if (file == null)
            {
                return;
            }

            var merged = MergeWithDefaults(file);
            var dir = Path.GetDirectoryName(GetTypesPath());
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonConvert.SerializeObject(merged, Formatting.Indented);
            File.WriteAllText(GetTypesPath(), json, Encoding.UTF8);
            lock (Gate)
            {
                _cache = Clone(merged);
            }
        }

        public static void InvalidateCache()
        {
            lock (Gate)
            {
                _cache = null;
            }
        }

        public static string UiLanguage()
        {
            var lang = AppSettings.LoadOrCreate().UiLanguage;
            return string.Equals(lang, "vi", StringComparison.OrdinalIgnoreCase) ? "vi" : "en";
        }

        public static BomTypeDefinition Find(string typeId)
        {
            var id = NormalizeId(typeId);
            return Load().Types.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
                   ?? Load().Types.First(t => t.Id == BomTypeIds.Detail);
        }

        public static string DisplayName(string typeId)
        {
            return Find(typeId).DisplayName(UiLanguage());
        }

        public static string ErpSection(string typeId)
        {
            var t = Find(typeId);
            return string.IsNullOrWhiteSpace(t.ErpSection) ? t.Id : t.ErpSection.Trim();
        }

        public static bool IncludeInExcel(string typeId)
        {
            return Find(typeId).IncludeInExcel;
        }

        public static bool IncludeInErp(string typeId)
        {
            return Find(typeId).IncludeInErp;
        }

        public static Brush BackgroundBrush(string typeId)
        {
            return BrushFromHex(Find(typeId).BackgroundHex, "#FFE8F5E9");
        }

        public static Brush ForegroundBrush(string typeId)
        {
            return BrushFromHex(Find(typeId).ForegroundHex, "#FF15803D");
        }

        public static string NormalizeId(string typeId)
        {
            if (string.IsNullOrWhiteSpace(typeId))
            {
                return BomTypeIds.Detail;
            }

            var v = typeId.Trim();
            // Legacy enum / Vietnamese / English names from older builds.
            if (v.Equals("Detail", StringComparison.OrdinalIgnoreCase)
                || v.Equals("0", StringComparison.Ordinal)
                || v.Equals("Chi tiết", StringComparison.OrdinalIgnoreCase)
                || v.Equals("Chi tiet", StringComparison.OrdinalIgnoreCase)
                || v.Equals("wood", StringComparison.OrdinalIgnoreCase))
            {
                return BomTypeIds.Detail;
            }

            if (v.Equals("Hardware", StringComparison.OrdinalIgnoreCase)
                || v.Equals("1", StringComparison.Ordinal)
                || v.Equals("Vật tư", StringComparison.OrdinalIgnoreCase)
                || v.Equals("Vat tu", StringComparison.OrdinalIgnoreCase))
            {
                return BomTypeIds.Hardware;
            }

            if (v.Equals("Packaging", StringComparison.OrdinalIgnoreCase)
                || v.Equals("2", StringComparison.Ordinal)
                || v.Equals("Bao bì", StringComparison.OrdinalIgnoreCase)
                || v.Equals("Bao bi", StringComparison.OrdinalIgnoreCase))
            {
                return BomTypeIds.Packaging;
            }

            if (v.Equals("Other", StringComparison.OrdinalIgnoreCase)
                || v.Equals("3", StringComparison.Ordinal)
                || v.Equals("Khác", StringComparison.OrdinalIgnoreCase)
                || v.Equals("Khac", StringComparison.OrdinalIgnoreCase)
                || v.Equals("Skip", StringComparison.OrdinalIgnoreCase))
            {
                return BomTypeIds.Other;
            }

            return v.ToLowerInvariant();
        }

        /// <summary>
        /// First matching non-Detail type by keywords (priority = SortOrder). Returns null if no match.
        /// </summary>
        public static string MatchTypeId(params string[] names)
        {
            var hay = NormalizeMatchText(string.Join(" ", names ?? Array.Empty<string>()));
            if (string.IsNullOrWhiteSpace(hay))
            {
                return null;
            }

            foreach (var type in Load().Types.OrderBy(t => t.SortOrder)
                         .Where(t => t.Keywords != null && t.Keywords.Count > 0))
            {
                foreach (var kw in type.Keywords)
                {
                    var needle = NormalizeMatchText(kw);
                    if (needle.Length > 0 && hay.IndexOf(needle, StringComparison.Ordinal) >= 0)
                    {
                        return type.Id;
                    }
                }
            }

            return null;
        }

        public static string NewCustomId(IEnumerable<BomTypeDefinition> existing)
        {
            var set = new HashSet<string>(
                (existing ?? Enumerable.Empty<BomTypeDefinition>()).Select(t => t.Id),
                StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < 999; i++)
            {
                var id = "custom" + i;
                if (!set.Contains(id))
                {
                    return id;
                }
            }

            return "custom" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        public static BomTypeDefinition CreateCustomTemplate(IEnumerable<BomTypeDefinition> existing)
        {
            var list = (existing ?? Enumerable.Empty<BomTypeDefinition>()).ToList();
            var idx = list.Count(t => !t.IsBuiltIn);
            return new BomTypeDefinition
            {
                Id = NewCustomId(list),
                NameEn = "Custom " + (idx + 1),
                NameVi = "Tuỳ chỉnh " + (idx + 1),
                ErpSection = "custom",
                IsBuiltIn = false,
                SortOrder = 100 + idx * 10,
                IncludeInExcel = true,
                IncludeInErp = true,
                BackgroundHex = CustomPaletteBg[idx % CustomPaletteBg.Length],
                ForegroundHex = CustomPaletteFg[idx % CustomPaletteFg.Length],
                Keywords = new List<string>()
            };
        }

        private static void EnsureFile()
        {
            var path = GetTypesPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(path))
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(BomTypesFile.CreateDefaults(), Formatting.Indented), Encoding.UTF8);
            }
        }

        private static BomTypesFile MergeWithDefaults(BomTypesFile file)
        {
            var defaults = BomTypesFile.CreateDefaults();
            if (file?.Types == null || file.Types.Count == 0)
            {
                return defaults;
            }

            var byId = file.Types
                .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Id))
                .GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var builtIn in defaults.Types)
            {
                if (!byId.TryGetValue(builtIn.Id, out var existing))
                {
                    byId[builtIn.Id] = builtIn;
                    continue;
                }

                existing.IsBuiltIn = true;
                existing.Id = builtIn.Id;
                if (string.IsNullOrWhiteSpace(existing.ErpSection))
                {
                    existing.ErpSection = builtIn.ErpSection;
                }

                if (string.IsNullOrWhiteSpace(existing.BackgroundHex))
                {
                    existing.BackgroundHex = builtIn.BackgroundHex;
                }

                if (string.IsNullOrWhiteSpace(existing.ForegroundHex))
                {
                    existing.ForegroundHex = builtIn.ForegroundHex;
                }

                if (string.IsNullOrWhiteSpace(existing.NameEn))
                {
                    existing.NameEn = builtIn.NameEn;
                }

                if (string.IsNullOrWhiteSpace(existing.NameVi))
                {
                    existing.NameVi = builtIn.NameVi;
                }
            }

            // Migrate legacy ExportOther* flags into Other type once.
            try
            {
                var settings = AppSettings.LoadOrCreate();
                if (byId.TryGetValue(BomTypeIds.Other, out var other))
                {
                    if (settings.ExportOtherCategoryToExcel)
                    {
                        other.IncludeInExcel = true;
                    }

                    if (settings.ExportOtherCategoryToErp)
                    {
                        other.IncludeInErp = true;
                    }
                }
            }
            catch
            {
                /* ignore */
            }

            return new BomTypesFile
            {
                Version = 1,
                Types = byId.Values.OrderBy(t => t.SortOrder).ThenBy(t => t.NameEn).ToList()
            };
        }

        private static BomTypesFile Clone(BomTypesFile src)
        {
            var json = JsonConvert.SerializeObject(src);
            return JsonConvert.DeserializeObject<BomTypesFile>(json) ?? BomTypesFile.CreateDefaults();
        }

        private static Brush BrushFromHex(string hex, string fallback)
        {
            try
            {
                var h = string.IsNullOrWhiteSpace(hex) ? fallback : hex.Trim();
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(h));
            }
            catch
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
            }
        }

        private static string NormalizeMatchText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var formD = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);
            foreach (var ch in formD)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(char.ToLowerInvariant(ch));
                }
            }

            return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
        }
    }
}
