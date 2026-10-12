# Configurable Path Base (/pm) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let one PictureManager deployment be served under a sub-path (e.g. `http://servername/pm/`) behind a reverse proxy, while local dev and existing root deployments keep working unchanged.

**Architecture:** The base path is opt-in and defaults to `/`. Backend: an optional `PathBase` setting feeds `app.UsePathBase(...)`, which strips the prefix when present and passes un-prefixed requests through untouched. Frontend: the base is fixed at build time via `VITE_BASE_PATH` → Vite `base` → `import.meta.env.BASE_URL`; a single `withBase()` helper prefixes `/api/...` paths at the points where they hit the network (fetch, SSE, `<img src>`), and the router gets `basename`.

**Tech Stack:** ASP.NET Core 10 minimal APIs, xUnit + FluentAssertions; React 19, Vite, React Router, Vitest + msw; Docker / docker compose.

**Spec:** Conversation of 2026-10-12 (nginx proxy for `server/pm` → `server:5080`; approved change list: UsePathBase from config, Vite base, `withBase` helper, router basename, Docker build arg, compose settings, deployment docs).

## Global Constraints

- Default (no setting) behaviour must be byte-for-byte the current behaviour: dev URL stays `http://localhost:5173/`, API stays `/api/...`.
- API paths stay raw (`/api/...`) everywhere in code and in API responses (`ImageUrls.cs` unchanged); the prefix is added only where a URL is handed to the browser's network layer.
- One env value in `.env`: `PM_BASE_PATH` (e.g. `/pm`), empty = root.
- No database changes.

## Review Focus

1. Request without the prefix while `PathBase` is set (e.g. direct `server:5080/api/ping`) → still answered (UsePathBase passthrough). Test: smoke test `ApiPing_AnsweredWithAndWithoutPathBase`.
2. Routes not matching under a prefix because WebApplication's implicit `UseRouting` runs before `UsePathBase` → explicit `app.UseRouting()` after it. Test: same smoke test hitting `/pm/api/ping`.
3. Base path written with or without slashes (`pm`, `/pm`, `/pm/`, `/`, empty) → normalized identically. Tests: `PathBaseSettingTests`, and Vite normalization verified via build output.
4. An `<img>` site missed → broken thumbnails only under a prefix. Covered by enumerating every render site in Task 3 (grep list) and the `withBase` unit tests.
5. Face-crop override passed through `PhotoTile` getting prefixed twice → `faceThumbnailUrl()` stays raw; prefix only at render. Test: `withBase` is applied once per site (code review in Task 3).

---

### Task 1: Backend `PathBase` setting

**Files:**
- Create: `src/PictureManager.Api/PathBaseSetting.cs`
- Modify: `src/PictureManager.Api/Program.cs` (right after `app.UseForwardedHeaders();`)
- Modify: `tests/PictureManager.Api.Tests/Smoke/ApiSmokeFixture.cs` (set/clear `PathBase=/pm` env var)
- Create: `tests/PictureManager.Api.Tests/PathBaseSettingTests.cs`
- Create: `tests/PictureManager.Api.Tests/Smoke/PathBaseSmokeTests.cs`

**Interfaces:**
- Produces: `PathBaseSetting.Parse(string? value) : PathString` — empty/whitespace/`/` → `PathString.Empty`; otherwise leading `/` ensured, trailing `/` trimmed.

- [ ] Write `PathBaseSettingTests` (Theory: `null`, `""`, `" "`, `"/"` → empty; `"pm"`, `"/pm"`, `"/pm/"`, `" /pm/ "` → `/pm`; `"/a/b/"` → `/a/b`). Run → fails (type missing).
- [ ] Implement `PathBaseSetting`:

```csharp
public static class PathBaseSetting
{
    public static PathString Parse(string? value)
    {
        var trimmed = value?.Trim().Trim('/');
        return string.IsNullOrEmpty(trimmed) ? PathString.Empty : new PathString("/" + trimmed);
    }
}
```

- [ ] Program.cs after `app.UseForwardedHeaders();`:

```csharp
    // Optional sub-path hosting (e.g. PathBase=/pm behind a reverse proxy that forwards /pm unchanged).
    // Requests without the prefix still pass through, so direct access to the published port keeps working.
    app.UsePathBase(PathBaseSetting.Parse(app.Configuration["PathBase"]));
    // WebApplication otherwise adds routing at the very start of the pipeline, before UsePathBase has
    // stripped the prefix, and no /pm/... route would ever match.
    app.UseRouting();
```

- [ ] Fixture: `Environment.SetEnvironmentVariable("PathBase", "/pm")` in `InitializeAsync`, cleared in `DisposeAsync`.
- [ ] `PathBaseSmokeTests`: `/pm/api/ping` → 200 and `/api/ping` → 200; authenticated `/pm/api/auth/me`-style call through the admin client works.
- [ ] Run `dotnet test tests/PictureManager.Api.Tests` → all pass. Commit `feat(api): optional PathBase for sub-path hosting`.

### Task 2: Frontend build base + `withBase` helper

**Files:**
- Modify: `web/vite.config.ts` (`base` from `VITE_BASE_PATH`, proxy key `${base}api`)
- Modify: `web/src/api/client.ts` (export `withBase`, use in `apiFetch`/`apiFetchText`)
- Modify: `web/src/api/jobs.ts:153` (SSE URL)
- Modify: `web/src/App.tsx:11` (router `basename`)
- Test: `web/src/api/client.test.ts`

**Interfaces:**
- Produces: `withBase(path: string): string` — `import.meta.env.BASE_URL` without its trailing `/`, followed by `path`. `'/'` base → identity.

- [ ] Tests (use `vi.stubEnv('BASE_URL', '/pm/')` + `vi.unstubAllEnvs()` in `afterEach`): `withBase('/api/x')` → `/api/x` by default and `/pm/api/x` when stubbed; `apiFetch('/api/thing')` with base `/pm/` hits the msw handler `'/pm/api/thing'`. Run → fail.
- [ ] Implement:

```ts
/** Prefixes a root-relative API path with the app's base path (VITE_BASE_PATH at build time; "/" by default). */
export function withBase(path: string): string {
  return import.meta.env.BASE_URL.replace(/\/$/, '') + path
}
```

  and `fetch(new URL(withBase(path), window.location.origin), …)` in both fetch helpers; same in `jobs.ts`.
- [ ] `vite.config.ts`:

```ts
  // Sub-path hosting (e.g. VITE_BASE_PATH=/pm): normalized to "/pm/"; empty means "/".
  const trimmed = (env.VITE_BASE_PATH ?? '').trim().replace(/^\/+|\/+$/g, '')
  const base = trimmed === '' ? '/' : `/${trimmed}/`
  …
  base,
  server: { proxy: { [`${base}api`]: { target: … } } },
```

- [ ] `App.tsx`: `createBrowserRouter(appRoutes, { basename: import.meta.env.BASE_URL })`.
- [ ] `npm run test -- src/api` → pass. Commit `feat(web): build-time base path for sub-path hosting`.

### Task 3: Prefix image URLs at render sites

**Files (every place an API image URL reaches the browser):**
- `web/src/grid/PhotoTile.tsx:87` — `src={withBase(thumbnail)}` (covers `thumbnailOverride` face crops from `ImageBrowser`)
- `web/src/viewer/PhotoViewer.tsx:301` — `new Image().src = withBase(neighbour.previewUrl)`; `:339` — `src={withBase(src)}` (keep `key`, `loadedSrc` comparisons on the raw value)
- `web/src/people/SuggestedStrip.tsx:190` — `src={withBase(src)}`
- `web/src/people/PersonAvatar.tsx:11` — `src={withBase(faceThumbnailUrl(faceId))}`
- `web/src/viewer/PeopleInPhoto.tsx:89` — `src={withBase(faceThumbnailUrl(face.id))}`
- `web/src/albums/AlbumsPage.tsx:159` — `image={withBase(album.coverThumbnailUrl)}`

`faceThumbnailUrl()` stays returning a raw `/api/...` path so it is never prefixed twice.

- [ ] Apply the edits; `npm run test` → existing suites pass (base `/` → identity); `npm run lint`; `npm run build` with `VITE_BASE_PATH=/pm` and check `dist/index.html` references `/pm/assets/...` and `/pm/favicon.svg`. Commit `feat(web): prefix image URLs with the base path`.

### Task 4: Docker, compose, docs

**Files:**
- `Dockerfile`, `Dockerfile.nginx` — in `web-build` stage: `ARG VITE_BASE_PATH=` and `RUN VITE_BASE_PATH=${VITE_BASE_PATH} npm run build`.
- `docker-compose.prod.yml` — `api.build.args.VITE_BASE_PATH: ${PM_BASE_PATH:-}`, `api.environment.PathBase: ${PM_BASE_PATH:-}`.
- `docker-image.nginx.yml` — build arg only (its internal nginx serves files from root, so the external proxy must strip the prefix).
- `.env.prod.example` — `PM_BASE_PATH=` with comment.
- `Documents/PictureManager-Docker-Deployment-Guide.md` — new section "Hosting under a sub-path" with external nginx config for both variants.

- [ ] Edit, `docker compose -f docker-compose.prod.yml config` sanity check if docker is available. Commit `docs(deploy): sub-path hosting via PM_BASE_PATH`.
