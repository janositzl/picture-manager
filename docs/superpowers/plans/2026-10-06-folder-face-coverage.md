# Folder Face-Coverage Indicator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show in the folder tree which folders (and subtrees) have been face-processed by the current model, so the user can run face recognition folder by folder without losing track.

**Architecture:** No new stored state. A new read, `GET /api/face-recognitions/coverage`, counts each visible folder's own images (total / done / permanently failed) with exactly the image set and "done" rule of `FaceRepository.GetCandidateImageIdsAsync`. It then rolls the counts up the folder tree in memory (pure function in Application) and returns one row per folder whose subtree has photos. The frontend fetches it once into the TanStack cache under the `['folders', …]` prefix, so the existing invalidations (job completed, exclusion, removal) refresh it for free. It also polls every 10 s while a face job runs. `FolderTreeNode` replaces the green "Scanned" check with a face-status badge and colours the folder icon sky-blue when the subtree is fully processed.

**Tech Stack:** .NET 10 / EF Core 10 / PostgreSQL, xUnit + FluentAssertions + NSubstitute; React 19 + TypeScript + MUI + TanStack Query, Vitest + Testing Library + MSW.

**Spec:** The design agreed in conversation on 2026-10-06 (no separate spec file). It is summarised here:
- Status is derived from `FaceProcessingState`. Nothing new is written during detection.
- **Excluded folders are out of face scope everywhere.** The face job skips photos in an excluded folder or anywhere beneath one (they may still be indexed from before the exclusion), and the coverage doesn't count them. One shared query helper defines the scope for both.
- Per folder, over its whole subtree: `total` = visible Indexed images outside excluded subtrees, `done` = Completed for the current model with matching `ContentHash`, `failed` = PermanentlyFailed (same match). `total − done − failed` = what a face job on that folder would process.
- One indicator per row (the green "Scanned" check is removed):

| State | Folder icon | Badge |
|---|---|---|
| Not scanned (`isScanned == false`, not face-complete) | `text.disabled` colour, tooltip "Not scanned yet" | none |
| No photos in subtree / no coverage row | normal | none |
| Not started (`done + failed == 0`) | normal, tooltip "Faces: not processed (N photos)" | none |
| Partial | normal | small determinate ring |
| Complete | sky-blue `FACE_DONE` | none |
| Complete, with permanent failures | sky-blue `FACE_DONE` | small amber dot |
| Excluded row | existing `BlockIcon` | none |

- Model change ⇒ everything reverts to "not started" (falls out of keying by model).
- Face models unavailable ⇒ endpoint returns `[]` ⇒ no face badges. The tree still works.

## Global Constraints

- Backend: file-scoped namespaces, existing repository/service/endpoint split. No new EF migration (no schema change).
- The coverage image set and the job's candidate set come from one private helper, `FaceRepository.FaceScopeImages()`: `Images.WhereVisible().Where(i => i.IndexState == IndexState.Indexed)`, minus images in an excluded folder or beneath one (same root, `RelativePath` equal or under the excluded folder's path). State matched by `FaceModelId == current` and `ImageFingerprint == i.ContentHash`.
- Endpoint lives on the **admin** surface next to `/face-recognitions/failures`: `GET /api/face-recognitions/coverage`, camelCase JSON `[{ folderId, total, done, failed }]`.
- Frontend query key: `['folders', 'face-coverage']`.
- Colour constant `FACE_DONE = '#2e94dc'` in `web/src/design/accent.ts` (≥3:1 against white for a non-text icon; distinct from the indigo `ACCENT`).
- Don't let build/test output dump more than 20 lines into responses.

## Review Focus

1. **Content changed after processing** (Completed state with an old fingerprint): must count as *not done*, so the folder drops back to partial. Pinned in Task 1's repository test (`changed` image).
2. **Retryable `Failed` state**: must count as *pending*, not failed, since a job would retry it. Pinned in Task 1 (`failed` image).
3. **Missing folders / missing images / non-Indexed images**: contribute nothing. A folder whose images are all missing gets no row, so it shows no badge. Pinned in Task 1.
4. **Deep trees and sibling-prefix names** ("Trips" vs "Trips2"): the roll-up goes by `ParentId`, not path prefix. Pinned in Task 1's roll-up tests (a three-level chain and siblings).
5. **Coverage request fails or models are unavailable**: the tree must render exactly as before minus badges, with no error alert. Pinned in Task 3 (`500` handler test) and Task 2 (unavailable model test).
6. **Photos indexed before their folder was excluded** (including in a non-excluded child of an excluded folder): the face job must not process them, and they must not count. A job started on the parent, the root, or "everything" skips them. Pinned in Task 1 (candidate and counts tests).

Not in scope (possible follow-ups): a "Not fully processed" tree filter, and a scan-in-progress spinner per folder (`ScanStatus` is not exposed on `FolderNode`; `JobStatusBanner` already shows running jobs).

---

### Task 1: Shared face scope (skips excluded subtrees), per-folder counts and roll-up

**Files:**
- Create: `src/PictureManager.Application/Faces/FaceCoverage.cs`
- Modify: `src/PictureManager.Application/Repositories/IFaceRepository.cs` (doc of `GetCandidateImageIdsAsync` lines 27-31; add method after it, line 32)
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/FaceRepository.cs` (line 52 of `GetCandidateImageIdsAsync` uses the new helper; add the helper and the counts method after it, line 78)
- Create: `tests/PictureManager.Application.Tests/Faces/FaceCoverageRollupTests.cs`
- Modify: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FaceRepositoryTests.cs` (new test after `GetCandidateImageIdsAsync_AppliesIndexStateVisibilityScopeAndState`)

**Interfaces:**
- Produces:
  - `public sealed record FolderFaceCounts(int FolderId, int? ParentId, int Total, int Done, int Failed);`
  - `public sealed record FolderFaceCoverage(int FolderId, int Total, int Done, int Failed);`
  - `public static class FaceCoverageRollup { public static IReadOnlyList<FolderFaceCoverage> Roll(IReadOnlyList<FolderFaceCounts> folders); }`
  - `IFaceRepository.GetFolderFaceCountsAsync(int faceModelId, CancellationToken cancellationToken = default) : Task<IReadOnlyList<FolderFaceCounts>>` returns one row per visible folder (own images only, zeros allowed).

- [ ] **Step 1: Write the failing roll-up tests**

`tests/PictureManager.Application.Tests/Faces/FaceCoverageRollupTests.cs`:

```csharp
using FluentAssertions;
using PictureManager.Application.Faces;
using Xunit;

namespace PictureManager.Application.Tests.Faces;

public class FaceCoverageRollupTests
{
    [Fact]
    public void Roll_SumsEachFolderIntoEveryAncestor()
    {
        // top(1) -> trips(2) -> madeira(3); top -> trips2(4)
        var result = FaceCoverageRollup.Roll(new[]
        {
            new FolderFaceCounts(1, null, Total: 1, Done: 1, Failed: 0),
            new FolderFaceCounts(2, 1, Total: 4, Done: 2, Failed: 1),
            new FolderFaceCounts(3, 2, Total: 3, Done: 3, Failed: 0),
            new FolderFaceCounts(4, 1, Total: 2, Done: 0, Failed: 0),
        });

        result.Should().BeEquivalentTo(new[]
        {
            new FolderFaceCoverage(1, Total: 10, Done: 6, Failed: 1),
            new FolderFaceCoverage(2, Total: 7, Done: 5, Failed: 1),
            new FolderFaceCoverage(3, Total: 3, Done: 3, Failed: 0),
            new FolderFaceCoverage(4, Total: 2, Done: 0, Failed: 0),
        });
    }

    [Fact]
    public void Roll_LeavesOutFoldersWithNoPhotosInTheirSubtree_ButKeepsEmptyParentsOfPhotos()
    {
        var result = FaceCoverageRollup.Roll(new[]
        {
            new FolderFaceCounts(1, null, 0, 0, 0),
            new FolderFaceCounts(2, 1, 0, 0, 0),   // empty parent of a folder with photos: kept
            new FolderFaceCounts(3, 2, 5, 5, 0),
            new FolderFaceCounts(4, 1, 0, 0, 0),   // empty leaf: left out
        });

        result.Select(r => r.FolderId).Should().Equal(1, 2, 3);
        result.Single(r => r.FolderId == 1).Should().Be(new FolderFaceCoverage(1, 5, 5, 0));
    }

    [Fact]
    public void Roll_IgnoresAParentThatIsNotInTheList()
    {
        // A folder whose parent isn't visible (not returned) still reports its own counts.
        var result = FaceCoverageRollup.Roll(new[] { new FolderFaceCounts(7, 99, 2, 1, 0) });

        result.Should().Equal(new FolderFaceCoverage(7, 2, 1, 0));
    }

    [Fact]
    public void Roll_Empty_ReturnsEmpty() =>
        FaceCoverageRollup.Roll(Array.Empty<FolderFaceCounts>()).Should().BeEmpty();
}
```

(If the test project lacks implicit usings for `System`/`System.Linq`, add `using System;` and `using System.Linq;`. Match the other files in `tests/PictureManager.Application.Tests/Faces/`.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/PictureManager.Application.Tests --filter FaceCoverageRollupTests`
Expected: build FAIL, `FaceCoverageRollup` / `FolderFaceCounts` not found.

- [ ] **Step 3: Implement the records and roll-up**

`src/PictureManager.Application/Faces/FaceCoverage.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace PictureManager.Application.Faces;

/// <summary>One visible folder's own images (not its subfolders'), as the current face model sees them.</summary>
public sealed record FolderFaceCounts(int FolderId, int? ParentId, int Total, int Done, int Failed);

/// <summary>
/// Face coverage of a folder and everything beneath it. Total = visible Indexed images, Done = Completed for the
/// current model and content, Failed = PermanentlyFailed. Total - Done - Failed is what a face job on the folder
/// would process.
/// </summary>
public sealed record FolderFaceCoverage(int FolderId, int Total, int Done, int Failed);

public static class FaceCoverageRollup
{
    /// <summary>
    /// Adds each folder's own counts to every ancestor (by ParentId). Folders with no photos anywhere beneath
    /// them are left out. Ordered by FolderId.
    /// </summary>
    public static IReadOnlyList<FolderFaceCoverage> Roll(IReadOnlyList<FolderFaceCounts> folders)
    {
        var parents = folders.ToDictionary(f => f.FolderId, f => f.ParentId);
        var totals = folders.ToDictionary(f => f.FolderId, f => (f.Total, f.Done, f.Failed));

        foreach (var folder in folders.Where(f => f.Total > 0))
        {
            // The visited set only guards against a corrupt ParentId cycle; a real tree never revisits.
            var visited = new HashSet<int> { folder.FolderId };
            var parentId = folder.ParentId;
            while (parentId is int id && parents.TryGetValue(id, out var next) && visited.Add(id))
            {
                var t = totals[id];
                totals[id] = (t.Total + folder.Total, t.Done + folder.Done, t.Failed + folder.Failed);
                parentId = next;
            }
        }

        return totals
            .Where(p => p.Value.Total > 0)
            .OrderBy(p => p.Key)
            .Select(p => new FolderFaceCoverage(p.Key, p.Value.Total, p.Value.Done, p.Value.Failed))
            .ToList();
    }
}
```

- [ ] **Step 4: Run roll-up tests to verify they pass**

Run: `dotnet test tests/PictureManager.Application.Tests --filter FaceCoverageRollupTests`
Expected: 4 passed.

- [ ] **Step 5: Write the failing repository tests**

Add to `FaceRepositoryTests.cs`, right after `GetCandidateImageIdsAsync_AppliesIndexStateVisibilityScopeAndState`. First, the job must skip excluded subtrees. The existing test above has no excluded folders and must keep passing unchanged:

```csharp
[Fact]
public async Task GetCandidateImageIdsAsync_SkipsImagesInOrBeneathAnExcludedFolder()
{
    await using var db = await PostgresTestDatabase.CreateAsync();
    var top = await FaceTestData.SeedRootAsync(db.Context);
    var trips = await FaceTestData.AddFolderAsync(db.Context, top, "Trips");
    var privateFolder = await FaceTestData.AddFolderAsync(db.Context, trips, "Private");
    var nested = await FaceTestData.AddFolderAsync(db.Context, privateFolder, "Nested");      // not flagged itself
    var privateBis = await FaceTestData.AddFolderAsync(db.Context, trips, "Private2");        // prefix trap: stays in scope
    var model = await FaceTestData.AddModelAsync(db.Context);

    var inTrips = await FaceTestData.AddImageAsync(db.Context, trips, "a");
    await FaceTestData.AddImageAsync(db.Context, privateFolder, "b");     // indexed before the exclusion
    await FaceTestData.AddImageAsync(db.Context, nested, "c");
    var inPrivateBis = await FaceTestData.AddImageAsync(db.Context, privateBis, "d");
    privateFolder.IsExcluded = true;
    await db.Context.SaveChangesAsync();
    var repository = new FaceRepository(db.CreateContext());

    var expected = new[] { inTrips.Id, inPrivateBis.Id };
    (await repository.GetCandidateImageIdsAsync(model, trips.Id, isRecursive: true)).Should().BeEquivalentTo(expected);
    (await repository.GetCandidateImageIdsAsync(model, top.Id, isRecursive: true)).Should().BeEquivalentTo(expected);
    (await repository.GetCandidateImageIdsAsync(model, null, isRecursive: true)).Should().BeEquivalentTo(expected);
    (await repository.GetCandidateImageIdsAsync(model, privateFolder.Id, isRecursive: true)).Should().BeEmpty();
    (await repository.GetCandidateImageIdsAsync(model, nested.Id, isRecursive: false)).Should().BeEmpty();
}
```

Then the counts. This test reuses the candidate test's seed shape, so the two rules can't drift apart unnoticed:

```csharp
[Fact]
public async Task GetFolderFaceCountsAsync_CountsOwnImages_WithTheCandidateRules()
{
    await using var db = await PostgresTestDatabase.CreateAsync();
    var top = await FaceTestData.SeedRootAsync(db.Context);
    var trips = await FaceTestData.AddFolderAsync(db.Context, top, "Trips");
    var madeira = await FaceTestData.AddFolderAsync(db.Context, trips, "Madeira");
    var gone = await FaceTestData.AddFolderAsync(db.Context, top, "Gone");
    var model = await FaceTestData.AddModelAsync(db.Context);
    var otherModel = await FaceTestData.AddModelAsync(db.Context, "model-2");

    await FaceTestData.AddImageAsync(db.Context, trips, "new");                                  // pending
    await FaceTestData.AddImageAsync(db.Context, trips, "notIndexed", IndexState.Pending);       // not counted
    var missing = await FaceTestData.AddImageAsync(db.Context, trips, "missing");                // not counted
    missing.MissingSinceUtc = Now;
    var done = await FaceTestData.AddImageAsync(db.Context, trips, "done", hash: "h1");          // done
    var changed = await FaceTestData.AddImageAsync(db.Context, trips, "changed", hash: "new");   // pending (old fingerprint)
    var retry = await FaceTestData.AddImageAsync(db.Context, trips, "retry", hash: "h2");        // pending (retryable)
    var givenUp = await FaceTestData.AddImageAsync(db.Context, trips, "gaveup", hash: "h3");     // failed
    var otherDone = await FaceTestData.AddImageAsync(db.Context, madeira, "other", hash: "h4");  // pending (other model)
    var madeiraDone = await FaceTestData.AddImageAsync(db.Context, madeira, "m", hash: "h5");    // done
    await FaceTestData.AddImageAsync(db.Context, gone, "g");                                     // folder missing: not counted
    gone.MissingSinceUtc = Now;
    var excluded = await FaceTestData.AddFolderAsync(db.Context, top, "Private");
    var underExcluded = await FaceTestData.AddFolderAsync(db.Context, excluded, "Nested");
    await FaceTestData.AddImageAsync(db.Context, excluded, "x");                                 // excluded: not counted
    await FaceTestData.AddImageAsync(db.Context, underExcluded, "y");                            // beneath excluded: not counted
    excluded.IsExcluded = true;
    db.Context.FaceProcessingStates.AddRange(
        new FaceProcessingState { ImageId = done.Id, FaceModelId = model, ImageFingerprint = "h1", Status = FaceProcessingStatus.Completed, ProcessedUtc = Now },
        new FaceProcessingState { ImageId = changed.Id, FaceModelId = model, ImageFingerprint = "old", Status = FaceProcessingStatus.Completed, ProcessedUtc = Now },
        new FaceProcessingState { ImageId = retry.Id, FaceModelId = model, ImageFingerprint = "h2", Status = FaceProcessingStatus.Failed, Attempts = 1, ProcessedUtc = Now },
        new FaceProcessingState { ImageId = givenUp.Id, FaceModelId = model, ImageFingerprint = "h3", Status = FaceProcessingStatus.PermanentlyFailed, Attempts = 3, ProcessedUtc = Now },
        new FaceProcessingState { ImageId = otherDone.Id, FaceModelId = otherModel, ImageFingerprint = "h4", Status = FaceProcessingStatus.Completed, ProcessedUtc = Now },
        new FaceProcessingState { ImageId = madeiraDone.Id, FaceModelId = model, ImageFingerprint = "h5", Status = FaceProcessingStatus.Completed, ProcessedUtc = Now });
    await db.Context.SaveChangesAsync();
    var repository = new FaceRepository(db.CreateContext());

    var counts = await repository.GetFolderFaceCountsAsync(model);

    counts.Should().BeEquivalentTo(new[]
    {
        new FolderFaceCounts(top.Id, null, 0, 0, 0),
        new FolderFaceCounts(trips.Id, top.Id, Total: 5, Done: 1, Failed: 1),
        new FolderFaceCounts(madeira.Id, trips.Id, Total: 2, Done: 1, Failed: 0),
        new FolderFaceCounts(gone.Id, top.Id, 0, 0, 0),
        new FolderFaceCounts(excluded.Id, top.Id, 0, 0, 0),
        new FolderFaceCounts(underExcluded.Id, excluded.Id, 0, 0, 0),
    });

    // Invariant with the job: what's left in a folder's own images is exactly its non-recursive candidates.
    var tripsCandidates = await repository.GetCandidateImageIdsAsync(model, trips.Id, isRecursive: false);
    tripsCandidates.Should().HaveCount(5 - 1 - 1);
}
```

Add `using PictureManager.Application.Faces;` if it's not already there (it is: line 6).

- [ ] **Step 6: Run to verify it fails**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "GetFolderFaceCountsAsync|GetCandidateImageIdsAsync"`
Expected: build FAIL, `GetFolderFaceCountsAsync` not defined. (Once it compiles, the excluded-candidate test fails until Step 8.)

- [ ] **Step 7: Update the interface**

In `IFaceRepository.cs`, replace the summary of `GetCandidateImageIdsAsync` (lines 27-31) with:

```csharp
    /// <summary>
    /// Ids (ascending) of visible Indexed images in scope that still need analysis by this model: no state, a
    /// state for another model or an older ContentHash, or a retryable Failed state. Images in an excluded folder
    /// or beneath one are never in scope (they may still be indexed from before the exclusion). folderId null =
    /// everything; an unknown folderId = nothing.
    /// </summary>
```

Then add after `GetCandidateImageIdsAsync` (line 32):

```csharp
    /// <summary>
    /// One row per visible folder with its own (not its subfolders') visible Indexed images: Total, Done =
    /// Completed by this model for the current ContentHash, Failed = PermanentlyFailed likewise. Same image set
    /// and rule as GetCandidateImageIdsAsync, so Total - Done - Failed equals the folder's non-recursive candidates.
    /// </summary>
    Task<IReadOnlyList<FolderFaceCounts>> GetFolderFaceCountsAsync(int faceModelId, CancellationToken cancellationToken = default);
```

- [ ] **Step 8: Implement it in `FaceRepository`**

In `GetCandidateImageIdsAsync`, replace line 52

```csharp
        var query = _dbContext.Images.AsNoTracking().WhereVisible().Where(i => i.IndexState == IndexState.Indexed);
```

with

```csharp
        var query = FaceScopeImages();
```

After `GetCandidateImageIdsAsync` (line 78), add the helper and the counts method:

```csharp
    /// <summary>
    /// The images face recognition works on: visible, Indexed, and not in or beneath an excluded folder. Shared by
    /// the job's candidates and the folder coverage, so the two always agree.
    /// </summary>
    private IQueryable<Image> FaceScopeImages() =>
        _dbContext.Images.AsNoTracking().WhereVisible()
            .Where(i => i.IndexState == IndexState.Indexed)
            .Where(i => !_dbContext.Folders.Any(x => x.IsExcluded && x.RootId == i.Folder!.RootId
                && (x.Id == i.FolderId || x.RelativePath == "" || i.Folder.RelativePath.StartsWith(x.RelativePath + "/"))));

    public async Task<IReadOnlyList<FolderFaceCounts>> GetFolderFaceCountsAsync(int faceModelId, CancellationToken cancellationToken = default)
    {
        var folders = await _dbContext.Folders.AsNoTracking().WhereVisible()
            .Select(f => new { f.Id, f.ParentId })
            .ToListAsync(cancellationToken);

        // The image set and "done" rule of GetCandidateImageIdsAsync; the state is one row per image (keyed by ImageId).
        var counts = await (
                from i in FaceScopeImages()
                join s in _dbContext.FaceProcessingStates.Where(s => s.FaceModelId == faceModelId) on i.Id equals s.ImageId into states
                from s in states.DefaultIfEmpty()
                select new
                {
                    i.FolderId,
                    Done = s != null && s.ImageFingerprint == i.ContentHash && s.Status == FaceProcessingStatus.Completed,
                    Failed = s != null && s.ImageFingerprint == i.ContentHash && s.Status == FaceProcessingStatus.PermanentlyFailed
                })
            .GroupBy(r => r.FolderId)
            .Select(g => new { FolderId = g.Key, Total = g.Count(), Done = g.Count(r => r.Done), Failed = g.Count(r => r.Failed) })
            .ToDictionaryAsync(c => c.FolderId, cancellationToken);

        return folders
            .Select(f => counts.TryGetValue(f.Id, out var c)
                ? new FolderFaceCounts(f.Id, f.ParentId, c.Total, c.Done, c.Failed)
                : new FolderFaceCounts(f.Id, f.ParentId, 0, 0, 0))
            .ToList();
    }
```

If EF can't translate `g.Count(r => r.Done)`, replace both conditional counts with `g.Sum(r => r.Done ? 1 : 0)` / `g.Sum(r => r.Failed ? 1 : 0)`. Don't switch to raw SQL: the visibility rule must stay in `WhereVisible`.

- [ ] **Step 9: Run the repository tests**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "GetFolderFaceCountsAsync|GetCandidateImageIdsAsync"`
Expected: 3 passed (the existing candidate test unchanged, the excluded-candidate test, the counts test).

Then run the face-related suites, so nothing that relied on excluded images being processed breaks: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~Face"` and `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~Face"`. Expected: all pass.

- [ ] **Step 10: Commit**

```bash
git add src/PictureManager.Application/Faces/FaceCoverage.cs src/PictureManager.Application/Repositories/IFaceRepository.cs src/PictureManager.Infrastructure/Persistence/Repositories/FaceRepository.cs tests/PictureManager.Application.Tests/Faces/FaceCoverageRollupTests.cs tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FaceRepositoryTests.cs
git commit -m "feat(faces): skip excluded subtrees; per-folder face coverage counts and roll-up"
```

---

### Task 2: Service method and `GET /api/face-recognitions/coverage`

**Files:**
- Modify: `src/PictureManager.Application/Faces/IFaceRecognitionService.cs`
- Modify: `src/PictureManager.Application/Faces/FaceRecognitionService.cs:133-147` (`GetPermanentFailuresAsync`, plus a new method and a private helper)
- Modify: `src/PictureManager.Api/Endpoints/FaceRecognitionEndpoints.cs`
- Test: `tests/PictureManager.Application.Tests/Faces/FaceRecognitionServiceTests.cs`
- Test: `tests/PictureManager.Api.Tests/Endpoints/FaceRecognitionEndpointsTests.cs`

**Interfaces:**
- Consumes: `IFaceRepository.GetFolderFaceCountsAsync`, `FaceCoverageRollup.Roll`, `FolderFaceCoverage` (Task 1).
- Produces: `IFaceRecognitionService.GetFolderCoverageAsync(CancellationToken cancellationToken = default) : Task<IReadOnlyList<FolderFaceCoverage>>`; `FaceRecognitionEndpoints.GetCoverageAsync(IFaceRecognitionService, CancellationToken) : Task<IResult>` returning `Ok<IReadOnlyList<FolderFaceCoverage>>`; route `GET /api/face-recognitions/coverage`.

- [ ] **Step 1: Write the failing service tests**

Add to `FaceRecognitionServiceTests.cs` (the fixture already stubs `_analyzer.Model` → `Descriptor` and `GetOrCreateModelIdAsync` → `3`):

```csharp
[Fact]
public async Task GetFolderCoverageAsync_RollsUpTheCurrentModelsCounts()
{
    _faces.GetFolderFaceCountsAsync(3, Arg.Any<CancellationToken>()).Returns(new[]
    {
        new FolderFaceCounts(1, null, 1, 1, 0),
        new FolderFaceCounts(2, 1, 4, 2, 1),
    });

    var coverage = await Create().GetFolderCoverageAsync();

    coverage.Should().Equal(new FolderFaceCoverage(1, 5, 3, 1), new FolderFaceCoverage(2, 4, 2, 1));
}

[Fact]
public async Task GetFolderCoverageAsync_ModelsUnavailable_ReturnsEmpty()
{
    _analyzer.Model.Throws(new FaceModelUnavailableException("missing"));

    var coverage = await Create().GetFolderCoverageAsync();

    coverage.Should().BeEmpty();
    await _faces.DidNotReceive().GetFolderFaceCountsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
}
```

Check `FaceModelUnavailableException`'s constructor with `tokensave_search FaceModelUnavailableException` and adjust the argument. If an existing test in this file already throws it, copy that construction.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/PictureManager.Application.Tests --filter GetFolderCoverageAsync`
Expected: build FAIL, `GetFolderCoverageAsync` not defined.

- [ ] **Step 3: Implement**

In `IFaceRecognitionService.cs`, after `GetPermanentFailuresAsync`:

```csharp
    /// <summary>Face coverage of every folder subtree that has photos, for the current model. Empty when the models are unavailable.</summary>
    Task<IReadOnlyList<FolderFaceCoverage>> GetFolderCoverageAsync(CancellationToken cancellationToken = default);
```

In `FaceRecognitionService.cs`, replace `GetPermanentFailuresAsync` (lines 133-147) with this. The model lookup that both reads need moves into one helper:

```csharp
    public async Task<IReadOnlyList<FaceFailure>> GetPermanentFailuresAsync(CancellationToken cancellationToken = default) =>
        await TryGetCurrentModelIdAsync(cancellationToken) is int faceModelId
            ? await _faceRepository.GetPermanentFailuresAsync(faceModelId, cancellationToken)
            : Array.Empty<FaceFailure>();

    public async Task<IReadOnlyList<FolderFaceCoverage>> GetFolderCoverageAsync(CancellationToken cancellationToken = default) =>
        await TryGetCurrentModelIdAsync(cancellationToken) is int faceModelId
            ? FaceCoverageRollup.Roll(await _faceRepository.GetFolderFaceCountsAsync(faceModelId, cancellationToken))
            : Array.Empty<FolderFaceCoverage>();

    /// <summary>The current model's FaceModel id, or null when the models are unavailable.</summary>
    private async Task<int?> TryGetCurrentModelIdAsync(CancellationToken cancellationToken)
    {
        FaceModelDescriptor model;
        try
        {
            model = _analyzer.Model;
        }
        catch (FaceModelUnavailableException)
        {
            return null;
        }

        return await _faceRepository.GetOrCreateModelIdAsync(model, _clock.UtcNow, cancellationToken);
    }
```

- [ ] **Step 4: Run service tests (new and existing)**

Run: `dotnet test tests/PictureManager.Application.Tests --filter FaceRecognitionServiceTests`
Expected: all pass, including the existing permanent-failures tests.

- [ ] **Step 5: Write the failing endpoint test**

Add to `FaceRecognitionEndpointsTests.cs` (add `using System.Collections.Generic;`):

```csharp
[Fact]
public async Task GetCoverageAsync_ReturnsTheServicesCoverage()
{
    var service = Substitute.For<IFaceRecognitionService>();
    IReadOnlyList<FolderFaceCoverage> coverage = new[] { new FolderFaceCoverage(2, 4, 2, 1) };
    service.GetFolderCoverageAsync(Arg.Any<CancellationToken>()).Returns(coverage);

    var result = await FaceRecognitionEndpoints.GetCoverageAsync(service, CancellationToken.None);

    result.Should().BeOfType<Ok<IReadOnlyList<FolderFaceCoverage>>>().Which.Value.Should().Equal(coverage);
}
```

- [ ] **Step 6: Run to verify it fails**

Run: `dotnet test tests/PictureManager.Api.Tests --filter GetCoverageAsync`
Expected: build FAIL, `GetCoverageAsync` not defined.

- [ ] **Step 7: Implement the endpoint**

In `FaceRecognitionEndpoints.cs`, register it in `MapFaceRecognitionEndpoints` after the failures route:

```csharp
        admin.MapGet("/face-recognitions/coverage", GetCoverageAsync);
```

and add next to `GetFailuresAsync`:

```csharp
    public static async Task<IResult> GetCoverageAsync(IFaceRecognitionService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetFolderCoverageAsync(cancellationToken));
```

- [ ] **Step 8: Run the API tests**

Run: `dotnet test tests/PictureManager.Api.Tests --filter FaceRecognitionEndpointsTests`
Expected: all pass. If `ApiSmokeTests` enumerates admin routes against a list, run `dotnet test tests/PictureManager.Api.Tests` and add the new route there if it fails.

- [ ] **Step 9: Commit**

```bash
git add src/PictureManager.Application/Faces/IFaceRecognitionService.cs src/PictureManager.Application/Faces/FaceRecognitionService.cs src/PictureManager.Api/Endpoints/FaceRecognitionEndpoints.cs tests/PictureManager.Application.Tests/Faces/FaceRecognitionServiceTests.cs tests/PictureManager.Api.Tests/Endpoints/FaceRecognitionEndpointsTests.cs
git commit -m "feat(faces): GET /api/face-recognitions/coverage"
```

---

### Task 3: Frontend: coverage query, state helper and tree indicator

**Files:**
- Modify: `web/src/api/types.ts` (add type after `FolderNode`)
- Modify: `web/src/api/queries.ts` (query key and hook)
- Modify: `web/src/design/accent.ts` (`FACE_DONE`)
- Create: `web/src/tree/faceCoverage.ts`
- Create: `web/src/tree/faceCoverage.test.ts`
- Create: `web/src/tree/FaceCoverageBadge.tsx`
- Modify: `web/src/tree/FolderTree.tsx`
- Modify: `web/src/tree/FolderTreeNode.tsx`
- Modify: `web/src/test/fixtures.ts`, `web/src/test/handlers.ts`
- Modify: `web/src/tree/FolderTree.test.tsx`

**Interfaces:**
- Consumes: `GET /api/face-recognitions/coverage` → `[{ folderId, total, done, failed }]` (Task 2); `useFolderJobs().activeJob` (existing, `web/src/tree/FolderJobsContext.tsx`).
- Produces:
  - `type FolderFaceCoverage = { folderId: number; total: number; done: number; failed: number }`
  - `queryKeys.faceCoverage() = ['folders', 'face-coverage']`
  - `useFaceCoverage(isFaceJobRunning: boolean)` → query whose `data` is `ReadonlyMap<number, FolderFaceCoverage>`
  - `faceCoverageState(c?: FolderFaceCoverage): FaceCoverageState`, `faceCoverageLabel(c?: FolderFaceCoverage): string | null`
  - `FolderTreeNode` gains prop `faceCoverage: ReadonlyMap<number, FolderFaceCoverage>`

- [ ] **Step 1: Write the failing helper tests**

`web/src/tree/faceCoverage.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { faceCoverageLabel, faceCoverageState } from './faceCoverage'

const c = (total: number, done: number, failed = 0) => ({ folderId: 1, total, done, failed })

describe('faceCoverageState', () => {
  it.each([
    [undefined, 'none'],
    [c(0, 0), 'none'],
    [c(5, 0), 'notStarted'],
    [c(5, 2), 'partial'],
    [c(5, 0, 1), 'partial'],
    [c(5, 5), 'complete'],
    [c(5, 4, 1), 'completeWithFailures'],
  ] as const)('%o -> %s', (coverage, expected) => {
    expect(faceCoverageState(coverage)).toBe(expected)
  })
})

describe('faceCoverageLabel', () => {
  it('describes each state', () => {
    expect(faceCoverageLabel(undefined)).toBeNull()
    expect(faceCoverageLabel(c(1, 0))).toBe('Faces: not processed (1 photo)')
    expect(faceCoverageLabel(c(480, 300, 12))).toBe('Faces: 312 of 480 photos processed')
    expect(faceCoverageLabel(c(480, 480))).toBe('Faces: all 480 photos processed')
    expect(faceCoverageLabel(c(480, 477, 3))).toBe("Faces: all 480 photos processed, 3 couldn't be read")
  })
})
```

- [ ] **Step 2: Run to verify it fails**

Run (from `web/`): `npx vitest run src/tree/faceCoverage.test.ts`
Expected: FAIL, cannot resolve `./faceCoverage`.

- [ ] **Step 3: Add the type and the helper**

`web/src/api/types.ts`, after `FolderNode`:

```ts
/**
 * Face coverage of a folder and its subfolders for the current face model. Folders with no photos
 * beneath them have no entry. total - done - failed is what a face job on the folder would process.
 */
export type FolderFaceCoverage = { folderId: number; total: number; done: number; failed: number }
```

`web/src/tree/faceCoverage.ts`:

```ts
import type { FolderFaceCoverage } from '../api/types'

export type FaceCoverageState = 'none' | 'notStarted' | 'partial' | 'complete' | 'completeWithFailures'

/** Failed (given-up) photos count as processed: re-running the job won't change them. */
export function faceCoverageState(coverage: FolderFaceCoverage | undefined): FaceCoverageState {
  if (!coverage || coverage.total === 0) return 'none'
  const processed = coverage.done + coverage.failed
  if (processed === 0) return 'notStarted'
  if (processed < coverage.total) return 'partial'
  return coverage.failed > 0 ? 'completeWithFailures' : 'complete'
}

export function faceCoverageLabel(coverage: FolderFaceCoverage | undefined): string | null {
  const state = faceCoverageState(coverage)
  if (state === 'none' || !coverage) return null
  const photos = `${coverage.total} ${coverage.total === 1 ? 'photo' : 'photos'}`
  switch (state) {
    case 'notStarted':
      return `Faces: not processed (${photos})`
    case 'partial':
      return `Faces: ${coverage.done + coverage.failed} of ${photos} processed`
    case 'complete':
      return `Faces: all ${photos} processed`
    case 'completeWithFailures':
      return `Faces: all ${photos} processed, ${coverage.failed} couldn't be read`
  }
}
```

- [ ] **Step 4: Run helper tests**

Run: `npx vitest run src/tree/faceCoverage.test.ts`
Expected: PASS.

- [ ] **Step 5: Write the failing tree tests**

Fixtures. Add to `web/src/test/fixtures.ts`, after `childrenById`. The ids match the folders above: dev(1) partial, Holidays(2) partial, Madeira(3) complete with failures. Old(4) is missing, so it has no entry:

```ts
export const faceCoverage: FolderFaceCoverage[] = [
  { folderId: 1, total: 5, done: 2, failed: 1 },
  { folderId: 2, total: 5, done: 2, failed: 1 },
  { folderId: 3, total: 3, done: 2, failed: 1 },
]
```

(add `FolderFaceCoverage` to the type import at the top of the file).

Default handler. In `web/src/test/handlers.ts`, add `faceCoverage` to the fixtures import and this entry to `handlers` (anywhere; it doesn't overlap `/api/folders/*`):

```ts
  http.get('/api/face-recognitions/coverage', () => HttpResponse.json(faceCoverage)),
```

Tests. Append inside `describe('FolderTree', …)` in `web/src/tree/FolderTree.test.tsx`:

```tsx
  it('shows face progress on partly processed folders', async () => {
    renderApp('/folders/3')
    await screen.findByRole('treeitem', { name: 'Madeira' })
    expect(
      await screen.findAllByRole('img', { name: 'Faces: 3 of 5 photos processed' }),
    ).toHaveLength(2) // dev and Holidays
  })

  it('marks a fully processed folder with failures and colours its icon', async () => {
    renderApp('/folders/3')
    await screen.findByRole('treeitem', { name: 'Madeira' })
    expect(
      await screen.findByRole('img', { name: "Faces: all 3 photos processed, 1 couldn't be read" }),
    ).toBeInTheDocument()
    expect(screen.getByTestId('folder-icon-3')).toHaveAttribute('data-face-state', 'completeWithFailures')
  })

  it('no longer shows the Scanned check', async () => {
    const { user } = renderApp('/folders/3')
    await user.hover(await screen.findByTestId('folder-icon-3'))
    expect(await screen.findByRole('tooltip')).not.toHaveTextContent('Scanned')
  })

  it('dims folders that were never scanned', async () => {
    renderApp('/folders/3')
    await screen.findByRole('treeitem', { name: 'Madeira' })
    // Old (4): isScanned false, no coverage
    expect(screen.getByTestId('folder-icon-4')).toHaveAttribute('data-not-scanned', 'true')
    expect(screen.getByTestId('folder-icon-3')).not.toHaveAttribute('data-not-scanned')
  })

  it('renders the tree without face badges when coverage fails to load', async () => {
    server.use(
      http.get('/api/face-recognitions/coverage', () =>
        HttpResponse.json({ title: 'boom' }, { status: 500 }),
      ),
    )
    renderApp('/folders/3')
    expect(await screen.findByRole('treeitem', { name: 'Madeira' })).toBeInTheDocument()
    expect(screen.queryByRole('img', { name: /^Faces:/ })).not.toBeInTheDocument()
    expect(screen.queryByText("Couldn't load folders.")).not.toBeInTheDocument()
  })
```

(Holidays' badge: `done 2 + failed 1 = 3 of 5`. Dev's is the same.)

- [ ] **Step 6: Run to verify they fail**

Run: `npx vitest run src/tree/FolderTree.test.tsx`
Expected: the 5 new tests FAIL (no `img` "Faces: …", no `folder-icon-*` test ids). The existing tests still pass.

- [ ] **Step 7: Add the colour, the query and the badge**

`web/src/design/accent.ts`, after `ACCENT_TEXT`:

```ts
/** Folder icon of a subtree whose faces are all processed (sky blue, distinct from ACCENT). */
export const FACE_DONE = '#2e94dc'
```

`web/src/api/queries.ts`:
- add `FolderFaceCoverage` to the type import;
- add to `queryKeys`, after `removedFolders`:

```ts
  // Under 'folders' so every invalidation that refreshes the tree (job done, exclusion, removal) refreshes it too.
  faceCoverage: () => ['folders', 'face-coverage'] as const,
```

- add after `useFolder`:

```ts
const FACE_COVERAGE_POLL_MS = 10_000

function toCoverageMap(rows: FolderFaceCoverage[]): ReadonlyMap<number, FolderFaceCoverage> {
  return new Map(rows.map((row) => [row.folderId, row]))
}

/** Face coverage by folder id. Polls while a face job runs so the tree shows progress as it happens. */
export function useFaceCoverage(isFaceJobRunning: boolean) {
  return useQuery({
    queryKey: queryKeys.faceCoverage(),
    queryFn: ({ signal }) =>
      apiFetch<FolderFaceCoverage[]>('/api/face-recognitions/coverage', { signal }),
    select: toCoverageMap,
    refetchInterval: isFaceJobRunning ? FACE_COVERAGE_POLL_MS : false,
  })
}
```

`web/src/tree/FaceCoverageBadge.tsx`:

```tsx
import { Box, CircularProgress, Tooltip } from '@mui/material'
import type { FolderFaceCoverage } from '../api/types'
import { FACE_DONE } from '../design/accent'
import type { FaceCoverageState } from './faceCoverage'

type Props = { coverage: FolderFaceCoverage; state: FaceCoverageState; label: string }

/** The row's face badge: a progress ring while partly processed, an amber dot when done with failures. */
export function FaceCoverageBadge({ coverage, state, label }: Props) {
  if (state === 'partial') {
    // Floor, with a 1% minimum: a started folder always shows a sliver, and 99.6% never looks full.
    const value = Math.max(1, Math.floor(((coverage.done + coverage.failed) / coverage.total) * 100))
    return (
      <Tooltip title={label}>
        <Box
          role="img"
          aria-label={label}
          sx={{ position: 'relative', display: 'inline-flex', ml: 0.5, flexShrink: 0 }}
        >
          <CircularProgress
            aria-hidden
            variant="determinate"
            value={100}
            size={12}
            thickness={6}
            sx={{ color: 'action.disabledBackground' }}
          />
          <CircularProgress
            aria-hidden
            variant="determinate"
            value={value}
            size={12}
            thickness={6}
            sx={{ color: FACE_DONE, position: 'absolute', left: 0 }}
          />
        </Box>
      </Tooltip>
    )
  }
  if (state === 'completeWithFailures') {
    return (
      <Tooltip title={label}>
        <Box
          role="img"
          aria-label={label}
          sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: 'warning.main', ml: 0.75, flexShrink: 0 }}
        />
      </Tooltip>
    )
  }
  return null
}
```

- [ ] **Step 8: Wire the tree**

`web/src/tree/FolderTree.tsx`:
- imports: add `useFaceCoverage` to the `../api/queries` import; add `import type { FolderFaceCoverage } from '../api/types'` and `import { useFolderJobs } from './FolderJobsContext'`.
- module level: `const NO_COVERAGE: ReadonlyMap<number, FolderFaceCoverage> = new Map()`
- in the component, after `const selected = useFolder(selectedId)`:

```tsx
  const { activeJob } = useFolderJobs()
  // Failures and loading just mean no face badges; the tree itself never waits for this.
  const faceCoverage = useFaceCoverage(activeJob?.kind === 'face-recognitions').data ?? NO_COVERAGE
```

- pass `faceCoverage={faceCoverage}` to each root `<FolderTreeNode>`.

`web/src/tree/FolderTreeNode.tsx`:
- imports: remove `CheckCircleOutlinedIcon`; add `FACE_DONE` to the accent import; add `import type { FolderFaceCoverage } from '../api/types'` (merge with the `FolderNode` import), `import { FaceCoverageBadge } from './FaceCoverageBadge'`, `import { faceCoverageLabel, faceCoverageState } from './faceCoverage'`.
- `Props`: add

```ts
  /** Face coverage by folder id, for this node and its descendants. */
  faceCoverage: ReadonlyMap<number, FolderFaceCoverage>
```

  and destructure `faceCoverage` in the parameter list.
- after `const dimmed = …`:

```tsx
  const coverage = faceCoverage.get(node.id)
  const faceState = faceCoverageState(coverage)
  const faceLabel = faceCoverageLabel(coverage)
  const facesDone = faceState === 'complete' || faceState === 'completeWithFailures'
  const notScanned = !node.isScanned && !facesDone
  const folderIconSx = {
    mr: 0.75,
    color: facesDone ? FACE_DONE : notScanned ? 'text.disabled' : selected ? ACCENT : 'text.secondary',
  }
  const FolderIcon = expanded ? FolderOpenOutlinedIcon : FolderOutlinedIcon
```

- replace the `) : expanded ? ( <FolderOpenOutlinedIcon … /> ) : ( <FolderOutlinedIcon … /> )}` branch of the icon ternary (lines 107-117) with:

```tsx
        ) : (
          <Tooltip title={faceLabel ?? (notScanned ? 'Not scanned yet' : '')}>
            <FolderIcon
              fontSize="small"
              data-testid={`folder-icon-${node.id}`}
              data-face-state={faceState}
              data-not-scanned={notScanned || undefined}
              sx={folderIconSx}
            />
          </Tooltip>
        )}
```

- replace the "Scanned" check block (lines 133-137) with:

```tsx
        {!excluded && coverage && faceLabel && (
          <FaceCoverageBadge coverage={coverage} state={faceState} label={faceLabel} />
        )}
```

- pass `faceCoverage={faceCoverage}` to the recursive child `<FolderTreeNode>`.

- [ ] **Step 9: Run tree and folder tests**

Run: `npx vitest run src/tree`
Expected: all pass. If the "no longer shows the Scanned check" test finds no tooltip (Madeira is scanned, so its title is its face label), that's correct behaviour. Hovering yields the face label, which doesn't contain "Scanned".

- [ ] **Step 10: Full frontend checks**

Run: `npm run test` then `npm run build` (from `web/`)
Expected: all tests pass; type-check and build succeed. Fix any other test that renders `FolderTreeNode` directly without the new prop.

- [ ] **Step 11: Commit**

```bash
git add web/src/api/types.ts web/src/api/queries.ts web/src/design/accent.ts web/src/tree/faceCoverage.ts web/src/tree/faceCoverage.test.ts web/src/tree/FaceCoverageBadge.tsx web/src/tree/FolderTree.tsx web/src/tree/FolderTreeNode.tsx web/src/test/fixtures.ts web/src/test/handlers.ts web/src/tree/FolderTree.test.tsx
git commit -m "feat(tree): face coverage badge and sky-blue icon for processed folders"
```

---

### Task 4: Whole-solution verification

- [ ] **Step 1:** `dotnet build` then `dotnet test` from the repo root. Expected: green. Report failures with ≤20 lines of output.
- [ ] **Step 2:** Manual check (optional, needs the running app). Run face recognition on one small folder. The tree should show its ring filling during the job (10 s polling). On completion the folder turns sky-blue and its parent shows a partial ring.
- [ ] **Step 3:** Performance check against a restored dump (optional): `EXPLAIN ANALYZE` the grouped count from `GetFolderFaceCountsAsync` (copy it from the Serilog EF command log). Expected: well under a second on the full library. If not, check for an index on `"Images"."FolderId"`.
