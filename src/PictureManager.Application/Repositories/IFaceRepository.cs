using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Faces;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public sealed record FaceFailure(int ImageId, string FileName, string Extension, int Attempts, string? ErrorMessage, DateTime ProcessedUtc);

public sealed record FaceCandidate(int Id, float Quality);
public sealed record FaceNeighbor(int FaceId, int? PersonId, float Distance);

/// <summary>Assigned = has a person, Auto or Confirmed. Unassigned = no person yet. Rejected faces are in neither.</summary>
public enum NeighborPool
{
    Assigned,
    Unassigned
}

public interface IFaceRepository
{
    /// <summary>The FaceModel row for this descriptor (matched by ModelHash), created on first use.</summary>
    Task<int> GetOrCreateModelIdAsync(FaceModelDescriptor model, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids (ascending) of visible Indexed images in scope that still need analysis by this model: no state, a
    /// state for another model or an older ContentHash, or a retryable Failed state. folderId null = everything;
    /// an unknown folderId = nothing.
    /// </summary>
    Task<IReadOnlyList<int>> GetCandidateImageIdsAsync(int faceModelId, int? folderId, bool isRecursive, CancellationToken cancellationToken = default);

    Task<FaceProcessingState?> GetStateAsync(int imageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// In one transaction: replaces all of the image's faces with these and records Completed state. A new face
    /// whose box overlaps an old face (IoU ≥ 0.5, greedy one-to-one, see FaceMatching) keeps that face's PersonId
    /// and AssignmentState; other new faces are Unassigned. False when the image no longer exists (deleted while
    /// the job ran).
    /// </summary>
    Task<bool> SaveResultAsync(int imageId, int faceModelId, string fingerprint, IReadOnlyList<DetectedFace> faces, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task SaveFailureAsync(int imageId, int faceModelId, string fingerprint, FaceProcessingStatus status, int attempts, string? errorMessage, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FaceFailure>> GetPermanentFailuresAsync(int faceModelId, CancellationToken cancellationToken = default);

    /// <summary>Unassigned faces of this model with QualityScore ≥ minQuality, best quality first.</summary>
    Task<IReadOnlyList<FaceCandidate>> GetUnassignedFacesAsync(int faceModelId, float minQuality, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unassigned faces of this model with QualityScore ≥ minQuality that no clustering pass has used as a seed yet
    /// (ClusteredUtc null), best quality first.
    /// </summary>
    Task<IReadOnlyList<FaceCandidate>> GetUnclusteredFacesAsync(int faceModelId, float minQuality, CancellationToken cancellationToken = default);

    /// <summary>Sets ClusteredUtc on these faces so later passes no longer use them as seeds.</summary>
    Task MarkClusteredAsync(IReadOnlyCollection<int> faceIds, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// The k nearest faces (cosine distance) of the same model in the given pool with QualityScore ≥ minQuality,
    /// nearest first, excluding faceId. The filters apply before the limit (filtered-out faces never fill the k slots).
    /// </summary>
    Task<IReadOnlyList<FaceNeighbor>> GetNearestAsync(
        int faceId, int faceModelId, NeighborPool pool, int k, float minQuality = 0f, CancellationToken cancellationToken = default);

    /// <summary>Sets PersonId and Auto on those of these faces that are still Unassigned. Never touches user decisions.</summary>
    Task AssignAsync(IReadOnlyCollection<int> faceIds, int personId, CancellationToken cancellationToken = default);

    Task<int> CreateUnnamedPersonAsync(int coverFaceId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Deletes unnamed people with no faces left. Named people are kept even when empty. Returns how many.</summary>
    Task<int> DeleteEmptyUnnamedPeopleAsync(CancellationToken cancellationToken = default);
}
