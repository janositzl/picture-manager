using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Application.Thumbnails;

namespace PictureManager.Api.Endpoints;

public static class ImageEndpoints
{
    public static void MapImageEndpoints(this WebApplication app)
    {
        app.MapGet("/api/images/{id:int}/thumbnail", GetThumbnailAsync);
        app.MapGet("/api/images/{id:int}/preview", GetPreviewAsync);
    }

    public static async Task<IResult> GetThumbnailAsync(
        int id, IImageRepository imageRepository, IThumbnailService thumbnailService, CancellationToken cancellationToken)
    {
        var image = await imageRepository.GetByIdWithFolderAsync(id, cancellationToken);
        if (image?.Folder?.Root is null || image.MissingSinceUtc is not null)
            return Results.NotFound();

        var physicalPath = ImagePathResolver.ResolvePhysicalPath(
            image.Folder.Root.MountPath, image.Folder.RelativePath, image.FileName, image.Extension);

        var derivativePath = await thumbnailService.GetOrCreateDerivativePathAsync(
            image.ContentHash, physicalPath, image.Orientation, DerivativeSize.Thumbnail, cancellationToken);
        if (derivativePath is null)
            return Results.NotFound();

        return Results.File(derivativePath, "image/webp", enableRangeProcessing: true);
    }

    public static async Task<IResult> GetPreviewAsync(
        int id, IImageRepository imageRepository, IThumbnailService thumbnailService, ThumbnailCacheOptions cacheOptions, CancellationToken cancellationToken)
    {
        var image = await imageRepository.GetByIdWithFolderAsync(id, cancellationToken);
        if (image?.Folder?.Root is null || image.MissingSinceUtc is not null)
            return Results.NotFound();

        var physicalPath = ImagePathResolver.ResolvePhysicalPath(
            image.Folder.Root.MountPath, image.Folder.RelativePath, image.FileName, image.Extension);

        if (!cacheOptions.PreviewEnabled)
        {
            if (!File.Exists(physicalPath))
                return Results.NotFound();

            return Results.File(physicalPath, ImageContentTypeResolver.Resolve(image.Extension), enableRangeProcessing: true);
        }

        var derivativePath = await thumbnailService.GetOrCreateDerivativePathAsync(
            image.ContentHash, physicalPath, image.Orientation, DerivativeSize.Preview, cancellationToken);
        if (derivativePath is null)
            return Results.NotFound();

        return Results.File(derivativePath, "image/webp", enableRangeProcessing: true);
    }
}
