# PictureManager — Application Specification

## 1. Overview

**Application name:** PictureManager

**Purpose:** A web application to index, browse, search, and organize local image files (JPG, JPEG, PNG). 
Users configure which folders are scanned, browse images per folder, search by name/date, mark favorites, and organize images into albums.

**Architecture:** ASP.NET Core Web API backend + React (TypeScript) SPA frontend.

---

## 2. Tech Stack

- **Backend:** ASP.NET Core 10 Web API (C#)
- **ORM:** Entity Framework Core (PostgeSQL)
- **EXIF parsing:** `MetadataExtractor` NuGet package
- **Frontend:** React 18 + TypeScript + Vite
- **HTTP client:** Axios (or fetch)
- **State management:** React Query (TanStack Query) for server state
- **UI:** Component library of choice (e.g., MUI) — not mandated, but consistent grid/gallery layout required
- **API style:** REST, JSON

---

## 3. Domain Model

### 3.1 Entities

**Folder**
| Field | Type | Notes |
|---|---|---|
| Id | Guid/int | PK |
| Path | string | Absolute path, unique |
| IsActive | bool | Enable/disable scanning |
| LastScannedAt | DateTime? | |

**Image**
| Field | Type | Notes |
|---|---|---|
| Id | Guid/int | PK |
| FolderId | FK → Folder | |
| FileName | string | |
| FilePath | string | Full path, unique |
| Extension | string | jpg / jpeg / png |
| SizeBytes | long | |
| CreatedAt | DateTime | File system creation date |
| ModifiedAt | DateTime | File system last-write date |
| IsFavorite | bool | Default false |
| ExifDateTaken | DateTime? | From EXIF, JPG only |
| ExifCameraMake | string? | |
| ExifCameraModel | string? | |
| ExifWidth | int? | |
| ExifHeight | int? | |
| ExifOrientation | int? | |
| ExifRaw | string? (JSON) | Full EXIF dump for extensibility |

**Album**
| Field | Type | Notes |
|---|---|---|
| Id | Guid/int | PK |
| Name | string | |
| CreatedAt | DateTime | |

**AlbumImage** (join table)
| Field | Type | Notes |
|---|---|---|
| AlbumId | FK → Album | |
| ImageId | FK → Image | |
| AddedAt | DateTime | |

### 3.2 Relationships
- Folder 1—* Image
- Album *—* Image (via AlbumImage)

---

## 4. Backend Features

### 4.1 Folder Scanning Service
- Recursively walk a configured folder path.
- Filter by extension: `.jpg`, `.jpeg`, `.png` (case-insensitive).
- For each file: extract filename, full path, size, creation date, modified date.
- For JPG/JPEG: extract EXIF metadata (date taken, camera make/model, dimensions, orientation); store raw EXIF as JSON for future-proofing.
- Upsert into database (skip unchanged files based on path + modified date; add new files; optionally mark missing files as deleted/orphaned).
- Should be runnable as a background job (e.g., triggered via API call, or `IHostedService` for periodic rescans).

### 4.2 Folder Management
- CRUD for configured folders (add, list, deactivate/delete).
- Trigger a (re)scan for a specific folder or all folders.

### 4.3 Image Listing & Search
- List images by folder (paginated).
- Search by:
  - Name (partial match, case-insensitive)
  - Date range (creation date and/or EXIF date taken)
- Sort options: name, date, size.

### 4.4 Favorites
- Toggle favorite flag on an image.
- List all favorite images (optionally filterable by folder).

### 4.5 Albums
- CRUD for albums (create, rename, delete).
- Add image(s) to an album.
- Remove image(s) from an album.
- List images within an album.

---

## 5. REST API Endpoints (proposed)

### Folders
- `GET /api/folders` — list configured folders
- `POST /api/folders` — add a folder `{ path }`
- `PUT /api/folders/{id}` — update (e.g., activate/deactivate)
- `DELETE /api/folders/{id}` — remove folder config
- `POST /api/folders/{id}/scan` — trigger scan for one folder
- `POST /api/folders/scan-all` — trigger scan for all active folders

### Images
- `GET /api/images?folderId=&search=&dateFrom=&dateTo=&favoritesOnly=&page=&pageSize=&sortBy=` — list/search images
- `GET /api/images/{id}` — image details (incl. EXIF)
- `GET /api/images/{id}/file` — stream/serve the actual image file (for thumbnails/preview)
- `PATCH /api/images/{id}/favorite` — toggle favorite `{ isFavorite: bool }`

### Albums
- `GET /api/albums` — list albums
- `POST /api/albums` — create `{ name }`
- `PUT /api/albums/{id}` — rename `{ name }`
- `DELETE /api/albums/{id}` — delete album
- `GET /api/albums/{id}/images` — list images in album
- `POST /api/albums/{id}/images` — add image(s) `{ imageIds: [] }`
- `DELETE /api/albums/{id}/images` — remove image(s) `{ imageIds: [] }`

---

## 6. Frontend Features

### 6.1 Folder Management Page
- Table/list of configured folders with active/inactive toggle.
- "Add folder" form (path input).
- "Scan now" action per folder + "Scan all".
- Delete folder config.

### 6.2 Image Browser Page
- Select a folder → grid/gallery of thumbnails.
- Search bar (name) + date range filter.
- Sort control.
- Click image → detail/lightbox view showing metadata (EXIF, size, dates, path).
- Favorite toggle (star icon) on each thumbnail and in detail view.
- Multi-select mode: select multiple images → "Add to album" action.

### 6.3 Favorites View
- Grid of all favorited images across folders.

### 6.4 Albums Page
- List of albums with create / rename / delete actions.
- Click album → album detail view showing its images in a grid.
- "Add images" button → opens folder/image picker (reuses Image Browser in selection mode) to add images to the album.
- Select images within album → "Remove from album" action.

### 6.5 Navigation
- App shell with nav: Folders | Browse | Favorites | Albums.

---

## 7. Non-Functional Requirements

- Thumbnails should be generated or served efficiently (consider on-the-fly resizing or a thumbnail cache to avoid loading full-size originals in grid views).
- Pagination required for image lists (folders may contain thousands of files).
- Scanning should not block the API (run as async/background task; expose scan status/progress if feasible).
- CORS configured to allow the React dev server origin.
- Basic error handling and validation on all endpoints (e.g., invalid/non-existent folder paths).
- Configuration (allowed root paths, max upload/scan size, etc.) via `appsettings.json`.

---

## 8. Suggested Project Structure

```
/PictureManager
  /src
    /PictureManager.Api          # ASP.NET Core Web API project
      /Controllers
      /Services                  # FolderScanService, ExifService, etc.
      /Data                      # DbContext, EF migrations
      /Models                    # Entities
      /DTOs
    /PictureManager.Client       # React + TypeScript (Vite) app
      /src
        /pages                   # Folders, Browse, Favorites, Albums
        /components               # Gallery, ImageCard, AlbumList, etc.
        /api                      # API client functions
        /types
  PictureManager.sln
```

---

## 9. Out of Scope (for initial version)
- User authentication/authorization (single-user, local app assumption).
- Editing image content (crop/rotate/filters).
- Cloud storage support.
- Video file support.
