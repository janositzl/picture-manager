using System;
using System.IO;
using ImageMagick;
using SkiaSharp;

namespace PictureManager.Infrastructure.Imaging;

/// <summary>
/// HEIC/HEIF decoding via Magick.NET (SkiaSharp ships no HEIF codec). Magick's HEIC coder applies the
/// container's own rotation, so the returned pixels are already upright and callers must NOT apply the
/// EXIF orientation again (see <see cref="EffectiveOrientation"/>).
/// </summary>
internal static class HeicDecoder
{
    internal static bool IsHeic(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".heic", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".heif", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>HEIC pixels come out already oriented, so the stored EXIF orientation is neutralised.</summary>
    internal static int? EffectiveOrientation(string path, int? orientation) => IsHeic(path) ? 1 : orientation;

    /// <summary>
    /// Decodes to an upright Rgba8888 bitmap, shrunk so the longest side is at most <paramref name="maxSide"/>
    /// (null = full size). Null if the content is undecodable; I/O errors are thrown.
    /// </summary>
    internal static SKBitmap? Decode(string path, int? maxSide)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            // Pin the coder: without it ImageMagick sniffs the content, so a file named .heic could be parsed
            // as MVG/SVG/etc. (ImageTragick-style). Only the HEIC coder may read these bytes.
            using var image = new MagickImage(bytes, new MagickReadSettings { Format = MagickFormat.Heic });
            image.AutoOrient();
            if (maxSide is { } max && Math.Max(image.Width, image.Height) > max)
                image.Resize(new MagickGeometry((uint)max, (uint)max));

            var pixels = image.GetPixelsUnsafe().ToByteArray(PixelMapping.RGBA);
            if (pixels is null)
                return null;

            var bitmap = new SKBitmap(new SKImageInfo((int)image.Width, (int)image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
            return bitmap;
        }
        catch (MagickException)
        {
            return null;
        }
    }
}
