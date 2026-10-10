using System;
using System.Linq;
using System.Linq.Expressions;
using PictureManager.Application.Images;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Queries;

internal static class ImageProjections
{
    /// <summary>The list row as <paramref name="userId"/> sees it: IsFavorite is that user's own favorite.</summary>
    public static Expression<Func<Image, ImageRow>> ToRow(int userId) => i => new ImageRow(
        i.Id, i.FolderId, i.FileName, i.Extension, i.Width, i.Height, i.DateTaken, i.Favorites.Any(f => f.UserId == userId),
        i.ContentHash, i.SortDate, i.FileName.ToLower(), i.Folder!.Root!.Name, i.Folder.RelativePath, i.IndexState, null, i.IsHidden, i.ThumbnailRotation);
}
