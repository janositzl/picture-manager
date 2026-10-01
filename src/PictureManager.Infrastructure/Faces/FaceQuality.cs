using System;
using SkiaSharp;

namespace PictureManager.Infrastructure.Faces;

/// <summary>0-1 score: detector confidence × size factor × sharpness factor. Picks covers, gates clustering.</summary>
public static class FaceQuality
{
    private const float FullSizePx = 112f;      // ArcFace's input side: at or above it, size no longer limits quality
    private const double FullSharpness = 150.0; // Laplacian variance of a reasonably sharp aligned face

    public static float Compute(float confidence, float faceSidePx, double laplacianVariance)
    {
        var size = Math.Min(1f, faceSidePx / FullSizePx);
        var sharpness = (float)Math.Min(1.0, laplacianVariance / FullSharpness);
        return Math.Clamp(confidence * size * sharpness, 0f, 1f);
    }

    /// <summary>Variance of the 4-neighbour Laplacian of the luma channel: low = blurry.</summary>
    public static double LaplacianVariance(SKBitmap rgba)
    {
        int w = rgba.Width, h = rgba.Height;
        var pixels = rgba.GetPixelSpan();
        var row = rgba.RowBytes;
        // Spans can't be captured by local functions, so the pixels are passed in.
        static double Luma(ReadOnlySpan<byte> p, int row, int x, int y)
        {
            var i = y * row + x * 4;
            return 0.299 * p[i] + 0.587 * p[i + 1] + 0.114 * p[i + 2];
        }

        double sum = 0, sumSquares = 0;
        var count = 0;
        for (var y = 1; y < h - 1; y++)
            for (var x = 1; x < w - 1; x++)
            {
                var laplacian = Luma(pixels, row, x - 1, y) + Luma(pixels, row, x + 1, y) + Luma(pixels, row, x, y - 1)
                    + Luma(pixels, row, x, y + 1) - 4 * Luma(pixels, row, x, y);
                sum += laplacian; sumSquares += laplacian * laplacian; count++;
            }

        if (count == 0) return 0;
        var mean = sum / count;
        return sumSquares / count - mean * mean;
    }
}
