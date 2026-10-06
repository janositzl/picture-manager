using System;
using PictureManager.Model;

namespace PictureManager.Application.Images;

public enum ImageSort
{
    Date,
    Name
}

public enum SortDirection
{
    Asc,
    Desc
}

/// <summary>Which of a person's faces make a photo count: Confirmed ones, or Suggested ones the user hasn't confirmed yet.</summary>
public enum PersonFaceState
{
    Confirmed,
    Suggested
}

/// <summary>
/// AND-combined listing filters. FolderId = images directly in that folder only. PersonId = photos with that person's
/// Suggested/Confirmed face; PersonState narrows that to Confirmed, or to Suggested-only (no Confirmed face of that person in the photo).
/// HasFaces true = at least one detected face that is not Ignored, false = none (including photos not analysed yet), null = no filter.
/// </summary>
public sealed record ImageListFilter(
    int? FolderId, string? FolderName, string? FileName, bool FavoritesOnly, int? PersonId = null, PersonFaceState? PersonState = null,
    bool? HasFaces = null);

/// <summary>"Continue after this row". Date sorts use SortDate; name sorts use SortName (the DB's lower(FileName)).</summary>
public sealed record ImageKeyset(DateTime? SortDate, string? SortName, int Id);

/// <summary>Slim list row projected in SQL (never loads RawMetadata). RootName/RelativePath build FolderPath.</summary>
public sealed record ImageRow(
    int Id,
    int FolderId,
    string FileName,
    string Extension,
    int? Width,
    int? Height,
    DateTime? DateTaken,
    bool IsFavorite,
    string ContentHash,
    DateTime SortDate,
    string SortName,
    string RootName,
    string RelativePath,
    IndexState IndexState = IndexState.Indexed,
    int? FaceId = null);

public sealed record ImageDetailRow(
    ImageRow Image,
    long FileSize,
    DateTime FileModified,
    int? Orientation,
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    double? Latitude,
    double? Longitude,
    string? RawMetadata,
    string RootName,
    string RelativePath);

public sealed record AlbumRef(int Id, string Name);
