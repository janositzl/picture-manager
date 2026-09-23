using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PictureManager.Application.Albums;

/// <summary>
/// Export line: {prefix without trailing '/'}/{Alias ?? Name}/{RelativePath}/{FileName}{Extension};
/// an empty RelativePath drops its segment; every line ends with '\n'.
/// </summary>
public static class AlbumExportFormatter
{
    private static readonly char[] InvalidFileNameChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

    public static string FormatLine(string? prefix, AlbumExportRow row)
    {
        var line = new StringBuilder((prefix ?? string.Empty).TrimEnd('/'));
        line.Append('/').Append(row.RootAlias ?? row.RootName);
        if (!string.IsNullOrEmpty(row.RelativePath))
            line.Append('/').Append(row.RelativePath);
        line.Append('/').Append(row.FileName).Append(row.Extension);
        return line.ToString();
    }

    public static string Format(string? prefix, IEnumerable<AlbumExportRow> rows)
    {
        var content = new StringBuilder();
        foreach (var row in rows)
            content.Append(FormatLine(prefix, row)).Append('\n');
        return content.ToString();
    }

    public static string FileName(string albumName)
    {
        var safe = new string(albumName
            .Select(c => char.IsControl(c) || Array.IndexOf(InvalidFileNameChars, c) >= 0 ? '_' : c)
            .ToArray()).Trim();
        return (safe.Length == 0 ? "album" : safe) + ".txt";
    }
}
