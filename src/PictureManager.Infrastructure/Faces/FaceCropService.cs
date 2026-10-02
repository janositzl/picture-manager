using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Application.Thumbnails;
using SkiaSharp;

namespace PictureManager.Infrastructure.Faces;

/// <summary>
/// Face crops cut from the cached preview derivative (already orientation-corrected, like the face box) into the
/// thumbnail cache. Face ids are never reused, so a crop never goes stale.
/// </summary>
public sealed class FaceCropService : IFaceCropService
{
    private const int CropSide = 160;
    private const float Padding = 0.3f;

    private readonly IPeopleRepository _people;
    private readonly IThumbnailService _thumbnails;
    private readonly ThumbnailCacheOptions _cacheOptions;

    public FaceCropService(IPeopleRepository people, IThumbnailService thumbnails, ThumbnailCacheOptions cacheOptions)
    {
        _people = people;
        _thumbnails = thumbnails;
        _cacheOptions = cacheOptions;
    }

    public async Task<string?> GetOrCreateCropPathAsync(int faceId, CancellationToken cancellationToken = default)
    {
        var cacheRoot = _cacheOptions.RootPath ?? Path.Combine(Path.GetTempPath(), "picturemanager-cache");
        var cropPath = Path.Combine(cacheRoot, "faces", $"{faceId}.jpg");
        if (File.Exists(cropPath))
            return cropPath;

        var source = await _people.GetFaceCropSourceAsync(faceId, cancellationToken);
        if (source is null)
            return null;

        var physical = ImagePathResolver.ResolvePhysicalPath(source.MountPath, source.RelativePath, source.FileName, source.Extension);
        var previewPath = await _thumbnails.GetOrCreateDerivativePathAsync(
            source.ContentHash, physical, source.Orientation, DerivativeSize.Preview, cancellationToken);
        if (previewPath is null)
            return null;

        using var preview = SKBitmap.Decode(previewPath);
        if (preview is null)
            return null;

        var padX = source.Width * Padding; var padY = source.Height * Padding;
        var rect = SKRectI.Round(new SKRect(
            Math.Max(0, source.X - padX) * preview.Width,
            Math.Max(0, source.Y - padY) * preview.Height,
            Math.Min(1, source.X + source.Width + padX) * preview.Width,
            Math.Min(1, source.Y + source.Height + padY) * preview.Height));
        if (rect.Width <= 0 || rect.Height <= 0)
            return null;

        var scale = Math.Min(1f, (float)CropSide / Math.Max(rect.Width, rect.Height));
        using var crop = new SKBitmap(Math.Max(1, (int)(rect.Width * scale)), Math.Max(1, (int)(rect.Height * scale)));
        using (var canvas = new SKCanvas(crop))
        {
            using var image = SKImage.FromBitmap(preview);
            canvas.DrawImage(image, rect, new SKRect(0, 0, crop.Width, crop.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cropPath)!);
        var tempPath = cropPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var output = File.Create(tempPath))
        {
            using var data = crop.Encode(SKEncodedImageFormat.Jpeg, 85);
            data.SaveTo(output);
        }
        File.Move(tempPath, cropPath, overwrite: true);
        return cropPath;
    }
}
