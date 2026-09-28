using System.Collections.Generic;

namespace PictureManager.Application.Settings;

/// <summary>
/// IncludedExtensions null = every extension in SupportedExtensions is allowed (subject to ExcludedExtensions).
/// SupportedExtensions is the config-level ceiling (Scanning:SupportedExtensions): it is never stored and cannot
/// be changed through this API, but the client needs it to explain what "all extensions" actually means and to
/// avoid adding an include entry that can never match anything.
/// </summary>
public sealed record SettingsDto(
    IReadOnlyList<string> ExcludedFolderNames,
    IReadOnlyList<string> ExcludedExtensions,
    IReadOnlyList<string>? IncludedExtensions,
    IReadOnlyList<string> SupportedExtensions);

public sealed record SettingsInput(
    IReadOnlyList<string?>? ExcludedFolderNames,
    IReadOnlyList<string?>? ExcludedExtensions,
    IReadOnlyList<string?>? IncludedExtensions);

/// <summary>PruneOnNextScan: the save newly excludes something, so the next scan will delete matching rows.</summary>
public sealed record SettingsSaveResult(
    IReadOnlyList<string> ExcludedFolderNames,
    IReadOnlyList<string> ExcludedExtensions,
    IReadOnlyList<string>? IncludedExtensions,
    IReadOnlyList<string> SupportedExtensions,
    bool PruneOnNextScan);
