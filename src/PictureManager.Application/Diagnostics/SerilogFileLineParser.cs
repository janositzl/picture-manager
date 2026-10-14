using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace PictureManager.Application.Diagnostics;

/// <summary>
/// Groups raw lines of a Serilog file sink (default template
/// "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}") into entries.
/// A line that does not start with a timestamp and level continues the previous entry.
/// </summary>
public static partial class SerilogFileLineParser
{
    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(VRB|DBG|INF|WRN|ERR|FTL)\] (.*)$")]
    private static partial Regex HeaderPattern();

    public static IReadOnlyList<LogEntry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<LogEntry>();
        string? timestamp = null;
        LogSeverity? level = null;
        string message = "";
        var continuation = new StringBuilder();
        var open = false;

        void Flush()
        {
            if (open)
                entries.Add(new LogEntry(timestamp, level, message, continuation.Length == 0 ? null : continuation.ToString()));
            continuation.Clear();
        }

        foreach (var line in lines)
        {
            var match = HeaderPattern().Match(line);
            if (match.Success)
            {
                Flush();
                timestamp = match.Groups[1].Value;
                level = ToLevel(match.Groups[2].Value);
                message = match.Groups[3].Value;
                open = true;
            }
            else
            {
                if (!open)
                {
                    timestamp = null;
                    level = null;
                    message = line;
                    open = true;
                    continue;
                }

                if (continuation.Length > 0)
                    continuation.Append('\n');
                continuation.Append(line);
            }
        }

        Flush();
        return entries;
    }

    private static LogSeverity ToLevel(string code) => code switch
    {
        "VRB" => LogSeverity.Verbose,
        "DBG" => LogSeverity.Debug,
        "INF" => LogSeverity.Information,
        "WRN" => LogSeverity.Warning,
        "ERR" => LogSeverity.Error,
        _ => LogSeverity.Fatal
    };
}
