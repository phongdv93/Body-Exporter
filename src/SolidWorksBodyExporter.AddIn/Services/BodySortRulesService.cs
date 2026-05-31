using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Newtonsoft.Json;
using SolidWorksBodyExporter.AddIn.Models;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Rule-based row ordering: match display/body names against user-defined keyword tiers.
    /// Each user edits their own <c>sort-rules.json</c> (created on first use, not shown in the main UI).
    /// </summary>
    public sealed class BodySortRulesService
    {
        private readonly BodySortRulesFile _rules;

        public BodySortRulesService(BodySortRulesFile rules = null)
        {
            _rules = rules ?? BodySortRulesFile.CreateUserTemplate();
        }

        public static string GetUserRulesPath()
        {
            return Path.Combine(GetUserRulesDirectory(), "sort-rules.json");
        }

        public static string GetUserRulesDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SolidWorksBodyExporter");
        }

        /// <summary>Creates an empty keyword template on first run so each user can define their own tiers.</summary>
        public static void EnsureUserRulesFile()
        {
            try
            {
                var path = GetUserRulesPath();
                var dir = GetUserRulesDirectory();
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (!File.Exists(path))
                {
                    var json = JsonConvert.SerializeObject(BodySortRulesFile.CreateUserTemplate(), Formatting.Indented);
                    File.WriteAllText(path, json, Encoding.UTF8);
                }

                var readmePath = Path.Combine(dir, "sort-rules.README.txt");
                if (!File.Exists(readmePath))
                {
                    File.WriteAllText(readmePath, BuildReadmeText(), Encoding.UTF8);
                }
            }
            catch
            {
                // Non-fatal — empty template still applies in memory.
            }
        }

        /// <summary>Opens the user's keyword file in Notepad (Export menu — not shown on the main toolbar).</summary>
        public static void OpenForEditing(Window owner)
        {
            EnsureUserRulesFile();
            var path = GetUserRulesPath();
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = "\"" + path + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    owner,
                    "Could not open keyword editor: " + ex.Message,
                    "Body Exporter",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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
                if (tier.Keywords == null || tier.Keywords.Count == 0)
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

        private static string BuildReadmeText()
        {
            return @"SolidWorks Body Exporter — BOM sort keywords
================================================

File sort-rules.json: each user defines their own keyword tiers.
Open it from Body Exporter → Export ▾ → ""BOM sort keywords…""

Structure:
  Priority  — lower number = earlier in BOM (10 before 20)
  Label     — display name for the tier (any text)
  Keywords  — words matched against body / display names (no accents needed)

Examples:
  ""mat ban"", ""chan tu"", ""cua"", ""hong"", ""bo goc""

Add or remove Tiers blocks to match your shop naming.
Save the file, then click ""BOM order"" or enable ""Auto BOM"".
";
        }
    }

    public sealed class BodySortRulesFile
    {
        public List<BodySortTier> Tiers { get; set; } = new List<BodySortTier>();

        /// <summary>Blank tiers — user fills Keywords to match their naming conventions.</summary>
        public static BodySortRulesFile CreateUserTemplate()
        {
            return new BodySortRulesFile
            {
                Tiers = new List<BodySortTier>
                {
                    new BodySortTier { Priority = 10, Label = "Nhóm ưu tiên 1", Keywords = new List<string>() },
                    new BodySortTier { Priority = 20, Label = "Nhóm ưu tiên 2", Keywords = new List<string>() },
                    new BodySortTier { Priority = 30, Label = "Nhóm ưu tiên 3", Keywords = new List<string>() },
                    new BodySortTier { Priority = 40, Label = "Nhóm ưu tiên 4", Keywords = new List<string>() },
                    new BodySortTier { Priority = 50, Label = "Nhóm ưu tiên 5", Keywords = new List<string>() }
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
