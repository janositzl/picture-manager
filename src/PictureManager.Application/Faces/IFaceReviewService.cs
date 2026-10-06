using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Faces;

/// <summary>A face as the API shows it. State is lower-case: unknown, suggested, confirmed or ignored.</summary>
public sealed record ImageFaceDto(int Id, float X, float Y, float Width, float Height, string State, int? PersonId, string? PersonName);

/// <summary>Who a face is assigned to: an existing person (PersonId) or a name (an existing person with that name, else a new one).</summary>
public sealed record AssignFaceRequest(int? PersonId, string? Name);

public sealed record CountResponse(int Count);

public interface IFaceReviewService
{
    /// <summary>NotFound when the image doesn't exist or isn't visible. confirmedOnly leaves out everything but Confirmed faces.</summary>
    Task<Result<IReadOnlyList<ImageFaceDto>>> GetImageFacesAsync(int imageId, bool includeIgnored, bool confirmedOnly, CancellationToken cancellationToken = default);

    Task<Result> AcceptAsync(int faceId, CancellationToken cancellationToken = default);
    Task<Result> RejectAsync(int faceId, CancellationToken cancellationToken = default);
    Task<Result> MarkUnknownAsync(int faceId, CancellationToken cancellationToken = default);
    Task<Result> IgnoreAsync(int faceId, CancellationToken cancellationToken = default);
    Task<Result> RestoreAsync(int faceId, CancellationToken cancellationToken = default);

    /// <summary>Confirms the face for a person and returns that person.</summary>
    Task<Result<PersonSummary>> AssignAsync(int faceId, AssignFaceRequest request, CancellationToken cancellationToken = default);

    Task<Result<CountResponse>> AcceptAllAsync(int personId, CancellationToken cancellationToken = default);
    Task<Result> AcceptImageAsync(int personId, int imageId, CancellationToken cancellationToken = default);
    Task<Result> RejectImageAsync(int personId, int imageId, CancellationToken cancellationToken = default);
}
