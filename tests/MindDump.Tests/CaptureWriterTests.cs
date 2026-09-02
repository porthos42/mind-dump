using MindDump.Services;

namespace MindDump.Tests;

public sealed class CaptureWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"MindDumpTests-{Guid.NewGuid():N}");
    private readonly CaptureWriter _writer = new();

    [Fact]
    public void GetMonthlyFileName_UsesSortableYearAndMonth()
    {
        Assert.Equal("2026-05 Mind Dump.md", _writer.GetMonthlyFileName(new DateTimeOffset(2026, 5, 31, 23, 59, 0, TimeSpan.Zero)));
        Assert.Equal("2027-01 Mind Dump.md", _writer.GetMonthlyFileName(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void GetMonthlyFilePath_StoresNotesInCalendarMonthlyBelowVaultRoot()
    {
        string path = _writer.GetMonthlyFilePath(_directory, At(27, 14, 32));

        Assert.Equal(
            Path.Combine(_directory, "Calendar", "Monthly", "2026-05 Mind Dump.md"),
            path);
    }

    [Fact]
    public async Task AppendAsync_CreatesMonthlyFileWithTitleDateAndEntry()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string path = await _writer.AppendAsync(_directory, "First captured thought", At(27, 14, 32), cancellationToken);

        string content = await File.ReadAllTextAsync(path, cancellationToken);
        Assert.StartsWith("# Mind Dump - May 2026\n\n## 2026-05-27\n%% minddump:entry id=", content, StringComparison.Ordinal);
        Assert.EndsWith(" %%\n- `14:32` First captured thought\n", content, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(_directory, "Calendar", "Monthly")));
    }

    [Fact]
    public async Task AppendAsync_AppendsSameDayEntriesWithoutAddingAnotherHeading()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string path = await _writer.AppendAsync(_directory, "First", At(27, 8, 0), cancellationToken);
        await _writer.AppendAsync(_directory, "Second", At(27, 9, 15), cancellationToken);

        string content = await File.ReadAllTextAsync(path, cancellationToken);
        Assert.Equal(1, CountOccurrences(content, "## 2026-05-27"));
        Assert.Contains("- `08:00` First\n", content, StringComparison.Ordinal);
        Assert.Contains("- `09:15` Second\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppendAsync_AddsNewHeadingWhenCalendarDayChanges()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string path = await _writer.AppendAsync(_directory, "Before sleep", At(27, 23, 55), cancellationToken);
        await _writer.AppendAsync(_directory, "New day", At(28, 0, 5), cancellationToken);

        string content = await File.ReadAllTextAsync(path, cancellationToken);
        Assert.Contains("\n## 2026-05-28\n%% minddump:entry id=", content, StringComparison.Ordinal);
        Assert.EndsWith(" %%\n- `00:05` New day\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatEntry_IndentsMultilineContentAsMarkdownContinuation()
    {
        string formatted = _writer.FormatEntry("Line one\r\nLine two\nLine three", At(27, 14, 32));

        Assert.Equal("- `14:32` Line one\n  Line two\n  Line three", formatted);
    }

    [Fact]
    public async Task AppendAsync_RejectsWhitespaceOnlyContentWithoutWritingFile()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _writer.AppendAsync(
            _directory,
            " \r\n ",
            At(27, 14, 32),
            TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task UndoAsync_RemovesCaptureFromExistingNote()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string path = await _writer.AppendAsync(_directory, "Keep this", At(27, 8, 0), cancellationToken);
        string original = await File.ReadAllTextAsync(path, cancellationToken);
        CaptureUndoToken token = await _writer.AppendWithUndoAsync(_directory, "Undo this", At(27, 9, 15), cancellationToken);

        await _writer.UndoAsync(token, cancellationToken);

        Assert.Equal(original, await File.ReadAllTextAsync(path, cancellationToken));
    }

    [Fact]
    public async Task UndoAsync_DeletesNoteCreatedByCapture()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        CaptureUndoToken token = await _writer.AppendWithUndoAsync(_directory, "Undo this", At(27, 9, 15), cancellationToken);

        await _writer.UndoAsync(token, cancellationToken);

        Assert.False(File.Exists(token.FilePath));
    }

    [Fact]
    public async Task UndoAsync_RefusesToOverwriteChangesMadeAfterCapture()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        CaptureUndoToken token = await _writer.AppendWithUndoAsync(_directory, "Captured", At(27, 9, 15), cancellationToken);
        await File.AppendAllTextAsync(token.FilePath, "External edit\n", cancellationToken);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _writer.UndoAsync(token, cancellationToken));

        Assert.Contains("changed after this capture", exception.Message, StringComparison.Ordinal);
        Assert.Contains("External edit", await File.ReadAllTextAsync(token.FilePath, cancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsTaggedEntriesNewestFirstAcrossMonthlyNotes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await _writer.AppendAsync(_directory, "Older", At(27, 8, 0), cancellationToken);
        await _writer.AppendAsync(_directory, "Newer\nwith detail", At(28, 9, 15), cancellationToken);

        IReadOnlyList<CaptureEntry> entries = await _writer.GetRecentAsync(_directory, cancellationToken: cancellationToken);

        Assert.Equal(["Newer\nwith detail", "Older"], entries.Select(entry => entry.Text));
        Assert.All(entries, entry => Assert.Equal(32, entry.Id.Length));
    }

    [Fact]
    public async Task UpdateAndDeleteEntryAsync_TargetEntryByIdentifier()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await _writer.AppendAsync(_directory, "Keep", At(27, 8, 0), cancellationToken);
        await _writer.AppendAsync(_directory, "Change", At(27, 9, 15), cancellationToken);
        CaptureEntry changed = (await _writer.GetRecentAsync(_directory, cancellationToken: cancellationToken))[0];

        await _writer.UpdateEntryAsync(changed, "Updated\ntext", cancellationToken);
        CaptureEntry updated = (await _writer.GetRecentAsync(_directory, cancellationToken: cancellationToken))[0];
        Assert.Equal("Updated\ntext", updated.Text);
        Assert.Equal(changed.Id, updated.Id);

        await _writer.DeleteEntryAsync(updated, cancellationToken);
        IReadOnlyList<CaptureEntry> remaining = await _writer.GetRecentAsync(_directory, cancellationToken: cancellationToken);
        Assert.Single(remaining);
        Assert.Equal("Keep", remaining[0].Text);
    }

    [Fact]
    public async Task MigrateLegacyMarkersAsync_UsesObsidianNativeCommentsAndKeepsEntryReadable()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string folder = _writer.GetMonthlyFolderPath(_directory);
        Directory.CreateDirectory(folder);
        string path = _writer.GetMonthlyFilePath(_directory, At(27, 8, 0));
        await File.WriteAllTextAsync(path,
            "# Mind Dump - May 2026\n\n## 2026-05-27\n<!-- minddump:entry id=0123456789abcdef0123456789abcdef captured=2026-05-27T08:00:00.0000000+07:00 -->\n- `08:00` Legacy\n",
            cancellationToken);

        await _writer.MigrateLegacyMarkersAsync(_directory, cancellationToken);

        string content = await File.ReadAllTextAsync(path, cancellationToken);
        Assert.DoesNotContain("<!-- minddump:entry", content, StringComparison.Ordinal);
        Assert.Contains("%% minddump:entry id=0123456789abcdef0123456789abcdef captured=2026-05-27T08:00:00.0000000+07:00 %%", content, StringComparison.Ordinal);
        Assert.Equal("Legacy", (await _writer.GetRecentAsync(_directory, cancellationToken: cancellationToken)).Single().Text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static DateTimeOffset At(int day, int hour, int minute)
        => new(2026, 5, day, hour, minute, 0, TimeSpan.FromHours(7));

    private static int CountOccurrences(string value, string needle)
        => value.Split(needle, StringSplitOptions.None).Length - 1;
}
