# PictureManager v1 — Design

Status: **Approved** (2026-09-21)
Source spec: [`Documents/PictureManager-brief.md`](../../../Documents/PictureManager-brief.md) — this document
records the build-order decisions and the handful of details the brief leaves open. It does not restate
what the brief already fully specifies (data model, scanning pipeline, thumbnail strategy, infra) —
read the brief alongside this doc.

## Stack (as specified, plus client-side additions)

Backend/DB/imaging/deployment stack is exactly as the brief's table. Frontend additions on top of
React + TypeScript + Vite + MUI:

- **Tailwind CSS** for utility styling, used alongside MUI (MUI for components, Tailwind for layout/spacing
  utility classes). Tailwind's `preflight` base-reset is disabled so it doesn't fight MUI's CssBaseline.
- **ESLint + Prettier**, configured together (`eslint-config-prettier` to disable stylistic ESLint rules
  Prettier owns), TypeScript-aware (`typescript-eslint`).
- **Environment variable support** via Vite's `import.meta.env` / `.env` files (`.env`, `.env.development`,
  `.env.production`), with an `.env.example` committed and real `.env*` files gitignored.

## Repository layout

```
PictureManager/
├── PictureManager.sln
├── src/
│   ├── PictureManager.Api/            — minimal API host, composition root
│   ├── PictureManager.Application/    — service interfaces + impls
│   ├── PictureManager.Model/          — entities (POCO, no EF attributes)
│   ├── PictureManager.Infrastructure/ — EF Core, Postgres, filesystem, EXIF, imaging, cache, logging
│   └── PictureManager.Worker/         — BackgroundServices, referenced by Api (in-process)
├── web/                                — React/TS/Vite SPA (matches Dockerfile's COPY paths)
├── tests/
│   ├── PictureManager.Application.Tests/
│   ├── PictureManager.Infrastructure.Tests/
│   └── PictureManager.E2E/            — Playwright, added once SPA has real screens (phase 8)
├── dev-data/images/                    — local dev ImageRoot, bind-mounted read-only; gitignored contents
├── docker-compose.yml                  — Postgres + app, for local/dev and for the full-stack smoke test
├── Dockerfile
└── docs/superpowers/specs/
```

## Decisions not fully pinned down by the brief

1. **Imaging library**: **SkiaSharp** (MIT license, no commercial-use gate), not ImageSharp — the brief
   flagged ImageSharp's license as something to check; SkiaSharp sidesteps that.
2. **No raw-original serving endpoint in v1.** The brief only specifies thumbnail (~300px) and preview
   (~1800px) WebP derivatives, generated lazily and cached. The image viewer uses the preview derivative,
   not the original file. Keeps the "never touch the NAS beyond reading for indexing" boundary tight.
   Revisit in v2 if a "download original" feature is wanted.
3. **`ScanJob` entity** (referenced in the brief's solution-structure comment but not detailed in the data
   model section): `Id, RootFolderId (nullable = all roots), IsRecursive, Status (Pending/Enumerating/
   Enriching/Completed/Failed/Cancelled), StartedUtc, CompletedUtc, FoldersScanned, FilesFound,
   FilesEnriched, ErrorMessage`. Drives the SSE progress feed.
4. **`AppSettings` singleton row** (DB-backed, not in the brief's data model): holds excluded folder names
   and extension include/exclude lists, editable from the Settings page without a redeploy. Single row,
   fixed `Id`, cached in memory by the scanner, invalidated on save.
5. **Dev ImageRoot**: `dev-data/images/` inside the repo, bind-mounted read-only into the app container
   for local dev, mirroring the brief's "Docker runs on the NAS itself → bind-mount directly" case. Real
   CIFS-mounted volumes are a production concern, documented but not exercised locally.

## Build phases

Each phase gets a short "here's what landed" checkpoint before moving to the next — not a full
re-approval, per your direction to proceed continuously.

1. **Scaffold** — solution/projects, git init (done), Serilog, docker-compose (Postgres + dev image
   bind-mount), EF Core + Npgsql wired to `PictureManagerDb`, web/ Vite+React+TS+MUI+Tailwind+ESLint+
   Prettier scaffold with env var support.
2. **Data model & persistence** — entities, EF configurations, migrations, seeded placeholder `AppUser`,
   repository interfaces + impls, xUnit coverage.
3. **Scanning pipeline** — enumerate/enrich phases, `Channel<Guid>` background enrichment, MetadataExtractor
   EXIF, xxHash partial-hash reconciliation, path normalization (NFC, case-insensitive compare, `/`
   storage), exclude rules, SSE progress, unit tests for path/hash/reconciliation logic.
4. **Thumbnails & image serving** — content-addressed sharded WebP cache, EXIF-orientation-corrected
   derivatives, range-enabled serving, immutable cache headers.
5. **REST API** — folders, images (keyset pagination/search/favorite), albums (CRUD/reorder/export),
   duplicate-finder view, settings; routes shaped for a later `RequireAuthorization` drop-in.
6. **Frontend SPA** — folder tree + virtualized grid, viewer, favorites, albums, search bar, settings page.
7. **Docker integration pass** — multi-stage build, full `docker compose up` smoke test end-to-end.
8. **Playwright e2e** — browse → view → favorite → album → export flows.

## Testing strategy

xUnit + FluentAssertions from phase 2 onward, written alongside the code (TDD). Playwright e2e deferred to
phase 8, once the SPA has real screens to drive against.

## Out of scope (per brief)

Auth/roles enforcement, per-user favorites, perceptual/visual-similarity duplicate detection, EXIF/date-range
search, full-text search engine.
