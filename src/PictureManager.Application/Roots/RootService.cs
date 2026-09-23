using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Roots;

public sealed class RootService : IRootService
{
    private readonly IImageRootRepository _roots;
    private readonly IFolderRepository _folders;

    public RootService(IImageRootRepository roots, IFolderRepository folders)
    {
        _roots = roots;
        _folders = folders;
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

    private static RootSummary ToSummary(ImageRoot root) =>
        new(root.Id, root.Name, root.Alias, root.MountPath, root.IsActive, RootNameRules.ExportSegment(root));
}
