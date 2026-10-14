using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Diagnostics;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LogSeverity
{
    Verbose = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Fatal = 5
}

/// <summary>
/// One log entry. Level is null for lines that could not be attributed to an entry (a read that began mid-entry).
/// Exception holds the continuation lines (stack trace, multi-line message tail), or null when there are none.
/// </summary>
public sealed record LogEntry(string? Timestamp, LogSeverity? Level, string Message, string? Exception);

/// <summary>FileName is null when no log file exists (none configured, or nothing written yet).</summary>
public sealed record LogTail(string? FileName, IReadOnlyList<LogEntry> Entries, bool Truncated);

public sealed record AppVersionInfo(
    string Version,
    DateTimeOffset BuildTimeUtc,
    DateTimeOffset StartedUtc,
    string Environment,
    string Runtime);

public interface ILogReader
{
    /// <summary>The newest <paramref name="take"/> entries of the current log file at or above <paramref name="minLevel"/>.</summary>
    Task<LogTail> ReadTailAsync(int take, LogSeverity minLevel, CancellationToken cancellationToken);
}
