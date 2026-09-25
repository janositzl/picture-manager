using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Roots;

/// <summary>
/// "Config creates, the database is the truth": creates roots whose MountPath isn't registered yet and
/// never changes an existing root (its Name/Alias/IsActive may have been edited through the API).
/// Never fails startup: problems are logged and the entry (or just its alias) is skipped.
/// </summary>
public sealed class ImageRootSeeder : IImageRootSeeder
{
    private readonly IImageRootRepository _roots;
    private readonly IFolderRepository _folders;
    private readonly IClock _clock;
    private readonly ImageRootsOptions _options;
    private readonly ILogger<ImageRootSeeder> _logger;

    public ImageRootSeeder(IImageRootRepository roots, IFolderRepository folders, IClock clock, ImageRootsOptions options, ILogger<ImageRootSeeder> logger)
    {
        _roots = roots;
        _folders = folders;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var roots = (await _roots.GetAllAsync(cancellationToken)).ToList();
        var configuredMountPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in _options.Entries)
        {
            var name = entry.Name?.Trim();
            var mountPath = entry.MountPath?.Trim();
            if (string.IsNullOrEmpty(mountPath) || RootNameRules.Validate(name) is not null)
            {
                _logger.LogWarning(
                    "Skipping ImageRoots entry (Name: {Name}, MountPath: {MountPath}): a MountPath and a valid Name are required.",
                    entry.Name, entry.MountPath);
                continue;
            }

            configuredMountPaths.Add(mountPath);
            if (roots.Any(r => r.MountPath == mountPath))
                continue;

            var alias = string.IsNullOrWhiteSpace(entry.Alias) ? null : entry.Alias.Trim();
            if (alias is not null && (RootNameRules.Validate(alias) is not null || SegmentTaken(roots, alias)))
            {
                _logger.LogWarning(
                    "Ignoring alias {Alias} for new ImageRoot {Name}: it is not a valid path segment or another root already exports under it.",
                    alias, name);
                alias = null;
            }

            if (roots.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
                || SegmentTaken(roots, alias ?? name!))
            {
                _logger.LogWarning(
                    "Skipping ImageRoots entry {Name} ({MountPath}): another root already uses that name or export segment.",
                    name, mountPath);
                continue;
            }

            var created = await _roots.AddAsync(new ImageRoot
            {
                Name = name!,
                Alias = alias,
                MountPath = mountPath,
                IsActive = true,
                CreatedUtc = _clock.UtcNow
            }, cancellationToken);
            roots.Add(created);
        }

        // Every root gets its top folder now, so it shows in the tree (not yet discovered) before any discovery
        // or scan. Inactive roots too: their folders are hidden anyway, and re-activating one then needs no restart.
        foreach (var root in roots)
        {
            if (await _folders.GetByRootAndRelativePathAsync(root.Id, string.Empty, cancellationToken) is not null)
                continue;

            await _folders.AddAsync(new Folder
            {
                RootId = root.Id,
                ParentId = null,
                Name = root.Name,
                RelativePath = string.Empty,
                CreatedUtc = _clock.UtcNow,
                ModifiedUtc = _clock.UtcNow
            }, cancellationToken);
        }

        foreach (var root in roots.Where(r => !configuredMountPaths.Contains(r.MountPath)))
        {
            _logger.LogWarning(
                "ImageRoot {Name} ({MountPath}) is not in the ImageRoots configuration; it is left unchanged.",
                root.Name, root.MountPath);
        }
    }

    private static bool SegmentTaken(IEnumerable<ImageRoot> roots, string segment) =>
        roots.Any(r => string.Equals(RootNameRules.ExportSegment(r), segment, StringComparison.OrdinalIgnoreCase));
}
