# PictureManager – Folder Discovery & Scanning Design

## 1. Two Separate Concerns

| Concern | What it does | Cost | Trigger |
|---|---|---|---|
| **Folder discovery** | Walks directory names only, builds the folder tree | Cheap | Automatic on root add / manual refresh |
| **Image scanning** | Enumerates and processes files | Expensive | User-chosen scope |

Both run as jobs through the same background worker infrastructure.

---

## 2. Folder Discovery

**Decision:** full eager discovery from registered roots. No lazy discovery on folder expand.

### Flow
1. User registers a NAS root → a `Folder` row with `ParentFolderId = null`.
2. A `DiscoverFolders` job is queued with **priority over scan jobs** (or its own worker lane), so it never waits behind a long image scan.
3. Discovered folders are pushed to the UI; the tree is usable immediately and fills in top-down.
4. Root node shows progress: *"Discovering… 1,240 folders found"*.

### Implementation guidelines
- **Breadth-first walk with bounded parallelism** (4–8 workers, configurable). SMB enumeration is latency-bound, so parallelism helps significantly.
- **Cheap enumeration** – directories only
- **Parallel breadth-first worker loop**
- **Store `LastWriteTimeUtc`** (free from the listing) for the later "possibly outdated" hint.
- **Skip reparse points** – protects against symlink loops.
- **Do not descend into excluded folders.** Default exclusion patterns:
  `$RECYCLE.BIN`, `System Volume Information`, `@eaDir`, `#recycle`, `.snapshot`
- **Resumable:** `Folder.ChildrenDiscoveredAt` (null = not walked yet). After restart, reseed the queue with non-excluded folders where `ChildrenDiscoveredAt IS NULL`.

### Refresh (same job, diff mode)
| Case | Action |
|---|---|
| New directory | Insert `Folder` |
| Directory no longer found | `IsMissing = true` (no hard delete) |
| Missing directory reappears | Clear `IsMissing` |

Used for first discovery, manual *"Refresh structure"* per root/node.

### Startup
- Load the tree from PostgreSQL — no NAS walk on app start.

---

## 3. Scanning

### Entry points (decided)

**Context menu / scan icon on a folder:**
```
Scan
 ├── Scan folder
 └── Scan folder + subfolders
```

### Recursive scan execution
1. Process the target folder's own files, commit in batches (e.g. 500).
2. Push each subfolder as a separate unit onto an internal work stack.
3. Process one folder at a time; after each, update `FoldersProcessed` and `LastProcessedPath`.
4. Never use `SearchOption.AllDirectories` on the whole subtree.

→ Cancel, pause and resume happen cleanly between folder units.

---

## 4. Scan Queue Page

```
┌──────────────────────────────────────────────────────────┐
│ Scan Queue                              [Pause queue]    │
├──────────────────────────────────────────────────────────┤
│ 2025/Madeira    Running  Folders 34/120 · 8,532 files  ✕ │
│ 2025/Istanbul   Waiting                          ↑ ↓   ✕ │
│ 2024/Italy      Waiting                          ↑ ↓   ✕ │
├──────────────────────────────────────────────────────────┤
│ History                                                  │
│ 2024/Rome       Completed  +1,204 / ~32 / −3   12m 41s   │
│ 2023/Paris      Failed     Access denied (details)       │
└──────────────────────────────────────────────────────────┘
```

- **Global** "Pause queue"; **per-row** cancel and reorder.
- **Progress:** folder count + running file count instead of an expensive total-file pre-count.
- **History section:** completed / failed / cancelled jobs with added/updated/missing counts, duration, error details.
- **Deduplication:** reject or merge jobs already covered by a queued ancestor recursive scan.
- **Pause semantics:** pause after the current folder unit finishes.
- **Persistence:** queue lives in the DB and survives restarts.

---

## 5. Folder Status Icons

### Decided
| Icon | Meaning |
|---|---|
| ✓ | Scanned (tooltip: last scan date + file count) |
| — | Never scanned |
| ↻ | Scanning |
| ! | Scan error |
| X | Excluded from scan |

### Suggested additions
| State | Why |
|---|---|
| ⧗ Queued | Distinguish waiting folders |
| Partial / incomplete | Scan cancelled or paused mid-way |
| Missing | Folder no longer on the NAS (greyed / struck-through) |
| Subtree indicator | "Contains unscanned subfolders" |
| Possibly outdated | `LastWriteTimeUtc` > `LastScannedAt` (hint only, not fully reliable) |

- Label ✓ as **"Scanned"** rather than "Synchronized".
- Use color + icon + tooltip for readability in a dense tree.

### Folder entity additions
```
Folder
- LastScannedAt
- LastScanFileCount
- ScanStatus            Idle | Queued | Scanning | Error | Partial
- IsExcluded
- IsMissing
- ChildrenDiscoveredAt
- LastWriteTimeUtc
```

### Exclusion rules (suggested)
- Excluding a parent excludes the whole subtree.
- Recursive scans skip excluded folders silently.
- A child cannot be re-included under an excluded parent.
