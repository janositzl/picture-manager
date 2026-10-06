using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class PeopleRepository : IPeopleRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public PeopleRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Counts are distinct images, not faces. An image with both a Confirmed and a Suggested face of the same person
    // counts as Confirmed only. The stored CoverFaceId goes stale (re-processing, image deleted, faces merged away),
    // so it is used only while it is still one of the person's Suggested/Confirmed faces; otherwise the best face:
    // Confirmed before Suggested, then quality, then lowest Id.
    private static IQueryable<PersonSummary> Summaries(IQueryable<Person> people) =>
        people.Select(p => new PersonSummary(
            p.Id,
            p.Name,
            p.Faces.Where(f => f.AssignmentState == FaceAssignmentState.Confirmed).Select(f => f.ImageId).Distinct().Count(),
            p.Faces.Where(f => f.AssignmentState == FaceAssignmentState.Suggested
                               && !p.Faces.Any(c => c.ImageId == f.ImageId && c.AssignmentState == FaceAssignmentState.Confirmed))
                .Select(f => f.ImageId).Distinct().Count(),
            p.Faces.Any(f => f.Id == p.CoverFaceId
                             && (f.AssignmentState == FaceAssignmentState.Suggested || f.AssignmentState == FaceAssignmentState.Confirmed))
                ? p.CoverFaceId
                : p.Faces.Where(f => f.AssignmentState == FaceAssignmentState.Suggested || f.AssignmentState == FaceAssignmentState.Confirmed)
                    .OrderByDescending(f => f.AssignmentState == FaceAssignmentState.Confirmed)
                    .ThenByDescending(f => f.QualityScore).ThenBy(f => f.Id)
                    .Select(f => (int?)f.Id)
                    .FirstOrDefault()));

    public async Task<IReadOnlyList<PersonSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // Filter on the entity (a constructor projection can't be filtered on), then order the small result in memory.
        var summaries = await Summaries(_dbContext.People.AsNoTracking()
                .Where(p => p.Name != null
                            || p.Faces.Any(f => f.AssignmentState == FaceAssignmentState.Suggested || f.AssignmentState == FaceAssignmentState.Confirmed)))
            .ToListAsync(cancellationToken);
        return summaries.OrderByDescending(s => s.ConfirmedImageCount).ThenByDescending(s => s.SuggestedImageCount).ThenBy(s => s.Id).ToList();
    }

    public Task<PersonSummary?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        Summaries(_dbContext.People.AsNoTracking().Where(p => p.Id == id)).FirstOrDefaultAsync(cancellationToken);

    public async Task<int?> FindIdByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var lowered = name.ToLower();
        return await _dbContext.People.AsNoTracking()
            .Where(p => p.Name != null && p.Name.ToLower() == lowered)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // Naming only labels the person: the AI-grouped faces stay Suggested until the user accepts them.
    public async Task<bool> SetNameAsync(int id, string name, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.People.Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, name).SetProperty(p => p.ModifiedUtc, nowUtc), cancellationToken);
        return rows > 0;
    }

    // Faces keep their state: a merge neither confirms nor un-confirms anything.
    public async Task MergeAsync(int sourceId, int targetId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.Faces
            .Where(f => f.PersonId == sourceId
                        && (f.AssignmentState == FaceAssignmentState.Suggested || f.AssignmentState == FaceAssignmentState.Confirmed))
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.PersonId, targetId), cancellationToken);
        await _dbContext.Faces.Where(f => f.RejectedPersonId == sourceId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.RejectedPersonId, (int?)targetId), cancellationToken);
        await _dbContext.People.Where(p => p.Id == targetId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ModifiedUtc, nowUtc), cancellationToken);
        await _dbContext.People.Where(p => p.Id == sourceId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<FaceCropSource?> GetFaceCropSourceAsync(int faceId, CancellationToken cancellationToken = default) =>
        _dbContext.Faces.AsNoTracking()
            .Where(f => f.Id == faceId)
            .Select(f => new FaceCropSource(
                f.Id, f.Image!.ContentHash, f.Image.Folder!.Root!.MountPath, f.Image.Folder.RelativePath,
                f.Image.FileName, f.Image.Extension, f.Image.Orientation, f.X, f.Y, f.Width, f.Height))
            .FirstOrDefaultAsync(cancellationToken);
}
