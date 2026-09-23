using System.Collections.Generic;

namespace PictureManager.Application.Settings;

/// <summary>IncludedExtensions null = every extension is allowed (subject to ExcludedExtensions).</summary>
public sealed record SettingsDto(
    IReadOnlyList<string> ExcludedFolderNames,
    IReadOnlyList<string> ExcludedExtensions,
    IReadOnlyList<string>? IncludedExtensions);

public sealed record SettingsInput(
    IReadOnlyList<string?>? ExcludedFolderNames,
    IReadOnlyList<string?>? ExcludedExtensions,
    IReadOnlyList<string?>? IncludedExtensions);

/// <summary>PruneOnNextScan: the save newly excludes something, so the next scan will delete matching rows.</summary>
public sealed record SettingsSaveResult(
    IReadOnlyList<string> ExcludedFolderNames,
    IReadOnlyList<string> ExcludedExtensions,
    IReadOnlyList<string>? IncludedExtensions,
    bool PruneOnNextScan);
