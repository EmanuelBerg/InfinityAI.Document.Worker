using InfinityAI.Document.Worker.Models;
using System.Text.RegularExpressions;

namespace InfinityAI.Document.Worker.Helpers;

public static class EndpointHelpers
{
    public static bool IsTextExtractableDocument(string documentType, string extension)
    {
        extension = extension.ToLowerInvariant();
        return string.Equals(documentType, "Txt",      StringComparison.OrdinalIgnoreCase)
            || string.Equals(documentType, "Markdown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(documentType, "Pdf",      StringComparison.OrdinalIgnoreCase)
            || string.Equals(documentType, "Docx",     StringComparison.OrdinalIgnoreCase)
            || string.Equals(documentType, "Excel",    StringComparison.OrdinalIgnoreCase)
            || string.Equals(documentType, "Csv",      StringComparison.OrdinalIgnoreCase)
            || string.Equals(documentType, "Code",     StringComparison.OrdinalIgnoreCase)
            || extension is ".txt" or ".md" or ".pdf" or ".docx" or ".xlsx" or ".xlsm" or ".csv"
            || IsPlainTextExtension(extension);
    }

    public static bool IsPlainTextExtension(string extension)
    {
        extension = extension.ToLowerInvariant();
        return extension is
            ".cs" or ".razor" or ".js" or ".ts" or ".jsx" or ".tsx" or
            ".py" or ".java" or ".go" or ".rs" or ".cpp" or ".c" or ".h" or ".hpp" or
            ".json" or ".xml" or ".yaml" or ".yml" or
            ".css" or ".scss" or ".sass" or ".less" or
            ".html" or ".htm" or ".svg" or
            ".sql" or ".sh" or ".bash" or ".zsh" or
            ".ps1" or ".psm1" or ".psd1" or ".bat" or ".cmd" or
            ".tsv" or ".log" or ".ini" or ".toml" or ".conf" or ".config" or
            ".gitignore" or ".editorconfig" or ".env" or
            ".tf" or ".hcl" or ".bicep" or
            ".vue" or ".svelte" or ".dart" or ".kt" or ".swift" or ".rb" or ".php" or
            ".lua" or ".r" or ".m" or ".f90" or ".fs" or ".fsx" or ".vb";
    }

    public static List<WorkerChunk> CreateDocumentChunks(
        Guid documentId,
        string text,
        int maxChunkLength = 3000,
        int overlapLength  = 300,
        int minChunkLength = 150)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        if (text.Contains("--- Sheet:", StringComparison.Ordinal))
            return CreateTableAwareChunks(documentId, text, maxChunkLength);

        var isPdfContent = PageMarkerRegex.IsMatch(text);
        var segments     = ParsePageSegments(text);
        var chunks       = new List<WorkerChunk>();
        var chunkIndex   = 0;
        string? prevTail = null;

        foreach (var segment in segments)
        {
            var pageText = NormalizeExtractedText(segment.Content, isPdfContent);
            if (string.IsNullOrWhiteSpace(pageText)) continue;

            var workText              = prevTail is not null ? prevTail + "\n\n" + pageText : pageText;
            var tableHeader           = ExtractMarkdownTableHeader(pageText);
            var position              = 0;
            var isFirstChunkOfSegment = true;

            while (position < workText.Length)
            {
                var remaining = workText.Length - position;
                var length    = Math.Min(maxChunkLength, remaining);
                var chunkText = workText.Substring(position, length);

                if (position + length < workText.Length)
                {
                    var splitAt = FindBestSplitPosition(chunkText, maxChunkLength);
                    if (splitAt > minChunkLength) { chunkText = chunkText[..splitAt].Trim(); length = splitAt; }
                }

                chunkText = chunkText.Trim();

                if (!isFirstChunkOfSegment &&
                    tableHeader is not null &&
                    chunkText.StartsWith('|') &&
                    !chunkText.StartsWith(tableHeader))
                {
                    chunkText = tableHeader + "\n" + chunkText;
                }

                if (chunkText.Length >= 80)
                {
                    chunks.Add(new WorkerChunk
                    {
                        Id             = Guid.NewGuid(),
                        DocumentId     = documentId,
                        ChunkIndex     = chunkIndex++,
                        Content        = chunkText,
                        PageNumber     = segment.PageNumber,
                        Heading        = DetectHeading(chunkText),
                        CharacterCount = chunkText.Length,
                        TokenCount     = EstimateTokenCount(chunkText)
                    });
                    isFirstChunkOfSegment = false;
                }

                if (position + length >= workText.Length) break;
                var step = length - overlapLength;
                if (step <= 0) step = length;
                position += step;
            }

            prevTail = workText.Length > overlapLength
                ? workText[^overlapLength..].Trim()
                : workText.Trim();
        }

        return chunks;
    }

    // ── Table-aware chunking ─────────────────────────────────────────────────

    private static List<WorkerChunk> CreateTableAwareChunks(Guid documentId, string text, int maxChunkLength)
    {
        var chunks       = new List<WorkerChunk>();
        var sheetMatches = SheetMarkerRegex.Matches(text);

        for (var i = 0; i < sheetMatches.Count; i++)
        {
            var m            = sheetMatches[i];
            var sheetName    = m.Groups[1].Value.Trim();
            var contentStart = m.Index + m.Length;
            var contentEnd   = i + 1 < sheetMatches.Count ? sheetMatches[i + 1].Index : text.Length;
            AddTableChunks(documentId, sheetName, text[contentStart..contentEnd], maxChunkLength, chunks);
        }

        return chunks;
    }

    private static readonly Regex SheetMarkerRegex =
        new(@"^---\s*Sheet:\s*(.+?)\s*---", RegexOptions.Compiled | RegexOptions.Multiline);

    private static void AddTableChunks(Guid documentId, string sheetName, string sheetContent, int maxChunkLength, List<WorkerChunk> chunks)
    {
        var lines       = sheetContent.Split('\n');
        string? headerLine = null, separatorLine = null;
        var dataLines   = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r').TrimEnd();
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith('|')) continue;
            if (headerLine is null)                                                    { headerLine = line; }
            else if (separatorLine is null && line.All(c => c is '|' or '-' or ' ' or ':')) { separatorLine = line; }
            else                                                                        { dataLines.Add(line); }
        }

        if (headerLine is null) return;

        var sep         = separatorLine ?? BuildSeparatorFromHeader(headerLine);
        var headerBlock = $"--- Sheet: {sheetName} ---\n{headerLine}\n{sep}";
        var currentRows   = new List<string>();
        var currentLength = headerBlock.Length;

        void Flush()
        {
            if (currentRows.Count == 0) return;
            var content = (headerBlock + "\n" + string.Join("\n", currentRows)).Trim();
            if (content.Length >= 80)
                chunks.Add(new WorkerChunk
                {
                    Id = Guid.NewGuid(), DocumentId = documentId, ChunkIndex = chunks.Count,
                    Content = content, Heading = sheetName, CharacterCount = content.Length,
                    TokenCount = EstimateTokenCount(content)
                });
            currentRows.Clear();
            currentLength = headerBlock.Length;
        }

        foreach (var row in dataLines)
        {
            if (currentRows.Count > 0 && currentLength + row.Length + 1 > maxChunkLength) Flush();
            currentRows.Add(row);
            currentLength += row.Length + 1;
        }

        Flush();

        if (dataLines.Count == 0 && headerBlock.Length >= 80)
            chunks.Add(new WorkerChunk
            {
                Id = Guid.NewGuid(), DocumentId = documentId, ChunkIndex = chunks.Count,
                Content = headerBlock.Trim(), Heading = sheetName,
                CharacterCount = headerBlock.Length, TokenCount = EstimateTokenCount(headerBlock)
            });
    }

    private static string BuildSeparatorFromHeader(string headerLine)
    {
        var parts = headerLine.Split('|', StringSplitOptions.RemoveEmptyEntries);
        return "| " + string.Join(" | ", parts.Select(p => new string('-', Math.Max(3, p.Trim().Length)))) + " |";
    }

    // ── Text helpers ─────────────────────────────────────────────────────────

    private static string NormalizeExtractedText(string text, bool isPdfContent = false)
    {
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\t", "    ");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        normalized = Regex.Replace(normalized, @"(?<!\n) {2,}", " ");
        if (isPdfContent) normalized = CleanupPdfArtifacts(normalized);
        return normalized.Trim();
    }

    internal static string CleanupPdfArtifacts(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        text = Regex.Replace(text, @"(\d)([A-Za-z])",         "$1 $2");
        text = Regex.Replace(text, @"Gbps(\d)",               "Gbps $1");
        text = Regex.Replace(text, @"Mbps(\d)",               "Mbps $1");
        text = Regex.Replace(text, @"([A-Za-z])(\d)([A-Za-z])", "$1 $2 $3");
        text = Regex.Replace(text, @" {2,}",                  " ");
        return text.Trim();
    }

    private static readonly Regex PageMarkerRegex =
        new(@"^--- Page (\d+) ---", RegexOptions.Multiline | RegexOptions.Compiled);

    private sealed record TextSegment(int? PageNumber, string Content);

    private static List<TextSegment> ParsePageSegments(string text)
    {
        var segments = new List<TextSegment>();
        var matches  = PageMarkerRegex.Matches(text);

        if (matches.Count == 0) { segments.Add(new TextSegment(null, text)); return segments; }

        if (matches[0].Index > 0)
        {
            var pre = text[..matches[0].Index].Trim();
            if (!string.IsNullOrWhiteSpace(pre)) segments.Add(new TextSegment(null, pre));
        }

        for (var i = 0; i < matches.Count; i++)
        {
            var match        = matches[i];
            var pageNumber   = int.Parse(match.Groups[1].Value);
            var contentStart = match.Index + match.Length;
            var contentEnd   = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var content      = text[contentStart..contentEnd].Trim();
            if (!string.IsNullOrWhiteSpace(content)) segments.Add(new TextSegment(pageNumber, content));
        }

        return segments;
    }

    private static readonly Regex NumberedHeadingRegex = new(@"^(\d+\.)+\s*\S", RegexOptions.Compiled);

    private static string? DetectHeading(string chunkText)
    {
        var firstLine = chunkText.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(firstLine) || firstLine.Length > 120) return null;
        if (NumberedHeadingRegex.IsMatch(firstLine)) return firstLine.Length > 100 ? firstLine[..100] : firstLine;
        if (firstLine.Length <= 80 && firstLine.ToUpperInvariant() == firstLine && firstLine.Any(char.IsLetter)) return firstLine;
        if (firstLine.EndsWith(':') && firstLine.Length <= 80) return firstLine.TrimEnd(':');
        return null;
    }

    internal static string? ExtractMarkdownTableHeader(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < Math.Min(lines.Length - 1, 8); i++)
        {
            if (!lines[i].StartsWith('|')) continue;
            var sep = lines[i + 1];
            if (sep.StartsWith('|') && sep.All(c => c is '|' or '-' or ' '))
                return lines[i] + "\n" + sep;
        }
        return null;
    }

    private static int FindBestSplitPosition(string text, int maxChunkLength)
    {
        var min            = maxChunkLength / 2;
        var paragraphBreak = text.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (paragraphBreak > min) return paragraphBreak;
        var sentenceEnd = Math.Max(text.LastIndexOf(". ", StringComparison.Ordinal),
            Math.Max(text.LastIndexOf("! ", StringComparison.Ordinal),
                     text.LastIndexOf("? ", StringComparison.Ordinal)));
        if (sentenceEnd > min) return sentenceEnd + 1;
        var lineBreak = text.LastIndexOf('\n');
        if (lineBreak > min) return lineBreak;
        var space = text.LastIndexOf(' ');
        if (space > min) return space;
        return -1;
    }

    private static int EstimateTokenCount(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
}
