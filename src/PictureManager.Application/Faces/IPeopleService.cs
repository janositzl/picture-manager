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
}
