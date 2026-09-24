# Phase 6b: Albums and Duplicates (SPA) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> Once approved, this file is saved to `docs/superpowers/plans/2026-09-24-phase6b-albums-duplicates.md` and executed from there, on a new branch `phase6b-albums-duplicates` off `master`.

## Context

Phase 6a shipped the browse SPA: folder tree, virtualized grid, favorites, search and viewer. The backend already has a complete album API and a read-only duplicates API, but the SPA has no UI for either.

The user wants albums for **curating and exporting**: an ordered, hand-picked selection whose path list goes to another tool. Duplicates are for **review only**. This plan implements the approved spec, which the user reviewed section by section:
- the Albums and Duplicates screens;
- multi-select in every grid, the album picker, and the viewer's A / Shift+A;
- drag-and-drop reordering (dnd-kit, non-virtualized album grid);
- edit, delete, remove and export;
- one backend change: `folderPath` on album photo items.

**Goal:** Albums (list, create, edit, delete, add from anywhere, reorder by drag and drop, remove, export) and a duplicates review view in the SPA.

**Architecture:**
- The work stays inside the 6a structure: TanStack Query hooks in `web/src/api/`, URL-as-state for view state, and one shared viewer.
- The viewer is refactored to take a list source instead of a filter, so folder, favorites, search, album and duplicates views can all feed it.
- Selection is a shared hook plus a bar. Adding goes through one picker dialog.
- The album grid is a plain, fully loaded CSS grid with dnd-kit. Every other grid stays virtualized.

**Tech Stack:**
- Frontend: React 19, TypeScript 6 (strict, `noUncheckedIndexedAccess`), MUI 9, react-router 8, TanStack Query 5, and new `@dnd-kit/core` 6.3, `@dnd-kit/sortable` 10, `@dnd-kit/utilities` 3.2. Tests with Vitest 5, Testing Library and MSW 2.
- Backend: .NET 10, xUnit, FluentAssertions, NSubstitute.

**Spec:** `docs/superpowers/specs/2026-09-24-phase6b-albums-duplicates-design.md`

## Global Constraints

- **Copy strings:** use these verbatim from the spec.
  - Empty and missing states:
    - "No albums yet. Create one, or select photos anywhere and choose Add to album."
    - "This album is empty. Select photos anywhere and choose Add to album."
    - "Album not found."
    - "No duplicates found."
    - "File missing"
  - Add-to-album errors:
    - "That album no longer exists."
    - "Couldn't add photos."
  - Album name conflict: "An album with this name already exists."
  - Reorder failure: "Couldn't save the new order."
  - Confirmations:
    - "Delete album {name}? The photos stay in your library."
    - "Remove {N photos} from {name}?"
  - Export warning: "{N photos are|1 photo is} missing on disk."
  - The add-result message format: "Added N photos to {Album}", with "(M were already there)" appended when M > 0.
- **Nav order:** Folders · Favorites · Albums · Duplicates.
- **`localStorage` keys:** `pm.albums.lastUsed`, `pm.albums.exportPrefix`, `pm.albums.showFolders`. Every access is wrapped in try/catch.
- **Selection** never goes in the URL. It resets when the grid's filter or album changes.
- **Reordering** moves one photo at a time. Dragging is disabled while any photo is selected. The album grid is not virtualized, and above 2,000 photos it shows "This album is large, so reordering may be slow."
- **Duplicates are read-only:** no delete, hide or merge.
- **Gates for every frontend task** (run from `web/`): `npm test`, `npm run build`, `npm run lint` (0 errors), and `npm run format:check`. Run `npx prettier --write src` before the format check.
- **Commits** end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Plan-level decisions (deviations from the spec's assumptions)

- **The API sorts albums by name, not by recent update.** The spec's statement "most recently updated first, as the API returns them" is wrong about the API. To keep the spec's intent, `useAlbums()` sorts on the client: `updatedAt` descending, then name. That order is used on the Albums page and in the picker.
- **Adding a folder:** the API adds only the folder's direct photos, as the spec says.
- **Deleting an album** only refreshes the album list. The deleted album's own cached queries are left to expire. Removing them while the album view is still mounted would refetch and flash "Album not found." before the navigation to `/albums`.
- **The album view waits for every page** before showing the grid (approach A), so reordering always works on the whole album.
- **The spec's `SortableAlbumGrid` is named `AlbumGrid`.** It becomes sortable in Task 10.

## Review Focus

These are the five failure modes most likely to bite a user that the feature tests wouldn't naturally hit. Each is pinned by the test named in brackets:

1. **Two quick drags before the first save returns.** Both moves must be saved in order, and the final order must match the second drag. [Task 3, `two quick moves are sent in order and both stick`]
2. **Adding a selection that includes missing-file photos** from an album. Only the available photos are sent, and the message counts the skipped ones. [Task 9, `adding a selection with a missing file skips it and says so`]
3. **Esc while a dialog is open over a selection.** It closes the dialog but keeps the selection. [Task 6, `Esc closes the picker but keeps the selection`]
4. **Ctrl+A while typing in the search box.** It must select the text, not the photos. [Task 6, `Ctrl+A in the search box does not select photos`]
5. **The last used album was deleted.** Shift+A opens the picker instead of failing, and the stale id is forgotten. [Task 7, `Shift+A with a deleted last album opens the picker and forgets it`]

## File structure

**Backend**
- Modify `src/PictureManager.Application/Albums/AlbumModels.cs`: `AlbumImageItem` gains `FolderPath`.

**Frontend, `web/src/`**
- **`api/`**
  - `types.ts`: album and duplicate types.
  - `client.ts`: `apiFetchText`.
  - `queries.ts`: album and duplicates query keys and hooks.
  - `favorites.ts`: `patchFavorite` also updates duplicate groups.
  - `albums.ts` (new): album mutations and `AddTarget`.
- **`albums/` (new)**
  - `preferences.ts`, `messages.ts`, `reorder.ts`, `errors.ts`, `exportFile.ts`, `useAlbumAdder.ts`;
  - `AlbumPicker.tsx`, `AlbumFormDialog.tsx`, `AlbumsPage.tsx`, `AlbumView.tsx`, `AlbumGrid.tsx`, `ExportDialog.tsx`.
- **`grid/`**
  - `useSelection.ts`, `SelectionBar.tsx` (new);
  - `PhotoTile.tsx` (selection, missing and drag activator);
  - `columns.ts` (`GRID_PADDING`), `PhotoGrid.tsx`.
- **`views/`**
  - `ImageBrowser.tsx` (selection and picker; the viewer takes a list);
  - `GridHeader.tsx` (`actions` slot), `FolderView.tsx` ("Add folder to album…");
  - `DuplicatesView.tsx` (new).
- **`viewer/PhotoViewer.tsx`:** list source, missing text, Add to album, A / Shift+A.
- **`shared/`**
  - `storage.ts`, `ConfirmDialog.tsx`, `useDebouncedValue.ts` (all new).
- **`app/`**
  - `notify.tsx` (optional action button), `AppShell.tsx` (nav), `routes.tsx`.
- **`test/`**
  - `fixtures.ts` (album seed, duplicate groups);
  - `albumHandlers.ts` (new: stateful fake album store and handlers, duplicates handler);
  - `handlers.ts`, `setup.ts` (reset the store before each test).

---

### Task 1: Backend — `folderPath` on album photo items

**Files:**
- Modify: `src/PictureManager.Application/Albums/AlbumModels.cs`
- Test: `tests/PictureManager.Application.Tests/Albums/AlbumServiceTests.cs`, `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AlbumQueryRepositoryTests.cs`

**Interfaces:**
- Consumes: `ImageRow.RootName` and `ImageRow.RelativePath` (already projected by `AlbumRepository.ListImagesAsync` since 6a), and `FolderDisplayPath.For(rootName, relativePath)`.
- Produces: JSON `folderPath` on every `GET /api/albums/{id}/images` item.

- [ ] **Step 1: Write the failing test.** In `AlbumServiceTests`, after `ListImagesAsync_MissingImage_HasNullUrls_AndPagingProducesCursor`:

```csharp
    [Fact]
    public async Task ListImagesAsync_ItemsCarryFolderPath_RootNameAloneForTheTopFolder()
    {
        var nested = new ImageRow(4, 3, "img4", ".jpg", null, null, null, false, "H", DateTime.UtcNow, "img4", "nas", "Holidays/Madeira");
        _albums.ListImagesAsync(7, null, null, 101, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new AlbumImageRow(Row(1), 0, false),
            new AlbumImageRow(nested, 1, false)
        });

        var page = (await CreateService().ListImagesAsync(7, null, null)).Value!;

        page.Items.Select(i => i.FolderPath).Should().Equal("nas", "nas/Holidays/Madeira");
    }
```

Then add this characterization test to `AlbumQueryRepositoryTests`, and add `using PictureManager.Application.Images;` to that file:

```csharp
    [Fact]
    public async Task ListImagesAsync_RowsCarryRootNameAndRelativePath()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("nas"), "Holidays/Madeira");
        var image = TestData.Image(folder, "a");
        var album = TestData.Album("A");
        db.Context.AlbumImages.Add(TestData.AlbumImage(album, image, 0));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new AlbumRepository(context).ListImagesAsync(album.Id, null, null, 10);

        rows.Should().ContainSingle().Which.Image.Should().Match<ImageRow>(r => r.RootName == "nas" && r.RelativePath == "Holidays/Madeira");
    }
```

- [ ] **Step 2: Run the tests.** `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~AlbumServiceTests"`.
  - Expected: a compile FAIL, because `AlbumImageItem` has no `FolderPath`.
  - The repository test is expected to PASS on its own, because the projection already exists since 6a. It pins what the DTO relies on.

- [ ] **Step 3: Implement.** In `AlbumModels.cs`, add `string FolderPath` as the last positional parameter of `AlbumImageItem`, after `bool IsMissing`. In `From`, pass it as the last argument:

```csharp
            row.IsMissing,
            FolderDisplayPath.For(image.RootName, image.RelativePath));
```

- [ ] **Step 4: Run the whole backend suite.** `dotnet test` from the repo root. Expected: all pass.

- [ ] **Step 5: Commit.**

```bash
git add src/PictureManager.Application/Albums/AlbumModels.cs tests/PictureManager.Application.Tests/Albums/AlbumServiceTests.cs tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AlbumQueryRepositoryTests.cs
git commit -m "feat: add folderPath to album image items"
```

---

### Task 2: Frontend foundations — types, queries, stateful fake API, notify actions

**Files:**
- Modify: `web/package.json` (via npm), `web/src/api/types.ts`, `web/src/api/client.ts`, `web/src/api/queries.ts`, `web/src/api/favorites.ts`, `web/src/app/notify.tsx`, `web/src/test/fixtures.ts`, `web/src/test/handlers.ts`, `web/src/test/setup.ts`
- Create: `web/src/shared/storage.ts`, `web/src/albums/preferences.ts`, `web/src/test/albumHandlers.ts`
- Test: `web/src/api/albumQueries.test.tsx`, `web/src/app/notify.test.tsx`

**Interfaces:**
- Produces:
  - **Types:** `AlbumSummary`, `AlbumDetail`, `AlbumImageItem`, `AlbumAddResult`, `DuplicateGroup`, `AlbumImagesData`.
  - **Client:** `apiFetchText(path, init?) → Promise<string>`.
  - **Query keys:** `queryKeys.albums()`, `.album(id)`, `.albumExport(id, prefix)`, `.albumImages(id)`, `.duplicates()`.
  - **Queries:** `albumsQuery` (queryOptions), `useAlbums()` (sorted by recent update), `useAlbum(id|null)`, `useAlbumImages(id|null)` (fetches every page), `useDuplicates()`.
  - **Notify:** `notify(message, action?: { label, onClick })`.
  - **Storage:** `readStored(key)`, `writeStored(key, value|null)`.
  - **Preferences:** `read/writeLastUsedAlbum`, `read/writeExportPrefix`, `read/writeShowFolders`.
  - **Test helpers:** `albumStore.get(id)`, `albumStore.byName(name)`, `resetAlbumStore()`, `albumHandlers`, `duplicateHandlers`, fixtures `albumSeed` and `duplicateGroups`.

- [ ] **Step 1: Install the drag library.** It's needed from Task 10, but installing now keeps one dependency commit. From `web/`: `npm install @dnd-kit/core@^6.3.1 @dnd-kit/sortable@^10.0.0 @dnd-kit/utilities@^3.2.2`.

- [ ] **Step 2: Add the types.** Append to `web/src/api/types.ts`:

```ts
export type AlbumSummary = {
  id: number
  name: string
  description: string | null
  imageCount: number
  coverThumbnailUrl: string | null
  updatedAt: string
}

export type AlbumDetail = {
  id: number
  name: string
  description: string | null
  imageCount: number
  createdAt: string
  updatedAt: string
}

/** Missing files stay listed, with null URLs. */
export type AlbumImageItem = ImageListItem & { isMissing: boolean }

export type AlbumAddResult = { added: number; skipped: number }

export type DuplicateGroup = { contentHash: string; count: number; images: ImageListItem[] }
```

- [ ] **Step 3: Add the fixtures and the stateful fake API.** Append to `web/src/test/fixtures.ts`:

```ts
export const albumSeed = [
  {
    id: 5,
    name: 'Best of 2025',
    description: 'Keepers',
    imageIds: [21, 20],
    updatedAt: '2026-09-20T10:00:00.000Z',
  },
  { id: 6, name: 'Empty', description: null, imageIds: [], updatedAt: '2026-09-10T10:00:00.000Z' },
]

export const duplicateGroups: DuplicateGroup[] = [
  { contentHash: 'H20', count: 2, images: [madeiraImages[0]!, holidaysImages[0]!] },
  { contentHash: 'H22', count: 2, images: [madeiraImages[2]!, holidaysImages[1]!] },
]
```

Add `DuplicateGroup` to that file's type import. Then create `web/src/test/albumHandlers.ts`:

```ts
import { http, HttpResponse, type PathParams } from 'msw'
import type { AlbumDetail, AlbumImageItem, AlbumSummary, ImageListItem } from '../api/types'
import { albumSeed, duplicateGroups, holidaysImages, madeiraImages } from './fixtures'

type StoredAlbum = {
  id: number
  name: string
  description: string | null
  imageIds: number[]
  missing: Set<number>
  createdAt: string
  updatedAt: string
}

let albums: StoredAlbum[] = []
let nextId = 100
let tick = 0

/** Called before every test (setup.ts), so each test starts from albumSeed. */
export function resetAlbumStore(): void {
  albums = albumSeed.map((album) => ({
    ...album,
    imageIds: [...album.imageIds],
    missing: new Set<number>(),
    createdAt: album.updatedAt,
  }))
  nextId = 100
  tick = 0
}
resetAlbumStore()

/** Lets tests inspect and adjust the fake server's albums. */
export const albumStore = {
  get: (id: number) => albums.find((album) => album.id === id),
  byName: (name: string) => albums.find((album) => album.name === name),
}

const library = (): ImageListItem[] => [...holidaysImages, ...madeiraImages]
const findImage = (id: number) => library().find((image) => image.id === id)
const notFound = () => HttpResponse.json({ title: 'Not Found', status: 404 }, { status: 404 })
const albumFor = (params: PathParams) => albumStore.get(Number(params.id))
/** Later than every seed date, and increasing, like a real clock. */
const now = () => new Date(Date.UTC(2026, 9, 1, 0, ++tick)).toISOString()

const summary = (album: StoredAlbum): AlbumSummary => {
  const first = album.imageIds[0]
  return {
    id: album.id,
    name: album.name,
    description: album.description,
    imageCount: album.imageIds.length,
    coverThumbnailUrl: first === undefined ? null : `/api/images/${first}/thumbnail?v=H${first}`,
    updatedAt: album.updatedAt,
  }
}

const detail = (album: StoredAlbum): AlbumDetail => ({
  id: album.id,
  name: album.name,
  description: album.description,
  imageCount: album.imageIds.length,
  createdAt: album.createdAt,
  updatedAt: album.updatedAt,
})

function albumItem(album: StoredAlbum, id: number): AlbumImageItem[] {
  const image = findImage(id)
  if (image === undefined) return []
  const missing = album.missing.has(id)
  return [
    {
      ...image,
      isMissing: missing,
      thumbnailUrl: missing ? null : image.thumbnailUrl,
      previewUrl: missing ? null : image.previewUrl,
    },
  ]
}

function nameProblem(name: string | null | undefined, exceptId?: number) {
  const trimmed = (name ?? '').trim()
  if (trimmed === '') {
    return HttpResponse.json(
      { title: 'One or more validation errors occurred.', status: 400, errors: { name: ['Name is required.'] } },
      { status: 400 },
    )
  }
  if (albums.some((a) => a.id !== exceptId && a.name.toLowerCase() === trimmed.toLowerCase())) {
    return HttpResponse.json(
      { title: 'Conflict', status: 409, detail: `An album named '${trimmed}' already exists.` },
      { status: 409 },
    )
  }
  return null
}

type NameBody = { name?: string | null; description?: string | null }

export const albumHandlers = [
  http.get('/api/albums', () => HttpResponse.json(albums.map(summary))),
  http.post('/api/albums', async ({ request }) => {
    const body = (await request.json()) as NameBody
    const problem = nameProblem(body.name)
    if (problem) return problem
    const created = now()
    const album: StoredAlbum = {
      id: nextId++,
      name: (body.name ?? '').trim(),
      description: body.description ?? null,
      imageIds: [],
      missing: new Set<number>(),
      createdAt: created,
      updatedAt: created,
    }
    albums.push(album)
    return HttpResponse.json(detail(album), { status: 201 })
  }),
  http.get('/api/albums/:id', ({ params }) => {
    const album = albumFor(params)
    return album ? HttpResponse.json(detail(album)) : notFound()
  }),
  http.patch('/api/albums/:id', async ({ params, request }) => {
    const album = albumFor(params)
    if (!album) return notFound()
    const body = (await request.json()) as NameBody
    if (body.name !== undefined) {
      const problem = nameProblem(body.name, album.id)
      if (problem) return problem
      album.name = (body.name ?? '').trim()
    }
    if (body.description !== undefined) album.description = body.description
    album.updatedAt = now()
    return HttpResponse.json(detail(album))
  }),
  http.delete('/api/albums/:id', ({ params }) => {
    const album = albumFor(params)
    if (!album) return notFound()
    albums = albums.filter((a) => a !== album)
    return new HttpResponse(null, { status: 204 })
  }),
  http.get('/api/albums/:id/images', ({ params }) => {
    const album = albumFor(params)
    if (!album) return notFound()
    return HttpResponse.json({
      items: album.imageIds.flatMap((id) => albumItem(album, id)),
      nextCursor: null,
    })
  }),
  http.post('/api/albums/:id/images', async ({ params, request }) => {
    const album = albumFor(params)
    if (!album) return notFound()
    const body = (await request.json()) as { imageIds?: number[]; folderId?: number }
    const candidates = [
      ...new Set(
        body.imageIds ??
          library()
            .filter((image) => image.folderId === body.folderId)
            .map((image) => image.id),
      ),
    ]
    const unknown = candidates.filter((id) => findImage(id) === undefined)
    if (unknown.length > 0) {
      return HttpResponse.json(
        {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: { imageIds: [`Unknown or unavailable image ids: ${unknown.join(', ')}.`] },
        },
        { status: 400 },
      )
    }
    const toAdd = candidates.filter((id) => !album.imageIds.includes(id))
    album.imageIds.push(...toAdd)
    if (toAdd.length > 0) album.updatedAt = now()
    return HttpResponse.json({ added: toAdd.length, skipped: candidates.length - toAdd.length })
  }),
  http.post('/api/albums/:id/images/remove', async ({ params, request }) => {
    const album = albumFor(params)
    if (!album) return notFound()
    const { imageIds } = (await request.json()) as { imageIds: number[] }
    album.imageIds = album.imageIds.filter((id) => !imageIds.includes(id))
    album.updatedAt = now()
    return new HttpResponse(null, { status: 204 })
  }),
  http.post('/api/albums/:id/images/:imageId/move', async ({ params, request }) => {
    const album = albumFor(params)
    if (!album) return notFound()
    const imageId = Number(params.imageId)
    const { afterImageId } = (await request.json()) as { afterImageId: number | null }
    if (!album.imageIds.includes(imageId)) {
      return HttpResponse.json({ title: 'Bad Request', status: 400 }, { status: 400 })
    }
    const order = album.imageIds.filter((id) => id !== imageId)
    order.splice(afterImageId === null ? 0 : order.indexOf(afterImageId) + 1, 0, imageId)
    album.imageIds = order
    album.updatedAt = now()
    return new HttpResponse(null, { status: 204 })
  }),
  http.get('/api/albums/:id/export', ({ params, request }) => {
    const album = albumFor(params)
    if (!album) return notFound()
    const prefix = (new URL(request.url).searchParams.get('prefix') ?? '').replace(/\/+$/, '')
    const text = album.imageIds
      .flatMap((id) => {
        const image = findImage(id)
        return image ? [`${prefix}/${image.folderPath}/${image.fileName}${image.extension}\n`] : []
      })
      .join('')
    return new HttpResponse(text, { headers: { 'Content-Type': 'text/plain; charset=utf-8' } })
  }),
]

/** Two pages of one group each, so tests can see "load more". */
export const duplicateHandlers = [
  http.get('/api/duplicates', ({ request }) =>
    new URL(request.url).searchParams.get('cursor') === null
      ? HttpResponse.json({ items: duplicateGroups.slice(0, 1), nextCursor: 'd1' })
      : HttpResponse.json({ items: duplicateGroups.slice(1), nextCursor: null }),
  ),
]
```

Wire it up:
- In `web/src/test/handlers.ts`, import `{ albumHandlers, duplicateHandlers } from './albumHandlers'` and append `...albumHandlers, ...duplicateHandlers` to the end of the `handlers` array.
- In `web/src/test/setup.ts`, import `beforeEach` from vitest and `resetAlbumStore` from `./albumHandlers`, and add `beforeEach(() => resetAlbumStore())`.

- [ ] **Step 4: Write the failing tests.** Create `web/src/api/albumQueries.test.tsx`:

```tsx
import type { InfiniteData } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { duplicateGroups, madeiraImages } from '../test/fixtures'
import { createTestQueryClient, createWrapper } from '../test/render'
import { server } from '../test/server'
import { patchFavorite } from './favorites'
import { queryKeys, useAlbumImages, useAlbums, useDuplicates } from './queries'
import type { DuplicateGroup, Page } from './types'

function setup() {
  const queryClient = createTestQueryClient()
  return { queryClient, wrapper: createWrapper(queryClient) }
}

describe('album and duplicate queries', () => {
  it('lists albums most recently updated first', async () => {
    server.use(
      http.get('/api/albums', () =>
        HttpResponse.json([
          { id: 1, name: 'Old', description: null, imageCount: 0, coverThumbnailUrl: null, updatedAt: '2026-01-01T00:00:00Z' },
          { id: 2, name: 'New', description: null, imageCount: 0, coverThumbnailUrl: null, updatedAt: '2026-05-01T00:00:00Z' },
        ]),
      ),
    )
    const { result } = renderHook(() => useAlbums(), { wrapper: setup().wrapper })
    await waitFor(() => expect(result.current.data?.map((a) => a.name)).toEqual(['New', 'Old']))
  })

  it('loads every page of an album', async () => {
    server.use(
      http.get('/api/albums/:id/images', ({ request }) =>
        new URL(request.url).searchParams.get('cursor') === null
          ? HttpResponse.json({ items: [{ ...madeiraImages[0]!, isMissing: false }], nextCursor: 'c1' })
          : HttpResponse.json({ items: [{ ...madeiraImages[1]!, isMissing: false }], nextCursor: null }),
      ),
    )
    const { result } = renderHook(() => useAlbumImages(5), { wrapper: setup().wrapper })
    await waitFor(() => expect(result.current.hasNextPage).toBe(false))
    expect(result.current.data?.pages.flatMap((p) => p.items.map((i) => i.id))).toEqual([20, 21])
  })

  it('loads duplicate groups page by page', async () => {
    const { result } = renderHook(() => useDuplicates(), { wrapper: setup().wrapper })
    await waitFor(() => expect(result.current.data?.pages).toHaveLength(1))
    expect(result.current.hasNextPage).toBe(true)
  })

  it('a star change also updates copies inside cached duplicate groups', () => {
    const { queryClient } = setup()
    queryClient.setQueryData<InfiniteData<Page<DuplicateGroup>, string | null>>(queryKeys.duplicates(), {
      pages: [{ items: duplicateGroups, nextCursor: null }],
      pageParams: [null],
    })
    patchFavorite(queryClient, 20, true)
    const data = queryClient.getQueryData<InfiniteData<Page<DuplicateGroup>>>(queryKeys.duplicates())
    expect(data?.pages[0]?.items[0]?.images.find((i) => i.id === 20)?.isFavorite).toBe(true)
  })
})
```

Create `web/src/app/notify.test.tsx`:

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { NotifyProvider, useNotify } from './notify'

function Trigger({ onAction }: { onAction: () => void }) {
  const notify = useNotify()
  return <button onClick={() => notify('Saved.', { label: 'Open', onClick: onAction })}>Go</button>
}

describe('notify', () => {
  it('shows an action button that runs and dismisses the message', async () => {
    const onAction = vi.fn()
    render(
      <NotifyProvider>
        <Trigger onAction={onAction} />
      </NotifyProvider>,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Go' }))
    expect(await screen.findByText('Saved.')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Open' }))
    expect(onAction).toHaveBeenCalledOnce()
    await waitFor(() => expect(screen.queryByText('Saved.')).not.toBeInTheDocument())
  })
})
```

- [ ] **Step 5: Run the tests.** From `web/`: `npx vitest run src/api/albumQueries.test.tsx src/app/notify.test.tsx`. Expected: FAIL, because `useAlbums`, `useAlbumImages`, `useDuplicates` and `queryKeys.duplicates` don't exist and `notify` takes no action.

- [ ] **Step 6: Implement.** In `web/src/api/client.ts`, append:

```ts
/** Like apiFetch, for plain-text responses (album export). */
export async function apiFetchText(path: string, init?: RequestInit): Promise<string> {
  const response = await fetch(new URL(path, window.location.origin), {
    ...init,
    headers: { Accept: 'text/plain', ...init?.headers },
  })
  if (!response.ok) {
    throw new ApiError(response.status, await readProblem(response))
  }
  return response.text()
}
```

In `web/src/api/queries.ts`:
- change the imports to `import { queryOptions, useInfiniteQuery, useQuery, type InfiniteData } from '@tanstack/react-query'` and `import { useEffect } from 'react'`;
- add `AlbumDetail, AlbumImageItem, AlbumSummary, DuplicateGroup` to the type import;
- extend `queryKeys` and append the hooks:

```ts
  albums: () => ['albums', 'list'] as const,
  album: (id: number) => ['albums', id, 'detail'] as const,
  albumExport: (id: number, prefix: string) => ['albums', id, 'export', prefix] as const,
  // Under the photo-list prefix, so patchFavorite updates album tiles too.
  albumImages: (id: number) => ['images', 'list', { kind: 'album', albumId: id }] as const,
  duplicates: () => ['duplicates'] as const,
```

```ts
const ALBUM_PAGE_SIZE = 200
const DUPLICATES_PAGE_SIZE = 50

export type AlbumImagesData = InfiniteData<Page<AlbumImageItem>, string | null>

function pageQuery(cursor: string | null, limit: number): URLSearchParams {
  const params = new URLSearchParams({ limit: String(limit) })
  if (cursor !== null) params.set('cursor', cursor)
  return params
}

/** The API sorts by name; the picker and the Albums page want the album being filled on top. */
function byRecentlyUpdated(albums: AlbumSummary[]): AlbumSummary[] {
  return [...albums].sort(
    (a, b) => b.updatedAt.localeCompare(a.updatedAt) || a.name.localeCompare(b.name),
  )
}

export const albumsQuery = queryOptions({
  queryKey: queryKeys.albums(),
  queryFn: ({ signal }) => apiFetch<AlbumSummary[]>('/api/albums', { signal }),
})

export function useAlbums() {
  return useQuery({ ...albumsQuery, select: byRecentlyUpdated })
}

export function useAlbum(id: number | null) {
  return useQuery({
    queryKey: queryKeys.album(id ?? 0),
    queryFn: ({ signal }) => apiFetch<AlbumDetail>(`/api/albums/${id}`, { signal }),
    enabled: id !== null,
  })
}

/** The whole album: keeps fetching pages until none are left, so it can be reordered as one list. */
export function useAlbumImages(id: number | null) {
  const query = useInfiniteQuery({
    queryKey: queryKeys.albumImages(id ?? 0),
    queryFn: ({ pageParam, signal }) =>
      apiFetch<Page<AlbumImageItem>>(
        `/api/albums/${id}/images?${pageQuery(pageParam, ALBUM_PAGE_SIZE)}`,
        { signal },
      ),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage: Page<AlbumImageItem>) => lastPage.nextCursor,
    enabled: id !== null,
  })
  const { hasNextPage, isFetchingNextPage, isError, fetchNextPage } = query
  useEffect(() => {
    if (hasNextPage && !isFetchingNextPage && !isError) void fetchNextPage()
  }, [hasNextPage, isFetchingNextPage, isError, fetchNextPage])
  return query
}

export function useDuplicates() {
  return useInfiniteQuery({
    queryKey: queryKeys.duplicates(),
    queryFn: ({ pageParam, signal }) =>
      apiFetch<Page<DuplicateGroup>>(
        `/api/duplicates?${pageQuery(pageParam, DUPLICATES_PAGE_SIZE)}`,
        { signal },
      ),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage: Page<DuplicateGroup>) => lastPage.nextCursor,
  })
}
```

In `web/src/api/favorites.ts`, append to the body of `patchFavorite`, and add `DuplicateGroup` to the type import:

```ts
  queryClient.setQueriesData<InfiniteData<Page<DuplicateGroup>, string | null>>(
    { queryKey: queryKeys.duplicates() },
    (data) =>
      data === undefined
        ? data
        : {
            ...data,
            pages: data.pages.map((page) => ({
              ...page,
              items: page.items.map((group) => ({
                ...group,
                images: group.images.map((item) => (item.id === id ? { ...item, isFavorite } : item)),
              })),
            })),
          },
  )
```

Replace `web/src/app/notify.tsx` with:

```tsx
import { Button, Snackbar } from '@mui/material'
import { createContext, useCallback, useContext, useState, type ReactNode } from 'react'

export type NotifyAction = { label: string; onClick: () => void }
type Notify = (message: string, action?: NotifyAction) => void
type Message = { text: string; action: NotifyAction | undefined }

const NotifyContext = createContext<Notify | null>(null)

/** One app-wide snackbar for short, transient messages, optionally with one action. */
export function NotifyProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState<Message | null>(null)
  const notify = useCallback<Notify>((text, action) => setMessage({ text, action }), [])
  const action = message?.action

  return (
    <NotifyContext.Provider value={notify}>
      {children}
      <Snackbar
        open={message !== null}
        message={message?.text ?? ''}
        autoHideDuration={4000}
        onClose={() => setMessage(null)}
        action={
          action && (
            <Button
              color="inherit"
              size="small"
              onClick={() => {
                setMessage(null)
                action.onClick()
              }}
            >
              {action.label}
            </Button>
          )
        }
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

Create `web/src/shared/storage.ts`:

```ts
/** localStorage can be missing or throw (private mode, blocked site data); treat that as "not stored". */
export function readStored(key: string): string | null {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

export function writeStored(key: string, value: string | null): void {
  try {
    if (value === null) localStorage.removeItem(key)
    else localStorage.setItem(key, value)
  } catch {
    // Storage unavailable: the preference just isn't remembered.
  }
}
```

Create `web/src/albums/preferences.ts`:

```ts
import { parseId } from '../routing/urlState'
import { readStored, writeStored } from '../shared/storage'

const LAST_USED = 'pm.albums.lastUsed'
const EXPORT_PREFIX = 'pm.albums.exportPrefix'
const SHOW_FOLDERS = 'pm.albums.showFolders'

export const readLastUsedAlbum = (): number | null => parseId(readStored(LAST_USED))
export const writeLastUsedAlbum = (id: number | null): void =>
  writeStored(LAST_USED, id === null ? null : String(id))

export const readExportPrefix = (): string => readStored(EXPORT_PREFIX) ?? ''
export const writeExportPrefix = (prefix: string): void =>
  writeStored(EXPORT_PREFIX, prefix === '' ? null : prefix)

export const readShowFolders = (): boolean => readStored(SHOW_FOLDERS) === 'true'
export const writeShowFolders = (show: boolean): void => writeStored(SHOW_FOLDERS, String(show))
```

- [ ] **Step 7: Run the gates.** From `web/`, run `npm test` (all pass, including every 6a suite), then `npm run build`, `npm run lint` and `npm run format:check`.

- [ ] **Step 8: Commit.**

```bash
git add web/package.json web/package-lock.json web/src
git commit -m "feat(web): add album/duplicate queries, notify actions and a stateful fake album API"
```

---

### Task 3: Album mutations, reorder planning and messages

**Files:**
- Create: `web/src/api/albums.ts`, `web/src/albums/reorder.ts`, `web/src/albums/messages.ts`
- Test: `web/src/api/albums.test.tsx`, `web/src/albums/reorder.test.ts`, `web/src/albums/messages.test.ts`

**Interfaces:**
- Consumes: from Task 2, `apiFetch`, `queryKeys`, `AlbumImagesData`, `useNotify`, and the album types.
- Produces:
  - **Types:** `AddTarget = { imageIds: number[] } | { folderId: number }`, `AlbumInput = { name: string; description: string | null }`.
  - **Mutation hooks:**
    - `useCreateAlbum()` → `mutateAsync(AlbumInput): Promise<AlbumDetail>`;
    - `useUpdateAlbum(id)` → `mutateAsync(AlbumInput): Promise<AlbumDetail>`;
    - `useDeleteAlbum(id)` → `mutateAsync(): Promise<void>`;
    - `useAddToAlbum()` → `mutateAsync({ albumId, target }): Promise<AlbumAddResult>`;
    - `useRemoveFromAlbum(albumId)` → `mutateAsync({ imageIds })`;
    - `useMoveInAlbum(albumId)` → `mutate({ imageId, afterImageId, order })`.
  - **Pure helpers:**
    - `planMove(ids, activeId, overId) → { order: number[]; afterImageId: number | null } | null`;
    - `reorderPages(data, order)`;
    - `photoCount(n)`;
    - `addedMessage(result, albumName, unavailable = 0)`.

- [ ] **Step 1: Write the failing tests.** Create `web/src/albums/reorder.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { planMove, reorderPages } from './reorder'

describe('planMove', () => {
  it.each([
    [1, 3, [2, 3, 1, 4], 3],
    [4, 1, [4, 1, 2, 3], null],
    [2, 4, [1, 3, 4, 2], 4],
  ])('moving %i onto %i gives %j after %s', (active, over, order, after) =>
    expect(planMove([1, 2, 3, 4], active, over)).toEqual({ order, afterImageId: after }))

  it('is a no-op onto itself or an unknown id', () => {
    expect(planMove([1, 2], 1, 1)).toBeNull()
    expect(planMove([1, 2], 1, 9)).toBeNull()
  })
})

describe('reorderPages', () => {
  it('keeps the page sizes and page params', () => {
    const data = {
      pages: [
        { items: [{ id: 1 }, { id: 2 }], nextCursor: 'c' },
        { items: [{ id: 3 }], nextCursor: null },
      ],
      pageParams: [null, 'c'],
    }
    const result = reorderPages(data, [3, 1, 2])
    expect(result.pages.map((p) => p.items.map((i) => i.id))).toEqual([[3, 1], [2]])
    expect(result.pageParams).toEqual([null, 'c'])
  })
})
```

Create `web/src/albums/messages.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { addedMessage, photoCount } from './messages'

describe('messages', () => {
  it('counts photos', () => {
    expect(photoCount(1)).toBe('1 photo')
    expect(photoCount(3)).toBe('3 photos')
  })

  it('says what was added and what was already there or missing', () => {
    expect(addedMessage({ added: 5, skipped: 0 }, 'Trip')).toBe('Added 5 photos to Trip.')
    expect(addedMessage({ added: 1, skipped: 2 }, 'Trip')).toBe(
      'Added 1 photo to Trip (2 were already there).',
    )
    expect(addedMessage({ added: 2, skipped: 1 }, 'Trip', 1)).toBe(
      'Added 2 photos to Trip (1 was already there; 1 missing on disk was skipped).',
    )
  })
})
```

Create `web/src/api/albums.test.tsx`:

```tsx
import { act, renderHook, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { imageDetail, madeiraImages } from '../test/fixtures'
import { createTestQueryClient, createWrapper } from '../test/render'
import { server } from '../test/server'
import { useAddToAlbum, useMoveInAlbum } from './albums'
import { queryKeys, useAlbumImages, useAlbums, type AlbumImagesData } from './queries'

function setup() {
  const queryClient = createTestQueryClient()
  return { queryClient, wrapper: createWrapper(queryClient) }
}

const ids = (data: AlbumImagesData | undefined) => data?.pages.flatMap((p) => p.items.map((i) => i.id))

describe('album mutations', () => {
  it("adding photos refreshes the album list and the photos' details", async () => {
    const { queryClient, wrapper } = setup()
    const list = renderHook(() => useAlbums(), { wrapper })
    await waitFor(() => expect(list.result.current.data).toBeDefined())
    queryClient.setQueryData(queryKeys.image(22), imageDetail(madeiraImages[2]!))
    const { result } = renderHook(() => useAddToAlbum(), { wrapper })

    await act(() => result.current.mutateAsync({ albumId: 6, target: { imageIds: [22] } }))

    await waitFor(() =>
      expect(list.result.current.data?.find((a) => a.id === 6)?.imageCount).toBe(1),
    )
    expect(queryClient.getQueryState(queryKeys.image(22))?.isInvalidated).toBe(true)
  })

  it('moves optimistically and keeps the order when the server agrees', async () => {
    const { queryClient, wrapper } = setup()
    const { result } = renderHook(() => ({ images: useAlbumImages(5), move: useMoveInAlbum(5) }), {
      wrapper,
    })
    await waitFor(() => expect(ids(result.current.images.data)).toEqual([21, 20]))

    act(() => result.current.move.mutate({ imageId: 20, afterImageId: null, order: [20, 21] }))

    await waitFor(() => expect(ids(queryClient.getQueryData(queryKeys.albumImages(5)))).toEqual([20, 21]))
    await waitFor(() => expect(result.current.move.isSuccess).toBe(true))
    expect(albumStore.get(5)!.imageIds).toEqual([20, 21])
  })

  it('puts the order back and says so when saving fails', async () => {
    server.use(
      http.post('/api/albums/:id/images/:imageId/move', () =>
        HttpResponse.json({ title: 'boom' }, { status: 500 }),
      ),
    )
    const { queryClient, wrapper } = setup()
    const { result } = renderHook(() => ({ images: useAlbumImages(5), move: useMoveInAlbum(5) }), {
      wrapper,
    })
    await waitFor(() => expect(ids(result.current.images.data)).toEqual([21, 20]))

    act(() => result.current.move.mutate({ imageId: 20, afterImageId: null, order: [20, 21] }))

    await waitFor(() => expect(result.current.move.isError).toBe(true))
    expect(ids(queryClient.getQueryData(queryKeys.albumImages(5)))).toEqual([21, 20])
    expect(await screen.findByText("Couldn't save the new order.")).toBeInTheDocument()
  })

  it('two quick moves are sent in order and both stick', async () => {
    albumStore.get(5)!.imageIds = [20, 21, 22]
    const { queryClient, wrapper } = setup()
    const { result } = renderHook(() => ({ images: useAlbumImages(5), move: useMoveInAlbum(5) }), {
      wrapper,
    })
    await waitFor(() => expect(ids(result.current.images.data)).toEqual([20, 21, 22]))

    act(() => {
      result.current.move.mutate({ imageId: 22, afterImageId: null, order: [22, 20, 21] })
      result.current.move.mutate({ imageId: 21, afterImageId: 22, order: [22, 21, 20] })
    })

    await waitFor(() => expect(queryClient.isMutating()).toBe(0))
    expect(albumStore.get(5)!.imageIds).toEqual([22, 21, 20])
    expect(ids(queryClient.getQueryData(queryKeys.albumImages(5)))).toEqual([22, 21, 20])
  })
})
```

- [ ] **Step 2: Run the tests.** `npx vitest run src/albums src/api/albums.test.tsx`. Expected: FAIL, because the modules don't exist.

- [ ] **Step 3: Implement.** Create `web/src/albums/reorder.ts`:

```ts
import type { InfiniteData } from '@tanstack/react-query'
import type { Page } from '../api/types'

/** Where `activeId` lands when dropped on `overId`, and the API's "move after" anchor (null = front). */
export function planMove(
  ids: readonly number[],
  activeId: number,
  overId: number,
): { order: number[]; afterImageId: number | null } | null {
  const from = ids.indexOf(activeId)
  const to = ids.indexOf(overId)
  if (from < 0 || to < 0 || from === to) return null
  const order = [...ids]
  order.splice(from, 1)
  order.splice(to, 0, activeId)
  const at = order.indexOf(activeId)
  return { order, afterImageId: at === 0 ? null : (order[at - 1] ?? null) }
}

/** Puts the cached items in `order`, keeping each page's size so page params stay aligned. */
export function reorderPages<T extends { id: number }>(
  data: InfiniteData<Page<T>, string | null>,
  order: readonly number[],
): InfiniteData<Page<T>, string | null> {
  const byId = new Map(data.pages.flatMap((page) => page.items).map((item) => [item.id, item]))
  const sorted = order.flatMap((id) => {
    const item = byId.get(id)
    return item === undefined ? [] : [item]
  })
  let offset = 0
  return {
    ...data,
    pages: data.pages.map((page) => {
      const items = sorted.slice(offset, offset + page.items.length)
      offset += page.items.length
      return { ...page, items }
    }),
  }
}
```

Create `web/src/albums/messages.ts`:

```ts
import type { AlbumAddResult } from '../api/types'

export const photoCount = (n: number): string => (n === 1 ? '1 photo' : `${n} photos`)

const were = (n: number) => (n === 1 ? 'was' : 'were')

export function addedMessage(result: AlbumAddResult, albumName: string, unavailable = 0): string {
  const notes: string[] = []
  if (result.skipped > 0) notes.push(`${result.skipped} ${were(result.skipped)} already there`)
  if (unavailable > 0) notes.push(`${unavailable} missing on disk ${were(unavailable)} skipped`)
  const suffix = notes.length > 0 ? ` (${notes.join('; ')})` : ''
  return `Added ${photoCount(result.added)} to ${albumName}${suffix}.`
}
```

Create `web/src/api/albums.ts`:

```ts
import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { useNotify } from '../app/notify'
import { reorderPages } from '../albums/reorder'
import { apiFetch } from './client'
import { queryKeys, type AlbumImagesData } from './queries'
import type { AlbumAddResult, AlbumDetail } from './types'

export type AddTarget = { imageIds: number[] } | { folderId: number }
export type AlbumInput = { name: string; description: string | null }
type MoveVars = { imageId: number; afterImageId: number | null; order: number[] }

const jsonRequest = (method: string, body: unknown): RequestInit => ({
  method,
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
})

/** After adding or removing: counts, covers and dates, the album's photos, and "in albums" details. */
function refreshAlbumContents(queryClient: QueryClient, albumId: number) {
  return Promise.all([
    queryClient.invalidateQueries({ queryKey: ['albums'] }),
    queryClient.invalidateQueries({ queryKey: queryKeys.albumImages(albumId) }),
    queryClient.invalidateQueries({ queryKey: ['images', 'detail'] }),
  ])
}

export function useCreateAlbum() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: AlbumInput) => apiFetch<AlbumDetail>('/api/albums', jsonRequest('POST', input)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.albums() }),
  })
}

export function useUpdateAlbum(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: AlbumInput) =>
      apiFetch<AlbumDetail>(`/api/albums/${id}`, jsonRequest('PATCH', input)),
    onSuccess: (album) => {
      queryClient.setQueryData(queryKeys.album(id), album)
      return queryClient.invalidateQueries({ queryKey: queryKeys.albums() })
    },
  })
}

export function useDeleteAlbum(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => apiFetch<void>(`/api/albums/${id}`, { method: 'DELETE' }),
    // The album's own queries are left to expire: removing them while its view is still mounted
    // would refetch and flash "Album not found." before the navigation away.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.albums() }),
  })
}

export function useAddToAlbum() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ albumId, target }: { albumId: number; target: AddTarget }) =>
      apiFetch<AlbumAddResult>(`/api/albums/${albumId}/images`, jsonRequest('POST', target)),
    onSuccess: (_result, { albumId }) => refreshAlbumContents(queryClient, albumId),
  })
}

export function useRemoveFromAlbum(albumId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ imageIds }: { imageIds: number[] }) =>
      apiFetch<void>(`/api/albums/${albumId}/images/remove`, jsonRequest('POST', { imageIds })),
    onSuccess: () => refreshAlbumContents(queryClient, albumId),
  })
}

export function useMoveInAlbum(albumId: number) {
  const queryClient = useQueryClient()
  const notify = useNotify()
  return useMutation({
    // One queue per album: each move is computed against the order the previous one left.
    scope: { id: `album-move-${albumId}` },
    mutationFn: ({ imageId, afterImageId }: MoveVars) =>
      apiFetch<void>(
        `/api/albums/${albumId}/images/${imageId}/move`,
        jsonRequest('POST', { afterImageId }),
      ),
    onMutate: async ({ order }: MoveVars) => {
      const key = queryKeys.albumImages(albumId)
      await queryClient.cancelQueries({ queryKey: key })
      const previous = queryClient.getQueryData<AlbumImagesData>(key)
      if (previous !== undefined) {
        queryClient.setQueryData<AlbumImagesData>(key, reorderPages(previous, order))
      }
      return { previous }
    },
    onError: (_error, _vars, context) => {
      if (context?.previous !== undefined) {
        queryClient.setQueryData(queryKeys.albumImages(albumId), context.previous)
      }
      notify("Couldn't save the new order.")
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.albums() }),
  })
}
```

- [ ] **Step 4: Run the gates.** Expected: all pass.

- [ ] **Step 5: Commit.** `git add web/src && git commit -m "feat(web): add album mutations with optimistic reorder"`

---

### Task 4: The viewer takes a list source

This is a refactor with no behavior change. The viewer no longer owns a `useImages` observer: the view that shows it passes its list. This lets the album and duplicates views use the same viewer.

**Files:**
- Modify: `web/src/viewer/PhotoViewer.tsx`, `web/src/views/ImageBrowser.tsx`
- Test: `web/src/viewer/PhotoViewer.test.tsx`

**Interfaces:**
- Produces: `export type ViewerList = { items: readonly ViewerItem[]; hasNextPage: boolean; fetchNextPage: () => unknown }`, where `ViewerItem = ImageListItem & { isMissing?: boolean }`, and `PhotoViewer({ list }: { list: ViewerList })`.

- [ ] **Step 1: Change the test first.** In `PhotoViewer.test.tsx`, the test `stepping past the last loaded photo fetches the next page` renders `<PhotoViewer filter={filter} />`. Replace that render line with a harness that owns the list, the way `ImageBrowser` will:

```tsx
function ViewerHarness({ filter }: { filter: ImageFilter }) {
  const images = useImages(filter)
  const items = useMemo(() => images.data?.pages.flatMap((page) => page.items) ?? [], [images.data])
  return (
    <PhotoViewer
      list={{
        items,
        hasNextPage: images.hasNextPage,
        fetchNextPage: () => images.fetchNextPage({ cancelRefetch: false }),
      }}
    />
  )
}
```

The render becomes `renderRoutes([{ path: '/', element: <ViewerHarness filter={filter} /> }], '/?image=21')`. Add the imports `useMemo` from `react` and `useImages` from `../api/queries`. Also add one new test for the missing-file stage:

```tsx
  it('says when a photo in the list has no file on disk', async () => {
    const items = [{ ...madeiraImages[0]!, isMissing: true, previewUrl: null, thumbnailUrl: null }]
    renderRoutes(
      [{ path: '/', element: <PhotoViewer list={{ items, hasNextPage: false, fetchNextPage: () => undefined }} /> }],
      '/?image=20',
    )
    expect(await screen.findByText('The file for this photo is missing on disk.')).toBeInTheDocument()
  })
```

- [ ] **Step 2: Run the tests.** `npx vitest run src/viewer`. Expected: FAIL (compile), because `PhotoViewer` has no `list` prop.

- [ ] **Step 3: Implement.** In `PhotoViewer.tsx`:
- remove the imports of `ImageFilter` and `useImages` (keep `useImage`), and import `type ImageListItem` from `../api/types`;
- add and export these types:

```ts
export type ViewerItem = ImageListItem & { isMissing?: boolean }
/** What the viewer steps through: the grid's loaded items, and how to load more. */
export type ViewerList = {
  items: readonly ViewerItem[]
  hasNextPage: boolean
  fetchNextPage: () => unknown
}

const isMissingItem = (item: object): boolean => 'isMissing' in item && item.isMissing === true
```

- change the signature to `export function PhotoViewer({ list }: { list: ViewerList })`;
- delete the `// No refetch on mount…` comment and the `const list = useImages(filter, { refetchOnMount: false })` line;
- replace the `const items = useMemo(...)` line with `const items = list.items`, and drop `useMemo` from the react import if it's now unused;
- in `canGoNext` and `goNext`, `list.hasNextPage` stays as written. Replace `void list.fetchNextPage({ cancelRefetch: false })` with `void list.fetchNextPage()`;
- in the `stage` chain, add a branch right after `current === undefined`:

```tsx
  } else if (isMissingItem(current)) {
    stage = (
      <Typography sx={{ color: 'grey.400' }}>The file for this photo is missing on disk.</Typography>
    )
```

In `ImageBrowser.tsx`, replace `<PhotoViewer filter={filter} />` with:

```tsx
<PhotoViewer
  list={{
    items,
    hasNextPage: images.hasNextPage,
    fetchNextPage: () => images.fetchNextPage({ cancelRefetch: false }),
  }}
/>
```

- [ ] **Step 4: Run the gates.** Expected: all pass. All 6a viewer tests pass unchanged apart from the harness swap.

- [ ] **Step 5: Commit.** `git add web/src && git commit -m "refactor(web): viewer steps through a list passed by its view"`

---

### Task 5: Album picker

**Files:**
- Create: `web/src/albums/errors.ts`, `web/src/albums/useAlbumAdder.ts`, `web/src/albums/AlbumPicker.tsx`
- Test: `web/src/albums/AlbumPicker.test.tsx`

**Interfaces:**
- Consumes: from Task 3, `useAddToAlbum`, `useCreateAlbum`, `AddTarget`, `addedMessage` and `photoCount`; from Task 2, `useAlbums`, the preferences and `notify` with an action.
- Produces:
  - `albumFormErrors(error) → { name?: string; description?: string; form?: string }`;
  - `useAlbumAdder() → { addTo(album: { id; name }, target, unavailable?) : Promise<'added' | 'gone' | 'failed'>; isAdding }`;
  - `AlbumPicker({ target, unavailable?, onClose, onAdded? })`, a dialog titled "Add to album".

- [ ] **Step 1: Write the failing tests.** Create `web/src/albums/AlbumPicker.test.tsx`:

```tsx
import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import type { AddTarget } from '../api/albums'
import { albumStore } from '../test/albumHandlers'
import { renderRoutes } from '../test/render'
import { server } from '../test/server'
import { AlbumPicker } from './AlbumPicker'

function Harness({ target }: { target: AddTarget }) {
  const [open, setOpen] = useState(true)
  return open ? <AlbumPicker target={target} onClose={() => setOpen(false)} /> : <p>closed</p>
}

function renderPicker(target: AddTarget) {
  return renderRoutes(
    [
      { path: '/', element: <Harness target={target} /> },
      { path: '/albums/:albumId', element: <p>Album page</p> },
    ],
    '/',
  )
}

const albumButtons = async () =>
  within(await screen.findByRole('list', { name: 'Albums' })).getAllByRole('button')

describe('AlbumPicker', () => {
  it('lists albums recently updated first, and the filter narrows them', async () => {
    const { user } = renderPicker({ imageIds: [22] })
    expect((await albumButtons()).map((b) => b.textContent)).toEqual([
      'Best of 20252 photos',
      'Empty0 photos',
    ])
    await user.type(screen.getByRole('textbox', { name: 'Filter albums' }), 'emp')
    expect((await albumButtons()).map((b) => b.textContent)).toEqual(['Empty0 photos'])
  })

  it('adds to an album, reports what was already there, and remembers it', async () => {
    const { user, router } = renderPicker({ imageIds: [20, 22] })
    await user.click((await albumButtons())[0]!)
    expect(
      await screen.findByText('Added 1 photo to Best of 2025 (1 was already there).'),
    ).toBeInTheDocument()
    expect(screen.getByText('closed')).toBeInTheDocument()
    expect(albumStore.get(5)!.imageIds).toEqual([21, 20, 22])
    expect(localStorage.getItem('pm.albums.lastUsed')).toBe('5')
    await user.click(screen.getByRole('button', { name: 'Open album' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/albums/5'))
  })

  it('creates an album and adds to it in one step', async () => {
    const { user } = renderPicker({ imageIds: [20, 22] })
    await user.click(await screen.findByRole('button', { name: 'New album' }))
    await user.type(screen.getByRole('textbox', { name: 'New album name' }), 'Trip')
    await user.click(screen.getByRole('button', { name: 'Create and add' }))
    expect(await screen.findByText('Added 2 photos to Trip.')).toBeInTheDocument()
    expect(albumStore.byName('Trip')!.imageIds).toEqual([20, 22])
  })

  it('says when a new album name is taken', async () => {
    const { user } = renderPicker({ imageIds: [20] })
    await user.click(await screen.findByRole('button', { name: 'New album' }))
    await user.type(screen.getByRole('textbox', { name: 'New album name' }), 'best of 2025')
    await user.click(screen.getByRole('button', { name: 'Create and add' }))
    expect(await screen.findByText('An album with this name already exists.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Add to album' })).toBeInTheDocument()
  })

  it('says when the album no longer exists, and stays open', async () => {
    server.use(
      http.post('/api/albums/:id/images', () =>
        HttpResponse.json({ title: 'Not Found', status: 404 }, { status: 404 }),
      ),
    )
    const { user } = renderPicker({ imageIds: [20] })
    await user.click((await albumButtons())[0]!)
    expect(await screen.findByText('That album no longer exists.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Add to album' })).toBeInTheDocument()
  })

  it('adds a whole folder', async () => {
    const { user } = renderPicker({ folderId: 3 })
    await user.click((await albumButtons())[1]!)
    expect(await screen.findByText('Added 3 photos to Empty.')).toBeInTheDocument()
    expect(albumStore.get(6)!.imageIds).toEqual([20, 21, 22])
  })
})
```

- [ ] **Step 2: Run the tests.** `npx vitest run src/albums/AlbumPicker.test.tsx`. Expected: FAIL, because the module doesn't exist.

- [ ] **Step 3: Implement.** Create `web/src/albums/errors.ts`:

```ts
import { ApiError } from '../api/client'

export type AlbumFormErrors = { name?: string; description?: string; form?: string }

function fieldError(errors: Record<string, string[]>, field: string): string | undefined {
  return Object.entries(errors).find(([key]) => key.toLowerCase() === field)?.[1][0]
}

/** Maps a create/update failure to the field it belongs to. */
export function albumFormErrors(error: unknown): AlbumFormErrors {
  if (error instanceof ApiError) {
    if (error.status === 409) return { name: 'An album with this name already exists.' }
    if (error.status === 400) {
      const errors = error.problem?.errors ?? {}
      const name = fieldError(errors, 'name')
      const description = fieldError(errors, 'description')
      if (name !== undefined || description !== undefined) return { name, description }
    }
  }
  return { form: "Couldn't save the album." }
}
```

Create `web/src/albums/useAlbumAdder.ts`:

```ts
import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { useAddToAlbum, type AddTarget } from '../api/albums'
import { isNotFound } from '../api/client'
import { queryKeys } from '../api/queries'
import { useNotify } from '../app/notify'
import { addedMessage } from './messages'
import { readLastUsedAlbum, writeLastUsedAlbum } from './preferences'

export type AddOutcome = 'added' | 'gone' | 'failed'
type AlbumRef = { id: number; name: string }

/** Adds photos to an album and reports it; shared by the picker and the viewer's Shift+A. */
export function useAlbumAdder() {
  const add = useAddToAlbum()
  const notify = useNotify()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const addTo = async (album: AlbumRef, target: AddTarget, unavailable = 0): Promise<AddOutcome> => {
    try {
      const result = await add.mutateAsync({ albumId: album.id, target })
      writeLastUsedAlbum(album.id)
      notify(addedMessage(result, album.name, unavailable), {
        label: 'Open album',
        onClick: () => void navigate(`/albums/${album.id}`),
      })
      return 'added'
    } catch (error) {
      if (!isNotFound(error)) return 'failed'
      if (readLastUsedAlbum() === album.id) writeLastUsedAlbum(null)
      void queryClient.invalidateQueries({ queryKey: queryKeys.albums() })
      return 'gone'
    }
  }

  return { addTo, isAdding: add.isPending }
}
```

Create `web/src/albums/AlbumPicker.tsx`:

```tsx
import AddIcon from '@mui/icons-material/Add'
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItemButton,
  ListItemText,
  TextField,
  Typography,
} from '@mui/material'
import { useState, type ReactNode } from 'react'
import { useCreateAlbum, type AddTarget } from '../api/albums'
import { useAlbums } from '../api/queries'
import { albumFormErrors } from './errors'
import { photoCount } from './messages'
import { useAlbumAdder } from './useAlbumAdder'

type Props = {
  target: AddTarget
  /** Selected photos left out because their file is missing; mentioned in the result message. */
  unavailable?: number
  onClose: () => void
  onAdded?: () => void
}

export function AlbumPicker({ target, unavailable = 0, onClose, onAdded }: Props) {
  const albums = useAlbums()
  const { addTo, isAdding } = useAlbumAdder()
  const createAlbum = useCreateAlbum()
  const [filter, setFilter] = useState('')
  const [newName, setNewName] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const busy = isAdding || createAlbum.isPending

  const choose = async (album: { id: number; name: string }) => {
    setError(null)
    const outcome = await addTo(album, target, unavailable)
    if (outcome === 'added') {
      onAdded?.()
      onClose()
    } else {
      setError(outcome === 'gone' ? 'That album no longer exists.' : "Couldn't add photos.")
    }
  }

  const createAndAdd = async () => {
    const name = (newName ?? '').trim()
    if (name === '') return
    setError(null)
    try {
      const album = await createAlbum.mutateAsync({ name, description: null })
      await choose(album)
    } catch (err) {
      const errors = albumFormErrors(err)
      setError(errors.name ?? errors.form ?? "Couldn't create the album.")
    }
  }

  const needle = filter.trim().toLowerCase()
  const visible = (albums.data ?? []).filter((album) => album.name.toLowerCase().includes(needle))

  let list: ReactNode
  if (albums.isPending) {
    list = <CircularProgress size={24} sx={{ my: 2 }} />
  } else if (albums.isError) {
    list = (
      <Alert severity="error" sx={{ my: 1 }}>
        Couldn't load albums.
      </Alert>
    )
  } else if (visible.length === 0) {
    list = (
      <Typography color="text.secondary" sx={{ my: 2 }}>
        {albums.data.length === 0 ? 'No albums yet.' : 'No albums match.'}
      </Typography>
    )
  } else {
    list = (
      <List dense aria-label="Albums" sx={{ maxHeight: 320, overflowY: 'auto' }}>
        {visible.map((album) => (
          <ListItemButton key={album.id} disabled={busy} onClick={() => void choose(album)}>
            <ListItemText primary={album.name} secondary={photoCount(album.imageCount)} />
          </ListItemButton>
        ))}
      </List>
    )
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>Add to album</DialogTitle>
      <DialogContent>
        <TextField
          label="Filter albums"
          value={filter}
          onChange={(event) => setFilter(event.target.value)}
          autoFocus
          fullWidth
          size="small"
          margin="dense"
        />
        {error !== null && (
          <Alert severity="error" sx={{ my: 1 }}>
            {error}
          </Alert>
        )}
        {list}
        {newName === null ? (
          <Button startIcon={<AddIcon />} onClick={() => setNewName('')} sx={{ mt: 1 }}>
            New album
          </Button>
        ) : (
          <Box
            component="form"
            onSubmit={(event) => {
              event.preventDefault()
              void createAndAdd()
            }}
            sx={{ display: 'flex', gap: 1, mt: 1 }}
          >
            <TextField
              label="New album name"
              value={newName}
              onChange={(event) => setNewName(event.target.value)}
              size="small"
              fullWidth
              autoFocus
            />
            <Button type="submit" variant="contained" disabled={busy || newName.trim() === ''}>
              Create and add
            </Button>
          </Box>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
      </DialogActions>
    </Dialog>
  )
}
```

- [ ] **Step 4: Run the gates.** Expected: all pass.

- [ ] **Step 5: Commit.** `git add web/src && git commit -m "feat(web): add the album picker dialog"`

---

### Task 6: Selection in every grid, and "Add folder to album…"

**Files:**
- Create: `web/src/grid/useSelection.ts`, `web/src/grid/SelectionBar.tsx`
- Modify: `web/src/grid/PhotoTile.tsx` (full replacement), `web/src/views/ImageBrowser.tsx` (full replacement), `web/src/views/GridHeader.tsx`, `web/src/views/FolderView.tsx`
- Test: `web/src/grid/useSelection.test.tsx`, `web/src/grid/PhotoTile.test.tsx`, `web/src/views/Selection.test.tsx`

**Interfaces:**
- Consumes: from Task 5, `AlbumPicker`; from Task 3, `AddTarget`.
- Produces:
  - `useSelection(orderedIds, resetKey) → Selection`, where `Selection = { selected: ReadonlySet<number>; count; isSelecting; toggle(id, { shift }); selectAll(); clear() }`, and `SelectMods = { shift: boolean }`;
  - `SelectionBar({ count, onAddToAlbum, onClear, onRemove? })`, a `role="toolbar"` named "Selection";
  - `PhotoTile` gains `missing?: boolean` and `selection?: TileSelection`, where `TileSelection = { selecting; selected; onSelect(id, mods) }`;
  - `GridHeader` gains `actions?: ReactNode`.

- [ ] **Step 1: Write the failing tests.** Create `web/src/grid/useSelection.test.tsx`:

```tsx
import { act, fireEvent, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { useSelection } from './useSelection'

const ids = [1, 2, 3, 4, 5]
const sorted = (set: ReadonlySet<number>) => [...set].sort((a, b) => a - b)

describe('useSelection', () => {
  it('toggles single photos', () => {
    const { result } = renderHook(() => useSelection(ids, 'k'))
    act(() => result.current.toggle(2, { shift: false }))
    expect(result.current.isSelecting).toBe(true)
    act(() => result.current.toggle(2, { shift: false }))
    expect(result.current.count).toBe(0)
  })

  it('Shift selects the range from the last clicked photo, either direction', () => {
    const { result } = renderHook(() => useSelection(ids, 'k'))
    act(() => result.current.toggle(4, { shift: false }))
    act(() => result.current.toggle(2, { shift: true }))
    expect(sorted(result.current.selected)).toEqual([2, 3, 4])
  })

  it('Ctrl+A selects all and Esc clears, only while selecting', () => {
    const { result } = renderHook(() => useSelection(ids, 'k'))
    fireEvent.keyDown(window, { key: 'a', ctrlKey: true })
    expect(result.current.count).toBe(0)
    act(() => result.current.toggle(1, { shift: false }))
    fireEvent.keyDown(window, { key: 'a', ctrlKey: true })
    expect(result.current.count).toBe(5)
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(result.current.count).toBe(0)
  })

  it('starts empty again when the reset key changes', () => {
    const { result, rerender } = renderHook(({ key }) => useSelection(ids, key), {
      initialProps: { key: 'a' },
    })
    act(() => result.current.toggle(1, { shift: false }))
    rerender({ key: 'b' })
    expect(result.current.count).toBe(0)
  })
})
```

Append these tests inside `describe('PhotoTile', …)` in `web/src/grid/PhotoTile.test.tsx`:

```tsx
  it('shows a placeholder when the file is missing', () => {
    render(
      <PhotoTile item={image(1, 3)} size={180} caption={null} dimmed={false} missing onOpen={noop} onToggleFavorite={noop} />,
    )
    expect(screen.getByText('File missing')).toBeInTheDocument()
  })

  it('Ctrl-click selects instead of opening, and the checkbox reports Shift', async () => {
    const onOpen = vi.fn()
    const onSelect = vi.fn()
    render(
      <PhotoTile
        item={image(7, 3)}
        size={180}
        caption={null}
        dimmed={false}
        selection={{ selecting: false, selected: false, onSelect }}
        onOpen={onOpen}
        onToggleFavorite={noop}
      />,
    )
    const user = userEvent.setup()
    await user.keyboard('{Control>}')
    await user.click(screen.getByRole('button', { name: 'IMG_0007.jpg' }))
    await user.keyboard('{/Control}')
    expect(onSelect).toHaveBeenLastCalledWith(7, { shift: false })
    await user.keyboard('{Shift>}')
    await user.click(screen.getByRole('checkbox', { name: 'Select IMG_0007.jpg' }))
    await user.keyboard('{/Shift}')
    expect(onSelect).toHaveBeenLastCalledWith(7, { shift: true })
    expect(onOpen).not.toHaveBeenCalled()
  })
```

Create `web/src/views/Selection.test.tsx`:

```tsx
import { screen, waitFor } from '@testing-library/react'
import { act } from 'react'
import { describe, expect, it } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { renderApp } from '../test/render'

const selectionBar = () => screen.findByRole('toolbar', { name: 'Selection' })

describe('selecting photos', () => {
  it('a ticked checkbox shows the selection bar, and clicks then toggle instead of opening', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    expect(await selectionBar()).toHaveTextContent('1 selected')
    await user.click(screen.getByRole('button', { name: 'IMG_0002.jpg' }))
    expect(await selectionBar()).toHaveTextContent('2 selected')
    expect(router.state.location.search).toBe('')
  })

  it('Shift-click selects a range, Esc clears, Ctrl+A selects all', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.keyboard('{Shift>}')
    await user.click(screen.getByRole('button', { name: 'IMG_0003.jpg' }))
    await user.keyboard('{/Shift}')
    expect(await selectionBar()).toHaveTextContent('3 selected')
    await user.keyboard('{Escape}')
    expect(await screen.findByRole('heading', { name: 'Madeira' })).toBeInTheDocument()
    await user.click(screen.getByRole('checkbox', { name: 'Select IMG_0002.jpg' }))
    await user.keyboard('{Control>}a{/Control}')
    expect(await selectionBar()).toHaveTextContent('3 selected')
  })

  it('Ctrl-click starts a selection', async () => {
    const { user } = renderApp('/folders/3')
    const tile = await screen.findByRole('button', { name: 'IMG_0001.jpg' })
    await user.keyboard('{Control>}')
    await user.click(tile)
    await user.keyboard('{/Control}')
    expect(await selectionBar()).toHaveTextContent('1 selected')
  })

  it('the selection clears when moving to another folder', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await selectionBar()
    await act(() => router.navigate('/folders/2'))
    expect(await screen.findByRole('heading', { name: 'Holidays' })).toBeInTheDocument()
    expect(screen.queryByRole('toolbar', { name: 'Selection' })).not.toBeInTheDocument()
  })

  it('adds the selection to an album and clears it', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(screen.getByRole('checkbox', { name: 'Select IMG_0003.jpg' }))
    await user.click(screen.getByRole('button', { name: 'Add to album…' }))
    await user.click(await screen.findByRole('button', { name: /^Empty/ }))
    expect(await screen.findByText('Added 2 photos to Empty.')).toBeInTheDocument()
    expect(albumStore.get(6)!.imageIds).toEqual([20, 22])
    await waitFor(() =>
      expect(screen.queryByRole('toolbar', { name: 'Selection' })).not.toBeInTheDocument(),
    )
  })

  it('Esc closes the picker but keeps the selection', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(screen.getByRole('button', { name: 'Add to album…' }))
    await screen.findByRole('dialog', { name: 'Add to album' })
    await user.keyboard('{Escape}')
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Add to album' })).not.toBeInTheDocument(),
    )
    expect(await selectionBar()).toHaveTextContent('1 selected')
  })

  it('Ctrl+A in the search box does not select photos', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    screen.getByRole('textbox', { name: 'Search file names' }).focus()
    await user.keyboard('{Control>}a{/Control}')
    expect(await selectionBar()).toHaveTextContent('1 selected')
  })

  it('adds a whole folder from its header', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('button', { name: 'Add folder to album…' }))
    await user.click(await screen.findByRole('button', { name: /^Empty/ }))
    expect(await screen.findByText('Added 3 photos to Empty.')).toBeInTheDocument()
  })

  it('offers no folder add when the folder has no photos of its own', async () => {
    renderApp('/folders/1')
    expect(await screen.findByRole('heading', { name: 'dev' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add folder to album…' })).not.toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run the tests.** `npx vitest run src/grid src/views/Selection.test.tsx`. Expected: FAIL, because the modules and props don't exist.

- [ ] **Step 3: Implement.** Create `web/src/grid/useSelection.ts`:

```ts
import { useEffect, useEffectEvent, useState } from 'react'

export type SelectMods = { shift: boolean }
export type Selection = {
  selected: ReadonlySet<number>
  count: number
  isSelecting: boolean
  toggle: (id: number, mods: SelectMods) => void
  selectAll: () => void
  clear: () => void
}

type State = { key: string; ids: ReadonlySet<number>; anchor: number | null }

/** In-memory selection over a grid, in grid order; starts empty again whenever `resetKey` changes. */
export function useSelection(orderedIds: readonly number[], resetKey: string): Selection {
  const [state, setState] = useState<State>({ key: resetKey, ids: new Set(), anchor: null })
  const current: State = state.key === resetKey ? state : { key: resetKey, ids: new Set(), anchor: null }
  const isSelecting = current.ids.size > 0

  const toggle = (id: number, { shift }: SelectMods) => {
    const ids = new Set(current.ids)
    const from = current.anchor === null ? -1 : orderedIds.indexOf(current.anchor)
    const to = orderedIds.indexOf(id)
    if (shift && from >= 0 && to >= 0) {
      for (const rangeId of orderedIds.slice(Math.min(from, to), Math.max(from, to) + 1)) ids.add(rangeId)
    } else if (ids.has(id)) {
      ids.delete(id)
    } else {
      ids.add(id)
    }
    setState({ key: resetKey, ids, anchor: id })
  }

  const selectAll = () => setState({ key: resetKey, ids: new Set(orderedIds), anchor: current.anchor })
  const clear = () => setState({ key: resetKey, ids: new Set(), anchor: null })

  const onKeyDown = useEffectEvent((event: KeyboardEvent) => {
    if (event.target instanceof HTMLElement && event.target.closest('input, textarea')) return
    if (event.key === 'Escape') {
      clear()
    } else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'a') {
      event.preventDefault()
      selectAll()
    }
  })

  useEffect(() => {
    if (!isSelecting) return
    // Bubble phase: an open dialog (MUI Modal) stops Escape first, so Esc closes it, not the selection.
    const listener = (event: KeyboardEvent) => onKeyDown(event)
    window.addEventListener('keydown', listener)
    return () => window.removeEventListener('keydown', listener)
  }, [isSelecting])

  return { selected: current.ids, count: current.ids.size, isSelecting, toggle, selectAll, clear }
}
```

Create `web/src/grid/SelectionBar.tsx`:

```tsx
import CloseIcon from '@mui/icons-material/Close'
import { Box, Button, IconButton, Typography } from '@mui/material'

type Props = {
  count: number
  onAddToAlbum: () => void
  onClear: () => void
  onRemove?: () => void
}

/** Replaces a grid's header while photos are selected. */
export function SelectionBar({ count, onAddToAlbum, onClear, onRemove }: Props) {
  return (
    <Box
      role="toolbar"
      aria-label="Selection"
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 1,
        px: 1,
        py: 1,
        borderBottom: 1,
        borderColor: 'divider',
        bgcolor: 'action.selected',
      }}
    >
      <IconButton aria-label="Clear selection" onClick={onClear}>
        <CloseIcon />
      </IconButton>
      <Typography sx={{ flex: 1 }}>{count} selected</Typography>
      <Button variant="contained" size="small" onClick={onAddToAlbum}>
        Add to album…
      </Button>
      {onRemove && (
        <Button color="error" size="small" onClick={onRemove}>
          Remove from album
        </Button>
      )}
      <Button size="small" onClick={onClear}>
        Clear
      </Button>
    </Box>
  )
}
```

Replace `web/src/grid/PhotoTile.tsx` with:

```tsx
import BrokenImageIcon from '@mui/icons-material/BrokenImage'
import HourglassEmptyIcon from '@mui/icons-material/HourglassEmpty'
import ImageNotSupportedIcon from '@mui/icons-material/ImageNotSupported'
import StarIcon from '@mui/icons-material/Star'
import StarBorderIcon from '@mui/icons-material/StarBorder'
import { Box, Checkbox, IconButton, Typography } from '@mui/material'
import { useState, type ReactNode } from 'react'
import type { ImageListItem } from '../api/types'
import type { SelectMods } from './useSelection'

export type TileSelection = {
  selecting: boolean
  selected: boolean
  onSelect: (id: number, mods: SelectMods) => void
}

type Props = {
  item: ImageListItem
  size: number
  caption: string | null
  dimmed: boolean
  missing?: boolean
  selection?: TileSelection
  onOpen: (id: number) => void
  onToggleFavorite: (item: ImageListItem) => void
}

export function PhotoTile({
  item,
  size,
  caption,
  dimmed,
  missing = false,
  selection,
  onOpen,
  onToggleFavorite,
}: Props) {
  const [failedSrc, setFailedSrc] = useState<string | null>(null)
  const name = `${item.fileName}${item.extension}`
  const thumbnail = item.thumbnailUrl
  const selecting = selection?.selecting ?? false
  const selected = selection?.selected ?? false

  let content: ReactNode
  if (missing) {
    content = <Placeholder icon={<ImageNotSupportedIcon />} label="File missing" />
  } else if (thumbnail === null) {
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

  // While selecting, activating a tile toggles it instead of opening the viewer.
  const activate = (shift: boolean) => {
    if (selection && selecting) selection.onSelect(item.id, { shift })
    else onOpen(item.id)
  }

  return (
    <Box
      role="button"
      tabIndex={0}
      aria-label={name}
      title={name}
      data-testid={`tile-${item.id}`}
      data-dimmed={dimmed}
      data-selected={selected}
      onClick={(event) => {
        if (selection && !selecting && (event.ctrlKey || event.metaKey)) {
          selection.onSelect(item.id, { shift: false })
          return
        }
        activate(event.shiftKey)
      }}
      onKeyDown={(event) => {
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault()
          activate(event.shiftKey)
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
        outline: selected ? '3px solid' : 'none',
        outlineColor: 'primary.main',
        outlineOffset: -3,
        '& .tile-star': { opacity: item.isFavorite ? 1 : 0 },
        '& .tile-check': { opacity: selecting ? 1 : 0 },
        '&:hover .tile-star, &:focus-within .tile-star, &:hover .tile-check, &:focus-within .tile-check':
          { opacity: 1 },
      }}
    >
      {content}
      {selected && (
        <Box
          aria-hidden
          sx={{
            position: 'absolute',
            inset: 0,
            bgcolor: 'primary.main',
            opacity: 0.25,
            pointerEvents: 'none',
          }}
        />
      )}
      {selection && (
        <Checkbox
          className="tile-check"
          size="small"
          checked={selected}
          slotProps={{ input: { 'aria-label': `Select ${name}` } }}
          onClick={(event) => event.stopPropagation()}
          onKeyDown={(event) => event.stopPropagation()}
          onChange={(event) =>
            selection.onSelect(item.id, {
              shift: (event.nativeEvent as MouseEvent).shiftKey === true,
            })
          }
          sx={{
            position: 'absolute',
            top: 0,
            left: 0,
            p: 0.5,
            borderRadius: 0,
            color: 'common.white',
            bgcolor: 'rgba(0,0,0,0.35)',
            '&.Mui-checked': { color: 'common.white' },
          }}
        />
      )}
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
      <Box
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
        <Typography variant="caption" noWrap component="div">
          {name}
        </Typography>
        {caption !== null && (
          <Typography variant="caption" noWrap component="div" sx={{ opacity: 0.8 }}>
            {caption}
          </Typography>
        )}
      </Box>
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

Replace `web/src/views/ImageBrowser.tsx` with:

```tsx
import { useMemo, useState, type ReactNode } from 'react'
import { useSearchParams } from 'react-router'
import { AlbumPicker } from '../albums/AlbumPicker'
import type { AddTarget } from '../api/albums'
import { useSetFavorite } from '../api/favorites'
import type { ImageFilter } from '../api/imageFilter'
import { useImages } from '../api/queries'
import type { ImageListItem } from '../api/types'
import { PhotoGrid } from '../grid/PhotoGrid'
import { PhotoTile } from '../grid/PhotoTile'
import { SelectionBar } from '../grid/SelectionBar'
import { useSelection } from '../grid/useSelection'
import { parseGridParams, withParams } from '../routing/urlState'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { PhotoViewer } from '../viewer/PhotoViewer'
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

/** Header (or selection bar), banner and virtualized grid for one image filter. */
export function ImageBrowser({
  filter,
  header,
  banner,
  dimUnfavorited = false,
  captionFor,
  emptyState,
}: Props) {
  const [searchParams, setSearchParams] = useSearchParams()
  const { image } = parseGridParams(searchParams)
  const images = useImages(filter)
  const setFavorite = useSetFavorite()
  const items = useMemo(() => images.data?.pages.flatMap((page) => page.items) ?? [], [images.data])
  const ids = useMemo(() => items.map((item) => item.id), [items])
  const filterKey = JSON.stringify(filter)
  const selection = useSelection(ids, filterKey)
  const [pickerTarget, setPickerTarget] = useState<AddTarget | null>(null)

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
        key={filterKey}
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
            selection={{
              selecting: selection.isSelecting,
              selected: selection.selected.has(item.id),
              onSelect: selection.toggle,
            }}
            onOpen={open}
            onToggleFavorite={(tile) =>
              setFavorite.mutate({ id: tile.id, isFavorite: !tile.isFavorite })
            }
          />
        )}
      />
    )
  }

  return (
    <>
      {selection.isSelecting ? (
        <SelectionBar
          count={selection.count}
          onAddToAlbum={() =>
            setPickerTarget({ imageIds: ids.filter((id) => selection.selected.has(id)) })
          }
          onClear={selection.clear}
        />
      ) : (
        header
      )}
      {banner}
      {body}
      {image !== null && (
        <PhotoViewer
          list={{
            items,
            hasNextPage: images.hasNextPage,
            fetchNextPage: () => images.fetchNextPage({ cancelRefetch: false }),
          }}
        />
      )}
      {pickerTarget !== null && (
        <AlbumPicker
          target={pickerTarget}
          onClose={() => setPickerTarget(null)}
          onAdded={selection.clear}
        />
      )}
    </>
  )
}
```

In `web/src/views/GridHeader.tsx`:
- add `import type { ReactNode } from 'react'`;
- add `actions?: ReactNode` to `Props` and destructure it;
- render `{actions}` right after the count `Typography` and before the sort `TextField`.

In `web/src/views/FolderView.tsx`:
- add `import { Button } from '@mui/material'` (merge it with the existing MUI import), `import { useState } from 'react'`, `import { AlbumPicker } from '../albums/AlbumPicker'` and `import type { AddTarget } from '../api/albums'`;
- add `const [pickerTarget, setPickerTarget] = useState<AddTarget | null>(null)` next to the other hooks, before any early return;
- pass this prop to `GridHeader`:

```tsx
          actions={
            !detail.isMissing && detail.imageCount > 0 ? (
              <Button size="small" onClick={() => setPickerTarget({ folderId })}>
                Add folder to album…
              </Button>
            ) : undefined
          }
```

- wrap the returned `<ImageBrowser … />` in a fragment followed by:

```tsx
      {pickerTarget !== null && (
        <AlbumPicker target={pickerTarget} onClose={() => setPickerTarget(null)} />
      )}
```

- [ ] **Step 4: Run the gates.** Expected: all pass. The 6a folder, favorites, search and viewer suites still pass.

- [ ] **Step 5: Commit.** `git add web/src && git commit -m "feat(web): select photos in any grid and add them, or a whole folder, to an album"`

---

### Task 7: Add to album from the viewer (button, A, Shift+A)

**Files:**
- Modify: `web/src/viewer/PhotoViewer.tsx`
- Test: `web/src/viewer/ViewerAlbums.test.tsx`

**Interfaces:**
- Consumes: from Task 2, `albumsQuery`; from Task 5, `useAlbumAdder` and `AlbumPicker`; from Task 2, `readLastUsedAlbum` and `writeLastUsedAlbum`.

- [ ] **Step 1: Write the failing tests.** Create `web/src/viewer/ViewerAlbums.test.tsx`:

```tsx
import { screen, waitFor } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { renderApp } from '../test/render'

const viewerImage = (name: string) => screen.findByRole('img', { name })

describe('adding from the viewer', () => {
  it('A opens the picker for the current photo', async () => {
    const { user } = renderApp('/folders/3?image=22')
    await viewerImage('IMG_0003.jpg')
    await user.keyboard('a')
    await user.click(await screen.findByRole('button', { name: /^Empty/ }))
    expect(await screen.findByText('Added 1 photo to Empty.')).toBeInTheDocument()
    expect(albumStore.get(6)!.imageIds).toEqual([22])
    expect(screen.getByRole('img', { name: 'IMG_0003.jpg' })).toBeInTheDocument()
  })

  it('the Add to album button opens the picker too', async () => {
    const { user } = renderApp('/folders/3?image=22')
    await viewerImage('IMG_0003.jpg')
    await user.click(screen.getByRole('button', { name: 'Add to album' }))
    expect(await screen.findByRole('dialog', { name: 'Add to album' })).toBeInTheDocument()
  })

  it('Shift+A adds straight to the last used album', async () => {
    localStorage.setItem('pm.albums.lastUsed', '6')
    const { user } = renderApp('/folders/3?image=22')
    await viewerImage('IMG_0003.jpg')
    await user.keyboard('{Shift>}A{/Shift}')
    expect(await screen.findByText('Added 1 photo to Empty.')).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Add to album' })).not.toBeInTheDocument()
  })

  it('Shift+A with no last used album opens the picker', async () => {
    const { user } = renderApp('/folders/3?image=22')
    await viewerImage('IMG_0003.jpg')
    await user.keyboard('{Shift>}A{/Shift}')
    expect(await screen.findByRole('dialog', { name: 'Add to album' })).toBeInTheDocument()
  })

  it('Shift+A with a deleted last album opens the picker and forgets it', async () => {
    localStorage.setItem('pm.albums.lastUsed', '999')
    const { user } = renderApp('/folders/3?image=22')
    await viewerImage('IMG_0003.jpg')
    await user.keyboard('{Shift>}A{/Shift}')
    expect(await screen.findByRole('dialog', { name: 'Add to album' })).toBeInTheDocument()
    expect(localStorage.getItem('pm.albums.lastUsed')).toBeNull()
  })

  it('Esc closes the picker, not the viewer', async () => {
    const { user } = renderApp('/folders/3?image=22')
    await viewerImage('IMG_0003.jpg')
    await user.keyboard('a')
    await screen.findByRole('dialog', { name: 'Add to album' })
    await user.keyboard('{Escape}')
    await waitFor(() =>
      expect(screen.queryByRole('dialog', { name: 'Add to album' })).not.toBeInTheDocument(),
    )
    expect(screen.getByRole('img', { name: 'IMG_0003.jpg' })).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run the tests.** `npx vitest run src/viewer/ViewerAlbums.test.tsx`. Expected: FAIL, because A does nothing and there's no button.

- [ ] **Step 3: Implement.** In `PhotoViewer.tsx`:
- **Imports:** add `PlaylistAddIcon` from `@mui/icons-material/PlaylistAdd`, `useQueryClient` from `@tanstack/react-query`, `albumsQuery` from `../api/queries`, `useNotify` from `../app/notify`, `AlbumPicker` from `../albums/AlbumPicker`, `useAlbumAdder` from `../albums/useAlbumAdder`, and `readLastUsedAlbum, writeLastUsedAlbum` from `../albums/preferences`.
- **State and handlers:** after the existing hooks, add:

```tsx
  const queryClient = useQueryClient()
  const notify = useNotify()
  const { addTo } = useAlbumAdder()
  const [pickerOpen, setPickerOpen] = useState(false)
```

  After `const notFound = …`, add:

```tsx
  const canAdd = current !== undefined && !isMissingItem(current)

  // Shift+A: straight into the last used album; without one (or if it's gone), fall back to the picker.
  const quickAdd = async () => {
    if (current === undefined || !canAdd) return
    const lastId = readLastUsedAlbum()
    const albums = lastId === null ? [] : await queryClient.ensureQueryData(albumsQuery)
    const album = albums.find((a) => a.id === lastId)
    if (album === undefined) {
      if (lastId !== null) writeLastUsedAlbum(null)
      setPickerOpen(true)
      return
    }
    const outcome = await addTo(album, { imageIds: [current.id] })
    if (outcome === 'gone') {
      notify('That album no longer exists.')
      setPickerOpen(true)
    } else if (outcome === 'failed') {
      notify("Couldn't add photos.")
    }
  }
```

- **Keys:** in `onKeyDown`, make the first statement `if (pickerOpen) return`, so keys typed in the picker, including Esc, don't drive the viewer. Add the case:

```tsx
      case 'a':
      case 'A':
        if (event.shiftKey) void quickAdd()
        else if (canAdd) setPickerOpen(true)
        break
```

- **Button:** in the top-right button group, before the info button:

```tsx
            {canAdd && (
              <IconButton
                aria-label="Add to album"
                onClick={() => setPickerOpen(true)}
                sx={{ color: 'common.white' }}
              >
                <PlaylistAddIcon />
              </IconButton>
            )}
```

- **Picker:** wrap the returned `<Dialog>…</Dialog>` in a fragment and add after it:

```tsx
      {pickerOpen && current !== undefined && (
        <AlbumPicker target={{ imageIds: [current.id] }} onClose={() => setPickerOpen(false)} />
      )}
```

- [ ] **Step 4: Run the gates.** Expected: all pass, including every existing viewer test.

- [ ] **Step 5: Commit.** `git add web/src && git commit -m "feat(web): add to album from the viewer with A and Shift+A"`

---

### Task 8: Albums page, album form, nav

**Files:**
- Create: `web/src/albums/AlbumFormDialog.tsx`, `web/src/albums/AlbumsPage.tsx`
- Modify: `web/src/app/AppShell.tsx`, `web/src/app/routes.tsx`
- Test: `web/src/albums/AlbumsPage.test.tsx`

**Interfaces:**
- Consumes: from Task 3, `useCreateAlbum`, `useUpdateAlbum` and `photoCount`; from Task 5, `albumFormErrors`.
- Produces:
  - `AlbumFormDialog`, with props `({ mode: 'create' } | { mode: 'edit'; album: AlbumDetail }) & { onClose(); onSaved(album: AlbumDetail) }`. Titles: "New album" or "Edit album". Submit button: "Create" or "Save".
  - `AlbumsPage`.
  - The `albums` route.

- [ ] **Step 1: Write the failing tests.** Create `web/src/albums/AlbumsPage.test.tsx`:

```tsx
import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { renderApp, renderRoutes } from '../test/render'
import { server } from '../test/server'
import { AlbumsPage } from './AlbumsPage'

function renderPage() {
  return renderRoutes(
    [
      { path: '/albums', element: <AlbumsPage /> },
      { path: '/albums/:albumId', element: <p>Album page</p> },
    ],
    '/albums',
  )
}

describe('AlbumsPage', () => {
  it('shows album cards, most recently updated first', async () => {
    renderPage()
    const cards = await screen.findAllByRole('link')
    expect(cards.map((c) => c.getAttribute('aria-label'))).toEqual(['Best of 2025', 'Empty'])
    expect(screen.getByText('2 photos · Updated 2026-09-20')).toBeInTheDocument()
  })

  it('says how to start when there are no albums', async () => {
    server.use(http.get('/api/albums', () => HttpResponse.json([])))
    renderPage()
    expect(
      await screen.findByText(
        'No albums yet. Create one, or select photos anywhere and choose Add to album.',
      ),
    ).toBeInTheDocument()
  })

  it('creates an album and opens it', async () => {
    const { user, router } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'New album' }))
    await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Trip')
    await user.click(screen.getByRole('button', { name: 'Create' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/albums/100'))
  })

  it('shows the name conflict on the field and stays open', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'New album' }))
    await user.type(screen.getByRole('textbox', { name: 'Name' }), 'Empty')
    await user.click(screen.getByRole('button', { name: 'Create' }))
    expect(await screen.findByText('An album with this name already exists.')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'New album' })).toBeInTheDocument()
  })

  it('is reachable from the app bar', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('link', { name: 'Albums' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/albums'))
    expect(await screen.findByRole('heading', { name: 'Albums' })).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run the tests.** `npx vitest run src/albums/AlbumsPage.test.tsx`. Expected: FAIL, because the modules don't exist.

- [ ] **Step 3: Implement.** Create `web/src/albums/AlbumFormDialog.tsx`:

```tsx
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  TextField,
} from '@mui/material'
import { useState, type FormEvent } from 'react'
import { useCreateAlbum, useUpdateAlbum } from '../api/albums'
import type { AlbumDetail } from '../api/types'
import { albumFormErrors, type AlbumFormErrors } from './errors'

type Props = ({ mode: 'create' } | { mode: 'edit'; album: AlbumDetail }) & {
  onClose: () => void
  onSaved: (album: AlbumDetail) => void
}

export function AlbumFormDialog(props: Props) {
  const initial = props.mode === 'edit' ? props.album : null
  const [name, setName] = useState(initial?.name ?? '')
  const [description, setDescription] = useState(initial?.description ?? '')
  const [errors, setErrors] = useState<AlbumFormErrors>({})
  const create = useCreateAlbum()
  const update = useUpdateAlbum(initial?.id ?? 0)
  const busy = create.isPending || update.isPending

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setErrors({})
    const input = {
      name: name.trim(),
      description: description.trim() === '' ? null : description.trim(),
    }
    try {
      const saved =
        props.mode === 'edit' ? await update.mutateAsync(input) : await create.mutateAsync(input)
      props.onSaved(saved)
    } catch (error) {
      setErrors(albumFormErrors(error))
    }
  }

  return (
    <Dialog open onClose={props.onClose} fullWidth maxWidth="xs">
      <form onSubmit={(event) => void submit(event)}>
        <DialogTitle>{props.mode === 'edit' ? 'Edit album' : 'New album'}</DialogTitle>
        <DialogContent>
          {errors.form !== undefined && (
            <Alert severity="error" sx={{ mb: 2 }}>
              {errors.form}
            </Alert>
          )}
          <TextField
            label="Name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            autoFocus
            fullWidth
            margin="dense"
            error={errors.name !== undefined}
            helperText={errors.name}
          />
          <TextField
            label="Description"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            multiline
            minRows={2}
            fullWidth
            margin="dense"
            error={errors.description !== undefined}
            helperText={errors.description}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={props.onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={busy || name.trim() === ''}>
            {props.mode === 'edit' ? 'Save' : 'Create'}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
```

Create `web/src/albums/AlbumsPage.tsx`:

```tsx
import PhotoLibraryOutlinedIcon from '@mui/icons-material/PhotoLibraryOutlined'
import { Box, Button, Card, CardActionArea, CardContent, CardMedia, Typography } from '@mui/material'
import { useState, type ReactNode } from 'react'
import { Link as RouterLink, useNavigate } from 'react-router'
import { useAlbums } from '../api/queries'
import type { AlbumSummary } from '../api/types'
import { EmptyMessage } from '../shared/EmptyMessage'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { GridSkeleton } from '../views/GridSkeleton'
import { AlbumFormDialog } from './AlbumFormDialog'
import { photoCount } from './messages'

export function AlbumsPage() {
  const albums = useAlbums()
  const navigate = useNavigate()
  const [creating, setCreating] = useState(false)

  let body: ReactNode
  if (albums.isPending) {
    body = <GridSkeleton />
  } else if (albums.isError) {
    body = <QueryErrorAlert message="Couldn't load albums." onRetry={() => void albums.refetch()} />
  } else if (albums.data.length === 0) {
    body = (
      <EmptyMessage>
        No albums yet. Create one, or select photos anywhere and choose Add to album.
      </EmptyMessage>
    )
  } else {
    body = (
      <Box
        component="ul"
        aria-label="Albums"
        sx={{
          listStyle: 'none',
          m: 0,
          p: 2,
          display: 'grid',
          gap: 2,
          gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))',
          alignContent: 'start',
          overflowY: 'auto',
          flex: 1,
        }}
      >
        {albums.data.map((album) => (
          <li key={album.id}>
            <AlbumCard album={album} />
          </li>
        ))}
      </Box>
    )
  }

  return (
    <>
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          gap: 2,
          px: 2,
          py: 1,
          borderBottom: 1,
          borderColor: 'divider',
        }}
      >
        <Typography variant="h6" component="h1">
          Albums
        </Typography>
        <Box sx={{ flex: 1 }} />
        <Button variant="contained" size="small" onClick={() => setCreating(true)}>
          New album
        </Button>
      </Box>
      {body}
      {creating && (
        <AlbumFormDialog
          mode="create"
          onClose={() => setCreating(false)}
          onSaved={(album) => {
            setCreating(false)
            void navigate(`/albums/${album.id}`)
          }}
        />
      )}
    </>
  )
}

function AlbumCard({ album }: { album: AlbumSummary }) {
  return (
    <Card variant="outlined">
      <CardActionArea component={RouterLink} to={`/albums/${album.id}`} aria-label={album.name}>
        {album.coverThumbnailUrl ? (
          <CardMedia
            component="img"
            image={album.coverThumbnailUrl}
            alt=""
            sx={{ height: 160, objectFit: 'cover' }}
          />
        ) : (
          <Box
            sx={{
              height: 160,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              bgcolor: 'action.hover',
              color: 'text.secondary',
            }}
          >
            <PhotoLibraryOutlinedIcon fontSize="large" />
          </Box>
        )}
        <CardContent>
          <Typography variant="subtitle1" component="h2" noWrap>
            {album.name}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {photoCount(album.imageCount)} · Updated {album.updatedAt.slice(0, 10)}
          </Typography>
        </CardContent>
      </CardActionArea>
    </Card>
  )
}
```

In `web/src/app/routes.tsx`, add `import { AlbumsPage } from '../albums/AlbumsPage'` and the route `{ path: 'albums', element: <AlbumsPage /> },` after `favorites`.

In `web/src/app/AppShell.tsx`, after the Favorites button, add:

```tsx
          <Button color="inherit" component={NavLink} to="/albums">
            Albums
          </Button>
```

- [ ] **Step 4: Run the gates.** Expected: all pass.

- [ ] **Step 5: Commit.** `git add web/src && git commit -m "feat(web): add the albums page and album form"`

---

### Task 9: Album view

**Files:**
- Create: `web/src/albums/AlbumView.tsx`, `web/src/albums/AlbumGrid.tsx`, `web/src/shared/ConfirmDialog.tsx`
- Modify: `web/src/grid/columns.ts` (export `GRID_PADDING = 8`), `web/src/grid/PhotoGrid.tsx` (import it instead of its local const), `web/src/app/routes.tsx`
- Test: `web/src/albums/AlbumView.test.tsx`

**Interfaces:**
- Consumes:
  - from Task 2: `useAlbum`, `useAlbumImages`, `readShowFolders`, `writeShowFolders`;
  - from Task 3: `useRemoveFromAlbum`, `useDeleteAlbum`, `photoCount`;
  - from Task 6: `useSelection`, `SelectionBar`, `PhotoTile`;
  - `PhotoViewer({ list })`, `AlbumPicker`, `AlbumFormDialog`.
- Produces:
  - `AlbumGrid({ items, showFolders, selection, onOpen, onToggleFavorite })`, which Task 10 makes sortable;
  - `ConfirmDialog({ title, message, confirmLabel, onConfirm, onClose, busy? })`;
  - the route `albums/:albumId`.

- [ ] **Step 1: Write the failing tests.** Create `web/src/albums/AlbumView.test.tsx`:

```tsx
import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { renderApp } from '../test/render'

const tileIds = () => screen.getAllByTestId(/^tile-/).map((tile) => tile.dataset.testid)

describe('AlbumView', () => {
  it('shows the header and the photos in album order', async () => {
    renderApp('/albums/5')
    expect(await screen.findByRole('heading', { name: 'Best of 2025' })).toBeInTheDocument()
    expect(screen.getByText('Keepers')).toBeInTheDocument()
    expect(screen.getByText('2 photos')).toBeInTheDocument()
    await screen.findByTestId('tile-20')
    expect(tileIds()).toEqual(['tile-21', 'tile-20'])
  })

  it('Show folders adds folder paths and is remembered', async () => {
    const { user } = renderApp('/albums/5')
    await screen.findByTestId('tile-20')
    expect(screen.queryByText('dev/Holidays/Madeira')).not.toBeInTheDocument()
    await user.click(screen.getByLabelText('Show folders'))
    expect(screen.getAllByText('dev/Holidays/Madeira')).toHaveLength(2)
    expect(localStorage.getItem('pm.albums.showFolders')).toBe('true')
  })

  it('keeps photos whose file is missing, with a placeholder', async () => {
    albumStore.get(5)!.missing.add(20)
    renderApp('/albums/5')
    expect(within(await screen.findByTestId('tile-20')).getByText('File missing')).toBeInTheDocument()
  })

  it.each(['/albums/404', '/albums/abc'])('says an unknown album (%s) is not found', async (path) => {
    renderApp(path)
    expect(await screen.findByText('Album not found.')).toBeInTheDocument()
  })

  it('says how to fill an empty album', async () => {
    renderApp('/albums/6')
    expect(
      await screen.findByText('This album is empty. Select photos anywhere and choose Add to album.'),
    ).toBeInTheDocument()
  })

  it('renames the album, and shows a taken name on the field', async () => {
    const { user } = renderApp('/albums/5')
    await user.click(await screen.findByRole('button', { name: 'Edit…' }))
    const name = screen.getByRole('textbox', { name: 'Name' })
    await user.clear(name)
    await user.type(name, 'Empty')
    await user.click(screen.getByRole('button', { name: 'Save' }))
    expect(await screen.findByText('An album with this name already exists.')).toBeInTheDocument()
    await user.clear(name)
    await user.type(name, 'Keepers 2025')
    await user.click(screen.getByRole('button', { name: 'Save' }))
    expect(await screen.findByRole('heading', { name: 'Keepers 2025' })).toBeInTheDocument()
  })

  it('deletes after confirming, and goes to Albums', async () => {
    const { user, router } = renderApp('/albums/5')
    await user.click(await screen.findByRole('button', { name: 'Delete…' }))
    expect(
      screen.getByText('Delete album Best of 2025? The photos stay in your library.'),
    ).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Delete' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/albums'))
    expect(albumStore.get(5)).toBeUndefined()
  })

  it('removes selected photos after confirming', async () => {
    const { user } = renderApp('/albums/5')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(screen.getByRole('button', { name: 'Remove from album' }))
    expect(screen.getByText('Remove 1 photo from Best of 2025?')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Remove' }))
    expect(await screen.findByText('Removed 1 photo from Best of 2025.')).toBeInTheDocument()
    await waitFor(() => expect(tileIds()).toEqual(['tile-21']))
  })

  it('adding a selection with a missing file skips it and says so', async () => {
    albumStore.get(5)!.missing.add(20)
    const { user } = renderApp('/albums/5')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(screen.getByRole('checkbox', { name: 'Select IMG_0002.jpg' }))
    await user.click(screen.getByRole('button', { name: 'Add to album…' }))
    await user.click(await screen.findByRole('button', { name: /^Empty/ }))
    expect(
      await screen.findByText('Added 1 photo to Empty (1 missing on disk was skipped).'),
    ).toBeInTheDocument()
    expect(albumStore.get(6)!.imageIds).toEqual([21])
  })

  it('the viewer steps through the album in album order', async () => {
    const { user } = renderApp('/albums/5')
    await user.click(await screen.findByRole('button', { name: 'IMG_0002.jpg' }))
    expect(await screen.findByRole('img', { name: 'IMG_0002.jpg' })).toBeInTheDocument()
    await user.keyboard('{ArrowRight}')
    expect(await screen.findByRole('img', { name: 'IMG_0001.jpg' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Next photo' })).not.toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run the tests.** `npx vitest run src/albums/AlbumView.test.tsx`. Expected: FAIL, because there's no `albums/:albumId` route yet.

- [ ] **Step 3: Implement.** In `web/src/grid/columns.ts`, add:

```ts
/** Breathing room on the right and bottom edges of a grid. */
export const GRID_PADDING = 8
```

In `PhotoGrid.tsx`, delete the local `GRID_PADDING` constant and its comment, and import `GRID_PADDING` from `./columns`.

Create `web/src/shared/ConfirmDialog.tsx`:

```tsx
import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
} from '@mui/material'

type Props = {
  title: string
  message: string
  confirmLabel: string
  onConfirm: () => void
  onClose: () => void
  busy?: boolean
}

export function ConfirmDialog({ title, message, confirmLabel, onConfirm, onClose, busy = false }: Props) {
  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <DialogContentText>{message}</DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button color="error" variant="contained" onClick={onConfirm} disabled={busy}>
          {confirmLabel}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
```

Create `web/src/albums/AlbumGrid.tsx`:

```tsx
import { useCallback, useState } from 'react'
import type { AlbumImageItem } from '../api/types'
import { columnCount, GRID_PADDING, TILE_GAP, tileSize } from '../grid/columns'
import { PhotoTile } from '../grid/PhotoTile'
import type { Selection } from '../grid/useSelection'

type Props = {
  items: AlbumImageItem[]
  showFolders: boolean
  selection: Selection
  onOpen: (id: number) => void
  onToggleFavorite: (item: AlbumImageItem) => void
}

/** The whole album in one plain grid (not virtualized), so any tile can be dragged anywhere. */
export function AlbumGrid({ items, showFolders, selection, onOpen, onToggleFavorite }: Props) {
  const [width, setWidth] = useState(0)

  const attach = useCallback((element: HTMLDivElement | null) => {
    if (element === null) return
    // clientWidth includes the padding but not the (always reserved) scrollbar gutter.
    const measure = () => setWidth(Math.max(0, element.clientWidth - GRID_PADDING))
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  const columns = columnCount(width)
  const size = tileSize(width, columns)

  return (
    <div
      ref={attach}
      className="min-h-0 flex-1 overflow-y-auto"
      data-testid="album-grid"
      style={{ scrollbarGutter: 'stable', paddingRight: GRID_PADDING, paddingBottom: GRID_PADDING }}
    >
      {width > 0 && (
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: `repeat(${columns}, ${size}px)`,
            gap: TILE_GAP,
          }}
        >
          {items.map((item) => (
            <PhotoTile
              key={item.id}
              item={item}
              size={size}
              caption={showFolders ? item.folderPath : null}
              dimmed={false}
              missing={item.isMissing}
              selection={{
                selecting: selection.isSelecting,
                selected: selection.selected.has(item.id),
                onSelect: selection.toggle,
              }}
              onOpen={onOpen}
              onToggleFavorite={() => onToggleFavorite(item)}
            />
          ))}
        </div>
      )}
    </div>
  )
}
```

Create `web/src/albums/AlbumView.tsx`:

```tsx
import { Alert, Box, Button, FormControlLabel, Link, Switch, Typography } from '@mui/material'
import { useMemo, useState, type ReactNode } from 'react'
import { Link as RouterLink, useNavigate, useParams, useSearchParams } from 'react-router'
import { useDeleteAlbum, useRemoveFromAlbum, type AddTarget } from '../api/albums'
import { isNotFound } from '../api/client'
import { useSetFavorite } from '../api/favorites'
import { useAlbum, useAlbumImages } from '../api/queries'
import { useNotify } from '../app/notify'
import { SelectionBar } from '../grid/SelectionBar'
import { useSelection } from '../grid/useSelection'
import { parseGridParams, parseId, withParams } from '../routing/urlState'
import { ConfirmDialog } from '../shared/ConfirmDialog'
import { EmptyMessage } from '../shared/EmptyMessage'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { PhotoViewer } from '../viewer/PhotoViewer'
import { GridSkeleton } from '../views/GridSkeleton'
import { AlbumFormDialog } from './AlbumFormDialog'
import { AlbumGrid } from './AlbumGrid'
import { AlbumPicker } from './AlbumPicker'
import { photoCount } from './messages'
import { readShowFolders, writeShowFolders } from './preferences'

const LARGE_ALBUM = 2000

type OpenDialog = 'edit' | 'delete' | 'remove' | null
type PickerState = { target: AddTarget; unavailable: number }

export function AlbumView() {
  const params = useParams()
  const albumId = parseId(params.albumId ?? null)
  const [searchParams, setSearchParams] = useSearchParams()
  const { image } = parseGridParams(searchParams)
  const navigate = useNavigate()
  const notify = useNotify()
  const album = useAlbum(albumId)
  const images = useAlbumImages(albumId)
  const setFavorite = useSetFavorite()
  const removeImages = useRemoveFromAlbum(albumId ?? 0)
  const deleteAlbum = useDeleteAlbum(albumId ?? 0)
  const items = useMemo(() => images.data?.pages.flatMap((page) => page.items) ?? [], [images.data])
  const ids = useMemo(() => items.map((item) => item.id), [items])
  const selection = useSelection(ids, `album-${albumId}`)
  const [showFolders, setShowFolders] = useState(readShowFolders)
  const [dialog, setDialog] = useState<OpenDialog>(null)
  const [picker, setPicker] = useState<PickerState | null>(null)

  if (albumId === null || isNotFound(album.error)) {
    return (
      <Box sx={{ p: 3 }}>
        <Typography gutterBottom>Album not found.</Typography>
        <Link component={RouterLink} to="/albums">
          Back to albums
        </Link>
      </Box>
    )
  }
  if (album.isPending) return <GridSkeleton />
  if (album.isError) {
    return <QueryErrorAlert message="Couldn't load this album." onRetry={() => void album.refetch()} />
  }

  const detail = album.data
  const selectedIds = ids.filter((id) => selection.selected.has(id))

  const open = (id: number) =>
    setSearchParams(withParams(searchParams, { image: id }), { state: { viewer: true } })

  const toggleShowFolders = () => {
    const next = !showFolders
    setShowFolders(next)
    writeShowFolders(next)
  }

  // Missing files can't be added (the API refuses them), so they're left out and counted.
  const addSelection = () => {
    const missing = new Set(items.filter((item) => item.isMissing).map((item) => item.id))
    const available = selectedIds.filter((id) => !missing.has(id))
    if (available.length === 0) {
      notify("Photos whose file is missing can't be added to an album.")
      return
    }
    setPicker({ target: { imageIds: available }, unavailable: selectedIds.length - available.length })
  }

  const confirmRemove = async () => {
    try {
      await removeImages.mutateAsync({ imageIds: selectedIds })
      notify(`Removed ${photoCount(selectedIds.length)} from ${detail.name}.`)
      selection.clear()
    } catch {
      notify("Couldn't remove photos.")
    }
    setDialog(null)
  }

  const confirmDelete = async () => {
    try {
      await deleteAlbum.mutateAsync()
      void navigate('/albums')
    } catch {
      notify("Couldn't delete the album.")
      setDialog(null)
    }
  }

  let body: ReactNode
  if (images.isError) {
    body = (
      <QueryErrorAlert message="Couldn't load this album's photos." onRetry={() => void images.refetch()} />
    )
  } else if (images.isPending || images.hasNextPage) {
    body = <GridSkeleton />
  } else if (items.length === 0) {
    body = (
      <EmptyMessage>This album is empty. Select photos anywhere and choose Add to album.</EmptyMessage>
    )
  } else {
    body = (
      <>
        {items.length > LARGE_ALBUM && (
          <Alert severity="info" sx={{ m: 2, mb: 0 }}>
            This album is large, so reordering may be slow.
          </Alert>
        )}
        <AlbumGrid
          items={items}
          showFolders={showFolders}
          selection={selection}
          onOpen={open}
          onToggleFavorite={(item) => setFavorite.mutate({ id: item.id, isFavorite: !item.isFavorite })}
        />
      </>
    )
  }

  const header = selection.isSelecting ? (
    <SelectionBar
      count={selection.count}
      onAddToAlbum={addSelection}
      onRemove={() => setDialog('remove')}
      onClear={selection.clear}
    />
  ) : (
    <Box
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 2,
        px: 2,
        py: 1,
        borderBottom: 1,
        borderColor: 'divider',
      }}
    >
      <Box sx={{ minWidth: 0 }}>
        <Typography variant="h6" component="h1" noWrap>
          {detail.name}
        </Typography>
        {detail.description && (
          <Typography variant="body2" color="text.secondary" noWrap>
            {detail.description}
          </Typography>
        )}
      </Box>
      <Box sx={{ flex: 1 }} />
      <Typography variant="body2" color="text.secondary" sx={{ flexShrink: 0 }}>
        {photoCount(detail.imageCount)}
      </Typography>
      <FormControlLabel
        control={<Switch size="small" checked={showFolders} onChange={toggleShowFolders} />}
        label="Show folders"
      />
      <Button size="small" onClick={() => setDialog('edit')}>
        Edit…
      </Button>
      <Button size="small" color="error" onClick={() => setDialog('delete')}>
        Delete…
      </Button>
    </Box>
  )

  return (
    <>
      {header}
      {body}
      {image !== null && (
        <PhotoViewer
          list={{ items, hasNextPage: images.hasNextPage, fetchNextPage: () => images.fetchNextPage() }}
        />
      )}
      {picker !== null && (
        <AlbumPicker
          target={picker.target}
          unavailable={picker.unavailable}
          onClose={() => setPicker(null)}
          onAdded={selection.clear}
        />
      )}
      {dialog === 'edit' && (
        <AlbumFormDialog
          mode="edit"
          album={detail}
          onClose={() => setDialog(null)}
          onSaved={() => setDialog(null)}
        />
      )}
      {dialog === 'delete' && (
        <ConfirmDialog
          title="Delete album"
          message={`Delete album ${detail.name}? The photos stay in your library.`}
          confirmLabel="Delete"
          busy={deleteAlbum.isPending}
          onConfirm={() => void confirmDelete()}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog === 'remove' && (
        <ConfirmDialog
          title="Remove photos"
          message={`Remove ${photoCount(selectedIds.length)} from ${detail.name}?`}
          confirmLabel="Remove"
          busy={removeImages.isPending}
          onConfirm={() => void confirmRemove()}
          onClose={() => setDialog(null)}
        />
      )}
    </>
  )
}
```

In `routes.tsx`, add `import { AlbumView } from '../albums/AlbumView'` and the route `{ path: 'albums/:albumId', element: <AlbumView /> },` after `albums`.

- [ ] **Step 4: Run the gates.** Expected: all pass.

- [ ] **Step 5: Commit.** `git add web/src && git commit -m "feat(web): add the album view with edit, delete, remove and show folders"`

---

### Task 10: Drag-and-drop reordering

**Files:**
- Modify: `web/src/albums/AlbumGrid.tsx` (full replacement), `web/src/grid/PhotoTile.tsx`, `web/src/albums/AlbumView.tsx`
- Test: `web/src/albums/AlbumReorder.test.tsx`

**Interfaces:**
- Consumes: from Task 3, `planMove` and `useMoveInAlbum`.
- Produces:
  - `PhotoTile` gains `activator?: TileActivator`, where `TileActivator = { ref: (element: HTMLElement | null) => void; props: HTMLAttributes<HTMLElement> }`;
  - `AlbumGrid` gains `onMove(activeId, overId)`.
- Keyboard: Space picks up and drops, the arrows move, Esc cancels. Enter still opens the viewer.

- [ ] **Step 1: Write the failing tests.** Create `web/src/albums/AlbumReorder.test.tsx`. jsdom lays nothing out, so this file gives each sortable tile its own box from its `data-sort-index`; the dnd-kit keyboard sensor needs distinct boxes to find a neighbour.

```tsx
import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { renderApp } from '../test/render'
import { server } from '../test/server'

const tileIds = () => screen.getAllByTestId(/^tile-/).map((tile) => tile.dataset.testid)

beforeEach(() => {
  albumStore.get(5)!.imageIds = [20, 21, 22]
  const original = HTMLElement.prototype.getBoundingClientRect
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (
    this: HTMLElement,
  ) {
    const index = this.closest<HTMLElement>('[data-sort-index]')?.dataset.sortIndex
    if (index === undefined) return original.call(this)
    const left = Number(index) * 200
    return { x: left, y: 0, left, top: 0, right: left + 180, bottom: 180, width: 180, height: 180, toJSON: () => ({}) } as DOMRect
  })
})

afterEach(() => vi.restoreAllMocks())

async function pickUp(name: string) {
  const tile = await screen.findByRole('button', { name })
  tile.focus()
  return tile
}

describe('reordering an album', () => {
  it('keyboard drag moves a photo and saves its new place', async () => {
    const { user } = renderApp('/albums/5')
    await pickUp('IMG_0001.jpg')
    await user.keyboard('[Space]')
    await user.keyboard('[ArrowRight]')
    await user.keyboard('[Space]')
    await waitFor(() => expect(tileIds()).toEqual(['tile-21', 'tile-20', 'tile-22']))
    await waitFor(() => expect(albumStore.get(5)!.imageIds).toEqual([21, 20, 22]))
  })

  it('moving to the front saves it there', async () => {
    const { user } = renderApp('/albums/5')
    await pickUp('IMG_0003.jpg')
    await user.keyboard('[Space]')
    await user.keyboard('[ArrowLeft]')
    await user.keyboard('[ArrowLeft]')
    await user.keyboard('[Space]')
    await waitFor(() => expect(albumStore.get(5)!.imageIds).toEqual([22, 20, 21]))
  })

  it('a failed save puts the order back', async () => {
    server.use(
      http.post('/api/albums/:id/images/:imageId/move', () =>
        HttpResponse.json({ title: 'boom' }, { status: 500 }),
      ),
    )
    const { user } = renderApp('/albums/5')
    await pickUp('IMG_0001.jpg')
    await user.keyboard('[Space]')
    await user.keyboard('[ArrowRight]')
    await user.keyboard('[Space]')
    expect(await screen.findByText("Couldn't save the new order.")).toBeInTheDocument()
    expect(tileIds()).toEqual(['tile-20', 'tile-21', 'tile-22'])
  })

  it('a click still opens the viewer, and Enter too', async () => {
    const { user } = renderApp('/albums/5')
    await user.click(await screen.findByRole('button', { name: 'IMG_0002.jpg' }))
    expect(await screen.findByRole('img', { name: 'IMG_0002.jpg' })).toBeInTheDocument()
  })

  it('dragging is off while photos are selected', async () => {
    const { user } = renderApp('/albums/5')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0003.jpg' }))
    await pickUp('IMG_0001.jpg')
    await user.keyboard('[Space]')
    expect(await screen.findByRole('toolbar', { name: 'Selection' })).toHaveTextContent('2 selected')
    expect(albumStore.get(5)!.imageIds).toEqual([20, 21, 22])
  })
})
```

- [ ] **Step 2: Run the tests.** `npx vitest run src/albums/AlbumReorder.test.tsx`. Expected: FAIL. Space opens the viewer and the order doesn't change.

- [ ] **Step 3: Implement.** In `PhotoTile.tsx`:
- change the react import to `import { useState, type HTMLAttributes, type ReactNode } from 'react'`;
- add and export the activator type:

```tsx
/** dnd-kit's drag handle wiring for a sortable tile (the tile itself is the handle). */
export type TileActivator = {
  ref: (element: HTMLElement | null) => void
  props: HTMLAttributes<HTMLElement>
}
```

- add `activator?: TileActivator` to `Props` and destructure it;
- on the root `Box`, add `ref={activator?.ref}` and `{...activator?.props}` as the **first** attributes, so the tile's own `role`, `tabIndex`, `aria-label`, `onClick` and `onKeyDown` override them;
- replace the tile's `onKeyDown` with:

```tsx
      onKeyDown={(event) => {
        // dnd-kit picks the tile up on Space (and marks the event handled); Enter still opens it.
        activator?.props.onKeyDown?.(event)
        if (event.defaultPrevented) return
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault()
          activate(event.shiftKey)
        }
      }}
```

Replace `web/src/albums/AlbumGrid.tsx` with:

```tsx
import {
  closestCenter,
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core'
import {
  rectSortingStrategy,
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
} from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { useCallback, useState, type HTMLAttributes, type ReactNode } from 'react'
import type { AlbumImageItem } from '../api/types'
import { columnCount, GRID_PADDING, TILE_GAP, tileSize } from '../grid/columns'
import { PhotoTile, type TileActivator } from '../grid/PhotoTile'
import type { Selection } from '../grid/useSelection'

type Props = {
  items: AlbumImageItem[]
  showFolders: boolean
  selection: Selection
  onOpen: (id: number) => void
  onToggleFavorite: (item: AlbumImageItem) => void
  onMove: (activeId: number, overId: number) => void
}

/** The whole album in one plain grid (not virtualized), so any tile can be dragged anywhere. */
export function AlbumGrid({ items, showFolders, selection, onOpen, onToggleFavorite, onMove }: Props) {
  const [width, setWidth] = useState(0)
  const sensors = useSensors(
    // A few pixels of travel before a drag starts, so a click still opens the photo.
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
      keyboardCodes: { start: ['Space'], cancel: ['Escape'], end: ['Space'] },
    }),
  )

  const attach = useCallback((element: HTMLDivElement | null) => {
    if (element === null) return
    // clientWidth includes the padding but not the (always reserved) scrollbar gutter.
    const measure = () => setWidth(Math.max(0, element.clientWidth - GRID_PADDING))
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  const columns = columnCount(width)
  const size = tileSize(width, columns)
  const ids = items.map((item) => item.id)

  const onDragEnd = ({ active, over }: DragEndEvent) => {
    if (over !== null && active.id !== over.id) onMove(Number(active.id), Number(over.id))
  }

  return (
    <div
      ref={attach}
      className="min-h-0 flex-1 overflow-y-auto"
      data-testid="album-grid"
      style={{ scrollbarGutter: 'stable', paddingRight: GRID_PADDING, paddingBottom: GRID_PADDING }}
    >
      {width > 0 && (
        <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={onDragEnd}>
          {/* A click must mean "toggle" while selecting, so dragging is off then. */}
          <SortableContext items={ids} strategy={rectSortingStrategy} disabled={selection.isSelecting}>
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: `repeat(${columns}, ${size}px)`,
                gap: TILE_GAP,
              }}
            >
              {items.map((item, index) => (
                <SortableTile key={item.id} id={item.id} index={index}>
                  {(activator) => (
                    <PhotoTile
                      item={item}
                      size={size}
                      caption={showFolders ? item.folderPath : null}
                      dimmed={false}
                      missing={item.isMissing}
                      selection={{
                        selecting: selection.isSelecting,
                        selected: selection.selected.has(item.id),
                        onSelect: selection.toggle,
                      }}
                      activator={activator}
                      onOpen={onOpen}
                      onToggleFavorite={() => onToggleFavorite(item)}
                    />
                  )}
                </SortableTile>
              ))}
            </div>
          </SortableContext>
        </DndContext>
      )}
    </div>
  )
}

function SortableTile({
  id,
  index,
  children,
}: {
  id: number
  index: number
  children: (activator: TileActivator) => ReactNode
}) {
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging } =
    useSortable({ id })
  return (
    <div
      ref={setNodeRef}
      data-sort-index={index}
      style={{
        transform: CSS.Transform.toString(transform),
        transition,
        position: 'relative',
        zIndex: isDragging ? 1 : undefined,
        opacity: isDragging ? 0.6 : 1,
      }}
    >
      {children({
        ref: setActivatorNodeRef,
        props: { ...attributes, ...(listeners as HTMLAttributes<HTMLElement> | undefined) },
      })}
    </div>
  )
}
```

In `AlbumView.tsx`:
- add `import { useMoveInAlbum } from '../api/albums'` (merge it with the existing import) and `import { planMove } from './reorder'`;
- add `const moveImage = useMoveInAlbum(albumId ?? 0)` next to the other mutation hooks;
- pass this prop to `AlbumGrid`:

```tsx
          onMove={(activeId, overId) => {
            const plan = planMove(ids, activeId, overId)
            if (plan !== null) moveImage.mutate({ imageId: activeId, ...plan })
          }}
```

- [ ] **Step 4: Run the gates.** Expected: all pass, including the Task 9 album view tests.

- [ ] **Step 5: Commit.** `git add web/src && git commit -m "feat(web): reorder album photos by drag and drop"`

---

### Task 11: Export dialog

**Files:**
- Create: `web/src/albums/exportFile.ts`, `web/src/albums/ExportDialog.tsx`, `web/src/shared/useDebouncedValue.ts`
- Modify: `web/src/albums/AlbumView.tsx`
- Test: `web/src/albums/exportFile.test.ts`, `web/src/albums/ExportDialog.test.tsx`

**Interfaces:**
- Consumes: from Task 2, `apiFetchText`, `queryKeys.albumExport`, `readExportPrefix` and `writeExportPrefix`.
- Produces:
  - `exportFileName(albumName) → string`, mirroring the API's `AlbumExportFormatter.FileName`;
  - `downloadText(fileName, text)`;
  - `useDebouncedValue(value, ms)`;
  - `ExportDialog({ album, missingCount, onClose })`, a dialog titled "Export album".

- [ ] **Step 1: Write the failing tests.** Create `web/src/albums/exportFile.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { exportFileName } from './exportFile'

describe('exportFileName', () => {
  it.each([
    ['Best of 2025', 'Best of 2025.txt'],
    ['Madeira: best?', 'Madeira_ best_.txt'],
    ['a/b\\c', 'a_b_c.txt'],
    ['   ', 'album.txt'],
  ])('%s → %s', (name, expected) => expect(exportFileName(name)).toBe(expected))
})
```

Create `web/src/albums/ExportDialog.test.tsx`:

```tsx
import { screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { renderApp } from '../test/render'

afterEach(() => vi.restoreAllMocks())

const preview = () => screen.getByLabelText('Export preview')

async function openExport() {
  const view = renderApp('/albums/5')
  await view.user.click(await screen.findByRole('button', { name: 'Export…' }))
  await screen.findByRole('dialog', { name: 'Export album' })
  return view
}

describe('ExportDialog', () => {
  it('previews the path list, applies the prefix, and remembers it', async () => {
    const { user } = await openExport()
    await waitFor(() => expect(preview()).toHaveTextContent('/dev/Holidays/Madeira/IMG_0002.jpg'))
    expect(screen.getByText('2 lines')).toBeInTheDocument()
    await user.type(screen.getByRole('textbox', { name: 'Path prefix' }), '/mnt/frame')
    await waitFor(() =>
      expect(preview()).toHaveTextContent('/mnt/frame/dev/Holidays/Madeira/IMG_0002.jpg'),
    )
    expect(localStorage.getItem('pm.albums.exportPrefix')).toBe('/mnt/frame')
  })

  it('downloads a file named after the album', async () => {
    const created: Blob[] = []
    // jsdom has no object URLs; these stay defined for later tests, which is harmless.
    URL.createObjectURL = vi.fn((blob: Blob) => {
      created.push(blob)
      return 'blob:export'
    })
    URL.revokeObjectURL = vi.fn()
    const downloads: string[] = []
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      downloads.push(this.download)
    })
    const { user } = await openExport()
    await waitFor(() => expect(preview()).toHaveTextContent('IMG_0001.jpg'))
    await user.click(screen.getByRole('button', { name: 'Download' }))
    expect(downloads).toEqual(['Best of 2025.txt'])
    expect(await created[0]!.text()).toBe(
      '/dev/Holidays/Madeira/IMG_0002.jpg\n/dev/Holidays/Madeira/IMG_0001.jpg\n',
    )
  })

  it('copies the list to the clipboard', async () => {
    const { user } = await openExport()
    await waitFor(() => expect(preview()).toHaveTextContent('IMG_0001.jpg'))
    await user.click(screen.getByRole('button', { name: 'Copy' }))
    expect(await screen.findByText('Copied to clipboard.')).toBeInTheDocument()
    expect(await navigator.clipboard.readText()).toContain('/dev/Holidays/Madeira/IMG_0001.jpg')
  })

  it('warns when some files are missing on disk', async () => {
    albumStore.get(5)!.missing.add(20)
    await openExport()
    expect(await screen.findByText('1 photo is missing on disk.')).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run the tests.** `npx vitest run src/albums/exportFile.test.ts src/albums/ExportDialog.test.tsx`. Expected: FAIL, because the modules and the Export… button don't exist.

- [ ] **Step 3: Implement.** Create `web/src/albums/exportFile.ts`:

```ts
const INVALID = new Set(['\\', '/', ':', '*', '?', '"', '<', '>', '|'])

const isControl = (code: number) => code < 32 || (code >= 127 && code <= 159)

/** Same rule as the API's AlbumExportFormatter.FileName. */
export function exportFileName(albumName: string): string {
  const safe = [...albumName]
    .map((char) => (isControl(char.charCodeAt(0)) || INVALID.has(char) ? '_' : char))
    .join('')
    .trim()
  return `${safe === '' ? 'album' : safe}.txt`
}

export function downloadText(fileName: string, text: string): void {
  const url = URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.append(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}
```

Create `web/src/shared/useDebouncedValue.ts`:

```ts
import { useEffect, useState } from 'react'

export function useDebouncedValue<T>(value: T, ms: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), ms)
    return () => window.clearTimeout(timer)
  }, [value, ms])
  return debounced
}
```

Create `web/src/albums/ExportDialog.tsx`:

```tsx
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  TextField,
  Typography,
} from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { apiFetchText } from '../api/client'
import { queryKeys } from '../api/queries'
import type { AlbumDetail } from '../api/types'
import { useNotify } from '../app/notify'
import { useDebouncedValue } from '../shared/useDebouncedValue'
import { downloadText, exportFileName } from './exportFile'
import { readExportPrefix, writeExportPrefix } from './preferences'

const PREVIEW_LINES = 5

type Props = { album: AlbumDetail; missingCount: number; onClose: () => void }

export function ExportDialog({ album, missingCount, onClose }: Props) {
  const notify = useNotify()
  const [prefix, setPrefix] = useState(readExportPrefix)
  const debounced = useDebouncedValue(prefix, 300)
  const exported = useQuery({
    queryKey: queryKeys.albumExport(album.id, debounced),
    queryFn: ({ signal }) => {
      const params = new URLSearchParams()
      if (debounced !== '') params.set('prefix', debounced)
      return apiFetchText(`/api/albums/${album.id}/export?${params}`, { signal })
    },
  })
  // Download/Copy only once the text matches what's typed in the prefix box.
  const ready = exported.data !== undefined && prefix === debounced && !exported.isFetching
  const lines = (exported.data ?? '').split('\n').filter((line) => line !== '')

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(exported.data ?? '')
      notify('Copied to clipboard.')
    } catch {
      notify("Couldn't copy.")
    }
  }

  let preview: ReactNode
  if (exported.isError) {
    preview = <Alert severity="error">Couldn't load the export.</Alert>
  } else if (exported.data === undefined) {
    preview = <CircularProgress size={24} />
  } else {
    preview = (
      <>
        <Typography variant="body2" color="text.secondary">
          {lines.length === 1 ? '1 line' : `${lines.length} lines`}
        </Typography>
        <Box
          component="pre"
          aria-label="Export preview"
          sx={{ m: 0, p: 1, overflowX: 'auto', fontSize: 12, bgcolor: 'action.hover' }}
        >
          {lines.slice(0, PREVIEW_LINES).join('\n')}
          {lines.length > PREVIEW_LINES ? '\n…' : ''}
        </Box>
      </>
    )
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Export album</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        <TextField
          label="Path prefix"
          placeholder="/mnt/frame"
          value={prefix}
          onChange={(event) => {
            setPrefix(event.target.value)
            writeExportPrefix(event.target.value)
          }}
          fullWidth
          size="small"
          margin="dense"
        />
        {missingCount > 0 && (
          <Alert severity="warning">
            {missingCount === 1 ? '1 photo is' : `${missingCount} photos are`} missing on disk.
          </Alert>
        )}
        {preview}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Close</Button>
        <Button disabled={!ready} onClick={() => void copy()}>
          Copy
        </Button>
        <Button
          variant="contained"
          disabled={!ready}
          onClick={() => downloadText(exportFileName(album.name), exported.data ?? '')}
        >
          Download
        </Button>
      </DialogActions>
    </Dialog>
  )
}
```

In `AlbumView.tsx`:
- add `import { ExportDialog } from './ExportDialog'`, and widen `OpenDialog` to `'edit' | 'delete' | 'remove' | 'export' | null`;
- add an `Export…` button between `Edit…` and `Delete…`:

```tsx
      <Button size="small" onClick={() => setDialog('export')}>
        Export…
      </Button>
```

- render the dialog:

```tsx
      {dialog === 'export' && (
        <ExportDialog
          album={detail}
          missingCount={items.filter((item) => item.isMissing).length}
          onClose={() => setDialog(null)}
        />
      )}
```

- [ ] **Step 4: Run the gates.** Expected: all pass.

- [ ] **Step 5: Commit.** `git add web/src && git commit -m "feat(web): export an album's path list"`

---

### Task 12: Duplicates view

**Files:**
- Create: `web/src/views/DuplicatesView.tsx`
- Modify: `web/src/app/routes.tsx`, `web/src/app/AppShell.tsx`
- Test: `web/src/views/DuplicatesView.test.tsx`

**Interfaces:**
- Consumes: from Task 2, `useDuplicates`; from Task 6, `useSelection`, `SelectionBar` and `PhotoTile`; `PhotoViewer({ list })`; `AlbumPicker`.
- Produces: the `duplicates` route and the Duplicates nav button.

- [ ] **Step 1: Write the failing tests.** Create `web/src/views/DuplicatesView.test.tsx`:

```tsx
import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('DuplicatesView', () => {
  it('lists the groups with each copy\'s folder, loading more as it goes', async () => {
    renderApp('/duplicates')
    await waitFor(() => expect(screen.getAllByRole('heading', { name: '2 copies' })).toHaveLength(2))
    const first = screen.getByTestId('tile-20')
    expect(within(first).getByText('dev/Holidays/Madeira')).toBeInTheDocument()
    expect(within(screen.getByTestId('tile-10')).getByText('dev/Holidays')).toBeInTheDocument()
  })

  it('the viewer steps only within the group', async () => {
    const { user } = renderApp('/duplicates')
    await user.click(await screen.findByRole('button', { name: 'IMG_0001.jpg' }))
    expect(await screen.findByRole('img', { name: 'IMG_0001.jpg' })).toBeInTheDocument()
    await user.keyboard('{ArrowRight}')
    expect(await screen.findByRole('img', { name: 'IMG_0001 copy.jpg' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Next photo' })).not.toBeInTheDocument()
  })

  it('starring a copy updates it', async () => {
    const { user } = renderApp('/duplicates')
    await user.click(await screen.findByRole('button', { name: 'Add IMG_0001.jpg to favorites' }))
    expect(
      await screen.findByRole('button', { name: 'Remove IMG_0001.jpg from favorites' }),
    ).toBeInTheDocument()
  })

  it('says when there are none', async () => {
    server.use(http.get('/api/duplicates', () => HttpResponse.json({ items: [], nextCursor: null })))
    renderApp('/duplicates')
    expect(await screen.findByText('No duplicates found.')).toBeInTheDocument()
  })

  it('a selection spans groups and can go into an album', async () => {
    const { user } = renderApp('/duplicates')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0003.jpg' }))
    expect(await screen.findByRole('toolbar', { name: 'Selection' })).toHaveTextContent('2 selected')
    await user.click(screen.getByRole('button', { name: 'Add to album…' }))
    await user.click(await screen.findByRole('button', { name: /^Empty/ }))
    expect(await screen.findByText('Added 2 photos to Empty.')).toBeInTheDocument()
    expect(albumStore.get(6)!.imageIds).toEqual([20, 22])
  })

  it('is reachable from the app bar', async () => {
    const { user, router } = renderApp('/folders/3')
    await user.click(await screen.findByRole('link', { name: 'Duplicates' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/duplicates'))
  })
})
```

- [ ] **Step 2: Run the tests.** `npx vitest run src/views/DuplicatesView.test.tsx`. Expected: FAIL, because there's no route.

- [ ] **Step 3: Implement.** Create `web/src/views/DuplicatesView.tsx`:

```tsx
import { Box, LinearProgress, Typography } from '@mui/material'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useSearchParams } from 'react-router'
import { AlbumPicker } from '../albums/AlbumPicker'
import type { AddTarget } from '../api/albums'
import { useSetFavorite } from '../api/favorites'
import { useDuplicates } from '../api/queries'
import { PhotoTile } from '../grid/PhotoTile'
import { SelectionBar } from '../grid/SelectionBar'
import { useSelection } from '../grid/useSelection'
import { parseGridParams, withParams } from '../routing/urlState'
import { EmptyMessage } from '../shared/EmptyMessage'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { PhotoViewer } from '../viewer/PhotoViewer'
import { GridSkeleton } from './GridSkeleton'

const TILE_SIZE = 180
/** Load the next page of groups when this close (px) to the bottom. */
const LOAD_MORE_MARGIN = 400
const noMorePages = () => undefined

/** Read-only review of identical photos: delete extra copies on disk, then rescan. */
export function DuplicatesView() {
  const [searchParams, setSearchParams] = useSearchParams()
  const { image } = parseGridParams(searchParams)
  const groups = useDuplicates()
  const setFavorite = useSetFavorite()
  const all = useMemo(() => groups.data?.pages.flatMap((page) => page.items) ?? [], [groups.data])
  const ids = useMemo(() => all.flatMap((group) => group.images.map((item) => item.id)), [all])
  const selection = useSelection(ids, 'duplicates')
  const [pickerTarget, setPickerTarget] = useState<AddTarget | null>(null)
  const scrollRef = useRef<HTMLDivElement>(null)
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = groups

  const loadMoreIfNearEnd = useCallback(() => {
    const element = scrollRef.current
    if (element === null || !hasNextPage || isFetchingNextPage) return
    if (element.scrollTop + element.clientHeight >= element.scrollHeight - LOAD_MORE_MARGIN) {
      void fetchNextPage()
    }
  }, [hasNextPage, isFetchingNextPage, fetchNextPage])

  // Also after each page: a short first page may not fill the screen, so no scroll event would come.
  useEffect(loadMoreIfNearEnd, [loadMoreIfNearEnd, all.length])

  const open = (id: number) =>
    setSearchParams(withParams(searchParams, { image: id }), { state: { viewer: true } })
  const viewerGroup =
    image === null ? undefined : all.find((group) => group.images.some((item) => item.id === image))

  let body: ReactNode
  if (groups.isPending) {
    body = <GridSkeleton />
  } else if (groups.isError) {
    body = <QueryErrorAlert message="Couldn't load duplicates." onRetry={() => void groups.refetch()} />
  } else if (all.length === 0) {
    body = <EmptyMessage>No duplicates found.</EmptyMessage>
  } else {
    body = (
      <Box
        ref={scrollRef}
        onScroll={loadMoreIfNearEnd}
        sx={{ flex: 1, minHeight: 0, overflowY: 'auto', p: 2, display: 'flex', flexDirection: 'column', gap: 3 }}
      >
        {all.map((group) => (
          <Box component="section" key={group.contentHash} aria-labelledby={`dup-${group.contentHash}`}>
            <Typography id={`dup-${group.contentHash}`} variant="subtitle1" component="h2" gutterBottom>
              {group.count} copies
            </Typography>
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
              {group.images.map((item) => (
                <PhotoTile
                  key={item.id}
                  item={item}
                  size={TILE_SIZE}
                  caption={item.folderPath}
                  dimmed={false}
                  selection={{
                    selecting: selection.isSelecting,
                    selected: selection.selected.has(item.id),
                    onSelect: selection.toggle,
                  }}
                  onOpen={open}
                  onToggleFavorite={(tile) =>
                    setFavorite.mutate({ id: tile.id, isFavorite: !tile.isFavorite })
                  }
                />
              ))}
            </Box>
          </Box>
        ))}
        {isFetchingNextPage && <LinearProgress />}
      </Box>
    )
  }

  return (
    <>
      {selection.isSelecting ? (
        <SelectionBar
          count={selection.count}
          onAddToAlbum={() =>
            setPickerTarget({ imageIds: ids.filter((id) => selection.selected.has(id)) })
          }
          onClear={selection.clear}
        />
      ) : (
        <Box sx={{ px: 2, py: 1, borderBottom: 1, borderColor: 'divider' }}>
          <Typography variant="h6" component="h1">
            Duplicates
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Photos with identical content. Delete extra copies on disk, then rescan.
          </Typography>
        </Box>
      )}
      {body}
      {image !== null && (
        <PhotoViewer
          list={{ items: viewerGroup?.images ?? [], hasNextPage: false, fetchNextPage: noMorePages }}
        />
      )}
      {pickerTarget !== null && (
        <AlbumPicker
          target={pickerTarget}
          onClose={() => setPickerTarget(null)}
          onAdded={selection.clear}
        />
      )}
    </>
  )
}
```

In `routes.tsx`, add `import { DuplicatesView } from '../views/DuplicatesView'` and the route `{ path: 'duplicates', element: <DuplicatesView /> },` after `albums/:albumId`.

In `AppShell.tsx`, after the Albums button, add:

```tsx
          <Button color="inherit" component={NavLink} to="/duplicates">
            Duplicates
          </Button>
```

- [ ] **Step 4: Run the gates.** Expected: all pass.

- [ ] **Step 5: Commit.** `git add web/src && git commit -m "feat(web): add the duplicates review view"`

---

### Task 13: Live verification (controller-performed, with a human browser pass)

This task needs a running API and dev server, and a person looking at a browser. The controller runs every check it can from the shell, prepares the environment, and hands the browser checklist to the user. Record every command and its result.

- [ ] **Step 1: Prepare.**
  1. Run `dotnet ef database update --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api`.
  2. Start the API in the background from `src/PictureManager.Api`: `ASPNETCORE_ENVIRONMENT=Development dotnet run`. It listens on `http://localhost:5080`.
  3. Run a full scan: `curl -s -X POST http://localhost:5080/api/scans -H 'Content-Type: application/json' -d '{"isRecursive":true}'`. Wait for `Completed` on `/api/scans/{id}/events`.
  4. Start the dev server in the background from `web/`: `npm run dev`. It listens on `http://localhost:5173`.

- [ ] **Step 2: Shell checks.**

| # | Command | Expected |
|---|---|---|
| 1 | `curl -s -X POST http://localhost:5173/api/albums -H 'Content-Type: application/json' -d '{"name":"Live check"}'` | `201` JSON with an `id` |
| 2 | `curl -s -X POST http://localhost:5173/api/albums/<id>/images -H 'Content-Type: application/json' -d '{"folderId":<Madeira id>}'` | `{"added":3,"skipped":0}` |
| 3 | `curl -s "http://localhost:5173/api/albums/<id>/images"` | 3 items, each with `"folderPath":"dev/Holidays/Madeira"` |
| 4 | `curl -s "http://localhost:5173/api/albums/<id>/export?prefix=/mnt/frame"` | 3 lines starting `/mnt/frame/dev/Holidays/Madeira/` |
| 5 | `curl -s "http://localhost:5173/api/duplicates"` | JSON page, groups with `folderPath` |
| 6 | `curl -s -X DELETE http://localhost:5173/api/albums/<id>` | `204` (cleanup) |

- [ ] **Step 3: Browser checklist (the human).** Hand this list to the user, with the app open at `http://localhost:5173`, and record what they report:
  1. The app bar shows Folders · Favorites · Albums · Duplicates.
  2. In Madeira, tick two photos. The selection bar appears. **Add to album…** → **New album** "Test" → the message has an **Open album** link that opens it.
  3. Shift-click selects a range, Ctrl+A selects all, and Esc clears.
  4. **Add folder to album…** in Madeira adds the missing photo, and the message counts the ones already there.
  5. In the viewer, **A** opens the picker. **Shift+A** on the next photo adds it to "Test" directly.
  6. In "Test":
     - drag a photo with the mouse to another spot;
     - with enough photos, drag towards the bottom edge and watch the grid auto-scroll;
     - reload: the order was kept.
  7. **Show folders** toggles the folder line and is remembered after a reload.
  8. **Export…** with the prefix `/mnt/frame`: the preview updates, and **Download** saves `Test.txt` with correct paths.
  9. **Edit…** renames the album. **Delete…** confirms and returns to Albums.
  10. Duplicates shows the groups with their folder paths. The viewer steps only within a group.
  11. Switch the OS between dark and light: the new screens follow.

- [ ] **Step 4: Restore and record.** Stop both servers. Delete any albums created during the check, and unstar anything starred during it. Record every result.

---

## Verification (end to end)

- **Per task:** the task's own tests, then from `web/` the gates `npm test`, `npm run build`, `npm run lint` and `npm run format:check`. For backend tasks, `dotnet test` from the repo root.
- **After all tasks:**
  - the full backend and frontend suites pass;
  - Task 13's shell checks against a real API;
  - the human browser checklist, covering mouse drag, auto-scroll, download and dark mode, which jsdom can't exercise;
  - then a whole-branch review (see superpowers:executing-plans / subagent-driven-development).
