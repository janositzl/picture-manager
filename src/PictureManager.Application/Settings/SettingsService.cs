using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;

namespace PictureManager.Application.Settings;

public sealed class SettingsService : ISettingsService
{
    private readonly IAppSettingsRepository _repository;
    private readonly ScanningOptions _scanningOptions;

    public SettingsService(IAppSettingsRepository repository, ScanningOptions scanningOptions)
    {
        _repository = repository;
        _scanningOptions = scanningOptions;
    }

    public async Task<SettingsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetAsync(cancellationToken);
        return new SettingsDto(settings.ExcludedFolderNames, settings.ExcludedExtensions,
            settings.IncludedExtensions is { Count: > 0 } ? settings.IncludedExtensions : null,
            _scanningOptions.SupportedExtensions);
    }

    public async Task<Result<SettingsSaveResult>> UpdateAsync(SettingsInput input, CancellationToken cancellationToken = default)
    {
        if (!SettingsNormalizer.TryNormalizeFolderNames(input.ExcludedFolderNames, out var folderNames, out var folderError))
            return Result.Invalid("excludedFolderNames", folderError!);
        if (!SettingsNormalizer.TryNormalizeExtensions(input.ExcludedExtensions, out var excluded, out var excludedError))
            return Result.Invalid("excludedExtensions", excludedError!);

        List<string>? included = null;
        if (input.IncludedExtensions is not null)
        {
            if (!SettingsNormalizer.TryNormalizeExtensions(input.IncludedExtensions, out var normalizedIncluded, out var includedError))
                return Result.Invalid("includedExtensions", includedError!);
            included = normalizedIncluded.Count == 0 ? null : normalizedIncluded;
        }

        var settings = await _repository.GetAsync(cancellationToken);
        var prune = NewlyExcludesSomething(settings, folderNames, excluded, included);

        settings.ExcludedFolderNames = folderNames;
        settings.ExcludedExtensions = excluded;
        settings.IncludedExtensions = included;
        await _repository.UpdateAsync(settings, cancellationToken);

        return Result<SettingsSaveResult>.Ok(
            new SettingsSaveResult(folderNames, excluded, included, _scanningOptions.SupportedExtensions, prune));
    }

    private static bool NewlyExcludesSomething(AppSettings old, List<string> folderNames, List<string> excluded, List<string>? included)
    {
        var oldFolders = new HashSet<string>(old.ExcludedFolderNames, StringComparer.OrdinalIgnoreCase);
        var oldExcluded = new HashSet<string>(old.ExcludedExtensions, StringComparer.OrdinalIgnoreCase);
        if (folderNames.Any(f => !oldFolders.Contains(f)) || excluded.Any(e => !oldExcluded.Contains(e)))
            return true;

        if (included is null)
            return false;

        // A new include list narrows what's allowed: from "everything" (no old list), or by dropping entries.
        if (old.IncludedExtensions is not { Count: > 0 } oldIncluded)
            return true;

        var newIncluded = new HashSet<string>(included, StringComparer.OrdinalIgnoreCase);
        return oldIncluded.Any(e => !newIncluded.Contains(e));
    }
}
