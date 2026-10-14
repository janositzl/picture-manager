using FluentAssertions;
using PictureManager.Application.Diagnostics;
using PictureManager.Infrastructure.Diagnostics;

namespace PictureManager.Infrastructure.Tests.Diagnostics;

public sealed class FileLogReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pm-logs-" + Guid.NewGuid().ToString("N"));

    public FileLogReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private FileLogReader Reader() => new(new LogFileOptions(_dir, "picturemanager-*.log"));

    private static string Line(int i, string level = "INF") =>
        $"2026-10-14 10:00:{i % 60:00}.000 +00:00 [{level}] message {i}";

    private string Write(string name, IEnumerable<string> lines, DateTime? writtenUtc = null)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, lines);
        if (writtenUtc is not null)
            File.SetLastWriteTimeUtc(path, writtenUtc.Value);
        return path;
    }

    [Fact]
    public async Task MissingDirectory_ReturnsEmptyTail()
    {
        var reader = new FileLogReader(new LogFileOptions(Path.Combine(_dir, "nope"), "picturemanager-*.log"));

        var tail = await reader.ReadTailAsync(10, LogSeverity.Verbose, default);

        tail.FileName.Should().BeNull();
        tail.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task PicksTheNewestFile()
    {
        Write("picturemanager-20261013.log", [Line(1)], new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc));
        Write("picturemanager-20261014.log", [Line(2)], new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc));

        var tail = await Reader().ReadTailAsync(10, LogSeverity.Verbose, default);

        tail.FileName.Should().Be("picturemanager-20261014.log");
        tail.Entries.Should().ContainSingle().Which.Message.Should().Be("message 2");
        tail.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task ReturnsTheLastNEntries()
    {
        Write("picturemanager-20261014.log", Enumerable.Range(0, 50).Select(i => Line(i)));

        var tail = await Reader().ReadTailAsync(5, LogSeverity.Verbose, default);

        tail.Entries.Select(e => e.Message).Should().Equal("message 45", "message 46", "message 47", "message 48", "message 49");
        tail.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task LargeFile_ReadsFromTheEnd_AndDropsThePartialFirstLine()
    {
        Write("picturemanager-20261014.log", Enumerable.Range(0, 20000).Select(i => Line(i)));

        var tail = await Reader().ReadTailAsync(3, LogSeverity.Verbose, default);

        tail.Entries.Select(e => e.Message).Should().Equal("message 19997", "message 19998", "message 19999");
    }

    [Fact]
    public async Task FiltersByMinimumLevel()
    {
        Write("picturemanager-20261014.log", [Line(1), Line(2, "WRN"), Line(3), Line(4, "ERR")]);

        var tail = await Reader().ReadTailAsync(10, LogSeverity.Warning, default);

        tail.Entries.Select(e => e.Level).Should().Equal(LogSeverity.Warning, LogSeverity.Error);
    }

    [Fact]
    public async Task ReadsAFileThatAnotherWriterHoldsOpen()
    {
        var path = Path.Combine(_dir, "picturemanager-20261014.log");
        await using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await writer.WriteAsync(System.Text.Encoding.UTF8.GetBytes(Line(1) + "\n"));
        await writer.FlushAsync();

        var tail = await Reader().ReadTailAsync(10, LogSeverity.Verbose, default);

        tail.Entries.Should().ContainSingle();
    }
}
