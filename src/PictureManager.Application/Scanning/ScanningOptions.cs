using System;
using System.Collections.Generic;
using PictureManager.Application.Settings;

namespace PictureManager.Application.Scanning;

/// <summary>From the "Scanning" config section. An empty list means every extension is a candidate.</summary>
public sealed class ScanningOptions
{
    /// <summary>File types the scanner indexes at all; the user's include/exclude settings narrow this further.</summary>
    public List<string> SupportedExtensions { get; set; } = new();

    /// <summary>
    /// Normalizes like the settings API ("jpg" → ".jpg") and fails fast on bad entries: a list that
    /// matches nothing would make the next scan prune every indexed image.
    /// </summary>
    public static ScanningOptions FromConfig(IEnumerable<string?>? supportedExtensions)
    {
        if (!SettingsNormalizer.TryNormalizeExtensions(supportedExtensions, out var normalized, out var error))
            throw new InvalidOperationException($"Scanning:SupportedExtensions is invalid: {error}");

        return new ScanningOptions { SupportedExtensions = normalized };
    }
}
