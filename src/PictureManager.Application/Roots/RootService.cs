using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PictureManager.Application.Common;
using PictureManager.Application.Discovery;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Roots;

public sealed class RootService : IRootService
{
    private readonly IImageRootRepository _roots;
    private readonly IFolderRepository _folders;
    private readonly IClock _clock;
    private readonly IDiscoveryService _discovery;
    private readonly ILogger<RootService> _logger;

    public RootService(
        IImageRootRepository roots, IFolderRepository folders, IClock clock, IDiscoveryService discovery, ILogger<RootService> logger)
    {
        _roots = roots;
        _folders = folders;
        _clock = clock;
        _discovery = discovery;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RootSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var roots = await _roots.GetAllAsync(cancellationToken);
        return roots.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Select(ToSummary).ToList();
    }

    public async Task<Result<RootSummary>> UpdateAsync(int id, RootUpdate update, CancellationToken cancellationToken = default)
    {
        var roots = await _roots.GetAllAsync(cancellationToken);
        var root = roots.FirstOrDefault(r => r.Id == id);
        if (root is null)
            return Result.NotFound();

        var name = root.Name;
        if (update.Name is not null)
        {
            name = update.Name.Trim();
            if (RootNameRules.Validate(name) is { } nameError)
                return Result.Invalid("name", nameError);
        }

        var alias = root.Alias;
        if (update.AliasSpecified)
        {
            alias = update.Alias?.Trim();
            if (alias is not null && RootNameRules.Validate(alias) is { } aliasError)
                return Result.Invalid("alias", aliasError);
        }

        var others = roots.Where(r => r.Id != id).ToList();
        if (others.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
            return Result.Conflict($"Another root is already named '{name}'.");

        var segment = alias ?? name;
        if (others.Any(r => string.Equals(RootNameRules.ExportSegment(r), segment, StringComparison.OrdinalIgnoreCase)))
            return Result.Conflict($"Another root already exports under '{segment}'.");

        var renamed = !string.Equals(root.Name, name, StringComparison.Ordinal);
        root.Name = name;
        root.Alias = alias;
        if (update.IsActive is bool isActive)
            root.IsActive = isActive;

        await _roots.UpdateAsync(root, cancellationToken);
        if (renamed)
            await _folders.RenameRootFolderAsync(root.Id, name, cancellationToken);

        return Result<RootSummary>.Ok(ToSummary(root));
    }

    public async Task<Result<RootSummary>> CreateAsync(RootCreate input, CancellationToken cancellationToken = default)
    {
        var mountPath = input.MountPath?.Trim();
        if (string.IsNullOrWhiteSpace(mountPath))
            return Result.Invalid("mountPath", "Must not be blank.");

        var name = input.Name?.Trim();
        if (RootNameRules.Validate(name) is { } nameError)
            return Result.Invalid("name", nameError);

        var alias = string.IsNullOrWhiteSpace(input.Alias) ? null : input.Alias.Trim();
        if (alias is not null && RootNameRules.Validate(alias) is { } aliasError)
            return Result.Invalid("alias", aliasError);

        var roots = await _roots.GetAllAsync(cancellationToken);
        if (roots.Any(r => string.Equals(r.MountPath, mountPath, StringComparison.Ordinal)))
            return Result.Conflict($"A root already exists at '{mountPath}'.");
        if (roots.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
            return Result.Conflict($"Another root is already named '{name}'.");

        var segment = alias ?? name;
        if (roots.Any(r => string.Equals(RootNameRules.ExportSegment(r), segment, StringComparison.OrdinalIgnoreCase)))
            return Result.Conflict($"Another root already exports under '{segment}'.");

        var now = _clock.UtcNow;
        var created = await _roots.AddAsync(new ImageRoot
        {
            Name = name!,
            Alias = alias,
            MountPath = mountPath,
            IsActive = true,
            CreatedUtc = now
        }, cancellationToken);

        // Every root needs its top folder to show in the tree before any discovery/scan, per ImageRootSeeder.
        await _folders.AddAsync(new Folder
        {
            RootId = created.Id,
            ParentId = null,
            Name = created.Name,
            RelativePath = string.Empty,
            CreatedUtc = now,
            ModifiedUtc = now
        }, cancellationToken);

        try
        {
            await _discovery.QueueDiscoveryAsync(created.Id, null, isRecursive: true, cancellationToken);
        }
        catch (DiscoveryAlreadyInProgressException)
        {
            // The root is still created; the admin can trigger "Refresh structure" once the current job finishes.
            _logger.LogWarning(
                "Could not auto-discover new root {Name} ({MountPath}): another scan or discovery is already in progress.",
                created.Name, created.MountPath);
        }

        return Result<RootSummary>.Ok(ToSummary(created));
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var root = await _roots.GetByIdAsync(id, cancellationToken);
        if (root is null)
            return Result.NotFound();

        // The top folder's row is deleted directly; the database cascades to every folder and image
        // beneath it (Folder.ParentId -> Cascade, Image.FolderId -> Cascade), which in turn frees the
        // Folder.RootId -> Restrict FK below.
        var topFolder = await _folders.GetByRootAndRelativePathAsync(id, string.Empty, cancellationToken);
        if (topFolder is not null)
            await _folders.DeleteSubtreeAsync(topFolder.Id, cancellationToken);

        await _roots.DeleteAsync(id, cancellationToken);
        return Result.Ok();
    }

    private static RootSummary ToSummary(ImageRoot root) =>
        new(root.Id, root.Name, root.Alias, root.MountPath, root.IsActive, RootNameRules.ExportSegment(root));
}
