using System;
using System.Collections.Generic;
using System.Linq;

namespace SolidWorksBodyExporter.AddIn.Models
{
    public static class BomTypeIds
    {
        public const string Detail = "detail";
        public const string Hardware = "hardware";
        public const string Packaging = "packaging";
        public const string Other = "other";
    }

    /// <summary>One BOM line type (built-in or user-defined).</summary>
    public sealed class BomTypeDefinition
    {
        public string Id { get; set; }

        public string NameEn { get; set; }

        public string NameVi { get; set; }

        /// <summary>ERP <c>section</c> value.</summary>
        public string ErpSection { get; set; }

        public List<string> Keywords { get; set; } = new List<string>();

        public bool IncludeInExcel { get; set; } = true;

        public bool IncludeInErp { get; set; } = true;

        public bool IsBuiltIn { get; set; }

        public int SortOrder { get; set; }

        public string BackgroundHex { get; set; }

        public string ForegroundHex { get; set; }

        public string DisplayName(string uiLanguage)
        {
            var vi = string.Equals(uiLanguage, "vi", StringComparison.OrdinalIgnoreCase);
            if (vi && !string.IsNullOrWhiteSpace(NameVi))
            {
                return NameVi.Trim();
            }

            if (!string.IsNullOrWhiteSpace(NameEn))
            {
                return NameEn.Trim();
            }

            return NameVi?.Trim() ?? Id ?? "Type";
        }

        public string KeywordsText
        {
            get => Keywords == null || Keywords.Count == 0
                ? string.Empty
                : string.Join(", ", Keywords);
            set
            {
                Keywords = (value ?? string.Empty)
                    .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
    }

    public sealed class BomTypesFile
    {
        public int Version { get; set; } = 1;

        public List<BomTypeDefinition> Types { get; set; } = new List<BomTypeDefinition>();

        public static BomTypesFile CreateDefaults()
        {
            return new BomTypesFile
            {
                Types = new List<BomTypeDefinition>
                {
                    new BomTypeDefinition
                    {
                        Id = BomTypeIds.Detail,
                        NameEn = "Detail",
                        NameVi = "Chi tiết",
                        ErpSection = "wood",
                        IsBuiltIn = true,
                        SortOrder = 10,
                        IncludeInExcel = true,
                        IncludeInErp = true,
                        BackgroundHex = "#FFE8F5E9",
                        ForegroundHex = "#FF15803D",
                        Keywords = new List<string>()
                    },
                    new BomTypeDefinition
                    {
                        Id = BomTypeIds.Hardware,
                        NameEn = "Hardware",
                        NameVi = "Vật tư",
                        ErpSection = "hardware",
                        IsBuiltIn = true,
                        SortOrder = 20,
                        IncludeInExcel = true,
                        IncludeInErp = true,
                        BackgroundHex = "#FFFFF3E0",
                        ForegroundHex = "#FFC2410C",
                        Keywords = new List<string>()
                    },
                    new BomTypeDefinition
                    {
                        Id = BomTypeIds.Packaging,
                        NameEn = "Packaging",
                        NameVi = "Bao bì",
                        ErpSection = "packaging",
                        IsBuiltIn = true,
                        SortOrder = 30,
                        IncludeInExcel = true,
                        IncludeInErp = true,
                        BackgroundHex = "#FFF3E5F5",
                        ForegroundHex = "#FF7E22CE",
                        Keywords = new List<string>()
                    },
                    new BomTypeDefinition
                    {
                        Id = BomTypeIds.Other,
                        NameEn = "Other",
                        NameVi = "Khác",
                        ErpSection = "other",
                        IsBuiltIn = true,
                        SortOrder = 40,
                        IncludeInExcel = false,
                        IncludeInErp = false,
                        BackgroundHex = "#FFF1F5F9",
                        ForegroundHex = "#FF64748B",
                        Keywords = new List<string>()
                    }
                }
            };
        }
    }

    public sealed class ExcelExportHistoryItem
    {
        [Newtonsoft.Json.JsonProperty("path")]
        public string Path { get; set; }

        [Newtonsoft.Json.JsonProperty("savedUtc")]
        public DateTime SavedUtc { get; set; }
    }
}