using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Repositories;

/// <summary>Counts are distinct images: Confirmed, and Suggested-but-not-yet-confirmed for that person.</summary>
public sealed record PersonSummary(int Id, string? Name, int ConfirmedImageCount, int SuggestedImageCount, int? CoverFaceId);

public sealed record FaceCropSource(
    int FaceId, string ContentHash, string MountPath, string RelativePath, string FileName, string Extension, int? Orientation,
    float X, float Y, float Width, float Height);

public interface IPeopleRepository
{
    /// <summary>Named people (even with no faces) and unnamed groups with at least one face; largest first.</summary>
    Task<IReadOnlyList<PersonSummary>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PersonSummary?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<int?> FindIdByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Sets the name only; the person's Suggested faces stay Suggested. False if the person doesn't exist.</summary>
    Task<bool> SetNameAsync(int id, string name, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the face the person's cover. False (nothing changed) if the person doesn't exist or the face is not one of
    /// their Suggested/Confirmed faces on a visible photo.
    /// </summary>
    Task<bool> SetCoverAsync(int id, int faceId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Moves every Suggested/Confirmed face of source to target, keeping its state, then deletes source.</summary>
    Task MergeAsync(int sourceId, int targetId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the Suggested faces of the unnamed group source to target as Confirmed, then deletes source.
    /// With imageIds only the faces on those photos move, and source is deleted only once it has no faces left.
    /// </summary>
    Task AssignGroupAsync(int sourceId, int targetId, DateTime nowUtc, IReadOnlyCollection<int>? imageIds = null, CancellationToken cancellationToken = default);

    /// <summary>Forgets the person: their faces go back to Unknown (and unclustered, so they can regroup), then the person is deleted. False if missing.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Ignores every face of the group (never suggested or grouped again), then deletes the group. Returns the face count, or null if missing.</summary>
    Task<int?> IgnoreGroupAsync(int id, CancellationToken cancellationToken = default);

    Task<FaceCropSource?> GetFaceCropSourceAsync(int faceId, CancellationToken cancellationToken = default);
}
