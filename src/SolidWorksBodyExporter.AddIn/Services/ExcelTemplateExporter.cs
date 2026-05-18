using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// Template-driven Excel export. Users craft their own .xlsx workbook (company logo,
    /// header, footer, signature box, etc.) and tag the cells that should receive data with
    /// a <c>{{Placeholder}}</c> marker. We then:
    /// <list type="number">
    ///   <item>Copy the template to the output path so all of the user's styling, merges,
    ///         column widths and embedded artwork are preserved exactly.</item>
    ///   <item>Walk every sheet looking for placeholder strings of the form
    ///         <c>{{KEY}}</c> where <c>KEY</c> maps to an <see cref="ExportColumn"/>.</item>
    ///   <item>Treat the row that contains placeholders as the "fill row". The first body
    ///         replaces the placeholder text in the fill row; subsequent bodies are written
    ///         to the rows below, vertically aligned with the placeholder columns. The fill
    ///         row's height is cloned down so the overflow rows match the template's
    ///         vertical rhythm without the user having to pre-create N empty rows.</item>
    ///   <item>The <c>{{Preview}}</c> placeholder additionally anchors a PNG image of the
    ///         body thumbnail into the matching cell using the same drawings pipeline as
    ///         <see cref="ExcelExporter"/>.</item>
    /// </list>
    /// <para>
    /// This is the v0.7.x replacement for the v0.5.x "user picks a dashed-red-border
    /// region" idea. Placeholder strings are an order of magnitude more robust to parse
    /// out of an .xlsx than border styles, give users a visible cue in Excel that "this
    /// cell will be filled", and allow precise per-column placement without forcing the
    /// addin to guess column boundaries from a visual border.
    /// </para>
    /// </summary>
    public sealed class ExcelTemplateExporter
    {
        /// <summary>
        /// Regex used to detect a placeholder anywhere inside a cell's text value. The
        /// content is captured (case-insensitive lookup) and trimmed by the consumer so
        /// users can type <c>{{ Body Name }}</c> with extra spaces and still match.
        /// </summary>
        private static readonly Regex PlaceholderRegex = new Regex(
            @"\{\{\s*(?<key>[^{}]+?)\s*\}\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Lookup of placeholder names the user may type into a template cell. Multiple
        /// aliases per <see cref="ExportColumn"/> let users pick whichever wording reads
        /// best in their language (e.g. "Body Name", "Part Name", "Tên chi tiết").
        /// Comparison is case-insensitive; whitespace is normalised to single space
        /// before lookup so "Body Name" and "body  name" both hit.
        /// </summary>
        private static readonly Dictionary<string, ExportColumn> AliasMap = BuildAliasMap();

        private static Dictionary<string, ExportColumn> BuildAliasMap()
        {
            var map = new Dictionary<string, ExportColumn>(StringComparer.OrdinalIgnoreCase);
            void Add(ExportColumn col, params string[] aliases)
            {
                foreach (var alias in aliases) map[alias] = col;
            }
            Add(ExportColumn.Preview,    "Preview", "Thumbnail", "Image", "Anh", "Hinh");
            Add(ExportColumn.BodyName,   "Body Name", "BodyName", "Name", "Part Name", "Ten chi tiet", "Ten");
            Add(ExportColumn.Length,     "Length", "Len", "Dai", "Chieu dai");
            Add(ExportColumn.Width,      "Width", "Rong", "Chieu rong");
            Add(ExportColumn.Thickness,  "Thickness", "Thick", "Day", "Chieu day");
            Add(ExportColumn.Quantity,   "Quantity", "Qty", "Count", "So luong", "SL");
            Add(ExportColumn.Appearance, "Appearance", "Color", "Finish", "Mau sac", "Mau");
            return map;
        }

        /// <summary>
        /// Resolves a placeholder key (the text between <c>{{</c> and <c>}}</c>) to an
        /// <see cref="ExportColumn"/>, or returns <c>false</c> if the key is not recognised.
        /// Whitespace is normalised to a single space so users can use multi-word aliases
        /// without worrying about extra spaces.
        /// </summary>
        private static bool TryResolvePlaceholder(string key, out ExportColumn column)
        {
            column = default;
            if (string.IsNullOrWhiteSpace(key)) return false;
            var normalised = Regex.Replace(key.Trim(), @"\s+", " ");
            return AliasMap.TryGetValue(normalised, out column);
        }

        /// <summary>
        /// Entry point used by the UI. Loads the template, fills in the rows, writes the
        /// resulting workbook to <paramref name="outputPath"/>. Returns a small status
        /// payload the caller surfaces via toast: how many bodies were written, which
        /// placeholders were resolved, and which (if any) the user typed but we did not
        /// recognise so the user can correct their template.
        /// </summary>
        public TemplateExportResult Export(string templatePath, string outputPath, IEnumerable<BodyExportRow> rows)
        {
            if (string.IsNullOrWhiteSpace(templatePath)) throw new ArgumentException("templatePath required", nameof(templatePath));
            if (!File.Exists(templatePath))             throw new FileNotFoundException("Template file not found", templatePath);
            if (string.IsNullOrWhiteSpace(outputPath))   throw new ArgumentException("outputPath required",   nameof(outputPath));

            // Materialise rows so we can iterate multiple times (count, write, embed images).
            var rowList = new List<BodyExportRow>(rows ?? Enumerable.Empty<BodyExportRow>());

            // Copy template to output first so the user's styling/merges/columns/embedded
            // artwork survive untouched. We then edit the output in place.
            File.Copy(templatePath, outputPath, overwrite: true);

            var result = new TemplateExportResult { RowsWritten = 0, UnknownPlaceholders = new List<string>() };

            using (var doc = SpreadsheetDocument.Open(outputPath, isEditable: true))
            {
                var workbookPart = doc.WorkbookPart;
                if (workbookPart == null)
                    throw new InvalidOperationException("Template workbook has no WorkbookPart.");

                var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;

                foreach (var wsPart in workbookPart.WorksheetParts)
                {
                    var sheetData = wsPart.Worksheet.GetFirstChild<SheetData>();
                    if (sheetData == null) continue;

                    var placeholders = FindPlaceholdersInSheet(sheetData, sharedStringTable, result.UnknownPlaceholders);
                    if (placeholders.Count == 0) continue;

                    // Only handle the first sheet that contains placeholders. A workbook
                    // with placeholders on multiple sheets is unusual; supporting it would
                    // also require ambiguous decisions about whether bodies replicate to
                    // every sheet or split across them. We keep behaviour predictable:
                    // first hit wins.
                    FillSheet(wsPart, sheetData, placeholders, rowList);
                    ExcelSpreadsheetHelper.InvalidateFormulaCaches(sheetData);
                    result.RowsWritten = rowList.Count;
                    wsPart.Worksheet.Save();
                    break;
                }

                ExcelSpreadsheetHelper.EnsureFullRecalculationOnLoad(workbookPart);
                workbookPart.Workbook.Save();
            }

            return result;
        }

        /// <summary>
        /// Locates every cell whose text contains a <c>{{Placeholder}}</c> token and maps
        /// it to the resolved <see cref="ExportColumn"/>. Returns the placeholders sorted
        /// by row, then column - the fill row is the row of the first hit.
        /// </summary>
        private static List<PlaceholderLocation> FindPlaceholdersInSheet(
            SheetData sheetData,
            SharedStringTable sharedStringTable,
            List<string> unknownPlaceholders)
        {
            var hits = new List<PlaceholderLocation>();
            foreach (var row in sheetData.Elements<Row>())
            {
                foreach (var cell in row.Elements<Cell>())
                {
                    var text = ReadCellText(cell, sharedStringTable);
                    if (string.IsNullOrEmpty(text)) continue;

                    foreach (Match m in PlaceholderRegex.Matches(text))
                    {
                        var key = m.Groups["key"].Value;
                        if (!TryResolvePlaceholder(key, out var column))
                        {
                            if (!unknownPlaceholders.Contains(key, StringComparer.OrdinalIgnoreCase))
                            {
                                unknownPlaceholders.Add(key);
                            }
                            continue;
                        }

                        var reference = cell.CellReference?.Value;
                        if (string.IsNullOrEmpty(reference)) continue;

                        ParseCellRef(reference, out var col, out var rowNumber);
                        hits.Add(new PlaceholderLocation
                        {
                            Column = column,
                            CellRef = reference,
                            RowNumber = rowNumber,
                            ColumnLetters = col,
                            ColumnIndex0 = ColumnLettersToIndex(col),
                            OriginalText = text,
                            PlaceholderToken = m.Value
                        });
                    }
                }
            }
            return hits
                .OrderBy(p => p.RowNumber)
                .ThenBy(p => p.ColumnIndex0)
                .ToList();
        }

        /// <summary>
        /// Writes one body per row, starting at the placeholder row and continuing
        /// downward. The fill row's height (if customised) is cloned to all overflow
        /// rows so the template's vertical rhythm survives bodies count overflows.
        /// </summary>
        private static void FillSheet(
            WorksheetPart wsPart,
            SheetData sheetData,
            List<PlaceholderLocation> placeholders,
            IReadOnlyList<BodyExportRow> rows)
        {
            if (placeholders.Count == 0 || rows.Count == 0) return;

            // The fill row is the row of the first placeholder. Bodies after the first
            // overflow into rows N+1, N+2, ... immediately below. If those rows already
            // exist in the template they are reused (and their styling preserved); if not
            // they are appended fresh.
            var fillRowNumber = (uint)placeholders[0].RowNumber;
            var fillRow = sheetData
                .Elements<Row>()
                .FirstOrDefault(r => r.RowIndex != null && r.RowIndex.Value == fillRowNumber);

            // Snapshot fill-row height so overflow rows can match. fillRow.Height is
            // double? - a null value means Excel will use its default.
            var fillRowHeight = fillRow?.Height?.Value;
            var fillRowHasHeight = fillRow?.CustomHeight?.Value == true && fillRowHeight.HasValue;

            // Lookup of (rowNumber -> Row) so we can iterate by position rather than
            // sheetData.Elements order, which is not always strictly sorted.
            var rowByNumber = sheetData.Elements<Row>()
                .Where(r => r.RowIndex != null)
                .ToDictionary(r => r.RowIndex.Value, r => r);

            uint nextPictureNumber = 1;
            DrawingsPart drawingsPart = null;

            for (var i = 0; i < rows.Count; i++)
            {
                var body = rows[i];
                var rowNumber = fillRowNumber + (uint)i;
                if (!rowByNumber.TryGetValue(rowNumber, out var targetRow))
                {
                    targetRow = new Row { RowIndex = rowNumber };
                    InsertRowInOrder(sheetData, targetRow);
                    rowByNumber[rowNumber] = targetRow;
                }

                if (fillRowHasHeight && i > 0)
                {
                    // Overflow rows inherit the fill row's height so the template stays
                    // visually consistent.
                    targetRow.Height = fillRowHeight;
                    targetRow.CustomHeight = true;
                }

                foreach (var placeholder in placeholders)
                {
                    var targetCellRef = placeholder.ColumnLetters + rowNumber.ToString(CultureInfo.InvariantCulture);
                    var targetCell = FindOrCreateCell(targetRow, targetCellRef);

                    if (placeholder.Column == ExportColumn.Preview)
                    {
                        ExcelSpreadsheetHelper.ClearCellValuePreservingStyle(targetCell);

                        var pngBytes = TryEncodePng(body?.Thumbnail);
                        if (pngBytes == null) continue;

                        drawingsPart = drawingsPart ?? EnsureDrawingsPart(wsPart);
                        AnchorImageToCell(drawingsPart, placeholder.ColumnIndex0, (int)rowNumber - 1, pngBytes, nextPictureNumber);
                        nextPictureNumber++;
                    }
                    else if (ExcelSpreadsheetHelper.TryGetNumericValue(body, placeholder.Column, out var num))
                    {
                        ExcelSpreadsheetHelper.WriteNumericCellPreservingStyle(targetCell, num);
                    }
                    else
                    {
                        ExcelSpreadsheetHelper.WriteTextCellPreservingStyle(
                            targetCell,
                            BodyExportWindow.ExportColumnValue(body, placeholder.Column));
                    }
                }
            }
        }

        /// <summary>
        /// Creates the drawing part once per sheet and reuses it for every subsequent
        /// image anchor. Excel requires a single <c>WorksheetDrawing</c> root per worksheet,
        /// so we ensure-and-cache here.
        /// </summary>
        private static DrawingsPart EnsureDrawingsPart(WorksheetPart wsPart)
        {
            var existing = wsPart.DrawingsPart;
            if (existing != null) return existing;

            var newPart = wsPart.AddNewPart<DrawingsPart>();
            newPart.WorksheetDrawing = new Xdr.WorksheetDrawing();
            newPart.WorksheetDrawing.AddNamespaceDeclaration("xdr",
                "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing");
            newPart.WorksheetDrawing.AddNamespaceDeclaration("a",
                "http://schemas.openxmlformats.org/drawingml/2006/main");
            wsPart.Worksheet.Append(new Drawing { Id = wsPart.GetIdOfPart(newPart) });
            return newPart;
        }

        /// <summary>
        /// Anchors a PNG to a single cell at the given 0-based column/row indices. The
        /// image is sized to 80x80 px (≈ 762000 EMUs) with a small 10 px offset from
        /// the top-left of the cell so it doesn't crash into the cell border.
        /// </summary>
        private static void AnchorImageToCell(
            DrawingsPart drawingsPart,
            int colIndex0,
            int rowIndex0,
            byte[] pngBytes,
            uint pictureNumber)
        {
            var imagePart = drawingsPart.AddImagePart(ImagePartType.Png);
            using (var ms = new MemoryStream(pngBytes))
            {
                imagePart.FeedData(ms);
            }
            var relationshipId = drawingsPart.GetIdOfPart(imagePart);

            const long extentEmu = 762000L; // 80 px at 96 DPI

            var anchor = new Xdr.OneCellAnchor(
                new Xdr.FromMarker(
                    new Xdr.ColumnId(colIndex0.ToString(CultureInfo.InvariantCulture)),
                    new Xdr.ColumnOffset("100000"),
                    new Xdr.RowId(rowIndex0.ToString(CultureInfo.InvariantCulture)),
                    new Xdr.RowOffset("100000")),
                new Xdr.Extent { Cx = extentEmu, Cy = extentEmu },
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
                            new A.Extents { Cx = extentEmu, Cy = extentEmu }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })),
                new Xdr.ClientData());

            drawingsPart.WorksheetDrawing.Append(anchor);
        }

        /// <summary>
        /// Returns the visible text of a cell, resolving shared strings if necessary.
        /// </summary>
        private static string ReadCellText(Cell cell, SharedStringTable sharedStringTable)
        {
            if (cell == null) return string.Empty;
            if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
            {
                if (sharedStringTable == null) return string.Empty;
                if (!int.TryParse(cell.CellValue?.Text ?? string.Empty, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                    return string.Empty;
                var item = sharedStringTable.Elements<SharedStringItem>().ElementAtOrDefault(idx);
                return item?.InnerText ?? string.Empty;
            }
            if (cell.DataType != null && cell.DataType.Value == CellValues.InlineString)
            {
                return cell.InlineString?.InnerText ?? string.Empty;
            }
            return cell.CellValue?.Text ?? cell.InnerText ?? string.Empty;
        }

        /// <summary>
        /// Reads <c>"B12"</c> -> letters=<c>"B"</c>, rowNumber=<c>12</c>.
        /// </summary>
        private static void ParseCellRef(string cellRef, out string columnLetters, out int rowNumber)
        {
            var splitIndex = 0;
            while (splitIndex < cellRef.Length && char.IsLetter(cellRef[splitIndex])) splitIndex++;
            columnLetters = cellRef.Substring(0, splitIndex);
            rowNumber = int.Parse(cellRef.Substring(splitIndex), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// "A" -> 0, "B" -> 1, ... "Z" -> 25, "AA" -> 26, ...
        /// </summary>
        private static int ColumnLettersToIndex(string letters)
        {
            var idx = 0;
            foreach (var ch in letters)
            {
                idx = idx * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            return idx - 1;
        }

        /// <summary>
        /// Inserts a row into sheetData maintaining ascending RowIndex order. Without
        /// this rows can appear out of order, which makes Excel re-sort the file at
        /// open and can break style associations.
        /// </summary>
        private static void InsertRowInOrder(SheetData sheetData, Row newRow)
        {
            var newIndex = newRow.RowIndex.Value;
            Row insertBefore = null;
            foreach (var existing in sheetData.Elements<Row>())
            {
                if (existing.RowIndex != null && existing.RowIndex.Value > newIndex)
                {
                    insertBefore = existing;
                    break;
                }
            }
            if (insertBefore != null) sheetData.InsertBefore(newRow, insertBefore);
            else sheetData.Append(newRow);
        }

        /// <summary>
        /// Finds the cell at <paramref name="targetCellRef"/> within <paramref name="row"/>,
        /// or creates and inserts a fresh one at the correct position. Required because
        /// OpenXml Row.Cell* elements must appear in column-letter ascending order.
        /// </summary>
        private static Cell FindOrCreateCell(Row row, string targetCellRef)
        {
            foreach (var existing in row.Elements<Cell>())
            {
                if (string.Equals(existing.CellReference?.Value, targetCellRef, StringComparison.OrdinalIgnoreCase))
                {
                    return existing;
                }
            }

            ParseCellRef(targetCellRef, out var targetCol, out _);
            var targetColIdx = ColumnLettersToIndex(targetCol);

            Cell insertBefore = null;
            foreach (var existing in row.Elements<Cell>())
            {
                if (existing.CellReference?.Value == null) continue;
                ParseCellRef(existing.CellReference.Value, out var col, out _);
                if (ColumnLettersToIndex(col) > targetColIdx)
                {
                    insertBefore = existing;
                    break;
                }
            }

            var newCell = new Cell { CellReference = targetCellRef };
            if (insertBefore != null) row.InsertBefore(newCell, insertBefore);
            else row.Append(newCell);
            return newCell;
        }

        /// <summary>
        /// Encodes a WPF ImageSource into PNG bytes; returns null on failure rather than
        /// throwing so a bad thumbnail never aborts the whole export.
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
                DiagnosticLog.Warn("ExcelTemplateExporter: PNG encoding failed - " + ex.Message);
                return null;
            }
        }

        private sealed class PlaceholderLocation
        {
            public ExportColumn Column;
            public string CellRef;
            public int RowNumber;
            public string ColumnLetters;
            public int ColumnIndex0;
            public string OriginalText;
            public string PlaceholderToken;
        }
    }

    /// <summary>
    /// Small result payload returned by <see cref="ExcelTemplateExporter.Export"/>. The UI
    /// surfaces these so users can confirm what was written and learn about any
    /// placeholders they typed that we did not recognise.
    /// </summary>
    public sealed class TemplateExportResult
    {
        public int RowsWritten { get; set; }
        public List<string> UnknownPlaceholders { get; set; }
    }
}
