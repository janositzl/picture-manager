# Phase 6b: Albums and Duplicates (SPA) — Design

**Status:** approved in conversation, 2026-09-24
**Builds on:** [Phase 6a SPA Browse](2026-09-24-phase6a-spa-browse-design.md)

## Goal

Let the user curate ordered photo albums and export their path lists, and review duplicate photos, inside the SPA built in 6a.

Albums are mainly for **curating and exporting**: the user builds a hand-picked, ordered selection and exports its path list to another tool or device, such as a photo frame or a copy job. Order and export matter most. Duplicates are **review only**. The user finds the copies here, then deletes them on disk and rescans.

**Success:**
- The user can create an album and fill it from anywhere they browse.
- They can reorder it by dragging, rename or delete it, and export its path list.
- They can see every group of identical photos, with where each copy lives.

## Scope

In scope:
- **Nav:** Albums and Duplicates entries in the app bar.
- **Album screens:** the album list page and the album view, with edit, delete, export and drag-and-drop reordering.
- **Adding photos:** multi-select in every grid, an album picker dialog, add-to-album from the viewer (A / Shift+A), and add-a-whole-folder from the folder header.
- **Removing photos:** from an album, via selection.
- **Duplicates:** a read-only review view.
- **Backend:** one change, `folderPath` on album photo items.

Out of scope:
- deleting, hiding or merging duplicates;
- dragging several photos at once;
- an undo for removal;
- album sharing, multiple users;
- per-album sorting other than the manual order;
- a virtualized album grid (see Decisions).

## Existing backend (unchanged except where noted)

| Endpoint | Use |
|---|---|
| `GET /api/albums` | `AlbumSummary[]` (id, name, description, imageCount, coverThumbnailUrl, updatedAt), most recently updated first |
| `POST /api/albums` | `{ name, description? }` → `201 AlbumDetail`. The name is required and unique (409 on conflict, 400 on invalid) |
| `GET /api/albums/{id}` | `AlbumDetail` (id, name, description, imageCount, createdAt, updatedAt), or 404 |
| `PATCH /api/albums/{id}` | `{ name?, description? }`. `description: null` clears it. 409/400/404 as above |
| `DELETE /api/albums/{id}` | 204. Photos stay in the library |
| `GET /api/albums/{id}/images?cursor&limit` | `Page<AlbumImageItem>` in album order. Missing files stay listed with `isMissing: true` and null URLs |
| `POST /api/albums/{id}/images` | `{ imageIds }` **or** `{ folderId }` → `{ added, skipped }`. New photos are appended at the end. `skipped` = already in the album. A folder adds only its **direct** photos. Unknown or unavailable ids → 400 |
| `POST /api/albums/{id}/images/remove` | `{ imageIds }` → 204 |
| `POST /api/albums/{id}/images/{imageId}/move` | `{ afterImageId }` → 204. `null` moves the photo to the front |
| `GET /api/albums/{id}/export?prefix=` | a `text/plain` attachment, one line per photo in album order: `{prefix}/{rootAlias or rootName}/{relativePath}/{fileName}{ext}`. The file name is `{album name}.txt` |
| `GET /api/duplicates?cursor&limit` | `Page<DuplicateGroup>`. Each group has `{ contentHash, count, images: DuplicateImageItem[] }` and each image carries `folderPath` |

**Backend change:** `AlbumImageItem` gains `FolderPath` (JSON `folderPath`), built with `FolderDisplayPath.For(rootName, relativePath)` exactly as 6a Task 1 did for `ImageListItem`. The album image row projection must carry the root name and relative path.

## Screens and navigation

The app bar nav becomes **Folders · Favorites · Albums · Duplicates**. The folder tree stays on the left on every route.

### `/albums`: album list

- A grid of album cards. Each shows:
  - the cover thumbnail, or a placeholder when there is none;
  - the name and the photo count;
  - the last-updated date.

  Cards follow the API order (most recently updated first). Clicking a card opens the album.
- A **New album** button opens the album form dialog. After creating, the app navigates to the new album.
- Empty state: "No albums yet. Create one, or select photos anywhere and choose Add to album."
- If loading fails: the 6a error box with Retry.

### `/albums/:albumId`: album view

- **Header:**
  - the album name, its description (if any) and "N photos";
  - actions: **Show folders** (toggle), **Edit…**, **Export…**, **Delete…**.
- **Grid:** a plain, fully loaded, drag-sortable grid in album order (see Decisions), with no sort control.
- **Tiles:**
  - They show the file name and ★ as in 6a.
  - With **Show folders** on, tiles add the folder path as a second line. The toggle is off by default and remembered in `localStorage` for all albums.
  - A photo whose file is missing (`isMissing`) shows a "File missing" placeholder. It can still be moved, removed and exported.
- **Viewer:** `?image=` opens the 6a viewer, stepping through the album's photos in album order.
- **States:**
  - unknown or invalid id: "Album not found." with a link to Albums;
  - empty album: "This album is empty. Select photos anywhere and choose Add to album.";
  - albums with more than 2,000 photos show a note that reordering may be slow.

### `/duplicates`: duplicates review

- One block per group, headed "N copies", with the group's copies side by side. Each copy shows its file name and folder path (always on).
- More groups load when the user scrolls near the end (API cursor paging).
- Clicking a copy opens the viewer (`?image=`), stepping only within that group.
- From the viewer the user can star a copy, add it to an album, or use **Go to folder** in the info panel.
- Empty state: "No duplicates found." If loading fails: the 6a error box with Retry.

## Selecting photos and adding them to albums

### Selection (every grid: folder, favorites, search, album, duplicates)

- **Starting:** hovering a tile shows a checkbox in its top-left corner (★ stays top-right). Ticking it, or Ctrl/⌘-clicking the tile, starts selection mode.
- **In selection mode:**
  - clicking a tile toggles it instead of opening the viewer;
  - Shift-click selects the range from the last clicked tile to this one, in grid order;
  - Ctrl/⌘+A selects all loaded photos;
  - Esc clears the selection.
- **Selected tiles** show a ticked checkbox and a tinted overlay.
- **Selection bar:** while anything is selected, it replaces the grid header. It reads "✕ N selected · **Add to album…** · **Clear**", and in the album view it also has **Remove from album**.
- **Lifetime:** selection lives only in component memory, never in the URL. It clears when the grid's filter changes (folder, sort, search query, scope) or the album changes.
- **Duplicates:** a single selection spans all loaded groups.

### Album picker (one dialog, used by every entry point)

- It lists albums with the most recently updated first. A filter box narrows by name, case-insensitively.
- **+ New album** asks for a name inline, creates the album, then adds the photos to it.
- **On success:**
  - a snackbar says "Added N photos to *Album*", adding "(M were already there)" when `skipped > 0`, with an **Open album** action;
  - the selection clears and the dialog closes;
  - the chosen album becomes the "last used album".
- **On failure** the dialog stays open, the selection is kept, and a message shows:
  - 404: "That album no longer exists." The album list refreshes.
  - other errors: "Couldn't add photos."

### Entry points

1. **Selection bar → Add to album…** adds the selected photos (`imageIds`).
2. **Viewer:**
   - an **Add to album** button and the **A** key open the picker for the current photo;
   - **Shift+A** adds the current photo straight to the last used album, with no dialog, and shows the same snackbar;
   - if there is no last used album yet, Shift+A opens the picker.
3. **Folder header → Add folder to album…** adds the folder's direct photos (`folderId`). It is hidden when the folder has no photos of its own.

The last used album id is remembered in `localStorage`.

## Inside an album

### Drag-and-drop reordering (dnd-kit)

- **Mouse:** a pointer drag starts after moving about 5 px, so a click still opens the viewer (or toggles in selection mode).
- **While dragging:** the other tiles shift to show the drop position (`@dnd-kit/sortable`, rect sorting), and the grid auto-scrolls near its top and bottom edges.
- **Keyboard:** focus a tile, press Space to pick it up, move it with the arrow keys, press Space to drop and Esc to cancel (dnd-kit keyboard sensor).
- **On drop:**
  - the new order is applied to the cache immediately;
  - `POST …/move` is sent with `afterImageId` = the id of the photo now before it, or `null` at the front;
  - on failure the previous order is restored and the message "Couldn't save the new order." is shown.
- Only one photo moves at a time. Dragging is disabled while any photo is selected.

### Edit, delete, remove

- **Edit…:** the album form dialog, prefilled with name and description. Errors:
  - 409: "An album with this name already exists." on the name field;
  - 400: the API's field message.

  The dialog stays open on error.
- **Delete…:** a confirmation reading "Delete album *Name*? The photos stay in your library." Then the app navigates to `/albums`.
- **Remove from album** (selection bar): a confirmation reading "Remove N photos from *Name*?" There is no undo, because removal loses their place in the order.

### Export…

- A dialog with a **Path prefix** field. The last value is remembered in `localStorage`.
- It shows:
  - a preview of the first 5 lines and the total line count, from `GET …/export?prefix=`;
  - when some photos are missing (from the album's loaded items): "N photos are missing on disk."
- **Download** saves the fetched text as `{album name}.txt`, with the file name sanitized the same way as the API. **Copy** puts the text on the clipboard.

## Architecture and data layer

### Types (`web/src/api/types.ts`)

- `AlbumSummary`, `AlbumDetail`, `AlbumAddResult`.
- `AlbumImageItem = ImageListItem & { isMissing: boolean }`. It has `folderPath` once the backend change lands.
- `DuplicateGroup = { contentHash: string; count: number; images: ImageListItem[] }`, since `DuplicateImageItem` has the `ImageListItem` shape.

### Queries (`web/src/api/`)

| Key | Hook | Notes |
|---|---|---|
| `['albums','list']` | `useAlbums()` | album list |
| `['albums', id, 'detail']` | `useAlbum(id)` | 404 → not found |
| `['images','list',{kind:'album',albumId}]` | `useAlbumImages(id)` | infinite query that keeps fetching until `nextCursor` is null. Keyed under the photo-list prefix so 6a's `patchFavorite` updates album tiles |
| `['duplicates']` | `useDuplicates()` | infinite query over groups. `patchFavorite` is extended to update copies inside cached groups |

### Mutations (`web/src/api/albums.ts`)

- **Create, update, delete:**
  - all refresh the album list;
  - update and delete also refresh the album detail;
  - delete removes that album's cached queries.
- **Add and remove:**
  - refresh the album list, the album detail and the album's photos;
  - refresh the details of the affected photos, because the info panel lists their albums.
- **Move:** optimistic reorder of the cached album photos, with rollback on error, then refresh the album list, since `updatedAt` changes.

### Refactors to 6a code

- **`PhotoViewer` takes a list source instead of a filter.** The source is `{ items, hasNextPage, fetchNextPage }`.
  - `ImageBrowser` passes its infinite list, the album view passes the album's photos, and duplicates pass the group's copies with `hasNextPage: false`.
  - Deep-link fallback via `useImage`, not-found handling, history rules and keys stay as they are.
  - The viewer gains the Add to album button and the A / Shift+A keys.
- **`PhotoTile`** gains `selectable`, `selected` and `onSelect(id, { shift, meta })`, plus an optional `missing` placeholder.
- **Shared selection:** `useSelection(items, resetKey)` and a `SelectionBar` component, used by `ImageBrowser`, the album view and the duplicates view.
- **Folder header:** `FolderView` gains the "Add folder to album…" header action.

### New components

`AlbumsPage`, `AlbumCard`, `AlbumView`, `SortableAlbumGrid`, `DuplicatesView`, `AlbumPicker`, `AlbumFormDialog`, `ExportDialog`, `ConfirmDialog`.

### Routes

`albums`, `albums/:albumId` and `duplicates` go under the 6a `AppShell` layout, and the nav gains **Albums** and **Duplicates** buttons.

### Stored in `localStorage` (wrapped in try/catch, as in 6a)

- `pm.albums.lastUsed` (album id)
- `pm.albums.exportPrefix`
- `pm.albums.showFolders`

### New dependencies

`@dnd-kit/core`, `@dnd-kit/sortable`, `@dnd-kit/utilities`.

## Decisions

- **The album grid is not virtualized.** The album view loads every page and renders a plain CSS grid with dnd-kit. Drag and auto-scroll are reliable because every tile exists. Curated albums are expected to stay well under a few thousand photos, and above 2,000 the view shows a note. The other grids stay virtualized as in 6a.
- **Selection is ephemeral.** It lives in memory, not the URL, so shared links stay clean and a reload doesn't restore a half-finished selection.
- **Duplicates are read-only.** No delete, hide or merge. The user removes copies on disk and rescans.
- **One photo moves at a time**, matching the API's move endpoint.

## Errors and edge cases

| Situation | Behavior |
|---|---|
| Album deleted elsewhere while adding | Picker shows "That album no longer exists.", refreshes the list, keeps the selection |
| Name conflict or invalid name | Field error in the form dialog; the dialog stays open |
| Move fails | Order reverts; "Couldn't save the new order." |
| Add, remove, export or delete fails | Error snackbar; nothing lost; selection kept |
| Unknown or invalid album id | "Album not found." with a link to Albums |
| Duplicates or albums fail to load | The 6a error box with Retry |
| Empty album / no albums / no duplicates | The empty-state messages above |
| Shift+A with no last used album | Opens the picker |
| Last used album no longer exists | The 404 message; the last-used id is forgotten |

## Testing

The stack is the same as 6a: Vitest, Testing Library, MSW and jsdom. MSW album handlers are backed by an in-memory store that resets before each test, so flows can be checked end to end: add → count, reorder → order.

- **Selection:** tick, Shift-range, Ctrl+A, Esc, clicking toggles in selection mode, and the selection clears on navigation.
- **Picker:**
  - add to an existing album, and create-and-add;
  - the "already there" count and the 404 message;
  - A and Shift+A in the viewer, including Shift+A with no last used album;
  - "Add folder to album…" sends `folderId`.
- **Album list:** cards, empty state, and create navigates to the album.
- **Album view:**
  - edit, including the 409 message;
  - delete navigates to `/albums`;
  - remove confirms;
  - the Show folders toggle is remembered;
  - missing-file tiles;
  - not-found and empty states;
  - the viewer steps in album order.
- **Reorder:**
  - keyboard drag (Space, arrow, Space) sends `move` with the right `afterImageId`, and to the front sends `null`;
  - the order updates on screen;
  - it reverts on failure.
- **Export:** the preview and line count, the remembered prefix, the downloaded file name, and the missing count.
- **Duplicates:** groups with folder paths, the next page on scroll, the viewer steps only within a group, and a ★ updates the copy.
- **Regression:** every existing 6a test still passes after the viewer and tile refactors.
- **Backend:** a repository test that album rows carry the root name and relative path, and a service test that `AlbumImageItem.FolderPath` is built correctly.
- **Live browser check (human):**
  - mouse drag with edge auto-scroll;
  - a large selection added to an album;
  - the downloaded export opens with correct paths;
  - Shift+A curating flow;
  - the duplicates view on real data;
  - dark mode.
