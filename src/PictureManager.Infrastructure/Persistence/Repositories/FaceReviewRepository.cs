using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Infrastructure.Persistence.Queries;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class FaceReviewRepository : IFaceReviewRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public FaceReviewRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ImageFace>?> GetImageFacesAsync(int imageId, bool includeIgnored, CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Images.AsNoTracking().WhereVisible().AnyAsync(i => i.Id == imageId, cancellationToken))
            return null;

        return await _dbContext.Faces.AsNoTracking()
            .Where(f => f.ImageId == imageId && (includeIgnored || f.AssignmentState != FaceAssignmentState.Ignored))
            .OrderBy(f => f.X).ThenBy(f => f.Id)
            .Select(f => new ImageFace(f.Id, f.X, f.Y, f.Width, f.Height, f.AssignmentState, f.PersonId, f.Person!.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecheckFace>?> GetUnknownFacesAsync(int imageId, CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Images.AsNoTracking().WhereVisible().AnyAsync(i => i.Id == imageId, cancellationToken))
            return null;

        return await _dbContext.Faces.AsNoTracking()
            .Where(f => f.ImageId == imageId && f.AssignmentState == FaceAssignmentState.Unknown)
            .OrderBy(f => f.Id)
            .Select(f => new RecheckFace(f.Id, f.FaceModelId, f.QualityScore, f.RejectedPersonId))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> FaceExistsAsync(int faceId, CancellationToken cancellationToken = default) =>
        _dbContext.Faces.AnyAsync(f => f.Id == faceId, cancellationToken);

    public Task<bool> PersonExistsAsync(int personId, CancellationToken cancellationToken = default) =>
        _dbContext.People.AnyAsync(p => p.Id == personId, cancellationToken);

    public async Task<int> CreateNamedPersonAsync(string name, int coverFaceId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var person = new Person { Name = name, CoverFaceId = coverFaceId, CreatedUtc = nowUtc, ModifiedUtc = nowUtc };
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
        return person.Id;
    }

    public Task<int> AcceptAsync(int faceId, CancellationToken cancellationToken = default) =>
        AcceptWhere(f => f.Id == faceId, cancellationToken);

    public Task<int> RejectAsync(int faceId, CancellationToken cancellationToken = default) =>
        BackToUnknown(_dbContext.Faces.Where(f => f.Id == faceId && f.AssignmentState == FaceAssignmentState.Suggested), cancellationToken);

    public Task<int> MarkUnknownAsync(int faceId, CancellationToken cancellationToken = default) =>
        BackToUnknown(_dbContext.Faces.Where(f => f.Id == faceId
            && (f.AssignmentState == FaceAssignmentState.Suggested || f.AssignmentState == FaceAssignmentState.Confirmed)), cancellationToken);

    public async Task<int> AssignAsync(int faceId, int personId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var rows = await _dbContext.Faces
            .Where(f => f.Id == faceId && f.AssignmentState != FaceAssignmentState.Ignored)
            .ExecuteUpdateAsync(s => s
                // Moving away from a suggested person rejects them; a remembered rejection of the new person is dropped.
                .SetProperty(f => f.RejectedPersonId,
                    f => f.AssignmentState == FaceAssignmentState.Suggested && f.PersonId != null && f.PersonId != personId
                        ? f.PersonId
                        : f.RejectedPersonId == personId ? null : f.RejectedPersonId)
                .SetProperty(f => f.PersonId, personId)
                .SetProperty(f => f.AssignmentState, FaceAssignmentState.Confirmed)
                .SetProperty(f => f.MatchDistance, (float?)null), cancellationToken);
        if (rows > 0)
            await _dbContext.People.Where(p => p.Id == personId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ModifiedUtc, nowUtc), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    public Task<int> IgnoreAsync(int faceId, CancellationToken cancellationToken = default) =>
        _dbContext.Faces.Where(f => f.Id == faceId && f.AssignmentState != FaceAssignmentState.Ignored)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.AssignmentState, FaceAssignmentState.Ignored)
                .SetProperty(f => f.PersonId, (int?)null)
                .SetProperty(f => f.RejectedPersonId, (int?)null)
                .SetProperty(f => f.MatchDistance, (float?)null), cancellationToken);

    public Task<int> RestoreAsync(int faceId, CancellationToken cancellationToken = default) =>
        _dbContext.Faces.Where(f => f.Id == faceId && f.AssignmentState == FaceAssignmentState.Ignored)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.AssignmentState, FaceAssignmentState.Unknown), cancellationToken);

    public Task<int> AcceptAllAsync(int personId, CancellationToken cancellationToken = default) =>
        AcceptWhere(f => f.PersonId == personId, cancellationToken);

    public Task<int> AcceptImageAsync(int personId, int imageId, CancellationToken cancellationToken = default) =>
        AcceptWhere(f => f.PersonId == personId && f.ImageId == imageId, cancellationToken);

    public Task<int> RejectImageAsync(int personId, int imageId, CancellationToken cancellationToken = default) =>
        BackToUnknown(_dbContext.Faces.Where(f => f.PersonId == personId && f.ImageId == imageId
            && f.AssignmentState == FaceAssignmentState.Suggested), cancellationToken);

    private Task<int> AcceptWhere(Expression<Func<Face, bool>> predicate, CancellationToken cancellationToken) =>
        _dbContext.Faces.Where(predicate).Where(f => f.AssignmentState == FaceAssignmentState.Suggested)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.AssignmentState, FaceAssignmentState.Confirmed)
                .SetProperty(f => f.MatchDistance, (float?)null), cancellationToken);

    private static Task<int> BackToUnknown(IQueryable<Face> faces, CancellationToken cancellationToken) =>
        faces.ExecuteUpdateAsync(s => s
            .SetProperty(f => f.RejectedPersonId, f => f.PersonId)
            .SetProperty(f => f.PersonId, (int?)null)
            .SetProperty(f => f.AssignmentState, FaceAssignmentState.Unknown)
            .SetProperty(f => f.MatchDistance, (float?)null), cancellationToken);
}
