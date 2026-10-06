using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

/// <summary>One detected face of an image. The box is normalized (0-1) against the orientation-corrected image.</summary>
public sealed record ImageFace(
    int Id, float X, float Y, float Width, float Height, FaceAssignmentState State, int? PersonId, string? PersonName);

/// <summary>
/// The user's decisions about detected faces. Every method touches only faces in the state it is meant for, so a
/// repeated or stale request changes nothing; the int results are the number of faces changed.
/// </summary>
public interface IFaceReviewRepository
{
    /// <summary>Faces of a visible image, left to right; null when the image doesn't exist or isn't visible. Ignored faces only on request.</summary>
    Task<IReadOnlyList<ImageFace>?> GetImageFacesAsync(int imageId, bool includeIgnored, CancellationToken cancellationToken = default);

    Task<bool> FaceExistsAsync(int faceId, CancellationToken cancellationToken = default);
    Task<bool> PersonExistsAsync(int personId, CancellationToken cancellationToken = default);
    Task<int> CreateNamedPersonAsync(string name, int coverFaceId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Suggested → Confirmed.</summary>
    Task<int> AcceptAsync(int faceId, CancellationToken cancellationToken = default);

    /// <summary>Suggested → Unknown, remembering the rejected person so it is never suggested for this face again.</summary>
    Task<int> RejectAsync(int faceId, CancellationToken cancellationToken = default);

    /// <summary>Suggested or Confirmed → Unknown, remembering the person like a rejection.</summary>
    Task<int> MarkUnknownAsync(int faceId, CancellationToken cancellationToken = default);

    /// <summary>Unknown, Suggested or Confirmed → Confirmed for personId. Leaving a suggested person counts as rejecting them.</summary>
    Task<int> AssignAsync(int faceId, int personId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Unknown, Suggested or Confirmed → Ignored (no person, nothing remembered).</summary>
    Task<int> IgnoreAsync(int faceId, CancellationToken cancellationToken = default);

    /// <summary>Ignored → Unknown.</summary>
    Task<int> RestoreAsync(int faceId, CancellationToken cancellationToken = default);

    /// <summary>Accepts every Suggested face of the person.</summary>
    Task<int> AcceptAllAsync(int personId, CancellationToken cancellationToken = default);

    /// <summary>Accepts the person's Suggested faces in one image.</summary>
    Task<int> AcceptImageAsync(int personId, int imageId, CancellationToken cancellationToken = default);

    /// <summary>Rejects the person's Suggested faces in one image.</summary>
    Task<int> RejectImageAsync(int personId, int imageId, CancellationToken cancellationToken = default);
}
