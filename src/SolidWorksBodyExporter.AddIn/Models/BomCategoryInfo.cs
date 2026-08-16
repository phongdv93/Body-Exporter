using System;
using System.Windows.Media;
using SolidWorksBodyExporter.AddIn.Services;

namespace SolidWorksBodyExporter.AddIn.Models
{
    /// <summary>Compatibility helpers — prefer <see cref="BomTypesService"/>.</summary>
    public static class BomCategoryInfo
    {
        public static string ToDisplayName(BomCategory category)
        {
            return BomTypesService.DisplayName(ToTypeId(category));
        }

        public static string ToDisplayName(string typeId)
        {
            return BomTypesService.DisplayName(typeId);
        }

        public static string ToErpSection(BomCategory category)
        {
            return BomTypesService.ErpSection(ToTypeId(category));
        }

        public static string ToErpSection(string typeId)
        {
            return BomTypesService.ErpSection(typeId);
        }

        public static string ToExcelSheetName(BomCategory category)
        {
            return ToDisplayName(category);
        }

        public static string ToExcelSheetName(string typeId)
        {
            return ToDisplayName(typeId);
        }

        public static bool IncludeInExcel(BomCategory category, Services.Api.AppSettings settings = null)
        {
            return IncludeInExcel(ToTypeId(category), settings);
        }

        public static bool IncludeInExcel(string typeId, Services.Api.AppSettings settings = null)
        {
            var id = BomTypesService.NormalizeId(typeId);
            if (id == BomTypeIds.Other && settings != null && settings.ExportOtherCategoryToExcel)
            {
                return true;
            }

            return BomTypesService.IncludeInExcel(typeId);
        }

        public static bool IncludeInErp(BomCategory category, Services.Api.AppSettings settings = null)
        {
            return IncludeInErp(ToTypeId(category), settings);
        }

        public static bool IncludeInErp(string typeId, Services.Api.AppSettings settings = null)
        {
            var id = BomTypesService.NormalizeId(typeId);
            if (id == BomTypeIds.Other && settings != null && settings.ExportOtherCategoryToErp)
            {
                return true;
            }

            return BomTypesService.IncludeInErp(typeId);
        }

        public static Color BackgroundColor(BomCategory category)
        {
            return ((SolidColorBrush)BomTypesService.BackgroundBrush(ToTypeId(category))).Color;
        }

        public static Brush BackgroundBrush(BomCategory category)
        {
            return BomTypesService.BackgroundBrush(ToTypeId(category));
        }

        public static Brush BackgroundBrush(string typeId)
        {
            return BomTypesService.BackgroundBrush(typeId);
        }

        public static Brush ForegroundBrush(BomCategory category)
        {
            return BomTypesService.ForegroundBrush(ToTypeId(category));
        }

        public static Brush ForegroundBrush(string typeId)
        {
            return BomTypesService.ForegroundBrush(typeId);
        }

        public static BomCategory Parse(string value)
        {
            switch (BomTypesService.NormalizeId(value))
            {
                case BomTypeIds.Hardware: return BomCategory.Hardware;
                case BomTypeIds.Packaging: return BomCategory.Packaging;
                case BomTypeIds.Other: return BomCategory.Other;
                default: return BomCategory.Detail;
            }
        }

        public static string ToTypeId(BomCategory category)
        {
            switch (category)
            {
                case BomCategory.Hardware: return BomTypeIds.Hardware;
                case BomCategory.Packaging: return BomTypeIds.Packaging;
                case BomCategory.Other: return BomTypeIds.Other;
                default: return BomTypeIds.Detail;
            }
        }
    }

    /// <summary>Overall part bounding box in mm, ordered Length ≥ Width ≥ Height.</summary>
    public sealed class PartOverallSize
    {
        public double LengthMm { get; set; }

        public double WidthMm { get; set; }

        public double HeightMm { get; set; }

        public bool HasValue => LengthMm > 0 || WidthMm > 0 || HeightMm > 0;

        public string DisplayText
        {
            get
            {
                if (!HasValue)
                {
                    return string.Empty;
                }

                return Format(LengthMm) + "×" + Format(WidthMm) + "×" + Format(HeightMm);
            }
        }

        private static string Format(double mm)
        {
            if (Math.Abs(mm - Math.Round(mm)) < 0.05)
            {
                return Math.Round(mm).ToString("0");
            }

            return mm.ToString("0.###");
        }
    }
}
