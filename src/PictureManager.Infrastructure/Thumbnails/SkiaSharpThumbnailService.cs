using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Thumbnails;
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
        string contentHash, string sourcePath, int? orientation, DerivativeSize size, CancellationToken cancellationToken = default)
    {
        var rootPath = _options.RootPath ?? throw new InvalidOperationException("ThumbnailCache:RootPath is not configured.");
        var finalPath = ThumbnailCachePathResolver.GetPath(rootPath, contentHash, size);

        if (File.Exists(finalPath))
            return finalPath;

        if (!File.Exists(sourcePath))
            return null;

        var key = $"{contentHash}-{(int)size}";
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(finalPath))
                return finalPath;

            return Generate(sourcePath, orientation, size, rootPath, contentHash, finalPath);
        }
        finally
        {
            gate.Release();
        }
    }

    private static string? Generate(
        string sourcePath, int? orientation, DerivativeSize size, string rootPath, string contentHash, string finalPath)
    {
        using var sourceBitmap = DecodeSafely(sourcePath);
        if (sourceBitmap is null)
            return null;

        var normalizedOrientation = ThumbnailResizeCalculator.NormalizeOrientation(orientation);
        using var orientedBitmap = ApplyOrientation(sourceBitmap, normalizedOrientation);

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

    private static SKBitmap? DecodeSafely(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return SKBitmap.Decode(stream);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Applies EXIF-orientation correction using plain canvas transforms (Translate/Scale/RotateDegrees)
    /// rather than raw SKMatrix composition, since SkiaSharp's matrix-concatenation API has shifted across
    /// package versions. Each canvas transform call post-concatenates onto the current transform, so for
    /// calls canvas.A(); canvas.B(); the point mapping applied to subsequently drawn content is A(B(p)) --
    /// i.e. the transform called LAST is applied FIRST to the source geometry. All 8 EXIF orientation cases
    /// below were derived and verified against that composition rule.
    /// </summary>
    private static SKBitmap ApplyOrientation(SKBitmap source, int orientation)
    {
        if (orientation == 1)
            return source.Copy();

        var swapDimensions = orientation is 5 or 6 or 7 or 8;
        var width = swapDimensions ? source.Height : source.Width;
        var height = swapDimensions ? source.Width : source.Height;

        var rotated = new SKBitmap(width, height);
        using var canvas = new SKCanvas(rotated);

        switch (orientation)
        {
            case 2: // mirror horizontal
                canvas.Translate(width, 0);
                canvas.Scale(-1, 1);
                break;
            case 3: // rotate 180
                canvas.Translate(width, height);
                canvas.RotateDegrees(180);
                break;
            case 4: // mirror vertical
                canvas.Translate(0, height);
                canvas.Scale(1, -1);
                break;
            case 5: // transpose (mirror horizontal + rotate 270 CW)
                canvas.Scale(1, -1);
                canvas.RotateDegrees(-90);
                break;
            case 6: // rotate 90 CW
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                break;
            case 7: // transverse (mirror horizontal + rotate 90 CW)
                canvas.Translate(width, height);
                canvas.Scale(1, -1);
                canvas.RotateDegrees(90);
                break;
            case 8: // rotate 270 CW (= 90 CCW)
                canvas.Translate(0, height);
                canvas.RotateDegrees(-90);
                break;
            default:
                // Unrecognized value: treat as normal.
                canvas.DrawBitmap(source, 0, 0, SKSamplingOptions.Default);
                return rotated;
        }

        canvas.DrawBitmap(source, 0, 0, SKSamplingOptions.Default);
        return rotated;
    }
}
