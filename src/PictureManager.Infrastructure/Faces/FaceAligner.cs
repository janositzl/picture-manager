using System.Collections.Generic;
using SkiaSharp;

namespace PictureManager.Infrastructure.Faces;

/// <summary>Aligns a detected face onto ArcFace's canonical 112×112 five-point layout.</summary>
public static class FaceAligner
{
    public const int Size = 112;

    /// <summary>InsightFace's reference landmarks (eyes, nose, mouth corners) for a 112×112 crop.</summary>
    public static readonly (float X, float Y)[] ArcFaceTemplate =
    {
        (38.2946f, 51.6963f), (73.5318f, 51.5014f), (56.0252f, 71.7366f), (41.5493f, 92.3655f), (70.7299f, 92.2041f)
    };

    /// <summary>
    /// Least-squares similarity transform (uniform scale + rotation + translation, no reflection) mapping source
    /// onto target -- the closed-form 2D case of Umeyama's method:
    /// x' = a·x − b·y + tx, y' = b·x + a·y + ty.
    /// </summary>
    public static SKMatrix EstimateSimilarity(IReadOnlyList<(float X, float Y)> source, IReadOnlyList<(float X, float Y)> target)
    {
        var n = source.Count;
        double msx = 0, msy = 0, mtx = 0, mty = 0;
        for (var i = 0; i < n; i++)
        {
            msx += source[i].X; msy += source[i].Y; mtx += target[i].X; mty += target[i].Y;
        }
        msx /= n; msy /= n; mtx /= n; mty /= n;

        double dot = 0, cross = 0, norm = 0;
        for (var i = 0; i < n; i++)
        {
            var sx = source[i].X - msx; var sy = source[i].Y - msy;
            var tx = target[i].X - mtx; var ty = target[i].Y - mty;
            dot += sx * tx + sy * ty;
            cross += sx * ty - sy * tx;
            norm += sx * sx + sy * sy;
        }

        var a = dot / norm;
        var b = cross / norm;
        var transX = mtx - a * msx + b * msy;
        var transY = mty - b * msx - a * msy;

        return new SKMatrix((float)a, (float)-b, (float)transX, (float)b, (float)a, (float)transY, 0, 0, 1);
    }

    public static SKBitmap Warp(SKBitmap image, IReadOnlyList<(float X, float Y)> landmarks)
    {
        var aligned = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(aligned);
        canvas.Clear(SKColors.Black);
        canvas.SetMatrix(EstimateSimilarity(landmarks, ArcFaceTemplate));
        using var source = SKImage.FromBitmap(image);
        canvas.DrawImage(source, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return aligned;
    }
}
