# PictureManager — Docker Deployment Guide

This guide covers building and running PictureManager — web frontend, API,
and PostgreSQL — with Docker Compose. The frontend is built as static files
and served directly by the API from the same origin (`/`), while the REST
API stays under `/api`; there is no separate frontend container or port.

## Files involved

| File | Purpose |
|---|---|
| [Dockerfile](../Dockerfile) | Multi-stage build: builds the web frontend (Vite/React) as static files, publishes `PictureManager.Api`, and packages both into a runtime image that serves the frontend from `wwwroot` |
| [docker/entrypoint.sh](../docker/entrypoint.sh) | Container entrypoint: applies EF Core migrations, then starts the API |
| [docker-compose.prod.yml](../docker-compose.prod.yml) | Deployment stack: `db` (PostgreSQL) + `api` (PictureManager.Api + web UI) |
| [.env.prod.example](../.env.prod.example) | Template for the `.env` file the compose file reads |

`docker-compose.yml` (no suffix) is the existing **local-development** file —
PostgreSQL only, bound to `127.0.0.1`, meant to sit next to `dotnet run`. It is
unrelated to this deployment stack and untouched by this guide.

### nginx variant

There is a second deployment variant that fronts the API with nginx instead of
having Kestrel serve the frontend directly:

| File | Purpose |
|---|---|
| [Dockerfile.nginx](../Dockerfile.nginx) | Same build as `Dockerfile`, but the runtime stage adds nginx + supervisord |
| [docker/nginx.conf](../docker/nginx.conf) | Serves `wwwroot` and reverse-proxies `/api/` to Kestrel (loopback-only) |
| [docker/supervisord.conf](../docker/supervisord.conf) | Runs nginx and the API as sibling processes, restarting either on crash |
| [docker/entrypoint-nginx.sh](../docker/entrypoint-nginx.sh) | Applies migrations, then starts supervisord |
| [docker-image.nginx.yml](../docker-image.nginx.yml) | Compose file for this variant — same `.env` as `docker-compose.prod.yml` |

Run it with:

```bash
docker compose -f docker-image.nginx.yml up -d --build
```

Pick this over the default `Dockerfile`/`docker-compose.prod.yml` if you need
gzip/brotli compression, fine-grained cache-control per asset type, or plan to
put this behind something that expects a standard nginx front door. It costs
you a second in-container process (supervisord-managed) and a larger,
Debian-based runtime image (nginx isn't available on the `aspnet` image's
slim base without `apt-get install`). For a single-user/small-scale
deployment, the plain `Dockerfile` is simpler and sufficient.

## 1. Prerequisites

- Docker Engine 24+ and the Docker Compose plugin (`docker compose version`).
- A host directory containing your photo library (read-only mount).
- Enough disk space for the PostgreSQL volume and the thumbnail cache volume.

## 2. Configure

1. Copy the environment template and edit it:

   ```bash
   cp .env.prod.example .env
   ```

2. Edit `.env`:

   | Variable | Description |
   |---|---|
   | `POSTGRES_DB` / `POSTGRES_USER` | Database name and role PictureManager uses |
   | `POSTGRES_PASSWORD` | **Required.** Set a strong password — compose refuses to start without it |
   | `API_PORT` | Host port the API is published on (container listens on 8080 internally) |
   | `IMAGE_LIBRARY_PATH` | Absolute host path to your photo library, mounted read-only at `/data/images` |
   | `IMAGE_ROOT_NAME` | Display name for this image root |

   `.env` is gitignored — never commit real credentials.

3. Multiple image roots: `docker-compose.prod.yml` only wires up one root
   (`ImageRoots__0__*`) via `.env`. To add more, either edit the `api` service's
   `environment:` block directly with additional `ImageRoots__1__Name`,
   `ImageRoots__1__MountPath`, etc. (and a matching extra volume mount), or
   mount several source directories as subfolders under one
   `IMAGE_LIBRARY_PATH` and point a single root at the parent.

## 3. Build and start

From the repository root:

```bash
docker compose -f docker-compose.prod.yml up -d --build
```

This will:

1. Build the `api` image from `Dockerfile` in two stages: `npm ci && npm run
   build` for the web frontend, and `dotnet publish` plus a self-contained EF
   Core migration bundle for the API. The built frontend is copied into the
   final image's `wwwroot`.
2. Start `db` (PostgreSQL 17) and wait for it to report healthy.
3. Start `api`, which runs the migration bundle against `db` to bring the
   schema up to date, then launches `PictureManager.Api` — which now also
   serves the web UI.

Check status:

```bash
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs -f api
```

Verify the stack is up:

```bash
curl http://localhost:${API_PORT:-5080}/api/health
curl http://localhost:${API_PORT:-5080}/api/ping
```

Then open `http://localhost:${API_PORT:-5080}/` in a browser for the web UI.

## 4. Data persistence

Three named volumes are created:

- `pgdata` — PostgreSQL data directory.
- `thumbnail-cache` — generated thumbnails (`ThumbnailCache__RootPath=/data/thumbnail-cache`).
- `api-logs` — Serilog file output (`/app/logs`).

Your photo library itself is **not** a Docker volume — it's a bind mount from
`IMAGE_LIBRARY_PATH` on the host, mounted read-only. PictureManager never
writes into it.

## 5. Updating to a new version

```bash
git pull
docker compose -f docker-compose.prod.yml up -d --build
```

The entrypoint re-runs the migration bundle on every start, so any new EF Core
migrations shipped with the update are applied automatically before the API
starts serving traffic.

## 6. Stopping / removing

```bash
# Stop containers, keep data
docker compose -f docker-compose.prod.yml down

# Stop and delete all data (irreversible: database, thumbnails, logs)
docker compose -f docker-compose.prod.yml down -v
```

## 7. Troubleshooting

- **`api` exits immediately with "ConnectionStrings__PictureManagerDb is not
  set"** — `.env` wasn't loaded; make sure you run `docker compose` from the
  directory containing `.env`, or pass `--env-file`.
- **`api` fails on migrations** — check `docker compose -f docker-compose.prod.yml
  logs api`; the entrypoint stops before starting the API if the migration
  bundle fails, so the API never runs against a half-migrated schema.
- **Images don't show up** — confirm `IMAGE_LIBRARY_PATH` is an absolute path
  and that the container's `/data/images` mount actually contains files
  (`docker compose -f docker-compose.prod.yml exec api ls /data/images`).

## 8. Face recognition

PictureManager can detect faces and group them into people. Everything runs
inside the `api` container (ONNX, CPU only); no image or face data leaves the
server.

### Database image

The `db` service is now built from `docker/postgres/Dockerfile`
(`postgres:17-alpine` plus the pgvector extension) instead of pulling a stock
image. Existing `pgdata` volumes keep working: the OS family (Alpine) and text
collation are unchanged, so no dump/restore is needed.

- `docker compose pull` cannot pull the `db` service because it is built
  locally. Use `docker compose build db`, or
  `docker compose pull --ignore-buildable`.
- Moving a database between servers requires the **same PostgreSQL major
  version (17)** and **pgvector installed on the target** before you restore a
  dump that contains `vector` columns. The easiest way is to use this repo's
  `db` image on the target as well.

### Models and licence

The image downloads the InsightFace `buffalo_l` models (`det_10g.onnx`
detector and `w600k_r50.onnx` recognizer) at build time, verifies the zip
against a pinned SHA-256 (`BUFFALO_L_SHA256` in the `Dockerfile`), and stores
them in `/app/models/buffalo_l`. `FaceRecognition__ModelDirectory` points
there. **The InsightFace pretrained weights are for non-commercial use only.**

### Privacy

Face embeddings are biometric data. Treat database backups (and the
`pgdata` volume) as sensitive. Nothing is sent off the server.

### Tuning

Set these as environment variables on the `api` service. Defaults come from
`FaceRecognitionOptions`.

| Variable | Default | Meaning |
|---|---|---|
| `FaceRecognition__ReadConcurrency` | `2` | Images decoded (NAS reads) concurrently, on top of inference |
| `FaceRecognition__InferenceConcurrency` | half the CPU cores (at least 1) | Concurrent model executions (CPU-bound) |
| `FaceRecognition__DetectionThreshold` | `0.6` | Minimum detector score for a face |
| `FaceRecognition__MinFaceSizePx` | `40` | Faces whose shorter side is below this many pixels (in the decoded image, at most 1600 px) are ignored |
| `FaceRecognition__MaxAttempts` | `3` | Attempts per image before it is marked permanently failed |
| `FaceRecognition__ClusterDistance` | `0.5` | Cosine distance for grouping unassigned faces |
| `FaceRecognition__AutoMatchDistance` | `0.4` | Stricter cosine distance for attaching a new face to an existing person |
| `FaceRecognition__MinFacesPerGroup` | `3` | Minimum faces to form an unnamed group |
| `FaceRecognition__MinQualityForClustering` | `0.5` | Minimum face quality score to take part in clustering |
| `FaceRecognition__ModelDirectory` | `models/buffalo_l` (the image sets `/app/models/buffalo_l`) | Folder holding the two model files; relative paths resolve against the app's content root |

### Usage

1. Scan your library first; only scanned images are processed.
2. Choose **Recognize faces** in a folder's menu, or use the People page to
   process everything.
3. Progress shows in the banner. **Cancel** stops the job. A cancelled or
   interrupted job resumes where it stopped when you start it again (only the
   remaining images are processed).
4. On the People page, name an unnamed group to turn it into a person. Giving
   a group the name of an existing person merges it into that person, after a
   confirmation (a merge cannot be undone).
5. Images that cannot be decoded are listed at
   `GET /api/face-recognitions/failures`.

Only one job (scan, discovery or face recognition) runs at a time.

If the share holding a root is not mounted, a face job over that root fails at
once with "Root '…' is unavailable" and processes nothing. When only some of
the job's roots are unavailable, the others are processed and the job then
ends Failed naming the unavailable ones (same as a scan).

### How faces and people are kept up to date

- **Re-processing keeps your decisions.** An image is analysed again when its
  content hash changes (any metadata write such as keywords or rotation
  changes it) or the model changes. Its faces are re-created, and each new face
  whose box overlaps an old face (intersection-over-union ≥ 0.5, best overlap
  first, one-to-one) keeps that face's person and state, including Confirmed
  and Rejected. A face that no longer overlaps any old face starts Unassigned.
- **Clustering looks only at new faces.** After each job, faces that no earlier
  clustering pass has seen (`Faces.ClusteredUtc` is null) are first matched
  against existing people, then grouped into new unnamed people. A new face may
  still group with older unassigned faces (they remain neighbours), but an
  older face is never the starting point of a group again. Every face a pass
  looked at gets `ClusteredUtc` set, so a job's cost follows the number of new
  faces, not the size of the library. Faces existing before this column was
  added have it null, so the first job after the upgrade looks at all
  unassigned faces once.

### Troubleshooting

- **After updating, the `api` container restarts in a loop and the whole app is
  down; the log shows `extension "vector" is not available`.** The database
  still runs the old stock `postgres:17-alpine` image, which has no pgvector,
  so the face-recognition migration fails at `CREATE EXTENSION vector`. The
  entrypoint (`set -e`) stops before starting the API, so nothing is served,
  not just faces. The migration runs in a transaction, so the database is left
  untouched. Fix: rebuild and recreate the `db` service from this repo's image
  (the `pgdata` volume is kept):

  ```bash
  docker compose -f docker-compose.prod.yml up -d --build db
  ```

  The `api` container retries on its next restart and applies the migration.

### Development

- `tools/download-face-models.ps1` downloads the models to `./models/buffalo_l`
  (gitignored). The dev `appsettings.json` default points there.
- The dev database must use the pgvector image:
  `docker compose up -d --build db`.
