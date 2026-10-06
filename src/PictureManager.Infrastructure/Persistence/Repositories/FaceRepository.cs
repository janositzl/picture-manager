using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Infrastructure.Persistence.Queries;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class FaceRepository : IFaceRepository
{
    /// <summary>
    /// For kNN queries (pgvector ≥ 0.8): keep scanning the HNSW index until enough rows pass the WHERE filters, in
    /// exact distance order; ef_search 100 is at least twice the largest k the clusterer asks for (32).
    /// </summary>
    private const string KnnSessionSettings =
        "SET LOCAL hnsw.iterative_scan = strict_order; SET LOCAL hnsw.ef_search = 100";

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

        // A new face that overlaps an old one is the same face: it keeps the person and the user's decision.
        var previous = await _dbContext.Faces.AsNoTracking()
            .Where(f => f.ImageId == imageId)
            .OrderBy(f => f.Id)
            .Select(f => new { f.PersonId, f.AssignmentState, f.RejectedPersonId, f.MatchDistance, f.X, f.Y, f.Width, f.Height })
            .ToListAsync(cancellationToken);
        var matches = FaceMatching.MatchByOverlap(
            previous.Select(p => new FaceBox(p.X, p.Y, p.Width, p.Height)).ToList(),
            faces.Select(f => new FaceBox(f.X, f.Y, f.Width, f.Height)).ToList());

        await _dbContext.Faces.Where(f => f.ImageId == imageId).ExecuteDeleteAsync(cancellationToken);
        _dbContext.Faces.AddRange(faces.Select((f, index) =>
        {
            var inherited = matches.TryGetValue(index, out var match) ? previous[match] : null;
            return new Face
            {
                ImageId = imageId,
                FaceModelId = faceModelId,
                PersonId = inherited?.PersonId,
                AssignmentState = inherited?.AssignmentState ?? FaceAssignmentState.Unknown,
                RejectedPersonId = inherited?.RejectedPersonId,
                MatchDistance = inherited?.MatchDistance,
                X = f.X, Y = f.Y, Width = f.Width, Height = f.Height,
                DetectionConfidence = f.DetectionConfidence,
                QualityScore = f.QualityScore,
                Embedding = new Vector(f.Embedding),
                CreatedUtc = nowUtc
            };
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

    public async Task<IReadOnlyList<FaceCandidate>> GetUnassignedFacesAsync(int faceModelId, float minQuality, CancellationToken cancellationToken = default) =>
        await _dbContext.Faces.AsNoTracking()
            .Where(f => f.FaceModelId == faceModelId && f.AssignmentState == FaceAssignmentState.Unknown && f.QualityScore >= minQuality)
            .OrderByDescending(f => f.QualityScore)
            .Select(f => new FaceCandidate(f.Id, f.QualityScore, f.RejectedPersonId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FaceCandidate>> GetUnclusteredFacesAsync(int faceModelId, float minQuality, CancellationToken cancellationToken = default) =>
        await _dbContext.Faces.AsNoTracking()
            .Where(f => f.FaceModelId == faceModelId && f.ClusteredUtc == null
                        && f.AssignmentState == FaceAssignmentState.Unknown && f.QualityScore >= minQuality)
            .OrderByDescending(f => f.QualityScore).ThenBy(f => f.Id)
            .Select(f => new FaceCandidate(f.Id, f.QualityScore, f.RejectedPersonId))
            .ToListAsync(cancellationToken);

    public async Task MarkClusteredAsync(IReadOnlyCollection<int> faceIds, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (faceIds.Count == 0)
            return;

        await _dbContext.Faces
            .Where(f => faceIds.Contains(f.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.ClusteredUtc, nowUtc), cancellationToken);
    }

    public async Task<IReadOnlyList<FaceNeighbor>> GetNearestAsync(
        int faceId, int faceModelId, NeighborPool pool, int k, float minQuality = 0f, CancellationToken cancellationToken = default)
    {
        var embedding = await _dbContext.Faces.AsNoTracking()
            .Where(f => f.Id == faceId).Select(f => f.Embedding).FirstOrDefaultAsync(cancellationToken);
        if (embedding is null)
            return Array.Empty<FaceNeighbor>();

        var faces = _dbContext.Faces.AsNoTracking()
            .Where(f => f.FaceModelId == faceModelId && f.Id != faceId && f.QualityScore >= minQuality);
        faces = pool == NeighborPool.Assigned
            ? faces.Where(f => f.PersonId != null
                               && (f.AssignmentState == FaceAssignmentState.Suggested || f.AssignmentState == FaceAssignmentState.Confirmed))
            : faces.Where(f => f.AssignmentState == FaceAssignmentState.Unknown);

        // The HNSW index scan yields only ef_search candidates and the filters run afterwards, so on a big table a
        // plain scan can return fewer than k (or no) rows. An iterative scan keeps scanning until k rows pass the
        // filters. SET LOCAL needs a transaction on this connection; it ends with it.
        var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        await _dbContext.Database.ExecuteSqlRawAsync(KnnSessionSettings, cancellationToken);

        var neighbors = await faces
            .OrderBy(f => f.Embedding.CosineDistance(embedding))
            .Take(k)
            .Select(f => new FaceNeighbor(f.Id, f.PersonId, (float)f.Embedding.CosineDistance(embedding)))
            .ToListAsync(cancellationToken);

        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return neighbors;
    }

    public async Task AssignAsync(IReadOnlyCollection<int> faceIds, int personId, float? matchDistance = null, CancellationToken cancellationToken = default)
    {
        await _dbContext.Faces
            .Where(f => faceIds.Contains(f.Id) && f.AssignmentState == FaceAssignmentState.Unknown && f.RejectedPersonId != personId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.PersonId, personId)
                .SetProperty(f => f.AssignmentState, FaceAssignmentState.Suggested)
                .SetProperty(f => f.MatchDistance, matchDistance),
                cancellationToken);
    }

    public async Task<int> CreateUnnamedPersonAsync(int coverFaceId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var person = new Person { CoverFaceId = coverFaceId, CreatedUtc = nowUtc, ModifiedUtc = nowUtc };
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
        return person.Id;
    }

    public Task<int> DeleteEmptyUnnamedPeopleAsync(CancellationToken cancellationToken = default) =>
        _dbContext.People
            .Where(p => p.Name == null && !_dbContext.Faces.Any(f => f.PersonId == p.Id))
            .ExecuteDeleteAsync(cancellationToken);
}
