using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Repositories;

/// <summary>FaceCount/PhotoCount count Auto and Confirmed faces only.</summary>
public sealed record PersonSummary(int Id, string? Name, int FaceCount, int PhotoCount, int? CoverFaceId);

public sealed record FaceCropSource(
    int FaceId, string ContentHash, string MountPath, string RelativePath, string FileName, string Extension, int? Orientation,
    float X, float Y, float Width, float Height);

public interface IPeopleRepository
{
    /// <summary>Named people (even with no faces) and unnamed groups with at least one face; largest first.</summary>
    Task<IReadOnlyList<PersonSummary>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PersonSummary?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<int?> FindIdByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Sets the name and turns the person's Auto faces into Confirmed. False if the person doesn't exist.</summary>
    Task<bool> SetNameAsync(int id, string name, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Moves every Auto/Confirmed face of source to target as Confirmed, then deletes source.</summary>
    Task MergeAsync(int sourceId, int targetId, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<FaceCropSource?> GetFaceCropSourceAsync(int faceId, CancellationToken cancellationToken = default);
}
