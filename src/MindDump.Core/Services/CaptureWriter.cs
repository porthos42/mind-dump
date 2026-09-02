using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MindDump.Services;

public sealed partial class CaptureWriter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly string[] MonthlyFolderSegments = ["Calendar", "Monthly"];

    public string GetMonthlyFileName(DateTimeOffset capturedAt)
        => $"{capturedAt:yyyy-MM} Mind Dump.md";

    public string GetMonthlyFilePath(string vaultFolderPath, DateTimeOffset capturedAt)
        => Path.Combine(GetMonthlyFolderPath(vaultFolderPath), GetMonthlyFileName(capturedAt));

    public string GetMonthlyFolderPath(string vaultFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultFolderPath);
        return Path.Combine(vaultFolderPath, Path.Combine(MonthlyFolderSegments));
    }

    public async Task<string> AppendAsync(
        string vaultFolderPath,
        string text,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default)
        => (await AppendWithUndoAsync(vaultFolderPath, text, capturedAt, cancellationToken)).FilePath;

    public async Task<CaptureUndoToken> AppendWithUndoAsync(
        string vaultFolderPath,
        string text,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultFolderPath);
        string normalizedText = NormalizeText(text);
        Directory.CreateDirectory(GetMonthlyFolderPath(vaultFolderPath));

        string path = GetMonthlyFilePath(vaultFolderPath, capturedAt);
        bool fileCreated = !File.Exists(path);
        long previousLength = fileCreated ? 0 : new FileInfo(path).Length;
        string dailyHeader = $"## {capturedAt:yyyy-MM-dd}";
        string entryId = Guid.NewGuid().ToString("N");
        string formattedEntry = FormatStoredEntry(entryId, normalizedText, capturedAt);

        if (fileCreated)
        {
            string title = capturedAt.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            string initialContent = $"# Mind Dump - {title}\n\n{dailyHeader}\n{formattedEntry}\n";
            await File.WriteAllTextAsync(path, initialContent, Utf8WithoutBom, cancellationToken);
        }
        else
        {
            string existing = await File.ReadAllTextAsync(path, cancellationToken);
            MatchCollection dailyHeadings = DailyHeadingRegex().Matches(existing);
            bool isCurrentDaySection = dailyHeadings.Count > 0
                && string.Equals(dailyHeadings[^1].Value, dailyHeader, StringComparison.Ordinal);

            StringBuilder appendText = new();
            if (!existing.EndsWith('\n'))
            {
                appendText.Append('\n');
            }

            if (!isCurrentDaySection)
            {
                appendText.Append('\n').Append(dailyHeader).Append('\n');
            }

            appendText.Append(formattedEntry).Append('\n');
            await File.AppendAllTextAsync(path, appendText.ToString(), Utf8WithoutBom, cancellationToken);
        }

        byte[] resultingBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return new CaptureUndoToken(path, previousLength, SHA256.HashData(resultingBytes), fileCreated, entryId);
    }

    public async Task<IReadOnlyList<CaptureEntry>> GetRecentAsync(
        string vaultFolderPath,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultFolderPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        string folder = GetMonthlyFolderPath(vaultFolderPath);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        List<CaptureEntry> entries = [];
        foreach (string path in Directory.EnumerateFiles(folder, "????-?? Mind Dump.md").OrderByDescending(Path.GetFileName))
        {
            string content = await File.ReadAllTextAsync(path, cancellationToken);
            entries.AddRange(ParseEntries(content, path));
        }

        return entries.OrderByDescending(entry => entry.CapturedAt).Take(limit).ToArray();
    }

    public Task DeleteEntryAsync(CaptureEntry entry, CancellationToken cancellationToken = default)
        => ReplaceEntryAsync(entry, replacementText: null, cancellationToken);

    public Task UpdateEntryAsync(CaptureEntry entry, string text, CancellationToken cancellationToken = default)
        => ReplaceEntryAsync(entry, NormalizeText(text), cancellationToken);

    public async Task MigrateLegacyMarkersAsync(string vaultFolderPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultFolderPath);
        string folder = GetMonthlyFolderPath(vaultFolderPath);
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (string path in Directory.EnumerateFiles(folder, "????-?? Mind Dump.md"))
        {
            string content = await File.ReadAllTextAsync(path, cancellationToken);
            string updated = LegacyMarkerRegex().Replace(
                content,
                match => $"%% minddump:entry id={match.Groups["id"].Value} captured={match.Groups["captured"].Value} %%");
            if (!string.Equals(content, updated, StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(path, updated, Utf8WithoutBom, cancellationToken);
            }
        }
    }

    public async Task UndoAsync(CaptureUndoToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!File.Exists(token.FilePath))
        {
            throw new InvalidOperationException("The note no longer exists, so the capture cannot be undone.");
        }

        byte[] currentBytes = await File.ReadAllBytesAsync(token.FilePath, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(currentBytes), token.ResultingFileHash))
        {
            throw new InvalidOperationException("The note changed after this capture. Undo was cancelled to protect those changes.");
        }

        if (token.FileCreated)
        {
            File.Delete(token.FilePath);
            return;
        }

        await using FileStream stream = new(token.FilePath, FileMode.Open, FileAccess.Write, FileShare.Read);
        stream.SetLength(token.PreviousLength);
    }

    public string FormatEntry(string text, DateTimeOffset capturedAt)
    {
        string normalizedText = NormalizeText(text);
        string[] lines = normalizedText.Split('\n');
        StringBuilder builder = new($"- `{capturedAt:HH:mm}` {lines[0]}");

        for (int index = 1; index < lines.Length; index++)
        {
            builder.Append('\n').Append("  ").Append(lines[index]);
        }

        return builder.ToString();
    }

    private string FormatStoredEntry(string id, string text, DateTimeOffset capturedAt)
        => $"%% minddump:entry id={id} captured={capturedAt:O} %%\n{FormatEntry(text, capturedAt)}";

    private async Task ReplaceEntryAsync(CaptureEntry entry, string? replacementText, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!File.Exists(entry.FilePath))
        {
            throw new InvalidOperationException("The note containing this capture no longer exists.");
        }

        string content = await File.ReadAllTextAsync(entry.FilePath, cancellationToken);
        Match match = EntryRegex().Matches(content).Cast<Match>()
            .FirstOrDefault(candidate => string.Equals(candidate.Groups["id"].Value, entry.Id, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("This capture could not be found. The note may have been edited.");

        string replacement = replacementText is null
            ? string.Empty
            : FormatStoredEntry(entry.Id, replacementText, entry.CapturedAt) + "\n";
        string updated = content.Remove(match.Index, match.Length).Insert(match.Index, replacement);
        await File.WriteAllTextAsync(entry.FilePath, updated, Utf8WithoutBom, cancellationToken);
    }

    private static IEnumerable<CaptureEntry> ParseEntries(string content, string path)
    {
        foreach (Match match in EntryRegex().Matches(content))
        {
            if (!DateTimeOffset.TryParseExact(match.Groups["captured"].Value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset capturedAt))
            {
                continue;
            }

            string firstLine = match.Groups["first"].Value;
            string continuations = match.Groups["continuations"].Value;
            string text = firstLine;
            if (continuations.Length > 0)
            {
                text += "\n" + string.Join('\n', continuations.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line[2..]));
            }

            yield return new CaptureEntry(match.Groups["id"].Value, text, capturedAt, path);
        }
    }

    private static string NormalizeText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    [GeneratedRegex(@"^## \d{4}-\d{2}-\d{2}$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex DailyHeadingRegex();

    [GeneratedRegex(@"^(?:%% minddump:entry id=(?<id>[a-f0-9]{32}) captured=(?<captured>[^ ]+) %%|<!-- minddump:entry id=(?<id>[a-f0-9]{32}) captured=(?<captured>[^ ]+) -->)\n- `\d{2}:\d{2}` (?<first>[^\n]*)(?<continuations>(?:\n  [^\n]*)*)\n", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex EntryRegex();

    [GeneratedRegex(@"^<!-- minddump:entry id=(?<id>[a-f0-9]{32}) captured=(?<captured>[^ ]+) -->$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyMarkerRegex();
}

public sealed record CaptureUndoToken(
    string FilePath,
    long PreviousLength,
    byte[] ResultingFileHash,
    bool FileCreated,
    string EntryId);

public sealed record CaptureEntry(
    string Id,
    string Text,
    DateTimeOffset CapturedAt,
    string FilePath);
