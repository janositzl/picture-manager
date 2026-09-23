using System;
using System.Collections.Generic;
using System.Linq;

namespace PictureManager.Application.Scanning;

/// <summary>
/// Roots selected for a scan whose folder was missing or empty (usually an unmounted share). Nothing under them
/// was changed; the other roots were scanned, and the job ends Failed with this message.
/// </summary>
public sealed class ScanRootsUnavailableException : Exception
{
    public ScanRootsUnavailableException(IReadOnlyList<string> rootNames)
        : base(string.Join(" ", rootNames.Select(name =>
            $"Root '{name}' is unavailable: its folder is missing or empty. Check that the share is mounted.")))
    {
        RootNames = rootNames;
    }

    public IReadOnlyList<string> RootNames { get; }
}
