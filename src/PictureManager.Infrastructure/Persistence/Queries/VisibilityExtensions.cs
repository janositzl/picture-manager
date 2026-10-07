using System.Linq;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Queries;

/// <summary>The visibility rule, in one place (phase 5, plus missing folders and user-hidden images).</summary>
internal static class VisibilityExtensions
{
    public static IQueryable<Image> WhereVisible(this IQueryable<Image> images) =>
        images.WhereExisting().Where(i => !i.IsHidden);

    /// <summary>Present and reachable (not missing, active folder and root), hidden images included.</summary>
    public static IQueryable<Image> WhereExisting(this IQueryable<Image> images) =>
        images.Where(i => i.MissingSinceUtc == null && i.Folder!.IsActive && i.Folder.MissingSinceUtc == null && i.Folder.Root!.IsActive);

    /// <summary>Missing folders stay visible (flagged IsMissing) so the user can see and act on them.</summary>
    public static IQueryable<Folder> WhereVisible(this IQueryable<Folder> folders) =>
        folders.Where(f => f.IsActive && f.Root!.IsActive);
}
