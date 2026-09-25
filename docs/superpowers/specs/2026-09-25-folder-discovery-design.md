# Folder Discovery — Design

## 1. Context and scope

Source idea: `Documents/PictureManager-Folder-Discovery-and-Scanning.md`, which separates two
concerns that are entangled in today's scanner:

| Concern | What it does | Cost | Trigger |
|---|---|---|---|
| **Folder discovery** | Walks directory names only, builds the folder tree | Cheap | User-triggered, per root or folder |
| **Image scanning** | Enumerates and processes files | Expensive | User-chosen scope (unchanged by this spec) |

That source document covers five things: folder discovery, folder-scoped image scanning, a
persisted job queue (pause/cancel/reorder), a Scan Queue admin page, and folder tree status
icons. They were decomposed into separate sub-projects, each with its own spec/plan cycle. **This
spec covers folder discovery only** (the first, foundational piece — nothing else in the source
document works until folders can be discovered independently of scanning).

**Backend only.** No frontend UI ships with this spec — no tree icons, no "not discovered yet"
badge, no discovery trigger button. Those land with the later folder-tree-icons sub-project. This
spec delivers the job type, the data model, and an admin API (`POST /api/discovery` +
SSE progress), tested via xUnit and curl — the same way the original scanning pipeline (phase 3)
shipped backend-first, ahead of phase 6's frontend.

**Explicitly deferred, not part of this spec:**
- Bounded-parallel walking (4–8 concurrent workers). The source document wants this for
  latency-bound SMB enumeration; this spec uses the existing sequential BFS pattern instead, as a
  fast-follow candidate once discovery is proven.
- True crash-resume of a job mid-walk. On an interrupted job, the existing
  `FailInterruptedJobsAsync` startup cleanup marks it `Failed`; the user re-triggers discovery,
  which naturally only re-walks what's still undiscovered (already-known folders are already in
  the DB).
- Folder-scoped **image** scanning (`POST /api/scans` keeps its existing `rootId`-only contract).
  The data model below is shaped so that sub-project can extend it without another migration, but
  wiring it up is out of scope here.
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

`IScanService` → keeps its name and its existing methods (`QueueScanAsync`, `RunScanAsync`,
`FailInterruptedJobsAsync`) unchanged in behavior; internally it now creates `Job` rows with
`Kind = Scan`. A new sibling `IDiscoveryService` (`QueueDiscoveryAsync`, `RunDiscoveryAsync`)
creates `Kind = Discovery` rows and implements the walk in §4.

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

## 5. API

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

**`POST /api/scans` is unchanged on the wire** — still `{ rootId?: int, isRecursive: bool }`.
Internally, `ScanService.QueueScanAsync` resolves `rootId` to that root's top-level `Folder.Id`
(guaranteed to exist per §3) and stores it as the new `Job.FolderId`; `rootId: null` still means
"every active root." `GET /api/scans/{id}/events`'s response shape
(`id, status, foldersScanned, filesFound, filesEnriched, errorMessage`) is also unchanged — the
JSON field is still called `foldersScanned` there even though the underlying column is now
`FoldersProcessed`; that's an endpoint-level DTO naming choice, independent of the storage rename.

## 6. Concurrency and interrupted jobs

Unifying the table means both generalize with no new logic:

- **Single global slot:** `HasActiveJobAsync` becomes "any `Job` row with `Status` in
  `{Enumerating, Enriching}`," regardless of `Kind`. A Discovery job blocks a Scan job and
  vice versa — confirmed acceptable for this spec; true independent lanes (the source document's
  "discovery has priority over scans") is not built here.
- **Crash recovery:** `FailInterruptedJobsAsync` already just flips any row stuck in
  `Enumerating`/`Enriching` to `Failed` with "Interrupted by an application restart." on startup;
  this now covers Discovery jobs for free.

## 7. Migration

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

## 8. Testing

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
- **API tests** for `POST /api/discovery` and `GET /api/discovery/{id}/events`, mirroring
  `ScanEndpointsTests`'s structure (happy path, unknown/invalid `folderId` → `400`, active job →
  `409`, unknown job id on the events route → `error` event, wrong-`Kind` job id → `404`).
- Existing `ScanService`/`ScanEndpoints`/`ScanJobRepository` tests are updated for the rename
  (`ScanJob` → `Job`, `RootId` → resolved `FolderId`) with their actual scan assertions otherwise
  unchanged — this is the regression net that the rename didn't change scan behavior.
- `ImageRootSeederTests` gains cases: seeding a root (new, pre-existing, or inactive) always
  results in a top-level `Folder` row with `ChildrenDiscoveredAt == null`, and re-running the
  seeder doesn't create a duplicate.

## Out of scope (tracked for later sub-projects)

- Bounded-parallel discovery workers.
- True mid-walk crash resume.
- Folder-scoped image scanning (`POST /api/scans` accepting `folderId` directly, folder-unit
  cancel/pause/resume, batch commits).
- A real job queue: persisted queueing, pause, cancel, reorder, dedup of overlapping recursive
  jobs. Today (and after this spec) it remains strictly "one job running, everything else
  refused" — no queueing.
- The Scan Queue admin page (frontend).
- Folder tree status icons, right-click "Scan folder / Scan folder + subfolders" menu, "not
  discovered yet" badge, manual "Refresh structure" button (frontend + the `ScanStatus`/
  `IsExcluded`/display fields that page needs).
- Exclusion-rule editing UI (the existing Settings page already edits
  `AppSettings.ExcludedFolderNames`; this spec only makes discovery respect it).
