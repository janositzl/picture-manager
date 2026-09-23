# Scanner: Missing Folders, Unavailable Roots, Background Scans — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Folders that vanish from disk are marked missing (never deleted) and hidden from image views. Roots with a missing or empty folder are left untouched and fail the scan with a warning. Scans run in the background, with live progress and restart recovery.

**Architecture:**
- **Data model:** a nullable `Folder.MissingSinceUtc` column. It is set on a vanished folder's whole subtree by one recursive-CTE update, and cleared per folder when the scan finds the folder again.
- **Visibility:** the image rule gains `Folder.MissingSinceUtc == null`. Folders stay in the tree, with an `IsMissing` flag.
- **Scan flow:** `ScanService` splits into:
  - `QueueScanAsync`: validates, creates the job, and enqueues it. Called by the endpoint.
  - `RunScanAsync`: the walk, run by a new `ScanBackgroundService` in the Worker project off a `Channel`, mirroring the existing enrichment pipeline.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core 10 + Npgsql (PostgreSQL 17), xUnit, FluentAssertions 7, NSubstitute.

**Spec:** `docs/superpowers/specs/2026-09-24-scanner-missing-folders-design.md`

## Global Constraints

- Scans are manual only (`POST /api/scans`). There is no schedule and no file-system watcher.
- The scanner never deletes a folder or image because it vanished from disk. It only marks it. Pruning items excluded by settings (phase 5) is unchanged.
- A root is unavailable when its `MountPath` directory does not exist or has no entries at all. Nothing under an unavailable root is created, updated or marked.
- Unavailable-root message, one sentence per root, joined with a single space: `Root '{Name}' is unavailable: its folder is missing or empty. Check that the share is mounted.`
- Restart-recovery message: `Interrupted by an application restart.`
- Walk progress is written every 50 folders.
- Folder-name comparison is `PathNormalizer.Normalize(name).ToLowerInvariant()`, so NFC-normalized and case-insensitive.
- No new NuGet packages. Keep the column naming the migrations already use: quoted PascalCase, e.g. `"MissingSinceUtc"`.
- Every commit ends with the trailer `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

These are the failure modes most likely to bite a user. Each one is pinned by the test named in brackets:

1. **A folder whose on-disk name uses NFD while the stored name is NFC** (a macOS SMB client) must not be marked missing. [Task 2, `…NameDiffersOnlyInCaseOrUnicodeNormalization_IsNotMarked`]
2. **A case-only rename** (`Trip` → `trip`) must neither mark the folder missing nor create a second folder. [same test]
3. **A root deactivated or deleted while its scan waits in the queue** must end the job `Failed` with a message, not leave it stuck `Enumerating`. [Task 3, `RunScanAsync_RootDeactivatedWhileQueued_FailsJobWithMessage`]
4. **An exception thrown by one scan** must not stop the background service; the next queued scan still runs. [Task 3, `ScanBackgroundService_RunsQueuedScans_AndSurvivesAFailingOne`]
5. **Startup recovery** fails only jobs that are `Enumerating`/`Enriching`, and leaves finished jobs untouched. It runs before the server accepts requests (Program.cs ordering), so it can never fail a fresh job. [Task 3, `FailActiveJobsAsync_FailsOnlyEnumeratingAndEnrichingJobs`; reviewer checks the ordering]

---

### Task 1: `Folder.MissingSinceUtc`, subtree marking, visibility and API shape

**Files:**
- Modify: `src/PictureManager.Model/Folder.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Configurations/FolderConfiguration.cs`
- Create: migration `FolderMissingSince` (generated into `src/PictureManager.Infrastructure/Migrations/`)
- Modify: `src/PictureManager.Application/Folders/FolderModels.cs`
- Modify: `src/PictureManager.Application/Repositories/IFolderRepository.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/FolderRepository.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Queries/VisibilityExtensions.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/AlbumRepository.cs:105`
- Modify: `tests/PictureManager.Infrastructure.Tests/Support/TestData.cs`
- Modify (FolderNode arity): `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FolderQueryRepositoryTests.cs:72-73`, `tests/PictureManager.Application.Tests/Folders/FolderServiceTests.cs:38`, `tests/PictureManager.Api.Tests/Endpoints/FolderEndpointsTests.cs:22`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/MissingFolderTests.cs`

**Interfaces:**
- Consumes: existing `FolderRepository`, `ImageQueryRepository`, `AlbumRepository`, `TestData`, `PostgresTestDatabase`.
- Produces:
  - `Folder.MissingSinceUtc` (`DateTime?`)
  - `IFolderRepository.MarkSubtreeMissingAsync(int folderId, DateTime missingSinceUtc, CancellationToken cancellationToken = default)`
  - `FolderNode(int Id, string Name, bool HasChildren, int ImageCount, bool IsMissing)`
  - `FolderDetail(int Id, string Name, int RootId, string RootName, string RelativePath, int ImageCount, bool IsMissing, IReadOnlyList<BreadcrumbItem> Breadcrumb)`
  - `TestData.Folder(..., DateTime? missingSinceUtc = null)`

- [ ] **Step 1: Add the test-data parameter**

In `tests/PictureManager.Infrastructure.Tests/Support/TestData.cs`, replace the `Folder` builder with:

```csharp
    public static Folder Folder(ImageRoot root, string relativePath, Folder? parent = null, bool isActive = true, DateTime? missingSinceUtc = null) => new()
    {
        Root = root,
        Parent = parent,
        Name = relativePath.Length == 0 ? root.Name : relativePath[(relativePath.LastIndexOf('/') + 1)..],
        RelativePath = relativePath,
        IsActive = isActive,
        MissingSinceUtc = missingSinceUtc,
        CreatedUtc = Utc,
        ModifiedUtc = Utc
    };
```

- [ ] **Step 2: Write the failing Postgres tests**

Create `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/MissingFolderTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Folders;
using PictureManager.Application.Images;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class MissingFolderTests
{
    private static readonly DateTime Earlier = new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task MarkSubtreeMissingAsync_MarksFolderAndDescendants_KeepsEarlierDates_SkipsTombstonesAndSiblings()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var trip = TestData.Folder(root, "Trip", top);
        var day1 = TestData.Folder(root, "Trip/Day1", trip);
        var day1Morning = TestData.Folder(root, "Trip/Day1/Morning", day1);
        var day2 = TestData.Folder(root, "Trip/Day2", trip, missingSinceUtc: Earlier);
        var removed = TestData.Folder(root, "Trip/Removed", trip, isActive: false);
        var sibling = TestData.Folder(root, "Other", top);
        db.Context.Folders.AddRange(top, trip, day1, day1Morning, day2, removed, sibling);
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new FolderRepository(context).MarkSubtreeMissingAsync(trip.Id, Now);

        await using var verify = db.CreateContext();
        var missing = await verify.Folders.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.MissingSinceUtc);
        missing[trip.Id].Should().Be(Now);
        missing[day1.Id].Should().Be(Now);
        missing[day1Morning.Id].Should().Be(Now);
        missing[day2.Id].Should().Be(Earlier);
        missing[removed.Id].Should().BeNull();
        missing[sibling.Id].Should().BeNull();
        missing[top.Id].Should().BeNull();
    }

    [Fact]
    public async Task ImagesInAMissingFolder_AreHiddenFromSearchFavoritesDuplicatesDetailAndFavoriteToggle()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var trip = TestData.Folder(root, "Trip", top, missingSinceUtc: Now);
        var tripItaly = TestData.Folder(root, "Trip-Italy", top);
        var hidden = TestData.Image(trip, "IMG_0001", contentHash: "AAAA", isFavorite: true);
        var shown = TestData.Image(tripItaly, "IMG_0001", contentHash: "AAAA", isFavorite: true);
        db.Context.Images.AddRange(hidden, shown);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);

        var byName = await repository.ListAsync(new ImageListFilter(null, null, "IMG_0001", false), ImageSort.Name, SortDirection.Asc, null, 10);
        byName.Select(r => r.Id).Should().Equal(shown.Id);

        var favorites = await repository.ListAsync(new ImageListFilter(null, null, null, true), ImageSort.Date, SortDirection.Desc, null, 10);
        favorites.Select(r => r.Id).Should().Equal(shown.Id);

        // One visible member left, so no duplicate group: a renamed folder no longer doubles every photo.
        (await repository.GetDuplicateGroupsAsync(null, 10)).Should().BeEmpty();
        (await repository.GetVisibleDetailAsync(hidden.Id)).Should().BeNull();
        (await repository.SetFavoriteAsync(hidden.Id, false, Now)).Should().BeFalse();
    }

    [Fact]
    public async Task FolderNodesAndDetail_ReportIsMissing_WithImageCountOfImagesThatWouldComeBack()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var home = TestData.Folder(root, "Home", top);
        var trip = TestData.Folder(root, "Trip", top, missingSinceUtc: Now);
        db.Context.Images.AddRange(
            TestData.Image(home, "a"),
            TestData.Image(trip, "b"),
            TestData.Image(trip, "c", missingSinceUtc: Earlier));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new FolderRepository(context);

        var nodes = await repository.GetVisibleChildrenAsync(top.Id);
        nodes.Should().Equal(
            new FolderNode(home.Id, "Home", HasChildren: false, ImageCount: 1, IsMissing: false),
            new FolderNode(trip.Id, "Trip", HasChildren: false, ImageCount: 1, IsMissing: true));

        var detail = await repository.GetVisibleDetailAsync(trip.Id);
        detail!.IsMissing.Should().BeTrue();
        detail.ImageCount.Should().Be(1);
    }

    [Fact]
    public async Task AlbumImages_InAMissingFolder_StayInTheAlbum_FlaggedMissing()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var trip = TestData.Folder(root, "Trip", top, missingSinceUtc: Now);
        var image = TestData.Image(trip, "IMG_0001");
        var album = TestData.Album("Best");
        db.Context.AlbumImages.Add(TestData.AlbumImage(album, image, 1));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new AlbumRepository(context).ListImagesAsync(album.Id, null, null, 10);

        rows.Should().ContainSingle().Which.IsMissing.Should().BeTrue();
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~MissingFolderTests"`
Expected: build FAILS. `Folder` has no `MissingSinceUtc`, `FolderNode` takes 4 arguments, and `MarkSubtreeMissingAsync` doesn't exist.

- [ ] **Step 4: Model, configuration and migration**

In `src/PictureManager.Model/Folder.cs`, add after `public bool IsActive { get; set; } = true;`:

```csharp
    /// <summary>Set when the scanner finds the folder gone from disk (with its whole subtree); cleared when it's back.</summary>
    public DateTime? MissingSinceUtc { get; set; }
```

In `FolderConfiguration.Configure`, add after the `ModifiedUtc` property block:

```csharp
        builder.Property(x => x.MissingSinceUtc)
            .HasColumnType("timestamp with time zone");
```

Run: `dotnet ef migrations add FolderMissingSince --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api`
Expected: a new migration whose `Up` holds exactly one `AddColumn<DateTime>(name: "MissingSinceUtc", table: "Folders", type: "timestamp with time zone", nullable: true)`, with the matching `DropColumn` in `Down`. If it contains anything else, stop: the model snapshot has drifted. Say so in the report.

- [ ] **Step 5: API shape**

In `src/PictureManager.Application/Folders/FolderModels.cs`, replace the `FolderNode` and `FolderDetail` declarations with:

```csharp
/// <summary>
/// Tree node. ImageCount = images directly in the folder that are not individually missing: for a present
/// folder that is what its grid shows; for a missing folder (IsMissing) it is what comes back if the folder
/// reappears, and what "remove from collection" would purge.
/// </summary>
public sealed record FolderNode(int Id, string Name, bool HasChildren, int ImageCount, bool IsMissing);

public sealed record BreadcrumbItem(int Id, string Name);

/// <summary>Breadcrumb runs from the root's top folder down to (and including) this folder.</summary>
public sealed record FolderDetail(
    int Id,
    string Name,
    int RootId,
    string RootName,
    string RelativePath,
    int ImageCount,
    bool IsMissing,
    IReadOnlyList<BreadcrumbItem> Breadcrumb);
```

(Keep `BreadcrumbItem` and `RemovedFolder` exactly as they are. Only the two records above change.)

Update the three existing test constructions:
- `FolderQueryRepositoryTests.cs:72-73`:
  - `new FolderNode(madeira.Id, "Madeira", HasChildren: true, ImageCount: 2, IsMissing: false),`
  - `new FolderNode(portugal.Id, "portugal", HasChildren: false, ImageCount: 0, IsMissing: false));`
- `FolderServiceTests.cs:38`: `var children = new[] { new FolderNode(5, "a", false, 3, false) };`
- `FolderEndpointsTests.cs:22`: `new FolderNode(1, "nas", true, 0, false)`

- [ ] **Step 6: Repository, visibility and album flag**

In `IFolderRepository`, add after `DeleteSubtreeAsync`:

```csharp
    /// <summary>
    /// Sets MissingSinceUtc on the folder and every active folder beneath it that isn't already marked (earlier
    /// dates are kept). Tombstones (IsActive == false) are left alone. Nothing is deleted.
    /// </summary>
    Task MarkSubtreeMissingAsync(int folderId, DateTime missingSinceUtc, CancellationToken cancellationToken = default);
```

Add `using System;` at the top of `IFolderRepository.cs`.

In `FolderRepository`, add after `DeleteSubtreeAsync`:

```csharp
    public async Task MarkSubtreeMissingAsync(int folderId, DateTime missingSinceUtc, CancellationToken cancellationToken = default)
    {
        // One statement for the whole subtree: image visibility checks each folder's own flag, not its
        // ancestors', so every descendant must carry the mark.
        await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            WITH RECURSIVE subtree AS (
                SELECT "Id" FROM "Folders" WHERE "Id" = {folderId}
                UNION ALL
                SELECT f."Id" FROM "Folders" f JOIN subtree s ON f."ParentId" = s."Id"
            )
            UPDATE "Folders" SET "MissingSinceUtc" = {missingSinceUtc}
            WHERE "Id" IN (SELECT "Id" FROM subtree) AND "MissingSinceUtc" IS NULL AND "IsActive"
            """, cancellationToken);
    }
```

Add `using System;` to `FolderRepository.cs` if it is not already there.

In `FolderRepository.ToNodes`, replace the `Select` with:

```csharp
            .Select(f => new FolderNode(
                f.Id,
                f.Name,
                f.Children.Any(c => c.IsActive),
                f.Images.Count(i => i.MissingSinceUtc == null),
                f.MissingSinceUtc != null));
```

In `FolderRepository.GetVisibleDetailAsync`:
- add `IsMissing = f.MissingSinceUtc != null` to the anonymous projection, after `ImageCount = …`;
- change the return to:

```csharp
        return new FolderDetail(folder.Id, folder.Name, folder.RootId, folder.RootName, folder.RelativePath,
            folder.ImageCount, folder.IsMissing, breadcrumb);
```

In `VisibilityExtensions.cs`, replace the image rule and update the summary:

```csharp
/// <summary>The visibility rule, in one place (phase 5, plus missing folders).</summary>
internal static class VisibilityExtensions
{
    public static IQueryable<Image> WhereVisible(this IQueryable<Image> images) =>
        images.Where(i => i.MissingSinceUtc == null && i.Folder!.IsActive && i.Folder.MissingSinceUtc == null && i.Folder.Root!.IsActive);

    /// <summary>Missing folders stay visible (flagged IsMissing) so the user can see and act on them.</summary>
    public static IQueryable<Folder> WhereVisible(this IQueryable<Folder> folders) =>
        folders.Where(f => f.IsActive && f.Root!.IsActive);
}
```

In `AlbumRepository.cs:105`, replace
`ai.Image.MissingSinceUtc != null || !ai.Image.Folder!.IsActive || !ai.Image.Folder.Root!.IsActive))`
with
`ai.Image.MissingSinceUtc != null || !ai.Image.Folder!.IsActive || ai.Image.Folder.MissingSinceUtc != null || !ai.Image.Folder.Root!.IsActive))`

- [ ] **Step 7: Run the tests, then the full suite**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~MissingFolderTests"`
Expected: PASS, 4 tests.

Run: `dotnet test`
Expected: 0 failed.

- [ ] **Step 8: Apply the migration to the dev database**

Run: `dotnet ef database update --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api`
Expected: `Applying migration '…_FolderMissingSince'. Done.`

- [ ] **Step 9: Commit**

```bash
git add src/PictureManager.Model/Folder.cs src/PictureManager.Infrastructure/Persistence/Configurations/FolderConfiguration.cs src/PictureManager.Infrastructure/Migrations src/PictureManager.Application/Folders/FolderModels.cs src/PictureManager.Application/Repositories/IFolderRepository.cs src/PictureManager.Infrastructure/Persistence/Repositories/FolderRepository.cs src/PictureManager.Infrastructure/Persistence/Queries/VisibilityExtensions.cs src/PictureManager.Infrastructure/Persistence/Repositories/AlbumRepository.cs tests/PictureManager.Infrastructure.Tests/Support/TestData.cs tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/MissingFolderTests.cs tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FolderQueryRepositoryTests.cs tests/PictureManager.Application.Tests/Folders/FolderServiceTests.cs tests/PictureManager.Api.Tests/Endpoints/FolderEndpointsTests.cs
git commit -m "feat: add Folder.MissingSinceUtc, subtree marking, and hide images in missing folders"
```

---

### Task 2: Scanner: unavailable roots, missing-folder marking, live progress

**Files:**
- Create: `src/PictureManager.Application/Scanning/ScanRootsUnavailableException.cs`
- Modify: `src/PictureManager.Application/Scanning/ScanService.cs` (`StartScanAsync` roots loop; `ScanRootAsync` rewritten in full; new helpers)
- Modify: `tests/PictureManager.Application.Tests/Scanning/ScanServiceTests.cs` (the `FileNoLongerPresent` test only)
- Test: `tests/PictureManager.Application.Tests/Scanning/ScanServicePhase5Tests.cs` (fixture mocks plus new tests)

**Interfaces:**
- Consumes: `IFolderRepository.MarkSubtreeMissingAsync`, `Folder.MissingSinceUtc` (Task 1); existing `IScanJobRepository.SetEnumerationResultAsync`.
- Produces: `ScanRootsUnavailableException(IReadOnlyList<string> rootNames)` with `IReadOnlyList<string> RootNames`. `StartScanAsync` throws it after scanning the available roots, and the job is recorded `Failed` with its message.

Note: until Task 3, `POST /api/scans` still awaits the walk, so an unavailable root surfaces there as a 500. Task 3 moves the walk off the request.

- [ ] **Step 1: Keep the existing missing-file test meaningful**

The test `StartScanAsync_FileNoLongerPresent_MarksExistingImageMissing` in `ScanServiceTests.cs` scans an empty root folder, which is now "unavailable". Give it an entry that the scan ignores:
- after `var tempRoot = Directory.CreateTempSubdirectory("pm-scan-test-");` and `try {`, add:

```csharp
            // An ignored file keeps the root non-empty (an empty root is treated as an unmounted share).
            await File.WriteAllBytesAsync(Path.Combine(tempRoot.FullName, "readme.txt"), new byte[] { 1 });
```

- in the same test, change `.Returns(new AppSettings());` to `.Returns(new AppSettings { ExcludedExtensions = new List<string> { ".txt" } });`

- [ ] **Step 2: Write the failing scanner tests**

In `ScanServicePhase5Tests.cs`, add these lines at the end of the constructor, so newly found files and folders get rows:

```csharp
        _images.AddAsync(Arg.Any<Image>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var image = call.Arg<Image>();
            image.Id = 70;
            return image;
        });
        _folders.AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var folder = call.Arg<Folder>();
            folder.Id = 60;
            return folder;
        });
```

Then add these tests to the class:

```csharp
    private const string DevUnavailable = "Root 'dev' is unavailable: its folder is missing or empty. Check that the share is mounted.";

    [Fact]
    public async Task StartScanAsync_RootFolderMissing_ChangesNothing_AndFailsWithMessage()
    {
        _roots.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ImageRoot { Id = 1, Name = "dev", MountPath = Path.Combine(_tempRoot.FullName, "not-mounted"), IsActive = true });

        var act = () => CreateService().StartScanAsync(rootId: 1, isRecursive: true);

        (await act.Should().ThrowAsync<ScanRootsUnavailableException>()).Which.RootNames.Should().Equal("dev");
        await _jobs.Received(1).SetFailureResultAsync(999, 0, 0, DevUnavailable, ScanJobStatus.Failed, _clock.UtcNow, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().GetByRootAndRelativePathAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _images.DidNotReceive().GetByFolderIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_RootFolderEmpty_ChangesNothing_AndFailsWithMessage()
    {
        // The fixture's temp root starts empty: exactly what an unmounted share's mount point looks like.
        var act = () => CreateService().StartScanAsync(rootId: 1, isRecursive: true);

        await act.Should().ThrowAsync<ScanRootsUnavailableException>();
        await _jobs.Received(1).SetFailureResultAsync(999, 0, 0, DevUnavailable, ScanJobStatus.Failed, _clock.UtcNow, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().GetByRootAndRelativePathAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _images.DidNotReceive().GetByFolderIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_AllRoots_OneUnavailable_ScansTheOthers_ThenFails()
    {
        await File.WriteAllBytesAsync(Path.Combine(_tempRoot.FullName, "a.jpg"), new byte[] { 1 });
        _roots.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ImageRoot>
        {
            new() { Id = 1, Name = "dev", MountPath = _tempRoot.FullName, IsActive = true },
            new() { Id = 2, Name = "nas", MountPath = Path.Combine(_tempRoot.FullName, "not-mounted"), IsActive = true }
        });

        var act = () => CreateService().StartScanAsync(rootId: null, isRecursive: true);

        (await act.Should().ThrowAsync<ScanRootsUnavailableException>()).Which.RootNames.Should().Equal("nas");
        await _images.Received(1).AddAsync(Arg.Is<Image>(i => i.FileName == "a"), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().GetByRootAndRelativePathAsync(2, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetFailureResultAsync(999, 1, 1,
            "Root 'nas' is unavailable: its folder is missing or empty. Check that the share is mounted.",
            ScanJobStatus.Failed, _clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_VanishedChildFolder_IsMarkedMissingWithItsSubtree()
    {
        await File.WriteAllBytesAsync(Path.Combine(_tempRoot.FullName, "a.jpg"), new byte[] { 1 });
        _folders.GetChildrenAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Folder>
        {
            new() { Id = 40, RootId = 1, ParentId = 10, Name = "Trip", RelativePath = "Trip" }
        });

        await CreateService().StartScanAsync(rootId: 1, isRecursive: true);

        await _folders.Received(1).MarkSubtreeMissingAsync(40, _clock.UtcNow, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().DeleteSubtreeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_RenamedChildFolder_OldIsMarkedMissing_NewIsIndexed()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Trip-Italy"));
        var renamed = new Folder { Id = 41, RootId = 1, ParentId = 10, Name = "Trip-Italy", RelativePath = "Trip-Italy" };
        _folders.GetByRootAndRelativePathAsync(1, "Trip-Italy", Arg.Any<CancellationToken>()).Returns(renamed);
        _folders.GetChildrenAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Folder>
        {
            new() { Id = 40, RootId = 1, ParentId = 10, Name = "Trip", RelativePath = "Trip" },
            renamed
        });

        await CreateService().StartScanAsync(rootId: 1, isRecursive: true);

        await _folders.Received(1).MarkSubtreeMissingAsync(40, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(41, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_MissingFolderBackOnDisk_IsUnmarked()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Trip"));
        var trip = new Folder { Id = 40, RootId = 1, ParentId = 10, Name = "Trip", RelativePath = "Trip", MissingSinceUtc = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc) };
        _folders.GetByRootAndRelativePathAsync(1, "Trip", Arg.Any<CancellationToken>()).Returns(trip);
        _folders.GetChildrenAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Folder> { trip });

        await CreateService().StartScanAsync(rootId: 1, isRecursive: true);

        await _folders.Received(1).UpdateAsync(Arg.Is<Folder>(f => f.Id == 40 && f.MissingSinceUtc == null), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_AlreadyMissingOrRemovedChildren_AreNotMarkedAgain()
    {
        await File.WriteAllBytesAsync(Path.Combine(_tempRoot.FullName, "a.jpg"), new byte[] { 1 });
        _folders.GetChildrenAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Folder>
        {
            new() { Id = 42, RootId = 1, ParentId = 10, Name = "Old", RelativePath = "Old", MissingSinceUtc = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc) },
            new() { Id = 43, RootId = 1, ParentId = 10, Name = "Removed", RelativePath = "Removed", IsActive = false }
        });

        await CreateService().StartScanAsync(rootId: 1, isRecursive: true);

        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_ChildNameDiffersOnlyInCaseOrUnicodeNormalization_IsNotMarked()
    {
        // On disk: "trip" (case differs) and "Cafe" + combining acute (NFD, as macOS SMB clients write it).
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "trip"));
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Café"));
        var trip = new Folder { Id = 44, RootId = 1, ParentId = 10, Name = "Trip", RelativePath = "Trip" };
        var cafe = new Folder { Id = 45, RootId = 1, ParentId = 10, Name = "Café", RelativePath = "Café" };
        _folders.GetByRootAndRelativePathAsync(1, "trip", Arg.Any<CancellationToken>()).Returns(trip);
        _folders.GetByRootAndRelativePathAsync(1, "Café", Arg.Any<CancellationToken>()).Returns(cafe);
        _folders.GetChildrenAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Folder> { trip, cafe });

        await CreateService().StartScanAsync(rootId: 1, isRecursive: true);

        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_NonRecursive_DoesNotCheckGrandchildren()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Trip"));
        var trip = new Folder { Id = 40, RootId = 1, ParentId = 10, Name = "Trip", RelativePath = "Trip" };
        _folders.GetByRootAndRelativePathAsync(1, "Trip", Arg.Any<CancellationToken>()).Returns(trip);
        _folders.GetChildrenAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Folder> { trip });

        await CreateService().StartScanAsync(rootId: 1, isRecursive: false);

        await _folders.DidNotReceive().GetChildrenAsync(40, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_WritesProgressEvery50Folders()
    {
        for (var i = 0; i < 50; i++)
            Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, $"f{i:D2}"));

        await CreateService().StartScanAsync(rootId: 1, isRecursive: true);

        // 51 folders in total (root + 50): one progress write at 50, then the final result.
        await _jobs.Received(1).SetEnumerationResultAsync(999, 50, 0, Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetEnumerationResultAsync(999, 51, 0, Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~ScanServicePhase5Tests"`
Expected: build FAILS because `ScanRootsUnavailableException` does not exist.

- [ ] **Step 4: The exception**

Create `src/PictureManager.Application/Scanning/ScanRootsUnavailableException.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace PictureManager.Application.Scanning;

/// <summary>
/// Roots selected for a scan whose folder was missing or empty (usually an unmounted share). Nothing under them
/// was changed; the other roots were scanned, and the job ends Failed with this message.
/// </summary>
public sealed class ScanRootsUnavailableException : Exception
{
    public ScanRootsUnavailableException(IReadOnlyList<string> rootNames)
        : base(string.Join(" ", rootNames.Select(name =>
            $"Root '{name}' is unavailable: its folder is missing or empty. Check that the share is mounted.")))
    {
        RootNames = rootNames;
    }

    public IReadOnlyList<string> RootNames { get; }
}
```

- [ ] **Step 5: `StartScanAsync` roots loop**

In `ScanService.cs`, add `private const int ProgressInterval = 50;` next to `MaxErrorMessageLength`.

Inside `StartScanAsync`'s `try`, replace:

```csharp
            foreach (var root in roots)
            {
                var (rootFoldersScanned, rootFilesFound) = await ScanRootAsync(root, isRecursive, excludeRules, scanJob.Id, cancellationToken);
                foldersScanned += rootFoldersScanned;
                filesFound += rootFilesFound;
            }

            await FinalizeSuccessAsync(scanJob.Id, foldersScanned, filesFound, cancellationToken);
```

with:

```csharp
            var unavailableRoots = new List<string>();

            foreach (var root in roots)
            {
                // A missing or empty mount folder is a mount problem, not a deletion: change nothing under it.
                if (!IsRootAvailable(root.MountPath))
                {
                    unavailableRoots.Add(root.Name);
                    continue;
                }

                var (rootFoldersScanned, rootFilesFound) = await ScanRootAsync(
                    root, isRecursive, excludeRules, scanJob.Id, foldersScanned, filesFound, cancellationToken);
                foldersScanned += rootFoldersScanned;
                filesFound += rootFilesFound;
            }

            // The available roots were scanned in full; the job still fails so the user sees the warning.
            if (unavailableRoots.Count > 0)
                throw new ScanRootsUnavailableException(unavailableRoots);

            await FinalizeSuccessAsync(scanJob.Id, foldersScanned, filesFound, cancellationToken);
```

- [ ] **Step 6: Rewrite `ScanRootAsync` and add the helpers**

Replace the whole `ScanRootAsync` method with the version below. The file-reconcile branch is unchanged.

```csharp
    private async Task<(int FoldersScanned, int FilesFound)> ScanRootAsync(
        ImageRoot root, bool isRecursive, ScanExcludeRules excludeRules, int scanJobId,
        int foldersBefore, int filesBefore, CancellationToken cancellationToken)
    {
        var foldersScanned = 0;
        var filesFound = 0;

        var rootFolder = await GetOrCreateFolderAsync(root.Id, parentId: null, relativePath: string.Empty, name: root.Name, cancellationToken);
        var pending = new Queue<(Folder Folder, string PhysicalPath, int Depth)>();
        pending.Enqueue((rootFolder, root.MountPath, 0));

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
                    observedFolders.Add(FolderNameKey(name));

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
                    if (isRecursive && depth < MaxScanDepth)
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
                    var fileModifiedUtc = TruncateToMicroseconds(fileInfo.LastWriteTimeUtc);
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
                if (child.IsActive && child.MissingSinceUtc is null && !observedFolders.Contains(FolderNameKey(child.Name)))
                    await _folderRepository.MarkSubtreeMissingAsync(child.Id, _clock.UtcNow, cancellationToken);
            }
        }

        return (foldersScanned, filesFound);
    }

    // A root whose folder is missing or has no entries at all is treated as an unmounted share.
    private static bool IsRootAvailable(string mountPath) =>
        Directory.Exists(mountPath) && Directory.EnumerateFileSystemEntries(mountPath).Any();

    // Same folding as the scanner's path comparisons: NFC, case-insensitive.
    private static string FolderNameKey(string name) => PathNormalizer.Normalize(name).ToLowerInvariant();
```

- [ ] **Step 7: Run the scanner tests, then the full suite**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~Scanning"`
Expected: PASS: every ScanServiceTests and ScanServicePhase5Tests test, including the 10 new ones.

Run: `dotnet test`
Expected: 0 failed.

- [ ] **Step 8: Commit**

```bash
git add src/PictureManager.Application/Scanning/ScanRootsUnavailableException.cs src/PictureManager.Application/Scanning/ScanService.cs tests/PictureManager.Application.Tests/Scanning/ScanServiceTests.cs tests/PictureManager.Application.Tests/Scanning/ScanServicePhase5Tests.cs
git commit -m "feat: scanner marks vanished folders missing and leaves unavailable roots untouched"
```

---

### Task 3: Background scans, error message on the event stream, restart recovery

**Files:**
- Create: `src/PictureManager.Application/Scanning/QueuedScan.cs`
- Create: `src/PictureManager.Application/Scanning/IScanQueue.cs`
- Modify: `src/PictureManager.Application/Scanning/IScanService.cs`
- Modify: `src/PictureManager.Application/Scanning/ScanService.cs` (constructor; `StartScanAsync` replaced by `QueueScanAsync` + `RunScanAsync`; `FailInterruptedJobsAsync`)
- Modify: `src/PictureManager.Application/Repositories/IScanJobRepository.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/ScanJobRepository.cs`
- Create: `src/PictureManager.Worker/Scanning/ChannelScanQueue.cs`
- Create: `src/PictureManager.Worker/Scanning/ScanBackgroundService.cs`
- Modify: `src/PictureManager.Worker/DependencyInjection/WorkerServiceCollectionExtensions.cs`
- Modify: `src/PictureManager.Api/Endpoints/ScanEndpoints.cs`
- Modify: `src/PictureManager.Api/Program.cs`
- Create: `tests/PictureManager.Application.Tests/Scanning/ScanServiceTestExtensions.cs`
- Modify: `tests/PictureManager.Application.Tests/Scanning/ScanServiceTests.cs`, `tests/PictureManager.Application.Tests/Scanning/ScanServicePhase5Tests.cs` (mechanical: constructor argument + `ScanNowAsync`)
- Test: `tests/PictureManager.Application.Tests/Scanning/ScanServiceQueueTests.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ScanJobRepositoryTests.cs` (one test added)
- Test: `tests/PictureManager.Worker.Tests/Scanning/ScanBackgroundServiceTests.cs`
- Test: `tests/PictureManager.Api.Tests/Endpoints/ScanEndpointsTests.cs`

**Interfaces:**
- Consumes: `ScanRootsUnavailableException` and the Task 2 walk.
- Produces:
  - `sealed record QueuedScan(int ScanJobId, int? RootId, bool IsRecursive)`
  - `IScanQueue { void Enqueue(QueuedScan scan); IAsyncEnumerable<QueuedScan> ReadAllAsync(CancellationToken cancellationToken = default); }`
  - `IScanService`:
    - `Task<int> QueueScanAsync(int? rootId, bool isRecursive, CancellationToken cancellationToken = default)`
    - `Task RunScanAsync(QueuedScan scan, CancellationToken cancellationToken = default)`
    - `Task<int> FailInterruptedJobsAsync(CancellationToken cancellationToken = default)`
  - `ScanService(IImageRootRepository, IFolderRepository, IImageRepository, IAppSettingsRepository, IScanJobRepository, IEnrichmentQueue, IScanQueue, IClock)`
  - `IScanJobRepository.FailActiveJobsAsync(string errorMessage, DateTime completedUtc, CancellationToken cancellationToken = default)` → `Task<int>`
  - `ScanProgress(int Id, string Status, int FoldersScanned, int FilesFound, int FilesEnriched, string? ErrorMessage)`

- [ ] **Step 1: Write the failing tests**

Create `tests/PictureManager.Application.Tests/Scanning/ScanServiceTestExtensions.cs`:

```csharp
using System.Threading.Tasks;
using PictureManager.Application.Scanning;

namespace PictureManager.Application.Tests.Scanning;

internal static class ScanServiceTestExtensions
{
    /// <summary>Queue + run in one call: what ScanBackgroundService does, minus the queue hop.</summary>
    public static async Task<int> ScanNowAsync(this ScanService scanService, int? rootId, bool isRecursive)
    {
        var scanJobId = await scanService.QueueScanAsync(rootId, isRecursive);
        await scanService.RunScanAsync(new QueuedScan(scanJobId, rootId, isRecursive));
        return scanJobId;
    }
}
```

Mechanical updates to the existing scanner tests:
- **`ScanServiceTests.cs`:**
  - in every `new ScanService(...)` call, insert `Substitute.For<IScanQueue>(), ` right after the enrichment-queue argument (`enrichmentQueue, ` or `Substitute.For<IEnrichmentQueue>(), `). There are 9 calls.
  - replace every `scanService.StartScanAsync(` with `scanService.ScanNowAsync(`.
- **`ScanServicePhase5Tests.cs`:**
  - change `CreateService()` to `new(_roots, _folders, _images, _settings, _jobs, _queue, Substitute.For<IScanQueue>(), _clock)`.
  - replace every `CreateService().StartScanAsync(` with `CreateService().ScanNowAsync(`.

Create `tests/PictureManager.Application.Tests/Scanning/ScanServiceQueueTests.cs`:

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

public sealed class ScanServiceQueueTests : IDisposable
{
    private readonly DirectoryInfo _tempRoot = Directory.CreateTempSubdirectory("pm-scan-queue-");
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly IImageRepository _images = Substitute.For<IImageRepository>();
    private readonly IAppSettingsRepository _settings = Substitute.For<IAppSettingsRepository>();
    private readonly IScanJobRepository _jobs = Substitute.For<IScanJobRepository>();
    private readonly IScanQueue _scanQueue = Substitute.For<IScanQueue>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public ScanServiceQueueTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _roots.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ImageRoot { Id = 1, Name = "dev", MountPath = _tempRoot.FullName, IsActive = true });
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings());
        _jobs.AddAsync(Arg.Any<ScanJob>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var job = call.Arg<ScanJob>();
            job.Id = 999;
            return job;
        });
    }

    public void Dispose() => _tempRoot.Delete(recursive: true);

    private ScanService CreateService() =>
        new(_roots, _folders, _images, _settings, _jobs, Substitute.For<IEnrichmentQueue>(), _scanQueue, _clock);

    [Fact]
    public async Task QueueScanAsync_CreatesEnumeratingJob_EnqueuesIt_AndDoesNotWalk()
    {
        await File.WriteAllBytesAsync(Path.Combine(_tempRoot.FullName, "a.jpg"), new byte[] { 1 });

        var scanJobId = await CreateService().QueueScanAsync(rootId: 1, isRecursive: true);

        scanJobId.Should().Be(999);
        await _jobs.Received(1).AddAsync(Arg.Is<ScanJob>(j => j.Status == ScanJobStatus.Enumerating && j.IsRecursive), Arg.Any<CancellationToken>());
        _scanQueue.Received(1).Enqueue(new QueuedScan(999, 1, true));
        await _folders.DidNotReceive().GetByRootAndRelativePathAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueueScanAsync_ScanAlreadyActive_ThrowsAndEnqueuesNothing()
    {
        _jobs.HasActiveJobAsync(Arg.Any<CancellationToken>()).Returns(true);

        var act = () => CreateService().QueueScanAsync(rootId: 1, isRecursive: true);

        await act.Should().ThrowAsync<ScanAlreadyInProgressException>();
        _scanQueue.DidNotReceive().Enqueue(Arg.Any<QueuedScan>());
    }

    [Fact]
    public async Task RunScanAsync_RootDeactivatedWhileQueued_FailsJobWithMessage()
    {
        _roots.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ImageRoot { Id = 1, Name = "dev", MountPath = _tempRoot.FullName, IsActive = false });

        var act = () => CreateService().RunScanAsync(new QueuedScan(999, 1, true));

        await act.Should().ThrowAsync<ScanRootUnavailableException>();
        await _jobs.Received(1).SetFailureResultAsync(999, 0, 0, "Image root 1 does not exist or is inactive.",
            ScanJobStatus.Failed, _clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailInterruptedJobsAsync_FailsActiveJobsWithTheRestartMessage()
    {
        // Hoisted: reading a substitute's property while building another substitute's call spec confuses NSubstitute.
        var now = _clock.UtcNow;
        _jobs.FailActiveJobsAsync("Interrupted by an application restart.", now, Arg.Any<CancellationToken>()).Returns(2);

        (await CreateService().FailInterruptedJobsAsync()).Should().Be(2);
    }
}
```

Add to `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ScanJobRepositoryTests.cs`, following the file's existing Postgres-backed tests:

```csharp
    [Fact]
    public async Task FailActiveJobsAsync_FailsOnlyEnumeratingAndEnrichingJobs()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var startedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var completedUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var enumerating = new ScanJob { Status = ScanJobStatus.Enumerating, StartedUtc = startedUtc };
        var enriching = new ScanJob { Status = ScanJobStatus.Enriching, StartedUtc = startedUtc };
        var completed = new ScanJob { Status = ScanJobStatus.Completed, StartedUtc = startedUtc, CompletedUtc = startedUtc };
        var failed = new ScanJob { Status = ScanJobStatus.Failed, StartedUtc = startedUtc, CompletedUtc = startedUtc, ErrorMessage = "earlier" };
        db.Context.ScanJobs.AddRange(enumerating, enriching, completed, failed);
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            (await new ScanJobRepository(context).FailActiveJobsAsync("Interrupted by an application restart.", completedUtc)).Should().Be(2);

        await using var verify = db.CreateContext();
        var jobs = await verify.ScanJobs.AsNoTracking().ToDictionaryAsync(j => j.Id);
        jobs[enumerating.Id].Status.Should().Be(ScanJobStatus.Failed);
        jobs[enumerating.Id].ErrorMessage.Should().Be("Interrupted by an application restart.");
        jobs[enumerating.Id].CompletedUtc.Should().Be(completedUtc);
        jobs[enriching.Id].Status.Should().Be(ScanJobStatus.Failed);
        jobs[completed.Id].Status.Should().Be(ScanJobStatus.Completed);
        jobs[failed.Id].ErrorMessage.Should().Be("earlier");
    }
```

(Add `using System.Linq;` if the file lacks it; `ToDictionaryAsync` comes from `Microsoft.EntityFrameworkCore`.)

Create `tests/PictureManager.Worker.Tests/Scanning/ScanBackgroundServiceTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PictureManager.Application.Scanning;
using PictureManager.Worker.Scanning;
using Xunit;

namespace PictureManager.Worker.Tests.Scanning;

public class ScanBackgroundServiceTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ScanBackgroundService_RunsQueuedScans_AndSurvivesAFailingOne()
    {
        var queue = new ChannelScanQueue();
        var scanService = Substitute.For<IScanService>();
        var secondRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scanService.RunScanAsync(new QueuedScan(1, null, true), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));
        scanService.RunScanAsync(new QueuedScan(2, null, true), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                secondRan.SetResult();
                return Task.CompletedTask;
            });

        var provider = new ServiceCollection().AddScoped(_ => scanService).BuildServiceProvider();
        var service = new ScanBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ScanBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(new QueuedScan(1, null, true));
        queue.Enqueue(new QueuedScan(2, null, true));

        await secondRan.Task.WaitAsync(WaitTimeout);
        await service.StopAsync(CancellationToken.None);

        await scanService.Received(1).RunScanAsync(new QueuedScan(1, null, true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChannelScanQueue_Enqueue_ThenReadAllAsync_YieldsTheScan()
    {
        var queue = new ChannelScanQueue();
        queue.Enqueue(new QueuedScan(7, 1, false));

        await foreach (var scan in queue.ReadAllAsync())
        {
            scan.Should().Be(new QueuedScan(7, 1, false));
            break;
        }
    }
}
```

In `tests/PictureManager.Api.Tests/Endpoints/ScanEndpointsTests.cs`:
- replace both `scanService.StartScanAsync(` setups with `scanService.QueueScanAsync(`, keeping the same arguments and return values;
- add:

```csharp
    [Fact]
    public async Task StreamScanEventsAsync_FailedJob_IncludesTheErrorMessage()
    {
        var scanJobRepository = Substitute.For<IScanJobRepository>();
        scanJobRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new ScanJob
        {
            Id = 1,
            Status = ScanJobStatus.Failed,
            ErrorMessage = "Root 'dev' is unavailable: its folder is missing or empty. Check that the share is mounted."
        });

        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        await ScanEndpoints.StreamScanEventsAsync(context, 1, scanJobRepository, CancellationToken.None);

        var written = Encoding.UTF8.GetString(body.ToArray());
        written.Should().Contain("\"Status\":\"Failed\"");
        written.Should().Contain("\"ErrorMessage\":");
        written.Should().Contain("is unavailable: its folder is missing or empty");
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build`
Expected: FAILS. The build reports that `IScanQueue`, `QueuedScan`, `QueueScanAsync`, `RunScanAsync`, `FailActiveJobsAsync`, `ChannelScanQueue` and `ScanBackgroundService` do not exist.

- [ ] **Step 3: Application contracts**

Create `src/PictureManager.Application/Scanning/QueuedScan.cs`:

```csharp
namespace PictureManager.Application.Scanning;

/// <summary>A scan whose job row exists (Enumerating) and whose walk waits for ScanBackgroundService.</summary>
public sealed record QueuedScan(int ScanJobId, int? RootId, bool IsRecursive);
```

Create `src/PictureManager.Application/Scanning/IScanQueue.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;

namespace PictureManager.Application.Scanning;

public interface IScanQueue
{
    void Enqueue(QueuedScan scan);
    IAsyncEnumerable<QueuedScan> ReadAllAsync(CancellationToken cancellationToken = default);
}
```

Replace `src/PictureManager.Application/Scanning/IScanService.cs` with:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Scanning;

public interface IScanService
{
    /// <summary>
    /// Validates the request, creates the job (Enumerating, so a second scan is refused while this one waits) and
    /// queues the walk. Returns the job id at once. Throws ScanAlreadyInProgressException or
    /// ScanRootUnavailableException.
    /// </summary>
    Task<int> QueueScanAsync(int? rootId, bool isRecursive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks a queued scan and records the outcome on its job (Enriching/Completed, Failed with a message, or
    /// Cancelled). Rethrows the failure after recording it.
    /// </summary>
    Task RunScanAsync(QueuedScan scan, CancellationToken cancellationToken = default);

    /// <summary>Fails jobs a previous process left Enumerating/Enriching. Call once at startup. Returns how many.</summary>
    Task<int> FailInterruptedJobsAsync(CancellationToken cancellationToken = default);
}
```

In `IScanJobRepository`, add after `HasActiveJobAsync`:

```csharp
    /// <summary>Sets every Enumerating or Enriching job to Failed with the message and CompletedUtc. Returns how many changed.</summary>
    Task<int> FailActiveJobsAsync(string errorMessage, DateTime completedUtc, CancellationToken cancellationToken = default);
```

In `ScanJobRepository`, add after `HasActiveJobAsync`:

```csharp
    public async Task<int> FailActiveJobsAsync(string errorMessage, DateTime completedUtc, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ScanJobs
            .Where(j => j.Status == ScanJobStatus.Enumerating || j.Status == ScanJobStatus.Enriching)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, ScanJobStatus.Failed)
                .SetProperty(j => j.ErrorMessage, errorMessage)
                .SetProperty(j => j.CompletedUtc, completedUtc),
                cancellationToken);
    }
```

- [ ] **Step 4: Split `ScanService`**

In `ScanService.cs`:
- Add the field `private readonly IScanQueue _scanQueue;` and the constant `private const string InterruptedMessage = "Interrupted by an application restart.";`.
- Change the constructor to take `IScanQueue scanQueue` after `IEnrichmentQueue enrichmentQueue` and assign it.
- Replace the whole `StartScanAsync` method with the three methods below. `ScanRootAsync`, the finalize methods, `GetActiveRootAsync` and the helpers stay as Task 2 left them.

```csharp
    public async Task<int> QueueScanAsync(int? rootId, bool isRecursive, CancellationToken cancellationToken = default)
    {
        if (await _scanJobRepository.HasActiveJobAsync(cancellationToken))
            throw new ScanAlreadyInProgressException();

        if (rootId.HasValue)
            await GetActiveRootAsync(rootId.Value, cancellationToken);

        // Created as Enumerating (not Pending) so HasActiveJobAsync refuses a second scan while this one waits
        // in the queue.
        var scanJob = await _scanJobRepository.AddAsync(new ScanJob
        {
            IsRecursive = isRecursive,
            Status = ScanJobStatus.Enumerating,
            StartedUtc = _clock.UtcNow
        }, cancellationToken);

        _scanQueue.Enqueue(new QueuedScan(scanJob.Id, rootId, isRecursive));
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
            // Resolved again here: an explicit root can be deactivated or deleted while the scan waits in the queue.
            var roots = scan.RootId.HasValue
                ? new[] { await GetActiveRootAsync(scan.RootId.Value, cancellationToken) }
                : (await _imageRootRepository.GetAllAsync(cancellationToken)).Where(r => r.IsActive).ToArray();

            var settings = await _appSettingsRepository.GetAsync(cancellationToken);
            var excludeRules = new ScanExcludeRules(settings);
            var unavailableRoots = new List<string>();

            foreach (var root in roots)
            {
                // A missing or empty mount folder is a mount problem, not a deletion: change nothing under it.
                if (!IsRootAvailable(root.MountPath))
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
```

- [ ] **Step 5: Worker**

Create `src/PictureManager.Worker/Scanning/ChannelScanQueue.cs`:

```csharp
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using PictureManager.Application.Scanning;

namespace PictureManager.Worker.Scanning;

public sealed class ChannelScanQueue : IScanQueue
{
    private readonly Channel<QueuedScan> _channel = Channel.CreateUnbounded<QueuedScan>();

    public void Enqueue(QueuedScan scan) => _channel.Writer.TryWrite(scan);

    public async IAsyncEnumerable<QueuedScan> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var scan in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return scan;
        }
    }
}
```

Create `src/PictureManager.Worker/Scanning/ScanBackgroundService.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PictureManager.Application.Scanning;

namespace PictureManager.Worker.Scanning;

/// <summary>Runs queued scans one at a time, off the HTTP request that started them.</summary>
public sealed class ScanBackgroundService : BackgroundService
{
    private readonly IScanQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScanBackgroundService> _logger;

    public ScanBackgroundService(IScanQueue queue, IServiceScopeFactory scopeFactory, ILogger<ScanBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var scan in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
                await scanService.RunScanAsync(scan, stoppingToken);
            }
            catch (ScanRootsUnavailableException ex)
            {
                // Expected when a share isn't mounted; the job already carries the message for the UI.
                _logger.LogWarning("Scan {ScanJobId} failed: {Message}", scan.ScanJobId, ex.Message);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down: RunScanAsync already recorded the job as Cancelled.
                break;
            }
            catch (Exception ex)
            {
                // RunScanAsync already recorded the failure on the job. The loop must survive: a faulted
                // ExecuteAsync would stop every later scan for the rest of the process lifetime.
                _logger.LogError(ex, "Scan {ScanJobId} failed", scan.ScanJobId);
            }
        }
    }
}
```

In `WorkerServiceCollectionExtensions.AddWorker`, add after the enrichment registrations:

```csharp
        services.AddSingleton<IScanQueue, ChannelScanQueue>();
        services.AddHostedService<ScanBackgroundService>();
```

- [ ] **Step 6: API and startup recovery**

In `ScanEndpoints.cs`:
- in `StartScanAsync`, change `scanService.StartScanAsync(request.RootId, request.IsRecursive, cancellationToken)` to `scanService.QueueScanAsync(request.RootId, request.IsRecursive, cancellationToken)`;
- in `StreamScanEventsAsync`, change the payload construction to
  `new ScanProgress(scanJob.Id, scanJob.Status.ToString(), scanJob.FoldersScanned, scanJob.FilesFound, scanJob.FilesEnriched, scanJob.ErrorMessage)`;
- change the record to
  `public sealed record ScanProgress(int Id, string Status, int FoldersScanned, int FilesFound, int FilesEnriched, string? ErrorMessage);`

In `Program.cs`:
- add `using PictureManager.Application.Scanning;`;
- extend the startup scope block so it reads:

```csharp
    using (var scope = app.Services.CreateScope())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<IImageRootSeeder>();
        await seeder.SeedAsync();

        // Before the server accepts requests, so it can only ever fail jobs a previous process left behind.
        var interrupted = await scope.ServiceProvider.GetRequiredService<IScanService>().FailInterruptedJobsAsync();
        if (interrupted > 0)
            Log.Warning("Marked {Count} scan job(s) interrupted by a restart as failed", interrupted);
    }
```

- [ ] **Step 7: Run the tests, then the full suite**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~Scanning"`
Expected: PASS, including the 4 new `ScanServiceQueueTests`.

Run: `dotnet test tests/PictureManager.Worker.Tests`
Expected: PASS, including the 2 new tests.

Run: `dotnet test`
Expected: 0 failed. This includes the smoke tests, which start the real host and so exercise the Program.cs wiring.

- [ ] **Step 8: Commit**

```bash
git add src/PictureManager.Application/Scanning src/PictureManager.Application/Repositories/IScanJobRepository.cs src/PictureManager.Infrastructure/Persistence/Repositories/ScanJobRepository.cs src/PictureManager.Worker src/PictureManager.Api/Endpoints/ScanEndpoints.cs src/PictureManager.Api/Program.cs tests/PictureManager.Application.Tests/Scanning tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ScanJobRepositoryTests.cs tests/PictureManager.Worker.Tests/Scanning/ScanBackgroundServiceTests.cs tests/PictureManager.Api.Tests/Endpoints/ScanEndpointsTests.cs
git commit -m "feat: run scans in the background, report errors on the event stream, recover interrupted jobs"
```

---

### Task 4: Live verification against the dev root (controller-performed)

This needs a running app and real folder renames. The controller runs it and records every command and its result in the task report.

- [ ] **Step 1: Prepare**

1. Run `dotnet ef database update --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api`. Expected: up to date.
2. Confirm `dev-data/images/Holidays/Madeira/IMG_0001..3.jpg` and `dev-data/images/Holidays/IMG_0001 copy.jpg` exist; they were left there by phase 5.
3. From `src/PictureManager.Api`, start the app in the background: `ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5199`.
4. Run a full scan and wait for `Completed` on `/api/scans/{id}/events`.
5. Note these ids: the dev root (`/api/roots`), `Holidays` and `Madeira` (`/api/folders/.../children`), and `IMG_0003` in Madeira.

- [ ] **Step 2: Checks**

| # | Action | Expected |
|---|---|---|
| 1 | `time curl -X POST /api/scans {"rootId":<dev>,"isRecursive":true}` | returns `{scanJobId}` at once; the event stream then ends `Completed` with `"ErrorMessage":null` |
| 2 | `PUT /api/images/{IMG_0003}/favorite`; rename `Holidays/Madeira` to `Holidays/Madeira2` on disk; scan | `GET /api/folders/{Holidays}/children` lists `Madeira` with `isMissing: true, imageCount: 3`, and `Madeira2` with `isMissing: false, imageCount: 3` |
| 3 | `GET /api/images?fileName=IMG_0002` | exactly one item (in Madeira2) |
| 4 | `GET /api/duplicates` | one group of 2 (`IMG_0001 copy` + Madeira2's `IMG_0001`), not 3 |
| 5 | `GET /api/images?favoritesOnly=true` | `[]` (the favorite lives on the old, missing folder's row) |
| 6 | rename `Madeira2` back to `Madeira`; scan | `Madeira` has `isMissing: false`; `Madeira2` has `isMissing: true`; `favoritesOnly=true` returns `IMG_0003` with its original id |
| 7 | `DELETE /api/folders/{Madeira2}` (after the scan is `Completed`) | `204`; it's gone from the children list and appears in `GET /api/folders/removed` |
| 8 | rename `dev-data/images` to `dev-data/images-off`; scan | POST returns `{scanJobId}`; events end `Failed` with `ErrorMessage` `Root 'dev' is unavailable: its folder is missing or empty. Check that the share is mounted.`; `GET /api/folders/{Holidays}/children` is unchanged (nothing marked). Rename it back afterwards. |
| 9 | in psql, set the latest `ScanJobs` row's `Status` to the integer value of `ScanJobStatus.Enumerating` (see `src/PictureManager.Model/ScanJobStatus.cs`); restart the app | the log contains "Marked 1 scan job(s) interrupted by a restart as failed"; that job is `Failed` with `Interrupted by an application restart.`; a new `POST /api/scans` returns 200, not 409 |
| 10 | `DELETE /api/images/{IMG_0003}/favorite` | `204`; `favoritesOnly=true` → `[]` (dev state restored: nothing favorited) |

- [ ] **Step 3: Restore and record**

Stop the app. Make sure `dev-data/images` has its original name and layout (`Holidays/Madeira/…`). Record every result in the task report.
