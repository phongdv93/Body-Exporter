using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SolidWorksBodyExporter.AddIn.Models;
using SolidWorksBodyExporter.AddIn.Ui;
using A = DocumentFormat.OpenXml.Drawing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace SolidWorksBodyExporter.AddIn.Services
{
    /// <summary>
    /// Writes the export to an .xlsx file using the raw DocumentFormat.OpenXml SDK.
    /// New workbooks split rows by BOM type into sheets (Detail always present; others when non-empty).
    /// </summary>
    public sealed class ExcelExporter
    {
        private const int PreviewImagePixels = 80;
        private const double PreviewRowHeightPoints = 75.0;

        public void Export(
            string filePath,
            IEnumerable<BodyExportRow> rows,
            IReadOnlyList<ExportColumn> columnOrder,
            PartOverallSize overallSize = null)
        {
            if (columnOrder == null || columnOrder.Count == 0)
            {
                throw new ArgumentException("At least one export column is required.", nameof(columnOrder));
            }

            var rowList = new List<BodyExportRow>();
            if (rows != null)
            {
                rowList.AddRange(rows.Where(r => r != null && r.Status != BodyRowStatus.Deleted));
            }

            var hasPreview = columnOrder.Any(c => c == ExportColumn.Preview);
            var overall = overallSize ?? new PartOverallSize();
            var types = BomTypesService.Load().Types.OrderBy(t => t.SortOrder).ToList();

            using (var document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = ExcelSpreadsheetHelper.CreateStylesheet();
                stylesPart.Stylesheet.Save();

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                uint sheetId = 1;
                var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var writeProductSize = true;

                foreach (var type in types)
                {
                    var typeId = BomTypesService.NormalizeId(type.Id);
                    var sheetRows = rowList
                        .Where(r => string.Equals(
                            BomTypesService.NormalizeId(r.TypeId),
                            typeId,
                            StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (sheetRows.Count == 0
                        && !string.Equals(typeId, BomTypeIds.Detail, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AddTypeSheet(
                        workbookPart,
                        sheets,
                        ref sheetId,
                        type,
                        sheetRows,
                        columnOrder,
                        hasPreview,
                        overall,
                        writeProductSize,
                        usedSheetNames);
                    writeProductSize = false;
                }

                var knownIds = new HashSet<string>(
                    types.Select(t => BomTypesService.NormalizeId(t.Id)),
                    StringComparer.OrdinalIgnoreCase);
                var orphanRows = rowList
                    .Where(r => !knownIds.Contains(BomTypesService.NormalizeId(r.TypeId)))
                    .ToList();
                if (orphanRows.Count > 0)
                {
                    AddTypeSheet(
                        workbookPart,
                        sheets,
                        ref sheetId,
                        new BomTypeDefinition { Id = "extra", NameEn = "Extra", NameVi = "Extra" },
                        orphanRows,
                        columnOrder,
                        hasPreview,
                        overall,
                        writeProductSize,
                        usedSheetNames);
                }

                ExcelSpreadsheetHelper.EnsureFullRecalculationOnLoad(workbookPart);
                workbookPart.Workbook.Save();
            }
        }

        private static void AddTypeSheet(
            WorkbookPart workbookPart,
            Sheets sheets,
            ref uint sheetId,
            BomTypeDefinition type,
            IList<BodyExportRow> sheetRows,
            IReadOnlyList<ExportColumn> columnOrder,
            bool hasPreview,
            PartOverallSize overall,
            bool writeProductSize,
            HashSet<string> usedSheetNames)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            uint rowIndex = 1;
            if (writeProductSize && overall != null && overall.HasValue)
            {
                var info = new Row { RowIndex = rowIndex };
                info.Append(StringCell(CellReference(1, rowIndex), "Product size (mm)"));
                info.Append(StringCell(CellReference(2, rowIndex), overall.DisplayText));
                sheetData.Append(info);
                rowIndex++;
            }

            sheetData.Append(BuildHeaderRow(columnOrder, rowIndex));
            rowIndex++;

            var dataStartIndex = rowIndex;
            foreach (var row in sheetRows)
            {
                sheetData.Append(BuildDataRow(rowIndex, row, columnOrder, hasPreview));
                rowIndex++;
            }

            worksheetPart.Worksheet = new Worksheet(BuildColumns(columnOrder), sheetData);

            if (hasPreview && sheetRows.Count > 0)
            {
                EmbedPreviewImages(
                    worksheetPart,
                    (IReadOnlyList<BodyExportRow>)sheetRows.ToList(),
                    columnOrder,
                    dataStartIndex);
            }

            worksheetPart.Worksheet.Save();

            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId,
                Name = UniqueSheetName(BomCategoryInfo.ToExcelSheetName(type.Id), usedSheetNames)
            });
            sheetId++;
        }

        private static string UniqueSheetName(string preferred, HashSet<string> used)
        {
            var baseName = SanitizeSheetName(preferred);
            var name = baseName;
            var n = 2;
            while (!used.Add(name))
            {
                var suffix = " (" + n + ")";
                var max = Math.Max(1, 31 - suffix.Length);
                name = (baseName.Length > max ? baseName.Substring(0, max) : baseName) + suffix;
                n++;
            }

            return name;
        }

        private static string SanitizeSheetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Sheet";
            }

            foreach (var c in new[] { ':', '\\', '/', '?', '*', '[', ']' })
            {
                name = name.Replace(c, '_');
            }

            name = name.Trim();
            if (name.Length > 31)
            {
                name = name.Substring(0, 31);
            }

            return string.IsNullOrWhiteSpace(name) ? "Sheet" : name;
        }

        private static Columns BuildColumns(IReadOnlyList<ExportColumn> order)
        {
            var columns = new Columns();
            for (var i = 0; i < order.Count; i++)
            {
                columns.Append(new Column
                {
                    Min = (uint)(i + 1),
                    Max = (uint)(i + 1),
                    Width = WidthFor(order[i]),
                    CustomWidth = true
                });
            }
            return columns;
        }

        private static double WidthFor(ExportColumn column)
        {
            switch (column)
            {
                case ExportColumn.Preview:    return 13;
                case ExportColumn.BodyName:   return 28;
                case ExportColumn.Category:   return 12;
                case ExportColumn.Length:     return 12;
                case ExportColumn.Width:      return 12;
                case ExportColumn.Thickness:  return 12;
                case ExportColumn.Quantity:   return 8;
                case ExportColumn.Appearance: return 24;
                default:                      return 14;
            }
        }

        private static Row BuildHeaderRow(IReadOnlyList<ExportColumn> order, uint rowIndex)
        {
            var row = new Row { RowIndex = rowIndex };
            for (var i = 0; i < order.Count; i++)
            {
                row.Append(StringCell(CellReference(i + 1, rowIndex), BodyExportWindow.ExportColumnHeader(order[i]), styleIndex: 1));
            }
            return row;
        }

        private static Row BuildDataRow(uint rowIndex, BodyExportRow row, IReadOnlyList<ExportColumn> order, bool hasPreview)
        {
            var r = new Row { RowIndex = rowIndex };
            if (hasPreview)
            {
                r.Height = PreviewRowHeightPoints;
                r.CustomHeight = true;
            }

            for (var i = 0; i < order.Count; i++)
            {
                var column = order[i];
                var reference = CellReference(i + 1, rowIndex);

                if (column == ExportColumn.Preview)
                {
                    r.Append(StringCell(reference, string.Empty));
                }
                else if (ExcelSpreadsheetHelper.TryGetNumericValue(row, column, out var num))
                {
                    r.Append(NumberCell(reference, num));
                }
                else
                {
                    r.Append(StringCell(reference, BodyExportWindow.ExportColumnValue(row, column)));
                }
            }
            return r;
        }

        private static Cell StringCell(string reference, string text, uint? styleIndex = null)
        {
            var cell = new Cell
            {
                CellReference = reference,
                DataType = CellValues.String,
                CellValue = new CellValue(text ?? string.Empty)
            };

            if (styleIndex.HasValue)
            {
                cell.StyleIndex = styleIndex.Value;
            }

            return cell;
        }

        private static Cell NumberCell(string reference, double value)
        {
            var cell = new Cell { CellReference = reference };
            ExcelSpreadsheetHelper.WriteNumericCellPreservingStyle(cell, value);
            return cell;
        }

        private static string CellReference(int column, uint row)
        {
            var columnName = string.Empty;
            while (column > 0)
            {
                var remainder = (column - 1) % 26;
                columnName = (char)('A' + remainder) + columnName;
                column = (column - 1) / 26;
            }
            return columnName + row.ToString(CultureInfo.InvariantCulture);
        }

        private static void EmbedPreviewImages(
            WorksheetPart worksheetPart,
            IReadOnlyList<BodyExportRow> rows,
            IReadOnlyList<ExportColumn> columnOrder,
            uint dataStartRowIndex)
        {
            var previewColumnIndex = -1;
            for (var i = 0; i < columnOrder.Count; i++)
            {
                if (columnOrder[i] == ExportColumn.Preview) { previewColumnIndex = i; break; }
            }
            if (previewColumnIndex < 0) return;

            var drawingsPart = worksheetPart.AddNewPart<DrawingsPart>();
            drawingsPart.WorksheetDrawing = new Xdr.WorksheetDrawing();
            drawingsPart.WorksheetDrawing.AddNamespaceDeclaration("xdr",
                "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing");
            drawingsPart.WorksheetDrawing.AddNamespaceDeclaration("a",
                "http://schemas.openxmlformats.org/drawingml/2006/main");

            worksheetPart.Worksheet.Append(new Drawing { Id = worksheetPart.GetIdOfPart(drawingsPart) });

            const long imageExtentEmu = 762000L;

            uint pictureNumber = 1;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var pngBytes = TryEncodePng(row?.Thumbnail);
                if (pngBytes == null) continue;

                var imagePart = drawingsPart.AddImagePart(ImagePartType.Png);
                using (var ms = new MemoryStream(pngBytes))
                {
                    imagePart.FeedData(ms);
                }
                var relationshipId = drawingsPart.GetIdOfPart(imagePart);

                var excelRowZeroBased = (int)dataStartRowIndex - 1 + i;
                var excelColumnZeroBased = previewColumnIndex;

                var anchor = new Xdr.OneCellAnchor(
                    new Xdr.FromMarker(
                        new Xdr.ColumnId(excelColumnZeroBased.ToString(CultureInfo.InvariantCulture)),
                        new Xdr.ColumnOffset("100000"),
                        new Xdr.RowId(excelRowZeroBased.ToString(CultureInfo.InvariantCulture)),
                        new Xdr.RowOffset("100000")),
                    new Xdr.Extent { Cx = imageExtentEmu, Cy = imageExtentEmu },
                    new Xdr.Picture(
                        new Xdr.NonVisualPictureProperties(
                            new Xdr.NonVisualDrawingProperties { Id = pictureNumber, Name = "Preview " + pictureNumber },
                            new Xdr.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true })),
                        new Xdr.BlipFill(
                            new A.Blip { Embed = relationshipId, CompressionState = A.BlipCompressionValues.Print },
                            new A.Stretch(new A.FillRectangle())),
                        new Xdr.ShapeProperties(
                            new A.Transform2D(
                                new A.Offset { X = 0L, Y = 0L },
                                new A.Extents { Cx = imageExtentEmu, Cy = imageExtentEmu }),
                            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })),
                    new Xdr.ClientData());

                drawingsPart.WorksheetDrawing.Append(anchor);
                pictureNumber++;
            }
        }

        private static byte[] TryEncodePng(System.Windows.Media.ImageSource source)
        {
            try
            {
                if (!(source is BitmapSource bitmap)) return null;
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("ExcelExporter: PNG encoding failed - " + ex.Message);
                return null;
            }
        }
    }
}
