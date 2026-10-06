using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Faces;

public sealed class PeopleService : IPeopleService
{
    private const int MaxNameLength = 200;

    private readonly IPeopleRepository _people;
    private readonly IClock _clock;

    public PeopleService(IPeopleRepository people, IClock clock)
    {
        _people = people;
        _clock = clock;
    }

    public Task<IReadOnlyList<PersonSummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _people.GetAllAsync(cancellationToken);

    public async Task<Result<PersonSummary>> GetAsync(int id, CancellationToken cancellationToken = default) =>
        await _people.GetAsync(id, cancellationToken) is { } person ? Result<PersonSummary>.Ok(person) : Result.NotFound();

    public async Task<Result<PersonSummary>> NameAsync(int id, string? name, CancellationToken cancellationToken = default)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return Result.Invalid("name", "Enter a name.");
        if (trimmed.Length > MaxNameLength)
            return Result.Invalid("name", $"Names can be at most {MaxNameLength} characters.");

        if (await _people.GetAsync(id, cancellationToken) is null)
            return Result.NotFound();

        var existingId = await _people.FindIdByNameAsync(trimmed, cancellationToken);
        if (existingId is int targetId && targetId != id)
        {
            await _people.MergeAsync(id, targetId, _clock.UtcNow, cancellationToken);
            return await GetAsync(targetId, cancellationToken);
        }

        await _people.SetNameAsync(id, trimmed, _clock.UtcNow, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<PersonSummary>> AssignGroupAsync(int id, int targetId, IReadOnlyCollection<int>? imageIds = null, CancellationToken cancellationToken = default)
    {
        if (imageIds is { Count: 0 })
            return Result.Invalid("imageIds", "Select at least one photo.");
        if (await _people.GetAsync(id, cancellationToken) is not { } group
            || await _people.GetAsync(targetId, cancellationToken) is not { } target)
            return Result.NotFound();
        if (group.Name is not null)
            return Result.Invalid("name", "Only unknown groups can be assigned to a person.");
        if (target.Name is null)
            return Result.Invalid("personId", "Choose a named person.");

        await _people.AssignGroupAsync(id, targetId, _clock.UtcNow, imageIds, cancellationToken);
        return await GetAsync(targetId, cancellationToken);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        await _people.DeleteAsync(id, cancellationToken) ? Result.Ok() : Result.NotFound();

    public async Task<Result<CountResponse>> IgnoreGroupAsync(int id, CancellationToken cancellationToken = default)
    {
        if (await _people.GetAsync(id, cancellationToken) is not { } person)
            return Result.NotFound();
        if (person.Name is not null)
            return Result.Invalid("name", "Only unknown groups can be ignored.");

        return await _people.IgnoreGroupAsync(id, cancellationToken) is int count
            ? Result<CountResponse>.Ok(new CountResponse(count))
            : Result.NotFound();
    }
}
