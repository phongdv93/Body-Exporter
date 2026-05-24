using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using SolidWorksBodyExporter.AddIn.Models;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Rule-based row ordering (no AI): match display/body names against priority keyword tiers
    /// for production BOM (main panels first, trim/edge band last).
    /// </summary>
    public sealed class BodySortRulesService
    {
        private readonly BodySortRulesFile _rules;

        public BodySortRulesService(BodySortRulesFile rules = null)
        {
            _rules = rules ?? BodySortRulesFile.CreateDefault();
        }

        public static string GetUserRulesPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SolidWorksBodyExporter",
                "sort-rules.json");
        }

        /// <summary>Writes default <c>sort-rules.json</c> on first run so users can edit tiers.</summary>
        public static void EnsureUserRulesFile()
        {
            try
            {
                var path = GetUserRulesPath();
                if (File.Exists(path))
                {
                    return;
                }

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonConvert.SerializeObject(BodySortRulesFile.CreateDefault(), Formatting.Indented);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch
            {
                // Non-fatal — built-in defaults still apply.
            }
        }

        public static BodySortRulesService LoadFromUserSettings()
        {
            EnsureUserRulesFile();
            try
            {
                var path = GetUserRulesPath();
                if (!File.Exists(path))
                {
                    return new BodySortRulesService();
                }

                var json = File.ReadAllText(path, Encoding.UTF8);
                var file = JsonConvert.DeserializeObject<BodySortRulesFile>(json);
                if (file?.Tiers == null || file.Tiers.Count == 0)
                {
                    return new BodySortRulesService();
                }

                return new BodySortRulesService(file);
            }
            catch
            {
                return new BodySortRulesService();
            }
        }

        public IReadOnlyList<BodyExportRow> Sort(IEnumerable<BodyExportRow> rows)
        {
            var list = (rows ?? Enumerable.Empty<BodyExportRow>()).ToList();
            return list
                .OrderBy(r => r.Status == BodyRowStatus.Deleted)
                .ThenBy(r => Score(r))
                .ThenBy(r => r.DisplayName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public BodySortAnalysis Analyze(IReadOnlyList<BodyExportRow> currentOrder)
        {
            var analysis = new BodySortAnalysis();
            if (currentOrder == null || currentOrder.Count == 0)
            {
                return analysis;
            }

            var suggested = Sort(currentOrder);
            var suggestedIndex = new Dictionary<BodyExportRow, int>();
            for (var i = 0; i < suggested.Count; i++)
            {
                suggestedIndex[suggested[i]] = i;
            }

            for (var i = 0; i < currentOrder.Count; i++)
            {
                var row = currentOrder[i];
                if (row.Status == BodyRowStatus.Deleted)
                {
                    continue;
                }

                if (!suggestedIndex.TryGetValue(row, out var want) || want == i)
                {
                    continue;
                }

                var name = row.DisplayName ?? row.SolidWorksBodyName ?? "?";
                var tier = FindTierLabel(Score(row));
                analysis.OutOfOrder.Add(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        "\"{0}\" ({1}) — nên ở gần đầu danh sách hơn (vị trí hiện tại {2}, gợi ý ~{3})",
                        name,
                        tier,
                        i + 1,
                        want + 1));
            }

            return analysis;
        }

        private int Score(BodyExportRow row)
        {
            if (row == null)
            {
                return 9000;
            }

            if (row.Status == BodyRowStatus.Deleted)
            {
                return 9999;
            }

            var haystack = Normalize($"{row.DisplayName} {row.SolidWorksBodyName}");
            var best = 5000;
            foreach (var tier in _rules.Tiers.OrderBy(t => t.Priority))
            {
                if (tier.Keywords == null)
                {
                    continue;
                }

                foreach (var keyword in tier.Keywords)
                {
                    if (NameContainsKeyword(haystack, keyword))
                    {
                        best = Math.Min(best, tier.Priority);
                        break;
                    }
                }
            }

            return best;
        }

        private static bool NameContainsKeyword(string haystack, string keyword)
        {
            var needle = Normalize(keyword);
            if (needle.Length == 0 || string.IsNullOrEmpty(haystack))
            {
                return false;
            }

            if (needle.Contains(" "))
            {
                return haystack.Contains(needle);
            }

            if (needle.Length <= 3)
            {
                return Regex.IsMatch(
                    haystack,
                    @"(^|[\s_\-./\\])" + Regex.Escape(needle) + @"($|[\s_\-./\\])",
                    RegexOptions.CultureInvariant);
            }

            return haystack.Contains(needle);
        }

        private string FindTierLabel(int score)
        {
            var tier = _rules.Tiers?.FirstOrDefault(t => t.Priority == score);
            return tier?.Label ?? "Khác";
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var lower = value.Trim().ToLowerInvariant();
            var formD = lower.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(formD.Length);
            foreach (var ch in formD)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }

    public sealed class BodySortRulesFile
    {
        public List<BodySortTier> Tiers { get; set; } = new List<BodySortTier>();

        public static BodySortRulesFile CreateDefault()
        {
            return new BodySortRulesFile
            {
                Tiers = new List<BodySortTier>
                {
                    new BodySortTier
                    {
                        Priority = 10,
                        Label = "Mặt / nóc",
                        Keywords = new List<string>
                        {
                            "mat ban", "mat tren", "mat duoi", "mat chinh", "mat go", "mat",
                            "mặt bàn", "mặt trên", "mặt dưới", "mặt chính", "mặt",
                            "noc", "nóc", "nap", "nắp", "top", "table top", "desktop", "countertop"
                        }
                    },
                    new BodySortTier
                    {
                        Priority = 20,
                        Label = "Chân / khung",
                        Keywords = new List<string>
                        {
                            "chan ban", "chan tu", "chan go", "chan",
                            "chân bàn", "chân tủ", "chân",
                            "khung", "frame", "than chinh", "than doc", "than ngang", "leg", "foot", "base"
                        }
                    },
                    new BodySortTier
                    {
                        Priority = 30,
                        Label = "Cửa / cánh",
                        Keywords = new List<string>
                        {
                            "cua tu", "cua kinh", "canh cua", "canh kinh", "canh trai", "canh phai",
                            "cua", "cửa", "door", "flap", "canh", "cánh", "wing", "draw front"
                        }
                    },
                    new BodySortTier
                    {
                        Priority = 40,
                        Label = "Hông / vách / hồi",
                        Keywords = new List<string>
                        {
                            "hong tu", "hong trai", "hong phai", "hong",
                            "hông", "side", "vach", "vách", "hoi", "hồi", "panel", "back", "partition"
                        }
                    },
                    new BodySortTier
                    {
                        Priority = 50,
                        Label = "Ngăn / kệ / hộc",
                        Keywords = new List<string>
                        {
                            "ngan keo", "ngan tu", "ngan", "ngăn kéo", "ngăn",
                            "ke", "kệ", "shelf", "drawer", "hop", "hộc", "tray"
                        }
                    },
                    new BodySortTier
                    {
                        Priority = 60,
                        Label = "Chi tiết phụ",
                        Keywords = new List<string>
                        {
                            "tam lot", "tấm lót", "de", "đế", "vach lot", "divider", "support", "bracket", "treo", "hook"
                        }
                    },
                    new BodySortTier
                    {
                        Priority = 80,
                        Label = "Bọ / nẹp / viền",
                        Keywords = new List<string>
                        {
                            "bo goc", "bo canh", "bo nep",
                            "bọ góc", "bọ cạnh", "bọ",
                            "nep", "nẹp", "trim", "edging", "cap", "vien", "viền", "lip", "edge band"
                        }
                    }
                }
            };
        }
    }

    public sealed class BodySortTier
    {
        public int Priority { get; set; }
        public string Label { get; set; }
        public List<string> Keywords { get; set; } = new List<string>();
    }

    public sealed class BodySortAnalysis
    {
        public List<string> OutOfOrder { get; } = new List<string>();
        public bool HasIssues => OutOfOrder.Count > 0;
    }
}
