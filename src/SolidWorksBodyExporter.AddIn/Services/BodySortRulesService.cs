using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using SolidWorksBodyExporter.AddIn.Models;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Rule-based row ordering (no AI): match display/body names against priority keyword tiers.
    /// </summary>
    public sealed class BodySortRulesService
    {
        private readonly BodySortRulesFile _rules;

        public BodySortRulesService(BodySortRulesFile rules = null)
        {
            _rules = rules ?? BodySortRulesFile.CreateDefault();
        }

        public static BodySortRulesService LoadFromUserSettings()
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SolidWorksBodyExporter");
                var path = Path.Combine(dir, "sort-rules.json");
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
                    var needle = Normalize(keyword);
                    if (needle.Length == 0)
                    {
                        continue;
                    }

                    if (haystack.Contains(needle))
                    {
                        best = Math.Min(best, tier.Priority);
                        break;
                    }
                }
            }

            return best;
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
                        Label = "Chính / khung",
                        Keywords = new List<string>
                        {
                            "mat ban", "mặt bàn", "khung", "frame", "than chinh", "thân",
                            "panel chinh", "main", "table top", "desktop"
                        }
                    },
                    new BodySortTier
                    {
                        Priority = 20,
                        Label = "Vách / cánh",
                        Keywords = new List<string>
                        {
                            "vach", "vách", "canh", "cánh", "side", "panel", "hồi", "hoi",
                            "wing", "door", "cua", "cửa"
                        }
                    },
                    new BodySortTier
                    {
                        Priority = 30,
                        Label = "Phụ kiện / bọ",
                        Keywords = new List<string>
                        {
                            "bo goc", "bọ góc", "bo", "bọ", "nep", "nẹp", "trim", "cap",
                            "cover", "treo", "hook", "bracket", "phu kien", "phụ kiện"
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
