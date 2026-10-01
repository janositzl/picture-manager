using System;
using System.IO;
using SkiaSharp;

namespace PictureManager.Infrastructure.Imaging;

internal static class SkiaBitmapOps
{
    internal static SKBitmap? DecodeSafely(string path)
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
    /// Decodes at reduced size when the codec supports it (JPEG decodes at 1/2, 1/4 or 1/8 scale -- much faster
    /// than a full 24MP decode). Other formats decode at full size. Always Rgba8888. Null if undecodable.
    /// </summary>
    internal static SKBitmap? DecodeDownsampled(string path, int maxSide)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
                return null;

            var info = codec.Info;
            var longest = Math.Max(info.Width, info.Height);
            if (longest > maxSide)
            {
                var scaled = codec.GetScaledDimensions((float)maxSide / longest);
                info = info.WithSize(scaled.Width, scaled.Height);
            }

            return SKBitmap.Decode(codec, info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul));
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
    internal static SKBitmap ApplyOrientation(SKBitmap source, int orientation)
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
