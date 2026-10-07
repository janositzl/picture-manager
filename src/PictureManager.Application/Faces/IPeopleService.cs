using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Faces;

public interface IPeopleService
{
    Task<IReadOnlyList<PersonSummary>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Result<PersonSummary>> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Names a person or group (trimmed, 1-200 chars). If another person already has that name (case-insensitive),
    /// this one is merged into them and the surviving person is returned.
    /// </summary>
    Task<Result<PersonSummary>> NameAsync(int id, string? name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges an unnamed group into a named person, confirming the group's faces; with imageIds only those on the
    /// given photos (the group stays while it has faces left). Returns the target.
    /// </summary>
    Task<Result<PersonSummary>> AssignGroupAsync(int id, int targetId, IReadOnlyCollection<int>? imageIds = null, CancellationToken cancellationToken = default);

    /// <summary>Chooses the face shown for a person or group; it must be one of their Suggested/Confirmed faces on a visible photo.</summary>
    Task<Result<PersonSummary>> SetCoverAsync(int id, int? faceId, CancellationToken cancellationToken = default);

    /// <summary>Forgets a person or group: their faces become unassigned and may regroup on the next recognition run.</summary>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Ignores an unnamed group for good (its faces become Ignored). Named people can't be ignored.</summary>
    Task<Result<CountResponse>> IgnoreGroupAsync(int id, CancellationToken cancellationToken = default);
}
