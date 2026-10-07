# Hide Images Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user hide photos (one or many, via the existing multi-select bar) so they vanish from every view and from face recognition, and can be found and unhidden again only from the folder view's "Show hidden" toggle.

**Architecture:** A new `Image.IsHidden` flag. `WhereVisible` (the single visibility rule) starts excluding hidden images, so every existing query, the face job's candidates/coverage, duplicates and the grids drop them automatically. A new `WhereExisting` keeps the old rule (present on disk, active folder/root) for the few places that must still see hidden rows: the folder list when `includeHidden=true`, image detail (the viewer), and the hide endpoint itself. Face queries that do not go through `WhereVisible` (clustering, neighbours, people counts) get an explicit `!Image.IsHidden`. One bulk endpoint `PUT /api/images/hidden` serves both Hide and Unhide; the frontend adds a Hide/Unhide button to `SelectionBar` and a `?hidden=1` toggle on the folder view.

**Tech Stack:** .NET 10, EF Core 10 + PostgreSQL (xUnit, FluentAssertions, real-Postgres test DB), React 19, TypeScript, TanStack Query, MUI, Vitest + msw.

**Spec:** No spec file. Design agreed in chat on 2026-10-07: boolean flag; hide via multi-select bar; "Show hidden" only in the folder view; hidden images excluded from face recognition.

## Global Constraints

- The flag is `Image.IsHidden` (bool, default false, NOT NULL). Not a timestamp, not an `IndexState` value, not `MissingSinceUtc`.
- A rescan, re-enrichment or content change must never reset `IsHidden` (the scanner never writes it).
- Hidden images appear only in `GET /api/images?folderId=…&includeHidden=true`. `includeHidden` without `folderId` is ignored (treated as false).
- Face recognition must ignore hidden images: no new face jobs, no clustering, no suggestions from or to them, no people counts.
- Hidden images keep their thumbnails/previews (`/api/images/{id}/thumbnail|preview` ignore the flag).
- Backend: file-scoped namespaces, no full-solution builds just to check errors (build/test the touched project). Frontend: no semicolons, single quotes, 2-space indent, MUI `sx` + Tailwind like neighbours.
- Commands: backend from `C:\Work\PictureManager`, frontend from `C:\Work\PictureManager\web`. Keep build/test error output under ~20 lines.
- Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Review Focus

- **Hiding an image that is in an album:** album views must treat it as unavailable (flagged `IsMissing`, like a missing file) rather than showing it or crashing. Pinned in Task 3.
- **Folder `imageCount` and the "No photos directly in this folder" banner** must not count hidden images, or the banner and grid disagree. Pinned in Task 3.
- **Hide/unhide with an empty list, a huge list, or ids that don't exist/aren't visible:** empty = 422, more than 5000 = 422, unknown ids are silently skipped (affected count returned, no 404). Pinned in Task 3.
- **Unhiding must put the image back into face processing** without a manual re-analyse: it simply becomes a candidate again (no `FaceProcessingState` is touched). Pinned in Task 2.
- **A face on a hidden image must not become a neighbour/suggestion for other people's faces.** Pinned in Task 2.
- **Selecting hidden photos in "Show hidden" view shows Unhide, not Hide** (a mixed selection offers Unhide only if every selected photo is hidden). Pinned in Task 5.

## File Structure

- Modify `src/PictureManager.Model/Image.cs` — add `IsHidden`.
- Modify `ImageConfiguration.cs` — default (no extra index).
- Create migration `AddImageIsHidden` in `src/PictureManager.Infrastructure/Migrations/`.
- Modify `src/PictureManager.Infrastructure/Persistence/Queries/VisibilityExtensions.cs` — `WhereVisible` hides, new `WhereExisting`.
- Modify `src/PictureManager.Infrastructure/Persistence/Repositories/FaceRepository.cs`, `PeopleRepository.cs`, `AlbumRepository.cs`, `FolderRepository.cs`, `ImageQueryRepository.cs`.
- Modify `src/PictureManager.Application/Images/` — `ImageQueryModels.cs`, `ImageDtos.cs`, `IImageQueryService.cs`, `ImageQueryService.cs`; `Repositories/IImageQueryRepository.cs`.
- Modify `src/PictureManager.Infrastructure/Persistence/Queries/ImageProjections.cs`.
- Modify `src/PictureManager.Api/Endpoints/ImageQueryEndpoints.cs`.
- Tests: `tests/PictureManager.Infrastructure.Tests/Support/TestData.cs`, `Persistence/Repositories/ImageQueryRepositoryTests.cs`, `FaceRepositoryTests.cs`, `PeopleRepositoryTests.cs` (if present, else add to the nearest people test), `tests/PictureManager.Application.Tests/Images/ImageQueryServiceTests.cs`.
- Frontend: `web/src/api/types.ts`, `api/imageFilter.ts`, `api/hidden.ts` (new), `routing/urlState.ts`, `grid/SelectionBar.tsx`, `grid/PhotoTile.tsx`, `views/ImageBrowser.tsx`, `views/FolderView.tsx`, `views/HiddenToggle.tsx` (new), `test/handlers.ts`, `views/Hide.test.tsx` (new).

---

### Task 1: IsHidden column and the visibility rule

**Files:**
- Modify: `src/PictureManager.Model/Image.cs:33`
- Modify: `src/PictureManager.Infrastructure/Persistence/Configurations/ImageConfiguration.cs:79`
- Create: migration via `dotnet ef`
- Modify: `src/PictureManager.Infrastructure/Persistence/Queries/VisibilityExtensions.cs`
- Modify: `tests/PictureManager.Infrastructure.Tests/Support/TestData.cs:35-44`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ImageQueryRepositoryTests.cs`

**Interfaces:**
- Produces: `Image.IsHidden: bool`; `IQueryable<Image>.WhereExisting()` (old rule, hidden included); `IQueryable<Image>.WhereVisible()` (= `WhereExisting()` + `!IsHidden`); `TestData.Image(..., bool isHidden = false)`.

- [ ] **Step 1: Add the test helper parameter**

In `TestData.cs`, add `bool isHidden = false` after `IndexState indexState = IndexState.Indexed` in `Image(...)` and `IsHidden = isHidden,` after `IndexState = indexState,`.

- [ ] **Step 2: Write the failing test**

Add to `ImageQueryRepositoryTests.cs`:

```csharp
    [Fact]
    public async Task ListAsync_ExcludesHiddenImages()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("active");
        var folder = TestData.Folder(root, "");
        var shown = TestData.Image(folder, "shown");
        db.Context.Images.AddRange(shown, TestData.Image(folder, "hidden", isHidden: true));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new ImageQueryRepository(context).ListAsync(NoFilter, ImageSort.Date, SortDirection.Desc, null, 50);

        rows.Select(r => r.Id).Should().Equal(shown.Id);
    }
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~ListAsync_ExcludesHiddenImages"`
Expected: build error (`IsHidden` does not exist).

- [ ] **Step 4: Add the property, mapping and index**

`Image.cs`, after `IsFavorite`:

```csharp
    /// <summary>User-hidden: out of every view and out of face recognition; only the folder view's "Show hidden" lists it.</summary>
    public bool IsHidden { get; set; }
```

`ImageConfiguration.cs`, after the `IsFavorite` index line:

```csharp
        builder.Property(x => x.IsHidden).HasDefaultValue(false);
        builder.HasIndex(x => x.FolderId).HasFilter("\"IsHidden\"").HasDatabaseName("IX_Images_FolderId_Hidden");
```

`VisibilityExtensions.cs` — replace the image `WhereVisible` with:

```csharp
    /// <summary>Present and reachable (not missing, active folder and root), hidden images included.</summary>
    public static IQueryable<Image> WhereExisting(this IQueryable<Image> images) =>
        images.Where(i => i.MissingSinceUtc == null && i.Folder!.IsActive && i.Folder.MissingSinceUtc == null && i.Folder.Root!.IsActive);

    /// <summary>What every view and the face pipeline see: existing and not hidden.</summary>
    public static IQueryable<Image> WhereVisible(this IQueryable<Image> images) =>
        images.WhereExisting().Where(i => !i.IsHidden);
```

Update the class summary comment to mention hidden images.

- [ ] **Step 5: Generate the migration**

Run: `dotnet ef migrations add AddImageIsHidden --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api`
Open the generated file and confirm it adds `IsHidden` boolean NOT NULL default false and the filtered index, and nothing else.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~ImageQueryRepositoryTests"`
Expected: all PASS (the new test and the existing ones).

- [ ] **Step 7: Commit**

```bash
git add src/PictureManager.Model src/PictureManager.Infrastructure tests/PictureManager.Infrastructure.Tests
git commit -m "feat(images): IsHidden flag; WhereVisible hides, WhereExisting keeps the old rule"
```

---

### Task 2: Face recognition ignores hidden images

**Files:**
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/FaceRepository.cs:243-299`
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/PeopleRepository.cs:25-50`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FaceRepositoryTests.cs`

**Interfaces:**
- Consumes: `Image.IsHidden` (Task 1). Candidates and folder coverage already go through `FaceScopeImages()` → `WhereVisible()`, so they exclude hidden images with no change; this task pins that with a test and fixes the queries that bypass it.

- [ ] **Step 1: Write the failing tests**

Add to `FaceRepositoryTests.cs` (`Now` and the `FaceTestData` helpers exist in this file/support):

```csharp
    [Fact]
    public async Task GetCandidateImageIdsAsync_SkipsHiddenImages_AndTheyReturnWhenUnhidden()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var folder = await FaceTestData.AddFolderAsync(db.Context, top, "Trips");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var shown = await FaceTestData.AddImageAsync(db.Context, folder, "shown");
        var hidden = await FaceTestData.AddImageAsync(db.Context, folder, "hidden");
        hidden.IsHidden = true;
        await db.Context.SaveChangesAsync();

        var repository = new FaceRepository(db.CreateContext());
        (await repository.GetCandidateImageIdsAsync(model, null, isRecursive: true)).Should().Equal(shown.Id);

        hidden.IsHidden = false;
        await db.Context.SaveChangesAsync();
        (await new FaceRepository(db.CreateContext()).GetCandidateImageIdsAsync(model, null, isRecursive: true))
            .Should().BeEquivalentTo(new[] { shown.Id, hidden.Id });
    }

    [Fact]
    public async Task FaceQueries_IgnoreFacesOnHiddenImages()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var folder = await FaceTestData.AddFolderAsync(db.Context, top, "Trips");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var shown = await FaceTestData.AddImageAsync(db.Context, folder, "shown");
        var hidden = await FaceTestData.AddImageAsync(db.Context, folder, "hidden");
        var shownFace = await FaceTestData.AddFaceAsync(db.Context, shown.Id, model, FaceTestData.Embedding(0));
        await FaceTestData.AddFaceAsync(db.Context, hidden.Id, model, FaceTestData.Embedding(0, 0.01));
        hidden.IsHidden = true;
        await db.Context.SaveChangesAsync();

        var repository = new FaceRepository(db.CreateContext());

        (await repository.GetUnassignedFacesAsync(model, 0f)).Select(f => f.FaceId).Should().Equal(shownFace.Id);
        (await repository.GetUnclusteredFacesAsync(model, 0f)).Select(f => f.FaceId).Should().Equal(shownFace.Id);
        (await repository.GetNearestAsync(shownFace.Id, model, NeighborPool.Unassigned, 10)).Should().BeEmpty();
    }
```

(If `FaceCandidate`'s id property is not named `FaceId`, use its first positional member — it is constructed as `new FaceCandidate(f.Id, f.QualityScore, f.RejectedPersonId)` at `FaceRepository.cs:247`. If `NeighborPool`'s non-assigned member has another name, use the one that the `else` branch of `GetNearestAsync` (`FaceRepository.cs:278-281`) handles.)

- [ ] **Step 2: Run to verify the second test fails**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~FaceRepositoryTests"`
Expected: `GetCandidateImageIdsAsync_SkipsHiddenImages…` PASSES already; `FaceQueries_IgnoreFacesOnHiddenImages` FAILS.

- [ ] **Step 3: Exclude hidden images in the face queries**

In `FaceRepository.cs` add `&& !f.Image!.IsHidden` to the `Where` of `GetUnassignedFacesAsync` (line 245), `GetUnclusteredFacesAsync` (line 252) and, in `GetNearestAsync`, to the `faces` base query (line 277):

```csharp
        var faces = _dbContext.Faces.AsNoTracking()
            .Where(f => f.FaceModelId == faceModelId && f.Id != faceId && f.QualityScore >= minQuality && !f.Image!.IsHidden);
```

- [ ] **Step 4: Exclude hidden images from people counts**

In `PeopleRepository.Summaries` (lines 25-40) and the `GetAllAsync` filter (lines 46-47), add `!f.Image!.IsHidden` to every `p.Faces.Where(...)` / `p.Faces.Any(...)` predicate that counts or covers (the inner `p.Faces.Any(c => c.ImageId == f.ImageId && c.AssignmentState == Confirmed)` becomes `... && !c.Image!.IsHidden`). Example for the first count:

```csharp
            p.Faces.Where(f => f.AssignmentState == FaceAssignmentState.Confirmed && !f.Image!.IsHidden)
                .Select(f => f.ImageId).Distinct().Count(),
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~FaceRepositoryTests|FullyQualifiedName~PeopleRepositoryTests"`
Expected: PASS. If a people-count test exists, add a case: a person with one face on a hidden image has that image excluded from `ConfirmedImageCount`.

- [ ] **Step 6: Commit**

```bash
git add src/PictureManager.Infrastructure tests/PictureManager.Infrastructure.Tests
git commit -m "feat(faces): hidden images are excluded from face jobs, clustering, suggestions and people counts"
```

---

### Task 3: Hide API, folder `includeHidden`, viewer detail

**Files:**
- Modify: `src/PictureManager.Application/Images/ImageQueryModels.cs:30-60`, `ImageDtos.cs:10-75`, `IImageQueryService.cs`, `ImageQueryService.cs`
- Modify: `src/PictureManager.Application/Repositories/IImageQueryRepository.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Queries/ImageProjections.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/ImageQueryRepository.cs:24-127`, `AlbumRepository.cs:100-105`, `FolderRepository.cs:81`
- Modify: `src/PictureManager.Api/Endpoints/ImageQueryEndpoints.cs`
- Test: `ImageQueryRepositoryTests.cs`, `tests/PictureManager.Application.Tests/Images/ImageQueryServiceTests.cs`

**Interfaces:**
- Produces:
  - `ImageListFilter(..., bool? HasFaces = null, bool IncludeHidden = false)`
  - `ImageRow(..., IndexState IndexState = Indexed, int? FaceId = null, bool IsHidden = false)`
  - `ImageListItem(..., int? FaceId = null, bool IsHidden = false)`; `ImageDetail(..., bool IsInvalid, bool IsHidden)`
  - `ImageListRequest(..., string? Faces = null, bool IncludeHidden = false)`
  - `IImageQueryRepository.SetHiddenAsync(IReadOnlyCollection<int> ids, bool isHidden, DateTime updatedAtUtc, CancellationToken ct) : Task<int>`
  - `HiddenResult(int Affected)` (Application/Images/ImageDtos.cs)
  - `IImageQueryService.SetHiddenAsync(IReadOnlyCollection<int> imageIds, bool isHidden, CancellationToken ct) : Task<Result<HiddenResult>>`
  - HTTP: `PUT /api/images/hidden` body `{ "imageIds": [1,2], "isHidden": true }` → `200 { "affected": n }`; 422 on empty/over 5000. `GET /api/images?folderId=3&includeHidden=true`.

- [ ] **Step 1: Write the failing repository tests**

Add to `ImageQueryRepositoryTests.cs`:

```csharp
    [Fact]
    public async Task ListAsync_IncludeHiddenInAFolder_ListsHiddenImagesWithTheFlag()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("active");
        var folder = TestData.Folder(root, "");
        var other = TestData.Folder(root, "other");
        var shown = TestData.Image(folder, "shown");
        var hidden = TestData.Image(folder, "hidden", isHidden: true);
        db.Context.Images.AddRange(shown, hidden, TestData.Image(other, "elsewhere", isHidden: true));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var filter = new ImageListFilter(folder.Id, null, null, false, IncludeHidden: true);
        var rows = await new ImageQueryRepository(context).ListAsync(filter, ImageSort.Name, SortDirection.Asc, null, 50);

        rows.Select(r => (r.Id, r.IsHidden)).Should().Equal((hidden.Id, true), (shown.Id, false));
    }

    [Fact]
    public async Task SetHiddenAsync_HidesAndUnhides_SkipsUnknownAndMissing_AndReturnsTheAffectedCount()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("active");
        var folder = TestData.Folder(root, "");
        var a = TestData.Image(folder, "a");
        var b = TestData.Image(folder, "b");
        var missing = TestData.Image(folder, "missing", missingSinceUtc: TestData.Utc);
        db.Context.Images.AddRange(a, b, missing);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);

        (await repository.SetHiddenAsync(new[] { a.Id, missing.Id, 999_999 }, true, TestData.Utc)).Should().Be(1);
        (await repository.ListAsync(NoFilter, ImageSort.Name, SortDirection.Asc, null, 50)).Select(r => r.Id).Should().Equal(b.Id);

        (await repository.SetHiddenAsync(new[] { a.Id }, false, TestData.Utc)).Should().Be(1);
        (await repository.ListAsync(NoFilter, ImageSort.Name, SortDirection.Asc, null, 50)).Should().HaveCount(2);
    }

    [Fact]
    public async Task GetVisibleDetailAsync_ReturnsAHiddenImage_SoTheViewerCanOpenIt()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("active");
        var folder = TestData.Folder(root, "");
        var hidden = TestData.Image(folder, "hidden", isHidden: true);
        db.Context.Images.Add(hidden);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var detail = await new ImageQueryRepository(context).GetVisibleDetailAsync(hidden.Id);

        detail.Should().NotBeNull();
        detail!.Image.IsHidden.Should().BeTrue();
    }
```

- [ ] **Step 2: Run to verify they fail (build error)**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~ImageQueryRepositoryTests"`
Expected: build errors for `IncludeHidden`, `IsHidden`, `SetHiddenAsync`.

- [ ] **Step 3: Extend the models and DTOs**

`ImageQueryModels.cs`:

```csharp
public sealed record ImageListFilter(
    int? FolderId, string? FolderName, string? FileName, bool FavoritesOnly, int? PersonId = null, PersonFaceState? PersonState = null,
    bool? HasFaces = null, bool IncludeHidden = false);
```

and append `bool IsHidden = false` after `int? FaceId = null` in `ImageRow`.

`ImageDtos.cs`: append `bool IsHidden = false` after `int? FaceId = null` in `ImageListItem` and pass `row.IsHidden` as the last argument in `From`; append `bool IsHidden` after `bool IsInvalid` in `ImageDetail`; append `bool IncludeHidden = false` after `string? Faces = null` in `ImageListRequest`.

`ImageProjections.cs`: end the constructor call with `i.IndexState, null, i.IsHidden);`. Do the same in the two other `new ImageRow(` projections in `ImageQueryRepository.cs` (`GetVisibleDetailAsync`, `MemberProjection`) and in `AlbumRepository.ListImagesAsync` (it passes no IndexState; leave its row alone, see Step 6).

- [ ] **Step 4: Repository: list, detail, set**

`IImageQueryRepository.cs`, add:

```csharp
    /// <summary>Sets IsHidden on every existing image in `ids`; unknown or missing ids are skipped. Returns the rows changed.</summary>
    Task<int> SetHiddenAsync(IReadOnlyCollection<int> ids, bool isHidden, DateTime updatedAtUtc, CancellationToken cancellationToken = default);
```

`ImageQueryRepository.cs`:

1. In `ListAsync` replace line 28 with:

```csharp
        // Hidden images are listed only for one folder's "Show hidden" view.
        var query = filter.IncludeHidden && filter.FolderId is not null
            ? _dbContext.Images.AsNoTracking().WhereExisting()
            : _dbContext.Images.AsNoTracking().WhereVisible();
```

2. `GetVisibleDetailAsync`: change `.WhereVisible()` to `.WhereExisting()` and pass `i.IsHidden` as the last `ImageRow` argument (use `i.IndexState, null, i.IsHidden`).
3. Add:

```csharp
    public async Task<int> SetHiddenAsync(IReadOnlyCollection<int> ids, bool isHidden, DateTime updatedAtUtc, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        return await _dbContext.Images.WhereExisting()
            .Where(i => idList.Contains(i.Id) && i.IsHidden != isHidden)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.IsHidden, isHidden)
                .SetProperty(i => i.UpdatedAt, updatedAtUtc),
                cancellationToken);
    }
```

4. `MemberProjection`: append `, i.IndexState, null, i.IsHidden` — it currently ends at `i.Folder.RelativePath, i.IndexState)`, so just add `, null, i.IsHidden` before the closing parenthesis of the `ImageRow`.

- [ ] **Step 5: Service and endpoint**

`IImageQueryService.cs`:

```csharp
    Task<Result<HiddenResult>> SetHiddenAsync(IReadOnlyCollection<int> imageIds, bool isHidden, CancellationToken cancellationToken = default);
```

`ImageQueryService.cs`:

```csharp
    private const int MaxHideBatch = 5000;

    public async Task<Result<HiddenResult>> SetHiddenAsync(IReadOnlyCollection<int> imageIds, bool isHidden, CancellationToken cancellationToken = default)
    {
        if (imageIds.Count == 0)
            return Result.Invalid("imageIds", "Must not be empty.");
        if (imageIds.Count > MaxHideBatch)
            return Result.Invalid("imageIds", $"Must not contain more than {MaxHideBatch} ids.");

        var affected = await _images.SetHiddenAsync(imageIds, isHidden, _clock.UtcNow, cancellationToken);
        return Result<HiddenResult>.Ok(new HiddenResult(affected));
    }
```

In `ListAsync`, build the filter with the new flag: `new ImageListFilter(..., hasFaces, request.IncludeHidden)`. In `GetDetailAsync`, pass `image.IsHidden` as the new last `ImageDetail` argument.

`ImageQueryEndpoints.cs`: add `bool? includeHidden` to `ListAsync` (pass `includeHidden ?? false` as the last `ImageListRequest` argument) and:

```csharp
        user.MapPut("/images/hidden", SetHiddenAsync);
```

```csharp
    public sealed record SetHiddenRequest(IReadOnlyList<int>? ImageIds, bool IsHidden);

    public static async Task<Results<Ok<HiddenResult>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> SetHiddenAsync(
        SetHiddenRequest request, IImageQueryService service, CancellationToken cancellationToken) =>
        (await service.SetHiddenAsync(request.ImageIds ?? [], request.IsHidden, cancellationToken)).ToOk();
```

Add `public sealed record HiddenResult(int Affected);` to `ImageDtos.cs`. The response is `{ "affected": n }` (System.Text.Json camel-cases it).

- [ ] **Step 6: Albums and folder count**

`AlbumRepository.ListImagesAsync` (line 105): extend the unavailable flag with `|| ai.Image.IsHidden`.
`FolderRepository.cs:81`: `ImageCount = f.Images.Count(i => i.MissingSinceUtc == null && !i.IsHidden),`. In the raw SQL at line ~196, add `AND i."IsHidden" = FALSE` after `i."MissingSinceUtc" IS NULL`.

- [ ] **Step 7: Service tests**

Add to `ImageQueryServiceTests.cs` (mirror an existing test's setup for the substitute repository):

```csharp
    [Fact]
    public async Task SetHiddenAsync_EmptyList_IsInvalid()
    {
        var result = await CreateService().SetHiddenAsync(Array.Empty<int>(), true);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SetHiddenAsync_TooManyIds_IsInvalid()
    {
        var result = await CreateService().SetHiddenAsync(Enumerable.Range(1, 5001).ToArray(), true);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SetHiddenAsync_ReturnsTheRepositoryCount()
    {
        _images.SetHiddenAsync(Arg.Any<IReadOnlyCollection<int>>(), true, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(2);
        var result = await CreateService().SetHiddenAsync(new[] { 1, 2, 3 }, true);
        result.Value!.Affected.Should().Be(2);
    }
```

(Use the file's actual factory/field names for the service and the `IImageQueryRepository` substitute — see the existing `SetFavorite` tests there.)

- [ ] **Step 8: Run backend tests**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~ImageQueryRepositoryTests|FullyQualifiedName~Folder|FullyQualifiedName~Album"` then `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~ImageQueryServiceTests"` then `dotnet test tests/PictureManager.Api.Tests`
Expected: PASS. Fix any positional-constructor breakages (`ImageRow`, `ImageDetail`) in existing tests by appending the new argument.

- [ ] **Step 9: Commit**

```bash
git add src tests
git commit -m "feat(images): bulk hide/unhide endpoint, includeHidden folder listing, hidden flag in DTOs"
```

---

### Task 4: Frontend API layer

**Files:**
- Modify: `web/src/api/types.ts:36-55`, `web/src/api/imageFilter.ts`, `web/src/routing/urlState.ts:27-31`, `web/src/test/handlers.ts:49`
- Create: `web/src/api/hidden.ts`
- Test: `web/src/api/imageFilter.test.ts`, `web/src/routing/urlState.test.ts`

**Interfaces:**
- Produces:
  - `ImageListItem.isHidden?: boolean`
  - `ImageFilter` folder variant gains `includeHidden?: boolean`; `toImageQuery` emits `includeHidden=true`.
  - `parseHiddenParam(params: URLSearchParams): boolean` (true iff `?hidden=1`).
  - `useSetHidden()` → mutation taking `{ imageIds: number[]; isHidden: boolean }`.

- [ ] **Step 1: Write the failing tests**

`imageFilter.test.ts`:

```ts
  it('adds includeHidden for a folder filter that asks for it', () => {
    const params = toImageQuery({ kind: 'folder', folderId: 3, includeHidden: true, sort: 'date', order: 'desc' }, null)
    expect(params.get('includeHidden')).toBe('true')
    expect(toImageQuery({ kind: 'folder', folderId: 3, sort: 'date', order: 'desc' }, null).has('includeHidden')).toBe(false)
  })
```

`urlState.test.ts`:

```ts
  it('parses the hidden toggle from ?hidden=1 only', () => {
    expect(parseHiddenParam(new URLSearchParams('hidden=1'))).toBe(true)
    expect(parseHiddenParam(new URLSearchParams('hidden=0'))).toBe(false)
    expect(parseHiddenParam(new URLSearchParams(''))).toBe(false)
  })
```

(import `parseHiddenParam` in the test file.)

- [ ] **Step 2: Run to verify they fail**

Run: `npx vitest run src/api/imageFilter.test.ts src/routing/urlState.test.ts`
Expected: FAIL (`parseHiddenParam` is not exported; `includeHidden` not emitted).

- [ ] **Step 3: Implement types, filter and URL param**

`types.ts`, in `ImageListItem` after `isInvalid`:

```ts
  /** Only true in the folder view's "Show hidden" list; hidden photos appear nowhere else. */
  isHidden?: boolean
```

`imageFilter.ts`: folder variant becomes `{ kind: 'folder'; folderId: number; faces?: FacesFilter; includeHidden?: boolean }` and in `case 'folder'` add `if (filter.includeHidden === true) params.set('includeHidden', 'true')`.

`urlState.ts`, after `parseFacesParam`:

```ts
/** The folder view's "Show hidden" switch (?hidden=1). */
export function parseHiddenParam(params: URLSearchParams): boolean {
  return params.get('hidden') === '1'
}
```

- [ ] **Step 4: Add the mutation hook**

Create `web/src/api/hidden.ts`:

```ts
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNotify } from '../app/notify'
import { apiFetch } from './client'
import { queryKeys } from './queries'

type HiddenChange = { imageIds: number[]; isHidden: boolean }

/** Hides or unhides photos in bulk. Every image list, duplicate group and person list is refetched. */
export function useSetHidden() {
  const queryClient = useQueryClient()
  const notify = useNotify()

  return useMutation({
    mutationFn: ({ imageIds, isHidden }: HiddenChange) =>
      apiFetch<{ affected: number }>('/api/images/hidden', {
        method: 'PUT',
        body: JSON.stringify({ imageIds, isHidden }),
        headers: { 'Content-Type': 'application/json' },
      }),
    onSuccess: (_data, { imageIds, isHidden }) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.imageLists() })
      void queryClient.invalidateQueries({ queryKey: queryKeys.duplicates() })
      void queryClient.invalidateQueries({ queryKey: ['folders'] })
      const count = imageIds.length
      notify(`${count} photo${count === 1 ? '' : 's'} ${isHidden ? 'hidden' : 'unhidden'}.`)
    },
    onError: () => notify("Couldn't update the photos."),
  })
}
```

Before writing it, open `web/src/api/client.ts` and `web/src/api/albums.ts` and copy how a mutation there sends a JSON body (the `apiFetch` options shape) and which query-key roots exist in `queries.ts` (`queryKeys.imageLists`, `queryKeys.duplicates`, the folder/tree keys); use those exact names instead of `['folders']` if the tree uses another key.

- [ ] **Step 5: Test handler**

In `web/src/test/handlers.ts` after the favorite handlers add (and honour `includeHidden` in the `/api/images` list handler by filtering `i.isHidden` out unless `includeHidden=true`):

```ts
  http.put('/api/images/hidden', async ({ request }) => {
    const body = (await request.json()) as { imageIds: number[]; isHidden: boolean }
    hiddenIds = body.isHidden
      ? new Set([...hiddenIds, ...body.imageIds])
      : new Set([...hiddenIds].filter((id) => !body.imageIds.includes(id)))
    return HttpResponse.json({ affected: body.imageIds.length })
  }),
```

with a module-level `let hiddenIds = new Set<number>()` reset in the same place the other stores reset (`setup.ts` `beforeEach`), and in the list handler: `items = items.map((i) => ({ ...i, isHidden: hiddenIds.has(i.id) })).filter((i) => i.isHidden === (query.get('includeHidden') === 'true' ? i.isHidden : false))`.

- [ ] **Step 6: Run and commit**

Run: `npx vitest run src/api src/routing`
Expected: PASS.

```bash
git add web/src
git commit -m "feat(web): hide api hook, includeHidden filter and ?hidden param"
```

---

### Task 5: Hide / Unhide in the selection bar and the folder "Show hidden" toggle

**Files:**
- Modify: `web/src/grid/SelectionBar.tsx`, `web/src/grid/PhotoTile.tsx`, `web/src/views/ImageBrowser.tsx`, `web/src/views/FolderView.tsx`
- Create: `web/src/views/HiddenToggle.tsx`
- Test: `web/src/views/Hide.test.tsx`

**Interfaces:**
- Consumes: `useSetHidden` (Task 4), `parseHiddenParam`, `ImageFilter.includeHidden`, `ImageListItem.isHidden`.
- Produces: `SelectionBar` props `onHide?: () => void` and `hideLabel?: 'Hide' | 'Unhide'`; `ImageBrowser` prop `hideable?: boolean` (true only for the folder view).

- [ ] **Step 1: Write the failing tests**

Create `web/src/views/Hide.test.tsx`:

```tsx
import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { renderApp } from '../test/render'

const selectionBar = () => screen.findByRole('toolbar', { name: 'Selection' })

describe('hiding photos', () => {
  it('Hide in the selection bar removes the selected photos from the folder grid', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(within(await selectionBar()).getByRole('button', { name: 'Hide' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'IMG_0001.jpg' })).not.toBeInTheDocument())
    expect(screen.getByRole('button', { name: 'IMG_0002.jpg' })).toBeInTheDocument()
  })

  it('"Show hidden" lists hidden photos, and selecting them offers Unhide', async () => {
    const { user } = renderApp('/folders/3')
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    await user.click(within(await selectionBar()).getByRole('button', { name: 'Hide' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'IMG_0001.jpg' })).not.toBeInTheDocument())

    await user.click(screen.getByRole('switch', { name: 'Show hidden' }))
    await user.click(await screen.findByRole('checkbox', { name: 'Select IMG_0001.jpg' }))
    const bar = await selectionBar()
    expect(within(bar).queryByRole('button', { name: 'Hide' })).not.toBeInTheDocument()
    await user.click(within(bar).getByRole('button', { name: 'Unhide' }))
    await user.click(screen.getByRole('switch', { name: 'Show hidden' }))
    expect(await screen.findByRole('button', { name: 'IMG_0001.jpg' })).toBeInTheDocument()
  })

  it('only the folder view offers Hide or Show hidden', async () => {
    const { user } = renderApp('/favorites')
    await user.click(await screen.findByRole('checkbox', { name: /Select / }))
    expect(within(await selectionBar()).queryByRole('button', { name: 'Hide' })).not.toBeInTheDocument()
    expect(screen.queryByRole('switch', { name: 'Show hidden' })).not.toBeInTheDocument()
  })
})
```

(`/favorites` must be the actual favourites route in `renderApp` — check `web/src/routing`; use the same route the existing favorites tests visit. The fixture photos and folder id 3 "Madeira" come from `Selection.test.tsx`.)

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/views/Hide.test.tsx`
Expected: FAIL (no Hide button / switch).

- [ ] **Step 3: SelectionBar**

Add to `Props`: `onHide?: () => void` and `hideLabel?: 'Hide' | 'Unhide'`. Render, after the "Assign to person…" block:

```tsx
      {onHide && (
        <Button
          variant="outlined"
          size="small"
          onClick={onHide}
          sx={{ borderRadius: '8px', textTransform: 'none', fontWeight: 600 }}
        >
          {hideLabel ?? 'Hide'}
        </Button>
      )}
```

(destructure the two new props in the component signature.)

- [ ] **Step 4: HiddenToggle**

Create `web/src/views/HiddenToggle.tsx`:

```tsx
import { FormControlLabel, Switch } from '@mui/material'
import { useSearchParams } from 'react-router'
import { withParams } from '../routing/urlState'

/** The folder view's "Show hidden" switch; the choice lives in ?hidden=1 so it survives reloads. */
export function HiddenToggle({ checked }: { checked: boolean }) {
  const [searchParams, setSearchParams] = useSearchParams()

  return (
    <FormControlLabel
      label="Show hidden"
      control={
        <Switch
          size="small"
          checked={checked}
          onChange={(_event, next) =>
            setSearchParams(withParams(searchParams, { hidden: next ? 1 : null }), { replace: true })
          }
        />
      }
      sx={{ mr: 0, '& .MuiFormControlLabel-label': { fontSize: 13, fontWeight: 600 } }}
    />
  )
}
```

- [ ] **Step 5: FolderView**

`FolderView.tsx`: import `parseHiddenParam` and `HiddenToggle`; `const showHidden = parseHiddenParam(searchParams)`; add `...(showHidden && { includeHidden: true })` to the `filter` object; pass `hideable` to `ImageBrowser`; put `<HiddenToggle checked={showHidden} />` in the header `actions` fragment next to `<FacesFilterToggle … />`. The toggle must show even when `imageCount === 0` (all photos hidden), so change the `actions` condition: render `<HiddenToggle>` whenever `!detail.isMissing`, and keep the other three controls under the existing `imageCount > 0` condition.

- [ ] **Step 6: ImageBrowser**

Add prop `hideable?: boolean` (default false). Inside the component:

```tsx
  const setHidden = useSetHidden()
  const selectedItems = items.filter((item) => selection.selected.has(item.id))
  const allHidden = selectedItems.length > 0 && selectedItems.every((item) => item.isHidden === true)
  const hideSelected = () =>
    setHidden.mutate(
      { imageIds: selectedItems.map((item) => item.id), isHidden: !allHidden },
      { onSuccess: selection.clear },
    )
```

and on `SelectionBar`: `onHide={hideable ? hideSelected : undefined}` and `hideLabel={allHidden ? 'Unhide' : 'Hide'}`. A mixed selection (some hidden, some not) shows **Hide** (it hides the rest); this is the "Unhide only when every selected photo is hidden" rule.

- [ ] **Step 7: Hidden badge on tiles**

In `PhotoTile.tsx`, where the tile overlays are rendered (next to the corrupt-file placeholder / favourite star), render a small `VisibilityOffOutlinedIcon` chip with `aria-label="Hidden"` and reduce the image opacity to ~0.5 when `item.isHidden`. Follow the existing `dimmed` styling (`dimUnfavorited`) so no new style concepts appear.

- [ ] **Step 8: Run the frontend tests**

Run: `npx vitest run src/views src/grid src/api src/routing`
Expected: PASS. Then `npm run build` (type-check only; fix type errors, show at most 20 lines).

- [ ] **Step 9: Commit**

```bash
git add web/src
git commit -m "feat(web): hide/unhide selected photos; Show hidden toggle in the folder view"
```

---

### Task 6: Whole-suite check and migration note

- [ ] **Step 1:** `dotnet test` (all projects) and, from `web`, `npm run test` and `npm run build`. Expected: PASS, otherwise fix (cap error output at ~20 lines).
- [ ] **Step 2:** Add one line to `Documents/Database-Schema.md` under `Images`: `IsHidden bool NOT NULL default false — user-hidden; out of all views and face recognition; listed only by the folder view's Show hidden.`
- [ ] **Step 3: Commit**

```bash
git add Documents/Database-Schema.md
git commit -m "docs: document Images.IsHidden"
```

---

## Self-Review

- **Spec coverage:** boolean flag → T1; multi-select Hide on the top bar → T5 (`SelectionBar`); "Show hidden" only in the folder view → T3 (`includeHidden` needs `folderId`) + T5 (`FolderView` only); excluded from face recognition → T1 (`FaceScopeImages` via `WhereVisible`) + T2 (clustering/neighbours/people counts). Albums and folder counts → T3 step 6.
- **Placeholders:** the only deliberate "look at the neighbouring code" instructions are the `Result` error-mapping helper (T3 step 5), the query-key names (T4 step 4) and the favourites route in the test (T5 step 1); each names exactly where to look and what to copy.
- **Type consistency:** `IsHidden`, `IncludeHidden`, `SetHiddenAsync(ids, isHidden, …)`, `useSetHidden`, `parseHiddenParam`, `hideable`, `onHide`, `hideLabel` are used with the same names in every task.
- **Review Focus coverage:** albums → T3 step 6; folder count/banner → T3 step 6; empty/huge/unknown ids → T3 steps 5, 7 and repository test; unhide re-enters face processing → T2 test 1; hidden-face neighbours → T2 test 2; Unhide only when all selected are hidden → T5 step 6 + test 2.
