using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MetadataExtractor;
using MetadataExtractor.Formats.Bmp;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Gif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.WebP;
using PictureManager.Application.Scanning;
using SkiaSharp;
using Directory = MetadataExtractor.Directory;

namespace PictureManager.Infrastructure.Scanning;

public sealed class MetadataExtractorExifReader : IExifReader
{
    public Task<ExifData> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(filePath);
        }
        catch (Exception)
        {
            var (fw, fh) = ReadCodecSize(filePath);
            return Task.FromResult(fw is null ? ExifData.Empty : ExifData.Empty with { Width = fw, Height = fh });
        }

        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();

        var (width, height) = ReadDimensions(directories, ifd0, subIfd);
        if (width is null || height is null)
            (width, height) = ReadCodecSize(filePath);
        int? orientation = ifd0 is not null && ifd0.TryGetInt32(ExifIfd0Directory.TagOrientation, out var o) ? o : null;
        DateTime? dateTaken = subIfd is not null && subIfd.TryGetDateTime(ExifSubIfdDirectory.TagDateTimeOriginal, out var dt) ? dt : null;
        var geoLocation = gps?.GetGeoLocation();

        var rawMetadata = new Dictionary<string, string>();
        foreach (var directory in directories)
        {
            foreach (var tag in directory.Tags)
            {
                rawMetadata[$"{directory.Name}.{tag.Name}"] = StripNulChars(tag.Description) ?? string.Empty;
            }
        }

        var result = new ExifData(
            width, height, orientation, dateTaken,
            StripNulChars(ifd0?.GetDescription(ExifIfd0Directory.TagMake)),
            StripNulChars(ifd0?.GetDescription(ExifIfd0Directory.TagModel)),
            StripNulChars(subIfd?.GetDescription(ExifSubIfdDirectory.TagLensModel)),
            geoLocation?.Latitude,
            geoLocation?.Longitude,
            JsonSerializer.Serialize(rawMetadata));

        return Task.FromResult(result);
    }

    private static (int? Width, int? Height) ReadDimensions(
        IReadOnlyList<Directory> directories, ExifIfd0Directory? ifd0, ExifSubIfdDirectory? subIfd)
    {
        (Directory? Dir, int WidthTag, int HeightTag)[] candidates =
        [
            (directories.OfType<JpegDirectory>().FirstOrDefault(), JpegDirectory.TagImageWidth, JpegDirectory.TagImageHeight),
            (directories.OfType<PngDirectory>().FirstOrDefault(), PngDirectory.TagImageWidth, PngDirectory.TagImageHeight),
            (directories.OfType<GifHeaderDirectory>().FirstOrDefault(), GifHeaderDirectory.TagImageWidth, GifHeaderDirectory.TagImageHeight),
            (directories.OfType<BmpHeaderDirectory>().FirstOrDefault(), BmpHeaderDirectory.TagImageWidth, BmpHeaderDirectory.TagImageHeight),
            (directories.OfType<WebPDirectory>().FirstOrDefault(), WebPDirectory.TagImageWidth, WebPDirectory.TagImageHeight),
            (subIfd, ExifDirectoryBase.TagExifImageWidth, ExifDirectoryBase.TagExifImageHeight),
            (ifd0, ExifDirectoryBase.TagImageWidth, ExifDirectoryBase.TagImageHeight),
        ];

        foreach (var (dir, widthTag, heightTag) in candidates)
        {
            if (dir is not null && dir.TryGetInt32(widthTag, out var w) && dir.TryGetInt32(heightTag, out var h) && w > 0 && h > 0)
                return (w, h);
        }

        return (null, null);
    }

    // Header-only probe: SKCodec reads just the container header, it does not decode pixels.
    private static (int? Width, int? Height) ReadCodecSize(string filePath)
    {
        try
        {
            using var codec = SKCodec.Create(filePath);
            return codec is null ? (null, null) : (codec.Info.Width, codec.Info.Height);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    // Some tag values (notably ICC profile descriptions, e.g. "sRGB IEC61966-2.1") come through with
    // trailing NUL padding from their source binary format. PostgreSQL's text and jsonb types can't
    // store a literal NUL byte at all -- jsonb fails hard with "22P05: unsupported Unicode escape
    // sequence" -- so it must be stripped before anything here reaches the database.
    public static string? StripNulChars(string? value) => value?.Replace("\0", string.Empty);
}
