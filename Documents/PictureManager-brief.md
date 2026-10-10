# PictureManager — Project Brief

A web app for organizing images that live on a Windows NAS, shared over the network. It indexes folders and files into a catalog, extracts EXIF metadata, and layers albums and favorites on top — **without ever modifying the original files.**

Use this document as the spec for the app. Local-account authentication, user administration, album sharing and per-user favorites (see "Authentication") are implemented.

## Stack

| Layer | Choice |
|---|---|
| Backend | .NET 10, ASP.NET Core (minimal APIs) |
| ORM | EF Core 10 |
| Database | PostgreSQL |
| EXIF | MetadataExtractor |
| Imaging | ImageSharp (check license terms for commercial use) or SkiaSharp |
| Frontend | React + TypeScript + Vite + MUI |
| Server state | TanStack Query |
| Grid virtualization | TanStack Virtual or react-virtuoso |
| Deployment | Docker, single container (SPA served from the API's `wwwroot`) |
| Testing | xUnit, FluentAssertions, Playwright |
| Logging | Serilog |

## Core principle

Files on the NAS are the source of truth for pixels. The database is a catalog, never a copy. Every design decision below follows from that — the app is **strictly read-only** against the NAS: it never creates, renames, or deletes anything on the share, only within its own catalog.

## Solution structure

```
PictureManager/
├── PictureManager.Api            — REST API, image/thumbnail serving, search, albums, favorites
├── PictureManager.Application    — business logic: ImageService, FolderService, AlbumService,
│                                    FavoriteService, SearchService, ScanService (interfaces + impls)
├── PictureManager.Model          — Image, Folder, Album, AlbumImage, AppUser, ScanJob
├── PictureManager.Infrastructure — EF Core, Postgres, filesystem access, EXIF reader,
│                                    image resizing, thumbnail cache, logging
├── PictureManager.Worker         — class library referenced by Api, runs as an in-process
│                                    BackgroundService (NOT a separate container/process)
└── picturemanager.client         — React/TypeScript/Vite SPA
```

Use repository interfaces for data access and interfaces for all Application services (for testability and mocking in xUnit).

## Data model

```
Folder
  Id, ParentId, Name, RelativePath, IsActive, CreatedUtc, ModifiedUtc

Image
  Id, FolderId, FileName, Extension, ContentHash, PerceptualHash (nullable, unused in v1),
  FileSize, FileModified, Width, Height, Orientation,
  DateTaken, CameraMake, CameraModel, LensModel,
  Latitude, Longitude, RawMetadata (jsonb),
  (favorites are per user, in the UserFavorites table),
  IndexState, FirstSeenUtc, MissingSinceUtc,
  CreatedAt, UpdatedAt

Album
  Id, Name, Description, OwnerUserId (FK -> AppUser, NON-NULLABLE — see "Album ownership" below),
  CreatedAt, UpdatedAt

AlbumImage
  AlbumId, ImageId, SortOrder, AddedAt

AppUser
  Id, Username, NormalizedUsername (unique), DisplayName, Role, PasswordHash (nullable), IsActive,
  MustChangePassword, CanRunFolderActions, SecurityStamp, CreatedAt, LastLoginAt
```

Relationships: `Folder 1—* Image`, `Folder 1—* Folder` (parent/children), `Album 1—* AlbumImage *—1 Image`, `AppUser 1—* Album`.

**No logical photo grouping.** Each file on disk is its own `Image` record, independent of any other file with the same visual content — a RAW and its JPEG sibling, or a copy exported to another folder, are two rows. `ContentHash` is not used to merge them; it powers a separate duplicate-finder view (`GROUP BY ContentHash HAVING COUNT(*) > 1`) for manual review only.

**Reconciliation via `ContentHash`.** On scan:
- same path, same size + mtime → skip (cheap common case)
- same hash, new path → the file moved; update the path
- same path, changed mtime → re-extract EXIF, recalculate hash
- gone → set `MissingSinceUtc`, **don't delete** (a folder marked as deleted from the collection *does* cascade-delete its images/albumimages — that's an explicit user action, different from a transient missing file)

Hash cheaply: `size + first 64KB + last 64KB` via xxHash for identity checks; only full-hash if exact-duplicate detection needs it.

**Album ownership.** `Album.OwnerUserId` is non-nullable. User #1 is the initial `admin` account (seeded by the migration; it was the pre-accounts placeholder owner, so every existing album stays with it). New albums belong to the signed-in user.

## Folder management

- Multiple independent roots are supported — each with its own mount/volume and its own `ImageRoot`, not necessarily the same NAS share. One named Docker volume per root.
- Never store absolute paths. Store paths relative to each root's own `ImageRoot`:
  `physical: /images/Holidays/Madeira/IMG001.jpg` → `ImageRoot = /images`, stored path = `Holidays/Madeira/IMG001.jpg`
- A folder can be excluded from scan (config), or specific folder *names* excluded globally (e.g. `raw`, `backup`, `@eaDir`, `Thumbs.db`, `desktop.ini`, `.DS_Store`).
- Deleting a folder from the collection cascade-deletes its `Image` and `AlbumImage` rows for that folder and its subfolders.
- Extension include/exclude is a **scan-time filter**: an excluded extension (e.g. `.heic`) is invisible end-to-end — never scanned, never gets a `Folder`/`Image` row, not just hidden in the UI.

## Scanning pipeline (Worker)

Two phases so the UI has something to show quickly:

1. **Enumerate** — walk the tree, upsert path/size/mtime rows, mark new ones `Pending`. Fast, I/O bound.
2. **Enrich** — a `BackgroundService` pulls `Pending` rows off a `Channel<Guid>`, extracts EXIF, calculates the content hash, writes back `Indexed`. **Thumbnails are NOT generated here** — see below.

Scan can run single-level or recursive, on one or more selected folders. Expose scan status/progress (e.g. via SSE) so the client can show it live.

**`FileSystemWatcher` does not work reliably over a CIFS mount** — inotify doesn't cross the SMB boundary. Don't build the refresh story on it. Use a periodic rescan (e.g. every 10–15 min) plus a manual "Rescan" trigger.

## Thumbnails — lazy, not eager

Don't send an 8–20MB original to the browser to show a 200×200 thumbnail, and don't generate thumbnails during scan (keeps rescans cheap).

```
React → GET /api/images/{id}/thumbnail → ASP.NET Core
                                            ├── exists in cache? → yes → return
                                            └── no → generate → cache → return
```

Generate two derivatives per image on first request, cached from then on: a ~300px grid thumbnail and a ~1800px preview, both WebP. Store content-addressed with sharded directories (`/cache/a3/f9/a3f9c2...-300.webp`) so no folder ends up with hundreds of thousands of files, and a re-indexed file naturally gets a new cache entry (its hash changed). Apply EXIF orientation server-side; don't push that onto the browser.

Serve with `Results.File(path, contentType, enableRangeProcessing: true)`, and cache derivative URLs aggressively since they're content-addressed:
```
Cache-Control: private, max-age=31536000, immutable
```

## Listing & search

Use **keyset pagination**, not `Skip/Take` — offset pagination degrades badly on an infinite-scroll grid:
```sql
WHERE (TakenAtUtc, Id) < (@lastDate, @lastId) ORDER BY TakenAtUtc DESC, Id DESC
```

v1 search scope is intentionally narrow — folder name, file name, and favorites-only:
```
GET /api/images?folder=Vacation&fileName=IMG_4&favoritesOnly=true
```
`ILIKE '%term%'` is fine at this scale; add a `pg_trgm` trigram index on `FileName`/`RelativePath` if substring search gets slow. No EXIF/date-range search, no full-text engine in v1.

## Albums

- A user creates albums and assigns images from any folder, including bulk-assign from the folder view.
- An image can belong to multiple albums; an album cannot contain the same image twice.
- Images within an album have a user-settable `SortOrder`.
- Export: a text file, one image per line: `{prefix}{RelativePath}/{FileName}{Extension}` — prefix optional, defaults to empty. Plain UTF-8, `\n` line endings, in the album's own order.
- Albums are owned by the signed-in user (`OwnerUserId`).

## Authentication

Local accounts (no external IdP); there is no registration. Accounts are managed by an admin.
- Cookie session `pm.auth` (HttpOnly, SameSite=Strict, 14-day sliding); Data Protection keys live in Postgres.
- The initial `admin` account gets its password from `Auth__InitialAdmin__Password` on first start and must change it at first login.
- Surfaces (route groups in Program.cs, enforced server-side):
  - `user`: any signed-in user; browse, favorites, own albums, read-only job status.
  - `folderActions`: scans, discovery, face recognition, folder exclude/remove. Admins, plus users with `CanRunFolderActions`.
  - `admin`: roots, settings, removed folders, hide/rotate, people/face edits.
  - `auth`: login/logout/me/password.
- User administration: admins manage accounts at Admin → Users (create, edit name/role/active/folder-actions, reset password, delete with album transfer or delete). A new or reset account must change its password at first login. Disabling, resetting or deleting a user ends their sessions immediately. The last active admin and your own account are protected from being disabled, demoted or deleted. `GET /api/users/directory` lists active users for sharing (used from Phase 3).
- Sharing: owners share an album from its Share dialog as Can view (Viewer) or Can edit (Editor). Shared albums appear under "Shared with me". Viewers browse and export; editors also add, remove, reorder and set the cover; only the owner renames, deletes or shares. Anyone can leave an album shared with them. An album you have no access to is a 404; too little access is a 403.
- Favorites are per user (`UserFavorites`); existing favorites went to the initial admin when the feature was added.
- Design: docs/superpowers/specs/2026-10-09-user-management-design.md

## Infrastructure & deployment

Mount the NAS as a Docker volume — the container never speaks SMB/CIFS directly:

```yaml
volumes:
  images:
    driver: local
    driver_opts:
      type: cifs
      o: "username=${NAS_USER},password=${NAS_PASS},vers=3.0,ro,uid=1000,gid=1000,iocharset=utf8"
      device: "//nas-host/Images"
```

Mount **read-only**. One named volume per independent root. If Docker runs on the NAS itself, skip CIFS and bind-mount directly (`D:\Images:/images:ro`).

Single-container deploy: build the SPA into the API's `wwwroot` via a multi-stage Dockerfile (no CORS, no separate reverse proxy):

```dockerfile
FROM node:22-alpine AS ui
WORKDIR /ui
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY . .
RUN dotnet publish src/PictureManager.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app .
COPY --from=ui /ui/dist ./wwwroot
ENTRYPOINT ["dotnet", "PictureManager.Api.dll"]
```

`PictureManager.Worker` runs in-process inside this same container as a `BackgroundService` — it is **not** a second container/service in compose.

## Known gotchas (handle explicitly, don't rediscover the hard way)

- **Case sensitivity** — Windows paths are case-insensitive; the Linux container isn't. Normalize stored paths to lowercase for comparisons; keep the original for display.
- **Unicode normalization** — normalize filenames to NFC before storing, or accented characters (é, ő, ű, …) can produce phantom duplicates depending on which client wrote the file.
- **Path separators** — store relative paths with `/`; convert only at the filesystem boundary.
- **Timezones** — EXIF `DateTimeOriginal` has no timezone. Store as local-naive, not a UTC instant, or times will drift by hours.
- **SMB latency** — enumerating over CIFS is slow; batch DB writes, avoid stat'ing the same file twice per pass.

## v1 scope checklist

**Backend**
- [ ] ASP.NET Core + EF Core + PostgreSQL
- [ ] Multi-root, read-only NAS filesystem access
- [ ] EXIF extraction (MetadataExtractor)
- [ ] Content hashing (xxHash, cheap partial-hash strategy)
- [ ] Lazy thumbnail/preview generation, cached, content-addressed
- [ ] In-process background scanner (enumerate + enrich phases)
- [ ] REST API: folders, images, albums, favorites, basic search
- [x] Local-account auth: every endpoint behind a `user`/`folderActions`/`admin` policy
- [x] Seeded initial `admin` `AppUser` (owns albums)
- [ ] Serilog logging

**Frontend**
- [ ] React + TypeScript + Vite + MUI
- [ ] Folder tree (left) / image grid (right), virtualized
- [ ] Image viewer, sortable by name/date
- [ ] Favorites view (per-user favorites)
- [ ] Album create/manage/export
- [ ] Basic search bar (folder name, file name, favorites-only)
- [ ] Settings page: excluded folder names, excluded/included extensions


**Explicitly out of scope for v1:** perceptual/visual-similarity duplicate detection, EXIF/date-range search, full-text search engine.
