using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Folders;

public sealed class FolderService : IFolderService
{
    private readonly IFolderRepository _folders;
    private readonly IClock _clock;

    public FolderService(IFolderRepository folders, IClock clock)
    {
        _folders = folders;
        _clock = clock;
    }

    public Task<IReadOnlyList<FolderNode>> GetRootsAsync(CancellationToken cancellationToken = default) =>
        _folders.GetVisibleRootFoldersAsync(cancellationToken);

    public async Task<Result<IReadOnlyList<FolderNode>>> GetChildrenAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!await _folders.IsVisibleAsync(id, cancellationToken))
            return Result.NotFound();

        return Result<IReadOnlyList<FolderNode>>.Ok(await _folders.GetVisibleChildrenAsync(id, cancellationToken));
    }

    public async Task<Result<FolderDetail>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var detail = await _folders.GetVisibleDetailAsync(id, cancellationToken);
        return detail is null ? Result.NotFound() : Result<FolderDetail>.Ok(detail);
    }

    public Task<IReadOnlyList<RemovedFolder>> GetRemovedAsync(CancellationToken cancellationToken = default) =>
        _folders.GetRemovedAsync(cancellationToken);

    public async Task<Result> RemoveAsync(int id, CancellationToken cancellationToken = default)
    {
        var folder = await _folders.GetByIdAsync(id, cancellationToken);
        if (folder is null || !folder.IsActive)
            return Result.NotFound();
        if (folder.ParentId is null)
            return Result.Invalid("id", "A root's top folder cannot be removed; deactivate the root instead.");

        await _folders.RemoveFromCollectionAsync(id, cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> RestoreAsync(int id, CancellationToken cancellationToken = default)
    {
        var folder = await _folders.GetByIdAsync(id, cancellationToken);
        if (folder is null)
            return Result.NotFound();
        if (folder.IsActive)
            return Result.Invalid("id", "The folder has not been removed.");

        folder.IsActive = true;
        folder.ModifiedUtc = _clock.UtcNow;
        await _folders.UpdateAsync(folder, cancellationToken);
        return Result.Ok();
    }
}
