using System.Linq;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Queries;

/// <summary>The phase 5 visibility rule, in one place.</summary>
internal static class VisibilityExtensions
{
    public static IQueryable<Image> WhereVisible(this IQueryable<Image> images) =>
        images.Where(i => i.MissingSinceUtc == null && i.Folder!.IsActive && i.Folder.Root!.IsActive);

    public static IQueryable<Folder> WhereVisible(this IQueryable<Folder> folders) =>
        folders.Where(f => f.IsActive && f.Root!.IsActive);
}
