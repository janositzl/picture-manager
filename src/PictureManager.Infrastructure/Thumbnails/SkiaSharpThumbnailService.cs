using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Thumbnails;
using PictureManager.Infrastructure.Imaging;
using SkiaSharp;

namespace PictureManager.Infrastructure.Thumbnails;

public sealed class SkiaSharpThumbnailService : IThumbnailService
{
    private const int WebPQuality = 82;
    private readonly ThumbnailCacheOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public SkiaSharpThumbnailService(ThumbnailCacheOptions options)
    {
        _options = options;
    }

    public async Task<string?> GetOrCreateDerivativePathAsync(
        string contentHash, string sourcePath, int? orientation, DerivativeSize size, CancellationToken cancellationToken = default, int rotation = 0)
    {
        var rootPath = _options.RootPath ?? throw new InvalidOperationException("ThumbnailCache:RootPath is not configured.");
        // A rotated derivative is cached under its own key, so it never clashes with an unrotated copy of the same content.
        var cacheKey = RotatedKey(contentHash, rotation);
        var finalPath = ThumbnailCachePathResolver.GetPath(rootPath, cacheKey, size);

        if (File.Exists(finalPath))
            return finalPath;

        if (!File.Exists(sourcePath))
            return null;

        var key = $"{cacheKey}-{(int)size}";
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(finalPath))
                return finalPath;

            return Generate(sourcePath, orientation, rotation, size, rootPath, cacheKey, finalPath);
        }
        finally
        {
            gate.Release();
        }
    }

    public void DeleteDerivatives(string contentHash)
    {
        if (_options.RootPath is not { } rootPath || contentHash.Length < 4)
            return;

        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            foreach (var size in Enum.GetValues<DerivativeSize>())
            {
                try
                {
                    File.Delete(ThumbnailCachePathResolver.GetPath(rootPath, RotatedKey(contentHash, rotation), size));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best effort: a stuck file only costs disk space, and is regenerated or overwritten later.
                }
            }
        }
    }

    private static string RotatedKey(string contentHash, int rotation) =>
        rotation == 0 ? contentHash : $"{contentHash}r{rotation}";

    private static string? Generate(
        string sourcePath, int? orientation, int rotation, DerivativeSize size, string rootPath, string contentHash, string finalPath)
    {
        using var sourceBitmap = SkiaBitmapOps.DecodeSafely(sourcePath);
        if (sourceBitmap is null)
            return null;

        var normalizedOrientation = ThumbnailResizeCalculator.NormalizeOrientation(HeicDecoder.EffectiveOrientation(sourcePath, orientation));
        using var exifOrientedBitmap = SkiaBitmapOps.ApplyOrientation(sourceBitmap, normalizedOrientation);
        // Clockwise 90/180/270 equal EXIF orientations 6/3/8, so the extra user rotation reuses the same transform.
        using var orientedBitmap = rotation switch
        {
            90 => SkiaBitmapOps.ApplyOrientation(exifOrientedBitmap, 6),
            180 => SkiaBitmapOps.ApplyOrientation(exifOrientedBitmap, 3),
            270 => SkiaBitmapOps.ApplyOrientation(exifOrientedBitmap, 8),
            _ => SkiaBitmapOps.ApplyOrientation(exifOrientedBitmap, 1),
        };

        var (targetWidth, targetHeight) = ThumbnailResizeCalculator.CalculateTargetDimensions(
            orientedBitmap.Width, orientedBitmap.Height, (int)size);

        using var resizedBitmap = orientedBitmap.Resize(
            new SKImageInfo(targetWidth, targetHeight), SKSamplingOptions.Default);
        if (resizedBitmap is null)
            return null;

        var shardDirectory = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(shardDirectory);

        var tempPath = ThumbnailCachePathResolver.GetTempPath(rootPath, contentHash, size);
        try
        {
            using (var image = SKImage.FromBitmap(resizedBitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Webp, WebPQuality))
            using (var fileStream = File.Create(tempPath))
            {
                data.SaveTo(fileStream);
            }

            File.Move(tempPath, finalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        return finalPath;
    }
}
