# Scanner: missing folders, unavailable roots, background scans — design

Follow-up to phase 5 ([2026-09-23-phase5-rest-api-design.md](2026-09-23-phase5-rest-api-design.md)), before
the phase 6 SPA. Status: draft for review.

## Problem

A folder that is renamed or deleted on disk is never visited again, so its rows stay visible forever. After a
rename, every photo appears twice in the grid, search and duplicates, and nothing tells the user why.

Scans are also fragile:

- The folder walk runs inside the `POST /api/scans` request. A large share keeps the request open for
  minutes, and closing the tab cancels the scan.
- An unmounted share looks like an empty folder, and today an empty root marks every top-level image missing.
- A restart during a scan leaves the job active forever. After that, every scan request and every folder
  removal returns `409`.

## Principles (user decisions)

- **Scans are manual only.** They start from a user action (`POST /api/scans`, a "Scan now" button in
  phase 6). There is no schedule and no file-system watcher.
- **The scanner never deletes folders or images because they vanished from disk.** It only marks them. The
  user decides what to do. Pruning items that settings exclude (phase 5) is unchanged; that deletion
  follows a choice the user made.
- **A root whose folder is missing or empty is a mount problem, not a deletion.** The scan changes nothing
  under that root and reports the problem.

## 1. Unavailable roots

A root is **unavailable** when its `MountPath` directory does not exist or contains no entries at all.

- The scanner checks each root before walking it. An unavailable root is skipped entirely: no folders are
  created or marked, and no images are added, updated or marked missing.
- The job still scans the other selected roots, then ends as **`Failed`** with an `ErrorMessage` naming the
  unavailable roots, for example:
  `Root 'nas-photos' is unavailable: its folder is missing or empty. Check that the share is mounted.`
  With several unavailable roots, the message lists each of them.
- A newly added root with an empty folder gets the same result. The message tells the user why nothing
  was found.

## 2. Missing folders

- New column `Folder.MissingSinceUtc` (`timestamptz`, nullable), added by a migration.
- **Marking:** at the end of each visited folder's pass, every active child folder row whose name was not
  seen in that directory listing is marked missing. So is its whole subtree, in one set-based SQL update.
  Names are compared the way the scanner already compares paths: NFC-normalized and case-insensitive.
  Folders already marked keep their original date. Removed-from-collection tombstones are left alone.
- **Unmarking:** a folder the scan finds on disk again has its `MissingSinceUtc` cleared. This applies to
  that folder only. Its descendants are cleared as the walk reaches them, so after a non-recursive scan
  they stay marked until a recursive one. Nothing is deleted or re-created, so favorites and album entries
  come back as they were.
- Per-image `MissingSinceUtc` is untouched by folder marking. When a folder comes back, each image's own
  state is still accurate.
- **Visibility:**
  - Images: `MissingSinceUtc == null && Folder.IsActive && Folder.MissingSinceUtc == null && Root.IsActive`.
    Images in missing folders drop out of the grid, search, favorites and duplicates.
  - Folders: unchanged. Missing folders stay in the tree so the user can see and act on them.
- **API shape:**
  - `FolderNode` and `FolderDetail` gain `IsMissing`.
  - For a missing folder, `ImageCount` counts its images that are not individually missing. That is what
    comes back if the folder reappears, and what "Remove" would purge.
  - Albums keep showing those images flagged `isMissing` (existing behaviour).
- **User actions (phase 6 UI):** the tree shows a "missing" badge. The user can:
  - fix the share and rescan, which unmarks the folder;
  - remove it with the existing remove-from-collection action (`DELETE /api/folders/{id}`), which purges
    its contents and leaves a tombstone.
- **Rename workflow (for now):** the user renames on disk, then scans. The new folder is indexed and the old
  one is marked missing. The user removes the old one. Favorites and album entries on the old folder's
  images are lost; see Deferred.

## 3. Background scans

- `POST /api/scans` validates the request (409 if a scan is active, 400 for an unknown or inactive
  `rootId`), creates the job, and returns `{ scanJobId }` at once. The walk runs on a background service in
  the Worker project, in its own DI scope, and is not tied to the HTTP request.
- Only one scan runs at a time, as now.
- **Live progress:** `FoldersScanned` and `FilesFound` are written every 50 folders during the walk, not only
  at the end.
- `GET /api/scans/{id}/events` payloads gain `errorMessage` (null unless the job failed), so the UI can show
  the unavailable-root warning.
- Application shutdown during a walk ends the job as `Cancelled`.
- **Startup recovery:** on start, any job still `Enumerating` or `Enriching` is set to `Failed` with
  `ErrorMessage` "Interrupted by an application restart." Without this, one crash blocks all scans and
  folder removals forever.

## Testing

- **Scanner unit tests** against a real temp directory:
  - missing root folder and empty root folder: no repository writes, job `Failed` with the message;
  - one unavailable root among several;
  - a vanished folder marks its subtree;
  - a rename marks the old folder and indexes the new one;
  - a reappearing folder is unmarked;
  - a non-recursive scan leaves descendants marked;
  - tombstones are untouched.
- **Postgres tests:**
  - the subtree marking query;
  - the image visibility rule with a missing folder, in list, search, favorites and duplicates;
  - `IsMissing` and `ImageCount` on folder nodes.
- **API tests:**
  - `POST /api/scans` returns before the walk finishes;
  - the event payload carries `errorMessage`;
  - startup recovery.
- **Live check:**
  - rename a folder on disk, scan, see the old folder marked and the new one indexed;
  - rename it back, scan, and the old folder is restored with its favorites.

## Deferred (take care of later)

- **Rename / relink.** When the user tells the app that a missing folder now lives at a new path, move the
  old image rows into the new folder, matched by file name and extension. They keep their ids, favorites and
  album entries, and the duplicate new rows are dropped. Until then, a rename loses favorites and album
  entries on that folder's images.
- **Per-image move detection.** Match a "new" file to a missing image by name, size and modified date, and
  re-link the row.
- **Purge missing.** An explicit user action that permanently deletes folders and images missing for longer
  than a chosen time.
- **Enrichment after a restart.** Images left `Pending` when the app stopped are not re-enqueued. Their size
  and date are unchanged, so the next scan treats them as `Unchanged`, and they never get EXIF data or a hash.
