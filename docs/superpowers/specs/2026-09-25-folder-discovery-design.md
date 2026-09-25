# Folder Discovery and Folder-Scoped Scanning — Design

## 1. Context and scope

Source idea: `Documents/PictureManager-Folder-Discovery-and-Scanning.md`, which separates two
concerns that are entangled in today's scanner:

| Concern | What it does | Cost | Trigger |
|---|---|---|---|
| **Folder discovery** | Walks directory names only, builds the folder tree | Cheap | User-triggered, per root or folder |
| **Image scanning** | Enumerates and processes files | Expensive | User-chosen scope: all roots, one root, or one folder |

That source document covers five things: folder discovery, folder-scoped image scanning, a
persisted job queue (pause/cancel/reorder), a Scan Queue admin page, and folder tree status
icons. They were decomposed into separate sub-projects, each with its own spec/plan cycle. **This
spec covers folder discovery plus basic folder-scoped scanning** — the two foundational pieces:
folders can be discovered independently of scanning, and any folder (not just a whole root) can
be chosen as the target of an image scan.

**Backend only.** No frontend UI ships with this spec — no tree icons, no "not discovered yet"
badge, no discovery or scan trigger button. Those land with the later folder-tree-icons
sub-project. This spec delivers the job type, the data model, and an admin API
(`POST /api/discovery` + SSE progress, and `POST /api/scans` accepting a `folderId`), tested via
xUnit and curl — the same way the original scanning pipeline (phase 3) shipped backend-first,
ahead of phase 6's frontend.

**Explicitly deferred, not part of this spec:**
- Bounded-parallel walking (4–8 concurrent workers). The source document wants this for
  latency-bound SMB enumeration; this spec uses the existing sequential BFS pattern instead, as a
  fast-follow candidate once discovery is proven.
- True crash-resume of a job mid-walk. On an interrupted job, the existing
  `FailInterruptedJobsAsync` startup cleanup marks it `Failed`; the user re-triggers discovery,
  which naturally only re-walks what's still undiscovered (already-known folders are already in
  the DB).
- The source document's folder-unit scan execution: processing one folder per unit with
  `FoldersProcessed`/`LastProcessedPath` checkpoints, batch commits (e.g. 500), and
  cancel/pause/resume between units. A folder-scoped scan here reuses today's scan walk
  unchanged, just started from a different folder (§5).
- Exclusion-rule editing UI, `ScanStatus`/`LastScannedAt` display fields, the "possibly outdated"
  hint, the Scan Queue page, pause/cancel/reorder. All later sub-projects.

## 2. Data model

### `ScanJob` → `Job` (renamed, unified across both job kinds)

```csharp
public enum JobKind { Discovery, Scan }

public enum JobStatus { Pending, Enumerating, Enriching, Completed, Failed, Cancelled }
// Enumerating = walking (folder names for Discovery; folders+files for Scan).
// Enriching = Scan only (background per-image metadata extraction). A Discovery job goes straight
// from Enumerating to a terminal status — it never enters Enriching.

public class Job
{
    public int Id { get; set; }
    public JobKind Kind { get; set; }

    /// <summary>
    /// Scope: null = all active roots (Scan's existing "scan everything"; Discovery may also use
    /// this to mean "discover every root's own top level"). Otherwise the Folder this job starts
    /// from — a root's own top Folder for a whole-root job, or any subfolder for a folder-scoped
    /// job. Replaces today's ScanJob.RootId (an ImageRoot FK) and the never-populated
    /// RootFolderId with one field: every root now always has a top-level Folder row (see
    /// §3), so "the whole root" is simply that Folder with IsRecursive = true.
    /// </summary>
    public int? FolderId { get; set; }
    public bool IsRecursive { get; set; }

    public JobStatus Status { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }

    public int FoldersProcessed { get; set; }   // folders visited; meaningful for both kinds. Renamed from FoldersScanned.
    public int FilesFound { get; set; }         // Scan only; always 0 for Discovery jobs.
    public int FilesEnriched { get; set; }      // Scan only; always 0 for Discovery jobs.

    public string? ErrorMessage { get; set; }
    public Folder? Folder { get; set; }
}
```

Renames that follow from this: `IScanJobRepository` → `IJobRepository`, `ScanJobRepository` →
`JobRepository`, `ScanJobConfiguration` → `JobConfiguration`, table `ScanJobs` → `Jobs`. Existing
repository methods keep their shape (`GetByIdAsync`, `AddAsync`, `SetEnumerationResultAsync`,
`TryTransitionToEnrichingAsync`, `IncrementFilesEnrichedAsync`, `TryMarkCompletedIfEnrichedAsync`,
`SetFailureResultAsync`, `FailActiveJobsAsync`, `HasActiveJobAsync`, `ReloadAsync`) — they operate
on `Job` rows regardless of `Kind`, since discovery reuses the same status machine and the same
progress-counter pattern (just never touching `FilesFound`/`FilesEnriched`).

`IScanService` keeps its name and its methods (`QueueScanAsync`, `RunScanAsync`,
`FailInterruptedJobsAsync`); it now creates `Job` rows with `Kind = Scan`, and `QueueScanAsync`
gains a `folderId` parameter for folder-scoped scans (§5). A new sibling `IDiscoveryService`
(`QueueDiscoveryAsync`, `RunDiscoveryAsync`) creates `Kind = Discovery` rows and implements the
walk in §4.

### `Folder` additions

```csharp
public DateTime? ChildrenDiscoveredAt { get; set; }   // null = never discovered; set once this folder's own children have been diffed
public DateTime? LastWriteTimeUtc { get; set; }        // from the directory listing itself (free); powers a later "possibly outdated" hint — not used by this spec
```

`MissingSinceUtc` (existing field) is reused as-is for folders discovery finds gone from disk —
no new "IsMissing" concept. `IsExcluded`, `ScanStatus`, `LastScannedAt`, `LastScanFileCount` from
the source document are **not** added here; they're display/exclusion-editing concerns for later
sub-projects.

## 3. Root registration

Today `ImageRootSeeder.SeedAsync` creates `ImageRoot` rows only; the root's own top-level `Folder`
row is created lazily, the first time it's scanned (`ScanService.ScanRootAsync` →
`GetOrCreateFolderAsync`). Since the folder tree is built from `Folder` rows
(`GetVisibleRootFoldersAsync`), a freshly-seeded root doesn't appear in the tree at all until its
first scan — which conflicts with the goal of a new root showing up immediately as "not discovered
yet."

**Change:** after seeding/upserting `ImageRoot` rows, `ImageRootSeeder` ensures **every** root —
active or not, newly created in this run or pre-existing — has a top-level `Folder` row
(`RootId`, `ParentId = null`, `RelativePath = ""`, `Name = root.Name`, `ChildrenDiscoveredAt =
null`). This covers both a fresh dev database (post-migration, no roots discovered yet) and roots
that already existed before this spec landed. Inactive roots are included deliberately: their
folders are already hidden by the existing root-`IsActive` visibility rule, so the row is
harmless, and it means a root re-activated later through `PATCH /api/roots/{id}` shows up in the
tree immediately instead of only after the next restart. Idempotent and safe to run every
startup, same as the rest of seeding.

## 4. Discovery algorithm

`RunDiscoveryAsync(QueuedDiscovery)` walks directories only, reusing the same sequential
BFS-with-`Queue` shape as `ScanService.ScanRootAsync`, stripped to folders:

1. Start from the job's target `Folder` (resolved from `FolderId`, or every active root's top
   folder if `FolderId` is null).
2. Dequeue a folder; list its immediate subdirectories only (no file enumeration).
3. **Exclusion, checked before anything else per entry** — a name matching either the hardcoded
   list (`$RECYCLE.BIN`, `System Volume Information`, `@eaDir`, `#recycle`, `.snapshot`) or the
   existing user-editable `AppSettings.ExcludedFolderNames` is:
   - never turned into a `Folder` row (no insert, no descend), and
   - if a `Folder` row already exists for that name (it was discovered before being excluded),
     hard-deleted with its whole subtree (`DeleteSubtreeAsync`) — no tombstone, since the
     exclusion rule itself is what keeps it out and removing the rule brings it back next time.
     Same "prune on scan" behavior `ScanRootAsync` already has for scanning.
4. For every other subdirectory name observed on disk:
   - not an existing child `Folder` row → insert one.
   - existing row, currently `MissingSinceUtc != null` → clear it (back on disk).
   - existing row, `IsActive == false` (tombstoned via the folder-remove admin action) → leave
     alone, don't descend (same as scanning today).
5. For existing child `Folder` rows **not** observed on disk this pass (and not caught by the
   exclusion prune in step 3): `MissingSinceUtc = now` if not already set. Nothing is deleted —
   same "mark missing, don't remove" policy as scanning.
6. Once a folder's own children are fully diffed: `ChildrenDiscoveredAt = now`, and
   `LastWriteTimeUtc` = that folder's own directory last-write time. For every folder reached
   through a parent's listing, this is captured from that listing entry (no second stat call).
   The job's starting folder(s) have no parent listing in this job, so each is stat'd once
   directly (`Directory.GetLastWriteTimeUtc`) — one extra call per job target, not per folder.
7. If `IsRecursive`, enqueue each child that's active and not missing for the same treatment
   (depth-capped at `MaxScanDepth`, same value and reasoning as scanning — guards a
   symlink/junction cycle); reparse points are skipped outright, never enqueued. If not recursive,
   stop after step 6 for the one target folder.

Progress (`FoldersProcessed`) is written every `ProgressInterval` folders, same pattern and
constant as scanning's `SetEnumerationResultAsync` calls.

## 5. Folder-scoped scanning

An image scan can target any folder, not just a whole root. The scan walk itself is unchanged —
`ScanService.ScanRootAsync`'s loop (files reconciled, images enqueued for enrichment, missing
files and folders marked, excluded names pruned) is generalized to start from a given folder
instead of always the root's top folder.

**Scope resolution at queue time** (`QueueScanAsync(int? rootId, int? folderId, bool isRecursive)`):

| Request | Stored `Job.FolderId` | Walk starts at |
|---|---|---|
| neither `rootId` nor `folderId` | `null` | every active root's top folder (today's behavior) |
| `rootId` only | that root's top-level `Folder.Id` (exists per §3) | that root's top folder (today's behavior) |
| `folderId` only | `folderId` | that folder |
| both | — | refused, `400` |

A `folderId` is accepted only if the folder is visible under the existing visibility rules
(`IFolderRepository.IsVisibleAsync`: it exists, isn't tombstoned, and its root is active) **and**
isn't marked missing (`MissingSinceUtc == null`). Otherwise `400` keyed `folderId`. A missing
folder is refused because the scan couldn't reach it; scanning or discovering its parent is what
notices it's back.

A folder-scoped scan doesn't require the folder to have been discovered first
(`ChildrenDiscoveredAt` may be null): scanning still creates `Folder` rows as it walks, as today.
It never sets `ChildrenDiscoveredAt` either — that field belongs to discovery.

**At run time** the folder is resolved again, since it can be tombstoned or its root deactivated
while the job waits:
1. Folder no longer visible → the job fails, with the same pattern as today's "explicit root no
   longer active" case.
2. The folder's root isn't available (unmounted share, per the existing `IsRootAvailable` check) →
   the job fails with the existing unavailable-roots message; nothing is changed under it.
3. The folder's own directory (`root.MountPath` + `folder.RelativePath`, with `RelativePath`'s `/`
   separators mapped to the OS separator) no longer exists → the job fails with
   `"Folder is no longer on disk: {root name}/{relative path}"`. Nothing is marked missing by this
   job; a scan or discovery of the parent does that.
4. Otherwise the walk runs from that folder. `IsRecursive` means the same as today:
   `false` = that folder's own files only (its child `Folder` rows are still created and diffed,
   as today); `true` = the folder and everything below it, with `MaxScanDepth` counted from the
   starting folder.

**Nothing outside the target subtree is touched.** Sibling and ancestor folders, and their
images, are neither reconciled nor marked missing — only folders the walk actually visits are
diffed, which is already how the per-folder diff in `ScanRootAsync` works.

## 6. API

Both endpoints live in the `admin` route group (existing `ApiSurfaceMetadata(ApiSurface.Admin)`
seam), same as `/api/scans` today.

**`POST /api/discovery`** `{ folderId?: int, isRecursive: bool }` → `200` `{ jobId: int }`.
- `folderId: null` → every active root's top folder, each recursively per the shared
  `isRecursive` flag.
- `folderId: <id>` → that folder only. Unknown or inactive/tombstoned folder → `400`
  `ValidationProblem` keyed `folderId`.
- `409 Conflict` if any `Job` (either `Kind`) is currently `Enumerating`/`Enriching` — same
  `HasActiveJobAsync` check `POST /api/scans` uses, now naturally spanning both kinds since
  they're one table.

**`GET /api/discovery/{id}/events`** — SSE, identical poll-and-stream mechanics to
`/api/scans/{id}/events` (1s poll of `IJobRepository.GetByIdAsync`, stream closes on a terminal
status, unknown id → one `event: error` message). Payload:
```json
{"id": 8, "status": "Enumerating", "foldersProcessed": 340, "errorMessage": null}
```
No `filesFound`/`filesEnriched` in the Discovery payload (meaningless for this job kind) — a
separate `DiscoveryProgress` DTO, not a reuse of `ScanProgress` with zeroed fields. If the id
refers to a `Kind: Scan` job, `GET /api/discovery/{id}/events` returns `404` (use
`/api/scans/{id}/events` for that job instead) — the two routes are kind-specific views over the
same table, not interchangeable.

**`POST /api/scans`** `{ rootId?: int, folderId?: int, isRecursive: bool }` → `200`
`{ scanJobId: int }` (response unchanged). `folderId` is new and optional, so existing callers
are unaffected; scope resolution and validation are in §5.
- Both `rootId` and `folderId` → `400` `ValidationProblem` keyed `folderId` ("Give rootId or
  folderId, not both.").
- Invalid `folderId` (not visible, or missing) → `400` keyed `folderId`.
- Invalid `rootId` → `400` keyed `rootId` (existing behavior).
- Active job of either kind → `409` (existing behavior).

`GET /api/scans/{id}/events`'s response shape
(`id, status, foldersScanned, filesFound, filesEnriched, errorMessage`) is unchanged. The JSON
field is still called `foldersScanned` even though the column is now `FoldersProcessed`; that's an
endpoint-level DTO name, independent of the storage rename. If the id refers to a
`Kind: Discovery` job, this route returns `404`, mirroring the discovery route.

## 7. Concurrency and interrupted jobs

Unifying the table means both generalize with no new logic:

- **Single global slot:** `HasActiveJobAsync` becomes "any `Job` row with `Status` in
  `{Enumerating, Enriching}`," regardless of `Kind`. A Discovery job blocks a Scan job and
  vice versa — confirmed acceptable for this spec; true independent lanes (the source document's
  "discovery has priority over scans") is not built here.
- **Crash recovery:** `FailInterruptedJobsAsync` already just flips any row stuck in
  `Enumerating`/`Enriching` to `Failed` with "Interrupted by an application restart." on startup;
  this now covers Discovery jobs for free.

## 8. Migration

One EF Core migration. Dev-only project, no production data to preserve, so this is a
straightforward drop-and-recreate rather than a data-preserving rename:
- Rename table `ScanJobs` → `Jobs`.
- Add `Kind` (int enum, default backfill `Scan` for any pre-existing rows — in practice the dev DB
  can simply be recreated, since there's no real data at stake).
- Drop `RootId`, `RootFolderId`; add `FolderId` (nullable FK to `Folder`, `SetNull` on delete,
  same as today's `RootFolderId` FK behavior).
- Rename `FoldersScanned` → `FoldersProcessed`.
- Index on `Status` (existing) carries over; `Kind` doesn't need its own index — `HasActiveJobAsync`
  filters on `Status` alone.

## 9. Testing

- **`DiscoveryServiceTests`** (new; structure mirrors `ScanServiceTests`/`ScanServicePhase5Tests`):
  new folder found; folder gone → `MissingSinceUtc` set; missing folder reappears → cleared;
  hardcoded-excluded name skipped and never inserted; `AppSettings`-excluded name skipped and
  never inserted; previously-discovered folder later excluded → pruned with its subtree;
  non-recursive stops after the target folder's own children; depth cap; reparse point skipped;
  `ChildrenDiscoveredAt`/`LastWriteTimeUtc` stamped correctly; interrupted job → `Failed` on
  startup (shared `FailInterruptedJobsAsync` path, one Discovery-specific case added to its
  existing test suite).
- **`JobRepositoryTests`** (renamed from `ScanJobRepositoryTests`, same coverage) plus one new
  case: a `Kind: Discovery` job in `Enumerating` makes `HasActiveJobAsync` true, and a queued
  `Kind: Scan` job is refused (`409`) while it's active, and vice versa.
- **Folder-scoped scan tests** (new, alongside `ScanServicePhase5Tests`): a subfolder scan
  reconciles only that subtree (new/modified images enqueued, missing images marked) while
  sibling and ancestor folders' images are untouched and not marked missing; non-recursive
  subfolder scan processes only its own files; `rootId` scan stores the root's top folder as
  `Job.FolderId`; `folderId` scan stores that folder; both ids → refused; tombstoned, unknown,
  inactive-root or missing folder → refused at queue time; folder tombstoned while queued → job
  `Failed`; folder's root unavailable at run time → job `Failed` with the unavailable-roots
  message and nothing changed; folder's directory gone at run time → job `Failed` with
  "Folder is no longer on disk" and nothing marked missing.
- **API tests** for `POST /api/discovery` and `GET /api/discovery/{id}/events`, mirroring
  `ScanEndpointsTests`'s structure (happy path, unknown/invalid `folderId` → `400`, active job →
  `409`, unknown job id on the events route → `error` event, wrong-`Kind` job id → `404`).
  `ScanEndpointsTests` gains: `folderId` accepted and passed through, `rootId` + `folderId` →
  `400`, invalid `folderId` → `400`, and a Discovery job id on `/api/scans/{id}/events` → `404`.
- Existing `ScanService`/`ScanEndpoints`/`ScanJobRepository` tests are updated for the rename
  (`ScanJob` → `Job`, `RootId` → resolved `FolderId`) with their actual scan assertions otherwise
  unchanged — this is the regression net that the rename didn't change scan behavior.
- `ImageRootSeederTests` gains cases: seeding a root (new, pre-existing, or inactive) always
  results in a top-level `Folder` row with `ChildrenDiscoveredAt == null`, and re-running the
  seeder doesn't create a duplicate.

## Out of scope (tracked for later sub-projects)

- Bounded-parallel discovery workers.
- True mid-walk crash resume.
- Folder-unit scan execution: per-folder checkpoints (`LastProcessedPath`), batch commits, and
  cancel/pause/resume between folder units. (Choosing a folder as a scan's target is in scope,
  §5; changing how the walk executes is not.)
- A real job queue: persisted queueing, pause, cancel, reorder, dedup of overlapping recursive
  jobs. Today (and after this spec) it remains strictly "one job running, everything else
  refused" — no queueing.
- The Scan Queue admin page (frontend).
- Folder tree status icons, right-click "Scan folder / Scan folder + subfolders" menu, "not
  discovered yet" badge, manual "Refresh structure" button (frontend + the `ScanStatus`/
  `IsExcluded`/display fields that page needs).
- Exclusion-rule editing UI (the existing Settings page already edits
  `AppSettings.ExcludedFolderNames`; this spec only makes discovery respect it).
