# Folder Discovery and Folder-Scoped Scanning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Separate folder discovery from image scanning: a unified `Job` table (Scan | Discovery), a directories-only discovery job with its admin API, every root's top folder created at startup, and image scans that can target any folder.

**Architecture:** `ScanJob` is renamed to `Job` and gains `Kind` and a `FolderId` scope; one "any job active" check keeps a single job running at a time. `DiscoveryService` mirrors `ScanService`'s queue-then-run shape (its own `IDiscoveryQueue` + `DiscoveryBackgroundService`) and walks directories only. `ScanService` gains a folder-target branch that reuses its existing walk, started from the chosen folder.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core 10.0.12 + Npgsql (Postgres 17), xUnit, FluentAssertions, NSubstitute.

**Spec:** `docs/superpowers/specs/2026-09-25-folder-discovery-design.md`

## Global Constraints

- Backend only. No change under `web/`.
- One job at a time, across both kinds: a new job of either kind is refused while any `Job` is `Enumerating` or `Enriching`.
- `JobKind` values in this order: `Scan` (0), `Discovery` (1), so a `Job` created without a `Kind` is a scan and existing rows read as scans.
- Always-excluded folder names, compared case-insensitively: `$RECYCLE.BIN`, `System Volume Information`, `@eaDir`, `#recycle`, `.snapshot`. Applied in addition to `AppSettings.ExcludedFolderNames`.
- Folder walks stop descending 50 levels below their starting folder (`ScanTargets.MaxFolderDepth = 50`); progress is written every 50 folders.
- Timestamps written to Postgres are truncated to microseconds first (`PostgresTimestamps.TruncateToMicroseconds`).
- Exact user-facing strings:
  - `"Give rootId or folderId, not both."`
  - `"Folder is no longer on disk: {root name}/{relative path}"`
  - `"Folder {id} does not exist, was removed from the collection, or its root is inactive."`
  - `"Folder {id} is missing on disk. Scan or discover its parent folder instead."`
  - `"A scan or folder discovery is already in progress."`
- `POST /api/scans` callers that send only `rootId`/`isRecursive` behave exactly as today; its response and its events payload (`Id, Status, FoldersScanned, FilesFound, FilesEnriched, ErrorMessage`) keep their field names.
- New endpoints go on the `admin` route group.
- Postgres-backed tests need the compose `db` service running (`docker compose up -d db`).

## Plan-level decisions (where the spec is silent or its example doesn't fit the code)

1. **The always-excluded list lives in `ScanExcludeRules`, so scanning skips and prunes those folders too.** Otherwise a scan would re-create `@eaDir` rows (and index the NAS's thumbnail JPEGs inside them) that the next discovery deletes, over and over.
2. **Discovery checks root availability the way scanning does.** An unmounted share looks like an empty folder, and walking it would mark the whole tree missing. Unavailable roots are skipped, the rest are discovered, and the job ends `Failed` with the existing "Root '…' is unavailable" message.
3. **Discovery payloads use the same JSON casing as scan events** (default `JsonSerializer`, PascalCase: `"Status":"Completed"`). The spec's camelCase example was illustrative.
4. **A wrong-kind job id on an events route behaves like an unknown id** (`event: error` / `data: not found`), because that's how the existing SSE route reports "not found". The spec's "404" means "not found on this route".
5. **Discovery has its own queue and background service**, mirroring the scan pair, instead of generalizing the scan queue. The one-job rule is still enforced at queue time by `HasActiveJobAsync`.
6. **Symlinks and junctions count as present on disk but are never inserted or followed.** So a row an earlier scan created for one isn't marked missing.
7. **Discovery can target a folder marked missing** (the spec refuses only unknown or removed folders). If its directory is back, discovery clears its mark; if not, the job fails with "Folder is no longer on disk".
8. **A discovery job completes straight from `Enumerating`** through a new `IJobRepository.TryMarkCompletedAsync`, so it never shows `Enriching`.
9. **The migration is EF-generated drop-and-recreate** (`ScanJobs` dropped, `Jobs` created), as approved. Existing scan history in the dev database is lost.

## Review Focus

Five inputs a person will meet that the spec implies but doesn't spell out. Each has a test in the task that owns the code:

1. **An unmounted share during an all-roots discovery** must not mark that root's tree missing; other roots are still discovered and the job fails with the unavailable message. → Task 5, `RunDiscoveryAsync_AllRoots_SkipsAnUnmountedRoot_AndFailsTheJob`.
2. **A folder whose name differs on disk only by case** (renamed `Madeira` → `madeira`) must match its existing row: no duplicate, not marked missing. → Task 5, `RunDiscoveryAsync_CaseOnlyDifference_MatchesTheExistingFolder`.
3. **NAS housekeeping folders** (`@eaDir` etc.) must be excluded by scanning and discovery alike, so the two walks never undo each other. → Task 5, `IsFolderExcluded_OsAndNasHousekeepingFolders_AreAlwaysExcluded`.
4. **A target folder deleted from disk after the job was queued** must fail the job and mark nothing. → Task 3, `RunScanAsync_FolderGoneFromDisk_FailsWithoutMarkingAnything`, and Task 5, `RunDiscoveryAsync_TargetFolderGoneBeforeRun_FailsWithoutMarkingAnything`.
5. **A root that has no top folder row yet** (created before the seeding rule) must get one during an all-roots discovery. → Task 5, `RunDiscoveryAsync_AllRoots_CreatesAMissingTopFolder`.

---

## File structure

**Model** (`src/PictureManager.Model/`)
- `Job.cs` (renamed from `ScanJob.cs`), `JobStatus.cs` (renamed from `ScanJobStatus.cs`), `JobKind.cs` (new), `Folder.cs` (two new fields).

**Application** (`src/PictureManager.Application/`)
- `Repositories/IJobRepository.cs` (renamed), plus `TryMarkCompletedAsync`.
- `Common/PostgresTimestamps.cs` (new, shared microsecond truncation).
- `Scanning/ScanTargets.cs` (new): resolves a visible folder and its root, checks root availability, and holds the depth cap.
- `Scanning/FolderUnavailableException.cs`, `Scanning/FolderNotOnDiskException.cs` (new).
- `Scanning/PathNormalizer.cs` (+`FolderNameKey`), `Scanning/ImagePathResolver.cs` (+`ResolveFolderPath`), `Scanning/ScanExcludeRules.cs` (always-excluded names).
- `Scanning/IScanService.cs`, `QueuedScan.cs`, `ScanService.cs`: folder-scoped scans.
- `Scanning/IDiscoveryService.cs`, `DiscoveryService.cs`, `QueuedDiscovery.cs`, `IDiscoveryQueue.cs` (new).
- `Roots/ImageRootSeeder.cs`: top folder for every root.
- `DependencyInjection/ApplicationServiceCollectionExtensions.cs`: `IDiscoveryService`.

**Infrastructure** (`src/PictureManager.Infrastructure/`)
- `Persistence/Configurations/JobConfiguration.cs` (renamed), `FolderConfiguration.cs`, `Persistence/Repositories/JobRepository.cs` (renamed), a new migration.

**Worker** (`src/PictureManager.Worker/`)
- `Scanning/ChannelDiscoveryQueue.cs`, `Scanning/DiscoveryBackgroundService.cs` (new); `Scanning/ScanBackgroundService.cs` (new expected failures); DI registration.

**Api** (`src/PictureManager.Api/`)
- `Endpoints/ScanEndpoints.cs` (`folderId`, kind check), `Endpoints/DiscoveryEndpoints.cs` (new), `Program.cs`.

---

### Task 1: Unify `ScanJob` into `Job` and add the folder discovery columns

**Files:**
- Rename: `src/PictureManager.Model/ScanJob.cs` → `Job.cs`, `ScanJobStatus.cs` → `JobStatus.cs`
- Create: `src/PictureManager.Model/JobKind.cs`
- Rename: `src/PictureManager.Application/Repositories/IScanJobRepository.cs` → `IJobRepository.cs`
- Rename: `src/PictureManager.Infrastructure/Persistence/Configurations/ScanJobConfiguration.cs` → `JobConfiguration.cs`
- Rename: `src/PictureManager.Infrastructure/Persistence/Repositories/ScanJobRepository.cs` → `JobRepository.cs`
- Modify: `src/PictureManager.Model/Folder.cs`, `src/PictureManager.Infrastructure/Persistence/Configurations/FolderConfiguration.cs`, every non-migration `.cs` file that references the renamed types (by script, Step 2)
- Create: `src/PictureManager.Infrastructure/Migrations/<timestamp>_UnifiedJobsAndFolderDiscovery.cs` (generated)
- Rename: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ScanJobRepositoryTests.cs` → `JobRepositoryTests.cs`
- Test: `JobRepositoryTests.cs`, `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FolderRepositoryTests.cs`

**Interfaces:**
- Produces: `Job { Id, Kind, FolderId?, IsRecursive, Status, StartedUtc, CompletedUtc?, FoldersProcessed, FilesFound, FilesEnriched, ErrorMessage?, Folder? }`; `enum JobKind { Scan, Discovery }`; `enum JobStatus` (same values as before); `IJobRepository` / `JobRepository` (same methods as before, same parameter names, including `SetEnumerationResultAsync(int scanJobId, int foldersScanned, int filesFound, …)`); `PictureManagerDbContext.Jobs`; `Folder.ChildrenDiscoveredAt`, `Folder.LastWriteTimeUtc` (both `DateTime?`).

- [ ] **Step 1: Rename the files with git**

```bash
git mv src/PictureManager.Model/ScanJob.cs src/PictureManager.Model/Job.cs
git mv src/PictureManager.Model/ScanJobStatus.cs src/PictureManager.Model/JobStatus.cs
git mv src/PictureManager.Application/Repositories/IScanJobRepository.cs src/PictureManager.Application/Repositories/IJobRepository.cs
git mv src/PictureManager.Infrastructure/Persistence/Configurations/ScanJobConfiguration.cs src/PictureManager.Infrastructure/Persistence/Configurations/JobConfiguration.cs
git mv src/PictureManager.Infrastructure/Persistence/Repositories/ScanJobRepository.cs src/PictureManager.Infrastructure/Persistence/Repositories/JobRepository.cs
git mv tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ScanJobRepositoryTests.cs tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/JobRepositoryTests.cs
```

- [ ] **Step 2: Rename the identifiers in every non-migration source and test file**

Word boundaries keep `ScanJobId` (a property name that stays) and test method names intact. The existing migrations are not touched.

```bash
files=$(git ls-files 'src/*.cs' 'tests/*.cs' | grep -v '/Migrations/')
sed -i -E \
  -e 's/\bIScanJobRepository\b/IJobRepository/g' \
  -e 's/\bScanJobRepositoryTests\b/JobRepositoryTests/g' \
  -e 's/\bScanJobRepository\b/JobRepository/g' \
  -e 's/\bScanJobConfiguration\b/JobConfiguration/g' \
  -e 's/\bScanJobStatus\b/JobStatus/g' \
  -e 's/\bScanJobs\b/Jobs/g' \
  -e 's/\bScanJob\b/Job/g' \
  -e 's/\.FoldersScanned\b/.FoldersProcessed/g' \
  -e 's/\bFoldersScanned = /FoldersProcessed = /g' \
  $files
```

Check: `git grep -n "ScanJob\b\|IScanJobRepository\|ScanJobStatus" -- 'src/*.cs' 'tests/*.cs' ':!*/Migrations/*'` prints nothing. `git grep -n "FoldersScanned" -- 'src/*.cs' ':!*/Migrations/*'` shows only the `ScanProgress` record's `int FoldersScanned` parameter (it stays: it's the scan events' JSON name), the old `Job.cs` property declaration (Step 5 replaces it), and two `IJobRepository` doc comments. Change those two comments to say `FoldersProcessed`.

- [ ] **Step 3: Write the failing tests**

Append to `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/JobRepositoryTests.cs`, inside the class:

```csharp
    [Fact]
    public void NewJob_DefaultsToScanKind()
    {
        new Job().Kind.Should().Be(JobKind.Scan);
    }

    [Fact]
    public async Task HasActiveJobAsync_CountsJobsOfEitherKind()
    {
        await using var context = CreateContext();
        var repository = new JobRepository(context);

        await repository.AddAsync(new Job { Kind = JobKind.Discovery, Status = JobStatus.Enumerating, StartedUtc = DateTime.UtcNow });

        (await repository.HasActiveJobAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task AddAsync_RoundTripsKindFolderIdAndFoldersProcessed()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("jobs");
        var top = TestData.Folder(root, "");
        db.Context.Folders.Add(top);
        await db.Context.SaveChangesAsync();

        var repository = new JobRepository(db.Context);
        var job = await repository.AddAsync(new Job
        {
            Kind = JobKind.Discovery,
            FolderId = top.Id,
            IsRecursive = true,
            Status = JobStatus.Enumerating,
            StartedUtc = TestData.Utc
        });
        await repository.SetEnumerationResultAsync(job.Id, foldersScanned: 4, filesFound: 0);

        await using var verify = db.CreateContext();
        var fetched = await new JobRepository(verify).GetByIdAsync(job.Id);
        fetched!.Kind.Should().Be(JobKind.Discovery);
        fetched.FolderId.Should().Be(top.Id);
        fetched.FoldersProcessed.Should().Be(4);
    }

    [Fact]
    public async Task FailActiveJobsAsync_FailsAnInterruptedDiscoveryJobToo()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var discovery = new Job { Kind = JobKind.Discovery, Status = JobStatus.Enumerating, StartedUtc = TestData.Utc };
        db.Context.Jobs.Add(discovery);
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            (await new JobRepository(context).FailActiveJobsAsync("Interrupted by an application restart.", TestData.Utc)).Should().Be(1);

        await using var verify = db.CreateContext();
        (await verify.Jobs.AsNoTracking().SingleAsync(j => j.Id == discovery.Id)).Status.Should().Be(JobStatus.Failed);
    }
```

Append to `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FolderRepositoryTests.cs`, inside the class (add `using PictureManager.Tests.Support;` and `using Microsoft.EntityFrameworkCore;` at the top if they aren't there):

```csharp
    [Fact]
    public async Task DiscoveryTimestamps_RoundTripThroughPostgres()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("discovery-columns");
        var top = TestData.Folder(root, "");
        top.ChildrenDiscoveredAt = TestData.Utc;
        top.LastWriteTimeUtc = TestData.Utc.AddDays(-1);
        db.Context.Folders.Add(top);
        await db.Context.SaveChangesAsync();

        await using var verify = db.CreateContext();
        var fetched = await verify.Folders.AsNoTracking().SingleAsync(f => f.Id == top.Id);
        fetched.ChildrenDiscoveredAt.Should().Be(TestData.Utc);
        fetched.LastWriteTimeUtc.Should().Be(TestData.Utc.AddDays(-1));
    }
```

- [ ] **Step 4: Build to confirm the failure**

Run: `dotnet build`
Expected: FAIL: `JobKind` doesn't exist, `Job` has no `Kind`/`FolderId`/`FoldersProcessed`, and `Folder` has no `ChildrenDiscoveredAt`/`LastWriteTimeUtc`.

- [ ] **Step 5: Implement the model and configuration changes**

Replace `src/PictureManager.Model/Job.cs`:

```csharp
using System;

namespace PictureManager.Model;

/// <summary>A background job: an image scan or a folder discovery. Only one runs at a time.</summary>
public class Job
{
    public int Id { get; set; }
    public JobKind Kind { get; set; }

    /// <summary>
    /// Scope: null = all active roots. Otherwise the folder the job starts from: a root's top folder for a
    /// whole-root job, or any subfolder.
    /// </summary>
    public int? FolderId { get; set; }
    public bool IsRecursive { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }

    /// <summary>Folders visited so far, for both kinds.</summary>
    public int FoldersProcessed { get; set; }

    /// <summary>Scan only; always 0 for a discovery job.</summary>
    public int FilesFound { get; set; }

    /// <summary>Scan only; always 0 for a discovery job.</summary>
    public int FilesEnriched { get; set; }
    public string? ErrorMessage { get; set; }
    public Folder? Folder { get; set; }
}
```

Create `src/PictureManager.Model/JobKind.cs`:

```csharp
namespace PictureManager.Model;

/// <summary>Scan first: a job created without a kind, and every row from before this column existed, is a scan.</summary>
public enum JobKind
{
    Scan,
    Discovery
}
```

In `src/PictureManager.Model/JobStatus.cs`, put this comment on the enum (values unchanged):

```csharp
/// <summary>
/// Enumerating = walking (folder names for Discovery; folders and files for Scan). Enriching is Scan only; a
/// discovery goes from Enumerating straight to a terminal status.
/// </summary>
public enum JobStatus
```

In `src/PictureManager.Model/Folder.cs`, add after `MissingSinceUtc`:

```csharp
    /// <summary>When a discovery last diffed this folder's subfolders; null = never discovered.</summary>
    public DateTime? ChildrenDiscoveredAt { get; set; }

    /// <summary>The directory's last-write time as of the last discovery; for a later "possibly outdated" hint.</summary>
    public DateTime? LastWriteTimeUtc { get; set; }
```

Replace `src/PictureManager.Infrastructure/Persistence/Configurations/JobConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("Jobs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(4000);

        builder.Property(x => x.StartedUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.CompletedUtc)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(x => x.Folder)
            .WithMany()
            .HasForeignKey(x => x.FolderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.Status);
    }
}
```

In `src/PictureManager.Infrastructure/Persistence/Configurations/FolderConfiguration.cs`, add after the `MissingSinceUtc` property configuration:

```csharp
        builder.Property(x => x.ChildrenDiscoveredAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.LastWriteTimeUtc)
            .HasColumnType("timestamp with time zone");
```

Run: `dotnet build`
Expected: build succeeds.

- [ ] **Step 6: Generate the migration**

```bash
dotnet ef migrations add UnifiedJobsAndFolderDiscovery --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api
```

Open the generated `…_UnifiedJobsAndFolderDiscovery.cs` and check its `Up`: it drops `ScanJobs`, creates `Jobs` with `Kind`, `FolderId` (FK to `Folders`, `ReferentialAction.SetNull`), `FoldersProcessed`, `FilesFound`, `FilesEnriched`, `IsRecursive`, `Status`, `StartedUtc`, `CompletedUtc`, `ErrorMessage`, plus indexes on `FolderId` and `Status`; and it adds nullable `ChildrenDiscoveredAt` and `LastWriteTimeUtc` (`timestamp with time zone`) to `Folders`. If EF generated a `RenameTable` instead of drop/create, that's equally fine. Anything else (for example a change to an unrelated table) means the model changed by accident: stop and find out why.

- [ ] **Step 7: Run the whole suite**

Run: `docker ps` (the `db` container must be up; if not, `docker compose up -d db`), then `dotnet test`
Expected: all backend tests pass, including the five new ones. This run is also the regression net for the rename.

- [ ] **Step 8: Commit**

```bash
git add -A src tests
git commit -m "refactor: unify ScanJob into Job with a kind and folder scope; add folder discovery columns"
```

(`git add -A src tests` stages the renames. Check `git status` first: nothing outside `src/` and `tests/` should be staged, and untracked files such as `web/src/theme.ts` must stay unstaged.)

---

### Task 2: Every root gets its top folder at startup

**Files:**
- Modify: `src/PictureManager.Application/Roots/ImageRootSeeder.cs`
- Test: `tests/PictureManager.Application.Tests/Roots/ImageRootSeederTests.cs`

**Interfaces:**
- Consumes: `IFolderRepository.GetByRootAndRelativePathAsync(int rootId, string relativePath, …)`, `IFolderRepository.AddAsync(Folder, …)`.
- Produces: `ImageRootSeeder(IImageRootRepository roots, IFolderRepository folders, IClock clock, ImageRootsOptions options, ILogger<ImageRootSeeder> logger)`. DI resolves the new parameter; nothing else constructs the seeder.

- [ ] **Step 1: Write the failing tests**

In `ImageRootSeederTests.cs`, add the field and update the helper (add `using PictureManager.Model;` if missing; it's already there):

```csharp
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
```

```csharp
    private Task SeedAsync(params ImageRootConfigEntry[] entries) =>
        new ImageRootSeeder(_roots, _folders, _clock, new ImageRootsOptions { Entries = new List<ImageRootConfigEntry>(entries) },
            NullLogger<ImageRootSeeder>.Instance).SeedAsync();
```

Add these tests:

```csharp
    [Fact]
    public async Task SeedAsync_NewRoot_CreatesItsTopFolder_NotYetDiscovered()
    {
        _roots.AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var root = call.Arg<ImageRoot>();
            root.Id = 5;
            return root;
        });

        await SeedAsync(Entry("nas-photos", "/images/photos"));

        await _folders.Received(1).AddAsync(
            Arg.Is<Folder>(f => f.RootId == 5 && f.ParentId == null && f.RelativePath == "" && f.Name == "nas-photos"
                                && f.ChildrenDiscoveredAt == null && f.CreatedUtc == _clock.UtcNow),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_ExistingAndInactiveRootsWithoutTopFolder_GetOne()
    {
        _existing.Add(new ImageRoot { Id = 1, Name = "active", MountPath = "/a", IsActive = true });
        _existing.Add(new ImageRoot { Id = 2, Name = "off", MountPath = "/b", IsActive = false });

        await SeedAsync(Entry("active", "/a"), Entry("off", "/b"));

        await _folders.Received(1).AddAsync(Arg.Is<Folder>(f => f.RootId == 1 && f.Name == "active"), Arg.Any<CancellationToken>());
        await _folders.Received(1).AddAsync(Arg.Is<Folder>(f => f.RootId == 2 && f.Name == "off"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_RootThatHasItsTopFolder_CreatesNoDuplicate()
    {
        _existing.Add(new ImageRoot { Id = 1, Name = "dev", MountPath = "/a", IsActive = true });
        _folders.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>())
            .Returns(new Folder { Id = 10, RootId = 1, Name = "dev", RelativePath = string.Empty });

        await SeedAsync(Entry("dev", "/a"));

        await _folders.DidNotReceive().AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 2: Run the tests to confirm the failure**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~ImageRootSeederTests"`
Expected: FAIL to compile: `ImageRootSeeder` has no constructor that takes an `IFolderRepository`.

- [ ] **Step 3: Implement**

In `src/PictureManager.Application/Roots/ImageRootSeeder.cs`, add the field and constructor parameter:

```csharp
    private readonly IImageRootRepository _roots;
    private readonly IFolderRepository _folders;
    private readonly IClock _clock;
    private readonly ImageRootsOptions _options;
    private readonly ILogger<ImageRootSeeder> _logger;

    public ImageRootSeeder(IImageRootRepository roots, IFolderRepository folders, IClock clock, ImageRootsOptions options, ILogger<ImageRootSeeder> logger)
    {
        _roots = roots;
        _folders = folders;
        _clock = clock;
        _options = options;
        _logger = logger;
    }
```

In `SeedAsync`, between the `foreach (var entry in _options.Entries)` loop and the "not in the ImageRoots configuration" warning loop, add:

```csharp
        // Every root gets its top folder now, so it shows in the tree (not yet discovered) before any discovery
        // or scan. Inactive roots too: their folders are hidden anyway, and re-activating one then needs no restart.
        foreach (var root in roots)
        {
            if (await _folders.GetByRootAndRelativePathAsync(root.Id, string.Empty, cancellationToken) is not null)
                continue;

            await _folders.AddAsync(new Folder
            {
                RootId = root.Id,
                ParentId = null,
                Name = root.Name,
                RelativePath = string.Empty,
                CreatedUtc = _clock.UtcNow,
                ModifiedUtc = _clock.UtcNow
            }, cancellationToken);
        }
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~ImageRootSeederTests"`
Expected: PASS, including the existing seeder tests.

- [ ] **Step 5: Run the whole suite and commit**

Run: `dotnet test`
Expected: all pass.

```bash
git add src/PictureManager.Application/Roots/ImageRootSeeder.cs tests/PictureManager.Application.Tests/Roots/ImageRootSeederTests.cs
git commit -m "feat: create every root's top folder at startup"
```

---

### Task 3: Image scans can target any folder

**Files:**
- Create: `src/PictureManager.Application/Common/PostgresTimestamps.cs`, `src/PictureManager.Application/Scanning/ScanTargets.cs`, `FolderUnavailableException.cs`, `FolderNotOnDiskException.cs`
- Modify: `src/PictureManager.Application/Scanning/PathNormalizer.cs`, `ImagePathResolver.cs`, `QueuedScan.cs`, `IScanService.cs`, `ScanService.cs` (full replacement), `src/PictureManager.Worker/Scanning/ScanBackgroundService.cs`, `src/PictureManager.Api/Endpoints/ScanEndpoints.cs` (one call)
- Test: `tests/PictureManager.Application.Tests/Scanning/ScanServiceFolderScopeTests.cs` (new), `ScanServiceTestExtensions.cs`, `ScanServiceQueueTests.cs`, `PathNormalizerTests.cs`, `ImagePathResolverTests.cs`, `tests/PictureManager.Worker.Tests/Scanning/ScanBackgroundServiceTests.cs`, `tests/PictureManager.Api.Tests/Endpoints/ScanEndpointsTests.cs`

**Interfaces:**
- Consumes: `Job`, `JobKind`, `JobStatus`, `IJobRepository` (Task 1).
- Produces (Task 5 and Task 7 use these):
  - `PostgresTimestamps.TruncateToMicroseconds(DateTime) → DateTime`
  - `PathNormalizer.FolderNameKey(string) → string`
  - `ImagePathResolver.ResolveFolderPath(string mountPath, string relativeFolderPath) → string`
  - `ScanTargets.MaxFolderDepth` (`const int` = 50), `ScanTargets.IsRootAvailable(string mountPath) → bool`, `ScanTargets.GetVisibleFolderAsync(IFolderRepository, IImageRootRepository, int folderId, CancellationToken) → Task<(ImageRoot Root, Folder Folder)>` (throws `FolderUnavailableException`)
  - `FolderUnavailableException` with `int FolderId`, `static NotFound(int folderId)`, `static Missing(int folderId)`
  - `FolderNotOnDiskException(string rootName, string relativePath)`
  - `QueuedScan(int ScanJobId, int? RootId, int? FolderId, bool IsRecursive)`
  - `IScanService.QueueScanAsync(int? rootId, int? folderId, bool isRecursive, CancellationToken)`

- [ ] **Step 1: Write the failing tests**

Update `tests/PictureManager.Application.Tests/Scanning/ScanServiceTestExtensions.cs`:

```csharp
using System.Threading.Tasks;
using PictureManager.Application.Scanning;

namespace PictureManager.Application.Tests.Scanning;

internal static class ScanServiceTestExtensions
{
    /// <summary>Queue + run in one call: what ScanBackgroundService does, minus the queue hop.</summary>
    public static async Task<int> ScanNowAsync(this ScanService scanService, int? rootId, bool isRecursive)
    {
        var scanJobId = await scanService.QueueScanAsync(rootId, folderId: null, isRecursive);
        await scanService.RunScanAsync(new QueuedScan(scanJobId, rootId, null, isRecursive));
        return scanJobId;
    }

    /// <summary>Same as ScanNowAsync, for a folder-scoped scan.</summary>
    public static async Task<int> ScanFolderNowAsync(this ScanService scanService, int folderId, bool isRecursive)
    {
        var scanJobId = await scanService.QueueScanAsync(rootId: null, folderId, isRecursive);
        await scanService.RunScanAsync(new QueuedScan(scanJobId, null, folderId, isRecursive));
        return scanJobId;
    }
}
```

In `ScanServiceQueueTests.cs`, update the existing calls:
- `QueueScanAsync(rootId: 1, isRecursive: true)` → `QueueScanAsync(rootId: 1, folderId: null, isRecursive: true)` (both places)
- `new QueuedScan(999, 1, true)` → `new QueuedScan(999, 1, null, true)` (both places)
- In `QueueScanAsync_CreatesEnumeratingJob_EnqueuesIt_AndDoesNotWalk`, replace the line
  `await _folders.DidNotReceive().GetByRootAndRelativePathAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());`
  with the following. The root's top folder is now looked up once to record the job's scope; "does not walk" means no folder or file work.
  ```csharp
        await _folders.DidNotReceive().GetChildrenAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>());
        await _images.DidNotReceive().GetByFolderIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
  ```

In `tests/PictureManager.Worker.Tests/Scanning/ScanBackgroundServiceTests.cs`, give every `QueuedScan` its new `FolderId`:

```bash
sed -i -E 's/new QueuedScan\(([0-9]+), (null|[0-9]+), (true|false)\)/new QueuedScan(\1, \2, null, \3)/g' tests/PictureManager.Worker.Tests/Scanning/ScanBackgroundServiceTests.cs
```

In `tests/PictureManager.Api.Tests/Endpoints/ScanEndpointsTests.cs`, update the two stubs: `QueueScanAsync(1, true, …)` → `QueueScanAsync(1, null, true, …)` and `QueueScanAsync(2, true, …)` → `QueueScanAsync(2, null, true, …)`.

Append to `tests/PictureManager.Application.Tests/Scanning/PathNormalizerTests.cs`, inside the class:

```csharp
    [Fact]
    public void FolderNameKey_FoldsCaseAndUnicodeNormalization()
    {
        PathNormalizer.FolderNameKey("Café").Should().Be(PathNormalizer.FolderNameKey("CAFÉ"));
    }
```

Append to `tests/PictureManager.Application.Tests/Scanning/ImagePathResolverTests.cs`, inside the class (add `using System.IO;` if missing):

```csharp
    [Fact]
    public void ResolveFolderPath_TopFolder_IsTheMountPath()
    {
        ImagePathResolver.ResolveFolderPath("/mnt/dev", "").Should().Be("/mnt/dev");
    }

    [Fact]
    public void ResolveFolderPath_Subfolder_UsesTheOsSeparator()
    {
        ImagePathResolver.ResolveFolderPath("/mnt/dev", "Trips/Madeira")
            .Should().Be(Path.Combine("/mnt/dev", "Trips", "Madeira"));
    }
```

Add to `ScanBackgroundServiceTests.cs`, inside the class:

```csharp
    [Fact]
    public async Task ScanBackgroundService_ExpectedTargetFailure_DoesNotRetryMarkingTheJobFailed()
    {
        var queue = new ChannelScanQueue();
        var scanService = Substitute.For<IScanService>();
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scanService.RunScanAsync(new QueuedScan(8, null, 20, true), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                ran.SetResult();
                return Task.FromException(new FolderNotOnDiskException("dev", "Trips"));
            });

        var jobs = Substitute.For<IJobRepository>();
        var provider = new ServiceCollection().AddScoped(_ => scanService).AddScoped(_ => jobs).BuildServiceProvider();
        var service = new ScanBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IClock>(), NullLogger<ScanBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(new QueuedScan(8, null, 20, true));
        await ran.Task.WaitAsync(WaitTimeout);
        await service.StopAsync(CancellationToken.None);

        await jobs.DidNotReceive().FailActiveJobsAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
```

Create `tests/PictureManager.Application.Tests/Scanning/ScanServiceFolderScopeTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public sealed class ScanServiceFolderScopeTests : IDisposable
{
    private readonly DirectoryInfo _tempRoot = Directory.CreateTempSubdirectory("pm-scan-folder-");
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly IImageRepository _images = Substitute.For<IImageRepository>();
    private readonly IAppSettingsRepository _settings = Substitute.For<IAppSettingsRepository>();
    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly IScanQueue _scanQueue = Substitute.For<IScanQueue>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ImageRoot _root;
    private readonly Folder _top = new() { Id = 10, RootId = 1, Name = "dev", RelativePath = string.Empty };
    private readonly Folder _trips = new() { Id = 20, RootId = 1, ParentId = 10, Name = "Trips", RelativePath = "Trips" };
    private readonly Folder _madeira = new() { Id = 21, RootId = 1, ParentId = 20, Name = "Madeira", RelativePath = "Trips/Madeira" };
    private int _nextImageId = 100;

    // dev/
    //   Trips/a.jpg
    //   Trips/Madeira/b.jpg
    //   Other/c.jpg        <- a sibling the folder scan must not touch
    public ScanServiceFolderScopeTests()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Trips", "Madeira"));
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Other"));
        File.WriteAllBytes(Path.Combine(_tempRoot.FullName, "Trips", "a.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(_tempRoot.FullName, "Trips", "Madeira", "b.jpg"), new byte[] { 2 });
        File.WriteAllBytes(Path.Combine(_tempRoot.FullName, "Other", "c.jpg"), new byte[] { 3 });

        _root = new ImageRoot { Id = 1, Name = "dev", MountPath = _tempRoot.FullName, IsActive = true };
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _roots.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(_root);
        _folders.GetByIdAsync(20, Arg.Any<CancellationToken>()).Returns(_trips);
        _folders.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>()).Returns(_top);
        _folders.GetByRootAndRelativePathAsync(1, "Trips/Madeira", Arg.Any<CancellationToken>()).Returns(_madeira);
        _folders.GetChildrenAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(new List<Folder>());
        _folders.GetChildrenAsync(20, Arg.Any<CancellationToken>()).Returns(new List<Folder> { _madeira });
        _images.GetByFolderIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<Image>());
        _images.AddAsync(Arg.Any<Image>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var image = call.Arg<Image>();
            image.Id = _nextImageId++;
            return image;
        });
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings());
        _jobs.AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var job = call.Arg<Job>();
            job.Id = 999;
            return job;
        });
        _jobs.TryTransitionToEnrichingAsync(999, Arg.Any<CancellationToken>()).Returns(true);
    }

    public void Dispose() => _tempRoot.Delete(recursive: true);

    private ScanService CreateService() =>
        new(_roots, _folders, _images, _settings, _jobs, Substitute.For<IEnrichmentQueue>(), _scanQueue, _clock, new ScanningOptions());

    [Fact]
    public async Task FolderScan_Recursive_ReconcilesOnlyThatSubtree()
    {
        await CreateService().ScanFolderNowAsync(folderId: 20, isRecursive: true);

        await _images.Received(1).AddAsync(Arg.Is<Image>(i => i.FolderId == 20 && i.FileName == "a"), Arg.Any<CancellationToken>());
        await _images.Received(1).AddAsync(Arg.Is<Image>(i => i.FolderId == 21 && i.FileName == "b"), Arg.Any<CancellationToken>());
        await _images.DidNotReceive().AddAsync(Arg.Is<Image>(i => i.FileName == "c"), Arg.Any<CancellationToken>());
        await _images.DidNotReceive().GetByFolderIdAsync(10, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetEnumerationResultAsync(999, foldersScanned: 2, filesFound: 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FolderScan_NonRecursive_ProcessesOnlyItsOwnFiles()
    {
        await CreateService().ScanFolderNowAsync(folderId: 20, isRecursive: false);

        await _images.Received(1).AddAsync(Arg.Any<Image>(), Arg.Any<CancellationToken>());
        await _images.Received(1).AddAsync(Arg.Is<Image>(i => i.FolderId == 20 && i.FileName == "a"), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetEnumerationResultAsync(999, foldersScanned: 1, filesFound: 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueueScanAsync_Folder_StoresItAsTheJobScope()
    {
        await CreateService().QueueScanAsync(rootId: null, folderId: 20, isRecursive: true);

        await _jobs.Received(1).AddAsync(
            Arg.Is<Job>(j => j.Kind == JobKind.Scan && j.FolderId == 20 && j.IsRecursive && j.Status == JobStatus.Enumerating),
            Arg.Any<CancellationToken>());
        _scanQueue.Received(1).Enqueue(new QueuedScan(999, null, 20, true));
    }

    [Fact]
    public async Task QueueScanAsync_Root_StoresItsTopFolderAsTheJobScope()
    {
        await CreateService().QueueScanAsync(rootId: 1, folderId: null, isRecursive: true);

        await _jobs.Received(1).AddAsync(Arg.Is<Job>(j => j.Kind == JobKind.Scan && j.FolderId == 10), Arg.Any<CancellationToken>());
        _scanQueue.Received(1).Enqueue(new QueuedScan(999, 1, null, true));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("removed")]
    [InlineData("inactive root")]
    public async Task QueueScanAsync_FolderNotVisible_Throws_AndCreatesNoJob(string reason)
    {
        switch (reason)
        {
            case "unknown":
                _folders.GetByIdAsync(20, Arg.Any<CancellationToken>()).Returns((Folder?)null);
                break;
            case "removed":
                _trips.IsActive = false;
                break;
            case "inactive root":
                _root.IsActive = false;
                break;
        }

        var act = () => CreateService().QueueScanAsync(rootId: null, folderId: 20, isRecursive: true);

        (await act.Should().ThrowAsync<FolderUnavailableException>()).Which.FolderId.Should().Be(20);
        await _jobs.DidNotReceive().AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueueScanAsync_FolderMarkedMissing_Throws()
    {
        _trips.MissingSinceUtc = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        var act = () => CreateService().QueueScanAsync(rootId: null, folderId: 20, isRecursive: true);

        (await act.Should().ThrowAsync<FolderUnavailableException>())
            .WithMessage("Folder 20 is missing on disk. Scan or discover its parent folder instead.");
    }

    [Fact]
    public async Task RunScanAsync_FolderRemovedWhileQueued_FailsTheJob()
    {
        _trips.IsActive = false;

        var act = () => CreateService().RunScanAsync(new QueuedScan(999, null, 20, true));

        await act.Should().ThrowAsync<FolderUnavailableException>();
        await _jobs.Received(1).SetFailureResultAsync(999, 0, 0,
            "Folder 20 does not exist, was removed from the collection, or its root is inactive.",
            JobStatus.Failed, _clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunScanAsync_FolderRootUnavailable_FailsTheJobAndChangesNothing()
    {
        _root.MountPath = Path.Combine(_tempRoot.FullName, "not-mounted");

        var act = () => CreateService().RunScanAsync(new QueuedScan(999, null, 20, true));

        await act.Should().ThrowAsync<ScanRootsUnavailableException>();
        await _images.DidNotReceive().AddAsync(Arg.Any<Image>(), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetFailureResultAsync(999, 0, 0, Arg.Is<string>(m => m.Contains("Root 'dev' is unavailable")),
            JobStatus.Failed, _clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunScanAsync_FolderGoneFromDisk_FailsWithoutMarkingAnything()
    {
        Directory.Delete(Path.Combine(_tempRoot.FullName, "Trips"), recursive: true);

        var act = () => CreateService().RunScanAsync(new QueuedScan(999, null, 20, true));

        await act.Should().ThrowAsync<FolderNotOnDiskException>().WithMessage("Folder is no longer on disk: dev/Trips");
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _images.DidNotReceive().UpdateAsync(Arg.Any<Image>(), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetFailureResultAsync(999, 0, 0, "Folder is no longer on disk: dev/Trips",
            JobStatus.Failed, _clock.UtcNow, Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run the tests to confirm the failure**

Run: `dotnet build`
Expected: FAIL: `QueuedScan` has 3 parameters, `QueueScanAsync` has no `folderId`, and `FolderUnavailableException`, `FolderNotOnDiskException`, `PathNormalizer.FolderNameKey` and `ImagePathResolver.ResolveFolderPath` don't exist.

- [ ] **Step 3: Add the shared helpers and exceptions**

Create `src/PictureManager.Application/Common/PostgresTimestamps.cs`:

```csharp
using System;

namespace PictureManager.Application.Common;

public static class PostgresTimestamps
{
    // Postgres timestamptz stores microsecond precision; file-system times carry 100ns ticks. Truncate before
    // storing or comparing, so a round-tripped value compares equal to itself next time.
    public static DateTime TruncateToMicroseconds(DateTime value) => new(value.Ticks - (value.Ticks % 10), value.Kind);
}
```

In `src/PictureManager.Application/Scanning/PathNormalizer.cs`, add:

```csharp
    /// <summary>Key for comparing folder names the way the scanner and discovery do: NFC, case-insensitive.</summary>
    public static string FolderNameKey(string name) => Normalize(name).ToLowerInvariant();
```

In `src/PictureManager.Application/Scanning/ImagePathResolver.cs`, add:

```csharp
    public static string ResolveFolderPath(string mountPath, string relativeFolderPath) =>
        string.IsNullOrEmpty(relativeFolderPath)
            ? mountPath
            : Path.Combine(mountPath, relativeFolderPath.Replace('/', Path.DirectorySeparatorChar));
```

Create `src/PictureManager.Application/Scanning/FolderUnavailableException.cs`:

```csharp
using System;

namespace PictureManager.Application.Scanning;

/// <summary>A folder requested as a job's target can't be used: unknown, removed, on an inactive root, or missing.</summary>
public sealed class FolderUnavailableException : Exception
{
    private FolderUnavailableException(int folderId, string message)
        : base(message)
    {
        FolderId = folderId;
    }

    public int FolderId { get; }

    public static FolderUnavailableException NotFound(int folderId) =>
        new(folderId, $"Folder {folderId} does not exist, was removed from the collection, or its root is inactive.");

    public static FolderUnavailableException Missing(int folderId) =>
        new(folderId, $"Folder {folderId} is missing on disk. Scan or discover its parent folder instead.");
}
```

Create `src/PictureManager.Application/Scanning/FolderNotOnDiskException.cs`:

```csharp
using System;

namespace PictureManager.Application.Scanning;

/// <summary>A job's target folder was gone from disk when the job ran. Nothing was changed.</summary>
public sealed class FolderNotOnDiskException : Exception
{
    public FolderNotOnDiskException(string rootName, string relativePath)
        : base($"Folder is no longer on disk: {rootName}/{relativePath}")
    {
    }
}
```

Create `src/PictureManager.Application/Scanning/ScanTargets.cs`:

```csharp
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Scanning;

/// <summary>Resolving and checking what a scan or discovery walks. Shared by both job kinds.</summary>
public static class ScanTargets
{
    /// <summary>Walks stop descending this many levels below their starting folder (guards a symlink/junction cycle).</summary>
    public const int MaxFolderDepth = 50;

    /// <summary>The folder and its root, if the folder exists, isn't removed (tombstoned) and its root is active.</summary>
    public static async Task<(ImageRoot Root, Folder Folder)> GetVisibleFolderAsync(
        IFolderRepository folders, IImageRootRepository roots, int folderId, CancellationToken cancellationToken)
    {
        var folder = await folders.GetByIdAsync(folderId, cancellationToken);
        var root = folder is null ? null : await roots.GetByIdAsync(folder.RootId, cancellationToken);
        if (folder is not { IsActive: true } || root is not { IsActive: true })
            throw FolderUnavailableException.NotFound(folderId);

        return (root, folder);
    }

    // A root whose folder is missing or has no entries at all is treated as an unmounted share.
    public static bool IsRootAvailable(string mountPath) =>
        Directory.Exists(mountPath) && Directory.EnumerateFileSystemEntries(mountPath).Any();
}
```

- [ ] **Step 4: Change the scan contract**

Replace `src/PictureManager.Application/Scanning/QueuedScan.cs`:

```csharp
namespace PictureManager.Application.Scanning;

/// <summary>
/// A scan whose job row exists (Enumerating) and whose walk waits for ScanBackgroundService. FolderId set = a
/// folder-scoped scan; otherwise RootId (one root) or neither (all active roots).
/// </summary>
public sealed record QueuedScan(int ScanJobId, int? RootId, int? FolderId, bool IsRecursive);
```

In `src/PictureManager.Application/Scanning/IScanService.cs`, replace the `QueueScanAsync` declaration and its comment:

```csharp
    /// <summary>
    /// Validates the request, creates the job (Enumerating, so any other job is refused while this one waits) and
    /// queues the walk. Returns the job id at once. folderId set = scan that folder; otherwise rootId (one root)
    /// or neither (all active roots). Throws ScanAlreadyInProgressException, ScanRootUnavailableException or
    /// FolderUnavailableException.
    /// </summary>
    Task<int> QueueScanAsync(int? rootId, int? folderId, bool isRecursive, CancellationToken cancellationToken = default);
```

In `src/PictureManager.Api/Endpoints/ScanEndpoints.cs`, change the one call so it compiles (Task 4 adds `folderId` to the request):

```csharp
            var scanJobId = await scanService.QueueScanAsync(request.RootId, null, request.IsRecursive, cancellationToken);
```

- [ ] **Step 5: Replace `ScanService.cs`**

Replace `src/PictureManager.Application/Scanning/ScanService.cs`. The walk loop is today's `ScanRootAsync` loop, moved into `ScanTreeAsync` with a caller-chosen starting folder. Compared with today, the only other changes are the three shared helpers (`ScanTargets`, `PathNormalizer.FolderNameKey`, `PostgresTimestamps`).

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Scanning;

public sealed class ScanService : IScanService
{
    private const int MaxErrorMessageLength = 4000;
    private const int ProgressInterval = 50;
    private const string InterruptedMessage = "Interrupted by an application restart.";

    private readonly IImageRootRepository _imageRootRepository;
    private readonly IFolderRepository _folderRepository;
    private readonly IImageRepository _imageRepository;
    private readonly IAppSettingsRepository _appSettingsRepository;
    private readonly IJobRepository _scanJobRepository;
    private readonly IEnrichmentQueue _enrichmentQueue;
    private readonly IScanQueue _scanQueue;
    private readonly IClock _clock;
    private readonly ScanningOptions _scanningOptions;

    public ScanService(
        IImageRootRepository imageRootRepository,
        IFolderRepository folderRepository,
        IImageRepository imageRepository,
        IAppSettingsRepository appSettingsRepository,
        IJobRepository scanJobRepository,
        IEnrichmentQueue enrichmentQueue,
        IScanQueue scanQueue,
        IClock clock,
        ScanningOptions scanningOptions)
    {
        _imageRootRepository = imageRootRepository;
        _folderRepository = folderRepository;
        _imageRepository = imageRepository;
        _appSettingsRepository = appSettingsRepository;
        _scanJobRepository = scanJobRepository;
        _enrichmentQueue = enrichmentQueue;
        _scanQueue = scanQueue;
        _clock = clock;
        _scanningOptions = scanningOptions;
    }

    public async Task<int> QueueScanAsync(int? rootId, int? folderId, bool isRecursive, CancellationToken cancellationToken = default)
    {
        if (await _scanJobRepository.HasActiveJobAsync(cancellationToken))
            throw new ScanAlreadyInProgressException();

        int? jobFolderId = null;
        if (folderId.HasValue)
        {
            var (_, folder) = await ScanTargets.GetVisibleFolderAsync(_folderRepository, _imageRootRepository, folderId.Value, cancellationToken);

            // The walk couldn't reach it; scanning or discovering its parent is what notices it's back.
            if (folder.MissingSinceUtc is not null)
                throw FolderUnavailableException.Missing(folderId.Value);

            jobFolderId = folder.Id;
        }
        else if (rootId.HasValue)
        {
            await GetActiveRootAsync(rootId.Value, cancellationToken);

            // Every root has a top folder (ImageRootSeeder); it's the job's recorded scope.
            jobFolderId = (await _folderRepository.GetByRootAndRelativePathAsync(rootId.Value, string.Empty, cancellationToken))?.Id;
        }

        // Created as Enumerating (not Pending) so HasActiveJobAsync refuses any other job while this one waits
        // in the queue.
        var scanJob = await _scanJobRepository.AddAsync(new Job
        {
            Kind = JobKind.Scan,
            FolderId = jobFolderId,
            IsRecursive = isRecursive,
            Status = JobStatus.Enumerating,
            StartedUtc = _clock.UtcNow
        }, cancellationToken);

        _scanQueue.Enqueue(new QueuedScan(scanJob.Id, folderId.HasValue ? null : rootId, folderId, isRecursive));
        return scanJob.Id;
    }

    public async Task RunScanAsync(QueuedScan scan, CancellationToken cancellationToken = default)
    {
        // Declared outside the try so the catch block can report how far the scan got before
        // failing (see FinalizeFailureAsync).
        var foldersScanned = 0;
        var filesFound = 0;

        try
        {
            if (scan.FolderId.HasValue)
            {
                (foldersScanned, filesFound) = await ScanFolderTargetAsync(scan.FolderId.Value, scan, cancellationToken);
            }
            else
            {
                // Resolved again here: an explicit root can be deactivated or deleted while the scan waits in the queue.
                var roots = scan.RootId.HasValue
                    ? new[] { await GetActiveRootAsync(scan.RootId.Value, cancellationToken) }
                    : (await _imageRootRepository.GetAllAsync(cancellationToken)).Where(r => r.IsActive).ToArray();

                var settings = await _appSettingsRepository.GetAsync(cancellationToken);
                var excludeRules = new ScanExcludeRules(settings, _scanningOptions.SupportedExtensions);
                var unavailableRoots = new List<string>();

                foreach (var root in roots)
                {
                    // A missing or empty mount folder is a mount problem, not a deletion: change nothing under it.
                    if (!ScanTargets.IsRootAvailable(root.MountPath))
                    {
                        unavailableRoots.Add(root.Name);
                        continue;
                    }

                    var (rootFoldersScanned, rootFilesFound) = await ScanRootAsync(
                        root, scan.IsRecursive, excludeRules, scan.ScanJobId, foldersScanned, filesFound, cancellationToken);
                    foldersScanned += rootFoldersScanned;
                    filesFound += rootFilesFound;
                }

                // The available roots were scanned in full; the job still fails so the user sees the warning.
                if (unavailableRoots.Count > 0)
                    throw new ScanRootsUnavailableException(unavailableRoots);
            }

            await FinalizeSuccessAsync(scan.ScanJobId, foldersScanned, filesFound, cancellationToken);
        }
        catch (Exception ex)
        {
            await FinalizeFailureAsync(scan.ScanJobId, ex, foldersScanned, filesFound);
            throw;
        }
    }

    public Task<int> FailInterruptedJobsAsync(CancellationToken cancellationToken = default) =>
        _scanJobRepository.FailActiveJobsAsync(InterruptedMessage, _clock.UtcNow, cancellationToken);

    private async Task<(int FoldersScanned, int FilesFound)> ScanFolderTargetAsync(int folderId, QueuedScan scan, CancellationToken cancellationToken)
    {
        // Resolved again here: the folder can be removed, or its root deactivated, while the scan waits in the queue.
        var (root, folder) = await ScanTargets.GetVisibleFolderAsync(_folderRepository, _imageRootRepository, folderId, cancellationToken);

        // Same rule as a whole-root scan: an unmounted share changes nothing.
        if (!ScanTargets.IsRootAvailable(root.MountPath))
            throw new ScanRootsUnavailableException(new[] { root.Name });

        // Gone from disk: fail without marking anything; a scan or discovery of its parent does that.
        var folderPath = ImagePathResolver.ResolveFolderPath(root.MountPath, folder.RelativePath);
        if (!Directory.Exists(folderPath))
            throw new FolderNotOnDiskException(root.Name, folder.RelativePath);

        var settings = await _appSettingsRepository.GetAsync(cancellationToken);
        var excludeRules = new ScanExcludeRules(settings, _scanningOptions.SupportedExtensions);
        return await ScanTreeAsync(root, folder, folderPath, scan.IsRecursive, excludeRules, scan.ScanJobId, 0, 0, cancellationToken);
    }

    private async Task FinalizeSuccessAsync(int scanJobId, int foldersScanned, int filesFound, CancellationToken cancellationToken)
    {
        // Every write below is a targeted, atomic SQL UPDATE (ExecuteUpdateAsync), not a whole-row
        // read-modify-write: EnrichmentBackgroundService concurrently increments FilesEnriched (and
        // can itself flip Status to Completed) on this same row from a different DbContext scope, on
        // every scan with enough files that draining starts before the walk finishes. A whole-row
        // write here would silently overwrite whatever it just committed, and vice versa -- this was a
        // reachable production race, not a hypothetical one.
        await _scanJobRepository.SetEnumerationResultAsync(scanJobId, foldersScanned, filesFound, cancellationToken);
        await _scanJobRepository.TryTransitionToEnrichingAsync(scanJobId, cancellationToken);

        // Whichever of "transition to Enriching" (just above) or the background service's last
        // FilesEnriched increment happens last is the one that will see FilesEnriched >= FilesFound
        // and flip the job to Completed -- so this must be attempted here too, not just from
        // EnrichmentBackgroundService.MarkOneEnrichedAsync. When filesFound == 0 this also correctly
        // completes the job immediately (FilesEnriched 0 >= FilesFound 0).
        await _scanJobRepository.TryMarkCompletedIfEnrichedAsync(scanJobId, _clock.UtcNow, cancellationToken);
    }

    private async Task FinalizeFailureAsync(int scanJobId, Exception ex, int foldersScanned, int filesFound)
    {
        var isCancellation = ex is OperationCanceledException;
        var status = isCancellation ? JobStatus.Cancelled : JobStatus.Failed;
        var errorMessage = isCancellation ? null : TruncateErrorMessage(ex.Message);

        // CancellationToken.None: if the scan failed because its own token was cancelled, reusing
        // that (now-cancelled) token for this write would itself throw immediately, leaving the job
        // stuck instead of ever recording its terminal status.
        await _scanJobRepository.SetFailureResultAsync(
            scanJobId, foldersScanned, filesFound, errorMessage, status, _clock.UtcNow, CancellationToken.None);
    }

    private static string? TruncateErrorMessage(string? message) =>
        message is { Length: > MaxErrorMessageLength } ? message[..MaxErrorMessageLength] : message;

    private async Task<ImageRoot> GetActiveRootAsync(int rootId, CancellationToken cancellationToken)
    {
        var root = await _imageRootRepository.GetByIdAsync(rootId, cancellationToken);
        return root is { IsActive: true } ? root : throw new ScanRootUnavailableException(rootId);
    }

    private async Task<(int FoldersScanned, int FilesFound)> ScanRootAsync(
        ImageRoot root, bool isRecursive, ScanExcludeRules excludeRules, int scanJobId,
        int foldersBefore, int filesBefore, CancellationToken cancellationToken)
    {
        var rootFolder = await GetOrCreateFolderAsync(root.Id, parentId: null, relativePath: string.Empty, name: root.Name, cancellationToken);
        return await ScanTreeAsync(root, rootFolder, root.MountPath, isRecursive, excludeRules, scanJobId, foldersBefore, filesBefore, cancellationToken);
    }

    private async Task<(int FoldersScanned, int FilesFound)> ScanTreeAsync(
        ImageRoot root, Folder startFolder, string startPath, bool isRecursive, ScanExcludeRules excludeRules, int scanJobId,
        int foldersBefore, int filesBefore, CancellationToken cancellationToken)
    {
        var foldersScanned = 0;
        var filesFound = 0;

        var pending = new Queue<(Folder Folder, string PhysicalPath, int Depth)>();
        pending.Enqueue((startFolder, startPath, 0));

        while (pending.Count > 0)
        {
            var (folder, physicalPath, depth) = pending.Dequeue();
            foldersScanned++;

            // Live progress for the UI; totals include the roots scanned before this one.
            if (foldersScanned % ProgressInterval == 0)
                await _scanJobRepository.SetEnumerationResultAsync(scanJobId, foldersBefore + foldersScanned, filesBefore + filesFound, cancellationToken);

            if (!Directory.Exists(physicalPath))
                continue;

            var observedFiles = new HashSet<(string FileName, string Extension)>();
            var observedFolders = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entryPath in Directory.EnumerateFileSystemEntries(physicalPath))
            {
                var name = Path.GetFileName(entryPath);

                if (Directory.Exists(entryPath))
                {
                    // Every directory on disk counts as seen, excluded or not: excluded ones are pruned below,
                    // never marked missing.
                    observedFolders.Add(PathNormalizer.FolderNameKey(name));

                    // Excluded names are skipped here; rows that already exist for them are pruned
                    // after this folder's pass (below).
                    if (excludeRules.IsFolderExcluded(name))
                        continue;

                    var childRelativePath = PathNormalizer.Combine(folder.RelativePath, name);
                    var childFolder = await GetOrCreateFolderAsync(root.Id, folder.Id, childRelativePath, name, cancellationToken);

                    // Removed from the collection (tombstone): never descend into it or re-index it.
                    if (!childFolder.IsActive)
                        continue;

                    // Back on disk (remounted, or renamed back): clear the mark. Its descendants are cleared as
                    // the walk reaches them.
                    if (childFolder.MissingSinceUtc is not null)
                    {
                        childFolder.MissingSinceUtc = null;
                        childFolder.ModifiedUtc = _clock.UtcNow;
                        await _folderRepository.UpdateAsync(childFolder, cancellationToken);
                    }

                    // Always create the child Folder row for tree visibility, but stop descending
                    // once isRecursive is false, or once a depth cap is hit (guards against unbounded
                    // growth from a symlink/junction cycle).
                    if (isRecursive && depth < ScanTargets.MaxFolderDepth)
                        pending.Enqueue((childFolder, entryPath, depth + 1));
                }
                else
                {
                    var extension = Path.GetExtension(name).ToLowerInvariant();
                    var fileNameWithoutExtension = PathNormalizer.Normalize(Path.GetFileNameWithoutExtension(name));

                    if (!excludeRules.IsExtensionAllowed(extension))
                        continue;

                    observedFiles.Add(NormalizeKey(fileNameWithoutExtension, extension));

                    var fileInfo = new FileInfo(entryPath);
                    var fileModifiedUtc = PostgresTimestamps.TruncateToMicroseconds(fileInfo.LastWriteTimeUtc);
                    var existingImage = await _imageRepository.GetByFolderAndFileNameAsync(folder.Id, fileNameWithoutExtension, extension, cancellationToken);
                    var decision = ImageReconciler.Decide(existingImage, fileInfo.Length, fileModifiedUtc);

                    switch (decision)
                    {
                        case ReconcileAction.New:
                            var newImage = await _imageRepository.AddAsync(new Image
                            {
                                FolderId = folder.Id,
                                FileName = fileNameWithoutExtension,
                                Extension = extension,
                                ContentHash = string.Empty,
                                FileSize = fileInfo.Length,
                                FileModified = fileModifiedUtc,
                                FirstSeenUtc = _clock.UtcNow,
                                CreatedAt = _clock.UtcNow,
                                UpdatedAt = _clock.UtcNow,
                                IndexState = IndexState.Pending
                            }, cancellationToken);
                            _enrichmentQueue.Enqueue(scanJobId, newImage.Id);
                            filesFound++;
                            break;

                        case ReconcileAction.Modified:
                            existingImage!.FileSize = fileInfo.Length;
                            existingImage.FileModified = fileModifiedUtc;
                            existingImage.IndexState = IndexState.Pending;
                            existingImage.MissingSinceUtc = null;
                            existingImage.UpdatedAt = _clock.UtcNow;
                            await _imageRepository.UpdateAsync(existingImage, cancellationToken);
                            _enrichmentQueue.Enqueue(scanJobId, existingImage.Id);
                            filesFound++;
                            break;

                        case ReconcileAction.Unchanged:
                            // Not enqueued for enrichment, so it must not count toward FilesFound:
                            // FilesFound drives the background service's FilesEnriched >= FilesFound
                            // completion check, and must equal the number of items actually enqueued.
                            break;
                    }
                }
            }

            var existingImages = await _imageRepository.GetByFolderIdAsync(folder.Id, cancellationToken);
            foreach (var image in existingImages)
            {
                // Prune on scan: an extension the settings now exclude removes the row (and, by
                // cascade, its album entries) instead of leaving it marked missing forever.
                if (!excludeRules.IsExtensionAllowed(image.Extension))
                {
                    await _imageRepository.DeleteAsync(image, cancellationToken);
                    continue;
                }

                if (image.MissingSinceUtc is null && !observedFiles.Contains(NormalizeKey(image.FileName, image.Extension)))
                {
                    image.MissingSinceUtc = _clock.UtcNow;
                    image.UpdatedAt = _clock.UtcNow;
                    await _imageRepository.UpdateAsync(image, cancellationToken);
                }
            }

            foreach (var child in await _folderRepository.GetChildrenAsync(folder.Id, cancellationToken))
            {
                // Prune on scan: child folders whose name is now excluded go with their whole subtree.
                // No tombstone is left, because the exclusion rule itself keeps them out, and removing
                // the rule brings them back on the next scan.
                if (excludeRules.IsFolderExcluded(child.Name))
                {
                    await _folderRepository.DeleteSubtreeAsync(child.Id, cancellationToken);
                    continue;
                }

                // Gone from disk (renamed, moved or deleted): mark it and its subtree missing. Nothing is
                // deleted -- the user decides whether to remove it, and it's unmarked if it comes back.
                if (child.IsActive && child.MissingSinceUtc is null && !observedFolders.Contains(PathNormalizer.FolderNameKey(child.Name)))
                    await _folderRepository.MarkSubtreeMissingAsync(child.Id, _clock.UtcNow, cancellationToken);
            }
        }

        return (foldersScanned, filesFound);
    }

    private async Task<Folder> GetOrCreateFolderAsync(int rootId, int? parentId, string relativePath, string name, CancellationToken cancellationToken)
    {
        var existing = await _folderRepository.GetByRootAndRelativePathAsync(rootId, relativePath, cancellationToken);
        if (existing is not null)
            return existing;

        return await _folderRepository.AddAsync(new Folder
        {
            RootId = rootId,
            ParentId = parentId,
            Name = name,
            RelativePath = relativePath,
            CreatedUtc = _clock.UtcNow,
            ModifiedUtc = _clock.UtcNow
        }, cancellationToken);
    }

    // Case- and normalization-insensitive key so the missing-file diff agrees with
    // IImageRepository.GetByFolderAndFileNameAsync's case-insensitive lookup: otherwise a file
    // that's genuinely present (but differs only in case/Unicode normalization from the stored
    // FileName) gets reconciled correctly yet still stamped MissingSinceUtc in the same pass.
    private static (string FileName, string Extension) NormalizeKey(string fileName, string extension) =>
        (PathNormalizer.Normalize(fileName).ToLowerInvariant(), PathNormalizer.Normalize(extension).ToLowerInvariant());
}
```

- [ ] **Step 6: Treat the new target failures as expected in `ScanBackgroundService`**

In `src/PictureManager.Worker/Scanning/ScanBackgroundService.cs`, replace the two catch blocks for `ScanRootsUnavailableException` and `ScanRootUnavailableException` with one:

```csharp
            catch (Exception ex) when (ex is ScanRootsUnavailableException or ScanRootUnavailableException
                                            or FolderUnavailableException or FolderNotOnDiskException)
            {
                // Expected (share not mounted, root or folder removed while queued, folder gone from disk): the job
                // already carries the message for the UI.
                _logger.LogWarning("Scan {ScanJobId} failed: {Message}", scan.ScanJobId, ex.Message);
            }
```

- [ ] **Step 7: Run the tests to confirm they pass**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~Scanning"` then `dotnet test tests/PictureManager.Worker.Tests` then `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~ScanEndpointsTests"`
Expected: PASS, both the new folder-scope tests and every existing scan test unchanged.

- [ ] **Step 8: Run the whole suite and commit**

Run: `dotnet test`
Expected: all pass.

```bash
git add src tests
git commit -m "feat: image scans can target any folder"
```

(Check `git status` before committing: only files under `src/` and `tests/` that this task touched should be staged.)

---

### Task 4: `POST /api/scans` accepts `folderId`

**Files:**
- Modify: `src/PictureManager.Api/Endpoints/ScanEndpoints.cs`
- Test: `tests/PictureManager.Api.Tests/Endpoints/ScanEndpointsTests.cs`

**Interfaces:**
- Consumes: `IScanService.QueueScanAsync(int? rootId, int? folderId, bool isRecursive, …)`, `FolderUnavailableException`, `Job.Kind` (Tasks 1 and 3).
- Produces: `ScanRequest(int? RootId, bool IsRecursive, int? FolderId = null)`.

- [ ] **Step 1: Write the failing tests**

Add to `ScanEndpointsTests.cs` (add `using Microsoft.AspNetCore.Http.HttpResults;`):

```csharp
    [Fact]
    public async Task StartScanAsync_WithFolderId_PassesItThrough()
    {
        var scanService = Substitute.For<IScanService>();
        scanService.QueueScanAsync(null, 20, false, Arg.Any<CancellationToken>()).Returns(43);

        var result = await ScanEndpoints.StartScanAsync(new ScanRequest(null, false, 20), scanService, CancellationToken.None);

        result.Should().BeOfType<Ok<ScanStartedResponse>>().Which.Value!.ScanJobId.Should().Be(43);
    }

    [Fact]
    public async Task StartScanAsync_RootIdAndFolderId_ReturnsValidationProblem_WithoutQueuing()
    {
        var scanService = Substitute.For<IScanService>();

        var result = await ScanEndpoints.StartScanAsync(new ScanRequest(1, true, 20), scanService, CancellationToken.None);

        result.Should().BeOfType<ValidationProblem>().Which.ProblemDetails.Errors["folderId"]
            .Should().Equal("Give rootId or folderId, not both.");
        await scanService.DidNotReceiveWithAnyArgs().QueueScanAsync(default, default, default, default);
    }

    [Fact]
    public async Task StartScanAsync_UnavailableFolder_ReturnsValidationProblemKeyedFolderId()
    {
        var scanService = Substitute.For<IScanService>();
        scanService.QueueScanAsync(null, 20, true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(FolderUnavailableException.Missing(20)));

        var result = await ScanEndpoints.StartScanAsync(new ScanRequest(null, true, 20), scanService, CancellationToken.None);

        result.Should().BeOfType<ValidationProblem>().Which.ProblemDetails.Errors.Should().ContainKey("folderId");
    }

    [Fact]
    public async Task StreamScanEventsAsync_DiscoveryJobId_WritesErrorEvent()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new Job { Id = 1, Kind = JobKind.Discovery, Status = JobStatus.Completed });

        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        await ScanEndpoints.StreamScanEventsAsync(context, 1, jobs, CancellationToken.None);

        Encoding.UTF8.GetString(body.ToArray()).Should().Contain("event: error").And.NotContain("\"Status\"");
    }
```

- [ ] **Step 2: Run the tests to confirm the failure**

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~ScanEndpointsTests"`
Expected: FAIL to compile: `ScanRequest` has no third parameter.

- [ ] **Step 3: Implement**

In `src/PictureManager.Api/Endpoints/ScanEndpoints.cs`, replace `StartScanAsync`:

```csharp
    public static async Task<IResult> StartScanAsync(ScanRequest request, IScanService scanService, CancellationToken cancellationToken)
    {
        if (request.RootId.HasValue && request.FolderId.HasValue)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["folderId"] = new[] { "Give rootId or folderId, not both." }
            });
        }

        try
        {
            var scanJobId = await scanService.QueueScanAsync(request.RootId, request.FolderId, request.IsRecursive, cancellationToken);
            return Results.Ok(new ScanStartedResponse(scanJobId));
        }
        catch (ScanAlreadyInProgressException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
        catch (ScanRootUnavailableException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["rootId"] = new[] { ex.Message } });
        }
        catch (FolderUnavailableException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["folderId"] = new[] { ex.Message } });
        }
    }
```

In `StreamScanEventsAsync`, change the null check so a discovery job reads as "not found" on this route:

```csharp
            // A discovery job's id isn't a scan: same answer as an unknown id (see /api/discovery/{id}/events).
            if (scanJob is null || scanJob.Kind != JobKind.Scan)
            {
                await context.Response.WriteAsync("event: error\ndata: not found\n\n", cancellationToken);
                return;
            }
```

Replace the `ScanRequest` record:

```csharp
/// <summary>folderId is optional and new; a request with only rootId/isRecursive behaves exactly as before.</summary>
public sealed record ScanRequest(int? RootId, bool IsRecursive, int? FolderId = null);
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~ScanEndpointsTests"`
Expected: PASS (existing and new).

- [ ] **Step 5: Commit**

```bash
git add src/PictureManager.Api/Endpoints/ScanEndpoints.cs tests/PictureManager.Api.Tests/Endpoints/ScanEndpointsTests.cs
git commit -m "feat: POST /api/scans accepts a folderId"
```

---

### Task 5: The folder discovery service

**Files:**
- Create: `src/PictureManager.Application/Scanning/IDiscoveryService.cs`, `DiscoveryService.cs`, `QueuedDiscovery.cs`, `IDiscoveryQueue.cs`
- Modify: `src/PictureManager.Application/Repositories/IJobRepository.cs`, `src/PictureManager.Infrastructure/Persistence/Repositories/JobRepository.cs`, `src/PictureManager.Application/Scanning/ScanExcludeRules.cs`, `src/PictureManager.Application/Scanning/ScanAlreadyInProgressException.cs`
- Test: `tests/PictureManager.Application.Tests/Scanning/DiscoveryServiceTests.cs` (new), `ScanExcludeRulesTests.cs`, `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/JobRepositoryTests.cs`

**Interfaces:**
- Consumes: `ScanTargets`, `FolderUnavailableException`, `FolderNotOnDiskException`, `PathNormalizer.FolderNameKey`, `ImagePathResolver.ResolveFolderPath`, `PostgresTimestamps` (Task 3); `Job`, `JobKind`, `IJobRepository` (Task 1); `ScanRootsUnavailableException`, `ScanAlreadyInProgressException` (existing).
- Produces (Tasks 6 and 7 use these):
  - `IJobRepository.TryMarkCompletedAsync(int jobId, DateTime completedUtc, CancellationToken) → Task<bool>`
  - `QueuedDiscovery(int JobId, int? FolderId, bool IsRecursive)`
  - `IDiscoveryQueue { void Enqueue(QueuedDiscovery); IAsyncEnumerable<QueuedDiscovery> ReadAllAsync(CancellationToken) }`
  - `IDiscoveryService { Task<int> QueueDiscoveryAsync(int? folderId, bool isRecursive, CancellationToken); Task RunDiscoveryAsync(QueuedDiscovery, CancellationToken) }`
  - `DiscoveryService(IImageRootRepository, IFolderRepository, IAppSettingsRepository, IJobRepository, IDiscoveryQueue, IClock)`
  - `ScanExcludeRules.AlwaysExcludedFolderNames`

- [ ] **Step 1: Write the failing tests**

Append to `tests/PictureManager.Application.Tests/Scanning/ScanExcludeRulesTests.cs`, inside the class:

```csharp
    [Theory]
    [InlineData("$RECYCLE.BIN")]
    [InlineData("System Volume Information")]
    [InlineData("@eaDir")]
    [InlineData("#recycle")]
    [InlineData(".snapshot")]
    [InlineData("@EADIR")]
    public void IsFolderExcluded_OsAndNasHousekeepingFolders_AreAlwaysExcluded(string name)
    {
        new ScanExcludeRules(new AppSettings(), Array.Empty<string>()).IsFolderExcluded(name).Should().BeTrue();
    }
```

Append to `JobRepositoryTests.cs`, inside the class:

```csharp
    [Fact]
    public async Task TryMarkCompletedAsync_WhenEnumerating_CompletesTheJob()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var repository = new JobRepository(db.Context);
        var job = await repository.AddAsync(new Job { Kind = JobKind.Discovery, Status = JobStatus.Enumerating, StartedUtc = TestData.Utc });

        (await repository.TryMarkCompletedAsync(job.Id, TestData.Utc.AddMinutes(1))).Should().BeTrue();

        var fetched = await repository.GetByIdAsync(job.Id);
        fetched!.Status.Should().Be(JobStatus.Completed);
        fetched.CompletedUtc.Should().Be(TestData.Utc.AddMinutes(1));
    }

    [Fact]
    public async Task TryMarkCompletedAsync_WhenNotEnumerating_ReturnsFalse_AndLeavesRowUnchanged()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var repository = new JobRepository(db.Context);
        var job = await repository.AddAsync(new Job
        {
            Kind = JobKind.Discovery, Status = JobStatus.Failed, StartedUtc = TestData.Utc, CompletedUtc = TestData.Utc
        });

        (await repository.TryMarkCompletedAsync(job.Id, TestData.Utc.AddMinutes(1))).Should().BeFalse();

        (await repository.GetByIdAsync(job.Id))!.Status.Should().Be(JobStatus.Failed);
    }
```

Create `tests/PictureManager.Application.Tests/Scanning/DiscoveryServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public sealed class DiscoveryServiceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Earlier = new(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly DirectoryInfo _tempRoot = Directory.CreateTempSubdirectory("pm-discovery-");
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly IAppSettingsRepository _settings = Substitute.For<IAppSettingsRepository>();
    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly IDiscoveryQueue _queue = Substitute.For<IDiscoveryQueue>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ImageRoot _root;
    private readonly Folder _top = new() { Id = 10, RootId = 1, Name = "dev", RelativePath = string.Empty };
    private readonly List<Folder> _added = new();
    private int _nextFolderId = 100;

    public DiscoveryServiceTests()
    {
        _root = new ImageRoot { Id = 1, Name = "dev", MountPath = _tempRoot.FullName, IsActive = true };
        _clock.UtcNow.Returns(Now);
        _roots.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(_root);
        _roots.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ImageRoot> { _root });
        _folders.GetByIdAsync(10, Arg.Any<CancellationToken>()).Returns(_top);
        _folders.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>()).Returns(_top);
        _folders.GetChildrenAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(new List<Folder>());
        _folders.AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var folder = call.Arg<Folder>();
            folder.Id = _nextFolderId++;
            _added.Add(folder);
            return folder;
        });
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings());
        _jobs.AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var job = call.Arg<Job>();
            job.Id = 999;
            return job;
        });
    }

    public void Dispose() => _tempRoot.Delete(recursive: true);

    private DiscoveryService CreateService() => new(_roots, _folders, _settings, _jobs, _queue, _clock);

    private string Dir(string relativePath) =>
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar))).FullName;

    private Folder Child(int id, string name, int parentId = 10, string? relativePath = null) =>
        new() { Id = id, RootId = 1, ParentId = parentId, Name = name, RelativePath = relativePath ?? name };

    private void ChildrenOf(int parentId, params Folder[] children) =>
        _folders.GetChildrenAsync(parentId, Arg.Any<CancellationToken>()).Returns(children.ToList());

    /// <summary>Queue + run in one call: what DiscoveryBackgroundService does, minus the queue hop.</summary>
    private async Task DiscoverNowAsync(int? folderId, bool isRecursive)
    {
        var jobId = await CreateService().QueueDiscoveryAsync(folderId, isRecursive);
        await CreateService().RunDiscoveryAsync(new QueuedDiscovery(jobId, folderId, isRecursive));
    }

    private Folder AddedAt(string relativePath) => _added.Single(f => f.RelativePath == relativePath);

    // Symlinks need Developer Mode or admin on Windows; a junction doesn't. Both are reparse points.
    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception ex) when (OperatingSystem.IsWindows() && ex is IOException or UnauthorizedAccessException)
        {
            using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            })!;
            mklink.WaitForExit();
            if (!Directory.Exists(linkPath))
                throw new InvalidOperationException("Could not create a directory link for the test.", ex);
        }
    }

    [Fact]
    public async Task QueueDiscoveryAsync_CreatesAnEnumeratingDiscoveryJob_AndEnqueuesIt()
    {
        Dir("A");

        var jobId = await CreateService().QueueDiscoveryAsync(10, isRecursive: true);

        jobId.Should().Be(999);
        await _jobs.Received(1).AddAsync(
            Arg.Is<Job>(j => j.Kind == JobKind.Discovery && j.FolderId == 10 && j.IsRecursive
                             && j.Status == JobStatus.Enumerating && j.StartedUtc == Now),
            Arg.Any<CancellationToken>());
        _queue.Received(1).Enqueue(new QueuedDiscovery(999, 10, true));
        await _folders.DidNotReceive().AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueueDiscoveryAsync_JobAlreadyActive_Throws_AndEnqueuesNothing()
    {
        _jobs.HasActiveJobAsync(Arg.Any<CancellationToken>()).Returns(true);

        var act = () => CreateService().QueueDiscoveryAsync(null, isRecursive: true);

        await act.Should().ThrowAsync<ScanAlreadyInProgressException>();
        _queue.DidNotReceive().Enqueue(Arg.Any<QueuedDiscovery>());
    }

    [Fact]
    public async Task QueueDiscoveryAsync_UnknownOrRemovedFolder_Throws_AndCreatesNoJob()
    {
        var removed = Child(11, "Removed");
        removed.IsActive = false;
        _folders.GetByIdAsync(11, Arg.Any<CancellationToken>()).Returns(removed);

        var unknown = () => CreateService().QueueDiscoveryAsync(77, isRecursive: true);
        var tombstoned = () => CreateService().QueueDiscoveryAsync(11, isRecursive: true);

        (await unknown.Should().ThrowAsync<FolderUnavailableException>()).Which.FolderId.Should().Be(77);
        (await tombstoned.Should().ThrowAsync<FolderUnavailableException>()).Which.FolderId.Should().Be(11);
        await _jobs.DidNotReceive().AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_NewFolders_AreInsertedRecursively_WithTimestamps()
    {
        Dir("A/A1");
        Dir("B");

        await DiscoverNowAsync(10, isRecursive: true);

        _added.Select(f => f.RelativePath).Should().BeEquivalentTo("A", "B", "A/A1");
        AddedAt("A").ParentId.Should().Be(10);
        AddedAt("A/A1").ParentId.Should().Be(AddedAt("A").Id);
        _added.Should().OnlyContain(f => f.LastWriteTimeUtc != null && f.RootId == 1);
        _top.ChildrenDiscoveredAt.Should().Be(Now);
        _top.LastWriteTimeUtc.Should().NotBeNull();
        AddedAt("A").ChildrenDiscoveredAt.Should().Be(Now);
        AddedAt("A/A1").ChildrenDiscoveredAt.Should().Be(Now);
        await _jobs.Received(1).SetEnumerationResultAsync(999, 4, 0, Arg.Any<CancellationToken>());
        await _jobs.Received(1).TryMarkCompletedAsync(999, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_NonRecursive_DiffsOnlyTheTargetsChildren()
    {
        Dir("A/A1");

        await DiscoverNowAsync(10, isRecursive: false);

        _added.Select(f => f.RelativePath).Should().Equal("A");
        AddedAt("A").ChildrenDiscoveredAt.Should().BeNull();
        _top.ChildrenDiscoveredAt.Should().Be(Now);
        await _jobs.Received(1).SetEnumerationResultAsync(999, 1, 0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_FolderGoneFromDisk_IsMarkedMissing()
    {
        Dir("Kept");
        ChildrenOf(10, Child(20, "Kept"), Child(21, "Gone"));

        await DiscoverNowAsync(10, isRecursive: false);

        await _folders.Received(1).MarkSubtreeMissingAsync(21, Now, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(20, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_MissingFolderBackOnDisk_IsCleared()
    {
        Dir("Back");
        var back = Child(20, "Back");
        back.MissingSinceUtc = Earlier;
        ChildrenOf(10, back);

        await DiscoverNowAsync(10, isRecursive: false);

        back.MissingSinceUtc.Should().BeNull();
        back.LastWriteTimeUtc.Should().NotBeNull();
        await _folders.Received().UpdateAsync(back, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_CaseOnlyDifference_MatchesTheExistingFolder()
    {
        Dir("madeira");
        ChildrenOf(10, Child(20, "Madeira"));

        await DiscoverNowAsync(10, isRecursive: false);

        await _folders.DidNotReceive().AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_HousekeepingAndSettingsExcludedNames_AreNeverInserted()
    {
        Dir("@eaDir/thumbs");
        Dir("Private");
        Dir("Keep");
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings { ExcludedFolderNames = new List<string> { "Private" } });

        await DiscoverNowAsync(10, isRecursive: true);

        _added.Select(f => f.RelativePath).Should().Equal("Keep");
    }

    [Fact]
    public async Task RunDiscoveryAsync_PreviouslyDiscoveredFolderNowExcluded_IsPrunedWithItsSubtree()
    {
        Dir("Private");
        ChildrenOf(10, Child(20, "Private"));
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings { ExcludedFolderNames = new List<string> { "Private" } });

        await DiscoverNowAsync(10, isRecursive: true);

        await _folders.Received(1).DeleteSubtreeAsync(20, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_RemovedFolder_IsLeftAloneAndNotWalked()
    {
        Dir("Removed/Inner");
        var removed = Child(20, "Removed");
        removed.IsActive = false;
        ChildrenOf(10, removed);

        await DiscoverNowAsync(10, isRecursive: true);

        await _folders.DidNotReceive().AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().UpdateAsync(removed, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_DepthCap_StopsDescending()
    {
        Dir(string.Join('/', Enumerable.Repeat("d", 55)));

        await DiscoverNowAsync(10, isRecursive: true);

        // Folders at depth 0..50 are walked, so their children (depth 1..51) are inserted; nothing deeper.
        _added.Should().HaveCount(51);
    }

    [Fact]
    public async Task RunDiscoveryAsync_SymlinkOrJunction_IsNeitherInsertedNorFollowed_NorMarkedMissing()
    {
        var real = Dir("Real/Inner");
        CreateDirectoryLink(Path.Combine(_tempRoot.FullName, "Link"), Path.GetDirectoryName(real)!);
        ChildrenOf(10, Child(20, "Link"));

        await DiscoverNowAsync(10, isRecursive: true);

        _added.Select(f => f.RelativePath).Should().BeEquivalentTo("Real", "Real/Inner");
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(20, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_AllRoots_SkipsAnUnmountedRoot_AndFailsTheJob()
    {
        Dir("A");
        var nas = new ImageRoot { Id = 2, Name = "nas", MountPath = Path.Combine(_tempRoot.FullName, "not-mounted"), IsActive = true };
        _roots.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ImageRoot> { _root, nas });

        var act = () => DiscoverNowAsync(null, isRecursive: true);

        await act.Should().ThrowAsync<ScanRootsUnavailableException>();
        _added.Select(f => f.RelativePath).Should().Equal("A");
        await _folders.DidNotReceive().GetByRootAndRelativePathAsync(2, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        // Two folders walked on the available root: its top folder and A.
        await _jobs.Received(1).SetFailureResultAsync(999, 2, 0, Arg.Is<string>(m => m.Contains("Root 'nas' is unavailable")),
            JobStatus.Failed, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_AllRoots_CreatesAMissingTopFolder()
    {
        var fresh = new ImageRoot { Id = 3, Name = "fresh", MountPath = Dir("fresh-mount"), IsActive = true };
        Dir("fresh-mount/X");
        _roots.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ImageRoot> { fresh });

        await DiscoverNowAsync(null, isRecursive: true);

        var top = _added.Single(f => f.RootId == 3 && f.RelativePath == "");
        top.Name.Should().Be("fresh");
        top.ParentId.Should().BeNull();
        _added.Single(f => f.RelativePath == "X").ParentId.Should().Be(top.Id);
    }

    [Fact]
    public async Task RunDiscoveryAsync_TargetFolderGoneBeforeRun_FailsWithoutMarkingAnything()
    {
        Dir("A");
        _folders.GetByIdAsync(20, Arg.Any<CancellationToken>()).Returns(Child(20, "Trips"));

        var act = () => CreateService().RunDiscoveryAsync(new QueuedDiscovery(999, 20, true));

        await act.Should().ThrowAsync<FolderNotOnDiskException>().WithMessage("Folder is no longer on disk: dev/Trips");
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetFailureResultAsync(999, 0, 0, "Folder is no longer on disk: dev/Trips",
            JobStatus.Failed, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_MissingTargetBackOnDisk_ClearsItsOwnMark()
    {
        Dir("Trips");
        var trips = Child(20, "Trips");
        trips.MissingSinceUtc = Earlier;
        _folders.GetByIdAsync(20, Arg.Any<CancellationToken>()).Returns(trips);

        await DiscoverNowAsync(20, isRecursive: false);

        trips.MissingSinceUtc.Should().BeNull();
        trips.ChildrenDiscoveredAt.Should().Be(Now);
        await _folders.Received().UpdateAsync(trips, Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run the tests to confirm the failure**

Run: `dotnet build`
Expected: FAIL: `DiscoveryService`, `QueuedDiscovery`, `IDiscoveryQueue`, `IJobRepository.TryMarkCompletedAsync` don't exist.

- [ ] **Step 3: Add `TryMarkCompletedAsync`**

In `src/PictureManager.Application/Repositories/IJobRepository.cs`, add after `TryMarkCompletedIfEnrichedAsync`:

```csharp
    /// <summary>
    /// Atomically flips Status from Enumerating to Completed (setting CompletedUtc), for jobs with no enrichment
    /// phase (discovery). Returns whether a row was changed.
    /// </summary>
    Task<bool> TryMarkCompletedAsync(int jobId, DateTime completedUtc, CancellationToken cancellationToken = default);
```

In `src/PictureManager.Infrastructure/Persistence/Repositories/JobRepository.cs`, add:

```csharp
    public async Task<bool> TryMarkCompletedAsync(int jobId, DateTime completedUtc, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.Jobs
            .Where(j => j.Id == jobId && j.Status == JobStatus.Enumerating)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Completed)
                .SetProperty(j => j.CompletedUtc, completedUtc),
                cancellationToken);
        return rows > 0;
    }
```

- [ ] **Step 4: Always exclude the housekeeping folders, and name both job kinds in the "busy" message**

In `src/PictureManager.Application/Scanning/ScanExcludeRules.cs`, add `using System.Linq;`, add this member, and change the `_excludedFolderNames` line in the constructor:

```csharp
    /// <summary>
    /// OS and NAS housekeeping folders: never discovered or scanned, whatever the settings say. Shared by both walks
    /// so a scan never re-creates what a discovery pruned.
    /// </summary>
    public static readonly IReadOnlyList<string> AlwaysExcludedFolderNames = new[]
    {
        "$RECYCLE.BIN", "System Volume Information", "@eaDir", "#recycle", ".snapshot"
    };
```

```csharp
        _excludedFolderNames = new HashSet<string>(
            settings.ExcludedFolderNames.Concat(AlwaysExcludedFolderNames), StringComparer.OrdinalIgnoreCase);
```

In `src/PictureManager.Application/Scanning/ScanAlreadyInProgressException.cs`:

```csharp
    public ScanAlreadyInProgressException() : base("A scan or folder discovery is already in progress.")
```

- [ ] **Step 5: Add the discovery contract**

Create `src/PictureManager.Application/Scanning/QueuedDiscovery.cs`:

```csharp
namespace PictureManager.Application.Scanning;

/// <summary>A discovery whose job row exists (Enumerating) and whose walk waits for DiscoveryBackgroundService.</summary>
public sealed record QueuedDiscovery(int JobId, int? FolderId, bool IsRecursive);
```

Create `src/PictureManager.Application/Scanning/IDiscoveryQueue.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;

namespace PictureManager.Application.Scanning;

public interface IDiscoveryQueue
{
    void Enqueue(QueuedDiscovery discovery);
    IAsyncEnumerable<QueuedDiscovery> ReadAllAsync(CancellationToken cancellationToken = default);
}
```

Create `src/PictureManager.Application/Scanning/IDiscoveryService.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Scanning;

public interface IDiscoveryService
{
    /// <summary>
    /// Validates the request, creates the Discovery job (Enumerating, so any other job is refused while this one
    /// waits) and queues the walk. folderId null = every active root. Returns the job id at once. Throws
    /// ScanAlreadyInProgressException or FolderUnavailableException.
    /// </summary>
    Task<int> QueueDiscoveryAsync(int? folderId, bool isRecursive, CancellationToken cancellationToken = default);

    /// <summary>Walks a queued discovery and records the outcome on its job. Rethrows a failure after recording it.</summary>
    Task RunDiscoveryAsync(QueuedDiscovery discovery, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 6: Implement `DiscoveryService`**

Create `src/PictureManager.Application/Scanning/DiscoveryService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Scanning;

/// <summary>
/// Folder discovery: walks directory names only and keeps the Folder tree in step with disk. Never looks at files;
/// image scanning (ScanService) is separate.
/// </summary>
public sealed class DiscoveryService : IDiscoveryService
{
    private const int MaxErrorMessageLength = 4000;
    private const int ProgressInterval = 50;

    private readonly IImageRootRepository _roots;
    private readonly IFolderRepository _folders;
    private readonly IAppSettingsRepository _settings;
    private readonly IJobRepository _jobs;
    private readonly IDiscoveryQueue _queue;
    private readonly IClock _clock;

    public DiscoveryService(
        IImageRootRepository roots,
        IFolderRepository folders,
        IAppSettingsRepository settings,
        IJobRepository jobs,
        IDiscoveryQueue queue,
        IClock clock)
    {
        _roots = roots;
        _folders = folders;
        _settings = settings;
        _jobs = jobs;
        _queue = queue;
        _clock = clock;
    }

    public async Task<int> QueueDiscoveryAsync(int? folderId, bool isRecursive, CancellationToken cancellationToken = default)
    {
        if (await _jobs.HasActiveJobAsync(cancellationToken))
            throw new ScanAlreadyInProgressException();

        if (folderId.HasValue)
            await ScanTargets.GetVisibleFolderAsync(_folders, _roots, folderId.Value, cancellationToken);

        // Created as Enumerating (not Pending) so HasActiveJobAsync refuses any other job while this one waits.
        var job = await _jobs.AddAsync(new Job
        {
            Kind = JobKind.Discovery,
            FolderId = folderId,
            IsRecursive = isRecursive,
            Status = JobStatus.Enumerating,
            StartedUtc = _clock.UtcNow
        }, cancellationToken);

        _queue.Enqueue(new QueuedDiscovery(job.Id, folderId, isRecursive));
        return job.Id;
    }

    public async Task RunDiscoveryAsync(QueuedDiscovery discovery, CancellationToken cancellationToken = default)
    {
        // Outside the try so a failure can report how far the walk got.
        var foldersProcessed = 0;

        try
        {
            var targets = new List<(ImageRoot Root, Folder Folder, string Path)>();
            var unavailableRoots = new List<string>();

            if (discovery.FolderId.HasValue)
            {
                // Resolved again here: the folder can be removed, or its root deactivated, while the job waits.
                var (root, folder) = await ScanTargets.GetVisibleFolderAsync(_folders, _roots, discovery.FolderId.Value, cancellationToken);
                if (!ScanTargets.IsRootAvailable(root.MountPath))
                {
                    unavailableRoots.Add(root.Name);
                }
                else
                {
                    // Gone from disk: fail without marking anything; discovering its parent does that.
                    var path = ImagePathResolver.ResolveFolderPath(root.MountPath, folder.RelativePath);
                    if (!Directory.Exists(path))
                        throw new FolderNotOnDiskException(root.Name, folder.RelativePath);

                    targets.Add((root, folder, path));
                }
            }
            else
            {
                foreach (var root in (await _roots.GetAllAsync(cancellationToken)).Where(r => r.IsActive))
                {
                    // An unmounted share looks empty; walking it would mark its whole tree missing.
                    if (!ScanTargets.IsRootAvailable(root.MountPath))
                    {
                        unavailableRoots.Add(root.Name);
                        continue;
                    }

                    targets.Add((root, await GetOrCreateTopFolderAsync(root, cancellationToken), root.MountPath));
                }
            }

            var settings = await _settings.GetAsync(cancellationToken);
            var excludeRules = new ScanExcludeRules(settings, Array.Empty<string>());

            foreach (var (root, folder, path) in targets)
                foldersProcessed = await DiscoverTreeAsync(root, folder, path, discovery.IsRecursive, excludeRules, discovery.JobId, foldersProcessed, cancellationToken);

            // The available roots were discovered in full; the job still fails so the user sees the warning.
            if (unavailableRoots.Count > 0)
                throw new ScanRootsUnavailableException(unavailableRoots);

            await _jobs.SetEnumerationResultAsync(discovery.JobId, foldersProcessed, 0, cancellationToken);
            await _jobs.TryMarkCompletedAsync(discovery.JobId, _clock.UtcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            var isCancellation = ex is OperationCanceledException;

            // CancellationToken.None: a cancelled token would make this write throw too, leaving the job stuck.
            await _jobs.SetFailureResultAsync(
                discovery.JobId,
                foldersProcessed,
                0,
                isCancellation ? null : Truncate(ex.Message),
                isCancellation ? JobStatus.Cancelled : JobStatus.Failed,
                _clock.UtcNow,
                CancellationToken.None);
            throw;
        }
    }

    private async Task<int> DiscoverTreeAsync(
        ImageRoot root, Folder start, string startPath, bool isRecursive, ScanExcludeRules excludeRules, int jobId,
        int processedBefore, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var processed = processedBefore;

        // The start folder has no parent listing in this job, so its own last-write time comes from one direct
        // read. It's evidently on disk, so a missing mark on it is cleared too.
        start.LastWriteTimeUtc = PostgresTimestamps.TruncateToMicroseconds(Directory.GetLastWriteTimeUtc(startPath));
        start.MissingSinceUtc = null;

        var pending = new Queue<(Folder Folder, string Path, int Depth)>();
        pending.Enqueue((start, startPath, 0));

        while (pending.Count > 0)
        {
            var (folder, path, depth) = pending.Dequeue();
            processed++;

            if (processed % ProgressInterval == 0)
                await _jobs.SetEnumerationResultAsync(jobId, processed, 0, cancellationToken);

            var existingChildren = await _folders.GetChildrenAsync(folder.Id, cancellationToken);
            var byKey = new Dictionary<string, Folder>(StringComparer.Ordinal);
            foreach (var child in existingChildren)
                byKey.TryAdd(PathNormalizer.FolderNameKey(child.Name), child);

            var observed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var directory in new DirectoryInfo(path).EnumerateDirectories())
            {
                var key = PathNormalizer.FolderNameKey(directory.Name);
                observed.Add(key);

                // Excluded: never inserted or walked. A row that already exists is pruned below.
                if (excludeRules.IsFolderExcluded(directory.Name))
                    continue;

                // Symlinks and junctions are never followed or indexed (loop guard). They count as present, so a
                // row an earlier scan created for one isn't marked missing.
                if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;

                var lastWriteUtc = PostgresTimestamps.TruncateToMicroseconds(directory.LastWriteTimeUtc);
                Folder childFolder;

                if (byKey.TryGetValue(key, out var existing))
                {
                    // Removed from the collection (tombstone): never re-indexed or walked.
                    if (!existing.IsActive)
                        continue;

                    if (existing.MissingSinceUtc is not null || existing.LastWriteTimeUtc != lastWriteUtc)
                    {
                        existing.MissingSinceUtc = null;
                        existing.LastWriteTimeUtc = lastWriteUtc;
                        existing.ModifiedUtc = now;
                        await _folders.UpdateAsync(existing, cancellationToken);
                    }

                    childFolder = existing;
                }
                else
                {
                    childFolder = await _folders.AddAsync(new Folder
                    {
                        RootId = root.Id,
                        ParentId = folder.Id,
                        Name = directory.Name,
                        RelativePath = PathNormalizer.Combine(folder.RelativePath, directory.Name),
                        LastWriteTimeUtc = lastWriteUtc,
                        CreatedUtc = now,
                        ModifiedUtc = now
                    }, cancellationToken);
                }

                if (isRecursive && depth < ScanTargets.MaxFolderDepth)
                    pending.Enqueue((childFolder, directory.FullName, depth + 1));
            }

            foreach (var child in existingChildren)
            {
                // Prune: a name the settings now exclude goes with its whole subtree. No tombstone, because the
                // rule itself keeps it out, and removing the rule brings it back on the next discovery.
                if (excludeRules.IsFolderExcluded(child.Name))
                {
                    await _folders.DeleteSubtreeAsync(child.Id, cancellationToken);
                    continue;
                }

                // Gone from disk (renamed, moved or deleted): mark it and its subtree missing. Nothing is deleted.
                if (child.IsActive && child.MissingSinceUtc is null && !observed.Contains(PathNormalizer.FolderNameKey(child.Name)))
                    await _folders.MarkSubtreeMissingAsync(child.Id, now, cancellationToken);
            }

            folder.ChildrenDiscoveredAt = now;
            folder.ModifiedUtc = now;
            await _folders.UpdateAsync(folder, cancellationToken);
        }

        return processed;
    }

    // ImageRootSeeder creates every root's top folder at startup; this covers a root created before that rule.
    private async Task<Folder> GetOrCreateTopFolderAsync(ImageRoot root, CancellationToken cancellationToken) =>
        await _folders.GetByRootAndRelativePathAsync(root.Id, string.Empty, cancellationToken)
        ?? await _folders.AddAsync(new Folder
        {
            RootId = root.Id,
            Name = root.Name,
            RelativePath = string.Empty,
            CreatedUtc = _clock.UtcNow,
            ModifiedUtc = _clock.UtcNow
        }, cancellationToken);

    private static string? Truncate(string? message) =>
        message is { Length: > MaxErrorMessageLength } ? message[..MaxErrorMessageLength] : message;
}
```

- [ ] **Step 7: Run the tests to confirm they pass**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~DiscoveryServiceTests|FullyQualifiedName~ScanExcludeRulesTests"`
Expected: PASS. If the symlink/junction test can't create a link at all, it fails with "Could not create a directory link for the test." Treat that as an environment problem to report, not a code failure.

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~JobRepositoryTests"`
Expected: PASS.

- [ ] **Step 8: Run the whole suite and commit**

Run: `dotnet test`
Expected: all pass. The scan tests still pass with the housekeeping names excluded, because none of them uses those names.

```bash
git add src tests
git commit -m "feat: add the folder discovery service"
```

(Check `git status` before committing: only this task's files under `src/` and `tests/` should be staged.)

---

### Task 6: Run discoveries in the background

**Files:**
- Create: `src/PictureManager.Worker/Scanning/ChannelDiscoveryQueue.cs`, `src/PictureManager.Worker/Scanning/DiscoveryBackgroundService.cs`
- Modify: `src/PictureManager.Worker/DependencyInjection/WorkerServiceCollectionExtensions.cs`, `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Worker.Tests/Scanning/DiscoveryBackgroundServiceTests.cs` (new), `tests/PictureManager.Application.Tests/DependencyInjection/ApplicationServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes: `IDiscoveryService`, `IDiscoveryQueue`, `QueuedDiscovery`, `FolderUnavailableException`, `FolderNotOnDiskException`, `ScanRootsUnavailableException`, `IJobRepository.FailActiveJobsAsync` (Tasks 1, 3, 5).
- Produces: `ChannelDiscoveryQueue : IDiscoveryQueue`; `DiscoveryBackgroundService(IDiscoveryQueue, IServiceScopeFactory, IClock, ILogger<DiscoveryBackgroundService>)`; DI registrations `IDiscoveryService` (scoped), `IDiscoveryQueue` (singleton), and the hosted service.

- [ ] **Step 1: Write the failing tests**

Create `tests/PictureManager.Worker.Tests/Scanning/DiscoveryBackgroundServiceTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Worker.DependencyInjection;
using PictureManager.Worker.Scanning;
using Xunit;

namespace PictureManager.Worker.Tests.Scanning;

public class DiscoveryBackgroundServiceTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private static DiscoveryBackgroundService CreateService(IDiscoveryQueue queue, IDiscoveryService discoveryService, IJobRepository jobs)
    {
        var provider = new ServiceCollection().AddScoped(_ => discoveryService).AddScoped(_ => jobs).BuildServiceProvider();
        return new DiscoveryBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IClock>(), NullLogger<DiscoveryBackgroundService>.Instance);
    }

    [Fact]
    public async Task RunsQueuedDiscoveries_AndSurvivesAFailingOne()
    {
        var queue = new ChannelDiscoveryQueue();
        var discoveryService = Substitute.For<IDiscoveryService>();
        var secondRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        discoveryService.RunDiscoveryAsync(new QueuedDiscovery(1, null, true), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));
        discoveryService.RunDiscoveryAsync(new QueuedDiscovery(2, null, true), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                secondRan.SetResult();
                return Task.CompletedTask;
            });
        var jobs = Substitute.For<IJobRepository>();
        jobs.FailActiveJobsAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(1);
        var service = CreateService(queue, discoveryService, jobs);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(new QueuedDiscovery(1, null, true));
        queue.Enqueue(new QueuedDiscovery(2, null, true));
        await secondRan.Task.WaitAsync(WaitTimeout);
        await service.StopAsync(CancellationToken.None);

        await discoveryService.Received(1).RunDiscoveryAsync(new QueuedDiscovery(1, null, true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenRunThrowsUnexpectedly_RetriesUntilTheJobIsMarkedFailed()
    {
        var queue = new ChannelDiscoveryQueue();
        var discoveryService = Substitute.For<IDiscoveryService>();
        discoveryService.RunDiscoveryAsync(new QueuedDiscovery(5, null, true), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));
        var jobs = Substitute.For<IJobRepository>();
        var attempts = 0;
        var marked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        jobs.FailActiveJobsAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempts++;
                if (attempts < 2)
                    return Task.FromException<int>(new InvalidOperationException("db down"));
                marked.SetResult();
                return Task.FromResult(1);
            });
        var service = CreateService(queue, discoveryService, jobs);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(new QueuedDiscovery(5, null, true));
        await marked.Task.WaitAsync(WaitTimeout);
        await service.StopAsync(CancellationToken.None);

        attempts.Should().Be(2);
    }

    [Fact]
    public async Task ExpectedTargetFailure_DoesNotRetryMarkingTheJobFailed()
    {
        var queue = new ChannelDiscoveryQueue();
        var discoveryService = Substitute.For<IDiscoveryService>();
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        discoveryService.RunDiscoveryAsync(new QueuedDiscovery(8, 20, true), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                ran.SetResult();
                return Task.FromException(new FolderNotOnDiskException("dev", "Trips"));
            });
        var jobs = Substitute.For<IJobRepository>();
        var service = CreateService(queue, discoveryService, jobs);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(new QueuedDiscovery(8, 20, true));
        await ran.Task.WaitAsync(WaitTimeout);
        await service.StopAsync(CancellationToken.None);

        await jobs.DidNotReceive().FailActiveJobsAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChannelDiscoveryQueue_Enqueue_ThenReadAllAsync_YieldsTheDiscovery()
    {
        var queue = new ChannelDiscoveryQueue();
        queue.Enqueue(new QueuedDiscovery(7, 3, false));

        await foreach (var discovery in queue.ReadAllAsync())
        {
            discovery.Should().Be(new QueuedDiscovery(7, 3, false));
            break;
        }
    }

    [Fact]
    public void AddWorker_RegistersTheDiscoveryQueueAndBackgroundService()
    {
        var services = new ServiceCollection().AddWorker();

        services.Should().Contain(d => d.ServiceType == typeof(IDiscoveryQueue)
                                       && d.ImplementationType == typeof(ChannelDiscoveryQueue)
                                       && d.Lifetime == ServiceLifetime.Singleton);
        services.Should().Contain(d => d.ServiceType == typeof(IHostedService)
                                       && d.ImplementationType == typeof(DiscoveryBackgroundService));
    }
}
```

Append to `ApplicationServiceCollectionExtensionsTests.cs`, inside the class (add `using PictureManager.Application.Scanning;`):

```csharp
    [Fact]
    public void AddApplication_RegistersTheDiscoveryServiceAsScoped()
    {
        var services = new ServiceCollection().AddApplication();

        services.Should().Contain(d => d.ServiceType == typeof(IDiscoveryService)
                                       && d.ImplementationType == typeof(DiscoveryService)
                                       && d.Lifetime == ServiceLifetime.Scoped);
    }
```

- [ ] **Step 2: Run the tests to confirm the failure**

Run: `dotnet build`
Expected: FAIL: `ChannelDiscoveryQueue` and `DiscoveryBackgroundService` don't exist.

- [ ] **Step 3: Implement**

Create `src/PictureManager.Worker/Scanning/ChannelDiscoveryQueue.cs`:

```csharp
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using PictureManager.Application.Scanning;

namespace PictureManager.Worker.Scanning;

public sealed class ChannelDiscoveryQueue : IDiscoveryQueue
{
    private readonly Channel<QueuedDiscovery> _channel = Channel.CreateUnbounded<QueuedDiscovery>();

    public void Enqueue(QueuedDiscovery discovery) => _channel.Writer.TryWrite(discovery);

    public async IAsyncEnumerable<QueuedDiscovery> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var discovery in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return discovery;
        }
    }
}
```

Create `src/PictureManager.Worker/Scanning/DiscoveryBackgroundService.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;

namespace PictureManager.Worker.Scanning;

/// <summary>Runs queued folder discoveries one at a time, off the HTTP request that started them.</summary>
public sealed class DiscoveryBackgroundService : BackgroundService
{
    private const int MaxFailureRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly IDiscoveryQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<DiscoveryBackgroundService> _logger;

    public DiscoveryBackgroundService(IDiscoveryQueue queue, IServiceScopeFactory scopeFactory, IClock clock, ILogger<DiscoveryBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var discovery in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var discoveryService = scope.ServiceProvider.GetRequiredService<IDiscoveryService>();
                await discoveryService.RunDiscoveryAsync(discovery, stoppingToken);
            }
            catch (Exception ex) when (ex is ScanRootsUnavailableException or FolderUnavailableException or FolderNotOnDiskException)
            {
                // Expected (share not mounted, folder removed while queued, folder gone from disk): the job already
                // carries the message for the UI.
                _logger.LogWarning("Discovery {JobId} failed: {Message}", discovery.JobId, ex.Message);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down: RunDiscoveryAsync already recorded the job as Cancelled.
                break;
            }
            catch (Exception ex)
            {
                // RunDiscoveryAsync's own attempt to record this failure may itself have failed (e.g. the database is
                // down), so retry rather than leave the job Enumerating, which would refuse every later job until the
                // next restart. The loop itself must survive regardless.
                _logger.LogError(ex, "Discovery {JobId} failed", discovery.JobId);
                await MarkFailedWithRetryAsync(discovery.JobId, ex, stoppingToken);
            }
        }
    }

    private async Task MarkFailedWithRetryAsync(int jobId, Exception ex, CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxFailureRetries; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IJobRepository>();
                // Only one job of either kind can be active at a time, so this targets exactly the job that failed.
                await jobs.FailActiveJobsAsync(ex.Message, _clock.UtcNow, CancellationToken.None);
                return;
            }
            catch (Exception retryEx) when (attempt < MaxFailureRetries)
            {
                _logger.LogWarning(retryEx, "Retry {Attempt} failed to record discovery {JobId} failure", attempt, jobId);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }
}
```

In `src/PictureManager.Worker/DependencyInjection/WorkerServiceCollectionExtensions.cs`, add after the scan registrations:

```csharp
        services.AddSingleton<IDiscoveryQueue, ChannelDiscoveryQueue>();
        services.AddHostedService<DiscoveryBackgroundService>();
```

In `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`, add after `IScanService`:

```csharp
        services.AddScoped<IDiscoveryService, DiscoveryService>();
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test tests/PictureManager.Worker.Tests` then `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~ApplicationServiceCollectionExtensionsTests"`
Expected: PASS.

- [ ] **Step 5: Run the whole suite and commit**

Run: `dotnet test`
Expected: all pass.

```bash
git add src/PictureManager.Worker src/PictureManager.Application/DependencyInjection tests/PictureManager.Worker.Tests tests/PictureManager.Application.Tests/DependencyInjection
git commit -m "feat: run folder discoveries in the background"
```

---

### Task 7: The discovery admin API

**Files:**
- Create: `src/PictureManager.Api/Endpoints/DiscoveryEndpoints.cs`
- Modify: `src/PictureManager.Api/Program.cs`
- Test: `tests/PictureManager.Api.Tests/Endpoints/DiscoveryEndpointsTests.cs` (new), `tests/PictureManager.Api.Tests/Smoke/ApiSmokeTests.cs`

**Interfaces:**
- Consumes: `IDiscoveryService`, `IJobRepository`, `Job.Kind`, `FolderUnavailableException`, `ScanAlreadyInProgressException` (Tasks 1, 3, 5, 6).
- Produces: `POST /api/discovery` (`DiscoveryRequest(int? FolderId, bool IsRecursive)` → `DiscoveryStartedResponse(int JobId)`); `GET /api/discovery/{id:int}/events` (payload `DiscoveryProgress(int Id, string Status, int FoldersProcessed, string? ErrorMessage)`).

- [ ] **Step 1: Write the failing tests**

Create `tests/PictureManager.Api.Tests/Endpoints/DiscoveryEndpointsTests.cs`:

```csharp
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class DiscoveryEndpointsTests
{
    private static async Task<string> StreamAsync(IJobRepository jobs, int id)
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;
        await DiscoveryEndpoints.StreamDiscoveryEventsAsync(context, id, jobs, CancellationToken.None);
        return Encoding.UTF8.GetString(body.ToArray());
    }

    [Fact]
    public async Task StartDiscoveryAsync_QueuesAndReturnsTheJobId()
    {
        var discovery = Substitute.For<IDiscoveryService>();
        discovery.QueueDiscoveryAsync(10, true, Arg.Any<CancellationToken>()).Returns(42);

        var result = await DiscoveryEndpoints.StartDiscoveryAsync(new DiscoveryRequest(10, true), discovery, CancellationToken.None);

        result.Should().BeOfType<Ok<DiscoveryStartedResponse>>().Which.Value!.JobId.Should().Be(42);
    }

    [Fact]
    public async Task StartDiscoveryAsync_UnavailableFolder_ReturnsValidationProblemKeyedFolderId()
    {
        var discovery = Substitute.For<IDiscoveryService>();
        discovery.QueueDiscoveryAsync(77, true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(FolderUnavailableException.NotFound(77)));

        var result = await DiscoveryEndpoints.StartDiscoveryAsync(new DiscoveryRequest(77, true), discovery, CancellationToken.None);

        result.Should().BeOfType<ValidationProblem>().Which.ProblemDetails.Errors.Should().ContainKey("folderId");
    }

    [Fact]
    public async Task StartDiscoveryAsync_JobAlreadyActive_ReturnsConflict()
    {
        var discovery = Substitute.For<IDiscoveryService>();
        discovery.QueueDiscoveryAsync(null, true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new ScanAlreadyInProgressException()));

        var result = await DiscoveryEndpoints.StartDiscoveryAsync(new DiscoveryRequest(null, true), discovery, CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task StreamDiscoveryEventsAsync_CompletedJob_WritesOneEventWithoutFileCounts()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Job
        {
            Id = 1, Kind = JobKind.Discovery, Status = JobStatus.Completed, FoldersProcessed = 12
        });

        var written = await StreamAsync(jobs, 1);

        written.Should().Contain("\"Status\":\"Completed\"").And.Contain("\"FoldersProcessed\":12");
        written.Should().NotContain("FilesFound").And.NotContain("FilesEnriched");
    }

    [Fact]
    public async Task StreamDiscoveryEventsAsync_UnknownOrScanJobId_WritesErrorEvent()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Job?)null);
        jobs.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(new Job { Id = 2, Kind = JobKind.Scan, Status = JobStatus.Completed });

        (await StreamAsync(jobs, 1)).Should().Contain("event: error");
        (await StreamAsync(jobs, 2)).Should().Contain("event: error").And.NotContain("\"Status\"");
    }
}
```

In `tests/PictureManager.Api.Tests/Smoke/ApiSmokeTests.cs`, add two rows to the `Endpoint_IsOnTheExpectedSurface` theory:

```csharp
    [InlineData("POST", "/api/discovery", ApiSurface.Admin)]
    [InlineData("GET", "/api/discovery/{id:int}/events", ApiSurface.Admin)]
```

and add this end-to-end test, which runs against the real host, Postgres and background service. Add `using System;`, `using System.IO;`, `using System.Threading;` and `using Microsoft.EntityFrameworkCore;` at the top if missing:

```csharp
    [Fact]
    public async Task Discovery_BuildsTheFolderTreeFromDisk_AndMarksAFolderGoneMissing()
    {
        var mount = Directory.CreateTempSubdirectory("pm-smoke-discovery-");
        try
        {
            Directory.CreateDirectory(Path.Combine(mount.FullName, "Trips", "Madeira"));
            Directory.CreateDirectory(Path.Combine(mount.FullName, "Scans"));
            Directory.CreateDirectory(Path.Combine(mount.FullName, "@eaDir"));

            int topId;
            await using (var context = _fixture.Database.CreateContext())
            {
                var root = TestData.Root("smoke-discovery");
                root.MountPath = mount.FullName;
                var top = TestData.Folder(root, "");
                context.Folders.Add(top);
                await context.SaveChangesAsync();
                topId = top.Id;
            }

            await DiscoverAsync(topId);

            await using (var context = _fixture.Database.CreateContext())
            {
                var paths = await context.Folders.AsNoTracking()
                    .Where(f => f.Root!.Name == "smoke-discovery")
                    .Select(f => f.RelativePath)
                    .ToListAsync();
                paths.Should().BeEquivalentTo("", "Trips", "Trips/Madeira", "Scans");
            }

            Directory.Delete(Path.Combine(mount.FullName, "Scans"));
            await DiscoverAsync(topId);

            await using (var context = _fixture.Database.CreateContext())
            {
                var scans = await context.Folders.AsNoTracking()
                    .SingleAsync(f => f.Root!.Name == "smoke-discovery" && f.RelativePath == "Scans");
                scans.MissingSinceUtc.Should().NotBeNull();
                (await context.Folders.AsNoTracking().SingleAsync(f => f.Id == topId)).ChildrenDiscoveredAt.Should().NotBeNull();
            }
        }
        finally
        {
            mount.Delete(recursive: true);
        }
    }

    private async Task DiscoverAsync(int folderId)
    {
        var start = await _fixture.Client.PostAsJsonAsync("/api/discovery", new { folderId, isRecursive = true });
        start.StatusCode.Should().Be(HttpStatusCode.OK);
        var jobId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetInt32();

        // The stream closes once the job reaches a terminal status.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var events = await _fixture.Client.GetStringAsync($"/api/discovery/{jobId}/events", timeout.Token);
        events.Should().Contain("\"Status\":\"Completed\"");
    }
```

- [ ] **Step 2: Run the tests to confirm the failure**

Run: `dotnet build`
Expected: FAIL: `DiscoveryEndpoints`, `DiscoveryRequest`, `DiscoveryStartedResponse` don't exist.

- [ ] **Step 3: Implement**

Create `src/PictureManager.Api/Endpoints/DiscoveryEndpoints.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;

namespace PictureManager.Api.Endpoints;

public static class DiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapPost("/discovery", StartDiscoveryAsync);
        admin.MapGet("/discovery/{id:int}/events", StreamDiscoveryEventsAsync);
        return admin;
    }

    public static async Task<IResult> StartDiscoveryAsync(DiscoveryRequest request, IDiscoveryService discoveryService, CancellationToken cancellationToken)
    {
        try
        {
            var jobId = await discoveryService.QueueDiscoveryAsync(request.FolderId, request.IsRecursive, cancellationToken);
            return Results.Ok(new DiscoveryStartedResponse(jobId));
        }
        catch (ScanAlreadyInProgressException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
        catch (FolderUnavailableException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["folderId"] = new[] { ex.Message } });
        }
    }

    public static async Task StreamDiscoveryEventsAsync(HttpContext context, int id, IJobRepository jobs, CancellationToken cancellationToken)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        while (!cancellationToken.IsCancellationRequested)
        {
            var job = await jobs.GetByIdAsync(id, cancellationToken);

            // A scan job's id isn't a discovery: same answer as an unknown id (see /api/scans/{id}/events).
            if (job is null || job.Kind != JobKind.Discovery)
            {
                await context.Response.WriteAsync("event: error\ndata: not found\n\n", cancellationToken);
                return;
            }

            var payload = JsonSerializer.Serialize(new DiscoveryProgress(job.Id, job.Status.ToString(), job.FoldersProcessed, job.ErrorMessage));
            await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);

            if (job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                return;

            await Task.Delay(1000, cancellationToken);
        }
    }
}

public sealed record DiscoveryRequest(int? FolderId, bool IsRecursive);
public sealed record DiscoveryStartedResponse(int JobId);
public sealed record DiscoveryProgress(int Id, string Status, int FoldersProcessed, string? ErrorMessage);
```

In `src/PictureManager.Api/Program.cs`, map the endpoints after `admin.MapScanEndpoints();`:

```csharp
    admin.MapDiscoveryEndpoints();
```

and make the startup log line kind-neutral:

```csharp
            Log.Warning("Marked {Count} job(s) interrupted by a restart as failed", interrupted);
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test tests/PictureManager.Api.Tests`
Expected: PASS, including `EveryApiEndpoint_BelongsToExactlyOneSurface`, the two new surface rows, and the end-to-end discovery test.

- [ ] **Step 5: Run the whole suite and commit**

Run: `dotnet test`
Expected: all pass.

```bash
git add src/PictureManager.Api tests/PictureManager.Api.Tests
git commit -m "feat: add the folder discovery admin API"
```

---

### Task 8: Live verification against the real API (shell only)

This task needs the dev Postgres container, `dev-data/images`, and the API running. No browser: this spec is backend only. Record every command and its result. No commit unless something had to change.

- [ ] **Step 1: Prepare**
  1. `docker ps`: the Postgres container must be up (`docker compose up -d db` otherwise).
  2. `dotnet ef database update --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api`. Expected: applies `UnifiedJobsAndFolderDiscovery`; the old `ScanJobs` history is dropped (approved).
  3. Start the API in the background from `src/PictureManager.Api`: `ASPNETCORE_ENVIRONMENT=Development dotnet run`. It listens on `http://localhost:5080`. Wait until `GET /api/ping` returns 200.

- [ ] **Step 2: Shell checks** (`jq` optional; plain `curl -s` output is enough)

| # | Command | Expected |
|---|---|---|
| 1 | `curl -s http://localhost:5080/api/folders/roots` | Includes the `dev` root's top folder |
| 2 | `curl -s -X POST http://localhost:5080/api/discovery -H 'Content-Type: application/json' -d '{"isRecursive":true}'` | `{"jobId":N}` |
| 3 | `curl -s -N --max-time 30 http://localhost:5080/api/discovery/N/events` | Ends with `"Status":"Completed"`, `FoldersProcessed` ≥ the number of folders under `dev-data/images` |
| 4 | `mkdir dev-data/images/Holidays/DiscoveryCheck`, find Holidays' id (`GET /api/folders/{devTopId}/children`), then `POST /api/discovery` with `{"folderId":<Holidays id>,"isRecursive":false}` and wait for completion | `GET /api/folders/<Holidays id>/children` lists `DiscoveryCheck` |
| 5 | `rmdir dev-data/images/Holidays/DiscoveryCheck`, rediscover Holidays the same way | `GET /api/folders/<DiscoveryCheck id>` shows `"isMissing":true` |
| 6 | `curl -s -o /dev/null -w "%{http_code}" -X DELETE http://localhost:5080/api/folders/<DiscoveryCheck id>` | `204` (cleanup: removed from the collection) |
| 7 | `POST /api/scans` with `{"folderId":<Madeira id>,"isRecursive":false}`, then stream `/api/scans/<id>/events` | `"Status":"Completed"` |
| 8 | `POST /api/scans` with `{"rootId":1,"folderId":<Madeira id>,"isRecursive":true}` | `400`, body contains `Give rootId or folderId, not both.` |
| 9 | `curl -s --max-time 5 http://localhost:5080/api/discovery/<scan job id from #7>/events` | `event: error` |
| 10 | `POST /api/discovery` with `{"folderId":999999,"isRecursive":true}` | `400`, keyed `folderId` |

- [ ] **Step 3: Restore and record**

Stop the API. `DiscoveryCheck` should no longer exist on disk (rmdir'd in #5), and its row is tombstoned (#6). Record every result in the progress ledger.

---

## Verification (end to end)

- **Per task:** the task's own tests, then `dotnet test` from the repo root (Postgres must be up).
- **After all tasks:** the full backend suite passes, Task 8's shell checks pass against the real API and database, and then a whole-branch review follows.
