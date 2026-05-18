using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    /// Writes the export to an .xlsx file using the raw DocumentFormat.OpenXml SDK. We intentionally
    /// avoid ClosedXML's higher-level API because its column auto-sizing path forces a SixLabors.Fonts
    /// initializer that throws on certain Windows font configurations. The OpenXml SDK is a pure
    /// XML-over-zip writer and never touches the font subsystem.
    /// <para>
    /// When <see cref="ExportColumn.Preview"/> is included in the column order, each row's
    /// <see cref="BodyExportRow.Thumbnail"/> is encoded to PNG and anchored to the cell as an
    /// embedded image via the worksheet's <see cref="DrawingsPart"/>. Rows are sized tall enough
    /// (96 px) to display the 80x80 px image with breathing room.
    /// </para>
    /// </summary>
    public sealed class ExcelExporter
    {
        /// <summary>
        /// Pixel size (square) used for the embedded preview image. Same size in display pixels
        /// is reflected in the row height and column width tweaks below.
        /// </summary>
        private const int PreviewImagePixels = 80;

        /// <summary>
        /// Row height in points when the row hosts an embedded preview. Excel uses points (1 pt
        /// = 1/72 inch) for row height. 75 pt ≈ 100 px which gives the 80 px image a few pixels
        /// of vertical breathing room.
        /// </summary>
        private const double PreviewRowHeightPoints = 75.0;

        public void Export(string filePath, IEnumerable<BodyExportRow> rows, IReadOnlyList<ExportColumn> columnOrder)
        {
            if (columnOrder == null || columnOrder.Count == 0)
            {
                throw new ArgumentException("At least one export column is required.", nameof(columnOrder));
            }

            // Materialise rows so we can iterate multiple times (header pass, data pass, image pass).
            var rowList = new List<BodyExportRow>();
            if (rows != null) rowList.AddRange(rows);

            var hasPreview = false;
            for (var i = 0; i < columnOrder.Count; i++)
            {
                if (columnOrder[i] == ExportColumn.Preview) { hasPreview = true; break; }
            }

            using (var document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = ExcelSpreadsheetHelper.CreateStylesheet();
                stylesPart.Stylesheet.Save();

                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();

                sheetData.Append(BuildHeaderRow(columnOrder));

                uint rowIndex = 2;
                foreach (var row in rowList)
                {
                    sheetData.Append(BuildDataRow(rowIndex, row, columnOrder, hasPreview));
                    rowIndex++;
                }

                worksheetPart.Worksheet = new Worksheet(BuildColumns(columnOrder), sheetData);

                if (hasPreview)
                {
                    EmbedPreviewImages(worksheetPart, rowList, columnOrder);
                }

                worksheetPart.Worksheet.Save();

                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1U,
                    Name = "Bodies"
                });

                workbookPart.Workbook.Save();
            }
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
                // Preview column is sized to fit the embedded 80px image plus a little margin.
                // Excel column-width units approximate "monospace characters in default font",
                // and 12 units lands close to 84 px on Calibri 11.
                case ExportColumn.Preview:    return 13;
                case ExportColumn.BodyName:   return 28;
                case ExportColumn.Length:     return 12;
                case ExportColumn.Width:      return 12;
                case ExportColumn.Thickness:  return 12;
                case ExportColumn.Quantity:   return 8;
                case ExportColumn.Appearance: return 24;
                default:                      return 14;
            }
        }

        private static Row BuildHeaderRow(IReadOnlyList<ExportColumn> order)
        {
            var row = new Row { RowIndex = 1U };
            for (var i = 0; i < order.Count; i++)
            {
                row.Append(StringCell(CellReference(i + 1, 1), BodyExportWindow.ExportColumnHeader(order[i]), styleIndex: 1));
            }
            return row;
        }

        private static Row BuildDataRow(uint rowIndex, BodyExportRow row, IReadOnlyList<ExportColumn> order, bool hasPreview)
        {
            var r = new Row { RowIndex = rowIndex };
            if (hasPreview)
            {
                // Bump row height to fit the 80px preview. Excel applies the larger of the
                // explicit height and the auto-fit height, so without CustomHeight=true the
                // user's Excel could shrink the row back to default after open/save cycles.
                r.Height = PreviewRowHeightPoints;
                r.CustomHeight = true;
            }

            for (var i = 0; i < order.Count; i++)
            {
                var column = order[i];
                var reference = CellReference(i + 1, rowIndex);

                if (column == ExportColumn.Preview)
                {
                    // The cell itself is empty; the actual visual is the embedded image
                    // anchored via the DrawingsPart in EmbedPreviewImages.
                    r.Append(StringCell(reference, string.Empty));
                }
                else if (ExcelSpreadsheetHelper.TryGetNumericValue(row, column, out var num))
                {
                    r.Append(NumberCell(reference, num, ExcelSpreadsheetHelper.NumericStyleIndexFor(column)));
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

        private static Cell NumberCell(string reference, double value, uint styleIndex)
        {
            var cell = new Cell { CellReference = reference };
            ExcelSpreadsheetHelper.WriteNumericCell(cell, value, styleIndex);
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

        /// <summary>
        /// Anchors a PNG image for each row's <see cref="BodyExportRow.Thumbnail"/> into the
        /// Preview column. Strategy:
        /// <list type="number">
        ///   <item>Create a <see cref="DrawingsPart"/> on the worksheet and an empty
        ///         <c>WorksheetDrawing</c> root with the spreadsheet drawing + main drawing
        ///         namespaces.</item>
        ///   <item>For every data row, encode the thumbnail to PNG bytes (skipping rows whose
        ///         thumbnail failed to render), add an <c>ImagePart</c>, and anchor it with a
        ///         <c>OneCellAnchor</c> tied to the row + column of the Preview cell.</item>
        ///   <item>Use EMUs for the anchor extent (914400 EMU per inch). The 80px image at
        ///         96 DPI maps to 80/96 inch = 762000 EMU.</item>
        /// </list>
        /// </summary>
        private static void EmbedPreviewImages(
            WorksheetPart worksheetPart,
            IReadOnlyList<BodyExportRow> rows,
            IReadOnlyList<ExportColumn> columnOrder)
        {
            var previewColumnIndex = -1;
            for (var i = 0; i < columnOrder.Count; i++)
            {
                if (columnOrder[i] == ExportColumn.Preview) { previewColumnIndex = i; break; }
            }
            if (previewColumnIndex < 0) return;

            var drawingsPart = worksheetPart.AddNewPart<DrawingsPart>();
            drawingsPart.WorksheetDrawing = new Xdr.WorksheetDrawing();
            // The two namespace prefixes must be declared on the WorksheetDrawing root so the
            // <xdr:..> and <a:..> elements below resolve correctly when Excel opens the file.
            drawingsPart.WorksheetDrawing.AddNamespaceDeclaration("xdr",
                "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing");
            drawingsPart.WorksheetDrawing.AddNamespaceDeclaration("a",
                "http://schemas.openxmlformats.org/drawingml/2006/main");

            // Reference the drawing part from the worksheet so Excel knows to load it.
            worksheetPart.Worksheet.Append(new Drawing { Id = worksheetPart.GetIdOfPart(drawingsPart) });

            // 80 px at 96 DPI = 762000 EMU. Use slightly less than the row height so the image
            // sits centred with ~10 px of vertical padding inside the row.
            const long imageExtentEmu = 762000L; // 80 px

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

                // Excel row index for the i-th data row is i + 2 (header is row 1, data starts
                // at row 2). The cell anchor uses 0-based indices.
                var excelRowZeroBased = i + 1;
                var excelColumnZeroBased = previewColumnIndex;

                var anchor = new Xdr.OneCellAnchor(
                    new Xdr.FromMarker(
                        new Xdr.ColumnId(excelColumnZeroBased.ToString(CultureInfo.InvariantCulture)),
                        // Small EMU offsets so the image sits 10 px in from the top-left of the
                        // cell instead of crammed against the cell border.
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

        /// <summary>
        /// Encodes a WPF <see cref="System.Windows.Media.ImageSource"/> into PNG bytes. Returns
        /// <c>null</c> when the input is not a <see cref="BitmapSource"/> or when encoding
        /// throws - either failure should silently skip the image instead of aborting the
        /// whole export.
        /// </summary>
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
