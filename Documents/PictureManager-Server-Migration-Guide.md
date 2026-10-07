# PictureManager — Server Migration Guide

Moving a PictureManager deployment to a new server, keeping the same NAS
image library, without regenerating the thumbnail cache. Companion to
[PictureManager-Docker-Deployment-Guide.md](PictureManager-Docker-Deployment-Guide.md).

## Why this is safe to copy instead of regenerate

- **Thumbnails carry no server-specific data.** Cache files are keyed purely
  by content hash: `{hash[..2]}/{hash[2..4]}/{hash}-{size}.webp`
  ([ThumbnailCachePathResolver.cs](../src/PictureManager.Application/Thumbnails/ThumbnailCachePathResolver.cs)).
  Nothing in the path or file depends on the host, container, or database —
  copying the `thumbnail-cache` Docker volume byte-for-byte is equivalent to
  regenerating it.
- **The image library isn't touched by the app.** PictureManager is
  strictly read-only against the NAS (`:ro` mount), so there's nothing to
  migrate there — only the *mount path* needs to still resolve.
- **The database is the one piece with real migration risk** — it stores
  absolute folder/file paths under the image root, so the new server's NAS
  mount point must match (or you rewrite paths).

## Prerequisites

- Same Postgres major version on old and new server (both use `postgres:17-alpine`
  per [docker-compose.prod.yml](../docker-compose.prod.yml) — confirm the new
  server's compose file hasn't drifted).
- The NAS share is reachable from the new server and mounted at either the
  **same absolute path** as the old server, or you're prepared to rewrite
  paths in the DB (see Step 5).
- Docker + Docker Compose installed on the new server, repo checked out.

## Migration steps

### 1. Prepare the new server, don't start it yet

Check out the repo, copy `.env` (see
[.env.prod.example](../.env.prod.example)) with the same
`IMAGE_LIBRARY_PATH`, `IMAGE_ROOT_NAME`, `POSTGRES_*` values as the old
server. Build the image but don't bring up `api` yet:

```bash
docker compose -f docker-compose.prod.yml build
docker compose -f docker-compose.prod.yml up -d db
```

### 2. Dump the database on the old server

Use `pg_dump`/`pg_restore` rather than copying the raw `pgdata` volume —
it's version-safe and avoids copying Postgres's on-disk file format across
machines:

```bash
docker compose -f docker-compose.prod.yml exec db \
  pg_dump -U picturemanager -Fc picturemanager > picturemanager.dump
```

Copy `picturemanager.dump` to the new server (`scp`, rsync, etc.).

### 3. Restore the database on the new server

```bash
docker compose -f docker-compose.prod.yml exec -T db \
  pg_restore -U picturemanager -d picturemanager --clean --if-exists < picturemanager.dump
```

`--clean --if-exists` lets this run safely even though `up -d db` already
created an empty schema via a prior migration run — drop the empty schema
objects first if `pg_restore` complains about ownership.

### 4. Copy the thumbnail cache volume

Both servers must use the same named volume identity (`thumbnail-cache` per
the compose file). On the **old** server, export it:

```bash
docker run --rm \
  -v picturemanager_thumbnail-cache:/from \
  -v "$PWD":/backup alpine \
  tar czf /backup/thumbnail-cache.tgz -C /from .
```

Copy `thumbnail-cache.tgz` to the new server, then import it into the
volume Compose creates on first `up`:

```bash
docker compose -f docker-compose.prod.yml up -d --no-start api   # creates the volume
docker run --rm \
  -v picturemanager_thumbnail-cache:/to \
  -v "$PWD":/backup alpine \
  tar xzf /backup/thumbnail-cache.tgz -C /to
```

(Volume name is prefixed with the Compose project name — check the actual
name with `docker volume ls` if it doesn't match `picturemanager_thumbnail-cache`.)

Skip this step and let the API regenerate the cache instead if the transfer
would take longer than a regeneration would — for a small/moderate library
copying is faster, but weigh it if the cache is huge and bandwidth between
servers is poor.

### 5. Verify the image mount path matches

If `IMAGE_LIBRARY_PATH` on the new server mounts the NAS at the exact same
path as the old server, nothing else is needed. If the mount point differs,
the `Folder`/`Image` rows in the restored DB will point at paths that no
longer exist on disk, and PictureManager will flag the whole library as
missing (`MissingSinceUtc`, see
[VisibilityExtensions.cs](../src/PictureManager.Infrastructure/Persistence/Queries/VisibilityExtensions.cs)).
Either:
- match the old mount path exactly, or
- run a one-off SQL path rewrite against the restored DB before starting
  the API (update the path prefix on `Folder`/`Image` rows) — do this as a
  deliberate, reviewed step, not ad hoc, since it touches every row.

### 6. Start the API and verify

```bash
docker compose -f docker-compose.prod.yml up -d
```

`entrypoint.sh` applies any pending EF Core migrations before starting, so
this is safe even if the new server's schema is slightly behind.

Checklist:
- [ ] App loads, folder tree and grid show the existing library (no mass
      "missing" flags)
- [ ] Thumbnails render immediately for already-scanned images — confirms
      the cache copy worked, not a silent regeneration
- [ ] Spot-check a handful of thumbnail files exist in the new
      `thumbnail-cache` volume under their expected shard path
- [ ] Trigger a scan and confirm no unexpected full-library rescan is queued

### 7. Decommission the old server

Only after the above checklist passes and you've run the new server for a
reasonable soak period. Keep the `picturemanager.dump` and
`thumbnail-cache.tgz` backups until you're confident.
