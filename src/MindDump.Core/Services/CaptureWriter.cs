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
        string formattedEntry = FormatEntry(normalizedText, capturedAt);

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
        return new CaptureUndoToken(path, previousLength, SHA256.HashData(resultingBytes), fileCreated);
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

    private static string NormalizeText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    [GeneratedRegex(@"^## \d{4}-\d{2}-\d{2}$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex DailyHeadingRegex();
}

public sealed record CaptureUndoToken(
    string FilePath,
    long PreviousLength,
    byte[] ResultingFileHash,
    bool FileCreated);
