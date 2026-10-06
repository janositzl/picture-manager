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

    /// <summary>Moves every Suggested/Confirmed face of source to target, keeping its state, then deletes source.</summary>
    Task MergeAsync(int sourceId, int targetId, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<FaceCropSource?> GetFaceCropSourceAsync(int faceId, CancellationToken cancellationToken = default);
}
