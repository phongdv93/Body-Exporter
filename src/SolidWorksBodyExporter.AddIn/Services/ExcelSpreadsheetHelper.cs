using System;
using System.Globalization;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SolidWorksBodyExporter.AddIn.Models;
using SolidWorksBodyExporter.AddIn.Ui;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// OpenXML helpers: numeric cells (formula-friendly) with decimal separator from
    /// <see cref="CultureInfo.CurrentCulture"/> (user's Windows regional settings).
    /// </summary>
    internal static class ExcelSpreadsheetHelper
    {
        public const uint StyleIndexDefault = 0;
        public const uint StyleIndexHeader = 1;
        public const uint StyleIndexDimension = 2;
        public const uint StyleIndexQuantity = 3;

        private const uint DimensionNumberFormatId = 164;
        private const uint QuantityNumberFormatId = 165;

        public static bool IsNumericColumn(ExportColumn column)
        {
            switch (column)
            {
                case ExportColumn.Length:
                case ExportColumn.Width:
                case ExportColumn.Thickness:
                case ExportColumn.Quantity:
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryGetNumericValue(BodyExportRow row, ExportColumn column, out double value)
        {
            value = 0;
            if (row == null) return false;

            switch (column)
            {
                case ExportColumn.Length:
                    value = Math.Round(row.Length, 2, MidpointRounding.AwayFromZero);
                    return true;
                case ExportColumn.Width:
                    value = Math.Round(row.Width, 2, MidpointRounding.AwayFromZero);
                    return true;
                case ExportColumn.Thickness:
                    value = Math.Round(row.Thickness, 2, MidpointRounding.AwayFromZero);
                    return true;
                case ExportColumn.Quantity:
                    value = row.Quantity;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Excel format code: thousands + decimal per current culture.</summary>
        public static string DimensionNumberFormatCode()
        {
            return CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == ","
                ? "#.##0,00"
                : "#,##0.00";
        }

        public static string QuantityNumberFormatCode()
        {
            return CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == ","
                ? "#.##0"
                : "#,##0";
        }

        public static Stylesheet CreateStylesheet()
        {
            var fonts = new Fonts(
                new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
                new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" }))
            { Count = 2 };

            var fills = new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }))
            { Count = 2 };

            var borders = new Borders(new Border()) { Count = 1 };

            var numberingFormats = new NumberingFormats(
                new NumberingFormat { NumberFormatId = DimensionNumberFormatId, FormatCode = DimensionNumberFormatCode() },
                new NumberingFormat { NumberFormatId = QuantityNumberFormatId, FormatCode = QuantityNumberFormatCode() })
            { Count = 2 };

            var cellStyleFormats = new CellStyleFormats(new CellFormat()) { Count = 1 };

            var cellFormats = new CellFormats(
                new CellFormat { FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 },
                new CellFormat { FontId = 1, FillId = 0, BorderId = 0, FormatId = 0, ApplyFont = true },
                new CellFormat
                {
                    FontId = 0,
                    FillId = 0,
                    BorderId = 0,
                    FormatId = 0,
                    NumberFormatId = DimensionNumberFormatId,
                    ApplyNumberFormat = true
                },
                new CellFormat
                {
                    FontId = 0,
                    FillId = 0,
                    BorderId = 0,
                    FormatId = 0,
                    NumberFormatId = QuantityNumberFormatId,
                    ApplyNumberFormat = true
                })
            { Count = 4 };

            return new Stylesheet(fonts, fills, borders, numberingFormats, cellStyleFormats, cellFormats);
        }

        public static uint NumericStyleIndexFor(ExportColumn column)
        {
            return column == ExportColumn.Quantity ? StyleIndexQuantity : StyleIndexDimension;
        }

        public static void WriteNumericCell(Cell cell, double value, uint styleIndex)
        {
            if (cell == null) return;
            cell.DataType = CellValues.Number;
            cell.InlineString = null;
            cell.CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture));
            cell.StyleIndex = styleIndex;
        }

        public static void WriteTextCell(Cell cell, string text)
        {
            if (cell == null) return;
            cell.DataType = CellValues.String;
            cell.InlineString = null;
            cell.CellValue = new CellValue(text ?? string.Empty);
        }

        /// <summary>
        /// Ensures workbook has numeric cell formats (for template fill). Returns style indices.
        /// </summary>
        public static void EnsureWorkbookNumericStyles(WorkbookPart workbookPart, out uint dimensionStyleIndex, out uint quantityStyleIndex)
        {
            dimensionStyleIndex = StyleIndexDimension;
            quantityStyleIndex = StyleIndexQuantity;

            var stylesPart = workbookPart.WorkbookStylesPart ?? workbookPart.AddNewPart<WorkbookStylesPart>();
            var sheet = stylesPart.Stylesheet ?? new Stylesheet();
            stylesPart.Stylesheet = sheet;

            EnsureNumberingFormat(sheet, DimensionNumberFormatId, DimensionNumberFormatCode());
            EnsureNumberingFormat(sheet, QuantityNumberFormatId, QuantityNumberFormatCode());

            dimensionStyleIndex = EnsureCellFormat(sheet, DimensionNumberFormatId);
            quantityStyleIndex = EnsureCellFormat(sheet, QuantityNumberFormatId);

            sheet.Save();
        }

        private static void EnsureNumberingFormat(Stylesheet sheet, uint formatId, string formatCode)
        {
            if (sheet.NumberingFormats == null)
            {
                sheet.NumberingFormats = new NumberingFormats();
            }

            var existing = sheet.NumberingFormats.Elements<NumberingFormat>()
                .FirstOrDefault(n => n.NumberFormatId != null && n.NumberFormatId.Value == formatId);
            if (existing != null)
            {
                existing.FormatCode = formatCode;
                return;
            }

            sheet.NumberingFormats.Append(new NumberingFormat
            {
                NumberFormatId = formatId,
                FormatCode = formatCode
            });
            sheet.NumberingFormats.Count = (uint)sheet.NumberingFormats.Count();
        }

        private static uint EnsureCellFormat(Stylesheet sheet, uint numberFormatId)
        {
            if (sheet.CellFormats == null)
            {
                sheet.CellFormats = new CellFormats(new CellFormat());
            }

            uint index = 0;
            foreach (var cf in sheet.CellFormats.Elements<CellFormat>())
            {
                if (cf.NumberFormatId != null && cf.NumberFormatId.Value == numberFormatId)
                {
                    return index;
                }

                index++;
            }

            sheet.CellFormats.Append(new CellFormat
            {
                FontId = 0,
                FillId = 0,
                BorderId = 0,
                FormatId = 0,
                NumberFormatId = numberFormatId,
                ApplyNumberFormat = true
            });
            sheet.CellFormats.Count = (uint)sheet.CellFormats.Count();
            return index;
        }
    }
}
