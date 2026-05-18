using System;
using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SolidWorksBodyExporter.AddIn.Models;
using SolidWorksBodyExporter.AddIn.Ui;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// OpenXML helpers: formula-friendly numeric cells; template fill preserves cell styles.
    /// </summary>
    internal static class ExcelSpreadsheetHelper
    {
        public const uint StyleIndexDefault = 0;
        public const uint StyleIndexHeader = 1;

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
                    value = NormalizeNumeric(row.Length);
                    return true;
                case ExportColumn.Width:
                    value = NormalizeNumeric(row.Width);
                    return true;
                case ExportColumn.Thickness:
                    value = NormalizeNumeric(row.Thickness);
                    return true;
                case ExportColumn.Quantity:
                    value = NormalizeNumeric(row.Quantity);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>One decimal max; whole numbers stored without a fractional part.</summary>
        public static double NormalizeNumeric(double raw)
        {
            var rounded = Math.Round(raw, 1, MidpointRounding.AwayFromZero);
            var whole = Math.Round(rounded, 0, MidpointRounding.AwayFromZero);
            if (Math.Abs(rounded - whole) < 0.0001)
            {
                return whole;
            }

            return rounded;
        }

        public static string FormatNumericCellValue(double value)
        {
            value = NormalizeNumeric(value);
            var whole = Math.Round(value, 0, MidpointRounding.AwayFromZero);
            if (Math.Abs(value - whole) < 0.0001)
            {
                return whole.ToString(CultureInfo.InvariantCulture);
            }

            return value.ToString("0.0", CultureInfo.InvariantCulture);
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
            var cellStyleFormats = new CellStyleFormats(new CellFormat()) { Count = 1 };
            var cellFormats = new CellFormats(
                new CellFormat { FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 },
                new CellFormat { FontId = 1, FillId = 0, BorderId = 0, FormatId = 0, ApplyFont = true })
            { Count = 2 };

            var sheet = new Stylesheet();
            sheet.Append(fonts);
            sheet.Append(fills);
            sheet.Append(borders);
            sheet.Append(cellStyleFormats);
            sheet.Append(cellFormats);
            return sheet;
        }

        /// <summary>Sets numeric value only; does not change StyleIndex (template borders/formatting).</summary>
        public static void WriteNumericCellPreservingStyle(Cell cell, double value)
        {
            if (cell == null) return;
            cell.DataType = CellValues.Number;
            cell.InlineString = null;
            cell.CellValue = new CellValue(FormatNumericCellValue(value));
        }

        /// <summary>Sets text only; does not change StyleIndex.</summary>
        public static void WriteTextCellPreservingStyle(Cell cell, string text)
        {
            if (cell == null) return;
            cell.DataType = CellValues.String;
            cell.InlineString = null;
            cell.CellValue = new CellValue(text ?? string.Empty);
        }

        /// <summary>Clears placeholder text for preview image; does not change StyleIndex.</summary>
        public static void ClearCellValuePreservingStyle(Cell cell)
        {
            if (cell == null) return;
            cell.InlineString = null;
            cell.CellValue = new CellValue(string.Empty);
        }

        /// <summary>
        /// Forces Excel to recalculate all formulas when the file is opened (fixes stale cached
        /// values after we fill template input cells).
        /// </summary>
        public static void EnsureFullRecalculationOnLoad(WorkbookPart workbookPart)
        {
            if (workbookPart?.Workbook == null) return;

            var calc = workbookPart.Workbook.CalculationProperties;
            if (calc == null)
            {
                calc = new CalculationProperties();
                workbookPart.Workbook.CalculationProperties = calc;
            }

            calc.FullCalculationOnLoad = true;
            calc.CalculationOnSave = true;
            calc.CalculationMode = CalculateModeValues.Auto;
            calc.ForceFullCalculation = true;
        }

        /// <summary>Clear cached formula results so Excel recomputes on open.</summary>
        public static void InvalidateFormulaCaches(SheetData sheetData)
        {
            if (sheetData == null) return;

            foreach (var row in sheetData.Elements<Row>())
            {
                foreach (var cell in row.Elements<Cell>())
                {
                    if (cell.CellFormula == null) continue;
                    cell.CellValue = null;
                }
            }
        }
    }
}
