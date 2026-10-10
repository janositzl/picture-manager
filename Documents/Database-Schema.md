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
| IsHidden | bool, default false | User-hidden: out of every view and out of face recognition; only the folder view's Show hidden lists it. Never written by the scanner. |
| IndexState | enum (Pending/Indexed) | Whether metadata indexing is done |
| FirstSeenUtc | timestamptz | When the image was first discovered |
| MissingSinceUtc | timestamptz? | Set when file is gone from disk |
| CreatedAt | timestamptz | Row creation time |
| UpdatedAt | timestamptz | Row last-modified time |

Indexes: FolderId, ContentHash, composite (FolderId, SortDate, Id) for folder-grid date sorting. An additional expression index (lower(FileName)) exists as raw SQL in a migration, not via fluent API. Deleting a folder cascades to its images.

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

## AlbumShares

An album its owner shared with another user.

| Property | Type | Description |
|---|---|---|
| AlbumId | int (PK part, FK → Albums) | Shared album |
| UserId | int (PK part, FK → AppUsers), indexed | User it is shared with |
| Permission | enum (Viewer/Editor) | Viewers browse and export; editors also add, remove, reorder and set the cover |
| CreatedAt | timestamptz | When it was shared |

Composite primary key (AlbumId, UserId). Deleting either the album or the user cascades.

## UserFavorites

A user's favorite photos. Favorites are per user; the image row carries no flag.

| Property | Type | Description |
|---|---|---|
| UserId | int (PK part, FK → AppUsers) | Owner of the favorite |
| ImageId | int (PK part, FK → Images), indexed | Favorite image |
| CreatedAt | timestamptz | When it was marked |

Composite primary key (UserId, ImageId). Deleting either the user or the image cascades. The migration that introduced the table copied the old global favorites to the initial administrator (or the first active admin if that account no longer existed).

## AppUsers

Application user accounts.

| Property | Type | Description |
|---|---|---|
| Id | int (PK) | Row identifier |
| Username | string(64) | Login name as entered |
| NormalizedUsername | string(64), unique index | Lower-cased username; the lookup key |
| DisplayName | string(200) | Display name |
| Role | enum (User/Admin) | Authorization role |
| PasswordHash | string?(500) | Password hash; null means the account cannot log in |
| IsActive | bool | Disabled accounts cannot log in and their sessions stop validating |
| MustChangePassword | bool | The user must set a new password before using the app |
| CanRunFolderActions | bool | May run scans, discovery and face recognition and exclude/remove folders (admins always may) |
| SecurityStamp | Guid | Changes with password, role, active flag or folder-actions permission; invalidates older cookies |
| CreatedAt | timestamptz | When the account was created |
| LastLoginAt | timestamptz? | Last successful login |

Seeded with the initial administrator (Id 1, `admin`, "Administrator", role Admin, no password). The API sets its password at startup from `Auth:InitialAdmin:Password` and requires a change at first login. It was the pre-accounts "System" placeholder album owner, so existing albums stay with it.

## DataProtectionKeys

ASP.NET Core Data Protection key ring (signs the auth cookie); managed by the framework.

| Property | Type | Description |
|---|---|---|
| Id | int (PK) | Row identifier |
| FriendlyName | string? | Key name |
| Xml | string? | Serialized key material |

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
| PersonId | int? (FK → People), indexed | Assigned person; null = Unknown or Ignored |
| AssignmentState | enum, stored as int (Unknown 0 / Suggested 1 / Confirmed 2 / Ignored 4) | Suggested = set by clustering/matching and awaiting the user; Confirmed/Ignored = set by the user and never changed by automation. Unknown = no person yet. 3 was the old Rejected, folded into Unknown + RejectedPersonId. Naming or merging a person does not confirm their Suggested faces. Re-processing an image carries PersonId, AssignmentState, RejectedPersonId and MatchDistance over to the new face whose box overlaps the old one (IoU ≥ 0.5), which is how Ignored survives rescans |
| RejectedPersonId | int? | The person the user last rejected (or marked unknown) for this face; clustering never suggests them again. No foreign key on purpose; may be stale |
| MatchDistance | float? | Cosine distance behind a Suggested assignment (for sorting / low-confidence review); null otherwise |
| X, Y, Width, Height | float | Normalized bounding box |
| DetectionConfidence | float | Detector score |
| QualityScore | float | Face quality score; decides whether the face takes part in clustering |
| Embedding | vector(512), HNSW index (vector_cosine_ops) | L2-normalized embedding, compared with cosine distance. Biometric data |
| CreatedUtc | timestamptz | Creation time |
| ClusteredUtc | timestamptz?, indexed with FaceModelId | When a clustering pass last used this face as a seed; null = not yet (new, or re-created by re-processing). Clustering seeds only from Unknown faces with null here and then sets it |

Deleting the image cascades; deleting the person sets PersonId to null; deleting a face model is restricted while faces reference it.

## People

A person, or an unnamed group created by clustering.

| Property | Type | Description |
|---|---|---|
| Id | int (PK) | Row identifier |
| Name | string?(200), indexed | Null = unnamed group |
| CoverFaceId | int? | Face shown for this person. No foreign key on purpose (avoids a Faces/People cycle); may be stale, so reads use it only while it is still one of the person's Suggested/Confirmed faces and otherwise fall back to the best one (Confirmed before Suggested, then quality) |
| CreatedUtc | timestamptz | Creation time |
| ModifiedUtc | timestamptz | Last modification time |

## Extensions

The database requires the PostgreSQL `vector` extension (pgvector), enabled by migration. It provides the `vector(512)` column type and the HNSW index on `Faces.Embedding`.
