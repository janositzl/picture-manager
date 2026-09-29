using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using PictureManager.Application.Scanning;
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
            return Task.FromResult(ExifData.Empty);
        }

        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        var jpeg = directories.OfType<JpegDirectory>().FirstOrDefault();

        int? width = jpeg is not null && jpeg.TryGetInt32(JpegDirectory.TagImageWidth, out var w) ? w : null;
        int? height = jpeg is not null && jpeg.TryGetInt32(JpegDirectory.TagImageHeight, out var h) ? h : null;
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

    // Some tag values (notably ICC profile descriptions, e.g. "sRGB IEC61966-2.1") come through with
    // trailing NUL padding from their source binary format. PostgreSQL's text and jsonb types can't
    // store a literal NUL byte at all -- jsonb fails hard with "22P05: unsupported Unicode escape
    // sequence" -- so it must be stripped before anything here reaches the database.
    public static string? StripNulChars(string? value) => value?.Replace("\0", string.Empty);
}
