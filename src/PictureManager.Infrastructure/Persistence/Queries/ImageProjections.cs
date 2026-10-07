using System;
using System.Linq.Expressions;
using PictureManager.Application.Images;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Queries;

internal static class ImageProjections
{
    public static readonly Expression<Func<Image, ImageRow>> ToRow = i => new ImageRow(
        i.Id, i.FolderId, i.FileName, i.Extension, i.Width, i.Height, i.DateTaken, i.IsFavorite,
        i.ContentHash, i.SortDate, i.FileName.ToLower(), i.Folder!.Root!.Name, i.Folder.RelativePath, i.IndexState, null, i.IsHidden);
}
