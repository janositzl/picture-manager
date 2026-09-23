# PictureManager Phase 5: REST API design

Status: **Draft, pending review** (2026-09-23)
Parent spec: [`2026-09-21-picturemanager-v1-design.md`](2026-09-21-picturemanager-v1-design.md), build
phase 5. Source brief: [`Documents/PictureManager-brief.md`](../../../Documents/PictureManager-brief.md).
This document records the phase 5 decisions made during brainstorming. Anything the brief or the v1
spec already settles is not restated here.

## Goal

Expose everything the phase 6 SPA needs as REST endpoints, so the frontend can be built without backend
changes:
- browsing the folder tree;
- the image grid, with search and favorites;
- the image viewer's detail data;
- albums (manage, reorder, export);
- the duplicate-finder view;
- settings;
- root administration.

Endpoints are grouped so that v2 authorization is a one-line addition per group, with no route changes.

## Architecture

- **Minimal API endpoints**, as the brief specifies and as `ScanEndpoints`/`ImageEndpoints` already do.
  Controllers were considered and rejected: the brief pins minimal APIs, and one style across the API
  matters more than controller conveniences.
- **Thin handlers over Application services.** Handlers bind input, call a service, and map the result
  to `TypedResults`. The logic lives in these `PictureManager.Application` services, each behind an
  interface:
  - `IImageQueryService`
  - `IFolderService`
  - `IImageRootService`
  - `IAlbumService`
  - `IDuplicateService`
  - `ISettingsService`
- **Repositories grow query methods.** List queries project straight into slim records in SQL, so a
  grid page never loads `Image.RawMetadata`.
- **Route groups.** Every endpoint is registered in exactly one of two `MapGroup` builders:
  - **user**: browse, favorite, albums, duplicates;
  - **admin**: roots, folder remove/restore, settings, scans.

  Paths stay resource-named, with no `/admin/` prefix. In v2, `.RequireAuthorization("AdminOnly")` goes
  on the admin group and a user policy on the user group. The existing `POST /api/scans` and
  `GET /api/scans/{id}/events` move into the admin group; their paths don't change.
- **`ICurrentUser`** (Application) answers "who is calling". The v1 implementation always returns the
  seeded system `AppUser` id. Album queries are always scoped to it, so v2 swaps only this
  implementation.

## Data model changes (one migration)

1. **`Image.SortDate`**: a persisted, stored computed column, `COALESCE("DateTaken", "FileModified")`.
   It is never written by application code.
2. **`ImageRoot.Alias`**: nullable, max 200.
3. **Indexes:**
   - `Images (FolderId, SortDate, Id)` and `Images (FolderId, lower(FileName), Id)`, for folder grid
     keyset pagination;
   - `Images (IsFavorite)`, partial on `IsFavorite = true`, for the favorites view;
   - `Images (ContentHash)`, if not already present, for the duplicate grouping;
   - unique `Albums (OwnerUserId, lower(Name))`;
   - unique `ImageRoots (lower(COALESCE(Alias, Name)))`, which enforces a unique export segment;
   - the existing case-sensitive unique index on `ImageRoots.Name` is replaced by a unique index on
     `lower(Name)`.

   The `lower(...)`/`COALESCE(...)` expression indexes and the partial index are written as raw SQL in
   the migration, since EF's fluent API doesn't express them.

`DateTaken` is local-naive (a camera clock with no timezone) and `FileModified` is UTC. Mixing them in
`SortDate` can put undated images a few hours out of place relative to dated ones. This is accepted: it
only affects the ordering of undated images.

## Visibility rules (shared by all listings)

An image is **visible** when all of these hold:
- `MissingSinceUtc IS NULL`;
- its folder is active (`IsActive = true`);
- its folder's root is active.

These are the only images counted, listed, searched or grouped. Every image list, folder
`imageCount`, and the duplicate view applies this rule. Album contents are the single exception (see
Albums).

**Unprocessed images** (empty `ContentHash`) are visible in the grid and in counts, with null thumbnail
and preview URLs. They are excluded from the duplicate view.

## Images

### `GET /api/images` (user)

| Param | Meaning |
|---|---|
| `folderId` | Images **directly** in this folder (no subfolders) |
| `folder` | Folder-name substring, `ILIKE`, over visible folders |
| `fileName` | File-name substring, `ILIKE` |
| `favoritesOnly` | `true` → favorites only (this alone is the Favorites view) |
| `sort` | `date` (default) or `name` |
| `order` | `desc` (default for `date`) / `asc` (default for `name`) |
| `cursor` | Opaque; from the previous page's `nextCursor` |
| `limit` | 1–200, default 100 |

- Filters combine with AND.
- **Keyset order:**
  - `date` sorts by `(SortDate, Id)`;
  - `name` sorts by `(lower(FileName), Id)`;
  - `Id` always breaks ties, in the same direction as the sort key.
- **Cursor.** It is base64url JSON `{ "s": sort, "o": order, "k": key, "i": id }`. Clients treat it as
  opaque. A cursor that doesn't decode, or whose `s`/`o` differ from the request's, gets a `400`.
- **Response:** `{ "items": [ImageListItem], "nextCursor": string | null }`. `nextCursor` is `null` on
  the last page. An empty result is `200` with `items: []`.
- A `folderId` that doesn't exist or is inactive (or whose root is inactive) → `404`.
- No total count (see Folders `imageCount` for the folder grid header).

**`ImageListItem`:**

```json
{
  "id": 4812, "folderId": 12, "fileName": "IMG_4471", "extension": ".jpg",
  "width": 4032, "height": 3024, "dateTaken": "2025-08-14T18:32:05",
  "isFavorite": true,
  "thumbnailUrl": "/api/images/4812/thumbnail?v=D5A2BEB00E25DDA3",
  "previewUrl": "/api/images/4812/preview?v=D5A2BEB00E25DDA3"
}
```

- `dateTaken` is serialized without an offset, because it is local-naive.
- `width`/`height` are null until enrichment runs.
- `thumbnailUrl`/`previewUrl` are null while `ContentHash` is empty. Otherwise they carry
  `?v={ContentHash}`, so the phase 4 `immutable` Cache-Control is correct: a changed file gets a new
  URL. The thumbnail/preview endpoints ignore `v`.

### `GET /api/images/{id}` (user)

This is the viewer detail. It contains the `ImageListItem` fields plus:
- `fileSize`, `fileModified`, `orientation`;
- `cameraMake`, `cameraModel`, `lensModel`, `latitude`, `longitude`;
- `rawMetadata`, as a JSON object;
- `folderPath`, the display path `"{root Name}/{RelativePath}"`;
- `albums: [{ id, name }]`, the current user's albums containing the image.

Unknown or not-visible → `404`.

### Favorites (user)

- `PUT /api/images/{id}/favorite` sets the favorite and `DELETE /api/images/{id}/favorite` clears it.
- Both are idempotent and return `204`.
- Unknown or not-visible → `404`.

## Folders

### User surface

| Route | Returns |
|---|---|
| `GET /api/folders/roots` | Root folder (`RelativePath = ""`) of each active root, as `FolderNode[]` ordered by name |
| `GET /api/folders/{id}/children` | Active child folders as `FolderNode[]`, ordered by `lower(Name)` |
| `GET /api/folders/{id}` | `{ id, name, rootId, rootName, relativePath, imageCount, breadcrumb: [{ id, name }] }` |

- `FolderNode` = `{ id, name, hasChildren, imageCount }`.
- `hasChildren` considers active children only.
- `imageCount` is the number of **visible images directly in the folder**, which is exactly what the
  grid shows for that folder. It is computed with one grouped count over the folders being returned.
- A folder that is unknown or inactive, or whose root is inactive → `404`.

### Admin surface

| Route | Effect |
|---|---|
| `DELETE /api/folders/{id}` | **Remove from collection** (see below). `204`. A root's top folder → `400`. Unknown or already removed → `404`. |
| `GET /api/folders/removed` | Tombstoned folders: `[{ id, name, rootName, relativePath }]` |
| `POST /api/folders/{id}/restore` | Sets `IsActive = true`, `204`. The next scan re-indexes it. Not tombstoned → `400`. |

**Remove from collection**, in one transaction:
1. The folder is kept as a tombstone with `IsActive = false`.
2. All `Folder` rows beneath it are hard-deleted (by `RelativePath` prefix within the same root).
3. The images of the folder and of its deleted subfolders are hard-deleted. `AlbumImage` rows go with
   them by cascade.

The scanner never re-creates or descends into a tombstoned folder. Restore reactivates only the
tombstone; its former subfolders come back when the next scan finds them on disk.

Thumbnail cache files belonging to purged images are left on disk. They are content-addressed and
harmless. Cache garbage collection is out of scope for v1.

## Roots

### Config seeding (replaces `DevImageRoot`)

```json
"ImageRoots": [
  { "Name": "nas-photos", "MountPath": "/images/photos", "Alias": "family_photos" }
]
```

At startup each entry is upserted by **`MountPath`**. The rule is: **config creates, the database is
the truth.**
- A root with that `MountPath` already exists → nothing changes. Config never overwrites `Name`,
  `Alias` or `IsActive`, because those may have been edited through the API.
- No such root → it is created, active, with the entry's `Name` and `Alias`.
- If the new root's `Name` or export segment (`Alias ?? Name`) clashes with an existing root, or the
  `Alias` is not a valid segment (see below):
  - the alias is dropped and a warning is logged;
  - if the `Name` itself clashes, the entry is skipped with a warning.

  Startup never fails because of root config.
- A database root absent from config is left untouched and a warning is logged. Roots are never
  deleted by config.

`appsettings.Development.json` replaces `DevImageRoot` with an `ImageRoots` entry for
`../../dev-data/images`. `DevImageRootSeeder` and `DevImageRootOptions` are replaced by
`ImageRootSeeder` and `ImageRootsOptions`.

### Admin API

| Route | Effect |
|---|---|
| `GET /api/roots` | All roots including inactive: `[{ id, name, alias, mountPath, isActive, exportSegment }]` |
| `PATCH /api/roots/{id}` `{ name?, alias?, isActive? }` | Partial update. `alias: null` clears the alias. `name` also renames the root's top folder. |

- **Deactivating** a root is reversible. Its rows are kept; its folders and images become not-visible,
  and scans skip it.
- **Validation:**
  - `name` and `alias` must be non-blank, at most 200 characters, and contain no `/` or `\` → otherwise
    `400`;
  - `name` must be unique, case-insensitive → otherwise `409`;
  - the resulting export segment `Alias ?? Name` must be unique across roots, case-insensitive →
    otherwise `409`.

## Albums (user surface; all scoped to `ICurrentUser`)

Another owner's album behaves as unknown (`404`).

| Route | Notes |
|---|---|
| `GET /api/albums` | `[{ id, name, description, imageCount, coverThumbnailUrl, updatedAt }]` ordered by `lower(name)` |
| `POST /api/albums` `{ name, description? }` | `201` + album. Name non-blank, ≤ 200. Duplicate name for owner (case-insensitive) → `409`. |
| `GET /api/albums/{id}` | `{ id, name, description, imageCount, createdAt, updatedAt }` |
| `PATCH /api/albums/{id}` `{ name?, description? }` | Same validation and `409` rule |
| `DELETE /api/albums/{id}` | `204`; `AlbumImage` rows cascade, images untouched |
| `GET /api/albums/{id}/images?cursor&limit` | Album contents, keyset by `(SortOrder, ImageId)` |
| `POST /api/albums/{id}/images` | `{ imageIds: int[] }` **or** `{ folderId }`, never both → `{ added, skipped }` |
| `POST /api/albums/{id}/images/remove` `{ imageIds: int[] }` | `204`; ids not in the album are ignored |
| `POST /api/albums/{id}/images/{imageId}/move` `{ afterImageId: int \| null }` | `204`. `null` moves it to the front. |
| `GET /api/albums/{id}/export?prefix=` | Plain-text export (below) |

- **`coverThumbnailUrl`** is the versioned thumbnail URL of the first image in album order that has a
  content hash, or `null`.
- **`imageCount`** counts all entries, including missing images.
- **Album contents** use the `ImageListItem` shape plus `isMissing`. Images that went missing on disk
  (`MissingSinceUtc` set), or whose folder or root is inactive, **stay listed** with `isMissing: true`
  and null URLs, so the user can see and remove them.
- **Adding images:**
  - New entries are appended after the current maximum `SortOrder`. With `imageIds` they keep the given
    order; with `folderId` they take that folder's visible direct images in `(SortDate, Id)` ascending
    order.
  - Images already in the album are skipped and counted in `skipped`.
  - Unknown or not-visible image ids → `400`, listing the offending ids, and nothing is added.
  - Unknown or inactive `folderId` → `404`.
- **Move** renumbers the album's `SortOrder` densely (0..n-1) in one statement. If `imageId` or
  `afterImageId` is not in the album, or `afterImageId == imageId` → `400`.
- Any change to an album's entries or fields updates `Album.UpdatedAt`.

### Export format

Response headers:
- `Content-Type: text/plain; charset=utf-8`;
- `Content-Disposition: attachment; filename="{album name}.txt"` (with the RFC 5987 `filename*` form
  for non-ASCII names).

Each line is:

```
{prefix without trailing '/'}/{Alias ?? Name}/{RelativePath}/{FileName}{Extension}
```

- One line per album entry, in `SortOrder`, including missing images. Every line ends with `\n`, and
  the text is UTF-8 without a BOM.
- The root segment is **always** present.
- An empty `RelativePath` drops its segment, so there is never an empty segment.
- `prefix` is optional and defaults to empty. Only trailing `/` characters are trimmed from it; its
  content is otherwise used verbatim.
- Separators are always `/`.

Examples, for a root with alias `family_photos` and a root named `work-nas` without an alias:

| prefix | line |
|---|---|
| *(empty)* | `/family_photos/IMG_0001.jpg` |
| `/mnt` or `/mnt/` | `/mnt/family_photos/Holidays/Madeira/IMG_4471.jpg` |
| *(empty)* | `/work-nas/2025/Q3/whiteboard.png` |

## Duplicates (user surface)

`GET /api/duplicates?cursor&limit` groups images that share a `ContentHash`.
- **Which images:** visible images with a non-empty hash only. A group needs at least 2 members.
- **Ordering:** by `count DESC, contentHash ASC`, keyset-paginated over groups (`limit` 1–100, default
  50).
- **Group shape:** `{ contentHash, count, images: [ImageListItem + folderPath] }`, with the images in a
  group ordered by `folderPath, fileName`.
- **Read-only.** The app never deletes files. The view exists for manual review only.
- **Known limitation:** the hash covers size plus the first and last 64 KB, so a group can in theory
  contain different files. This is acceptable because the view takes no action.

## Settings (admin surface)

- `GET /api/settings` returns `{ excludedFolderNames, excludedExtensions, includedExtensions }`.
  `includedExtensions` is `null` when every extension is allowed.
- `PUT /api/settings` replaces all three and returns the saved (normalized) settings plus
  `pruneOnNextScan`. That flag is `true` when the save newly excludes something, and the UI uses it to
  warn the user.
- **Normalization on save:**
  - trim every value and drop duplicates, case-insensitive;
  - lowercase extensions and give them a leading `.`.
- **Rejected with `400`:** blank values, and folder names containing `/` or `\`.

## Scanner changes

1. **Inactive roots are never scanned.**
   - An all-roots scan already filters on `IsActive`.
   - An explicit `rootId` naming an inactive root → `POST /api/scans` returns `400`.
2. **Tombstoned folders** (`IsActive = false`) are neither descended into nor re-created. The existing
   row is found by `GetByRootAndRelativePathAsync` and skipped.
3. **Prune on scan.** A scan whose settings now exclude something hard-deletes what it finds:
   - an existing `Image` row whose extension is no longer allowed is deleted (instead of being marked
     missing), and its `AlbumImage` rows cascade;
   - an existing `Folder` row whose name is now excluded is deleted together with its subtree, as in
     "remove from collection" steps 2–3, but **without** leaving a tombstone. The exclusion rule itself
     keeps it out, and removing the rule brings it back on the next scan.

## Errors

| Situation | Response |
|---|---|
| Invalid input (params, body, cursor) | `400` `ValidationProblem` (field → messages) |
| Unknown or not-visible resource | `404` |
| Name / alias / export-segment clash | `409` ProblemDetails |
| Unexpected exception | `500` ProblemDetails, logged via Serilog; no stack trace outside Development |

Handlers return `TypedResults`, so every response a handler can produce is part of its signature.

## Testing

- **Application services** (xUnit + FluentAssertions + NSubstitute):
  - cursor encode, decode and mismatch;
  - export line formatting (every case in the table above, plus empty `RelativePath` and a trailing-`/`
    prefix);
  - album add (append order, skip existing, reject invisible ids), move renumbering and remove;
  - settings normalization;
  - root seeding (create-only, match by `MountPath`, clash handling, absent-from-config warning);
  - root patch validation;
  - folder remove/restore rules.
- **Repositories against real Postgres.** This is new infrastructure; all existing repository tests use
  InMemory. Keyset queries, the computed `SortDate`, `lower()` ordering, the case-insensitive unique
  indexes, `ILIKE` and the duplicate `GROUP BY` are Postgres behaviour that InMemory doesn't reproduce.
  - A `PostgresDatabaseFixture` creates a uniquely named database on the compose `db` service (the
    connection string can be overridden by an environment variable), applies migrations, and drops the
    database when done.
  - These tests fail with a clear message if Postgres is unreachable.
  - The existing InMemory tests stay as they are.
- **Endpoint handlers:** unit tests as in phase 4.
- **`WebApplicationFactory` smoke tests** covering:
  - routing and binding for each resource group;
  - `TypedResults` status codes;
  - that every endpoint belongs to exactly one of the user or admin groups, which proves the v2 auth
    seam.
- **Scanner:** tests for the inactive-root rejection, tombstone skipping, and both prune cases.
- **Live verification** against the dev root at the end, as in phase 4: browse, list and paginate,
  favorite, album round-trip with export, duplicates, settings plus prune on rescan, root rename and
  alias.

## Out of scope for phase 5

- EXIF/date-range search.
- Trigram indexes (add only if `ILIKE` gets slow).
- Total counts on search results.
- Bulk favorite.
- Thumbnail cache garbage collection.
- Backslash-separator export.
- Deleting roots.
- Auth enforcement (the seam only).
