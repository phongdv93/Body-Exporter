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
                    value = Math.Round(row.Length, 1, MidpointRounding.AwayFromZero);
                    return true;
                case ExportColumn.Width:
                    value = Math.Round(row.Width, 1, MidpointRounding.AwayFromZero);
                    return true;
                case ExportColumn.Thickness:
                    value = Math.Round(row.Thickness, 1, MidpointRounding.AwayFromZero);
                    return true;
                case ExportColumn.Quantity:
                    value = row.Quantity;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Excel format: integer values without decimals; fractional values with one optional decimal
        /// (e.g. 42 and 42,5), decimal separator from <see cref="CultureInfo.CurrentCulture"/>.
        /// </summary>
        public static string DimensionNumberFormatCode()
        {
            return CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == ","
                ? "#.##0,#"
                : "#,##0.#";
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

            // Schema order: numFmts, fonts, fills, borders, cellStyleXfs, cellXfs.
            var sheet = new Stylesheet();
            sheet.Append(numberingFormats);
            sheet.Append(fonts);
            sheet.Append(fills);
            sheet.Append(borders);
            sheet.Append(cellStyleFormats);
            sheet.Append(cellFormats);
            return sheet;
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
            var stylesPart = workbookPart.WorkbookStylesPart;
            if (stylesPart == null)
            {
                stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = CreateStylesheet();
                dimensionStyleIndex = StyleIndexDimension;
                quantityStyleIndex = StyleIndexQuantity;
                return;
            }

            var sheet = stylesPart.Stylesheet;
            if (sheet == null)
            {
                stylesPart.Stylesheet = CreateStylesheet();
                dimensionStyleIndex = StyleIndexDimension;
                quantityStyleIndex = StyleIndexQuantity;
                return;
            }

            // Edit the existing stylesheet in place. Re-assigning stylesPart.Stylesheet to the
            // same root element throws "already been associated with another OpenXmlPart".
            EnsureMinimalStyleChildren(sheet);
            EnsureNumberingFormat(sheet, DimensionNumberFormatId, DimensionNumberFormatCode());
            EnsureNumberingFormat(sheet, QuantityNumberFormatId, QuantityNumberFormatCode());
            dimensionStyleIndex = EnsureCellFormat(sheet, DimensionNumberFormatId);
            quantityStyleIndex = EnsureCellFormat(sheet, QuantityNumberFormatId);
        }

        private static void EnsureMinimalStyleChildren(Stylesheet sheet)
        {
            if (sheet.Fonts == null)
            {
                sheet.Fonts = new Fonts(new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" })) { Count = 1 };
            }

            if (sheet.Fills == null)
            {
                sheet.Fills = new Fills(
                    new Fill(new PatternFill { PatternType = PatternValues.None }),
                    new Fill(new PatternFill { PatternType = PatternValues.Gray125 }))
                { Count = 2 };
            }

            if (sheet.Borders == null)
            {
                sheet.Borders = new Borders(new Border()) { Count = 1 };
            }

            if (sheet.CellStyleFormats == null)
            {
                sheet.CellStyleFormats = new CellStyleFormats(new CellFormat()) { Count = 1 };
            }
        }

        private static void EnsureNumberingFormat(Stylesheet sheet, uint formatId, string formatCode)
        {
            if (sheet.NumberingFormats == null)
            {
                var numberingFormats = new NumberingFormats();
                OpenXmlElement insertBefore = sheet.Fonts;
                if (insertBefore == null) insertBefore = sheet.Fills;
                if (insertBefore == null) insertBefore = sheet.Borders;
                if (insertBefore == null) insertBefore = sheet.CellStyleFormats;
                if (insertBefore == null) insertBefore = sheet.CellFormats;
                if (insertBefore != null)
                {
                    sheet.InsertBefore(numberingFormats, insertBefore);
                }
                else
                {
                    sheet.NumberingFormats = numberingFormats;
                }
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
