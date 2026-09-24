# Phase 6a: SPA Browse Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A desktop SPA that browses the indexed photo collection. It has a folder tree, a virtualized photo grid, a full-screen keyboard-driven viewer, favorites, and file-name search. The backend gains one field, `folderPath` on image list items.

**Architecture:**
- **The URL is the state.** Routes plus `sort`/`order`/`image`/`q`/`in` search params hold everything the user sees, and one helper parses and serializes them.
- **Server data.**
  - TanStack Query caches every API response, and `useInfiniteQuery` follows the API's `nextCursor`.
  - One `useImages(filter)` hook feeds every grid, and the viewer reads the same cache.
  - The favorite toggle is one optimistic mutation that patches every cached copy of the image.
- **Rendering.**
  - React Router gives one layout route (`AppShell`) with the tree and the app bar, and child routes for the grid views.
  - TanStack Virtual virtualizes the grid rows.
  - The viewer is an MUI full-screen `Dialog` rendered by the grid view whenever `?image=` is set.

**Tech Stack:**
- React 19 + TypeScript 6 (strict) + Vite 8, with MUI 9 and Tailwind 4 (already scaffolded).
- react-router 8, @tanstack/react-query 5, @tanstack/react-virtual 3.
- Vitest 5 + jsdom + React Testing Library + user-event + MSW 2.
- Backend: .NET 10, xUnit, FluentAssertions 7, NSubstitute, Postgres test databases.

**Spec:** `docs/superpowers/specs/2026-09-24-phase6a-spa-browse-design.md`

## Global Constraints

- **Target and theme.**
  - Desktop browser only; no mobile or tablet layouts. The UI is in English.
  - The theme follows the OS light/dark setting (`createTheme({ colorSchemes: { light: true, dark: true } })`). There is no manual toggle.
- **New runtime dependencies, exactly these:**
  - `react-router@^8.4.0`
  - `@tanstack/react-query@^5.103.2`
  - `@tanstack/react-virtual@^3.14.13`
- **New dev dependencies, exactly these:**
  - `vitest@^5.0.1`
  - `jsdom@^30.1.1`
  - `@testing-library/react@^16.3.3`
  - `@testing-library/jest-dom@^7.0.1`
  - `@testing-library/user-event@^14.6.7`
  - `msw@^2.15.0`

  No other packages; in particular, not `@mui/x-tree-view`.
- **If a library's API has moved** in these majors and differs from the code in this plan: make the smallest change that keeps the behaviour, and record a ruling.
- **Styling.** Tailwind utility classes style only non-MUI elements (wrapper `div`s, layout). MUI components are styled with `sx`.
- **API calls.**
  - Every call goes through `apiFetch` with a relative `/api/...` path.
  - Server-provided image URLs (`thumbnailUrl`, `previewUrl`) are used exactly as received.
- **URL params:**
  - `sort` is `date` or `name`, default `date`;
  - `order` is `asc` or `desc`, defaulting to `desc` for `date` and `asc` for `name`;
  - `image`, `in` and folder ids are positive integers.

  Invalid `sort`/`order` values fall back to the defaults, and invalid ids are dropped.
- **Layout numbers:**

  | Constant | Value |
  |---|---|
  | page size | 100 |
  | minimum tile width | 180 px |
  | tile gap | 4 px |
  | tree width | 280 px |
  | info panel width | 320 px |
  | search debounce | 300 ms |
  | next-page trigger | within 2 rows of the loaded end |

- **`localStorage` key** for the info panel: `pm.viewer.infoOpen` (`'false'` means closed). Every read and write is in `try`/`catch`, and the panel defaults to open.
- **User-visible copy, verbatim:**
  - `This folder is missing on disk. Its photos are hidden until it's back. Rescan or remove it (Admin).`
  - `No photos directly in this folder. Pick a subfolder in the tree.`
  - `Folder not found.` with the link text `Go to the first folder`
  - `Couldn't update favorite.`
  - `No favorites yet. Star a photo with ★ or F in the viewer.`
  - `Type a file name to search.`
  - `No photos match “{q}”.` (curly quotes)
  - `This photo is no longer available.`
  - `Couldn't load folders.`
  - `Couldn't load subfolders.`
  - `Couldn't load this folder.`
  - `Couldn't load photos.`
  - `Couldn't load details.`
- **`folderPath` format:**
  - `"{root Name}/{RelativePath}"`;
  - just `"{root Name}"` for a root's top folder;
  - always built with the existing `FolderDisplayPath.For`.
- **Frontend gates.** Run these from `web/` after every task:
  - `npm run build` (this runs `tsc -b`)
  - `npm run lint` (no errors; warnings are allowed)
  - `npm run format:check`
  - `npm test`

  If `format:check` fails, run `npx prettier --write` on the files you touched. That is formatting only, not a behaviour change.
- **Backend gate:** `dotnet test` from the repo root, with 0 failed.
- **Commit trailer.** Every commit message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

These are the failure modes most likely to bite a user that the per-feature tests would not naturally hit. Each one is pinned by the test named in brackets:

1. **Two quick clicks on the same star.** The final state must match the last click; an intermediate rollback must not flip it back. [Task 3, `two quick toggles end in the last state`]
2. **A search query with URL-special characters** (`&`, `#`, `%`, `+`, spaces) must reach the URL and the API unchanged. [Task 6, `round-trips special characters in the query`]
3. **Browser Back after stepping through photos in the viewer.** It must close the viewer, not walk back through every photo. [Task 7, `Esc closes the viewer and Back does not walk through every photo`]
4. **Changing the sort after more pages have loaded.** The new listing must start without a cursor. A stale cursor from the old sort gets a `400` from the API. [Task 5, `changing the sort starts a fresh listing (no stale cursor) in that order`]
5. **A malformed `?image=` in a shared link** (`abc`, `0`, `-1`) must be ignored: the grid shows and no viewer opens. [Task 7, `ignores a malformed image param`; parsing is covered in Task 2's `urlState` tests]

---

### Task 1: Backend — `folderPath` on image list items

**Files:**
- Modify: `src/PictureManager.Application/Images/ImageQueryModels.cs` (`ImageRow`)
- Modify: `src/PictureManager.Application/Images/ImageDtos.cs` (`ImageListItem`)
- Modify: `src/PictureManager.Infrastructure/Persistence/Queries/ImageProjections.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/ImageQueryRepository.cs` (the two inline `new ImageRow(...)` in `GetVisibleDetailAsync` and `GetDuplicateMembersAsync`)
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/AlbumRepository.cs` (the inline `new ImageRow(...)` in `ListImagesAsync`)
- Modify (test helpers that build `ImageRow`):
  - `tests/PictureManager.Application.Tests/Images/ImageQueryServiceTests.cs` (`Row`)
  - `tests/PictureManager.Application.Tests/Albums/AlbumServiceTests.cs` (`Row`)
  - `tests/PictureManager.Application.Tests/Duplicates/DuplicateServiceTests.cs` (`Member`)
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ImageQueryRepositoryTests.cs`, `tests/PictureManager.Application.Tests/Images/ImageQueryServiceTests.cs`

**Interfaces:**
- Consumes: existing `FolderDisplayPath.For(string rootName, string relativePath)` (in `PictureManager.Application.Common`).
- Produces:
  - `ImageRow(..., string ContentHash, DateTime SortDate, string SortName, string RootName, string RelativePath)`;
  - `ImageListItem(..., string? ThumbnailUrl, string? PreviewUrl, string FolderPath)`, serialized as `folderPath`.

- [ ] **Step 1: Write the failing tests**

Add to `ImageQueryRepositoryTests` (Postgres-backed, same style as the class's other tests):

```csharp
    [Fact]
    public async Task ListAsync_RowsCarryRootNameAndRelativePath()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("nas");
        var top = TestData.Folder(root, "");
        var madeira = TestData.Folder(root, "Holidays/Madeira", top);
        var atTop = TestData.Image(top, "a");
        var nested = TestData.Image(madeira, "b");
        db.Context.Images.AddRange(atTop, nested);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new ImageQueryRepository(context).ListAsync(NoFilter, ImageSort.Name, SortDirection.Asc, null, 50);

        rows.Select(r => (r.Id, r.RootName, r.RelativePath)).Should().Equal(
            (atTop.Id, "nas", ""),
            (nested.Id, "nas", "Holidays/Madeira"));
    }
```

Add to `ImageQueryServiceTests` (add `using System.Linq;` if the file lacks it):

```csharp
    [Fact]
    public async Task ListAsync_ItemsCarryFolderPath_RootNameAloneForTheTopFolder()
    {
        StubRows(new[]
        {
            Row(1) with { RootName = "nas", RelativePath = "" },
            Row(2) with { RootName = "nas", RelativePath = "Holidays/Madeira" }
        });

        var items = (await CreateService().ListAsync(new ImageListRequest())).Value!.Items;

        items.Select(i => i.FolderPath).Should().Equal("nas", "nas/Holidays/Madeira");
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build`
Expected: FAILS. `ImageRow` has no `RootName`/`RelativePath`, and `ImageListItem` has no `FolderPath`.

- [ ] **Step 3: Implement**

In `ImageQueryModels.cs`, replace the `ImageRow` record with:

```csharp
/// <summary>Slim list row projected in SQL (never loads RawMetadata). RootName/RelativePath build FolderPath.</summary>
public sealed record ImageRow(
    int Id,
    int FolderId,
    string FileName,
    string Extension,
    int? Width,
    int? Height,
    DateTime? DateTaken,
    bool IsFavorite,
    string ContentHash,
    DateTime SortDate,
    string SortName,
    string RootName,
    string RelativePath);
```

In `ImageDtos.cs`, replace the `ImageListItem` record with:

```csharp
public sealed record ImageListItem(
    int Id,
    int FolderId,
    string FileName,
    string Extension,
    int? Width,
    int? Height,
    DateTime? DateTaken,
    bool IsFavorite,
    string? ThumbnailUrl,
    string? PreviewUrl,
    string FolderPath)
{
    public static ImageListItem From(ImageRow row) => new(
        row.Id, row.FolderId, row.FileName, row.Extension, row.Width, row.Height, row.DateTaken, row.IsFavorite,
        ImageUrls.Thumbnail(row.Id, row.ContentHash), ImageUrls.Preview(row.Id, row.ContentHash),
        FolderDisplayPath.For(row.RootName, row.RelativePath));
}
```

In `ImageProjections.cs`, replace `ToRow` with:

```csharp
    public static readonly Expression<Func<Image, ImageRow>> ToRow = i => new ImageRow(
        i.Id, i.FolderId, i.FileName, i.Extension, i.Width, i.Height, i.DateTaken, i.IsFavorite,
        i.ContentHash, i.SortDate, i.FileName.ToLower(), i.Folder!.Root!.Name, i.Folder.RelativePath);
```

In `ImageQueryRepository.cs`, in **both** `GetVisibleDetailAsync` and `GetDuplicateMembersAsync`, replace the inline row construction

```csharp
                new ImageRow(i.Id, i.FolderId, i.FileName, i.Extension, i.Width, i.Height, i.DateTaken, i.IsFavorite,
                    i.ContentHash, i.SortDate, i.FileName.ToLower()),
```

with

```csharp
                new ImageRow(i.Id, i.FolderId, i.FileName, i.Extension, i.Width, i.Height, i.DateTaken, i.IsFavorite,
                    i.ContentHash, i.SortDate, i.FileName.ToLower(), i.Folder!.Root!.Name, i.Folder.RelativePath),
```

In `AlbumRepository.ListImagesAsync`, replace

```csharp
                new ImageRow(ai.Image!.Id, ai.Image.FolderId, ai.Image.FileName, ai.Image.Extension, ai.Image.Width,
                    ai.Image.Height, ai.Image.DateTaken, ai.Image.IsFavorite, ai.Image.ContentHash, ai.Image.SortDate,
                    ai.Image.FileName.ToLower()),
```

with

```csharp
                new ImageRow(ai.Image!.Id, ai.Image.FolderId, ai.Image.FileName, ai.Image.Extension, ai.Image.Width,
                    ai.Image.Height, ai.Image.DateTaken, ai.Image.IsFavorite, ai.Image.ContentHash, ai.Image.SortDate,
                    ai.Image.FileName.ToLower(), ai.Image.Folder!.Root!.Name, ai.Image.Folder.RelativePath),
```

Update the three test helpers:
- `ImageQueryServiceTests.Row`:

```csharp
    private static ImageRow Row(int id, string hash = "H", string name = "img") =>
        new(id, 7, name, ".jpg", 10, 20, null, false, hash,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(-id), name.ToLowerInvariant(), "nas", "");
```

- `AlbumServiceTests.Row`:

```csharp
    private static ImageRow Row(int id, string hash = "H") =>
        new(id, 3, $"img{id}", ".jpg", null, null, null, false, hash, DateTime.UtcNow, $"img{id}", "nas", "");
```

- `DuplicateServiceTests.Member`:

```csharp
    private static DuplicateMemberRow Member(int id, string hash, string name, string relativePath) =>
        new(new ImageRow(id, 1, name, ".jpg", null, null, null, false, hash, DateTime.UtcNow, name.ToLowerInvariant(), "nas", relativePath), "nas", relativePath);
```

- [ ] **Step 4: Run the new tests, then the full suite**

Run: `dotnet test --filter "FullyQualifiedName~ListAsync_RowsCarryRootNameAndRelativePath|FullyQualifiedName~ListAsync_ItemsCarryFolderPath"`
Expected: PASS, 2 tests.

Run: `dotnet test`
Expected: 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/PictureManager.Application/Images src/PictureManager.Infrastructure/Persistence tests/PictureManager.Application.Tests tests/PictureManager.Infrastructure.Tests
git commit -m "feat: add folderPath to image list items

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Frontend tooling, API client, types, URL state, image filter, retry policy

**Files:**
- Modify: `web/package.json` (via npm; add the `test` scripts)
- Modify: `web/tsconfig.app.json` (add `noUncheckedIndexedAccess`)
- Modify: `web/vite.config.ts` (test config + `/api` dev proxy)
- Modify: `web/.env.example`
- Create: `web/src/test/setup.ts`, `web/src/test/server.ts`
- Create: `web/src/api/types.ts`, `web/src/api/client.ts`, `web/src/api/imageFilter.ts`
- Create: `web/src/routing/urlState.ts`
- Create: `web/src/app/queryClient.ts`
- Test: `web/src/api/client.test.ts`, `web/src/api/imageFilter.test.ts`, `web/src/routing/urlState.test.ts`, `web/src/app/queryClient.test.ts`

**Interfaces:**
- Produces:
  - **API client** (`src/api/client.ts`):
    - `class ApiError extends Error { status: number; problem: ProblemDetails | null }`
    - `isNotFound(error: unknown): boolean`
    - `apiFetch<T>(path: string, init?: RequestInit): Promise<T>`
  - **Types** (`src/api/types.ts`): `FolderNode`, `BreadcrumbItem`, `FolderDetail`, `ImageListItem`, `AlbumRef`, `ImageDetail`, `Page<T>`, `ProblemDetails`.
  - **URL state** (`src/routing/urlState.ts`):
    - types `Sort`, `Order`, `GridParams`, `SearchParamsState`
    - `defaultOrder(sort)`
    - `parseId(value: string | null): number | null`
    - `parseGridParams(params: URLSearchParams): GridParams`
    - `parseSearchState(params: URLSearchParams): SearchParamsState`
    - `withParams(current: URLSearchParams, changes: Record<string, string | number | null>): URLSearchParams`
  - **Image filter** (`src/api/imageFilter.ts`):
    - `PAGE_SIZE = 100`
    - `type ImageFilter`
    - `toImageQuery(filter: ImageFilter, cursor: string | null): URLSearchParams`
  - **Query client** (`src/app/queryClient.ts`):
    - `shouldRetry(failureCount: number, error: unknown): boolean`
    - `createQueryClient(): QueryClient`
  - **Tests:** `server` (MSW `setupServer()`) from `src/test/server.ts`.

- [ ] **Step 1: Install dependencies and configure tooling**

Run from `web/`:

```bash
npm install react-router@^8.4.0 @tanstack/react-query@^5.103.2 @tanstack/react-virtual@^3.14.13
npm install -D vitest@^5.0.1 jsdom@^30.1.1 @testing-library/react@^16.3.3 @testing-library/jest-dom@^7.0.1 @testing-library/user-event@^14.6.7 msw@^2.15.0
```

In `web/package.json` `scripts`, add after `"preview"`:

```json
    "test": "vitest run",
    "test:watch": "vitest",
```

In `web/tsconfig.app.json` `compilerOptions`, add after `"noFallthroughCasesInSwitch": true`:

```json
    "noUncheckedIndexedAccess": true,
```

(Put a comma after the preceding line, as JSON requires.)

Replace `web/vite.config.ts` with:

```ts
/// <reference types="vitest/config" />
import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  return {
    plugins: [react(), tailwindcss()],
    server: {
      // Same-origin in development: the API returns relative image URLs (/api/images/...).
      proxy: {
        '/api': { target: env.VITE_API_PROXY_TARGET || 'http://localhost:5080' },
      },
    },
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/test/setup.ts'],
    },
  }
})
```

Replace `web/.env.example` with:

```
VITE_API_PROXY_TARGET=http://localhost:5080
```

Create `web/src/test/server.ts`:

```ts
import { setupServer } from 'msw/node'

export const server = setupServer()
```

Create `web/src/test/setup.ts`:

```ts
import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { server } from './server'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  cleanup()
  localStorage.clear()
})
afterAll(() => server.close())

// jsdom gaps the app relies on.
if (!('ResizeObserver' in globalThis)) {
  class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
  globalThis.ResizeObserver = ResizeObserverStub as unknown as typeof ResizeObserver
}

Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener() {},
    removeEventListener() {},
    addListener() {},
    removeListener() {},
    dispatchEvent: () => false,
  }),
})

Element.prototype.scrollIntoView = function scrollIntoView() {}

// jsdom lays nothing out, so give every element a desktop-sized box; the grid virtualizer
// measures its scroll container and would otherwise render no rows.
Object.defineProperties(HTMLElement.prototype, {
  offsetWidth: { configurable: true, get: () => 1200 },
  offsetHeight: { configurable: true, get: () => 800 },
  clientWidth: { configurable: true, get: () => 1200 },
  clientHeight: { configurable: true, get: () => 800 },
})
HTMLElement.prototype.getBoundingClientRect = function getBoundingClientRect() {
  return {
    x: 0,
    y: 0,
    top: 0,
    left: 0,
    right: 1200,
    bottom: 800,
    width: 1200,
    height: 800,
    toJSON: () => ({}),
  } as DOMRect
}
```

- [ ] **Step 2: Write the failing tests**

Create `web/src/routing/urlState.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { parseGridParams, parseId, parseSearchState, withParams } from './urlState'

const params = (search: string) => new URLSearchParams(search)

describe('parseId', () => {
  it.each([
    ['7', 7],
    ['123', 123],
  ])('accepts %s', (value, expected) => expect(parseId(value)).toBe(expected))

  it.each([null, '', '0', '-1', '1.5', 'abc', '07', '99999999999999999999'])('rejects %s', (value) =>
    expect(parseId(value)).toBeNull(),
  )
})

describe('parseGridParams', () => {
  it('defaults to date, newest first, no image', () =>
    expect(parseGridParams(params(''))).toEqual({ sort: 'date', order: 'desc', image: null }))

  it('defaults name sorts to ascending', () =>
    expect(parseGridParams(params('?sort=name'))).toEqual({ sort: 'name', order: 'asc', image: null }))

  it('keeps an explicit order', () =>
    expect(parseGridParams(params('?sort=name&order=desc&image=5'))).toEqual({
      sort: 'name',
      order: 'desc',
      image: 5,
    }))

  it('falls back on invalid values and drops an invalid image id', () =>
    expect(parseGridParams(params('?sort=size&order=up&image=abc'))).toEqual({
      sort: 'date',
      order: 'desc',
      image: null,
    }))
})

describe('parseSearchState', () => {
  it('trims the query and parses the scope', () =>
    expect(parseSearchState(params('?q=%20img%20&in=3'))).toEqual({ q: 'img', in: 3 }))

  it('treats a missing query as empty and drops an invalid scope', () =>
    expect(parseSearchState(params('?in=x'))).toEqual({ q: '', in: null }))
})

describe('withParams', () => {
  it('sets, replaces and removes keys without touching the original', () => {
    const current = params('?sort=name&image=4')
    const next = withParams(current, { image: 9, order: 'desc', sort: null })
    expect(next.toString()).toBe('image=9&order=desc')
    expect(current.toString()).toBe('sort=name&image=4')
  })

  it('removes a key set to an empty string', () =>
    expect(withParams(params('?q=x'), { q: '' }).toString()).toBe(''))
})
```

Create `web/src/api/imageFilter.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { toImageQuery } from './imageFilter'

describe('toImageQuery', () => {
  it('maps a folder filter', () =>
    expect(
      toImageQuery({ kind: 'folder', folderId: 3, sort: 'date', order: 'desc' }, null).toString(),
    ).toBe('folderId=3&sort=date&order=desc&limit=100'))

  it('maps favorites and appends the cursor', () =>
    expect(toImageQuery({ kind: 'favorites', sort: 'name', order: 'asc' }, 'abc').toString()).toBe(
      'favoritesOnly=true&sort=name&order=asc&limit=100&cursor=abc',
    ))

  it('maps a search, scoped and unscoped', () => {
    expect(
      toImageQuery({ kind: 'search', q: 'a&b', sort: 'date', order: 'desc' }, null).get('fileName'),
    ).toBe('a&b')
    const scoped = toImageQuery(
      { kind: 'search', q: 'img', in: 7, sort: 'date', order: 'desc' },
      null,
    )
    expect(scoped.get('folderId')).toBe('7')
  })
})
```

Create `web/src/api/client.test.ts`:

```ts
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '../test/server'
import { ApiError, apiFetch, isNotFound } from './client'

describe('apiFetch', () => {
  it('returns the parsed JSON body', async () => {
    server.use(http.get('/api/thing', () => HttpResponse.json({ id: 1 })))
    await expect(apiFetch<{ id: number }>('/api/thing')).resolves.toEqual({ id: 1 })
  })

  it('returns undefined for 204', async () => {
    server.use(http.put('/api/thing', () => new HttpResponse(null, { status: 204 })))
    await expect(apiFetch<void>('/api/thing', { method: 'PUT' })).resolves.toBeUndefined()
  })

  it('throws ApiError carrying the status and ProblemDetails', async () => {
    server.use(
      http.get('/api/thing', () =>
        HttpResponse.json({ title: 'Not Found', status: 404 }, { status: 404 }),
      ),
    )
    const error = await apiFetch('/api/thing').catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(404)
    expect((error as ApiError).problem?.title).toBe('Not Found')
    expect(isNotFound(error)).toBe(true)
  })

  it('throws ApiError with no problem when the error body is not JSON', async () => {
    server.use(http.get('/api/thing', () => new HttpResponse('oops', { status: 500 })))
    const error = (await apiFetch('/api/thing').catch((e: unknown) => e)) as ApiError
    expect(error.status).toBe(500)
    expect(error.problem).toBeNull()
    expect(error.message).toBe('Request failed with status 500')
    expect(isNotFound(error)).toBe(false)
  })
})
```

Create `web/src/app/queryClient.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { ApiError } from '../api/client'
import { shouldRetry } from './queryClient'

describe('shouldRetry', () => {
  it('never retries a 4xx', () => {
    expect(shouldRetry(0, new ApiError(404, null))).toBe(false)
    expect(shouldRetry(0, new ApiError(400, null))).toBe(false)
  })

  it('retries a 5xx or a network error once', () => {
    expect(shouldRetry(0, new ApiError(500, null))).toBe(true)
    expect(shouldRetry(1, new ApiError(500, null))).toBe(false)
    expect(shouldRetry(0, new TypeError('Failed to fetch'))).toBe(true)
    expect(shouldRetry(1, new TypeError('Failed to fetch'))).toBe(false)
  })
})
```

- [ ] **Step 3: Run to verify they fail**

Run (from `web/`): `npm test`
Expected: FAIL. The suites can't resolve `./urlState`, `./imageFilter`, `./client` or `./queryClient`.

- [ ] **Step 4: Implement**

Create `web/src/api/types.ts`:

```ts
export type FolderNode = {
  id: number
  name: string
  hasChildren: boolean
  imageCount: number
  isMissing: boolean
}

export type BreadcrumbItem = { id: number; name: string }

export type FolderDetail = {
  id: number
  name: string
  rootId: number
  rootName: string
  relativePath: string
  imageCount: number
  isMissing: boolean
  breadcrumb: BreadcrumbItem[]
}

export type ImageListItem = {
  id: number
  folderId: number
  fileName: string
  extension: string
  width: number | null
  height: number | null
  /** Local-naive camera time without an offset, e.g. "2025-08-14T18:32:05". */
  dateTaken: string | null
  isFavorite: boolean
  thumbnailUrl: string | null
  previewUrl: string | null
  /** "{root name}/{relative path}", or just the root name for a root's top folder. */
  folderPath: string
}

export type AlbumRef = { id: number; name: string }

export type ImageDetail = ImageListItem & {
  fileSize: number
  fileModified: string
  orientation: number | null
  cameraMake: string | null
  cameraModel: string | null
  lensModel: string | null
  latitude: number | null
  longitude: number | null
  rawMetadata: unknown
  albums: AlbumRef[]
}

export type Page<T> = { items: T[]; nextCursor: string | null }

export type ProblemDetails = {
  title?: string
  status?: number
  detail?: string
  errors?: Record<string, string[]>
}
```

Create `web/src/api/client.ts`:

```ts
import type { ProblemDetails } from './types'

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null

  constructor(status: number, problem: ProblemDetails | null) {
    super(problem?.title ?? `Request failed with status ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

export function isNotFound(error: unknown): boolean {
  return error instanceof ApiError && error.status === 404
}

/**
 * Fetches a same-origin API path ("/api/..."). Resolving against the page origin keeps relative
 * paths working in the browser and in tests (Node's fetch rejects bare relative URLs).
 */
export async function apiFetch<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(new URL(path, window.location.origin), {
    ...init,
    headers: { Accept: 'application/json', ...init?.headers },
  })

  if (!response.ok) {
    throw new ApiError(response.status, await readProblem(response))
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

async function readProblem(response: Response): Promise<ProblemDetails | null> {
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return null
  }
}
```

Create `web/src/routing/urlState.ts`:

```ts
export type Sort = 'date' | 'name'
export type Order = 'asc' | 'desc'

export type GridParams = { sort: Sort; order: Order; image: number | null }
export type SearchParamsState = { q: string; in: number | null }

/** The API's default direction for each sort. */
export function defaultOrder(sort: Sort): Order {
  return sort === 'date' ? 'desc' : 'asc'
}

/** A positive integer id, or null for anything else ("", "0", "-3", "1.5", "07", "abc", overflow). */
export function parseId(value: string | null): number | null {
  if (value === null || !/^[1-9][0-9]*$/.test(value)) return null
  const id = Number(value)
  return Number.isSafeInteger(id) ? id : null
}

/** Invalid sort/order values fall back to the defaults; an invalid image id is dropped. */
export function parseGridParams(params: URLSearchParams): GridParams {
  const sort: Sort = params.get('sort') === 'name' ? 'name' : 'date'
  const rawOrder = params.get('order')
  const order: Order = rawOrder === 'asc' || rawOrder === 'desc' ? rawOrder : defaultOrder(sort)
  return { sort, order, image: parseId(params.get('image')) }
}

export function parseSearchState(params: URLSearchParams): SearchParamsState {
  return { q: (params.get('q') ?? '').trim(), in: parseId(params.get('in')) }
}

/** A copy of `current` with each change applied; null or '' removes the key. */
export function withParams(
  current: URLSearchParams,
  changes: Record<string, string | number | null>,
): URLSearchParams {
  const next = new URLSearchParams(current)
  for (const [key, value] of Object.entries(changes)) {
    if (value === null || value === '') next.delete(key)
    else next.set(key, String(value))
  }
  return next
}
```

Create `web/src/api/imageFilter.ts`:

```ts
import type { Order, Sort } from '../routing/urlState'

export const PAGE_SIZE = 100

/** What a grid shows. It is also the query key, so each view caches separately. */
export type ImageFilter = (
  | { kind: 'folder'; folderId: number }
  | { kind: 'favorites' }
  | { kind: 'search'; q: string; in?: number }
) & { sort: Sort; order: Order }

/** Query string for GET /api/images. */
export function toImageQuery(filter: ImageFilter, cursor: string | null): URLSearchParams {
  const params = new URLSearchParams()
  switch (filter.kind) {
    case 'folder':
      params.set('folderId', String(filter.folderId))
      break
    case 'favorites':
      params.set('favoritesOnly', 'true')
      break
    case 'search':
      params.set('fileName', filter.q)
      if (filter.in !== undefined) params.set('folderId', String(filter.in))
      break
  }
  params.set('sort', filter.sort)
  params.set('order', filter.order)
  params.set('limit', String(PAGE_SIZE))
  if (cursor !== null) params.set('cursor', cursor)
  return params
}
```

Create `web/src/app/queryClient.ts`:

```ts
import { QueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/client'

/** No retry for a 4xx (the answer won't change); one retry for network errors and 5xx. */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  if (error instanceof ApiError && error.status < 500) return false
  return failureCount < 1
}

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: shouldRetry,
        // Long enough that Back to a view is instant; favorites lists are marked stale explicitly.
        staleTime: 30_000,
        // A refetch on focus could drop a dimmed, just-unstarred photo from an open Favorites view.
        refetchOnWindowFocus: false,
      },
    },
  })
}
```

- [ ] **Step 5: Run the tests**

Run (from `web/`): `npm test`
Expected: PASS. All four suites are green.

- [ ] **Step 6: Run the gates**

Run (from `web/`): `npm run build`, `npm run lint`, `npm run format:check`
Expected: all succeed. `App.tsx` still imports `src/config/env.ts` at this point, which is fine; Task 4 replaces it.

- [ ] **Step 7: Commit**

```bash
git add web/package.json web/package-lock.json web/tsconfig.app.json web/vite.config.ts web/.env.example web/src/test web/src/api web/src/routing web/src/app
git commit -m "feat(web): add test tooling, API client, types, URL state and image filter

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Data hooks, optimistic favorite, notifications, test fixtures

**Files:**
- Create: `web/src/api/queries.ts`, `web/src/api/favorites.ts`
- Create: `web/src/app/notify.tsx`
- Create: `web/src/test/fixtures.ts`, `web/src/test/handlers.ts`, `web/src/test/render.tsx`
- Modify: `web/src/test/server.ts`
- Test: `web/src/api/queries.test.tsx`, `web/src/api/favorites.test.tsx`

**Interfaces:**
- Consumes: `apiFetch`, `toImageQuery`, `ImageFilter` and the types from Task 2.
- Produces:
  - **Query keys** (`src/api/queries.ts`): `queryKeys` with
    - `rootFolders()`
    - `folderChildren(id)`
    - `folder(id)`
    - `imageLists()`
    - `images(filter)`
    - `image(id)`
  - **Query hooks** (`src/api/queries.ts`):
    - `useRootFolders()`
    - `useFolderChildren(id: number, enabled: boolean)`
    - `useFolder(id: number | null)`
    - `useImages(filter: ImageFilter, options?: { refetchOnMount?: boolean })`
    - `useImage(id: number | null)`
  - **Favorites** (`src/api/favorites.ts`):
    - `patchFavorite(queryClient, id, isFavorite)`
    - `useSetFavorite()`, which mutates `{ id: number; isFavorite: boolean }`
  - **Notifications** (`src/app/notify.tsx`): `NotifyProvider`, `useNotify(): (message: string) => void`.
  - **Test fixtures** (`src/test/fixtures.ts`):
    - folders: `rootFolders`, `devRoot`, `holidays`, `madeira`, `old`, `childrenById`, `folderDetails`
    - images: `image(id, folderId, overrides?)`, `holidaysImages`, `madeiraImages`, `imageDetail(item)`
  - **Test handlers** (`src/test/handlers.ts`): `handlers`, and `pagedImages(pages: ImageListItem[][])`, which serves page n for cursor `p{n}`.
  - **Test render helpers** (`src/test/render.tsx`):
    - `createTestQueryClient()`
    - `createWrapper(queryClient)`
    - `renderRoutes(routes, path)`, which returns `{ user, router, queryClient, ...RTL }`

- [ ] **Step 1: Write the test support files**

Create `web/src/test/fixtures.ts`:

```ts
import type { FolderDetail, FolderNode, ImageDetail, ImageListItem } from '../api/types'

export const devRoot: FolderNode = { id: 1, name: 'dev', hasChildren: true, imageCount: 0, isMissing: false }
export const holidays: FolderNode = { id: 2, name: 'Holidays', hasChildren: true, imageCount: 2, isMissing: false }
export const madeira: FolderNode = { id: 3, name: 'Madeira', hasChildren: false, imageCount: 3, isMissing: false }
export const old: FolderNode = { id: 4, name: 'Old', hasChildren: false, imageCount: 1, isMissing: true }

export const rootFolders: FolderNode[] = [devRoot]

export const childrenById: Record<number, FolderNode[]> = {
  1: [holidays],
  2: [madeira, old],
  3: [],
  4: [],
}

const crumb = (node: FolderNode) => ({ id: node.id, name: node.name })

export const folderDetails: Record<number, FolderDetail> = {
  1: { id: 1, name: 'dev', rootId: 1, rootName: 'dev', relativePath: '', imageCount: 0, isMissing: false, breadcrumb: [crumb(devRoot)] },
  2: { id: 2, name: 'Holidays', rootId: 1, rootName: 'dev', relativePath: 'Holidays', imageCount: 2, isMissing: false, breadcrumb: [crumb(devRoot), crumb(holidays)] },
  3: { id: 3, name: 'Madeira', rootId: 1, rootName: 'dev', relativePath: 'Holidays/Madeira', imageCount: 3, isMissing: false, breadcrumb: [crumb(devRoot), crumb(holidays), crumb(madeira)] },
  4: { id: 4, name: 'Old', rootId: 1, rootName: 'dev', relativePath: 'Holidays/Old', imageCount: 1, isMissing: true, breadcrumb: [crumb(devRoot), crumb(holidays), crumb(old)] },
}

const folderPaths: Record<number, string> = {
  1: 'dev',
  2: 'dev/Holidays',
  3: 'dev/Holidays/Madeira',
  4: 'dev/Holidays/Old',
}

export function image(id: number, folderId: number, overrides: Partial<ImageListItem> = {}): ImageListItem {
  return {
    id,
    folderId,
    fileName: `IMG_${String(id).padStart(4, '0')}`,
    extension: '.jpg',
    width: 1200,
    height: 800,
    dateTaken: '2025-08-14T18:32:05',
    isFavorite: false,
    thumbnailUrl: `/api/images/${id}/thumbnail?v=H${id}`,
    previewUrl: `/api/images/${id}/preview?v=H${id}`,
    folderPath: folderPaths[folderId] ?? 'dev',
    ...overrides,
  }
}

export const holidaysImages: ImageListItem[] = [
  image(10, 2, { fileName: 'IMG_0001 copy' }),
  image(11, 2, { fileName: 'screenshot', extension: '.png' }),
]

export const madeiraImages: ImageListItem[] = [
  image(20, 3, { fileName: 'IMG_0001' }),
  image(21, 3, { fileName: 'IMG_0002', isFavorite: true }),
  image(22, 3, { fileName: 'IMG_0003' }),
]

export function imageDetail(item: ImageListItem): ImageDetail {
  return {
    ...item,
    fileSize: 2_400_000,
    fileModified: '2025-08-15T10:00:00Z',
    orientation: 1,
    cameraMake: 'Canon',
    cameraModel: 'EOS R6',
    lensModel: 'RF 24-105mm',
    latitude: 32.6669,
    longitude: -16.9241,
    rawMetadata: { Exif: { ISO: 100 } },
    albums: [{ id: 5, name: 'Best of 2025' }],
  }
}
```

Create `web/src/test/handlers.ts`:

```ts
import { http, HttpResponse } from 'msw'
import type { ImageListItem } from '../api/types'
import {
  childrenById,
  folderDetails,
  holidaysImages,
  imageDetail,
  madeiraImages,
  rootFolders,
} from './fixtures'

const allImages = (): ImageListItem[] => [...holidaysImages, ...madeiraImages]
const notFound = () => HttpResponse.json({ title: 'Not Found', status: 404 }, { status: 404 })

/** A stateless fake of the phase 5 API over the fixtures. Order matters: /roots before /:id. */
export const handlers = [
  http.get('/api/folders/roots', () => HttpResponse.json(rootFolders)),
  http.get('/api/folders/:id/children', ({ params }) =>
    HttpResponse.json(childrenById[Number(params.id)] ?? []),
  ),
  http.get('/api/folders/:id', ({ params }) => {
    const detail = folderDetails[Number(params.id)]
    return detail ? HttpResponse.json(detail) : notFound()
  }),
  http.get('/api/images', ({ request }) => {
    const query = new URL(request.url).searchParams
    let items = allImages()
    const folderId = query.get('folderId')
    if (folderId !== null) items = items.filter((i) => i.folderId === Number(folderId))
    if (query.get('favoritesOnly') === 'true') items = items.filter((i) => i.isFavorite)
    const fileName = query.get('fileName')?.toLowerCase()
    if (fileName) items = items.filter((i) => i.fileName.toLowerCase().includes(fileName))
    return HttpResponse.json({ items, nextCursor: null })
  }),
  http.get('/api/images/:id', ({ params }) => {
    const item = allImages().find((i) => i.id === Number(params.id))
    return item ? HttpResponse.json(imageDetail(item)) : notFound()
  }),
  http.put('/api/images/:id/favorite', () => new HttpResponse(null, { status: 204 })),
  http.delete('/api/images/:id/favorite', () => new HttpResponse(null, { status: 204 })),
]

/** Serves `pages` in order for GET /api/images, whatever the filter; cursor "p{n}" asks for page n. */
export function pagedImages(pages: ImageListItem[][]) {
  return http.get('/api/images', ({ request }) => {
    const cursor = new URL(request.url).searchParams.get('cursor')
    const index = cursor === null ? 0 : Number(cursor.slice(1))
    return HttpResponse.json({
      items: pages[index] ?? [],
      nextCursor: index + 1 < pages.length ? `p${index + 1}` : null,
    })
  })
}
```

Replace `web/src/test/server.ts` with:

```ts
import { setupServer } from 'msw/node'
import { handlers } from './handlers'

export const server = setupServer(...handlers)
```

Create `web/src/test/render.tsx`:

```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { createMemoryRouter, RouterProvider, type RouteObject } from 'react-router'
import { NotifyProvider } from '../app/notify'

export function createTestQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: 30_000, refetchOnWindowFocus: false },
      mutations: { retry: false },
    },
  })
}

export function createWrapper(queryClient: QueryClient) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <NotifyProvider>{children}</NotifyProvider>
      </QueryClientProvider>
    )
  }
}

/** Renders `routes` in a memory router at `path`, with fresh providers. */
export function renderRoutes(routes: RouteObject[], path: string) {
  const queryClient = createTestQueryClient()
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  const Wrapper = createWrapper(queryClient)
  const user = userEvent.setup()
  const view = render(
    <Wrapper>
      <RouterProvider router={router} />
    </Wrapper>,
  )
  return { ...view, user, router, queryClient }
}
```

- [ ] **Step 2: Write the failing tests**

Create `web/src/api/queries.test.tsx`:

```tsx
import { act, renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { madeiraImages } from '../test/fixtures'
import { createTestQueryClient, createWrapper } from '../test/render'
import { server } from '../test/server'
import type { ImageFilter } from './imageFilter'
import { useImages } from './queries'

const folderFilter: ImageFilter = { kind: 'folder', folderId: 3, sort: 'date', order: 'desc' }

describe('useImages', () => {
  it('requests the filter as query params and follows nextCursor', async () => {
    const seen: string[] = []
    server.use(
      http.get('/api/images', ({ request }) => {
        const url = new URL(request.url)
        seen.push(url.search)
        return url.searchParams.get('cursor') === null
          ? HttpResponse.json({ items: [madeiraImages[0]], nextCursor: 'p1' })
          : HttpResponse.json({ items: [madeiraImages[1]], nextCursor: null })
      }),
    )
    const wrapper = createWrapper(createTestQueryClient())
    const { result } = renderHook(() => useImages(folderFilter), { wrapper })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(result.current.hasNextPage).toBe(true)
    await act(() => result.current.fetchNextPage())

    expect(result.current.data?.pages.flatMap((p) => p.items).map((i) => i.id)).toEqual([20, 21])
    expect(result.current.hasNextPage).toBe(false)
    expect(seen).toEqual([
      '?folderId=3&sort=date&order=desc&limit=100',
      '?folderId=3&sort=date&order=desc&limit=100&cursor=p1',
    ])
  })
})
```

Create `web/src/api/favorites.test.tsx`:

```tsx
import type { InfiniteData, QueryClient } from '@tanstack/react-query'
import { act, renderHook, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { imageDetail, madeiraImages } from '../test/fixtures'
import { createTestQueryClient, createWrapper } from '../test/render'
import { server } from '../test/server'
import { useSetFavorite } from './favorites'
import type { ImageFilter } from './imageFilter'
import { queryKeys } from './queries'
import type { ImageDetail, ImageListItem, Page } from './types'

const folderFilter: ImageFilter = { kind: 'folder', folderId: 3, sort: 'date', order: 'desc' }
const favoritesFilter: ImageFilter = { kind: 'favorites', sort: 'date', order: 'desc' }

function seed() {
  const queryClient = createTestQueryClient()
  const pageOf = (items: ImageListItem[]) => ({ pages: [{ items, nextCursor: null }], pageParams: [null] })
  queryClient.setQueryData(queryKeys.images(folderFilter), pageOf(madeiraImages))
  queryClient.setQueryData(
    queryKeys.images(favoritesFilter),
    pageOf(madeiraImages.filter((i) => i.isFavorite)),
  )
  queryClient.setQueryData(queryKeys.image(20), imageDetail(madeiraImages[0]!))
  return { queryClient, wrapper: createWrapper(queryClient) }
}

function listItem(queryClient: QueryClient, filter: ImageFilter, id: number) {
  const data = queryClient.getQueryData<InfiniteData<Page<ImageListItem>>>(queryKeys.images(filter))
  return data?.pages.flatMap((p) => p.items).find((i) => i.id === id)
}

const detail = (queryClient: QueryClient, id: number) =>
  queryClient.getQueryData<ImageDetail>(queryKeys.image(id))

describe('useSetFavorite', () => {
  it('flips the star in every cached copy before the server answers', async () => {
    let release = () => {}
    const gate = new Promise<void>((resolve) => (release = resolve))
    server.use(
      http.put('/api/images/:id/favorite', async () => {
        await gate
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const { queryClient, wrapper } = seed()
    const { result } = renderHook(() => useSetFavorite(), { wrapper })

    act(() => result.current.mutate({ id: 20, isFavorite: true }))

    await waitFor(() => expect(listItem(queryClient, folderFilter, 20)?.isFavorite).toBe(true))
    expect(detail(queryClient, 20)?.isFavorite).toBe(true)
    expect(result.current.isPending).toBe(true)

    release()
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(listItem(queryClient, folderFilter, 20)?.isFavorite).toBe(true)
  })

  it('rolls back and notifies when the server refuses', async () => {
    server.use(
      http.put('/api/images/:id/favorite', () => HttpResponse.json({ title: 'boom' }, { status: 500 })),
    )
    const { queryClient, wrapper } = seed()
    const { result } = renderHook(() => useSetFavorite(), { wrapper })

    act(() => result.current.mutate({ id: 20, isFavorite: true }))

    await waitFor(() => expect(result.current.isError).toBe(true))
    expect(listItem(queryClient, folderFilter, 20)?.isFavorite).toBe(false)
    expect(detail(queryClient, 20)?.isFavorite).toBe(false)
    expect(await screen.findByText("Couldn't update favorite.")).toBeInTheDocument()
  })

  it('marks favorites lists stale without dropping the unstarred photo', async () => {
    const { queryClient, wrapper } = seed()
    const { result } = renderHook(() => useSetFavorite(), { wrapper })

    act(() => result.current.mutate({ id: 21, isFavorite: false }))

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(queryClient.getQueryState(queryKeys.images(favoritesFilter))?.isInvalidated).toBe(true)
    expect(listItem(queryClient, favoritesFilter, 21)?.isFavorite).toBe(false)
    expect(queryClient.getQueryState(queryKeys.images(folderFilter))?.isInvalidated).toBe(false)
  })

  it('two quick toggles end in the last state', async () => {
    const { queryClient, wrapper } = seed()
    const { result } = renderHook(() => useSetFavorite(), { wrapper })

    act(() => {
      result.current.mutate({ id: 20, isFavorite: true })
      result.current.mutate({ id: 20, isFavorite: false })
    })

    await waitFor(() => expect(queryClient.isMutating()).toBe(0))
    expect(listItem(queryClient, folderFilter, 20)?.isFavorite).toBe(false)
    expect(detail(queryClient, 20)?.isFavorite).toBe(false)
  })
})
```

- [ ] **Step 3: Run to verify they fail**

Run (from `web/`): `npm test`
Expected: FAIL. `./queries`, `./favorites` and `../app/notify` can't be resolved.

- [ ] **Step 4: Implement**

Create `web/src/app/notify.tsx`:

```tsx
import { Snackbar } from '@mui/material'
import { createContext, useCallback, useContext, useState, type ReactNode } from 'react'

type Notify = (message: string) => void

const NotifyContext = createContext<Notify | null>(null)

/** One app-wide snackbar for short, transient messages. */
export function NotifyProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState<string | null>(null)
  const notify = useCallback<Notify>((next) => setMessage(next), [])

  return (
    <NotifyContext.Provider value={notify}>
      {children}
      <Snackbar
        open={message !== null}
        message={message ?? ''}
        autoHideDuration={4000}
        onClose={() => setMessage(null)}
      />
    </NotifyContext.Provider>
  )
}

export function useNotify(): Notify {
  const notify = useContext(NotifyContext)
  if (notify === null) throw new Error('useNotify must be used inside NotifyProvider')
  return notify
}
```

Create `web/src/api/queries.ts`:

```ts
import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { apiFetch } from './client'
import { toImageQuery, type ImageFilter } from './imageFilter'
import type { FolderDetail, FolderNode, ImageDetail, ImageListItem, Page } from './types'

export const queryKeys = {
  rootFolders: () => ['folders', 'roots'] as const,
  folderChildren: (id: number) => ['folders', id, 'children'] as const,
  folder: (id: number) => ['folders', id, 'detail'] as const,
  imageLists: () => ['images', 'list'] as const,
  images: (filter: ImageFilter) => ['images', 'list', filter] as const,
  image: (id: number) => ['images', 'detail', id] as const,
}

export function useRootFolders() {
  return useQuery({
    queryKey: queryKeys.rootFolders(),
    queryFn: ({ signal }) => apiFetch<FolderNode[]>('/api/folders/roots', { signal }),
  })
}

export function useFolderChildren(id: number, enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.folderChildren(id),
    queryFn: ({ signal }) => apiFetch<FolderNode[]>(`/api/folders/${id}/children`, { signal }),
    enabled,
  })
}

export function useFolder(id: number | null) {
  return useQuery({
    queryKey: queryKeys.folder(id ?? 0),
    queryFn: ({ signal }) => apiFetch<FolderDetail>(`/api/folders/${id}`, { signal }),
    enabled: id !== null,
  })
}

type UseImagesOptions = { refetchOnMount?: boolean }

/** The one hook behind every grid (and the viewer, which walks the same cached pages). */
export function useImages(filter: ImageFilter, options: UseImagesOptions = {}) {
  return useInfiniteQuery({
    queryKey: queryKeys.images(filter),
    queryFn: ({ pageParam, signal }) =>
      apiFetch<Page<ImageListItem>>(`/api/images?${toImageQuery(filter, pageParam)}`, { signal }),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage: Page<ImageListItem>) => lastPage.nextCursor,
    refetchOnMount: options.refetchOnMount ?? true,
  })
}

export function useImage(id: number | null) {
  return useQuery({
    queryKey: queryKeys.image(id ?? 0),
    queryFn: ({ signal }) => apiFetch<ImageDetail>(`/api/images/${id}`, { signal }),
    enabled: id !== null,
  })
}
```

Create `web/src/api/favorites.ts`:

```ts
import {
  useMutation,
  useQueryClient,
  type InfiniteData,
  type QueryClient,
} from '@tanstack/react-query'
import { useNotify } from '../app/notify'
import { apiFetch } from './client'
import type { ImageFilter } from './imageFilter'
import { queryKeys } from './queries'
import type { ImageDetail, ImageListItem, Page } from './types'

type ListData = InfiniteData<Page<ImageListItem>, string | null>
type FavoriteChange = { id: number; isFavorite: boolean }

/** Sets isFavorite on the image in every cached list page and in its cached detail. */
export function patchFavorite(queryClient: QueryClient, id: number, isFavorite: boolean): void {
  queryClient.setQueriesData<ListData>({ queryKey: queryKeys.imageLists() }, (data) =>
    data === undefined
      ? data
      : {
          ...data,
          pages: data.pages.map((page) => ({
            ...page,
            items: page.items.map((item) => (item.id === id ? { ...item, isFavorite } : item)),
          })),
        },
  )
  queryClient.setQueryData<ImageDetail>(queryKeys.image(id), (detail) =>
    detail === undefined ? detail : { ...detail, isFavorite },
  )
}

function isFavoritesList(queryKey: readonly unknown[]): boolean {
  const filter = queryKey[2] as ImageFilter | undefined
  return filter?.kind === 'favorites'
}

export function useSetFavorite() {
  const queryClient = useQueryClient()
  const notify = useNotify()

  return useMutation({
    mutationFn: ({ id, isFavorite }: FavoriteChange) =>
      apiFetch<void>(`/api/images/${id}/favorite`, { method: isFavorite ? 'PUT' : 'DELETE' }),
    onMutate: ({ id, isFavorite }: FavoriteChange) => {
      const snapshot = queryClient.getQueriesData({ queryKey: ['images'] })
      patchFavorite(queryClient, id, isFavorite)
      return { snapshot }
    },
    onError: (_error, _change, context) => {
      context?.snapshot.forEach(([key, data]) => queryClient.setQueryData(key, data))
      notify("Couldn't update favorite.")
    },
    onSuccess: () =>
      // Stale, not refetched: an open Favorites view keeps the dimmed photo until it's left.
      queryClient.invalidateQueries({
        queryKey: queryKeys.imageLists(),
        predicate: (query) => isFavoritesList(query.queryKey),
        refetchType: 'none',
      }),
  })
}
```

- [ ] **Step 5: Run the tests**

Run (from `web/`): `npm test`
Expected: PASS, including the 1 `useImages` test and the 4 `useSetFavorite` tests.

- [ ] **Step 6: Run the gates**

Run (from `web/`): `npm run build`, `npm run lint`, `npm run format:check`
Expected: all succeed.

- [ ] **Step 7: Commit**

```bash
git add web/src/api web/src/app/notify.tsx web/src/test
git commit -m "feat(web): add data hooks, optimistic favorite toggle and test fixtures

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: App shell, routes, root redirect, folder tree

**Files:**
- Create: `web/src/app/AppShell.tsx`, `web/src/app/routes.tsx`
- Create: `web/src/views/RootRedirect.tsx`
- Create: `web/src/shared/QueryErrorAlert.tsx`
- Create: `web/src/tree/FolderTree.tsx`, `web/src/tree/FolderTreeNode.tsx`
- Modify: `web/src/App.tsx` (rewrite)
- Modify: `web/src/vite-env.d.ts`
- Delete: `web/src/config/env.ts`
- Modify: `web/src/test/render.tsx` (add `renderApp`)
- Test: `web/src/tree/FolderTree.test.tsx`

**Interfaces:**
- Consumes: `useRootFolders`, `useFolderChildren`, `useFolder`, `parseId`, and the Task 3 test helpers.
- Produces:
  - `appRoutes: RouteObject[]`, the single layout route `/` rendering `AppShell` with these children:
    - index → `RootRedirect`
    - `folders/:folderId` (no element yet)
    - `*` → redirect to `/`
  - `QueryErrorAlert({ message, onRetry })`
  - `renderApp(path)` = `renderRoutes(appRoutes, path)`
- **Tree semantics:** each node is `role="treeitem"`, with:
  - `aria-label` = the name, or `"{name} (missing)"` for a missing folder;
  - `aria-selected` and `aria-expanded`;
  - an expand button labelled `Expand {name}` / `Collapse {name}`.

  The selected folder's whole breadcrumb (its ancestors and itself) starts expanded; an explicit user toggle wins.

- [ ] **Step 1: Write the failing test**

Create `web/src/tree/FolderTree.test.tsx`:

```tsx
import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('FolderTree', () => {
  it('redirects / to the first root', async () => {
    const { router } = renderApp('/')
    await waitFor(() => expect(router.state.location.pathname).toBe('/folders/1'))
  })

  it('expands the selected folder and its ancestors on a deep link, and marks the selection', async () => {
    renderApp('/folders/3')
    const madeira = await screen.findByRole('treeitem', { name: 'Madeira' })
    expect(madeira).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('treeitem', { name: 'Holidays' })).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('treeitem', { name: 'Old (missing)' })).toBeInTheDocument()
  })

  it('loads children when a node is expanded and hides them when collapsed', async () => {
    const { user } = renderApp('/folders/1')
    await user.click(await screen.findByRole('button', { name: 'Expand Holidays' }))
    expect(await screen.findByRole('treeitem', { name: 'Madeira' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Collapse Holidays' }))
    expect(screen.queryByRole('treeitem', { name: 'Madeira' })).not.toBeInTheDocument()
  })

  it('navigates when a folder is clicked', async () => {
    const { user, router } = renderApp('/folders/2')
    await user.click(await screen.findByRole('treeitem', { name: 'Madeira' }))
    expect(router.state.location.pathname).toBe('/folders/3')
  })

  it('offers Retry when the roots fail to load', async () => {
    server.use(
      http.get('/api/folders/roots', () => HttpResponse.json({ title: 'boom' }, { status: 500 })),
    )
    renderApp('/folders/1')
    expect(await screen.findByText("Couldn't load folders.")).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })
})
```

Add to `web/src/test/render.tsx` (at the top, `import { appRoutes } from '../app/routes'`; at the bottom):

```tsx
/** The whole app (shell + routes) at `path`. */
export function renderApp(path: string) {
  return renderRoutes(appRoutes, path)
}
```

- [ ] **Step 2: Run to verify it fails**

Run (from `web/`): `npm test`
Expected: FAIL. `../app/routes` can't be resolved.

- [ ] **Step 3: Implement**

Create `web/src/shared/QueryErrorAlert.tsx`:

```tsx
import { Alert, Button } from '@mui/material'

export function QueryErrorAlert({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <Alert
      severity="error"
      sx={{ m: 2 }}
      action={
        <Button color="inherit" size="small" onClick={onRetry}>
          Retry
        </Button>
      }
    >
      {message}
    </Alert>
  )
}
```

Create `web/src/tree/FolderTreeNode.tsx`:

```tsx
import ChevronRightIcon from '@mui/icons-material/ChevronRight'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import WarningAmberIcon from '@mui/icons-material/WarningAmber'
import { CircularProgress, IconButton, List, ListItemButton, ListItemText } from '@mui/material'
import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router'
import { useFolderChildren } from '../api/queries'
import type { FolderNode } from '../api/types'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'

type Props = {
  node: FolderNode
  depth: number
  selectedId: number | null
  isExpanded: (id: number) => boolean
  onToggle: (id: number) => void
}

export function FolderTreeNode({ node, depth, selectedId, isExpanded, onToggle }: Props) {
  const navigate = useNavigate()
  const expanded = node.hasChildren && isExpanded(node.id)
  const children = useFolderChildren(node.id, expanded)
  const selected = node.id === selectedId
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (selected) ref.current?.scrollIntoView({ block: 'nearest' })
  }, [selected])

  return (
    <>
      <ListItemButton
        ref={ref}
        role="treeitem"
        aria-label={node.isMissing ? `${node.name} (missing)` : node.name}
        aria-level={depth + 1}
        aria-selected={selected}
        aria-expanded={node.hasChildren ? expanded : undefined}
        selected={selected}
        onClick={() => navigate(`/folders/${node.id}`)}
        sx={{ pl: 1 + depth * 2, py: 0.25 }}
      >
        <IconButton
          size="small"
          aria-label={expanded ? `Collapse ${node.name}` : `Expand ${node.name}`}
          tabIndex={node.hasChildren ? 0 : -1}
          onClick={(event) => {
            event.stopPropagation()
            onToggle(node.id)
          }}
          sx={{ mr: 0.5, visibility: node.hasChildren ? 'visible' : 'hidden' }}
        >
          {expanded ? <ExpandMoreIcon fontSize="small" /> : <ChevronRightIcon fontSize="small" />}
        </IconButton>
        {node.isMissing && (
          <WarningAmberIcon fontSize="small" color="warning" sx={{ mr: 0.5 }} />
        )}
        <ListItemText
          primary={node.name}
          slotProps={{
            primary: { noWrap: true, color: node.isMissing ? 'text.secondary' : undefined },
          }}
        />
        {expanded && children.isFetching && <CircularProgress size={14} />}
      </ListItemButton>
      {expanded && children.isError && (
        <QueryErrorAlert
          message="Couldn't load subfolders."
          onRetry={() => void children.refetch()}
        />
      )}
      {expanded && children.data && children.data.length > 0 && (
        <List dense disablePadding role="group">
          {children.data.map((child) => (
            <FolderTreeNode
              key={child.id}
              node={child}
              depth={depth + 1}
              selectedId={selectedId}
              isExpanded={isExpanded}
              onToggle={onToggle}
            />
          ))}
        </List>
      )}
    </>
  )
}
```

Create `web/src/tree/FolderTree.tsx`:

```tsx
import { Box, CircularProgress, List } from '@mui/material'
import { useState } from 'react'
import { useMatch } from 'react-router'
import { useFolder, useRootFolders } from '../api/queries'
import { parseId } from '../routing/urlState'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { FolderTreeNode } from './FolderTreeNode'

export function FolderTree() {
  const match = useMatch('/folders/:folderId')
  const selectedId = parseId(match?.params.folderId ?? null)
  const roots = useRootFolders()
  const selected = useFolder(selectedId)
  // Explicit user toggles win; otherwise the selected folder and its ancestors start expanded,
  // so a deep link opens the tree down to the folder.
  const [toggled, setToggled] = useState<ReadonlyMap<number, boolean>>(new Map())
  const pathIds = new Set((selected.data?.breadcrumb ?? []).map((crumb) => crumb.id))

  const isExpanded = (id: number) => toggled.get(id) ?? pathIds.has(id)
  const toggle = (id: number) =>
    setToggled((prev) => new Map(prev).set(id, !(prev.get(id) ?? pathIds.has(id))))

  if (roots.isPending) {
    return (
      <Box sx={{ p: 2 }}>
        <CircularProgress size={20} />
      </Box>
    )
  }

  if (roots.isError) {
    return <QueryErrorAlert message="Couldn't load folders." onRetry={() => void roots.refetch()} />
  }

  return (
    <List dense role="tree" aria-label="Folder tree">
      {roots.data.map((node) => (
        <FolderTreeNode
          key={node.id}
          node={node}
          depth={0}
          selectedId={selectedId}
          isExpanded={isExpanded}
          onToggle={toggle}
        />
      ))}
    </List>
  )
}
```

Create `web/src/views/RootRedirect.tsx`:

```tsx
import { Box, CircularProgress, Typography } from '@mui/material'
import { Navigate } from 'react-router'
import { useRootFolders } from '../api/queries'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'

/** "/" opens the first root's top folder. */
export function RootRedirect() {
  const roots = useRootFolders()

  if (roots.isPending) {
    return (
      <Box sx={{ p: 3 }}>
        <CircularProgress size={24} />
      </Box>
    )
  }

  if (roots.isError) {
    return <QueryErrorAlert message="Couldn't load folders." onRetry={() => void roots.refetch()} />
  }

  const first = roots.data[0]
  if (first === undefined) {
    return <Typography sx={{ p: 3 }}>No image roots are configured.</Typography>
  }

  return <Navigate to={`/folders/${first.id}`} replace />
}
```

Create `web/src/app/AppShell.tsx`:

```tsx
import { AppBar, Box, Button, Toolbar, Typography } from '@mui/material'
import { NavLink, Outlet } from 'react-router'
import { FolderTree } from '../tree/FolderTree'

export function AppShell() {
  return (
    <div className="flex h-screen flex-col">
      <AppBar position="static" elevation={0}>
        <Toolbar variant="dense" sx={{ gap: 2 }}>
          <Typography variant="h6" component="div" sx={{ flexShrink: 0 }}>
            PictureManager
          </Typography>
          <Box sx={{ flex: 1 }} />
          <Button color="inherit" component={NavLink} to="/">
            Folders
          </Button>
          <Button color="inherit" component={NavLink} to="/favorites">
            Favorites
          </Button>
        </Toolbar>
      </AppBar>
      <div className="flex min-h-0 flex-1">
        <Box
          component="nav"
          aria-label="Folders"
          sx={{ width: 280, flexShrink: 0, overflowY: 'auto', borderRight: 1, borderColor: 'divider' }}
        >
          <FolderTree />
        </Box>
        <main className="flex min-w-0 flex-1 flex-col">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
```

Create `web/src/app/routes.tsx`:

```tsx
import { Navigate, type RouteObject } from 'react-router'
import { RootRedirect } from '../views/RootRedirect'
import { AppShell } from './AppShell'

export const appRoutes: RouteObject[] = [
  {
    path: '/',
    element: <AppShell />,
    children: [
      { index: true, element: <RootRedirect /> },
      { path: 'folders/:folderId' },
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
]
```

Replace `web/src/App.tsx` with:

```tsx
import { CssBaseline, ThemeProvider, createTheme } from '@mui/material'
import { QueryClientProvider } from '@tanstack/react-query'
import { createBrowserRouter, RouterProvider } from 'react-router'
import { NotifyProvider } from './app/notify'
import { createQueryClient } from './app/queryClient'
import { appRoutes } from './app/routes'

// Follows the OS light/dark preference; there is no manual toggle.
const theme = createTheme({ colorSchemes: { light: true, dark: true } })
const queryClient = createQueryClient()
const router = createBrowserRouter(appRoutes)

function App() {
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <QueryClientProvider client={queryClient}>
        <NotifyProvider>
          <RouterProvider router={router} />
        </NotifyProvider>
      </QueryClientProvider>
    </ThemeProvider>
  )
}

export default App
```

Replace `web/src/vite-env.d.ts` with:

```ts
/// <reference types="vite/client" />
```

Delete `web/src/config/env.ts` (and the now-empty `web/src/config/` directory).

- [ ] **Step 4: Run the tests**

Run (from `web/`): `npm test`
Expected: PASS, including the 5 `FolderTree` tests.

- [ ] **Step 5: Run the gates**

Run (from `web/`): `npm run build`, `npm run lint`, `npm run format:check`
Expected: all succeed.

- [ ] **Step 6: Commit**

```bash
git add -A web/src
git commit -m "feat(web): add app shell, routes, root redirect and lazy folder tree

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Photo grid, folder view, favorites view

**Files:**
- Create: `web/src/grid/columns.ts`, `web/src/grid/PhotoTile.tsx`, `web/src/grid/PhotoGrid.tsx`
- Create: `web/src/views/ImageBrowser.tsx`, `web/src/views/GridHeader.tsx`, `web/src/views/GridSkeleton.tsx`
- Create: `web/src/views/FolderView.tsx`, `web/src/views/FavoritesView.tsx`
- Create: `web/src/shared/EmptyMessage.tsx`
- Modify: `web/src/app/routes.tsx`
- Test: `web/src/grid/columns.test.ts`, `web/src/grid/PhotoTile.test.tsx`, `web/src/views/FolderView.test.tsx`, `web/src/views/FavoritesView.test.tsx`

**Interfaces:**
- Consumes: `useFolder`, `useImages`, `useSetFavorite`, `parseGridParams`, `parseId`, `withParams`, `isNotFound`, `QueryErrorAlert`, and the Task 3 and 4 test helpers.
- Produces:
  - **Grid layout** (`src/grid/columns.ts`):
    - `MIN_TILE_WIDTH = 180`, `TILE_GAP = 4`
    - `columnCount(width)`
    - `tileSize(width, columns)`
  - **Tile** (`src/grid/PhotoTile.tsx`):
    - `PhotoTile({ item, size, caption, dimmed, onOpen, onToggleFavorite })`
    - The tile is `role="button"`, named `{fileName}{extension}`, with `data-testid="tile-{id}"` and `data-dimmed`.
    - Its star button is named `Add {name} to favorites` / `Remove {name} from favorites`.
  - **Grid** (`src/grid/PhotoGrid.tsx`): `PhotoGrid({ items, hasNextPage, isFetchingNextPage, fetchNextPage, renderTile })`.
  - **Browser** (`src/views/ImageBrowser.tsx`): `ImageBrowser({ filter, header, banner?, dimUnfavorited?, captionFor?, emptyState })`.
  - **Header** (`src/views/GridHeader.tsx`): `GridHeader({ title, path?, count?, sort, order })`, whose sort select and direction button write the URL.
  - **Shared bits:** `GridSkeleton()` (`src/views/GridSkeleton.tsx`) and `EmptyMessage({ children })` (`src/shared/EmptyMessage.tsx`).
  - **Views:** `FolderView` and `FavoritesView`.
  - **Routes:** `folders/:folderId` → `FolderView`, and `favorites` → `FavoritesView`.

- [ ] **Step 1: Write the failing tests**

Create `web/src/grid/columns.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { columnCount, tileSize } from './columns'

describe('columnCount', () => {
  it.each([
    [0, 1],
    [179, 1],
    [359, 1],
    [360, 2],
    [1200, 6],
  ])('%i px → %i columns', (width, expected) => expect(columnCount(width)).toBe(expected))
})

describe('tileSize', () => {
  it('fills the row, leaving the gaps', () => expect(tileSize(1200, 6)).toBe(196))
  it('never goes negative', () => expect(tileSize(0, 1)).toBe(0))
})
```

Create `web/src/grid/PhotoTile.test.tsx`:

```tsx
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { image } from '../test/fixtures'
import { PhotoTile } from './PhotoTile'

const noop = () => {}

describe('PhotoTile', () => {
  it('shows a processing placeholder until the image has a thumbnail', () => {
    render(
      <PhotoTile
        item={image(1, 3, { thumbnailUrl: null, previewUrl: null })}
        size={180}
        caption={null}
        dimmed={false}
        onOpen={noop}
        onToggleFavorite={noop}
      />,
    )
    expect(screen.getByText('Processing')).toBeInTheDocument()
  })

  it('falls back to a broken-image icon when the thumbnail fails to load', () => {
    render(
      <PhotoTile item={image(1, 3)} size={180} caption={null} dimmed={false} onOpen={noop} onToggleFavorite={noop} />,
    )
    fireEvent.error(screen.getByTestId('tile-1').querySelector('img')!)
    expect(screen.getByText('Thumbnail unavailable')).toBeInTheDocument()
  })

  it('shows the caption and opens on Enter', async () => {
    const onOpen = vi.fn()
    render(
      <PhotoTile item={image(7, 3)} size={180} caption="dev/Holidays/Madeira" dimmed={false} onOpen={onOpen} onToggleFavorite={noop} />,
    )
    expect(screen.getByText('dev/Holidays/Madeira')).toBeInTheDocument()
    screen.getByRole('button', { name: 'IMG_0007.jpg' }).focus()
    await userEvent.keyboard('{Enter}')
    expect(onOpen).toHaveBeenCalledWith(7)
  })
})
```

Create `web/src/views/FolderView.test.tsx`:

```tsx
import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { madeiraImages } from '../test/fixtures'
import { pagedImages } from '../test/handlers'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('FolderView', () => {
  it('shows the folder name, its clickable path, the photo count and its photos', async () => {
    renderApp('/folders/3')
    expect(await screen.findByRole('heading', { name: 'Madeira' })).toBeInTheDocument()
    const path = screen.getByRole('navigation', { name: 'Folder path' })
    expect(within(path).getByRole('link', { name: 'Holidays' })).toHaveAttribute('href', '/folders/2')
    expect(screen.getByText('3 photos')).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'IMG_0001.jpg' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'IMG_0003.jpg' })).toBeInTheDocument()
  })

  it('warns about a missing folder and hides its count', async () => {
    renderApp('/folders/4')
    expect(
      await screen.findByText(
        "This folder is missing on disk. Its photos are hidden until it's back. Rescan or remove it (Admin).",
      ),
    ).toBeInTheDocument()
    expect(screen.queryByText('1 photo')).not.toBeInTheDocument()
  })

  it('explains a folder with no direct photos', async () => {
    renderApp('/folders/1')
    expect(
      await screen.findByText('No photos directly in this folder. Pick a subfolder in the tree.'),
    ).toBeInTheDocument()
  })

  it('shows "Folder not found" for an unknown folder', async () => {
    renderApp('/folders/99')
    expect(await screen.findByText('Folder not found.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Go to the first folder' })).toHaveAttribute('href', '/')
  })

  it('changing the sort starts a fresh listing (no stale cursor) in that order', async () => {
    const requests: URLSearchParams[] = []
    server.use(
      http.get('/api/images', ({ request }) => {
        const query = new URL(request.url).searchParams
        requests.push(query)
        return query.get('cursor') === null
          ? HttpResponse.json({ items: madeiraImages, nextCursor: 'p1' })
          : HttpResponse.json({ items: [], nextCursor: null })
      }),
    )
    const { user, router } = renderApp('/folders/3')
    await waitFor(() => expect(requests.some((q) => q.get('cursor') === 'p1')).toBe(true))

    await user.click(screen.getByRole('combobox', { name: /Sort/ }))
    await user.click(await screen.findByRole('option', { name: 'Name' }))

    await waitFor(() => expect(requests.some((q) => q.get('sort') === 'name')).toBe(true))
    const firstNameRequest = requests.find((q) => q.get('sort') === 'name')!
    expect(firstNameRequest.get('order')).toBe('asc')
    expect(firstNameRequest.get('cursor')).toBeNull()
    expect(router.state.location.search).toBe('?sort=name')
  })

  it('toggles the direction', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('button', { name: 'Descending, switch to ascending' }))
    expect(router.state.location.search).toBe('?order=asc')
  })

  it('puts the photo in the URL when a tile is clicked', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('button', { name: 'IMG_0001.jpg' }))
    expect(router.state.location.search).toBe('?image=20')
  })

  it('stars a photo from its tile without opening it', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('button', { name: 'Add IMG_0001.jpg to favorites' }))
    expect(await screen.findByRole('button', { name: 'Remove IMG_0001.jpg from favorites' })).toBeInTheDocument()
    expect(router.state.location.search).toBe('')
  })

  it('loads the next page as the end of the grid comes into view', async () => {
    server.use(pagedImages([madeiraImages.slice(0, 2), madeiraImages.slice(2)]))
    renderApp('/folders/3')
    expect(await screen.findByRole('button', { name: 'IMG_0003.jpg' })).toBeInTheDocument()
  })

  it('offers Retry when photos fail to load', async () => {
    let fail = true
    server.use(
      http.get('/api/images', () =>
        fail
          ? HttpResponse.json({ title: 'boom' }, { status: 500 })
          : HttpResponse.json({ items: madeiraImages, nextCursor: null }),
      ),
    )
    const { user } = renderApp('/folders/3')
    expect(await screen.findByText("Couldn't load photos.")).toBeInTheDocument()
    fail = false
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByRole('button', { name: 'IMG_0001.jpg' })).toBeInTheDocument()
  })
})
```

Create `web/src/views/FavoritesView.test.tsx`:

```tsx
import { screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('FavoritesView', () => {
  it('lists only favorites, without a count', async () => {
    renderApp('/favorites')
    expect(await screen.findByRole('heading', { name: 'Favorites' })).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'IMG_0002.jpg' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'IMG_0001.jpg' })).not.toBeInTheDocument()
    expect(screen.queryByText(/photos?$/)).not.toBeInTheDocument()
  })

  it('keeps an unstarred photo in view, dimmed, until the view is left', async () => {
    const { user } = renderApp('/favorites')
    await user.click(await screen.findByRole('button', { name: 'Remove IMG_0002.jpg from favorites' }))
    expect(await screen.findByRole('button', { name: 'Add IMG_0002.jpg to favorites' })).toBeInTheDocument()
    expect(screen.getByTestId('tile-21')).toHaveAttribute('data-dimmed', 'true')
  })

  it('says when there are no favorites', async () => {
    server.use(http.get('/api/images', () => HttpResponse.json({ items: [], nextCursor: null })))
    renderApp('/favorites')
    expect(
      await screen.findByText('No favorites yet. Star a photo with ★ or F in the viewer.'),
    ).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run to verify they fail**

Run (from `web/`): `npm test`
Expected: FAIL. `./columns` and `./PhotoTile` can't be resolved, and the view tests find no heading because the routes have no views yet.

- [ ] **Step 3: Implement**

Create `web/src/grid/columns.ts`:

```ts
export const MIN_TILE_WIDTH = 180
export const TILE_GAP = 4

export function columnCount(width: number): number {
  return Math.max(1, Math.floor(width / MIN_TILE_WIDTH))
}

/** Square tile edge that fills a row of `columns` tiles separated by TILE_GAP. */
export function tileSize(width: number, columns: number): number {
  return Math.max(0, Math.floor((width - TILE_GAP * (columns - 1)) / columns))
}
```

Create `web/src/shared/EmptyMessage.tsx`:

```tsx
import { Typography } from '@mui/material'
import type { ReactNode } from 'react'

export function EmptyMessage({ children }: { children: ReactNode }) {
  return (
    <Typography sx={{ p: 3 }} color="text.secondary">
      {children}
    </Typography>
  )
}
```

Create `web/src/views/GridSkeleton.tsx`:

```tsx
import { Box, Skeleton } from '@mui/material'

export function GridSkeleton() {
  return (
    <Box aria-busy="true" aria-label="Loading photos" sx={{ display: 'flex', flexWrap: 'wrap', gap: '4px' }}>
      {Array.from({ length: 12 }, (_, index) => (
        <Skeleton key={index} variant="rectangular" width={180} height={180} />
      ))}
    </Box>
  )
}
```

Create `web/src/grid/PhotoTile.tsx`:

```tsx
import BrokenImageIcon from '@mui/icons-material/BrokenImage'
import HourglassEmptyIcon from '@mui/icons-material/HourglassEmpty'
import StarIcon from '@mui/icons-material/Star'
import StarBorderIcon from '@mui/icons-material/StarBorder'
import { Box, IconButton, Typography } from '@mui/material'
import { useState, type ReactNode } from 'react'
import type { ImageListItem } from '../api/types'

type Props = {
  item: ImageListItem
  size: number
  caption: string | null
  dimmed: boolean
  onOpen: (id: number) => void
  onToggleFavorite: (item: ImageListItem) => void
}

export function PhotoTile({ item, size, caption, dimmed, onOpen, onToggleFavorite }: Props) {
  const [failedSrc, setFailedSrc] = useState<string | null>(null)
  const name = `${item.fileName}${item.extension}`
  const thumbnail = item.thumbnailUrl

  let content: ReactNode
  if (thumbnail === null) {
    content = <Placeholder icon={<HourglassEmptyIcon />} label="Processing" />
  } else if (failedSrc === thumbnail) {
    content = <Placeholder icon={<BrokenImageIcon />} label="Thumbnail unavailable" />
  } else {
    content = (
      <img
        src={thumbnail}
        alt=""
        loading="lazy"
        onError={() => setFailedSrc(thumbnail)}
        style={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block' }}
      />
    )
  }

  return (
    <Box
      role="button"
      tabIndex={0}
      aria-label={name}
      title={name}
      data-testid={`tile-${item.id}`}
      data-dimmed={dimmed}
      onClick={() => onOpen(item.id)}
      onKeyDown={(event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault()
          onOpen(item.id)
        }
      }}
      sx={{
        position: 'relative',
        width: size,
        height: size,
        flexShrink: 0,
        overflow: 'hidden',
        cursor: 'pointer',
        bgcolor: 'action.hover',
        opacity: dimmed ? 0.4 : 1,
        '& .tile-star': { opacity: item.isFavorite ? 1 : 0 },
        '&:hover .tile-star, &:focus-within .tile-star': { opacity: 1 },
      }}
    >
      {content}
      <IconButton
        className="tile-star"
        size="small"
        aria-label={item.isFavorite ? `Remove ${name} from favorites` : `Add ${name} to favorites`}
        aria-pressed={item.isFavorite}
        onClick={(event) => {
          event.stopPropagation()
          onToggleFavorite(item)
        }}
        onKeyDown={(event) => event.stopPropagation()}
        sx={{
          position: 'absolute',
          top: 4,
          right: 4,
          color: 'warning.main',
          bgcolor: 'rgba(0,0,0,0.35)',
          '&:hover': { bgcolor: 'rgba(0,0,0,0.55)' },
        }}
      >
        {item.isFavorite ? <StarIcon fontSize="small" /> : <StarBorderIcon fontSize="small" />}
      </IconButton>
      {caption !== null && (
        <Typography
          variant="caption"
          noWrap
          sx={{
            position: 'absolute',
            left: 0,
            right: 0,
            bottom: 0,
            px: 1,
            py: 0.25,
            color: 'common.white',
            bgcolor: 'rgba(0,0,0,0.5)',
          }}
        >
          {caption}
        </Typography>
      )}
    </Box>
  )
}

function Placeholder({ icon, label }: { icon: ReactNode; label: string }) {
  return (
    <Box
      sx={{
        width: '100%',
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 0.5,
        color: 'text.secondary',
      }}
    >
      {icon}
      <Typography variant="caption">{label}</Typography>
    </Box>
  )
}
```

Create `web/src/grid/PhotoGrid.tsx`:

```tsx
import { LinearProgress } from '@mui/material'
import { useVirtualizer } from '@tanstack/react-virtual'
import { Fragment, useCallback, useEffect, useState, type ReactNode } from 'react'
import type { ImageListItem } from '../api/types'
import { columnCount, TILE_GAP, tileSize } from './columns'

type Props = {
  items: ImageListItem[]
  hasNextPage: boolean
  isFetchingNextPage: boolean
  fetchNextPage: () => void
  renderTile: (item: ImageListItem, size: number) => ReactNode
}

/** Rows within this many of the end of the loaded items trigger the next page. */
const PREFETCH_ROWS = 2

export function PhotoGrid({ items, hasNextPage, isFetchingNextPage, fetchNextPage, renderTile }: Props) {
  const [scrollElement, setScrollElement] = useState<HTMLDivElement | null>(null)
  const [width, setWidth] = useState(0)

  const attach = useCallback((element: HTMLDivElement | null) => {
    setScrollElement(element)
    if (element === null) return
    setWidth(element.clientWidth)
    const observer = new ResizeObserver(() => setWidth(element.clientWidth))
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  const columns = columnCount(width)
  const size = tileSize(width, columns)
  const rowCount = Math.ceil(items.length / columns)

  const virtualizer = useVirtualizer({
    count: rowCount,
    getScrollElement: () => scrollElement,
    estimateSize: () => size + TILE_GAP,
    overscan: 2,
  })

  useEffect(() => {
    virtualizer.measure()
  }, [size, virtualizer])

  const virtualRows = virtualizer.getVirtualItems()
  const lastVisibleRow = virtualRows.at(-1)?.index ?? -1

  useEffect(() => {
    if (hasNextPage && !isFetchingNextPage && lastVisibleRow >= rowCount - 1 - PREFETCH_ROWS) {
      fetchNextPage()
    }
  }, [hasNextPage, isFetchingNextPage, lastVisibleRow, rowCount, fetchNextPage])

  return (
    <div ref={attach} className="min-h-0 flex-1 overflow-y-auto" data-testid="photo-grid">
      <div style={{ height: virtualizer.getTotalSize(), position: 'relative' }}>
        {width > 0 &&
          virtualRows.map((row) => (
            <div
              key={row.key}
              style={{
                position: 'absolute',
                top: 0,
                left: 0,
                right: 0,
                transform: `translateY(${row.start}px)`,
                display: 'flex',
                gap: TILE_GAP,
              }}
            >
              {items.slice(row.index * columns, row.index * columns + columns).map((item) => (
                <Fragment key={item.id}>{renderTile(item, size)}</Fragment>
              ))}
            </div>
          ))}
      </div>
      {isFetchingNextPage && <LinearProgress />}
    </div>
  )
}
```

Create `web/src/views/GridHeader.tsx`:

```tsx
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward'
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward'
import { Box, Breadcrumbs, IconButton, Link, MenuItem, TextField, Typography } from '@mui/material'
import { Link as RouterLink, useSearchParams } from 'react-router'
import type { BreadcrumbItem } from '../api/types'
import { withParams, type Order, type Sort } from '../routing/urlState'

type Props = {
  title: string
  path?: BreadcrumbItem[]
  count?: number
  sort: Sort
  order: Order
}

export function GridHeader({ title, path, count, sort, order }: Props) {
  const [searchParams, setSearchParams] = useSearchParams()

  return (
    <Box
      sx={{ display: 'flex', alignItems: 'center', gap: 2, px: 2, py: 1, borderBottom: 1, borderColor: 'divider' }}
    >
      <Typography variant="h6" component="h1" noWrap sx={{ flexShrink: 0 }}>
        {title}
      </Typography>
      {path && (
        <Breadcrumbs aria-label="Folder path" sx={{ minWidth: 0, color: 'text.secondary', fontSize: 14 }}>
          {path.map((crumb) => (
            <Link key={crumb.id} component={RouterLink} to={`/folders/${crumb.id}`} underline="hover" color="inherit">
              {crumb.name}
            </Link>
          ))}
        </Breadcrumbs>
      )}
      <Box sx={{ flex: 1 }} />
      {count !== undefined && (
        <Typography variant="body2" color="text.secondary" sx={{ flexShrink: 0 }}>
          {count === 1 ? '1 photo' : `${count} photos`}
        </Typography>
      )}
      <TextField
        select
        size="small"
        label="Sort"
        value={sort}
        onChange={(event) => setSearchParams(withParams(searchParams, { sort: event.target.value, order: null }))}
        sx={{ minWidth: 110 }}
      >
        <MenuItem value="date">Date</MenuItem>
        <MenuItem value="name">Name</MenuItem>
      </TextField>
      <IconButton
        aria-label={order === 'asc' ? 'Ascending, switch to descending' : 'Descending, switch to ascending'}
        onClick={() => setSearchParams(withParams(searchParams, { order: order === 'asc' ? 'desc' : 'asc' }))}
      >
        {order === 'asc' ? <ArrowUpwardIcon /> : <ArrowDownwardIcon />}
      </IconButton>
    </Box>
  )
}
```

Create `web/src/views/ImageBrowser.tsx`:

```tsx
import { useMemo, type ReactNode } from 'react'
import { useSearchParams } from 'react-router'
import { useSetFavorite } from '../api/favorites'
import type { ImageFilter } from '../api/imageFilter'
import { useImages } from '../api/queries'
import type { ImageListItem } from '../api/types'
import { PhotoGrid } from '../grid/PhotoGrid'
import { PhotoTile } from '../grid/PhotoTile'
import { withParams } from '../routing/urlState'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { GridSkeleton } from './GridSkeleton'

type Props = {
  filter: ImageFilter
  header: ReactNode
  banner?: ReactNode
  /** Favorites view: an unstarred photo stays, dimmed, until the view is left. */
  dimUnfavorited?: boolean
  captionFor?: (item: ImageListItem) => string | null
  emptyState: ReactNode
}

/** Header, banner and virtualized grid for one image filter. */
export function ImageBrowser({ filter, header, banner, dimUnfavorited = false, captionFor, emptyState }: Props) {
  const [searchParams, setSearchParams] = useSearchParams()
  const images = useImages(filter)
  const setFavorite = useSetFavorite()
  const items = useMemo(() => images.data?.pages.flatMap((page) => page.items) ?? [], [images.data])

  // Opening is a push (Back closes the viewer); the state marks it as opened in-app.
  const open = (id: number) =>
    setSearchParams(withParams(searchParams, { image: id }), { state: { viewer: true } })

  let body: ReactNode
  if (images.isPending) {
    body = <GridSkeleton />
  } else if (images.isError) {
    body = <QueryErrorAlert message="Couldn't load photos." onRetry={() => void images.refetch()} />
  } else if (items.length === 0) {
    body = emptyState
  } else {
    body = (
      <PhotoGrid
        // A new filter (folder, sort, query) starts a new grid at the top.
        key={JSON.stringify(filter)}
        items={items}
        hasNextPage={images.hasNextPage}
        isFetchingNextPage={images.isFetchingNextPage}
        fetchNextPage={() => void images.fetchNextPage({ cancelRefetch: false })}
        renderTile={(item, size) => (
          <PhotoTile
            item={item}
            size={size}
            caption={captionFor?.(item) ?? null}
            dimmed={dimUnfavorited && !item.isFavorite}
            onOpen={open}
            onToggleFavorite={(tile) => setFavorite.mutate({ id: tile.id, isFavorite: !tile.isFavorite })}
          />
        )}
      />
    )
  }

  return (
    <>
      {header}
      {banner}
      {body}
    </>
  )
}
```

Create `web/src/views/FolderView.tsx`:

```tsx
import { Alert, Box, Link, Typography } from '@mui/material'
import { Link as RouterLink, useParams, useSearchParams } from 'react-router'
import { isNotFound } from '../api/client'
import { useFolder } from '../api/queries'
import { parseGridParams, parseId } from '../routing/urlState'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { GridHeader } from './GridHeader'
import { GridSkeleton } from './GridSkeleton'
import { ImageBrowser } from './ImageBrowser'

export function FolderView() {
  const params = useParams()
  const folderId = parseId(params.folderId ?? null)
  const [searchParams] = useSearchParams()
  const { sort, order } = parseGridParams(searchParams)
  const folder = useFolder(folderId)

  if (folderId === null || isNotFound(folder.error)) {
    return (
      <Box sx={{ p: 3 }}>
        <Typography gutterBottom>Folder not found.</Typography>
        <Link component={RouterLink} to="/">
          Go to the first folder
        </Link>
      </Box>
    )
  }

  if (folder.isPending) return <GridSkeleton />

  if (folder.isError) {
    return <QueryErrorAlert message="Couldn't load this folder." onRetry={() => void folder.refetch()} />
  }

  const detail = folder.data
  let banner = null
  if (detail.isMissing) {
    banner = (
      <Alert severity="warning" sx={{ m: 2, mb: 0 }}>
        This folder is missing on disk. Its photos are hidden until it's back. Rescan or remove it (Admin).
      </Alert>
    )
  } else if (detail.imageCount === 0) {
    banner = (
      <Alert severity="info" sx={{ m: 2, mb: 0 }}>
        No photos directly in this folder. Pick a subfolder in the tree.
      </Alert>
    )
  }

  return (
    <ImageBrowser
      filter={{ kind: 'folder', folderId, sort, order }}
      header={
        <GridHeader
          title={detail.name}
          path={detail.breadcrumb}
          // A missing folder's photos are hidden, so its count would contradict the empty grid.
          count={detail.isMissing ? undefined : detail.imageCount}
          sort={sort}
          order={order}
        />
      }
      banner={banner}
      emptyState={null}
    />
  )
}
```

Create `web/src/views/FavoritesView.tsx`:

```tsx
import { useSearchParams } from 'react-router'
import { parseGridParams } from '../routing/urlState'
import { EmptyMessage } from '../shared/EmptyMessage'
import { GridHeader } from './GridHeader'
import { ImageBrowser } from './ImageBrowser'

export function FavoritesView() {
  const [searchParams] = useSearchParams()
  const { sort, order } = parseGridParams(searchParams)

  return (
    <ImageBrowser
      filter={{ kind: 'favorites', sort, order }}
      header={<GridHeader title="Favorites" sort={sort} order={order} />}
      dimUnfavorited
      emptyState={<EmptyMessage>No favorites yet. Star a photo with ★ or F in the viewer.</EmptyMessage>}
    />
  )
}
```

Replace `web/src/app/routes.tsx` with:

```tsx
import { Navigate, type RouteObject } from 'react-router'
import { FavoritesView } from '../views/FavoritesView'
import { FolderView } from '../views/FolderView'
import { RootRedirect } from '../views/RootRedirect'
import { AppShell } from './AppShell'

export const appRoutes: RouteObject[] = [
  {
    path: '/',
    element: <AppShell />,
    children: [
      { index: true, element: <RootRedirect /> },
      { path: 'folders/:folderId', element: <FolderView /> },
      { path: 'favorites', element: <FavoritesView /> },
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
]
```

- [ ] **Step 4: Run the tests**

Run (from `web/`): `npm test`
Expected: PASS: `columns`, `PhotoTile`, the 10 `FolderView` tests, the 3 `FavoritesView` tests, and all earlier suites.

- [ ] **Step 5: Run the gates**

Run (from `web/`): `npm run build`, `npm run lint`, `npm run format:check`
Expected: all succeed.

- [ ] **Step 6: Commit**

```bash
git add -A web/src
git commit -m "feat(web): add virtualized photo grid, folder view and favorites view

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Search box and search view

**Files:**
- Create: `web/src/search/SearchBox.tsx`
- Create: `web/src/views/SearchView.tsx`
- Modify: `web/src/app/AppShell.tsx` (put the search box in the app bar)
- Modify: `web/src/app/routes.tsx` (add `search`)
- Test: `web/src/search/Search.test.tsx`

**Interfaces:**
- Consumes: `parseSearchState`, `parseGridParams`, `parseId`, `useFolder`, `ImageBrowser`, `GridHeader`, `EmptyMessage`.
- Produces:
  - `SEARCH_DEBOUNCE_MS = 300`
  - `SearchBox()`: a text box with the accessible name `Search file names`, plus a scope chip. The chip is either `In this folder` (outlined, toggles on) or `in: {folder name}` (filled, with a delete icon).
  - `SearchView()`
  - route `search`

- [ ] **Step 1: Write the failing tests**

Create `web/src/search/Search.test.tsx`:

```tsx
import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'
import { server } from '../test/server'

const searchBox = () => screen.findByRole('textbox', { name: 'Search file names' })

describe('Search', () => {
  it('navigates to the results after the debounce, not on every keystroke', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.type(await searchBox(), 'IMG')
    expect(router.state.location.pathname).toBe('/folders/3')
    await waitFor(() => expect(router.state.location.pathname).toBe('/search'))
    expect(router.state.location.search).toBe('?q=IMG')
    expect(await screen.findByRole('heading', { name: 'Search: IMG' })).toBeInTheDocument()
  })

  it("shows each result's folder path, and scopes to the folder when the chip is on", async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('button', { name: 'In this folder' }))
    await user.type(await searchBox(), 'IMG_0001')
    await waitFor(() => expect(router.state.location.search).toBe('?q=IMG_0001&in=3'))
    const tile = await screen.findByRole('button', { name: 'IMG_0001.jpg' })
    expect(within(tile).getByText('dev/Holidays/Madeira')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'IMG_0001 copy.jpg' })).not.toBeInTheDocument()
  })

  it('searches everywhere once the scope chip is removed', async () => {
    const { user, router } = renderApp('/search?q=IMG_0001&in=3')
    const chip = await screen.findByRole('button', { name: 'in: Madeira' })
    await user.click(within(chip).getByTestId('CancelIcon'))
    await waitFor(() => expect(router.state.location.search).toBe('?q=IMG_0001'))
    expect(await screen.findByRole('button', { name: 'IMG_0001 copy.jpg' })).toBeInTheDocument()
  })

  it('shows a prompt and sends no request for an empty query', async () => {
    let requests = 0
    server.use(
      http.get('/api/images', () => {
        requests++
        return HttpResponse.json({ items: [], nextCursor: null })
      }),
    )
    renderApp('/search')
    expect(await screen.findByText('Type a file name to search.')).toBeInTheDocument()
    expect(requests).toBe(0)
  })

  it('says when nothing matches', async () => {
    renderApp('/search?q=zzz')
    expect(await screen.findByText('No photos match “zzz”.')).toBeInTheDocument()
  })

  it('round-trips special characters in the query', async () => {
    const seen: (string | null)[] = []
    server.use(
      http.get('/api/images', ({ request }) => {
        seen.push(new URL(request.url).searchParams.get('fileName'))
        return HttpResponse.json({ items: [], nextCursor: null })
      }),
    )
    const { user, router } = renderApp('/folders/3')
    await user.type(await searchBox(), 'a&b #1 50%+')
    await waitFor(() => expect(router.state.location.pathname).toBe('/search'))
    expect(new URLSearchParams(router.state.location.search).get('q')).toBe('a&b #1 50%+')
    await waitFor(() => expect(seen).toContain('a&b #1 50%+'))
  })
})
```

- [ ] **Step 2: Run to verify they fail**

Run (from `web/`): `npm test`
Expected: FAIL. There is no `Search file names` textbox.

- [ ] **Step 3: Implement**

Create `web/src/search/SearchBox.tsx`:

```tsx
import SearchIcon from '@mui/icons-material/Search'
import { Box, Chip, InputAdornment, TextField } from '@mui/material'
import { useEffect, useRef, useState } from 'react'
import { useLocation, useMatch, useNavigate } from 'react-router'
import { useFolder } from '../api/queries'
import { parseId, parseSearchState } from '../routing/urlState'

export const SEARCH_DEBOUNCE_MS = 300

/** File-name search in the app bar. Typing navigates to /search after a pause. */
export function SearchBox() {
  const navigate = useNavigate()
  const location = useLocation()
  const folderMatch = useMatch('/folders/:folderId')
  const onSearchRoute = location.pathname === '/search'
  const fromUrl = parseSearchState(new URLSearchParams(location.search))

  const [text, setText] = useState(onSearchRoute ? fromUrl.q : '')
  const [scopeId, setScopeId] = useState<number | null>(onSearchRoute ? fromUrl.in : null)
  const currentFolderId = parseId(folderMatch?.params.folderId ?? null)
  const scopeFolder = useFolder(scopeId)
  const timer = useRef<number | undefined>(undefined)

  useEffect(() => () => window.clearTimeout(timer.current), [])

  const submit = (nextText: string, nextScope: number | null) => {
    const q = nextText.trim()
    // Clearing the box elsewhere shouldn't jump to an empty search page.
    if (q === '' && !onSearchRoute) return
    const params = new URLSearchParams()
    if (q !== '') params.set('q', q)
    if (nextScope !== null) params.set('in', String(nextScope))
    const search = params.size > 0 ? `?${params}` : ''
    // Refining a search replaces the entry, so Back leaves search instead of replaying keystrokes.
    navigate({ pathname: '/search', search }, { replace: onSearchRoute })
  }

  const changeText = (next: string) => {
    setText(next)
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => submit(next, scopeId), SEARCH_DEBOUNCE_MS)
  }

  const changeScope = (next: number | null) => {
    setScopeId(next)
    if (text.trim() !== '') submit(text, next)
  }

  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flex: 1, maxWidth: 560 }}>
      <TextField
        size="small"
        fullWidth
        placeholder="Search file names…"
        value={text}
        onChange={(event) => changeText(event.target.value)}
        slotProps={{
          htmlInput: { 'aria-label': 'Search file names' },
          input: {
            startAdornment: (
              <InputAdornment position="start">
                <SearchIcon fontSize="small" />
              </InputAdornment>
            ),
            sx: { bgcolor: 'background.paper' },
          },
        }}
      />
      {scopeId !== null ? (
        <Chip
          label={`in: ${scopeFolder.data?.name ?? '…'}`}
          onDelete={() => changeScope(null)}
          color="secondary"
        />
      ) : (
        currentFolderId !== null && (
          <Chip
            label="In this folder"
            variant="outlined"
            onClick={() => changeScope(currentFolderId)}
            sx={{ color: 'inherit', borderColor: 'currentColor' }}
          />
        )
      )}
    </Box>
  )
}
```

Create `web/src/views/SearchView.tsx`:

```tsx
import { useSearchParams } from 'react-router'
import type { ImageFilter } from '../api/imageFilter'
import { parseGridParams, parseSearchState } from '../routing/urlState'
import { EmptyMessage } from '../shared/EmptyMessage'
import { GridHeader } from './GridHeader'
import { ImageBrowser } from './ImageBrowser'

export function SearchView() {
  const [searchParams] = useSearchParams()
  const { sort, order } = parseGridParams(searchParams)
  const { q, in: scopeId } = parseSearchState(searchParams)

  if (q === '') return <EmptyMessage>Type a file name to search.</EmptyMessage>

  const filter: ImageFilter =
    scopeId === null ? { kind: 'search', q, sort, order } : { kind: 'search', q, in: scopeId, sort, order }

  return (
    <ImageBrowser
      filter={filter}
      header={<GridHeader title={`Search: ${q}`} sort={sort} order={order} />}
      captionFor={(item) => item.folderPath}
      emptyState={<EmptyMessage>No photos match “{q}”.</EmptyMessage>}
    />
  )
}
```

In `web/src/app/AppShell.tsx`:
- add `import { SearchBox } from '../search/SearchBox'`;
- replace the spacer `<Box sx={{ flex: 1 }} />` inside the `Toolbar` with:

```tsx
          <Box sx={{ flex: 1, display: 'flex', justifyContent: 'center' }}>
            <SearchBox />
          </Box>
```

In `web/src/app/routes.tsx`:
- add `import { SearchView } from '../views/SearchView'`;
- add `{ path: 'search', element: <SearchView /> },` after the `favorites` route.

- [ ] **Step 4: Run the tests**

Run (from `web/`): `npm test`
Expected: PASS, including the 6 `Search` tests.

- [ ] **Step 5: Run the gates**

Run (from `web/`): `npm run build`, `npm run lint`, `npm run format:check`
Expected: all succeed.

- [ ] **Step 6: Commit**

```bash
git add -A web/src
git commit -m "feat(web): add debounced file-name search with folder scope chip

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Photo viewer and info panel

**Files:**
- Create: `web/src/viewer/format.ts`, `web/src/viewer/InfoPanel.tsx`, `web/src/viewer/PhotoViewer.tsx`
- Modify: `web/src/views/ImageBrowser.tsx` (render the viewer)
- Test: `web/src/viewer/format.test.ts`, `web/src/viewer/PhotoViewer.test.tsx`

**Interfaces:**
- Consumes:
  - `useImages(filter, { refetchOnMount: false })`, `useImage`, `useSetFavorite`, `isNotFound`;
  - `parseGridParams`, `withParams`;
  - the `{ viewer: true }` history state set by `ImageBrowser.open`.
- Produces:
  - **Formatters** (`src/viewer/format.ts`):
    - `formatBytes(bytes)`
    - `formatDateTaken(value)`
    - `openStreetMapUrl(latitude, longitude)`
  - **`PhotoViewer({ filter })`.** It reads `?image=` and shows a full-screen `Dialog`. Its controls:
    - buttons named `Previous photo`, `Next photo`, `Close viewer`, `Add to favorites` / `Remove from favorites`, and `Hide info` / `Show info`;
    - keys ←/→, Esc, `F`/`f` and `I`/`i`.
  - **`InfoPanel({ detail, isLoading, isError, onRetry })`**, rendered as `<aside aria-label="Photo info">`.
- **History rules:**
  - Opening the viewer is a push, done by `ImageBrowser`.
  - Stepping is a replace, keeping the history state.
  - Closing goes Back when the viewer was opened in-app (state `viewer: true`). Otherwise it removes `image` with a replace.

- [ ] **Step 1: Write the failing tests**

Create `web/src/viewer/format.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { formatBytes, formatDateTaken, openStreetMapUrl } from './format'

describe('formatBytes', () => {
  it.each([
    [500, '500 B'],
    [1536, '1.5 KB'],
    [2_400_000, '2.3 MB'],
    [5 * 1024 ** 3, '5.0 GB'],
  ])('%i → %s', (bytes, expected) => expect(formatBytes(bytes)).toBe(expected))
})

describe('formatDateTaken', () => {
  it('shows the camera time as written, without timezone conversion', () =>
    expect(formatDateTaken('2025-08-14T18:32:05')).toBe('2025-08-14 18:32'))
})

describe('openStreetMapUrl', () => {
  it('points a marker at the coordinates', () =>
    expect(openStreetMapUrl(32.6669, -16.9241)).toBe(
      'https://www.openstreetmap.org/?mlat=32.6669&mlon=-16.9241#map=16/32.6669/-16.9241',
    ))
})
```

Create `web/src/viewer/PhotoViewer.test.tsx`:

```tsx
import { act, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import type { ImageFilter } from '../api/imageFilter'
import { madeiraImages } from '../test/fixtures'
import { renderApp, renderRoutes } from '../test/render'
import { server } from '../test/server'
import { PhotoViewer } from './PhotoViewer'

const viewerImage = (name: string) => screen.findByRole('img', { name })
const infoPanel = () => screen.queryByRole('complementary', { name: 'Photo info' })

describe('PhotoViewer', () => {
  it('opens from the grid and steps with the arrow keys', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('button', { name: 'IMG_0001.jpg' }))
    expect(await viewerImage('IMG_0001.jpg')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Previous photo' })).not.toBeInTheDocument()

    await user.keyboard('{ArrowRight}')
    expect(await viewerImage('IMG_0002.jpg')).toBeInTheDocument()
    expect(router.state.location.search).toBe('?image=21')

    await user.keyboard('{ArrowLeft}')
    expect(await viewerImage('IMG_0001.jpg')).toBeInTheDocument()
  })

  it('Esc closes the viewer and Back does not walk through every photo', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('button', { name: 'IMG_0001.jpg' }))
    await viewerImage('IMG_0001.jpg')
    await user.keyboard('{ArrowRight}')
    await user.keyboard('{ArrowRight}')
    await waitFor(() => expect(router.state.location.search).toBe('?image=22'))

    await act(() => router.navigate(-1))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(router.state.location.search).toBe('')

    await user.click(screen.getByRole('button', { name: 'IMG_0002.jpg' }))
    await viewerImage('IMG_0002.jpg')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(router.state.location.search).toBe('')
  })

  it('Esc on a deep link closes without leaving the app', async () => {
    const { user, router } = renderApp('/folders/3?image=21')
    await viewerImage('IMG_0002.jpg')
    await user.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(router.state.location.pathname).toBe('/folders/3')
    expect(router.state.location.search).toBe('')
  })

  it('F toggles the favorite, and the grid tile follows', async () => {
    const { user } = renderApp('/folders/3?image=20')
    await viewerImage('IMG_0001.jpg')
    await screen.findByRole('button', { name: 'Add to favorites' })
    await user.keyboard('f')
    expect(await screen.findByRole('button', { name: 'Remove from favorites' })).toBeInTheDocument()
    await user.keyboard('{Escape}')
    expect(await screen.findByRole('button', { name: 'Remove IMG_0001.jpg from favorites' })).toBeInTheDocument()
  })

  it('I toggles the info panel and remembers it', async () => {
    const first = renderApp('/folders/3?image=20')
    await viewerImage('IMG_0001.jpg')
    expect(infoPanel()).toBeInTheDocument()
    await first.user.keyboard('i')
    expect(infoPanel()).not.toBeInTheDocument()
    expect(localStorage.getItem('pm.viewer.infoOpen')).toBe('false')
    first.unmount()

    renderApp('/folders/3?image=20')
    await viewerImage('IMG_0001.jpg')
    expect(infoPanel()).not.toBeInTheDocument()
  })

  it('shows the photo details, with a Go to folder link that closes the viewer', async () => {
    const { user, router } = renderApp('/favorites?image=21')
    await viewerImage('IMG_0002.jpg')
    const panel = await screen.findByRole('complementary', { name: 'Photo info' })
    expect(await within(panel).findByText('IMG_0002.jpg')).toBeInTheDocument()
    expect(within(panel).getByText('2025-08-14 18:32')).toBeInTheDocument()
    expect(within(panel).getByText('1200 × 800')).toBeInTheDocument()
    expect(within(panel).getByText('2.3 MB')).toBeInTheDocument()
    expect(within(panel).getByText('Canon EOS R6')).toBeInTheDocument()
    expect(within(panel).getByText('Best of 2025')).toBeInTheDocument()
    expect(within(panel).getByRole('link', { name: /OpenStreetMap/ })).toHaveAttribute(
      'href',
      expect.stringContaining('mlat=32.6669'),
    )

    await user.click(within(panel).getByRole('link', { name: 'Go to folder' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/folders/3'))
    expect(router.state.location.search).toBe('')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('opens a deep-linked photo that is not in the list, without arrows', async () => {
    renderApp('/favorites?image=20')
    expect(await viewerImage('IMG_0001.jpg')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByTestId('tile-21')).toBeInTheDocument())
    expect(screen.queryByRole('button', { name: 'Previous photo' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Next photo' })).not.toBeInTheDocument()
  })

  it('says an unknown photo is no longer available, and Close returns to the grid', async () => {
    const { user, router } = renderApp('/folders/3?image=999')
    expect(await screen.findByText('This photo is no longer available.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Close' }))
    await waitFor(() => expect(router.state.location.search).toBe(''))
  })

  it('ignores a malformed image param', async () => {
    renderApp('/folders/3?image=abc')
    expect(await screen.findByRole('button', { name: 'IMG_0001.jpg' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('stepping past the last loaded photo fetches the next page', async () => {
    const cursors: (string | null)[] = []
    server.use(
      http.get('/api/images', ({ request }) => {
        const cursor = new URL(request.url).searchParams.get('cursor')
        cursors.push(cursor)
        return cursor === null
          ? HttpResponse.json({ items: madeiraImages.slice(0, 2), nextCursor: 'p1' })
          : HttpResponse.json({ items: madeiraImages.slice(2), nextCursor: null })
      }),
    )
    const filter: ImageFilter = { kind: 'folder', folderId: 3, sort: 'date', order: 'desc' }
    const { user, router } = renderRoutes([{ path: '/', element: <PhotoViewer filter={filter} /> }], '/?image=21')

    await viewerImage('IMG_0002.jpg')
    await screen.findByRole('button', { name: 'Next photo' })
    expect(cursors).toEqual([null])

    await user.keyboard('{ArrowRight}')
    expect(await viewerImage('IMG_0003.jpg')).toBeInTheDocument()
    expect(router.state.location.search).toBe('?image=22')
    expect(cursors).toEqual([null, 'p1'])
    expect(screen.queryByRole('button', { name: 'Next photo' })).not.toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run to verify they fail**

Run (from `web/`): `npm test`
Expected: FAIL. `./format` and `./PhotoViewer` can't be resolved.

- [ ] **Step 3: Implement**

Create `web/src/viewer/format.ts`:

```ts
const UNITS = ['KB', 'MB', 'GB', 'TB']

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  let value = bytes / 1024
  let unit = 0
  while (value >= 1024 && unit < UNITS.length - 1) {
    value /= 1024
    unit++
  }
  return `${value.toFixed(1)} ${UNITS[unit] ?? 'TB'}`
}

/** The camera clock has no timezone, so show it as written ("2025-08-14T18:32:05" → "2025-08-14 18:32"). */
export function formatDateTaken(value: string): string {
  return value.replace('T', ' ').slice(0, 16)
}

export function openStreetMapUrl(latitude: number, longitude: number): string {
  return `https://www.openstreetmap.org/?mlat=${latitude}&mlon=${longitude}#map=16/${latitude}/${longitude}`
}
```

Create `web/src/viewer/InfoPanel.tsx`:

```tsx
import { Alert, Box, Button, Chip, CircularProgress, Link, Stack, Typography } from '@mui/material'
import type { ReactNode } from 'react'
import { Link as RouterLink } from 'react-router'
import type { ImageDetail } from '../api/types'
import { formatBytes, formatDateTaken, openStreetMapUrl } from './format'

type Props = {
  detail: ImageDetail | undefined
  isLoading: boolean
  isError: boolean
  onRetry: () => void
}

export function InfoPanel({ detail, isLoading, isError, onRetry }: Props) {
  let content: ReactNode
  if (isError) {
    content = (
      <Alert
        severity="error"
        action={
          <Button color="inherit" size="small" onClick={onRetry}>
            Retry
          </Button>
        }
      >
        Couldn't load details.
      </Alert>
    )
  } else if (isLoading || detail === undefined) {
    content = <CircularProgress size={20} color="inherit" />
  } else {
    content = <Details detail={detail} />
  }

  return (
    <Box
      component="aside"
      aria-label="Photo info"
      sx={{ width: 320, flexShrink: 0, overflowY: 'auto', p: 2, bgcolor: 'grey.900', color: 'grey.100' }}
    >
      {content}
    </Box>
  )
}

function Details({ detail }: { detail: ImageDetail }) {
  const camera = [detail.cameraMake, detail.cameraModel].filter(Boolean).join(' ')
  const rows: Array<[string, ReactNode]> = [
    ['Date taken', detail.dateTaken === null ? 'Unknown' : formatDateTaken(detail.dateTaken)],
    [
      'Dimensions',
      detail.width !== null && detail.height !== null ? `${detail.width} × ${detail.height}` : 'Unknown',
    ],
    ['File size', formatBytes(detail.fileSize)],
  ]
  if (camera !== '') rows.push(['Camera', camera])
  if (detail.lensModel) rows.push(['Lens', detail.lensModel])
  if (detail.latitude !== null && detail.longitude !== null) {
    rows.push([
      'Location',
      <Link
        href={openStreetMapUrl(detail.latitude, detail.longitude)}
        target="_blank"
        rel="noreferrer"
        color="inherit"
      >
        {`${detail.latitude.toFixed(5)}, ${detail.longitude.toFixed(5)} (OpenStreetMap)`}
      </Link>,
    ])
  }

  return (
    <Stack spacing={2}>
      <div>
        <Typography variant="subtitle1" sx={{ wordBreak: 'break-all' }}>
          {detail.fileName}
          {detail.extension}
        </Typography>
        <Typography variant="body2" sx={{ color: 'grey.400', wordBreak: 'break-all' }}>
          {detail.folderPath}
        </Typography>
        <Link component={RouterLink} to={`/folders/${detail.folderId}`} color="inherit" variant="body2">
          Go to folder
        </Link>
      </div>
      <Box component="dl" sx={{ m: 0 }}>
        {rows.map(([label, value]) => (
          <Box key={label} sx={{ mb: 1 }}>
            <Typography component="dt" variant="caption" sx={{ color: 'grey.400' }}>
              {label}
            </Typography>
            <Typography component="dd" variant="body2" sx={{ m: 0 }}>
              {value}
            </Typography>
          </Box>
        ))}
      </Box>
      <div>
        <Typography variant="caption" sx={{ color: 'grey.400' }}>
          Albums
        </Typography>
        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5, mt: 0.5 }}>
          {detail.albums.length === 0 ? (
            <Typography variant="body2">Not in any album</Typography>
          ) : (
            detail.albums.map((album) => (
              <Chip key={album.id} size="small" label={album.name} sx={{ color: 'inherit' }} variant="outlined" />
            ))
          )}
        </Box>
      </div>
      <details>
        <summary>Raw metadata</summary>
        <pre className="mt-2 overflow-x-auto text-xs">{JSON.stringify(detail.rawMetadata, null, 2)}</pre>
      </details>
    </Stack>
  )
}
```

Create `web/src/viewer/PhotoViewer.tsx`:

```tsx
import ChevronLeftIcon from '@mui/icons-material/ChevronLeft'
import ChevronRightIcon from '@mui/icons-material/ChevronRight'
import CloseIcon from '@mui/icons-material/Close'
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined'
import StarIcon from '@mui/icons-material/Star'
import StarBorderIcon from '@mui/icons-material/StarBorder'
import { Box, Button, CircularProgress, Dialog, IconButton, Stack, Typography } from '@mui/material'
import { useEffect, useEffectEvent, useMemo, useRef, useState } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router'
import { isNotFound } from '../api/client'
import { useSetFavorite } from '../api/favorites'
import type { ImageFilter } from '../api/imageFilter'
import { useImage, useImages } from '../api/queries'
import { parseGridParams, withParams } from '../routing/urlState'
import { InfoPanel } from './InfoPanel'

const INFO_PANEL_KEY = 'pm.viewer.infoOpen'

function readInfoOpen(): boolean {
  try {
    return localStorage.getItem(INFO_PANEL_KEY) !== 'false'
  } catch {
    return true
  }
}

function writeInfoOpen(open: boolean): void {
  try {
    localStorage.setItem(INFO_PANEL_KEY, String(open))
  } catch {
    // Storage unavailable (private mode, blocked): the panel just won't remember.
  }
}

type ViewerHistoryState = { viewer?: boolean } | null

/** Full-screen viewer for ?image=, stepping through the same cached list as the grid below it. */
export function PhotoViewer({ filter }: { filter: ImageFilter }) {
  const [searchParams, setSearchParams] = useSearchParams()
  const location = useLocation()
  const navigate = useNavigate()
  const imageId = parseGridParams(searchParams).image
  // No refetch on mount: it would drop a just-unstarred photo from an open Favorites view.
  const list = useImages(filter, { refetchOnMount: false })
  const detail = useImage(imageId)
  const setFavorite = useSetFavorite()
  const [infoOpen, setInfoOpen] = useState(readInfoOpen)
  const [loadedSrc, setLoadedSrc] = useState<string | null>(null)
  const pendingNext = useRef(false)

  const items = useMemo(() => list.data?.pages.flatMap((page) => page.items) ?? [], [list.data])
  const index = imageId === null ? -1 : items.findIndex((item) => item.id === imageId)
  const inList = index >= 0
  const current = inList ? items[index] : detail.data
  const prev = inList ? items[index - 1] : undefined
  const next = inList ? items[index + 1] : undefined
  const canGoPrev = prev !== undefined
  const canGoNext = inList && (next !== undefined || list.hasNextPage)
  const notFound = !inList && isNotFound(detail.error)

  // Stepping replaces the entry, so Back closes the viewer instead of replaying every photo.
  const show = (id: number) =>
    setSearchParams(withParams(searchParams, { image: id }), { replace: true, state: location.state })

  const close = () => {
    if ((location.state as ViewerHistoryState)?.viewer) navigate(-1)
    else setSearchParams(withParams(searchParams, { image: null }), { replace: true })
  }

  const goPrev = () => {
    if (prev !== undefined) show(prev.id)
  }

  const goNext = () => {
    if (next !== undefined) {
      show(next.id)
    } else if (inList && list.hasNextPage) {
      pendingNext.current = true
      void list.fetchNextPage({ cancelRefetch: false })
    }
  }

  const toggleFavorite = () => {
    if (current !== undefined) setFavorite.mutate({ id: current.id, isFavorite: !current.isFavorite })
  }

  const toggleInfo = () => {
    const nextOpen = !infoOpen
    setInfoOpen(nextOpen)
    writeInfoOpen(nextOpen)
  }

  const onNextLoaded = useEffectEvent((id: number) => {
    pendingNext.current = false
    show(id)
  })

  useEffect(() => {
    if (pendingNext.current && next !== undefined) onNextLoaded(next.id)
  }, [next])

  const onKeyDown = useEffectEvent((event: KeyboardEvent) => {
    if (event.altKey || event.ctrlKey || event.metaKey) return
    if (event.target instanceof HTMLElement && event.target.closest('input, textarea')) return
    switch (event.key) {
      case 'ArrowLeft':
        event.preventDefault()
        goPrev()
        break
      case 'ArrowRight':
        event.preventDefault()
        goNext()
        break
      case 'Escape':
        event.preventDefault()
        close()
        break
      case 'f':
      case 'F':
        toggleFavorite()
        break
      case 'i':
      case 'I':
        toggleInfo()
        break
    }
  })

  useEffect(() => {
    const listener = (event: KeyboardEvent) => onKeyDown(event)
    window.addEventListener('keydown', listener)
    return () => window.removeEventListener('keydown', listener)
  }, [])

  // Warm the browser cache so ←/→ feel instant.
  useEffect(() => {
    for (const neighbour of [prev, next]) {
      if (neighbour?.previewUrl) new Image().src = neighbour.previewUrl
    }
  }, [prev, next])

  const src = current?.previewUrl ?? null
  const name = current === undefined ? 'Photo viewer' : `${current.fileName}${current.extension}`

  let stage
  if (notFound) {
    stage = (
      <Stack alignItems="center" spacing={2}>
        <Typography>This photo is no longer available.</Typography>
        <Button variant="contained" onClick={close}>
          Close
        </Button>
      </Stack>
    )
  } else if (current === undefined) {
    stage = <CircularProgress color="inherit" />
  } else if (src === null) {
    stage = <Typography sx={{ color: 'grey.400' }}>This photo hasn't been processed yet.</Typography>
  } else {
    stage = (
      <>
        {loadedSrc !== src && <CircularProgress color="inherit" sx={{ position: 'absolute' }} />}
        <img
          key={src}
          src={src}
          alt={name}
          onLoad={() => setLoadedSrc(src)}
          style={{ maxWidth: '100%', maxHeight: '100%', objectFit: 'contain' }}
        />
      </>
    )
  }

  return (
    <Dialog
      open
      fullScreen
      disableEscapeKeyDown
      slotProps={{ paper: { 'aria-label': name, sx: { bgcolor: 'common.black', color: 'common.white' } } }}
    >
      <Box sx={{ display: 'flex', height: '100%' }}>
        <Box
          sx={{
            position: 'relative',
            flex: 1,
            minWidth: 0,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          {stage}
          {canGoPrev && (
            <IconButton
              aria-label="Previous photo"
              onClick={goPrev}
              sx={{ position: 'absolute', left: 8, color: 'common.white' }}
            >
              <ChevronLeftIcon fontSize="large" />
            </IconButton>
          )}
          {canGoNext && (
            <IconButton
              aria-label="Next photo"
              onClick={goNext}
              sx={{ position: 'absolute', right: 8, color: 'common.white' }}
            >
              <ChevronRightIcon fontSize="large" />
            </IconButton>
          )}
          <Box sx={{ position: 'absolute', top: 8, right: 8, display: 'flex', gap: 1 }}>
            {current !== undefined && (
              <IconButton
                aria-label={current.isFavorite ? 'Remove from favorites' : 'Add to favorites'}
                aria-pressed={current.isFavorite}
                onClick={toggleFavorite}
                sx={{ color: 'warning.main' }}
              >
                {current.isFavorite ? <StarIcon /> : <StarBorderIcon />}
              </IconButton>
            )}
            <IconButton
              aria-label={infoOpen ? 'Hide info' : 'Show info'}
              onClick={toggleInfo}
              sx={{ color: 'common.white' }}
            >
              <InfoOutlinedIcon />
            </IconButton>
            <IconButton aria-label="Close viewer" onClick={close} sx={{ color: 'common.white' }}>
              <CloseIcon />
            </IconButton>
          </Box>
        </Box>
        {infoOpen && !notFound && (
          <InfoPanel
            detail={detail.data}
            isLoading={detail.isPending}
            isError={detail.isError}
            onRetry={() => void detail.refetch()}
          />
        )}
      </Box>
    </Dialog>
  )
}
```

In `web/src/views/ImageBrowser.tsx`:
- add the imports `import { PhotoViewer } from '../viewer/PhotoViewer'` and `parseGridParams` (next to `withParams`, from `'../routing/urlState'`);
- after `const [searchParams, setSearchParams] = useSearchParams()`, add:

```tsx
  const { image } = parseGridParams(searchParams)
```

- replace the final `return` with:

```tsx
  return (
    <>
      {header}
      {banner}
      {body}
      {image !== null && <PhotoViewer filter={filter} />}
    </>
  )
```

- [ ] **Step 4: Run the tests**

Run (from `web/`): `npm test`
Expected: PASS, including `format` and the 10 `PhotoViewer` tests, with every earlier suite still green.

- [ ] **Step 5: Run the gates**

Run (from `web/`): `npm run build`, `npm run lint`, `npm run format:check`
Expected: all succeed.

- [ ] **Step 6: Commit**

```bash
git add -A web/src
git commit -m "feat(web): add full-screen photo viewer with keyboard navigation and info panel

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Live verification (controller-performed, with a human browser pass)

This task needs a running API and dev server, and a person looking at a browser. The controller runs every check it can from the shell, prepares the environment, and hands the browser checklist to the user. Record every command and its result in the task report.

- [ ] **Step 1: Prepare**

1. Run `dotnet ef database update --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api`. Expected: up to date.
2. Start the API in the background, from `src/PictureManager.Api`: `ASPNETCORE_ENVIRONMENT=Development dotnet run`. It listens on `http://localhost:5080`.
3. Run a full scan: `curl -s -X POST http://localhost:5080/api/scans -H 'Content-Type: application/json' -d '{"isRecursive":true}'`. Then wait for `Completed` on `/api/scans/{id}/events`.
4. Start the dev server in the background, from `web/`: `npm run dev`. It listens on `http://localhost:5173`.

- [ ] **Step 2: Shell checks**

| # | Command | Expected |
|---|---|---|
| 1 | `curl -s http://localhost:5173/api/folders/roots` | the API's roots JSON, served through the Vite proxy |
| 2 | `curl -s "http://localhost:5173/api/images?fileName=IMG_0002"` | one item, with `"folderPath":"dev/Holidays/Madeira"` |
| 3 | `curl -s -o /dev/null -w "%{http_code}" "http://localhost:5173/api/images/<IMG_0002 id>/thumbnail?v=<hash>"` | `200` |
| 4 | `npm run build` (from `web/`) | succeeds |

- [ ] **Step 3: Browser checklist (the human)**

Hand this list to the user, open at `http://localhost:5173`, and record what they report:

1. `/` lands on the `dev` root, and the tree shows `Holidays` under it.
2. Deep link `/folders/<Madeira id>`: the tree opens down to Madeira, which is highlighted. The header shows `Madeira`, the path `dev / Holidays / Madeira` and `3 photos`.
3. Holidays shows its 2 own photos. The `dev` root shows the "No photos directly in this folder…" hint.
4. Click a photo: the viewer opens. ←/→ step through; Esc closes, and the grid keeps its scroll position. Open again, step twice, then press browser Back: the viewer closes.
5. `F` stars the photo, and its tile shows ★ after closing. `I` hides the info panel, which stays hidden after a reload with `?image=`.
6. Favorites lists the starred photo. Unstar it there: it stays, dimmed. Go to Folders and back to Favorites: it's gone.
7. Type `IMG` in search. The results appear after a short pause, with folder-path captions. Turn on "In this folder" from Madeira: only Madeira's photos. Remove the chip: all matches again.
8. Sort by Name, then toggle the direction: the order changes, and the grid starts at the top.
9. In a second terminal, rename `dev-data/images/Holidays/Madeira` to `Madeira2` and run a scan (as in Step 1.3). Reload: Madeira shows ⚠ in the tree and the missing banner, and `Madeira2` appears. Rename it back and scan again: the ⚠ is gone.
10. Switch the OS between dark and light mode: the app follows.

- [ ] **Step 4: Restore and record**

Stop both servers. Make sure `dev-data/images/Holidays/Madeira` has its original name, and unstar anything starred during the check. Record every result in the task report.
