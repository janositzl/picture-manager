using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Scanning;
using PictureManager.Application.Thumbnails;
using PictureManager.Infrastructure.Imaging;
using SkiaSharp;

namespace PictureManager.Infrastructure.Scanning;

public sealed class SkiaDHashPerceptualHasher : IPerceptualHasher
{
    private const int Width = 9, Height = 8;
    private static readonly SKSamplingOptions Downsample = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    public Task<string?> ComputeAsync(string filePath, int? orientation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var decoded = SkiaBitmapOps.DecodeSafely(filePath);
        if (decoded is null) return Task.FromResult<string?>(null);

        using var oriented = SkiaBitmapOps.ApplyOrientation(decoded, ThumbnailResizeCalculator.NormalizeOrientation(orientation));
        using var image = SKImage.FromBitmap(oriented);
        using var small = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Gray8, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(small))
        {
            canvas.Clear(SKColors.White); // alpha images composite on a fixed background, so hashes are deterministic
            canvas.DrawImage(image, new SKRect(0, 0, Width, Height), Downsample); // mipmaps avoid aliasing on big sources
        }

        ulong hash = 0;
        for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width - 1; x++)
                hash = (hash << 1) | (small.GetPixel(x, y).Red < small.GetPixel(x + 1, y).Red ? 1UL : 0UL);

        return Task.FromResult<string?>(hash.ToString("x16"));
    }
}
