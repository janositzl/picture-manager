using System;
using System.IO;
using System.Linq;
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
    private const int LockStripes = 64;
    // Striped instead of one lock per key: the same key always maps to the same stripe, and nothing accumulates.
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, LockStripes).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    // Bounds decodes across all requests, so a screenful of tiles can't decode in parallel.
    private readonly SemaphoreSlim _generationGate;

    public SkiaSharpThumbnailService(ThumbnailCacheOptions options)
    {
        _options = options;
        _generationGate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentGenerations));
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
        var gate = _locks[(uint)key.GetHashCode() % LockStripes];
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(finalPath))
                return finalPath;

            // After the cache re-check, so a request the browser already dropped never starts a decode.
            await _generationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return Generate(sourcePath, orientation, rotation, size, rootPath, cacheKey, finalPath);
            }
            finally
            {
                _generationGate.Release();
            }
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
        SKBitmap? sourceBitmap;
        try
        {
            // Scaled decode: a JPEG is decoded at 1/2, 1/4 or 1/8 instead of at full size.
            sourceBitmap = SkiaBitmapOps.DecodeDownsampled(sourcePath, (int)size);
        }
        catch (IOException)
        {
            // Missing file or an offline share: not a derivative we can make now, and not worth a 500.
            return null;
        }

        using var _ = sourceBitmap;
        if (sourceBitmap is null)
            return null;

        // Resize first: the 90 degree swap doesn't change the longest edge, so orientation and
        // rotation then only touch a thumbnail-sized bitmap instead of a full-resolution one.
        var (targetWidth, targetHeight) = ThumbnailResizeCalculator.CalculateTargetDimensions(
            sourceBitmap.Width, sourceBitmap.Height, (int)size);

        using var scaledBitmap = sourceBitmap.Resize(
            new SKImageInfo(targetWidth, targetHeight), SKSamplingOptions.Default);
        if (scaledBitmap is null)
            return null;

        var normalizedOrientation = ThumbnailResizeCalculator.NormalizeOrientation(HeicDecoder.EffectiveOrientation(sourcePath, orientation));
        using var exifOrientedBitmap = SkiaBitmapOps.ApplyOrientation(scaledBitmap, normalizedOrientation);
        // The extra user rotation reuses the EXIF transform.
        using var resizedBitmap = SkiaBitmapOps.ApplyOrientation(exifOrientedBitmap, SkiaBitmapOps.OrientationForRotation(rotation));

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
