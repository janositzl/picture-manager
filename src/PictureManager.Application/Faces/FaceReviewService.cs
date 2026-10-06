using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Faces;

public sealed class FaceReviewService : IFaceReviewService
{
    private const int MaxNameLength = 200;

    private readonly IFaceReviewRepository _review;
    private readonly IPeopleRepository _people;
    private readonly IClock _clock;

    public FaceReviewService(IFaceReviewRepository review, IPeopleRepository people, IClock clock)
    {
        _review = review;
        _people = people;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<ImageFaceDto>>> GetImageFacesAsync(
        int imageId, bool includeIgnored, bool confirmedOnly, CancellationToken cancellationToken = default)
    {
        var faces = await _review.GetImageFacesAsync(imageId, includeIgnored && !confirmedOnly, cancellationToken);
        if (faces is null)
            return Result.NotFound();

        IReadOnlyList<ImageFaceDto> dtos = faces
            .Where(f => !confirmedOnly || f.State == FaceAssignmentState.Confirmed)
            .Select(f => new ImageFaceDto(f.Id, f.X, f.Y, f.Width, f.Height, f.State.ToString().ToLowerInvariant(), f.PersonId, f.PersonName))
            .ToList();
        return Result<IReadOnlyList<ImageFaceDto>>.Ok(dtos);
    }

    public Task<Result> AcceptAsync(int faceId, CancellationToken cancellationToken = default) =>
        OnFaceAsync(faceId, _review.AcceptAsync, cancellationToken);

    public Task<Result> RejectAsync(int faceId, CancellationToken cancellationToken = default) =>
        OnFaceAsync(faceId, _review.RejectAsync, cancellationToken);

    public Task<Result> MarkUnknownAsync(int faceId, CancellationToken cancellationToken = default) =>
        OnFaceAsync(faceId, _review.MarkUnknownAsync, cancellationToken);

    public Task<Result> IgnoreAsync(int faceId, CancellationToken cancellationToken = default) =>
        OnFaceAsync(faceId, _review.IgnoreAsync, cancellationToken);

    public Task<Result> RestoreAsync(int faceId, CancellationToken cancellationToken = default) =>
        OnFaceAsync(faceId, _review.RestoreAsync, cancellationToken);

    public async Task<Result<PersonSummary>> AssignAsync(int faceId, AssignFaceRequest request, CancellationToken cancellationToken = default)
    {
        if (request.PersonId is null == string.IsNullOrWhiteSpace(request.Name))
            return Result.Invalid("personId", "Give either a person or a name.");

        if (!await _review.FaceExistsAsync(faceId, cancellationToken))
            return Result.NotFound();

        int personId;
        if (request.PersonId is int id)
        {
            if (!await _review.PersonExistsAsync(id, cancellationToken))
                return Result.Invalid("personId", "That person doesn't exist.");
            personId = id;
        }
        else
        {
            var name = request.Name!.Trim();
            if (name.Length > MaxNameLength)
                return Result.Invalid("name", $"Names can be at most {MaxNameLength} characters.");
            // An existing name means that person: assigning a face never merges anyone.
            personId = await _people.FindIdByNameAsync(name, cancellationToken)
                       ?? await _review.CreateNamedPersonAsync(name, faceId, _clock.UtcNow, cancellationToken);
        }

        await _review.AssignAsync(faceId, personId, _clock.UtcNow, cancellationToken);
        return await _people.GetAsync(personId, cancellationToken) is { } person ? Result<PersonSummary>.Ok(person) : Result.NotFound();
    }

    public async Task<Result<CountResponse>> AcceptAllAsync(int personId, CancellationToken cancellationToken = default) =>
        await _review.PersonExistsAsync(personId, cancellationToken)
            ? Result<CountResponse>.Ok(new CountResponse(await _review.AcceptAllAsync(personId, cancellationToken)))
            : Result.NotFound();

    public Task<Result> AcceptImageAsync(int personId, int imageId, CancellationToken cancellationToken = default) =>
        OnPersonAsync(personId, () => _review.AcceptImageAsync(personId, imageId, cancellationToken), cancellationToken);

    public Task<Result> RejectImageAsync(int personId, int imageId, CancellationToken cancellationToken = default) =>
        OnPersonAsync(personId, () => _review.RejectImageAsync(personId, imageId, cancellationToken), cancellationToken);

    // A face that is in another state than the action expects is not an error: the action just changes nothing.
    private async Task<Result> OnFaceAsync(int faceId, Func<int, CancellationToken, Task<int>> action, CancellationToken cancellationToken)
    {
        if (await action(faceId, cancellationToken) > 0)
            return Result.Ok();
        return await _review.FaceExistsAsync(faceId, cancellationToken) ? Result.Ok() : Result.NotFound();
    }

    private async Task<Result> OnPersonAsync(int personId, Func<Task<int>> action, CancellationToken cancellationToken)
    {
        if (!await _review.PersonExistsAsync(personId, cancellationToken))
            return Result.NotFound();
        await action();
        return Result.Ok();
    }
}
