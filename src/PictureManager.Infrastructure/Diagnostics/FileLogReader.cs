using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Diagnostics;

namespace PictureManager.Infrastructure.Diagnostics;

/// <summary>Directory and file-name glob of the Serilog file sink, e.g. ("/app/logs", "picturemanager-*.log").</summary>
public sealed record LogFileOptions(string? Directory, string? FilePattern);

public sealed class FileLogReader(LogFileOptions options) : ILogReader
{
    private const int ChunkSize = 64 * 1024;
    private const int MaxBytes = 4 * 1024 * 1024;

    public Task<LogTail> ReadTailAsync(int take, LogSeverity minLevel, CancellationToken cancellationToken)
    {
        var file = FindCurrentFile();
        if (file is null)
            return Task.FromResult(new LogTail(null, [], false));

        var (lines, startedAtBeginning) = ReadTailLines(file, take, minLevel, cancellationToken);
        var entries = SerilogFileLineParser.Parse(lines)
            .Where(e => e.Level is null || e.Level >= minLevel)
            .ToList();

        var truncated = !startedAtBeginning || entries.Count > take;
        if (entries.Count > take)
            entries = entries.GetRange(entries.Count - take, take);

        return Task.FromResult(new LogTail(file.Name, entries, truncated));
    }

    private FileInfo? FindCurrentFile()
    {
        if (string.IsNullOrEmpty(options.Directory) || string.IsNullOrEmpty(options.FilePattern)
            || !System.IO.Directory.Exists(options.Directory))
            return null;

        return new DirectoryInfo(options.Directory)
            .EnumerateFiles(options.FilePattern)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Reads backwards in chunks until enough lines are available for <paramref name="take"/> entries (a filtered
    /// read keeps going until the cap, since matching entries can be sparse). The file is opened shared because
    /// Serilog keeps it open for writing.
    /// </summary>
    private static (List<string> Lines, bool StartedAtBeginning) ReadTailLines(
        FileInfo file, int take, LogSeverity minLevel, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var length = stream.Length;
        var position = length;
        var buffer = new List<byte>();

        while (position > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var size = (int)Math.Min(ChunkSize, position);
            position -= size;
            stream.Seek(position, SeekOrigin.Begin);
            var chunk = new byte[size];
            stream.ReadExactly(chunk);
            buffer.InsertRange(0, chunk);

            if (length - position >= MaxBytes)
                break;

            var lines = Decode(buffer, position == 0);
            var matching = SerilogFileLineParser.Parse(lines).Count(e => e.Level is null || e.Level >= minLevel);
            if (matching > take)
                break;
        }

        var startedAtBeginning = position == 0;
        return (Decode(buffer, startedAtBeginning), startedAtBeginning);
    }

    private static List<string> Decode(List<byte> bytes, bool startedAtBeginning)
    {
        var text = Encoding.UTF8.GetString(bytes.ToArray());
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        // The first line is a fragment unless the read reached the start of the file.
        if (!startedAtBeginning && lines.Count > 0)
            lines.RemoveAt(0);
        return lines;
    }
}
