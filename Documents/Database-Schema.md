# PictureManager Database Schema

PostgreSQL database accessed via EF Core (`PictureManagerDbContext`). Source: `src/PictureManager.Model/*.cs` and `src/PictureManager.Infrastructure/Persistence/Configurations/*.cs`.

## ImageRoots

Top-level mount points (e.g. NAS shares) that folders/images are scanned from.

| Property | Type | Description |
|---|---|---|
| Id | int (PK) | Row identifier |
| Name | string(200) | Display name |
| MountPath | string(1000) | Filesystem path to the root |
| Alias | string?(200) | Export-only name for the root's first path segment |
| IsActive | bool | Whether the root is scanned |
| CreatedUtc | timestamptz | Row creation time |

## Folders

Directory tree under each root, mirrored for scanning/browsing.

| Property | Type | Description |
|---|---|---|
| Id | int (PK) | Row identifier |
| RootId | int (FK → ImageRoots) | Owning root |
| ParentId | int? (FK → Folders) | Parent folder, null for root folder |
| Name | string(500) | Folder name |
| RelativePath | string(2000), indexed | Path relative to the root |
| IsActive | bool | Whether folder is active |
| MissingSinceUtc | timestamptz? | Set when the scanner finds the folder gone from disk |
| ChildrenDiscoveredAt | timestamptz? | Last time subfolders were diffed; null = never |
| LastWriteTimeUtc | timestamptz? | Directory's last-write time as of last discovery |
| ScanStatus | enum (Idle/Scanning/Error), indexed | Current scan state |
| LastScannedAt | timestamptz? | Last successful scan completion; null = never scanned |
| LastScanFileCount | int? | Own image count as of LastScannedAt |
| IsExcluded | bool | Excludes folder and subtree from scans/discovery |
| CreatedUtc | timestamptz | Row creation time |
| ModifiedUtc | timestamptz | Row last-modified time |

Indexes: RootId, ParentId, RelativePath, ScanStatus. Deleting a parent cascades to children; deleting a root is restricted.

## Images

Individual image files with metadata.

| Property | Type | Description |
|---|---|---|
| Id | int (PK) | Row identifier |
| FolderId | int (FK → Folders), indexed | Containing folder |
| FileName | string(500) | File name |
| Extension | string(20) | File extension |
| ContentHash | string(200), indexed | Hash of file content |
| PerceptualHash | string?(200) | Perceptual/similarity hash |
| FileSize | long | File size in bytes |
| FileModified | timestamptz | File's last-modified time |
| Width | int? | Pixel width |
| Height | int? | Pixel height |
| Orientation | int? | EXIF orientation |
| DateTaken | timestamp (no tz)? | EXIF capture time, stored local-naive (no timezone) |
| SortDate | timestamptz, computed/stored | COALESCE(DateTaken as UTC wall-clock, FileModified); DB-generated, never set by app code |
| CameraMake | string?(200) | Camera manufacturer |
| CameraModel | string?(200) | Camera model |
| LensModel | string?(200) | Lens model |
| Latitude | double? | GPS latitude |
| Longitude | double? | GPS longitude |
| RawMetadata | jsonb? | Raw extracted metadata |
| IsFavorite | bool, indexed | User favorite flag |
| IndexState | enum (Pending/Indexed) | Whether metadata indexing is done |
| FirstSeenUtc | timestamptz | When the image was first discovered |
| MissingSinceUtc | timestamptz? | Set when file is gone from disk |
| CreatedAt | timestamptz | Row creation time |
| UpdatedAt | timestamptz | Row last-modified time |

Indexes: FolderId, ContentHash, IsFavorite, composite (FolderId, SortDate, Id) for folder-grid date sorting. Additional expression/partial indexes (lower(FileName), favorites) exist as raw SQL in a migration, not via fluent API. Deleting a folder cascades to its images.

## Albums

User-curated collections of images.

| Property | Type | Description |
|---|---|---|
| Id | int (PK) | Row identifier |
| Name | string(300) | Album name |
| Description | string?(2000) | Album description |
| OwnerUserId | int (FK → AppUsers), indexed | Owning user |
| CreatedAt | timestamptz | Row creation time |
| UpdatedAt | timestamptz | Row last-modified time |

Deleting the owning user is restricted (not cascaded).

## AlbumImages

Join table linking albums to images, with ordering.

| Property | Type | Description |
|---|---|---|
| AlbumId | int (PK part, FK → Albums) | Album reference |
| ImageId | int (PK part, FK → Images), indexed | Image reference |
| SortOrder | int | Position of the image within the album |
| AddedAt | timestamptz | When the image was added to the album |

Composite primary key (AlbumId, ImageId). Deleting either the album or the image cascades.

## AppUsers

Application user accounts.

| Property | Type | Description |
|---|---|---|
| Id | int (PK) | Row identifier |
| ZitadelSubjectId | string?(200), unique index | External identity subject id (Zitadel auth) |
| DisplayName | string(200) | Display name |
| Role | enum (User/Admin) | Authorization role |

Seeded with a system user (Id 1, "System") as a v1 placeholder album owner until v2 auth lands.

## Jobs

Background jobs (scan or discovery); only one runs at a time.

| Property | Type | Description |
|---|---|---|
| Id | int (PK) | Row identifier |
| Kind | enum (Scan/Discovery) | Job type |
| FolderId | int? (FK → Folders) | Scope: null = all active roots, else starting folder |
| IsRecursive | bool | Whether the job recurses into subfolders |
| Status | enum (Pending/Enumerating/Enriching/Completed/Failed/Cancelled), indexed | Current status |
| StartedUtc | timestamptz | Start time |
| CompletedUtc | timestamptz? | Completion time |
| FoldersProcessed | int | Folders visited so far |
| FilesFound | int | Scan only; always 0 for discovery |
| FilesEnriched | int | Scan only; always 0 for discovery |
| ErrorMessage | string?(4000) | Error detail on failure |

Deleting the referenced folder sets FolderId to null.

## AppSettings

Singleton row of global scan settings.

| Property | Type | Description |
|---|---|---|
| Id | int (PK) | Row identifier (singleton, seeded as 1) |
| ExcludedFolderNames | jsonb (List\<string\>) | Folder names always excluded from scans |
| ExcludedExtensions | jsonb (List\<string\>) | File extensions always excluded |
| IncludedExtensions | jsonb (List\<string\>?) | If set, only these extensions are scanned |

Seeded singleton: excluded folders `raw`, `backup`, `@eaDir`; excluded extension `.heic`; no included-extensions filter.
