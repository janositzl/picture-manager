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
