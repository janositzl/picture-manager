# Phase 6a: SPA Browse — design

Status: **Draft, pending review** (2026-09-24)
Parent spec: [`2026-09-21-picturemanager-v1-design.md`](2026-09-21-picturemanager-v1-design.md), build phase 6.
API: [`2026-09-23-phase5-rest-api-design.md`](2026-09-23-phase5-rest-api-design.md), plus
[`2026-09-24-scanner-missing-folders-design.md`](2026-09-24-scanner-missing-folders-design.md) (`isMissing`).

## Phase 6 split

Phase 6 ships as three sub-projects, each with its own spec, plan and build:

- **6a Browse** (this document): app shell, folder tree, photo grid, viewer, favorites, search.
- **6b Organize**: albums (create, add, reorder, export) and the duplicates view.
- **6c Admin**: settings, roots, the scan button with live progress, and the remove/restore actions for
  missing and removed folders.

6a builds only what it needs. The shell leaves room in the nav for 6b and 6c but stubs nothing.

## Decisions

- **Desktop browser only.** Wide, dense layout. No mobile or tablet layouts.
- **One user, no login UI.** The API's `ICurrentUser` stays hardcoded until v2.
- **English only.**
- **Theme follows the OS** light/dark setting (MUI `colorSchemes`). No manual toggle.
- **Navigation is the tree.** The main area shows only the selected folder's own photos, with no subfolder
  tiles.
- **The viewer is a full-screen overlay** addressed by the URL, driven by the keyboard.
- **Search is global**, over file names, with an optional "in this folder" chip. The chip limits results to
  that folder's **direct** photos, because the API's `folderId` filter doesn't include subfolders.
- **The URL is the state.** There is no global client store.

## Stack

- **React Router**: routes and search params.
- **TanStack Query**: server data. `useInfiniteQuery` maps onto the API's `nextCursor` paging.
- **TanStack Virtual**: virtualized grid rows.
- **MUI** for components, with **Tailwind** for non-MUI layout only (per the v1 spec).
- **TypeScript**, strict as scaffolded, plus `noUncheckedIndexedAccess` (added in 6a).
- **Vitest + React Testing Library + MSW** for tests.

Rejected alternatives:
- plain `fetch` with hand-rolled caching, which would have to reimplement cursor accumulation and
  cross-view invalidation;
- TanStack Router, a heavier API with little benefit for four routes.

## Same-origin API

The API returns relative URLs (`/api/images/4812/thumbnail?v=…`), so the SPA is served from the same
origin as the API:

- **Development:** `vite.config.ts` proxies `/api` to the API. The target comes from
  `VITE_API_PROXY_TARGET`, which replaces `VITE_API_BASE_URL`, and `.env.example` is updated to match.
- **Production:** the API serves the built SPA. That is phase 7 work, not 6a.

The client calls relative `/api/...` paths and uses the server-provided image URLs as they are.

## Layout

```
┌──────────────────────────────────────────────────────────────────────┐
│ PictureManager   [🔍 search file names…   ][in: Holidays ×]  Folders  Favorites │
├───────────────┬──────────────────────────────────────────────────────┤
│ ▾ dev         │ Holidays   dev / Holidays       2 photos   Sort: Date ▾ ↓ │
│   ▾ Holidays ◀│ ┌───┐┌───┐                                            │
│       Madeira │ │   ││   │   virtualized photo grid                   │
│     ⚠ Old     │ └───┘└───┘                                            │
└───────────────┴──────────────────────────────────────────────────────┘
```

- **App bar:** app name, search field with the optional scope chip, and nav links (Folders, Favorites).
  6b adds Albums and Duplicates, and 6c adds Admin.
- **Folder tree:** fixed at 280 px on the left, always visible on every route, and scrolls on its own.
- **Main area:** a header row, optional banners, then the photo grid.

## Routes and URL state

| Route | View |
|---|---|
| `/` | redirects to the first root's top folder (`/folders/roots`, first entry) |
| `/folders/:folderId` | that folder's direct photos |
| `/favorites` | favorites |
| `/search?q=…&in=:folderId` | file-name search; `in` is the scope chip |

Search params shared by every grid view:

| Param | Values | Default |
|---|---|---|
| `sort` | `date`, `name` | `date` |
| `order` | `asc`, `desc` | `desc` for `date`, `asc` for `name` (the API's defaults) |
| `image` | an image id | absent (viewer closed) |

- **One helper parses and serializes all URL state.** Components never read raw `searchParams` strings.
  An invalid value falls back to the default, and an invalid `image` or `in` is dropped.
- **Opening the viewer** adds `image`. Closing it (Esc, the close button, or Back) removes it, and the grid
  keeps its scroll position.

## Header and banners

**Header row:**
- **Title.** The folder name, `Favorites`, or `Search: <q>`.
- **Path** (folder view only). A small, muted, clickable path built from the folder's breadcrumb. It
  orients you when you arrive from search or the viewer's "Go to folder" link.
- **Count.** The folder's `imageCount`, or omitted where the API gives no total (favorites and search).
- **Sort control.** Date or name, plus direction.
- **Space on the right** is left free for 6b's "Add to album" action. Nothing is built there in 6a.

**Banners** (folder view only):
- **Missing folder** (`isMissing`): "This folder is missing on disk. Its photos are hidden until it's back.
  Rescan or remove it (Admin)." The grid is empty in this case, because images in missing folders are not
  visible.
- **No direct photos:** "No photos directly in this folder. Pick a subfolder in the tree."

## Data layer (`src/api/`)

**`client.ts`.** A thin wrapper over `fetch` for relative `/api/...` paths. A non-2xx response throws
`ApiError { status, problem }`, where `problem` is the parsed ProblemDetails when present.

**`types.ts`.** Hand-written types that mirror the API records:
- `FolderNode { id, name, hasChildren, imageCount, isMissing }`
- `FolderDetail { id, name, rootId, rootName, relativePath, imageCount, isMissing, breadcrumb }`
- `ImageListItem`, including the new `folderPath`
- `ImageDetail`
- `Page<T> = { items: T[]; nextCursor: string | null }`

**`queries.ts`.** TanStack Query hooks:

| Hook | Endpoint | Notes |
|---|---|---|
| `useRootFolders()` | `GET /api/folders/roots` | top level of the tree |
| `useFolderChildren(id)` | `GET /api/folders/{id}/children` | fetched when a tree node expands |
| `useFolder(id)` | `GET /api/folders/{id}` | header, banners, tree deep-link expansion |
| `useImages(filter)` | `GET /api/images` | infinite query; the one hook behind every grid |
| `useImage(id)` | `GET /api/images/{id}` | viewer detail panel |
| `useSetFavorite()` | `PUT`/`DELETE /api/images/{id}/favorite` | optimistic mutation |

**`useImages` filter** is a discriminated union, and it doubles as the query key:

```ts
type ImageFilter = (
  | { kind: 'folder'; folderId: number }
  | { kind: 'favorites' }
  | { kind: 'search'; q: string; in?: number }
) & { sort: 'date' | 'name'; order: 'asc' | 'desc' }
```

It maps to API params as follows:
- `folder` → `folderId`
- `favorites` → `favoritesOnly=true`
- `search` → `fileName=q`, plus `folderId=in` when set

Pages are 100 items. The grid fetches the next page when the virtualizer is within two rows of the end of
the loaded items.

**Favorite toggle:**
- The ★ flips immediately in every cached `useImages` page that contains the image, and in its
  `useImage` detail. It rolls back on error and shows a snackbar ("Couldn't update favorite.").
- In the Favorites view, an unstarred photo **stays** in the grid, dimmed, until you leave the view.
  Removing it on the spot would shift the grid and throw off ←/→ in an open viewer. The favorites
  query is marked stale, so it refetches the next time the view mounts.

**Search input:**
- Typing updates `?q=` after a 300 ms debounce.
- An empty `q` shows a prompt and sends no request.
- It searches file names only. Folders are found with the tree.

**Retries:** none for `4xx`. Network errors and `5xx` get one automatic retry, then the error UI.

## Components

### Folder tree (`FolderTree`, `FolderTreeNode`)

- Built from MUI `List` + `Collapse`. `@mui/x-tree-view` is not used, because its lazy loading of children
  is a paid feature.
- A node fetches its children on first expand, and shows a small spinner while they load.
- The expand arrow appears only when `hasChildren` is true.
- A missing node (`isMissing`) shows a ⚠ badge and muted text. There are no actions on it in 6a.
- The selected folder is highlighted.
- **Deep links.** On `/folders/:id`, every ancestor in the folder's breadcrumb is expanded, and the selected
  node is scrolled into view.

### Photo grid (`PhotoGrid`, `PhotoTile`)

- **Rows are virtualized.** The column count is `max(1, floor(width / 180))`, where `width` comes from a
  ResizeObserver. Tiles are square and fill the row.
- **Thumbnails** use `object-fit: cover`.
- **Placeholders:**
  - a null `thumbnailUrl` (not yet processed) shows a neutral "processing" placeholder;
  - a thumbnail that fails to load shows a broken-image icon.
- **★ on the tile.** It is always shown for a favorite, and shown on hover otherwise. Clicking it toggles
  the favorite without opening the viewer.
- **Caption.** In search results it is the `folderPath`. Elsewhere the file name appears on hover.
- **Opening a photo.** A click sets `?image=id`.
- **First page:** skeleton tiles while it loads.

### Viewer (`PhotoViewer`)

- **A full-screen portal** over the whole app, with a black backdrop.
- **The image** is the `previewUrl`, fit to the screen, with a spinner while it loads.
- **Neighbours are preloaded:** the next and previous previews are fetched as the viewer opens and steps.
- **On-screen controls:** ‹ ›, close, and ★.
- **Keys:**

  | Key | Action |
  |---|---|
  | ← / → | previous / next photo |
  | Esc | close |
  | `F` | toggle favorite |
  | `I` | toggle the info panel |

- **The list comes from the grid.** ←/→ walk the same `useImages` query as the grid underneath; the viewer
  keeps no list of its own. Stepping past the last loaded item fetches the next page, and the arrows stop at
  the true end.
- **Info panel.** It sits on the right and is 320 px wide. It can be collapsed, and whether it is open is
  remembered in `localStorage` (reads and writes are wrapped, with open as the fallback). It shows:
  - the file name;
  - the folder path, with a **Go to folder** link to `/folders/{folderId}`;
  - date taken, dimensions and file size;
  - camera and lens;
  - GPS coordinates, with an OpenStreetMap link;
  - the albums the photo is in, read-only;
  - the raw metadata, as collapsed JSON.
- **Edge cases:**
  - A deep-linked `?image=id` that isn't in the loaded list still opens, from `useImage`. The ‹ › arrows
    are hidden in that case.
  - A `404` shows "This photo is no longer available" and a Close button.

## Errors and empty states

| Situation | UI |
|---|---|
| Query fails (tree, header or grid) | inline MUI `Alert` in that pane, with **Retry**; the other panes keep working |
| Folder `404` (removed, stale link) | "Folder not found", with a link to the first root |
| Favorite toggle fails | rollback + snackbar |
| Favorites empty | "No favorites yet. Star a photo with ★ or `F` in the viewer." |
| Search has no results | "No photos match *q*." |
| Search `q` empty | prompt to type a file name |

## Backend change

- `ImageListItem` gains `folderPath`: `"{root Name}/{RelativePath}"`, the same format as the image detail and
  duplicates responses. For a root's top folder it is `"{root Name}"`, with no trailing slash.
- It is built with the existing `FolderDisplayPath.For(rootName, relativePath)`, as the detail and duplicates
  responses already are.
- The image list queries already join `Folder` and `Root` for the visibility rule, so this is a projection
  change only.
- Tests:
  - a Postgres repository test for the value, including the top-folder case;
  - updated endpoint and shape tests where `ImageListItem` is constructed.

## Testing

**Frontend** (Vitest + React Testing Library + MSW, with fixtures shaped like the real API records):
- URL state: parse and serialize `sort`/`order`/`image`/`q`/`in`; invalid values fall back or are dropped.
- Grid column count for a range of widths.
- Favorite toggle:
  - an optimistic update across a grid page, the detail, and the favorites cache;
  - rollback and snackbar on error;
  - an unstarred photo stays, dimmed, in Favorites.
- Viewer:
  - ←/→, including crossing a page boundary, which triggers the next-page fetch;
  - Esc removes `image`;
  - `F` toggles the favorite, and `I` toggles the panel;
  - a deep link to an image that isn't loaded opens with the arrows hidden;
  - a `404` shows the unavailable state.
- Tree:
  - lazy expand;
  - deep-link expansion of the ancestors;
  - the missing badge.
- Header: the missing banner, the empty banner, and the count.
- Search: debounce, no request for an empty `q`, and the scope chip adds `folderId`.
- Tiles: the processing placeholder and the broken-image fallback.

**Gates.** All of these pass:
- `npm run build` (runs `tsc -b`)
- `npm run lint`
- `npm run format:check`
- `npm test`

**Backend:** the `folderPath` tests above, with `dotnet test` green.

**Live check:** the Vite dev server and the API, against `dev-data`:
- open `/` and land on the dev root;
- expand the tree, and deep-link to Madeira;
- step through the viewer with the keyboard, including Esc and Back;
- favorite a photo, check Favorites, unfavorite it, and see it stay dimmed;
- search with and without the scope chip;
- rename a folder on disk, scan through the API, and see the ⚠ badge and banner; then rename it back and
  scan again.

## Out of scope for 6a

- Albums and duplicates (6b).
- Settings, roots, the scan button with progress, and remove/restore of missing or removed folders (6c).
- Live tree and grid refresh while a scan runs (6c). The scanner commits each folder row as soon as its
  parent is listed, and each image as its folder is visited, so a refresh driven by the scan's event stream
  shows the tree filling in. In 6a, new folders appear on the next fetch (expand or reload).
- **Rescan a single folder** (6c). The backend part is `POST /api/scans` with a `folderId`, walking from that
  folder instead of the root. The UI part is a "Rescan folder" action in the tree and the folder header.
  Rules to carry into the 6c design:
  - check the root's mount first, and fail as "root unavailable" if it is missing or empty;
  - if the folder itself is gone from disk, mark it and its subtree missing;
  - reject a removed (tombstoned) folder with `400`;
  - allow a missing folder, which is how the user checks whether it's back;
  - `isRecursive` keeps its meaning.
- Subtree search, folder-name search, a thumbnail-size slider, selection and bulk actions.
- Mobile and tablet layouts, i18n, and a manual theme toggle.
- Serving the SPA from the API (phase 7) and Playwright end-to-end tests (phase 8).
