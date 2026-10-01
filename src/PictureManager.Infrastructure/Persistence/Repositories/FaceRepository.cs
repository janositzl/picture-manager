using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Infrastructure.Persistence.Queries;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class FaceRepository : IFaceRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public FaceRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> GetOrCreateModelIdAsync(FaceModelDescriptor model, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.FaceModels.AsNoTracking()
            .Where(m => m.ModelHash == model.ModelHash).Select(m => (int?)m.Id).FirstOrDefaultAsync(cancellationToken);
        if (existing is int id)
            return id;

        var row = new FaceModel
        {
            Name = model.Name, Version = model.Version, EmbeddingDimensions = model.EmbeddingDimensions,
            ModelHash = model.ModelHash, CreatedUtc = nowUtc
        };
        _dbContext.FaceModels.Add(row);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<IReadOnlyList<int>> GetCandidateImageIdsAsync(int faceModelId, int? folderId, bool isRecursive, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Images.AsNoTracking().WhereVisible().Where(i => i.IndexState == IndexState.Indexed);

        if (folderId is int id)
        {
            var folder = await _dbContext.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
            if (folder is null)
                return Array.Empty<int>();

            if (!isRecursive)
                query = query.Where(i => i.FolderId == id);
            else if (folder.RelativePath.Length == 0)
                query = query.Where(i => i.Folder!.RootId == folder.RootId);
            else
            {
                var prefix = folder.RelativePath + "/";
                query = query.Where(i => i.Folder!.RootId == folder.RootId
                                         && (i.FolderId == id || i.Folder.RelativePath.StartsWith(prefix)));
            }
        }

        // Done = a Completed or PermanentlyFailed state for this model AND this exact content.
        query = query.Where(i => !_dbContext.FaceProcessingStates.Any(s =>
            s.ImageId == i.Id && s.FaceModelId == faceModelId && s.ImageFingerprint == i.ContentHash
            && s.Status != FaceProcessingStatus.Failed));

        return await query.OrderBy(i => i.Id).Select(i => i.Id).ToListAsync(cancellationToken);
    }

    public Task<FaceProcessingState?> GetStateAsync(int imageId, CancellationToken cancellationToken = default) =>
        _dbContext.FaceProcessingStates.AsNoTracking().FirstOrDefaultAsync(s => s.ImageId == imageId, cancellationToken);

    public async Task<bool> SaveResultAsync(
        int imageId, int faceModelId, string fingerprint, IReadOnlyList<DetectedFace> faces, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await _dbContext.Images.AnyAsync(i => i.Id == imageId, cancellationToken))
            return false;

        await _dbContext.Faces.Where(f => f.ImageId == imageId).ExecuteDeleteAsync(cancellationToken);
        _dbContext.Faces.AddRange(faces.Select(f => new Face
        {
            ImageId = imageId,
            FaceModelId = faceModelId,
            X = f.X, Y = f.Y, Width = f.Width, Height = f.Height,
            DetectionConfidence = f.DetectionConfidence,
            QualityScore = f.QualityScore,
            Embedding = new Vector(f.Embedding),
            CreatedUtc = nowUtc
        }));
        await UpsertStateAsync(imageId, faceModelId, fingerprint, FaceProcessingStatus.Completed, 0, null, nowUtc, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // Deleted between the existence check and the insert.
            _dbContext.ChangeTracker.Clear();
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
        return true;
    }

    public async Task SaveFailureAsync(
        int imageId, int faceModelId, string fingerprint, FaceProcessingStatus status, int attempts, string? errorMessage, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await UpsertStateAsync(imageId, faceModelId, fingerprint, status, attempts, errorMessage, nowUtc, cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // Image deleted meanwhile: nothing to record.
        }
        _dbContext.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<FaceFailure>> GetPermanentFailuresAsync(int faceModelId, CancellationToken cancellationToken = default) =>
        await _dbContext.FaceProcessingStates.AsNoTracking()
            .Where(s => s.FaceModelId == faceModelId && s.Status == FaceProcessingStatus.PermanentlyFailed)
            .OrderByDescending(s => s.ProcessedUtc)
            .Select(s => new FaceFailure(s.ImageId, s.Image!.FileName, s.Image.Extension, s.Attempts, s.ErrorMessage, s.ProcessedUtc))
            .ToListAsync(cancellationToken);

    private async Task UpsertStateAsync(
        int imageId, int faceModelId, string fingerprint, FaceProcessingStatus status, int attempts, string? errorMessage, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var state = await _dbContext.FaceProcessingStates.FirstOrDefaultAsync(s => s.ImageId == imageId, cancellationToken);
        if (state is null)
        {
            state = new FaceProcessingState { ImageId = imageId };
            _dbContext.FaceProcessingStates.Add(state);
        }

        state.FaceModelId = faceModelId;
        state.ImageFingerprint = fingerprint;
        state.Status = status;
        state.Attempts = attempts;
        state.ErrorMessage = errorMessage;
        state.ProcessedUtc = nowUtc;
    }
}
