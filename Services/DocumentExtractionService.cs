using ClosedXML.Excel;
using InfinityAI.Document.Worker.Helpers;
using System.Text;
using System.Text.RegularExpressions;


namespace InfinityAI.Document.Worker.Services;

public sealed class DocumentExtractionService : IDocumentExtractionService
{
    private readonly ILogger<DocumentExtractionService> _logger;

    public DocumentExtractionService(ILogger<DocumentExtractionService> logger) =>
        _logger = logger;

    public async Task<string> ExtractTextAsync(byte[] content, string fileExtension, CancellationToken ct)
    {
        var extension = fileExtension.ToLowerInvariant();

        if (EndpointHelpers.IsPlainTextExtension(extension))
            return DecodeAsUtf8(content);

        return extension switch
        {
            ".txt" or ".md" => DecodeAsUtf8(content),
            ".pdf"          => ExtractPdf(content),
            ".docx"         => ExtractDocx(content),
            ".xlsx" or ".xlsm" => ExtractXlsx(content, fileExtension),
            ".csv"          => DecodeAsUtf8(content),
            _ => throw new NotSupportedException($"Document type {extension} is not supported.")
        };
    }

    private static string DecodeAsUtf8(byte[] bytes)
    {
        try   { return Encoding.UTF8.GetString(bytes); }
        catch { return Encoding.Latin1.GetString(bytes); }
    }

    private static string ExtractPdf(byte[] content)
    {
        using var stream   = new MemoryStream(content);
        using var document = UglyToad.PdfPig.PdfDocument.Open(stream);

        var sb = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            sb.AppendLine($"--- Page {page.Number} ---");

            var words = page.GetWords()
                .OrderByDescending(w => w.BoundingBox.Bottom)
                .ThenBy(w => w.BoundingBox.Left)
                .ToList();

            var lines = new List<List<UglyToad.PdfPig.Content.Word>>();
            const double yTolerance = 3.0;

            foreach (var word in words)
            {
                var line = lines.FirstOrDefault(l =>
                    Math.Abs(l[0].BoundingBox.Bottom - word.BoundingBox.Bottom) < yTolerance);

                if (line is null) lines.Add([word]);
                else              line.Add(word);
            }

            foreach (var line in lines)
            {
                var orderedLine = line.OrderBy(w => w.BoundingBox.Left).ToList();
                var lineSb      = new StringBuilder();
                var previousRight = 0.0;

                foreach (var word in orderedLine)
                {
                    var gap = word.BoundingBox.Left - previousRight;
                    if (lineSb.Length > 0) lineSb.Append(gap > 20 ? " | " : " ");
                    lineSb.Append(FixFortinetModelNames(word.Text));
                    previousRight = word.BoundingBox.Right;
                }

                sb.AppendLine(FixFortinetModelNames(lineSb.ToString()));
            }

            sb.AppendLine();
        }

        return CleanupPdfArtifacts(FixFortinetModelNames(sb.ToString()));
    }

    private static string FixFortinetModelNames(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        text = Regex.Replace(text, @"\b(FG|FWF)-(\d+)\s+([A-Z])\b", "$1-$2$3");
        text = Regex.Replace(text, @"\b(FG/FWF)-(\d+)\s+([A-Z])\b", "$1-$2$3");
        return text;
    }

    internal static string CleanupPdfArtifacts(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        text = Regex.Replace(text, @"(\d)([A-Za-z])", "$1 $2");
        text = Regex.Replace(text, @"Gbps(\d)", "Gbps $1");
        text = Regex.Replace(text, @"Mbps(\d)", "Mbps $1");
        text = Regex.Replace(text, @"([A-Za-z])(\d)([A-Za-z])", "$1 $2 $3");
        text = Regex.Replace(text, @" {2,}", " ");
        return text.Trim();
    }

    private static string ExtractDocx(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var doc    = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false);
        return doc.MainDocumentPart?.Document?.Body?.InnerText ?? "";
    }

    private static readonly Regex FntPattern =
        new(@"FNT\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private string ExtractXlsx(byte[] content, string fileExtension)
    {
        using var stream   = new MemoryStream(content);
        using var workbook = new XLWorkbook(stream);
        var sb = new StringBuilder();

        foreach (var sheet in workbook.Worksheets)
        {
            var usedRange = sheet.RangeUsed();
            if (usedRange == null) continue;

            var firstRow = usedRange.RangeAddress.FirstAddress.RowNumber;
            var lastRow  = usedRange.RangeAddress.LastAddress.RowNumber;
            var firstCol = usedRange.RangeAddress.FirstAddress.ColumnNumber;
            var lastCol  = usedRange.RangeAddress.LastAddress.ColumnNumber;

            var colNums = Enumerable.Range(firstCol, lastCol - firstCol + 1).ToList();

            sb.AppendLine($"--- Sheet: {sheet.Name} ---");

            var headerRowCount = DetectHeaderRowCount(sheet, firstRow, lastRow, firstCol, lastCol);
            var headerRows     = Enumerable.Range(firstRow, headerRowCount).ToList();

            List<string> headers;
            if (headerRowCount == 1)
                headers = colNums.Select(col => EscapeMarkdownCell(ReadCell(sheet, firstRow, col))).ToList();
            else
                headers = BuildQualifiedHeaders(sheet, headerRows, colNums);

            sb.AppendLine("| " + string.Join(" | ", headers) + " |");
            sb.AppendLine("| " + string.Join(" | ", headers.Select(h => new string('-', Math.Max(3, h.Length)))) + " |");

            var dataStart = firstRow + headerRowCount;
            var dataRows  = 0;
            var fntTotal  = 0;
            string? firstDataRow = null;
            string  lastDataRow  = "";

            for (var rowNum = dataStart; rowNum <= lastRow; rowNum++)
            {
                var cells   = colNums.Select(col => EscapeMarkdownCell(ReadCell(sheet, rowNum, col))).ToList();
                var rowText = "| " + string.Join(" | ", cells) + " |";
                sb.AppendLine(rowText);

                dataRows++;
                fntTotal += FntPattern.Matches(rowText).Count;
                firstDataRow ??= rowText;
                lastDataRow   = rowText;
            }

            _logger.LogInformation(
                "[EXTRACT_XLSX] Sheet='{Sheet}' HeaderRows={HdrRows} DataRows={Rows} Cols={Cols} FntMatches={Fnt}",
                sheet.Name, headerRowCount, dataRows, colNums.Count, fntTotal);

            if (firstDataRow != null)
                _logger.LogDebug("[EXTRACT_XLSX_SAMPLE] Sheet='{Sheet}' First='{First}' Last='{Last}'",
                    sheet.Name, firstDataRow, lastDataRow);

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static int DetectHeaderRowCount(IXLWorksheet sheet, int firstRow, int lastRow, int firstCol, int lastCol)
    {
        var groupRows = 0;
        for (var row = firstRow; row <= Math.Min(firstRow + 3, lastRow - 1); row++)
        {
            if (HasHorizontalMerge(sheet, row, firstCol, lastCol)) groupRows++;
            else break;
        }
        return groupRows + 1;
    }

    private static bool HasHorizontalMerge(IXLWorksheet sheet, int row, int firstCol, int lastCol) =>
        sheet.MergedRanges.Any(mr =>
        {
            var fa = mr.RangeAddress.FirstAddress;
            var la = mr.RangeAddress.LastAddress;
            return fa.RowNumber == row
                && fa.ColumnNumber >= firstCol
                && la.ColumnNumber <= lastCol
                && la.ColumnNumber > fa.ColumnNumber;
        });

    private static List<string> BuildQualifiedHeaders(IXLWorksheet sheet, List<int> headerRows, List<int> colNums)
    {
        var rawNames = new List<string>();

        foreach (var col in colNums)
        {
            var parts = new List<string>();
            foreach (var row in headerRows)
            {
                var val = ReadCell(sheet, row, col).Trim();
                if (!string.IsNullOrWhiteSpace(val) &&
                    !parts.Any(p => string.Equals(p, val, StringComparison.OrdinalIgnoreCase)))
                    parts.Add(val);
            }
            var name = string.Join("_", parts).Trim('_');
            rawNames.Add(string.IsNullOrWhiteSpace(name) ? "" : name);
        }

        var totalCounts = rawNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var occurrenceSoFar = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result          = new List<string>();

        for (var i = 0; i < rawNames.Count; i++)
        {
            var name = rawNames[i];
            if (string.IsNullOrWhiteSpace(name)) { result.Add(EscapeMarkdownCell($"Col{i + 1}")); continue; }
            if (totalCounts.TryGetValue(name, out var total) && total > 1)
            {
                occurrenceSoFar.TryGetValue(name, out var occ);
                occurrenceSoFar[name] = occ + 1;
                result.Add(EscapeMarkdownCell($"{name}_{occ + 1}"));
            }
            else result.Add(EscapeMarkdownCell(name));
        }

        return result;
    }

    private static string ReadCell(IXLWorksheet sheet, int row, int col)
    {
        var cell  = sheet.Cell(row, col);
        var value = cell.GetFormattedString();
        if (!string.IsNullOrEmpty(value)) return value;
        if (!cell.IsMerged()) return value;

        foreach (var mergedRange in sheet.MergedRanges)
        {
            var fa = mergedRange.RangeAddress.FirstAddress;
            var la = mergedRange.RangeAddress.LastAddress;
            if (row >= fa.RowNumber && row <= la.RowNumber &&
                col >= fa.ColumnNumber && col <= la.ColumnNumber)
                return mergedRange.FirstCell().GetFormattedString();
        }

        return value;
    }

    private static string EscapeMarkdownCell(string? value) =>
        (value ?? "").Replace("|", "\\|").Replace("\n", " ").Replace("\r", "").Trim();
}
