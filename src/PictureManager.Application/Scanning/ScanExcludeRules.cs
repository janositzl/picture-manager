using System;
using System.Collections.Generic;
using PictureManager.Model;

namespace PictureManager.Application.Scanning;

public sealed class ScanExcludeRules
{
    /// <summary>
    /// OS/NAS housekeeping folders excluded unconditionally, regardless of AppSettings.ExcludedFolderNames: a user
    /// editing that (database-backed) list must not be able to accidentally let a scan or discovery index these.
    /// </summary>
    public static readonly IReadOnlySet<string> AlwaysExcludedFolderNames = new HashSet<string>(
        new[] { "$RECYCLE.BIN", "System Volume Information", "@eaDir", "#recycle", ".snapshot" },
        StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _excludedFolderNames;
    private readonly HashSet<string> _excludedExtensions;
    private readonly HashSet<string>? _includedExtensions;
    private readonly HashSet<string>? _supportedExtensions;

    public ScanExcludeRules(AppSettings settings, IReadOnlyCollection<string> supportedExtensions)
    {
        _excludedFolderNames = new HashSet<string>(settings.ExcludedFolderNames, StringComparer.OrdinalIgnoreCase);
        _excludedFolderNames.UnionWith(AlwaysExcludedFolderNames);
        _excludedExtensions = new HashSet<string>(settings.ExcludedExtensions, StringComparer.OrdinalIgnoreCase);
        _includedExtensions = settings.IncludedExtensions is { Count: > 0 }
            ? new HashSet<string>(settings.IncludedExtensions, StringComparer.OrdinalIgnoreCase)
            : null;
        _supportedExtensions = supportedExtensions.Count > 0
            ? new HashSet<string>(supportedExtensions, StringComparer.OrdinalIgnoreCase)
            : null;
    }

    public bool IsFolderExcluded(string folderName) => _excludedFolderNames.Contains(folderName);

    public bool IsExtensionAllowed(string extension)
    {
        if (_supportedExtensions is not null && !_supportedExtensions.Contains(extension))
            return false;

        if (_excludedExtensions.Contains(extension))
            return false;

        return _includedExtensions is null || _includedExtensions.Contains(extension);
    }
}
