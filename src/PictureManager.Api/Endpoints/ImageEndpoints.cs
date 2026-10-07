using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Application.Thumbnails;

namespace PictureManager.Api.Endpoints;

public static class ImageEndpoints
{
    public static IEndpointRouteBuilder MapImageEndpoints(this IEndpointRouteBuilder user)
    {
        user.MapGet("/images/{id:int}/thumbnail", GetThumbnailAsync);
        user.MapGet("/images/{id:int}/preview", GetPreviewAsync);
        return user;
    }

    public static async Task<IResult> GetThumbnailAsync(
        int id, IImageRepository imageRepository, IThumbnailService thumbnailService, CancellationToken cancellationToken)
    {
        var image = await imageRepository.GetByIdWithFolderAsync(id, cancellationToken);
        if (image?.Folder?.Root is null || image.MissingSinceUtc is not null)
            return Results.NotFound();

        var physicalPath = ImagePathResolver.ResolvePhysicalPath(
            image.Folder.Root.MountPath, image.Folder.RelativePath, image.FileName, image.Extension);

        // A freshly discovered image has no content hash until enrichment runs, so no cache key exists yet.
        if (string.IsNullOrEmpty(image.ContentHash))
            return Results.NotFound();

        var derivativePath = await thumbnailService.GetOrCreateDerivativePathAsync(
            image.ContentHash, physicalPath, image.Orientation, DerivativeSize.Thumbnail, cancellationToken, image.ThumbnailRotation);
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

        if (string.IsNullOrEmpty(image.ContentHash))
            return Results.NotFound();

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
