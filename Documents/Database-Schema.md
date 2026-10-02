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

Background jobs (scan, discovery or face recognition); only one runs at a time.

| Property | Type | Description |
|---|---|---|
| Id | int (PK) | Row identifier |
| Kind | enum (Scan/Discovery/FaceRecognition) | Job type |
| FolderId | int? (FK → Folders) | Scope: null = all active roots, else starting folder |
| IsRecursive | bool | Whether the job recurses into subfolders |
| Status | enum (Pending/Enumerating/Enriching/Completed/Failed/Cancelled), indexed | Current status |
| StartedUtc | timestamptz | Start time |
| CompletedUtc | timestamptz? | Completion time |
| FoldersProcessed | int | Folders visited so far |
| FilesFound | int | Scan: files found. Face recognition: images to process. Always 0 for discovery |
| FilesEnriched | int | Scan: files enriched. Face recognition: images processed. Always 0 for discovery |
| FacesFound | int | Face recognition only: faces detected so far; 0 otherwise |
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

## FaceModels

The face model that produced a set of embeddings. Embeddings from different models are never compared.

| Property | Type | Description |
|---|---|---|
| Id | int (PK) | Row identifier |
| Name | string(100) | Model name |
| Version | string(100) | Model version |
| EmbeddingDimensions | int | Length of the embedding vectors |
| ModelHash | string(64), unique index | SHA-256 (hex) of the model files; identifies the model across restarts |
| CreatedUtc | timestamptz | When the model was first registered |

## FaceProcessingStates

Face analysis outcome for one image. Valid only while FaceModelId is the current model and ImageFingerprint still equals the image's ContentHash; otherwise the image is a candidate again.

| Property | Type | Description |
|---|---|---|
| ImageId | int (PK, FK → Images) | The analysed image |
| FaceModelId | int (FK → FaceModels), indexed with Status | Model used |
| ImageFingerprint | string | The image's ContentHash at analysis time |
| Status | enum (Completed/Failed/PermanentlyFailed) | Failed is retried by the next run; PermanentlyFailed means given up (undecodable or too many attempts) |
| Attempts | int | Number of attempts so far |
| ErrorMessage | string?(4000) | Error detail on failure |
| ProcessedUtc | timestamptz | When the image was last processed |

Deleting the image cascades. Deleting a face model is restricted while states reference it.

## Faces

A detected face. The box is normalized (0-1) against the orientation-corrected image.

| Property | Type | Description |
|---|---|---|
| Id | int (PK) | Row identifier |
| ImageId | int (FK → Images), indexed | Image containing the face |
| FaceModelId | int (FK → FaceModels), indexed with AssignmentState | Model that produced the embedding |
| PersonId | int? (FK → People), indexed | Assigned person; null = unassigned |
| AssignmentState | enum (Unassigned/Auto/Confirmed/Rejected) | Auto = set by clustering/matching; Confirmed/Rejected = set by the user and never changed by automation (re-processing an image carries PersonId and AssignmentState over to the new face whose box overlaps the old one, IoU ≥ 0.5) |
| X, Y, Width, Height | float | Normalized bounding box |
| DetectionConfidence | float | Detector score |
| QualityScore | float | Face quality score; decides whether the face takes part in clustering |
| Embedding | vector(512), HNSW index (vector_cosine_ops) | L2-normalized embedding, compared with cosine distance. Biometric data |
| CreatedUtc | timestamptz | Creation time |

Deleting the image cascades; deleting the person sets PersonId to null; deleting a face model is restricted while faces reference it.

## People

A person, or an unnamed group created by clustering.

| Property | Type | Description |
|---|---|---|
| Id | int (PK) | Row identifier |
| Name | string?(200), indexed | Null = unnamed group |
| CoverFaceId | int? | Face shown for this person. No foreign key on purpose (avoids a Faces/People cycle); may be stale, so reads use it only while it is still one of the person's Auto/Confirmed faces and otherwise fall back to the best-quality one |
| CreatedUtc | timestamptz | Creation time |
| ModifiedUtc | timestamptz | Last modification time |

## Extensions

The database requires the PostgreSQL `vector` extension (pgvector), enabled by migration. It provides the `vector(512)` column type and the HNSW index on `Faces.Embedding`.
