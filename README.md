# PictureManager

A self-hosted web app for browsing and organizing photo libraries that live on a NAS or other network share. It indexes folders and files into a catalog, extracts EXIF metadata, and layers albums, favorites, and duplicate detection on top — **without ever modifying the original files.** The database is a catalog, never a copy; PictureManager is strictly read-only against your photo library.

## Motivation

The problem that started this project: building a "best of" selection from a trip or occasion normally means copying the chosen images out of their original folders into a separate selection folder — which duplicates the files on disk. PictureManager avoids that by letting you build an **album** that references the original images in place, with no copying. The album can then export a plain-text file list (one image path per line) that's used directly as an input playlist for a **Kodi** picture viewer — so a "best of" selection stays a lightweight pointer into the existing library instead of a duplicated copy of it.

## Features

- **Folder-based browsing** — a virtualized folder tree on the left, a virtualized photo grid on the right, backed by a Postgres catalog rather than live filesystem scans.
- **Background discovery & scanning** — a two-phase pipeline (cheap folder discovery, then per-folder image scan) runs as an in-process background worker, with a scan queue, job status, and progress reporting.
- **EXIF-aware indexing** — extracts camera, lens, date-taken, and GPS metadata on scan.
- **Content-hash reconciliation** — detects moved, renamed, changed, and missing files across rescans without re-processing untouched images.
- **Lazy, content-addressed thumbnails** — grid and preview derivatives are generated on first request and cached, not during scan.
- **Albums** — create albums, assign images from any folder, and reorder them.
- **Kodi-ready export** — export an album as a plain-text file list (one image path per line), usable directly as an input playlist for a Kodi picture viewer — no duplicated files, no copying into a "best of" folder.
- **Favorites** — mark images as favorites for quick access.
- **Duplicate finder** — surfaces files that share a content hash for manual review.
- **Multi-root support** — register multiple independent NAS/local roots, each mounted read-only.
- **Admin settings** — manage roots, excluded folder names/extensions, and removed folders.

## Architecture

```
PictureManager/
├── src/
│   ├── PictureManager.Api             REST API, image/thumbnail serving, search, albums, favorites
│   ├── PictureManager.Application     business logic (folders, scanning, discovery, albums,
│   │                                   duplicates, thumbnails, settings) — interfaces + implementations
│   ├── PictureManager.Model           entities: Folder, Image, Album, AlbumImage, AppUser, Job, ...
│   ├── PictureManager.Infrastructure  EF Core + PostgreSQL, filesystem access, EXIF reading,
│   │                                   image resizing, thumbnail cache
│   └── PictureManager.Worker          background services (discovery/scan/enrichment queues),
│                                       runs in-process inside the API host — not a separate container
├── web/                                React 19 + TypeScript + Vite SPA (MUI, Tailwind CSS, TanStack Query)
├── tests/                              xUnit test projects, one per backend project
└── docker/                             entrypoint scripts and nginx config for deployment
```

| Layer | Stack |
|---|---|
| Backend | .NET 10, ASP.NET Core Minimal APIs |
| ORM | Entity Framework Core 10 |
| Database | PostgreSQL |
| Frontend | React 19, TypeScript, Vite, MUI, Tailwind CSS v4 |
| Frontend server state | TanStack Query |
| Grid virtualization | TanStack Virtual |
| Logging | Serilog |
| Testing | xUnit, FluentAssertions, NSubstitute (backend) · Vitest, Testing Library (frontend) |
| Deployment | Docker Compose, single container serving the SPA from the API's `wwwroot` |

## Getting started

### Prerequisites

- .NET 10 SDK
- Node.js 22+
- Docker (for PostgreSQL locally, or for a full deployment)

### Local development

1. Start a local PostgreSQL instance:

   ```bash
   docker compose up -d
   ```

2. Run the API (applies EF Core migrations and starts the background worker in-process):

   ```bash
   dotnet run --project src/PictureManager.Api
   ```

3. Run the frontend dev server:

   ```bash
   cd web
   npm install
   npm run dev
   ```

### Tests

```bash
dotnet test
cd web && npm run test
```

### Docker deployment

A production deployment builds the SPA and API into a single container alongside PostgreSQL:

```bash
cp .env.prod.example .env
# edit .env: POSTGRES_PASSWORD, IMAGE_LIBRARY_PATH, IMAGE_ROOT_NAME, ...
docker compose -f docker-compose.prod.yml up -d --build
```

An nginx-fronted variant (gzip/brotli, fine-grained cache-control) is also available — see [Documents/PictureManager-Docker-Deployment-Guide.md](Documents/PictureManager-Docker-Deployment-Guide.md) for both variants and full configuration options.

## Documentation

- [Documents/PictureManager-brief.md](Documents/PictureManager-brief.md) — project brief and design decisions
- [Documents/Database-Schema.md](Documents/Database-Schema.md) — database schema
- [Documents/PictureManager-Folder-Discovery-and-Scanning.md](Documents/PictureManager-Folder-Discovery-and-Scanning.md) — folder discovery and scan pipeline design
- [Documents/PictureManager-Docker-Deployment-Guide.md](Documents/PictureManager-Docker-Deployment-Guide.md) — deployment guide

## Status

v1 in active development. No authentication yet — endpoints are structured so a `RequireAuthorization` policy layer can be added later without reshaping routes.
