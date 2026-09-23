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

        return ServePhysicalFile(derivativePath, "image/webp");
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

            return ServePhysicalFile(physicalPath, ImageContentTypeResolver.Resolve(image.Extension));
        }

        var derivativePath = await thumbnailService.GetOrCreateDerivativePathAsync(
            image.ContentHash, physicalPath, image.Orientation, DerivativeSize.Preview, cancellationToken);
        if (derivativePath is null)
            return Results.NotFound();

        return ServePhysicalFile(derivativePath, "image/webp");
    }

    // Results.File treats a non-rooted path as virtual (resolved against the web root), but the cache
    // root and image mount paths are configured relative to the process working directory.
    private static IResult ServePhysicalFile(string path, string contentType) =>
        Results.File(Path.IsPathRooted(path) ? path : Path.GetFullPath(path), contentType, enableRangeProcessing: true);
}
