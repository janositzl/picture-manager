using System;

namespace PictureManager.Application.Scanning;

public sealed record ExifData(
    int? Width,
    int? Height,
    int? Orientation,
    DateTime? DateTaken,
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    double? Latitude,
    double? Longitude,
    string? RawMetadataJson)
{
    public static readonly ExifData Empty = new(null, null, null, null, null, null, null, null, null, null);
}
