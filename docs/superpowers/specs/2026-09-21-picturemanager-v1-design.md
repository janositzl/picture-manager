# PictureManager v1 — Design

Status: **Approved** (2026-09-21)
Source spec: [`Documents/PictureManager-brief.md`](../../../Documents/PictureManager-brief.md) — this document
records the build-order decisions and the handful of details the brief leaves open. It does not restate
what the brief already fully specifies (data model, scanning pipeline, thumbnail strategy, infra) —
read the brief alongside this doc.

## Stack (as specified, plus client-side additions)

Backend/DB/imaging/deployment stack is exactly as the brief's table. Frontend additions on top of
React + TypeScript + Vite + MUI:

- **Tailwind CSS** for utility styling, used alongside MUI. Tailwind's `preflight` base-reset is disabled
  so it doesn't fight MUI's `CssBaseline`. Division of labor: **Tailwind utility classes style non-MUI
  layout only** (wrapper `div`s, page structure); **MUI's own `sx` prop styles MUI components**. This isn't
  a preference — MUI/Emotion injects unlayered CSS, which always wins the cascade over anything Tailwind
  puts inside a `@layer` regardless of specificity, so a Tailwind class on an MUI component (e.g.
  `<Typography className="mb-4">`) silently does nothing. Revisit with `<StyledEngineProvider
  enableCssLayer>` + an explicit `@layer theme, mui, utilities;` order once Playwright (phase 8) can verify
  rendered output in a real browser.
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
   Revisit in v2 if a "download original" feature is wanted. (Narrow, deliberate exception added in phase
   4 — see decision 11 — for a per-deployment opt-out that serves the original only when preview
   generation is turned off.)
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
6. **`ImageRoot` entity** (not in the brief's data model, needed to make "multiple independent roots"
   actually work): `Id, Name, MountPath, IsActive, CreatedUtc`. `MountPath` is the container-visible
   physical path (e.g. `/images` or `/images/holidays-nas`) that `Folder.RelativePath` values under this
   root are relative to. Every `Folder` row — top-level and nested alike — carries a required
   `Folder.RootId` FK, set once at creation by copying the parent's `RootId` (or the literal value for a
   new top-level folder). Denormalized deliberately: resolving "which root does this deeply-nested folder
   belong to" via a plain column beats a recursive walk up `ParentId` on every scan/query. Without this FK
   at all, there's no way to resolve a stored relative path back to a physical one when more than one root
   is registered — the brief names the multi-root feature but its listed `Folder` columns alone can't
   support it.
7. **Thumbnail cache storage** (phase 4): a new `ThumbnailCache:RootPath` config value (appsettings +
   docker-compose volume, same pattern as `ImageRoot.MountPath` — an infra/deployment concern, not a
   DB-editable setting). Sharded file layout exactly as the brief's example:
   `{root}/{hash[0..2]}/{hash[2..4]}/{hash}-{size}.webp`. No DB migration needed — the cache key derives
   entirely from `Image.ContentHash` plus the requested size (`Thumbnail` = 300, `Preview` = 1800, longest
   edge), so nothing new needs persisting.
8. **Resize basis** (phase 4): the longest edge of the source image is scaled to the target size (300 or
   1800), not a fixed width — this treats portrait and landscape photos uniformly. EXIF orientation
   correction uses the already-stored `Image.Orientation` column from phase 3's scan (no second EXIF read
   at generation time).
9. **Concurrent-generation dedup** (phase 4): an in-process keyed async lock (key = `{hash}-{size}`)
   prevents duplicate decode/encode work when multiple requests race for the same missing derivative.
   Generation writes to a temp file in the same shard directory, then atomically renames into place, so a
   third concurrent reader never observes a partially-written cache file.
10. **Missing/undecodable source** (phase 4): both `/thumbnail` and `/preview` return 404 when the `Image`
    row doesn't exist, `MissingSinceUtc` is set, or the source file fails to decode. No placeholder image
    is served server-side — that's a frontend concern for a later phase.
11. **Preview generation toggle** (phase 4): a new static config value, `ThumbnailCache:PreviewEnabled`
    (default `true`). Thumbnails are never affected by this toggle — they're always generated, since
    serving multi-megabyte originals into a scrolling grid of hundreds of tiles would be far too slow. When
    `PreviewEnabled` is `false`, the `/preview` endpoint skips derivative generation entirely and serves the
    original file directly: `Results.File(path, contentType, enableRangeProcessing: true)` with
    `Cache-Control: private, max-age=300` (short, no `immutable` — unlike the generated-derivative path,
    the file behind this id-keyed URL can legitimately change after an edit + rescan, so it must revalidate
    periodically rather than being cached for a year). `contentType` comes from a small, unit-testable
    `ImageContentTypeResolver.Resolve(extension)` pure function (`.jpg`/`.jpeg` → `image/jpeg`, `.png` →
    `image/png`, `.heic` → `image/heic`, `.webp` → `image/webp`, `.gif` → `image/gif`, `.bmp` →
    `image/bmp`, `.tiff`/`.tif` → `image/tiff`, else `application/octet-stream`).

## Build phases

Each phase gets a short "here's what landed" checkpoint before moving to the next — not a full
re-approval, per your direction to proceed continuously.

1. **Scaffold** — solution/projects, git init (done), Serilog, docker-compose (Postgres + dev image
   bind-mount), EF Core + Npgsql wired to `PictureManagerDb`, web/ Vite+React+TS+MUI+Tailwind+ESLint+
   Prettier scaffold with env var support.
2. **Data model & persistence** — entities, EF configurations, migrations, seeded placeholder `AppUser`,
   repository interfaces + impls, xUnit coverage. **No live database in this phase**: migrations are
   generated (`dotnet ef migrations add`) and reviewed, but not applied against a running Postgres — the
   `db` container from phase 1 stays stopped throughout. Repository tests use EF Core's InMemory provider
   for query-logic coverage; the `RawMetadata jsonb` column and any other Postgres-specific behavior aren't
   exercised by InMemory, so they're unverified until phase 3, which actually needs a running database to
   persist scan results and is the first phase to run `dotnet ef database update` for real.
3. **Scanning pipeline** — enumerate/enrich phases, `Channel<Guid>` background enrichment, MetadataExtractor
   EXIF, xxHash partial-hash reconciliation, path normalization (NFC, case-insensitive compare, `/`
   storage), exclude rules, SSE progress, unit tests for path/hash/reconciliation logic. First phase to
   start phase 1's `db` container and run `dotnet ef database update` against a live Postgres — applies
   phase 2's migrations for real and is where any InMemory-vs-Postgres surprises (e.g. `jsonb` mapping)
   surface.
4. **Thumbnails & image serving** — content-addressed sharded WebP cache, EXIF-orientation-corrected
   derivatives, range-enabled serving, immutable cache headers.
5. **REST API** — folders, images (keyset pagination/search/favorite), albums (CRUD/reorder/export),
   duplicate-finder view, settings; routes shaped for a later `RequireAuthorization` drop-in. Detailed
   decisions: [`2026-09-23-phase5-rest-api-design.md`](2026-09-23-phase5-rest-api-design.md).
   Follow-up before phase 6 (missing folders, unavailable roots, background scans; rename/relink deferred):
   [`2026-09-24-scanner-missing-folders-design.md`](2026-09-24-scanner-missing-folders-design.md).
6. **Frontend SPA** — folder tree + virtualized grid, viewer, favorites, albums, search bar, settings page.
   Split into 6a Browse, 6b Organize and 6c Admin, each with its own spec. 6a:
   [`2026-09-24-phase6a-spa-browse-design.md`](2026-09-24-phase6a-spa-browse-design.md).
7. **Docker integration pass** — multi-stage build, full `docker compose up` smoke test end-to-end.
8. **Playwright e2e** — browse → view → favorite → album → export flows.

## Testing strategy

xUnit + FluentAssertions from phase 2 onward, written alongside the code (TDD). Playwright e2e deferred to
phase 8, once the SPA has real screens to drive against.

## Out of scope (per brief)

Auth/roles enforcement, per-user favorites, perceptual/visual-similarity duplicate detection, EXIF/date-range
search, full-text search engine.
