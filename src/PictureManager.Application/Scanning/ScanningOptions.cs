using System.Collections.Generic;

namespace PictureManager.Application.Scanning;

/// <summary>Bound from the "Scanning" config section. An empty list means every extension is a candidate.</summary>
public sealed class ScanningOptions
{
    /// <summary>File types the scanner indexes at all; the user's include/exclude settings narrow this further.</summary>
    public List<string> SupportedExtensions { get; set; } = new();
}
