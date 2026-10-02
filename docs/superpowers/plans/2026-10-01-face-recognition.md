# Face Recognition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a cancellable, resumable "face recognition" background job, the third job kind after Discovery and Scan. It detects and embeds faces in already-scanned images, groups them into people, and lets the user name a group and browse that person's photos.

**Architecture:** The job reuses the existing single-active-job machinery (`Job` row, an in-memory channel queue, a `BackgroundService`, SSE progress). It selects candidate images from the database only, never from the NAS. Per image, an in-process ONNX Runtime pipeline (InsightFace `buffalo_l`: SCRFD detector + ArcFace embedder, with preprocessing in SkiaSharp) stores faces with 512-d embeddings in PostgreSQL + pgvector. A per-image `FaceProcessingState` records (model, content fingerprint, status, attempts), which makes re-runs resume and lets a changed file or a new model trigger re-processing. After detection, a clustering pass matches new faces to existing people (pgvector kNN) and groups the rest with DBSCAN into unnamed people. User cancellation goes through an in-memory `CancellationTokenSource` registry exposed as `POST /api/jobs/{id}/cancel`.

**Tech Stack:**
- Backend: .NET 10 Minimal APIs, EF Core 10 + Npgsql, Pgvector.EntityFrameworkCore, Microsoft.ML.OnnxRuntime (CPU), SkiaSharp. Tests use xUnit, FluentAssertions and NSubstitute.
- Frontend: React 19, TypeScript, TanStack Query, MUI, Tailwind v4. Tests use Vitest and msw.

**Spec:** `~/.claude/plans/plan-a-facerecogtition-module-humming-treehouse.md` (approved design, kept outside the repo). Background: `Documents/PictureManager_Face_Recognition_Architecture.md`. Executors read both.

## Global Constraints

- **Job kind:** append `FaceRecognition` to `JobKind`; never reorder the enum, which is stored as int. The face job shares the "one active job" rule (`IJobRepository.HasActiveJobAsync`): starting one while a scan or discovery is active gives 409, and vice versa.
- **Candidates come from DB rows only:** `IndexState == Indexed`, visible (`WhereVisible()`), inside the job scope, and with no `FaceProcessingState` matching (current model, current `ContentHash`) whose status is `Completed` or `PermanentlyFailed`. The job never enumerates directories.
- **Cancellation is user-triggered** via `POST /api/jobs/{id}/cancel`. Only `FaceRecognition` jobs are cancellable (Scan and Discovery get 409). Cancelling a queued job also works. Work already committed is kept.
- **Single container:** no Python and no sidecar. Models live at `FaceRecognition:ModelDirectory` (Docker: `/app/models/buffalo_l`). Missing models fail the face job with a clear message and must not break startup or any other feature.
- **pgvector database image:** stays on `postgres:17-alpine` with pgvector compiled in. Never switch to a Debian Postgres image, because the existing volume's collation would change.
- **Embeddings** are `vector(512)`, L2-normalized, and compared with cosine distance (`<=>`). Every similarity query filters on `FaceModelId`.
- **Assignments:** clustering never changes a face whose `AssignmentState` is `Confirmed` or `Rejected`. Automatic assignment only ever sets `Auto`. Naming a person confirms their faces.
- **Code style:** C# uses file-scoped namespaces and explicit constructors (match the surrounding files), and repositories use `ExecuteUpdateAsync`/`ExecuteDeleteAsync` for set-based writes. Frontend: no semicolons, single quotes, 2-space indent, MUI + Tailwind.
- **Commands:** backend commands run from `C:\Work\PictureManager`, frontend from `C:\Work\PictureManager\web`. Postgres-backed tests need `docker compose up -d db`. Keep pasted build/test error output under ~20 lines.
- **Commits:** every commit message ends with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. Work on branch `feature/face-recognition`, never on `main`.

## Review Focus

- **NAS share offline mid-job:** every file looks missing. Images must be **skipped without writing any state**, never counted as attempts, and never become `PermanentlyFailed`. Pinned in Task 10 (`ProcessAsync_FileMissing_SkipsWithoutWritingState`).
- **Cancel races:** a cancel while the job is still queued must end it `Cancelled` without processing. A cancel after it completed must return 404 and never flip `Completed` to `Cancelled`. Pinned in Task 2 (`CancelJobAsync_CompletedJob_ReturnsNotFound`) and Task 11 (`CancelledWhileQueued_RunsWithCancelledToken_AndLoopContinues`).
- **Image with zero faces:** must be recorded `Completed` with no faces and not re-processed by the next run. Pinned in Task 9 (`SaveResultAsync_NoFaces_RecordsCompletedState`) and Task 10.
- **Image deleted while the job runs:** saving its faces must not throw an FK error that fails the whole job; the image is skipped. Pinned in Task 9 (`SaveResultAsync_ImageDeleted_ReturnsFalse`).
- **Naming input:** names are trimmed; a blank name is 400; naming a group with an existing name in different case ("anna" vs "Anna") merges into the existing person. Named people with 0 faces survive clustering cleanup. Pinned in Task 14 and Task 12.

## File Structure

**Model** (`src/PictureManager.Model/`): `JobKind.cs` (modify), `Job.cs` (modify), and new `FaceModel.cs`, `FaceProcessingState.cs`, `FaceProcessingStatus.cs`, `Face.cs`, `FaceAssignmentState.cs` and `Person.cs`.

**Application** (`src/PictureManager.Application/`):
- `Common/IJobCancellationRegistry.cs`, `Common/JobCancellationRegistry.cs`: in-memory job cancellation.
- `Scanning/ScanTargets.cs` (modify): add the shared `ResolveJobFolderIdAsync`.
- `Faces/`:
  - Analyzer contract: `IFaceAnalyzer.cs`, `FaceModelUnavailableException.cs`, `FaceRecognitionOptions.cs`.
  - Job plumbing: `QueuedFaceRecognition.cs`, `IFaceRecognitionQueue.cs`, `FaceRecognitionAlreadyInProgressException.cs`, `IFaceRecognitionService.cs`, `FaceRecognitionService.cs`.
  - Per image: `IFaceImageProcessor.cs`, `FaceImageProcessor.cs`.
  - Clustering: `FaceClustering.cs` (pure), `IFaceClusterer.cs`, `FaceClusterer.cs`.
  - People: `IPeopleService.cs`, `PeopleService.cs`, `IFaceCropService.cs`.
- `Repositories/IFaceRepository.cs`, `Repositories/IPeopleRepository.cs`.
- `Images/ImageQueryModels.cs`, `Images/ImageDtos.cs`, `Images/ImageQueryService.cs` (modify): add the `PersonId` filter.

**Infrastructure** (`src/PictureManager.Infrastructure/`):
- `Faces/`: `FaceAligner.cs`, `ScrfdDecoder.cs`, `FaceQuality.cs`, `OnnxFaceAnalyzer.cs`, `FaceCropService.cs`.
- `Imaging/SkiaBitmapOps.cs` (modify): add `DecodeDownsampled`.
- `Persistence/Configurations/`: `FaceModelConfiguration.cs`, `FaceProcessingStateConfiguration.cs`, `FaceConfiguration.cs`, `PersonConfiguration.cs`.
- `Persistence/Repositories/FaceRepository.cs`, `PeopleRepository.cs`; `JobRepository.cs`, `ImageQueryRepository.cs` (modify).
- `Migrations/<timestamp>_FaceRecognition.cs` (generated).

**Worker** (`src/PictureManager.Worker/Faces/`): `ChannelFaceRecognitionQueue.cs`, `FaceRecognitionBackgroundService.cs`.

**API** (`src/PictureManager.Api/`):
- `Endpoints/JobEndpoints.cs` (modify).
- `Endpoints/FaceRecognitionEndpoints.cs`, `Endpoints/PeopleEndpoints.cs`.
- `Endpoints/ImageQueryEndpoints.cs` (modify).
- `Program.cs` (modify).

**Frontend** (`web/src/`):
- `api/jobs.ts`, `api/types.ts`, `api/imageFilter.ts` (modify); `api/people.ts` (new).
- `tree/FolderJobsContext.tsx`, `tree/JobStatusBanner.tsx`, `tree/FolderActionsMenu.tsx` (modify).
- `people/PeoplePage.tsx`, `people/PersonView.tsx` (new).
- `app/routes.tsx`, `app/AppShell.tsx` (modify).
- `test/jobHandlers.ts` (modify), `test/peopleHandlers.ts` (new).

**Deploy:** `docker/postgres/Dockerfile` (new); `docker-compose.yml`, `docker-compose.prod.yml`, `docker-image.nginx.yml`, `Dockerfile`, `.gitignore` (modify); `tools/download-face-models.ps1` (new); the guides in `Documents/` (modify).

## Deliberate deviations from the design

- **No separate read and inference worker pools.** One `Parallel.ForEachAsync` loop runs with `ReadConcurrency + InferenceConcurrency` workers, and `OnnxFaceAnalyzer` caps model execution with a `SemaphoreSlim(InferenceConcurrency)`. Decoding (NAS I/O) overlaps inference, and both knobs stay independently tunable, without a channel pipeline.
- **No `lastScannedAt` gating on the "Recognize faces" menu item.** The folder DTO doesn't expose it. An unscanned folder simply has 0 candidates and the job completes at once.
- **Clustering step (a) matches new faces to any person** (named or unnamed group), not only to named people. Otherwise every run would create duplicate unnamed groups for people already grouped.

---

### Task 0: Branch

- [ ] **Step 1: Create the feature branch**

```bash
cd /c/Work/PictureManager
git checkout -b feature/face-recognition
```

Expected: `Switched to a new branch 'feature/face-recognition'`. (Uncommitted files on `main` come along untouched; never `git add` files outside each task's list.)

---

### Task 1: Job cancellation registry

**Files:**
- Create: `src/PictureManager.Application/Common/IJobCancellationRegistry.cs`
- Create: `src/PictureManager.Application/Common/JobCancellationRegistry.cs`
- Modify: `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Application.Tests/Common/JobCancellationRegistryTests.cs`

**Interfaces:**
- Produces: `IJobCancellationRegistry { CancellationToken Register(int jobId); bool Cancel(int jobId); void Release(int jobId); }`, registered as a singleton. `Register` is idempotent and returns the same token for the same job id.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Threading;
using FluentAssertions;
using PictureManager.Application.Common;
using Xunit;

namespace PictureManager.Application.Tests.Common;

public class JobCancellationRegistryTests
{
    [Fact]
    public void Cancel_RegisteredJob_CancelsItsToken()
    {
        var registry = new JobCancellationRegistry();
        var token = registry.Register(5);

        registry.Cancel(5).Should().BeTrue();

        token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Register_SameJobTwice_ReturnsTheSameToken()
    {
        var registry = new JobCancellationRegistry();
        var first = registry.Register(5);
        registry.Cancel(5);

        registry.Register(5).IsCancellationRequested.Should().BeTrue();
        first.Should().Be(registry.Register(5));
    }

    [Fact]
    public void Cancel_UnknownJob_ReturnsFalse()
    {
        new JobCancellationRegistry().Cancel(42).Should().BeFalse();
    }

    [Fact]
    public void Cancel_AfterRelease_ReturnsFalse()
    {
        var registry = new JobCancellationRegistry();
        registry.Register(5);
        registry.Release(5);

        registry.Cancel(5).Should().BeFalse();
    }

    [Fact]
    public void Release_UnknownJob_DoesNotThrow()
    {
        var act = () => new JobCancellationRegistry().Release(1);
        act.Should().NotThrow();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~JobCancellationRegistryTests"`
Expected: build FAILS with `The type or namespace name 'JobCancellationRegistry' could not be found`.

- [ ] **Step 3: Write the implementation**

`src/PictureManager.Application/Common/IJobCancellationRegistry.cs`:

```csharp
using System.Threading;

namespace PictureManager.Application.Common;

/// <summary>
/// In-memory, per-process cancellation for user-cancellable jobs. A job registers when it's queued (so a cancel
/// while it still waits in the queue works too) and is released when its runner finishes. Lost on restart, which
/// is fine: FailInterruptedJobsAsync fails any job a previous process left active.
/// </summary>
public interface IJobCancellationRegistry
{
    /// <summary>The job's token, created on first call. Idempotent: the same job id always gets the same token.</summary>
    CancellationToken Register(int jobId);

    /// <summary>Requests cancellation. False when the job isn't registered (unknown, finished, or not cancellable).</summary>
    bool Cancel(int jobId);

    void Release(int jobId);
}
```

`src/PictureManager.Application/Common/JobCancellationRegistry.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace PictureManager.Application.Common;

public sealed class JobCancellationRegistry : IJobCancellationRegistry
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _sources = new();

    public CancellationToken Register(int jobId) =>
        _sources.GetOrAdd(jobId, _ => new CancellationTokenSource()).Token;

    public bool Cancel(int jobId)
    {
        if (!_sources.TryGetValue(jobId, out var source))
            return false;

        try
        {
            source.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // Released (job finished) between the lookup and the cancel.
            return false;
        }
    }

    public void Release(int jobId)
    {
        if (_sources.TryRemove(jobId, out var source))
            source.Dispose();
    }
}
```

In `ApplicationServiceCollectionExtensions.AddApplication`, after `services.AddSingleton<ICurrentUser, SystemCurrentUser>();` add:

```csharp
        services.AddSingleton<IJobCancellationRegistry, JobCancellationRegistry>();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~JobCancellationRegistryTests"`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/PictureManager.Application/Common/IJobCancellationRegistry.cs src/PictureManager.Application/Common/JobCancellationRegistry.cs src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs tests/PictureManager.Application.Tests/Common/JobCancellationRegistryTests.cs
git commit -m "feat: in-memory job cancellation registry

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: FaceRecognition job kind, completion primitive and cancel endpoint

**Files:**
- Modify: `src/PictureManager.Model/JobKind.cs`
- Modify: `src/PictureManager.Application/Repositories/IJobRepository.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/JobRepository.cs`
- Modify: `src/PictureManager.Api/Endpoints/JobEndpoints.cs`
- Test: `tests/PictureManager.Api.Tests/Endpoints/JobEndpointsTests.cs` (add tests)
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/JobRepositoryCompletionTests.cs` (new)

**Interfaces:**
- Consumes: `IJobCancellationRegistry` (Task 1).
- Produces:
  - `JobKind.FaceRecognition`.
  - `IJobRepository.TryMarkCompletedAsync(int jobId, JobStatus fromStatus, DateTime completedUtc, CancellationToken)` → `Task<bool>`.
  - `JobEndpoints.CancelJobAsync(int id, IJobRepository jobs, IJobCancellationRegistry cancellations, CancellationToken)` → `Task<IResult>`, which returns 202, 404 or 409.

- [ ] **Step 1: Write the failing endpoint tests**

Append to `JobEndpointsTests` (it already has `using NSubstitute; using PictureManager.Model;`; add `using PictureManager.Application.Common;`):

```csharp
    [Fact]
    public async Task CancelJobAsync_ActiveFaceJob_CancelsAndReturnsAccepted()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.GetByIdAsync(9, Arg.Any<CancellationToken>())
            .Returns(new Job { Id = 9, Kind = JobKind.FaceRecognition, Status = JobStatus.Enriching });
        var registry = new JobCancellationRegistry();
        var token = registry.Register(9);

        var result = await JobEndpoints.CancelJobAsync(9, jobs, registry, CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Accepted>();
        token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task CancelJobAsync_ScanJob_ReturnsConflict()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.GetByIdAsync(9, Arg.Any<CancellationToken>())
            .Returns(new Job { Id = 9, Kind = JobKind.Scan, Status = JobStatus.Enumerating });

        var result = await JobEndpoints.CancelJobAsync(9, jobs, new JobCancellationRegistry(), CancellationToken.None);

        result.Should().BeAssignableTo<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task CancelJobAsync_CompletedJob_ReturnsNotFound()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.GetByIdAsync(9, Arg.Any<CancellationToken>())
            .Returns(new Job { Id = 9, Kind = JobKind.FaceRecognition, Status = JobStatus.Completed });
        var registry = new JobCancellationRegistry();
        var token = registry.Register(9);

        var result = await JobEndpoints.CancelJobAsync(9, jobs, registry, CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NotFound>();
        token.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task CancelJobAsync_UnknownJob_ReturnsNotFound()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.GetByIdAsync(9, Arg.Any<CancellationToken>()).Returns((Job?)null);

        var result = await JobEndpoints.CancelJobAsync(9, jobs, new JobCancellationRegistry(), CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NotFound>();
    }
```

- [ ] **Step 2: Write the failing repository test**

`tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/JobRepositoryCompletionTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class JobRepositoryCompletionTests
{
    [Fact]
    public async Task TryMarkCompletedAsync_OnlyFlipsFromTheGivenStatus()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var job = new Job { Kind = JobKind.FaceRecognition, Status = JobStatus.Enriching, StartedUtc = DateTime.UtcNow };
        db.Context.Jobs.Add(job);
        await db.Context.SaveChangesAsync();
        var repository = new JobRepository(db.Context);

        (await repository.TryMarkCompletedAsync(job.Id, JobStatus.Enumerating, DateTime.UtcNow)).Should().BeFalse();
        (await repository.TryMarkCompletedAsync(job.Id, JobStatus.Enriching, DateTime.UtcNow)).Should().BeTrue();

        var reloaded = await repository.GetByIdAsync(job.Id);
        reloaded!.Status.Should().Be(JobStatus.Completed);
        reloaded.CompletedUtc.Should().NotBeNull();
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~JobEndpointsTests"`
Expected: build FAILS (`JobKind` has no `FaceRecognition`; `CancelJobAsync` not found).

- [ ] **Step 4: Implement**

`JobKind.cs`: append the member (keep the existing order):

```csharp
public enum JobKind
{
    Scan,
    Discovery,
    FaceRecognition
}
```

In `src/PictureManager.Model/Job.cs`, change the class summary to `/// <summary>A background job: an image scan, a folder discovery or a face recognition. Only one runs at a time.</summary>`.

`IJobRepository.cs`: add below `TryMarkCompletedFromEnumeratingAsync`:

```csharp
    /// <summary>Atomically flips Status from fromStatus to Completed (setting CompletedUtc). Returns whether a row changed.</summary>
    Task<bool> TryMarkCompletedAsync(int jobId, JobStatus fromStatus, DateTime completedUtc, CancellationToken cancellationToken = default);
```

`JobRepository.cs`: add below `TryMarkCompletedFromEnumeratingAsync`:

```csharp
    public async Task<bool> TryMarkCompletedAsync(int jobId, JobStatus fromStatus, DateTime completedUtc, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.Jobs
            .Where(j => j.Id == jobId && j.Status == fromStatus)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Completed)
                .SetProperty(j => j.CompletedUtc, completedUtc),
                cancellationToken);
        return rows > 0;
    }
```

`JobEndpoints.cs`: replace the file body with:

```csharp
using Microsoft.AspNetCore.Http;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Api.Endpoints;

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapGet("/jobs/active", GetActiveJobAsync);
        admin.MapPost("/jobs/{id:int}/cancel", CancelJobAsync);
        return admin;
    }

    public static async Task<IResult> GetActiveJobAsync(IJobRepository jobs, CancellationToken cancellationToken)
    {
        var job = await jobs.GetActiveAsync(cancellationToken);
        if (job is null)
            return Results.NoContent();

        return Results.Ok(new ActiveJobDto(
            job.Kind.ToString(), job.Id, job.FolderId, job.Status.ToString(),
            job.FoldersProcessed, job.FilesFound, job.FilesEnriched, job.ErrorMessage));
    }

    /// <summary>
    /// 202 = cancellation requested (the runner records Cancelled shortly). 404 = no such job, or it already
    /// finished. 409 = this kind can't be cancelled (only face recognition can, for now).
    /// </summary>
    public static async Task<IResult> CancelJobAsync(
        int id, IJobRepository jobs, IJobCancellationRegistry cancellations, CancellationToken cancellationToken)
    {
        var job = await jobs.GetByIdAsync(id, cancellationToken);
        if (job is null || job.Status is not (JobStatus.Enumerating or JobStatus.Enriching))
            return Results.NotFound();

        if (job.Kind != JobKind.FaceRecognition)
            return Results.Conflict(new { message = "Only face recognition jobs can be cancelled." });

        return cancellations.Cancel(id) ? Results.Accepted() : Results.NotFound();
    }
}

public sealed record ActiveJobDto(
    string Kind, int Id, int? FolderId, string Status,
    int FoldersProcessed, int FilesFound, int FilesEnriched, string? ErrorMessage);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~JobEndpointsTests"`
Expected: PASS (6 tests).
Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~JobRepositoryCompletionTests"` (needs `docker compose up -d db`)
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/PictureManager.Model/JobKind.cs src/PictureManager.Model/Job.cs src/PictureManager.Application/Repositories/IJobRepository.cs src/PictureManager.Infrastructure/Persistence/Repositories/JobRepository.cs src/PictureManager.Api/Endpoints/JobEndpoints.cs tests/PictureManager.Api.Tests/Endpoints/JobEndpointsTests.cs tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/JobRepositoryCompletionTests.cs
git commit -m "feat: FaceRecognition job kind and POST /jobs/{id}/cancel

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Shared job-scope resolver

**Files:**
- Modify: `src/PictureManager.Application/Scanning/ScanTargets.cs`
- Modify: `src/PictureManager.Application/Scanning/ScanService.cs:50-78` (`QueueScanAsync`)
- Modify: `src/PictureManager.Application/Discovery/DiscoveryService.cs:44-73` (`QueueDiscoveryAsync`)
- Test: `tests/PictureManager.Application.Tests/Scanning/ScanTargetsJobFolderTests.cs` (new)

**Interfaces:**
- Produces: `ScanTargets.ResolveJobFolderIdAsync(IFolderRepository folders, IImageRootRepository roots, int? rootId, int? folderId, CancellationToken)` → `Task<int?>`. It returns the job's folder: the given folder, a root's top folder, or `null` when neither id is given. It throws `FolderUnavailableException` (not found, missing or excluded) or `ScanRootUnavailableException`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public class ScanTargetsJobFolderTests
{
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();

    public ScanTargetsJobFolderTests()
    {
        _roots.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new ImageRoot { Id = 1, Name = "nas", IsActive = true });
    }

    [Fact]
    public async Task Neither_ReturnsNull()
    {
        (await ScanTargets.ResolveJobFolderIdAsync(_folders, _roots, null, null, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Root_ReturnsItsTopFolder()
    {
        _folders.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>())
            .Returns(new Folder { Id = 10, RootId = 1, RelativePath = string.Empty });

        (await ScanTargets.ResolveJobFolderIdAsync(_folders, _roots, 1, null, CancellationToken.None)).Should().Be(10);
    }

    [Fact]
    public async Task MissingFolder_Throws()
    {
        _folders.GetByIdAsync(20, Arg.Any<CancellationToken>())
            .Returns(new Folder { Id = 20, RootId = 1, IsActive = true, MissingSinceUtc = DateTime.UtcNow });

        var act = () => ScanTargets.ResolveJobFolderIdAsync(_folders, _roots, null, 20, CancellationToken.None);

        await act.Should().ThrowAsync<FolderUnavailableException>();
    }

    [Fact]
    public async Task FolderUnderExcludedAncestor_Throws()
    {
        _folders.GetByIdAsync(20, Arg.Any<CancellationToken>()).Returns(new Folder { Id = 20, RootId = 1, IsActive = true });
        _folders.HasExcludedAncestorAsync(20, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => ScanTargets.ResolveJobFolderIdAsync(_folders, _roots, null, 20, CancellationToken.None);

        await act.Should().ThrowAsync<FolderUnavailableException>();
    }

    [Fact]
    public async Task VisibleFolder_ReturnsIt()
    {
        _folders.GetByIdAsync(20, Arg.Any<CancellationToken>()).Returns(new Folder { Id = 20, RootId = 1, IsActive = true });

        (await ScanTargets.ResolveJobFolderIdAsync(_folders, _roots, null, 20, CancellationToken.None)).Should().Be(20);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~ScanTargetsJobFolderTests"`
Expected: build FAILS (`ResolveJobFolderIdAsync` not found).

- [ ] **Step 3: Implement and refactor the two callers**

Add to `ScanTargets` (after `GetActiveRootAsync`):

```csharp
    /// <summary>
    /// The folder a scan, discovery or face-recognition job is scoped to: the given folder (which must be visible,
    /// not missing and not excluded), a root's top folder, or null when neither is given (all active roots).
    /// </summary>
    public static async Task<int?> ResolveJobFolderIdAsync(
        IFolderRepository folders, IImageRootRepository roots, int? rootId, int? folderId, CancellationToken cancellationToken)
    {
        if (folderId.HasValue)
        {
            var (_, folder) = await GetVisibleFolderAsync(folders, roots, folderId.Value, cancellationToken);

            // The walk couldn't reach it; scanning or discovering its parent is what notices it's back.
            if (folder.MissingSinceUtc is not null)
                throw FolderUnavailableException.Missing(folderId.Value);

            if (folder.IsExcluded || await folders.HasExcludedAncestorAsync(folder.Id, cancellationToken))
                throw FolderUnavailableException.Excluded(folderId.Value);

            return folder.Id;
        }

        if (rootId.HasValue)
        {
            await GetActiveRootAsync(roots, rootId.Value, cancellationToken);

            // Every root has a top folder (ImageRootSeeder); it's the job's recorded scope.
            return (await folders.GetByRootAndRelativePathAsync(rootId.Value, string.Empty, cancellationToken))?.Id;
        }

        return null;
    }
```

In `ScanService.QueueScanAsync`, replace the block from `int? jobFolderId = null;` through the end of the `else if (rootId.HasValue) { ... }` block with:

```csharp
        var jobFolderId = await ScanTargets.ResolveJobFolderIdAsync(_folderRepository, _imageRootRepository, rootId, folderId, cancellationToken);
```

In `DiscoveryService.QueueDiscoveryAsync`, replace the block from `int? jobFolderId;` through the `else { throw new ArgumentException(...); }` block with:

```csharp
        if (!rootId.HasValue && !folderId.HasValue)
            throw new ArgumentException("Either rootId or folderId must be specified.");

        var jobFolderId = await ScanTargets.ResolveJobFolderIdAsync(_folderRepository, _imageRootRepository, rootId, folderId, cancellationToken);
```

- [ ] **Step 4: Run the new and existing tests**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~ScanTargetsJobFolderTests|FullyQualifiedName~ScanServiceTests|FullyQualifiedName~DiscoveryServiceTests"`
Expected: PASS (all; the existing Scan and Discovery tests are unchanged, which proves the refactor kept behavior).

- [ ] **Step 5: Commit**

```bash
git add src/PictureManager.Application/Scanning/ScanTargets.cs src/PictureManager.Application/Scanning/ScanService.cs src/PictureManager.Application/Discovery/DiscoveryService.cs tests/PictureManager.Application.Tests/Scanning/ScanTargetsJobFolderTests.cs
git commit -m "refactor: one job-scope resolver shared by scan and discovery

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Postgres image with pgvector (Alpine)

**Files:**
- Create: `docker/postgres/Dockerfile`
- Modify: `docker-compose.yml`, `docker-compose.prod.yml`, `docker-image.nginx.yml` (the `db` service's `image:` line)

**Interfaces:**
- Produces: a `db` service whose server has the `vector` extension available. Task 5's migration runs `CREATE EXTENSION vector`.

- [ ] **Step 1: Create the image**

`docker/postgres/Dockerfile`:

```dockerfile
# postgres:17-alpine plus pgvector. Stays on Alpine (musl) on purpose: switching an existing pgdata volume to a
# Debian (glibc) image changes text collation and silently corrupts text indexes.
FROM postgres:17-alpine

ARG PGVECTOR_VERSION=v0.8.0

# with_llvm=no: skip JIT bitcode (avoids needing the exact clang/llvm the base image was built with).
# OPTFLAGS="": no -march=native, so the image runs on any x86-64 NAS CPU, not just the build machine.
RUN apk add --no-cache --virtual .build-deps git build-base \
    && git clone --branch "${PGVECTOR_VERSION}" --depth 1 https://github.com/pgvector/pgvector.git /tmp/pgvector \
    && cd /tmp/pgvector \
    && make OPTFLAGS="" with_llvm=no \
    && make install with_llvm=no \
    && cd / && rm -rf /tmp/pgvector \
    && apk del .build-deps
```

- [ ] **Step 2: Point every compose `db` service at it**

In each of `docker-compose.yml`, `docker-compose.prod.yml` and `docker-image.nginx.yml`, replace

```yaml
    image: postgres:17-alpine
```

with

```yaml
    build:
      context: ./docker/postgres
    image: picturemanager-postgres:17-pgvector
```

- [ ] **Step 3: Build and verify the extension loads against the existing volume**

Run:

```bash
docker compose up -d --build db
docker compose exec db psql -U picturemanager -d picturemanager -c "CREATE EXTENSION IF NOT EXISTS vector; SELECT extversion FROM pg_extension WHERE extname = 'vector';"
```

Expected: `CREATE EXTENSION` then one row `0.8.0`. Then run the existing Postgres-backed suite to prove the swap broke nothing:

Run: `dotnet test tests/PictureManager.Infrastructure.Tests`
Expected: PASS (same count as before).

- [ ] **Step 4: Commit**

```bash
git add docker/postgres/Dockerfile docker-compose.yml docker-compose.prod.yml docker-image.nginx.yml
git commit -m "build: postgres 17 alpine image with pgvector

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Face entities, configurations and migration

**Files:**
- Create: `src/PictureManager.Model/FaceModel.cs`, `FaceProcessingStatus.cs`, `FaceProcessingState.cs`, `FaceAssignmentState.cs`, `Face.cs`, `Person.cs`
- Modify: `src/PictureManager.Model/Job.cs` (add `FacesFound`)
- Create: `src/PictureManager.Infrastructure/Persistence/Configurations/FaceModelConfiguration.cs`, `FaceProcessingStateConfiguration.cs`, `FaceConfiguration.cs`, `PersonConfiguration.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/PictureManagerDbContext.cs`
- Modify: `src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs:25`
- Modify: `tests/PictureManager.Infrastructure.Tests/Support/PostgresTestDatabase.cs` (two `UseNpgsql` calls)
- Create: `tests/PictureManager.Infrastructure.Tests/Support/FaceTestData.cs`
- Generated: `src/PictureManager.Infrastructure/Migrations/<timestamp>_FaceRecognition.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/FaceSchemaTests.cs`

**Interfaces:**
- Produces the entities, exactly as written below. They are used by every later backend task.
- Produces `FaceTestData` (test helper), used by Tasks 9, 12 and 14.

- [ ] **Step 1: Add packages**

```bash
dotnet add src/PictureManager.Model package Pgvector
dotnet add src/PictureManager.Infrastructure package Pgvector.EntityFrameworkCore
```

Expected: both resolve to the latest versions (Pgvector.EntityFrameworkCore must support EF Core 10; if restore reports an EF version conflict, pick the newest `Pgvector.EntityFrameworkCore` whose dependency is `Microsoft.EntityFrameworkCore` 10.x).

- [ ] **Step 2: Create the entities**

`FaceModel.cs`:

```csharp
using System;

namespace PictureManager.Model;

/// <summary>The model that produced a set of embeddings. Embeddings from different models are never compared.</summary>
public class FaceModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int EmbeddingDimensions { get; set; }

    /// <summary>SHA-256 (hex) of the model files; identifies the model across restarts.</summary>
    public string ModelHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}
```

`FaceProcessingStatus.cs`:

```csharp
namespace PictureManager.Model;

/// <summary>Failed = retried by the next run. PermanentlyFailed = given up (undecodable, or too many attempts).</summary>
public enum FaceProcessingStatus
{
    Completed,
    Failed,
    PermanentlyFailed
}
```

`FaceProcessingState.cs`:

```csharp
using System;

namespace PictureManager.Model;

/// <summary>
/// Face analysis outcome for one image, valid only while FaceModelId is the current model and ImageFingerprint
/// still equals the image's ContentHash; otherwise the image is a candidate again.
/// </summary>
public class FaceProcessingState
{
    public int ImageId { get; set; }
    public int FaceModelId { get; set; }
    public string ImageFingerprint { get; set; } = string.Empty;
    public FaceProcessingStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime ProcessedUtc { get; set; }

    public Image? Image { get; set; }
    public FaceModel? FaceModel { get; set; }
}
```

`FaceAssignmentState.cs`:

```csharp
namespace PictureManager.Model;

/// <summary>Auto = set by clustering/matching. Confirmed/Rejected = set by the user; automation never changes them.</summary>
public enum FaceAssignmentState
{
    Unassigned,
    Auto,
    Confirmed,
    Rejected
}
```

`Face.cs`:

```csharp
using System;
using Pgvector;

namespace PictureManager.Model;

/// <summary>A detected face. The box is normalized (0-1) against the orientation-corrected image.</summary>
public class Face
{
    public int Id { get; set; }
    public int ImageId { get; set; }
    public int FaceModelId { get; set; }
    public int? PersonId { get; set; }
    public FaceAssignmentState AssignmentState { get; set; } = FaceAssignmentState.Unassigned;
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float DetectionConfidence { get; set; }
    public float QualityScore { get; set; }

    /// <summary>L2-normalized; compared with cosine distance.</summary>
    public Vector Embedding { get; set; } = null!;
    public DateTime CreatedUtc { get; set; }

    public Image? Image { get; set; }
    public FaceModel? FaceModel { get; set; }
    public Person? Person { get; set; }
}
```

`Person.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace PictureManager.Model;

/// <summary>Name null = an unnamed group created by clustering.</summary>
public class Person
{
    public int Id { get; set; }
    public string? Name { get; set; }

    /// <summary>The face shown for this person. No FK on purpose (avoids a Faces↔People cycle); may be stale.</summary>
    public int? CoverFaceId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }

    public ICollection<Face> Faces { get; set; } = new List<Face>();
}
```

In `Job.cs`, after `FilesEnriched` add (and change the two "Scan only" comments on `FilesFound`/`FilesEnriched` to `/// <summary>Scan: files found / enriched. Face recognition: candidate images / images processed. Always 0 for a discovery job.</summary>`):

```csharp
    /// <summary>Face recognition only: faces detected so far.</summary>
    public int FacesFound { get; set; }
```

- [ ] **Step 3: Create the configurations**

`FaceModelConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class FaceModelConfiguration : IEntityTypeConfiguration<FaceModel>
{
    public void Configure(EntityTypeBuilder<FaceModel> builder)
    {
        builder.ToTable("FaceModels");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ModelHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.ModelHash).IsUnique();
    }
}
```

`FaceProcessingStateConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class FaceProcessingStateConfiguration : IEntityTypeConfiguration<FaceProcessingState>
{
    public void Configure(EntityTypeBuilder<FaceProcessingState> builder)
    {
        builder.ToTable("FaceProcessingStates");
        builder.HasKey(x => x.ImageId);
        builder.Property(x => x.ImageFingerprint).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);
        builder.Property(x => x.ProcessedUtc).HasColumnType("timestamp with time zone");

        builder.HasOne(x => x.Image).WithMany().HasForeignKey(x => x.ImageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FaceModel).WithMany().HasForeignKey(x => x.FaceModelId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.FaceModelId, x.Status });
    }
}
```

`FaceConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class FaceConfiguration : IEntityTypeConfiguration<Face>
{
    public void Configure(EntityTypeBuilder<Face> builder)
    {
        builder.ToTable("Faces");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Embedding).HasColumnType("vector(512)").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnType("timestamp with time zone");

        builder.HasOne(x => x.Image).WithMany().HasForeignKey(x => x.ImageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FaceModel).WithMany().HasForeignKey(x => x.FaceModelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Person).WithMany(p => p.Faces).HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.ImageId);
        builder.HasIndex(x => x.PersonId);
        builder.HasIndex(x => new { x.FaceModelId, x.AssignmentState });
        builder.HasIndex(x => x.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
    }
}
```

`PersonConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("People");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.CreatedUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.ModifiedUtc).HasColumnType("timestamp with time zone");
        builder.HasIndex(x => x.Name);
    }
}
```

- [ ] **Step 4: Wire the DbContext and Npgsql**

`PictureManagerDbContext`: add the sets and the extension:

```csharp
    public DbSet<FaceModel> FaceModels => Set<FaceModel>();
    public DbSet<FaceProcessingState> FaceProcessingStates => Set<FaceProcessingState>();
    public DbSet<Face> Faces => Set<Face>();
    public DbSet<Person> People => Set<Person>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PictureManagerDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
```

`InfrastructureServiceCollectionExtensions.cs:25`:

```csharp
        services.AddDbContext<PictureManagerDbContext>(options => options.UseNpgsql(connectionString, o => o.UseVector()));
```

`PostgresTestDatabase.cs`: change both `.UseNpgsql(ConnectionString)` and `.UseNpgsql(templateConnectionString)` to `.UseNpgsql(ConnectionString, o => o.UseVector())` and `.UseNpgsql(templateConnectionString, o => o.UseVector())`.

- [ ] **Step 5: Generate the migration**

```bash
dotnet ef migrations add FaceRecognition --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api
```

Expected: a new `<timestamp>_FaceRecognition.cs`. Open it and check that it contains `.Annotation("Npgsql:PostgresExtension:vector", ",,")`, the `FacesFound` column on `Jobs`, and an index on `Faces.Embedding` with `.Annotation("Npgsql:IndexMethod", "hnsw")` and `.Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" })`.

- [ ] **Step 6: Write the test helper**

`tests/PictureManager.Infrastructure.Tests/Support/FaceTestData.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using Pgvector;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Model;

namespace PictureManager.Tests.Support;

/// <summary>Seeds roots, folders, images, models and faces for face-related Postgres tests.</summary>
public static class FaceTestData
{
    public static async Task<Folder> SeedRootAsync(PictureManagerDbContext db, string mountPath = "/images")
    {
        var root = new ImageRoot { Name = "nas", MountPath = mountPath, IsActive = true, CreatedUtc = DateTime.UtcNow };
        var top = new Folder { Root = root, Name = "nas", RelativePath = string.Empty, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        db.Folders.Add(top);
        await db.SaveChangesAsync();
        return top;
    }

    public static async Task<Folder> AddFolderAsync(PictureManagerDbContext db, Folder parent, string name)
    {
        var folder = new Folder
        {
            RootId = parent.RootId,
            ParentId = parent.Id,
            Name = name,
            RelativePath = string.IsNullOrEmpty(parent.RelativePath) ? name : $"{parent.RelativePath}/{name}",
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow
        };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder;
    }

    public static async Task<Image> AddImageAsync(
        PictureManagerDbContext db, Folder folder, string fileName, IndexState state = IndexState.Indexed, string hash = "hash")
    {
        var image = new Image
        {
            FolderId = folder.Id,
            FileName = fileName,
            Extension = ".jpg",
            ContentHash = hash,
            FileSize = 100,
            FileModified = DateTime.UtcNow,
            FirstSeenUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IndexState = state
        };
        db.Images.Add(image);
        await db.SaveChangesAsync();
        return image;
    }

    public static async Task<int> AddModelAsync(PictureManagerDbContext db, string hash = "model-1")
    {
        var model = new FaceModel { Name = "test", Version = "1", EmbeddingDimensions = 512, ModelHash = hash, CreatedUtc = DateTime.UtcNow };
        db.FaceModels.Add(model);
        await db.SaveChangesAsync();
        return model.Id;
    }

    public static async Task<Face> AddFaceAsync(
        PictureManagerDbContext db, int imageId, int modelId, float[] embedding,
        int? personId = null, FaceAssignmentState state = FaceAssignmentState.Unassigned, float quality = 0.9f)
    {
        var face = new Face
        {
            ImageId = imageId,
            FaceModelId = modelId,
            PersonId = personId,
            AssignmentState = state,
            X = 0.1f, Y = 0.1f, Width = 0.2f, Height = 0.2f,
            DetectionConfidence = 0.9f,
            QualityScore = quality,
            Embedding = new Vector(embedding),
            CreatedUtc = DateTime.UtcNow
        };
        db.Faces.Add(face);
        await db.SaveChangesAsync();
        return face;
    }

    /// <summary>A unit vector along `axis`, rotated slightly toward axis+1 by `tilt` (radians): small tilt = near-duplicate.</summary>
    public static float[] Embedding(int axis, double tilt = 0)
    {
        var values = new float[512];
        values[axis] = (float)Math.Cos(tilt);
        values[(axis + 1) % 512] = (float)Math.Sin(tilt);
        return values;
    }

    public static float[] Normalize(float[] values)
    {
        var norm = (float)Math.Sqrt(values.Sum(v => v * v));
        return values.Select(v => v / norm).ToArray();
    }
}
```

- [ ] **Step 7: Write the schema tests**

`tests/PictureManager.Infrastructure.Tests/Persistence/FaceSchemaTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence;

public class FaceSchemaTests
{
    [Fact]
    public async Task Embedding_RoundTrips_AndCosineKnnIsFilteredByModel()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var modelA = await FaceTestData.AddModelAsync(db.Context, "a");
        var modelB = await FaceTestData.AddModelAsync(db.Context, "b");
        var near = await FaceTestData.AddFaceAsync(db.Context, image.Id, modelA, FaceTestData.Embedding(0, 0.1));
        await FaceTestData.AddFaceAsync(db.Context, image.Id, modelA, FaceTestData.Embedding(5));
        await FaceTestData.AddFaceAsync(db.Context, image.Id, modelB, FaceTestData.Embedding(0));
        var query = new Vector(FaceTestData.Embedding(0));

        await using var read = db.CreateContext();
        var nearest = await read.Faces
            .Where(f => f.FaceModelId == modelA)
            .OrderBy(f => f.Embedding.CosineDistance(query))
            .Select(f => new { f.Id, Distance = f.Embedding.CosineDistance(query) })
            .FirstAsync();

        nearest.Id.Should().Be(near.Id);
        nearest.Distance.Should().BeApproximately(1 - Math.Cos(0.1), 1e-4);
        (await read.Faces.FirstAsync(f => f.Id == near.Id)).Embedding.ToArray()[0].Should().BeApproximately((float)Math.Cos(0.1), 1e-6f);
    }

    [Fact]
    public async Task DeletingImage_CascadesToFacesAndState_AndDeletingPersonUnassignsFaces()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var kept = await FaceTestData.AddImageAsync(db.Context, top, "kept");
        var deleted = await FaceTestData.AddImageAsync(db.Context, top, "deleted");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var person = new Person { Name = "Anna", CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        db.Context.People.Add(person);
        await db.Context.SaveChangesAsync();
        var keptFace = await FaceTestData.AddFaceAsync(db.Context, kept.Id, model, FaceTestData.Embedding(0), person.Id, FaceAssignmentState.Confirmed);
        await FaceTestData.AddFaceAsync(db.Context, deleted.Id, model, FaceTestData.Embedding(1));
        db.Context.FaceProcessingStates.Add(new FaceProcessingState
        {
            ImageId = deleted.Id, FaceModelId = model, ImageFingerprint = "hash", Status = FaceProcessingStatus.Completed, ProcessedUtc = DateTime.UtcNow
        });
        await db.Context.SaveChangesAsync();

        await db.Context.Images.Where(i => i.Id == deleted.Id).ExecuteDeleteAsync();
        await db.Context.People.Where(p => p.Id == person.Id).ExecuteDeleteAsync();

        await using var read = db.CreateContext();
        (await read.Faces.CountAsync(f => f.ImageId == deleted.Id)).Should().Be(0);
        (await read.FaceProcessingStates.CountAsync()).Should().Be(0);
        (await read.Faces.SingleAsync(f => f.Id == keptFace.Id)).PersonId.Should().BeNull();
    }
}
```

- [ ] **Step 8: Run the tests**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests`
Expected: PASS, including the two new tests and every pre-existing one. The pre-existing `CascadeDeleteTests` use the EF InMemory provider, which builds the same model. If they now fail while building the model because of the `Vector` property or the HNSW index, it's a provider-mismatch problem, not a schema bug: stop and report it (the fix is a provider check around the Npgsql-only configuration, and that needs a decision). Make no change if they pass.

Run: `dotnet test tests/PictureManager.Api.Tests`
Expected: PASS (it links `PostgresTestDatabase.cs`).

- [ ] **Step 9: Commit**

```bash
git add src/PictureManager.Model src/PictureManager.Infrastructure/Persistence src/PictureManager.Infrastructure/Migrations src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs src/PictureManager.Infrastructure/PictureManager.Infrastructure.csproj tests/PictureManager.Infrastructure.Tests/Support tests/PictureManager.Infrastructure.Tests/Persistence/FaceSchemaTests.cs
git commit -m "feat: face, person, face model and processing state schema with pgvector

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Face alignment math

**Files:**
- Create: `src/PictureManager.Infrastructure/Faces/FaceAligner.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Faces/FaceAlignerTests.cs`

**Interfaces:**
- Produces:
  - `FaceAligner.Size` (112).
  - `FaceAligner.ArcFaceTemplate` (`(float X, float Y)[]`, 5 points).
  - `FaceAligner.EstimateSimilarity(IReadOnlyList<(float X, float Y)> source, IReadOnlyList<(float X, float Y)> target)` → `SKMatrix`, mapping source onto target.
  - `FaceAligner.Warp(SKBitmap image, IReadOnlyList<(float X, float Y)> landmarks)` → a new 112×112 `Rgba8888` `SKBitmap` (caller disposes).

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Linq;
using FluentAssertions;
using PictureManager.Infrastructure.Faces;
using SkiaSharp;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Faces;

public class FaceAlignerTests
{
    private static readonly (float X, float Y)[] Points = { (10, 20), (50, 22), (30, 40), (15, 60), (45, 61) };

    [Fact]
    public void EstimateSimilarity_SamePoints_IsIdentity()
    {
        var m = FaceAligner.EstimateSimilarity(Points, Points);

        m.ScaleX.Should().BeApproximately(1, 1e-4f);
        m.SkewX.Should().BeApproximately(0, 1e-4f);
        m.TransX.Should().BeApproximately(0, 1e-3f);
        m.TransY.Should().BeApproximately(0, 1e-3f);
    }

    [Fact]
    public void EstimateSimilarity_RecoversScaleRotationAndTranslation()
    {
        const double angle = Math.PI / 6;
        const float scale = 2f, tx = 7f, ty = -3f;
        var target = Points.Select(p => (
            X: (float)(scale * (Math.Cos(angle) * p.X - Math.Sin(angle) * p.Y) + tx),
            Y: (float)(scale * (Math.Sin(angle) * p.X + Math.Cos(angle) * p.Y) + ty))).ToArray();

        var m = FaceAligner.EstimateSimilarity(Points, target);

        foreach (var (p, expected) in Points.Zip(target))
        {
            var mapped = m.MapPoint(p.X, p.Y);
            mapped.X.Should().BeApproximately(expected.X, 1e-3f);
            mapped.Y.Should().BeApproximately(expected.Y, 1e-3f);
        }
    }

    [Fact]
    public void Warp_Returns112SquareRgba()
    {
        using var source = new SKBitmap(new SKImageInfo(200, 200, SKColorType.Rgba8888, SKAlphaType.Premul));
        source.Erase(SKColors.White);

        using var aligned = FaceAligner.Warp(source, Points);

        aligned.Width.Should().Be(112);
        aligned.Height.Should().Be(112);
        aligned.ColorType.Should().Be(SKColorType.Rgba8888);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~FaceAlignerTests"`
Expected: build FAILS (`FaceAligner` not found).

- [ ] **Step 3: Implement**

```csharp
using System.Collections.Generic;
using SkiaSharp;

namespace PictureManager.Infrastructure.Faces;

/// <summary>Aligns a detected face onto ArcFace's canonical 112×112 five-point layout.</summary>
public static class FaceAligner
{
    public const int Size = 112;

    /// <summary>InsightFace's reference landmarks (eyes, nose, mouth corners) for a 112×112 crop.</summary>
    public static readonly (float X, float Y)[] ArcFaceTemplate =
    {
        (38.2946f, 51.6963f), (73.5318f, 51.5014f), (56.0252f, 71.7366f), (41.5493f, 92.3655f), (70.7299f, 92.2041f)
    };

    /// <summary>
    /// Least-squares similarity transform (uniform scale + rotation + translation, no reflection) mapping source
    /// onto target -- the closed-form 2D case of Umeyama's method:
    /// x' = a·x − b·y + tx, y' = b·x + a·y + ty.
    /// </summary>
    public static SKMatrix EstimateSimilarity(IReadOnlyList<(float X, float Y)> source, IReadOnlyList<(float X, float Y)> target)
    {
        var n = source.Count;
        double msx = 0, msy = 0, mtx = 0, mty = 0;
        for (var i = 0; i < n; i++)
        {
            msx += source[i].X; msy += source[i].Y; mtx += target[i].X; mty += target[i].Y;
        }
        msx /= n; msy /= n; mtx /= n; mty /= n;

        double dot = 0, cross = 0, norm = 0;
        for (var i = 0; i < n; i++)
        {
            var sx = source[i].X - msx; var sy = source[i].Y - msy;
            var tx = target[i].X - mtx; var ty = target[i].Y - mty;
            dot += sx * tx + sy * ty;
            cross += sx * ty - sy * tx;
            norm += sx * sx + sy * sy;
        }

        var a = dot / norm;
        var b = cross / norm;
        var transX = mtx - a * msx + b * msy;
        var transY = mty - b * msx - a * msy;

        return new SKMatrix((float)a, (float)-b, (float)transX, (float)b, (float)a, (float)transY, 0, 0, 1);
    }

    public static SKBitmap Warp(SKBitmap image, IReadOnlyList<(float X, float Y)> landmarks)
    {
        var aligned = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(aligned);
        canvas.Clear(SKColors.Black);
        canvas.SetMatrix(EstimateSimilarity(landmarks, ArcFaceTemplate));
        using var source = SKImage.FromBitmap(image);
        canvas.DrawImage(source, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return aligned;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~FaceAlignerTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/PictureManager.Infrastructure/Faces/FaceAligner.cs tests/PictureManager.Infrastructure.Tests/Faces/FaceAlignerTests.cs
git commit -m "feat: five-point similarity alignment for ArcFace crops

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 7: SCRFD output decoding and NMS

**Files:**
- Create: `src/PictureManager.Infrastructure/Faces/ScrfdDecoder.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Faces/ScrfdDecoderTests.cs`

**Interfaces:**
- Produces:
  - `RawDetection(float X1, float Y1, float X2, float Y2, float Score, (float X, float Y)[] Landmarks)` with `RawDetection Scale(float factor)`.
  - `ScrfdDecoder.Decode(int inputSize, IReadOnlyList<float[]> scores, IReadOnlyList<float[]> boxes, IReadOnlyList<float[]> landmarks, float threshold)` → `List<RawDetection>`.
  - `ScrfdDecoder.NonMaxSuppression(IEnumerable<RawDetection>, float iouThreshold)` → `List<RawDetection>`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using PictureManager.Infrastructure.Faces;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Faces;

public class ScrfdDecoderTests
{
    private const int Input = 64; // strides 8/16/32 -> 8x8, 4x4, 2x2 cells, 2 anchors each

    private static (List<float[]> Scores, List<float[]> Boxes, List<float[]> Kps) EmptyOutputs()
    {
        var scores = new List<float[]>(); var boxes = new List<float[]>(); var kps = new List<float[]>();
        foreach (var stride in ScrfdDecoder.Strides)
        {
            var anchors = (Input / stride) * (Input / stride) * ScrfdDecoder.AnchorsPerCell;
            scores.Add(new float[anchors]); boxes.Add(new float[anchors * 4]); kps.Add(new float[anchors * 10]);
        }
        return (scores, boxes, kps);
    }

    [Fact]
    public void Decode_ScoreAboveThreshold_ProducesBoxAroundAnchorCenter()
    {
        var (scores, boxes, kps) = EmptyOutputs();
        // stride 8, anchor index 19 -> cell 9 -> column 1, row 1 -> center (8, 8)
        scores[0][19] = 0.9f;
        boxes[0][19 * 4 + 0] = 1; boxes[0][19 * 4 + 1] = 1; boxes[0][19 * 4 + 2] = 2; boxes[0][19 * 4 + 3] = 2;
        kps[0][19 * 10 + 0] = 0.5f; kps[0][19 * 10 + 1] = -0.5f;

        var detection = ScrfdDecoder.Decode(Input, scores, boxes, kps, threshold: 0.5f).Single();

        detection.X1.Should().Be(0); detection.Y1.Should().Be(0);
        detection.X2.Should().Be(24); detection.Y2.Should().Be(24);
        detection.Score.Should().Be(0.9f);
        detection.Landmarks[0].Should().Be((12f, 4f));
    }

    [Fact]
    public void Decode_ScoreBelowThreshold_IsDropped()
    {
        var (scores, boxes, kps) = EmptyOutputs();
        scores[1][3] = 0.4f;

        ScrfdDecoder.Decode(Input, scores, boxes, kps, threshold: 0.5f).Should().BeEmpty();
    }

    [Fact]
    public void NonMaxSuppression_KeepsHigherScoreOfOverlappingPair_AndDisjointBoxes()
    {
        var landmarks = new (float, float)[5];
        var best = new RawDetection(0, 0, 10, 10, 0.9f, landmarks);
        var overlapping = new RawDetection(1, 1, 11, 11, 0.8f, landmarks);
        var disjoint = new RawDetection(50, 50, 60, 60, 0.7f, landmarks);

        var kept = ScrfdDecoder.NonMaxSuppression(new[] { overlapping, disjoint, best }, 0.4f);

        kept.Should().Equal(best, disjoint);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~ScrfdDecoderTests"`
Expected: build FAILS (`ScrfdDecoder` not found).

- [ ] **Step 3: Implement**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace PictureManager.Infrastructure.Faces;

/// <summary>A face candidate in detector-input pixels: box corners, score and 5 landmarks.</summary>
public sealed record RawDetection(float X1, float Y1, float X2, float Y2, float Score, (float X, float Y)[] Landmarks)
{
    public RawDetection Scale(float factor) => new(
        X1 * factor, Y1 * factor, X2 * factor, Y2 * factor, Score,
        Landmarks.Select(p => (p.X * factor, p.Y * factor)).ToArray());
}

/// <summary>
/// Decodes InsightFace SCRFD outputs (det_10g): per stride 8/16/32, 2 anchors per grid cell, distances to the box
/// edges and landmark offsets in units of the stride, relative to the cell's top-left corner.
/// </summary>
public static class ScrfdDecoder
{
    public static readonly int[] Strides = { 8, 16, 32 };
    public const int AnchorsPerCell = 2;

    public static List<RawDetection> Decode(
        int inputSize, IReadOnlyList<float[]> scores, IReadOnlyList<float[]> boxes, IReadOnlyList<float[]> landmarks, float threshold)
    {
        var detections = new List<RawDetection>();
        for (var s = 0; s < Strides.Length; s++)
        {
            var stride = Strides[s];
            var cells = inputSize / stride;
            var score = scores[s]; var box = boxes[s]; var kps = landmarks[s];

            for (var i = 0; i < score.Length; i++)
            {
                if (score[i] < threshold)
                    continue;

                var cell = i / AnchorsPerCell;
                float cx = cell % cells * stride, cy = cell / cells * stride;
                var points = new (float X, float Y)[5];
                for (var k = 0; k < 5; k++)
                    points[k] = (cx + kps[i * 10 + 2 * k] * stride, cy + kps[i * 10 + 2 * k + 1] * stride);

                detections.Add(new RawDetection(
                    cx - box[i * 4] * stride, cy - box[i * 4 + 1] * stride,
                    cx + box[i * 4 + 2] * stride, cy + box[i * 4 + 3] * stride,
                    score[i], points));
            }
        }
        return detections;
    }

    public static List<RawDetection> NonMaxSuppression(IEnumerable<RawDetection> detections, float iouThreshold)
    {
        var kept = new List<RawDetection>();
        foreach (var candidate in detections.OrderByDescending(d => d.Score))
        {
            if (kept.All(k => IoU(k, candidate) <= iouThreshold))
                kept.Add(candidate);
        }
        return kept;
    }

    private static float IoU(RawDetection a, RawDetection b)
    {
        var w = Math.Max(0, Math.Min(a.X2, b.X2) - Math.Max(a.X1, b.X1));
        var h = Math.Max(0, Math.Min(a.Y2, b.Y2) - Math.Max(a.Y1, b.Y1));
        var intersection = w * h;
        var union = (a.X2 - a.X1) * (a.Y2 - a.Y1) + (b.X2 - b.X1) * (b.Y2 - b.Y1) - intersection;
        return union <= 0 ? 0 : intersection / union;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~ScrfdDecoderTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/PictureManager.Infrastructure/Faces/ScrfdDecoder.cs tests/PictureManager.Infrastructure.Tests/Faces/ScrfdDecoderTests.cs
git commit -m "feat: SCRFD output decoding and non-max suppression

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 8: ONNX face analyzer, quality score and model download

**Files:**
- Create: `src/PictureManager.Application/Faces/IFaceAnalyzer.cs`, `FaceModelUnavailableException.cs`, `FaceRecognitionOptions.cs`
- Create: `src/PictureManager.Infrastructure/Faces/FaceQuality.cs`, `OnnxFaceAnalyzer.cs`
- Modify: `src/PictureManager.Infrastructure/Imaging/SkiaBitmapOps.cs` (add `DecodeDownsampled`)
- Modify: `src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Modify: `src/PictureManager.Api/Program.cs` (bind options), `src/PictureManager.Api/appsettings.json`
- Create: `tools/download-face-models.ps1`; Modify: `.gitignore`
- Test: `tests/PictureManager.Infrastructure.Tests/Faces/FaceQualityTests.cs`, `OnnxFaceAnalyzerTests.cs`

**Interfaces:**
- Produces (Application):

```csharp
public sealed record FaceModelDescriptor(string Name, string Version, int EmbeddingDimensions, string ModelHash);
public sealed record DetectedFace(float X, float Y, float Width, float Height, float DetectionConfidence, float QualityScore, float[] Embedding);
public sealed record FaceAnalysisResult(IReadOnlyList<DetectedFace> Faces);
public interface IFaceAnalyzer
{
    FaceModelDescriptor Model { get; }                     // throws FaceModelUnavailableException
    Task<FaceAnalysisResult?> AnalyzeAsync(string imagePath, int? orientation, CancellationToken cancellationToken = default); // null = undecodable
}
public sealed class FaceRecognitionOptions { ModelDirectory, DetectionThreshold, MinFaceSizePx, ReadConcurrency, InferenceConcurrency, MaxAttempts, ClusterDistance, AutoMatchDistance, MinFacesPerGroup, MinQualityForClustering }
```

- Produces (Infrastructure): `FaceQuality.Compute(float confidence, float faceSidePx, double laplacianVariance)` → `float` and `FaceQuality.LaplacianVariance(SKBitmap rgba)` → `double`. `OnnxFaceAnalyzer` is registered as the singleton `IFaceAnalyzer`.

- [ ] **Step 1: Write the failing quality and analyzer tests**

`FaceQualityTests.cs`:

```csharp
using FluentAssertions;
using PictureManager.Infrastructure.Faces;
using SkiaSharp;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Faces;

public class FaceQualityTests
{
    [Fact]
    public void Compute_IsClampedToZeroToOne_AndRisesWithSizeAndSharpness()
    {
        var small = FaceQuality.Compute(0.9f, 30, 300);
        var large = FaceQuality.Compute(0.9f, 200, 300);
        var blurry = FaceQuality.Compute(0.9f, 200, 5);

        large.Should().BeInRange(0f, 1f);
        large.Should().BeGreaterThan(small);
        large.Should().BeGreaterThan(blurry);
        FaceQuality.Compute(1f, 10_000, 1e9).Should().Be(1f);
    }

    [Fact]
    public void LaplacianVariance_FlatImageIsZero_CheckerboardIsHigh()
    {
        using var flat = new SKBitmap(new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul));
        flat.Erase(SKColors.Gray);
        using var checker = new SKBitmap(new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
                checker.SetPixel(x, y, (x + y) % 2 == 0 ? SKColors.White : SKColors.Black);

        FaceQuality.LaplacianVariance(flat).Should().Be(0);
        FaceQuality.LaplacianVariance(checker).Should().BeGreaterThan(1000);
    }
}
```

`OnnxFaceAnalyzerTests.cs`. These are opt-in smoke tests: they return early unless the models and your own fixture photos exist. No face photos are committed, for privacy.

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Application.Faces;
using PictureManager.Infrastructure.Faces;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Faces;

/// <summary>
/// Smoke tests against the real models. Opt-in: set PICTUREMANAGER_FACE_MODELS (folder with det_10g.onnx and
/// w600k_r50.onnx) and PICTUREMANAGER_FACE_FIXTURES (folder with person-a-1.jpg and person-a-2.jpg = one person,
/// person-b.jpg = someone else, and no-face.jpg). Without them the tests pass without asserting.
/// </summary>
public class OnnxFaceAnalyzerTests
{
    private static readonly string? Models = Environment.GetEnvironmentVariable("PICTUREMANAGER_FACE_MODELS");
    private static readonly string? Fixtures = Environment.GetEnvironmentVariable("PICTUREMANAGER_FACE_FIXTURES");

    [Fact]
    public void Model_MissingFiles_ThrowsFaceModelUnavailable()
    {
        var analyzer = new OnnxFaceAnalyzer(new FaceRecognitionOptions { ModelDirectory = Path.Combine(Path.GetTempPath(), "no-models-here") });

        var act = () => analyzer.Model;

        act.Should().Throw<FaceModelUnavailableException>().WithMessage("*det_10g.onnx*");
    }

    [Fact]
    public async Task Analyze_SamePersonIsCloserThanDifferentPerson()
    {
        if (Models is null || Fixtures is null) return;
        using var analyzer = new OnnxFaceAnalyzer(new FaceRecognitionOptions { ModelDirectory = Models });

        var a1 = (await analyzer.AnalyzeAsync(Path.Combine(Fixtures, "person-a-1.jpg"), 1))!.Faces.OrderByDescending(f => f.Width).First();
        var a2 = (await analyzer.AnalyzeAsync(Path.Combine(Fixtures, "person-a-2.jpg"), 1))!.Faces.OrderByDescending(f => f.Width).First();
        var b = (await analyzer.AnalyzeAsync(Path.Combine(Fixtures, "person-b.jpg"), 1))!.Faces.OrderByDescending(f => f.Width).First();

        static double Distance(float[] x, float[] y) => 1 - x.Zip(y, (p, q) => (double)p * q).Sum();
        Distance(a1.Embedding, a2.Embedding).Should().BeLessThan(0.5);
        Distance(a1.Embedding, b.Embedding).Should().BeGreaterThan(Distance(a1.Embedding, a2.Embedding));
        a1.Embedding.Should().HaveCount(512);
        analyzer.Model.EmbeddingDimensions.Should().Be(512);
    }

    [Fact]
    public async Task Analyze_NoFace_ReturnsEmpty_AndUndecodableReturnsNull()
    {
        if (Models is null || Fixtures is null) return;
        using var analyzer = new OnnxFaceAnalyzer(new FaceRecognitionOptions { ModelDirectory = Models });
        var garbage = Path.GetTempFileName();
        await File.WriteAllTextAsync(garbage, "not an image");

        (await analyzer.AnalyzeAsync(Path.Combine(Fixtures, "no-face.jpg"), 1))!.Faces.Should().BeEmpty();
        (await analyzer.AnalyzeAsync(garbage, 1)).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~FaceQualityTests|FullyQualifiedName~OnnxFaceAnalyzerTests"`
Expected: build FAILS (types not found).

- [ ] **Step 3: Add the package and the Application contract**

```bash
dotnet add src/PictureManager.Infrastructure package Microsoft.ML.OnnxRuntime
```

`src/PictureManager.Application/Faces/IFaceAnalyzer.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Faces;

public sealed record FaceModelDescriptor(string Name, string Version, int EmbeddingDimensions, string ModelHash);

/// <summary>Box normalized 0-1 against the orientation-corrected image. Embedding is L2-normalized.</summary>
public sealed record DetectedFace(float X, float Y, float Width, float Height, float DetectionConfidence, float QualityScore, float[] Embedding);

public sealed record FaceAnalysisResult(IReadOnlyList<DetectedFace> Faces);

/// <summary>Face detection + embedding. Nothing model-specific leaks past this interface.</summary>
public interface IFaceAnalyzer
{
    /// <summary>Loads the models on first use. Throws FaceModelUnavailableException when they're missing.</summary>
    FaceModelDescriptor Model { get; }

    /// <summary>The faces in the image; an empty list if none; null if the file can't be decoded as an image.</summary>
    Task<FaceAnalysisResult?> AnalyzeAsync(string imagePath, int? orientation, CancellationToken cancellationToken = default);
}
```

`FaceModelUnavailableException.cs`:

```csharp
using System;

namespace PictureManager.Application.Faces;

public sealed class FaceModelUnavailableException(string message) : Exception(message);
```

`FaceRecognitionOptions.cs`:

```csharp
using System;

namespace PictureManager.Application.Faces;

/// <summary>Bound from the "FaceRecognition" configuration section.</summary>
public sealed class FaceRecognitionOptions
{
    /// <summary>Folder holding det_10g.onnx and w600k_r50.onnx. Relative paths resolve against the content root.</summary>
    public string ModelDirectory { get; set; } = "models/buffalo_l";
    public float DetectionThreshold { get; set; } = 0.6f;

    /// <summary>Shorter box side, in pixels of the decoded (≤1600px) image, below which a face is ignored.</summary>
    public int MinFaceSizePx { get; set; } = 40;

    /// <summary>Images decoded (NAS reads) concurrently on top of InferenceConcurrency.</summary>
    public int ReadConcurrency { get; set; } = 2;

    /// <summary>Concurrent model executions (CPU-bound).</summary>
    public int InferenceConcurrency { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Cosine distance for grouping unassigned faces (DBSCAN eps).</summary>
    public float ClusterDistance { get; set; } = 0.5f;

    /// <summary>Stricter cosine distance for attaching a new face to an existing person.</summary>
    public float AutoMatchDistance { get; set; } = 0.4f;
    public int MinFacesPerGroup { get; set; } = 3;
    public float MinQualityForClustering { get; set; } = 0.5f;
}
```

- [ ] **Step 4: Implement the quality score, downsampled decode and analyzer**

`src/PictureManager.Infrastructure/Faces/FaceQuality.cs`:

```csharp
using System;
using SkiaSharp;

namespace PictureManager.Infrastructure.Faces;

/// <summary>0-1 score: detector confidence × size factor × sharpness factor. Picks covers, gates clustering.</summary>
public static class FaceQuality
{
    private const float FullSizePx = 112f;      // ArcFace's input side: at or above it, size no longer limits quality
    private const double FullSharpness = 150.0; // Laplacian variance of a reasonably sharp aligned face

    public static float Compute(float confidence, float faceSidePx, double laplacianVariance)
    {
        var size = Math.Min(1f, faceSidePx / FullSizePx);
        var sharpness = (float)Math.Min(1.0, laplacianVariance / FullSharpness);
        return Math.Clamp(confidence * size * sharpness, 0f, 1f);
    }

    /// <summary>Variance of the 4-neighbour Laplacian of the luma channel: low = blurry.</summary>
    public static double LaplacianVariance(SKBitmap rgba)
    {
        int w = rgba.Width, h = rgba.Height;
        var pixels = rgba.GetPixelSpan();
        var row = rgba.RowBytes;
        double Luma(int x, int y)
        {
            var i = y * row + x * 4;
            return 0.299 * pixels[i] + 0.587 * pixels[i + 1] + 0.114 * pixels[i + 2];
        }

        double sum = 0, sumSquares = 0;
        var count = 0;
        for (var y = 1; y < h - 1; y++)
            for (var x = 1; x < w - 1; x++)
            {
                var laplacian = Luma(x - 1, y) + Luma(x + 1, y) + Luma(x, y - 1) + Luma(x, y + 1) - 4 * Luma(x, y);
                sum += laplacian; sumSquares += laplacian * laplacian; count++;
            }

        if (count == 0) return 0;
        var mean = sum / count;
        return sumSquares / count - mean * mean;
    }
}
```

In `SkiaBitmapOps` add:

```csharp
    /// <summary>
    /// Decodes at reduced size when the codec supports it (JPEG decodes at 1/2, 1/4 or 1/8 scale -- much faster
    /// than a full 24MP decode). Other formats decode at full size. Always Rgba8888. Null if undecodable.
    /// </summary>
    internal static SKBitmap? DecodeDownsampled(string path, int maxSide)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
                return null;

            var info = codec.Info;
            var longest = Math.Max(info.Width, info.Height);
            if (longest > maxSide)
            {
                var scaled = codec.GetScaledDimensions((float)maxSide / longest);
                info = info.WithSize(scaled.Width, scaled.Height);
            }

            return SKBitmap.Decode(codec, info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
```

`src/PictureManager.Infrastructure/Faces/OnnxFaceAnalyzer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PictureManager.Application.Faces;
using PictureManager.Application.Thumbnails;
using PictureManager.Infrastructure.Imaging;
using SkiaSharp;

namespace PictureManager.Infrastructure.Faces;

/// <summary>
/// InsightFace buffalo_l in-process: SCRFD-10G detection, five-point alignment, ArcFace R50 embedding. Models load
/// lazily on first use, so a missing model folder only fails face jobs, never startup.
/// </summary>
public sealed class OnnxFaceAnalyzer : IFaceAnalyzer, IDisposable
{
    private const string DetectorFile = "det_10g.onnx";
    private const string RecognizerFile = "w600k_r50.onnx";
    private const int DetectorInputSize = 640;
    private const int MaxDecodeSide = 1600;
    private const float NmsThreshold = 0.4f;
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly FaceRecognitionOptions _options;
    private readonly Lazy<Sessions> _sessions;
    private readonly SemaphoreSlim _inference;

    public OnnxFaceAnalyzer(FaceRecognitionOptions options)
    {
        _options = options;
        _sessions = new Lazy<Sessions>(LoadSessions, LazyThreadSafetyMode.ExecutionAndPublication);
        _inference = new SemaphoreSlim(Math.Max(1, options.InferenceConcurrency));
    }

    public FaceModelDescriptor Model => _sessions.Value.Descriptor;

    public async Task<FaceAnalysisResult?> AnalyzeAsync(string imagePath, int? orientation, CancellationToken cancellationToken = default)
    {
        var sessions = _sessions.Value;
        cancellationToken.ThrowIfCancellationRequested();

        // Decoding is NAS I/O + CPU and runs outside the inference gate, so reads overlap model execution.
        using var decoded = SkiaBitmapOps.DecodeDownsampled(imagePath, MaxDecodeSide);
        if (decoded is null)
            return null;
        using var image = SkiaBitmapOps.ApplyOrientation(decoded, ThumbnailResizeCalculator.NormalizeOrientation(orientation));

        await _inference.WaitAsync(cancellationToken);
        try
        {
            var detections = Detect(sessions.Detector, image)
                .Where(d => Math.Min(d.X2 - d.X1, d.Y2 - d.Y1) >= _options.MinFaceSizePx)
                .ToList();
            if (detections.Count == 0)
                return new FaceAnalysisResult(Array.Empty<DetectedFace>());

            var aligned = detections.Select(d => FaceAligner.Warp(image, d.Landmarks)).ToList();
            try
            {
                var embeddings = Embed(sessions.Recognizer, aligned);
                var faces = new List<DetectedFace>(detections.Count);
                for (var i = 0; i < detections.Count; i++)
                {
                    var d = detections[i];
                    var x1 = Math.Clamp(d.X1 / image.Width, 0f, 1f);
                    var y1 = Math.Clamp(d.Y1 / image.Height, 0f, 1f);
                    var x2 = Math.Clamp(d.X2 / image.Width, 0f, 1f);
                    var y2 = Math.Clamp(d.Y2 / image.Height, 0f, 1f);
                    var quality = FaceQuality.Compute(d.Score, Math.Min(d.X2 - d.X1, d.Y2 - d.Y1), FaceQuality.LaplacianVariance(aligned[i]));
                    faces.Add(new DetectedFace(x1, y1, x2 - x1, y2 - y1, d.Score, quality, embeddings[i]));
                }
                return new FaceAnalysisResult(faces);
            }
            finally
            {
                foreach (var crop in aligned)
                    crop.Dispose();
            }
        }
        finally
        {
            _inference.Release();
        }
    }

    private List<RawDetection> Detect(InferenceSession detector, SKBitmap image)
    {
        // Letterbox to the top-left of a 640² canvas, keeping the aspect ratio (as InsightFace does).
        var scale = (float)DetectorInputSize / Math.Max(image.Width, image.Height);
        using var input = new SKBitmap(new SKImageInfo(DetectorInputSize, DetectorInputSize, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(input))
        {
            canvas.Clear(SKColors.Black);
            using var source = SKImage.FromBitmap(image);
            canvas.DrawImage(source, new SKRect(0, 0, image.Width * scale, image.Height * scale), Sampling);
        }

        var tensor = ToTensor(new[] { input }, DetectorInputSize, mean: 127.5f, std: 128f);
        using var results = detector.Run(new[] { NamedOnnxValue.CreateFromTensor(detector.InputMetadata.Keys.First(), tensor) });

        // det_10g output order: scores (strides 8, 16, 32), then boxes, then landmarks.
        var outputs = results.Select(r => r.AsEnumerable<float>().ToArray()).ToList();
        var raw = ScrfdDecoder.Decode(DetectorInputSize, outputs.GetRange(0, 3), outputs.GetRange(3, 3), outputs.GetRange(6, 3), _options.DetectionThreshold);
        return ScrfdDecoder.NonMaxSuppression(raw, NmsThreshold).Select(d => d.Scale(1 / scale)).ToList();
    }

    private static List<float[]> Embed(InferenceSession recognizer, IReadOnlyList<SKBitmap> aligned)
    {
        var tensor = ToTensor(aligned, FaceAligner.Size, mean: 127.5f, std: 127.5f);
        using var results = recognizer.Run(new[] { NamedOnnxValue.CreateFromTensor(recognizer.InputMetadata.Keys.First(), tensor) });
        var flat = results.First().AsEnumerable<float>().ToArray();
        var dimensions = flat.Length / aligned.Count;

        return Enumerable.Range(0, aligned.Count).Select(i =>
        {
            var vector = flat.AsSpan(i * dimensions, dimensions).ToArray();
            var norm = (float)Math.Sqrt(vector.Sum(v => v * v));
            for (var j = 0; j < vector.Length; j++)
                vector[j] /= norm;
            return vector;
        }).ToList();
    }

    /// <summary>NCHW float tensor, RGB, (pixel − mean) / std. Bitmaps must be Rgba8888 and size×size.</summary>
    private static DenseTensor<float> ToTensor(IReadOnlyList<SKBitmap> bitmaps, int size, float mean, float std)
    {
        var tensor = new DenseTensor<float>(new[] { bitmaps.Count, 3, size, size });
        for (var n = 0; n < bitmaps.Count; n++)
        {
            var pixels = bitmaps[n].GetPixelSpan();
            var row = bitmaps[n].RowBytes;
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var i = y * row + x * 4;
                    tensor[n, 0, y, x] = (pixels[i] - mean) / std;
                    tensor[n, 1, y, x] = (pixels[i + 1] - mean) / std;
                    tensor[n, 2, y, x] = (pixels[i + 2] - mean) / std;
                }
        }
        return tensor;
    }

    private Sessions LoadSessions()
    {
        var detectorPath = Path.Combine(_options.ModelDirectory, DetectorFile);
        var recognizerPath = Path.Combine(_options.ModelDirectory, RecognizerFile);
        if (!File.Exists(detectorPath) || !File.Exists(recognizerPath))
            throw new FaceModelUnavailableException(
                $"Face recognition models not found: expected {DetectorFile} and {RecognizerFile} in '{_options.ModelDirectory}'. " +
                "Run tools/download-face-models.ps1 (development) or rebuild the Docker image.");

        var sessionOptions = new SessionOptions
        {
            // Parallelism comes from processing several images at once, not from threads inside one run.
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        using var sha = SHA256.Create();
        foreach (var path in new[] { detectorPath, recognizerPath })
        {
            var bytes = File.ReadAllBytes(path);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var hash = Convert.ToHexStringLower(sha.Hash!);

        return new Sessions(
            new InferenceSession(detectorPath, sessionOptions),
            new InferenceSession(recognizerPath, sessionOptions),
            new FaceModelDescriptor("insightface-buffalo_l", "det_10g+w600k_r50", 512, hash));
    }

    public void Dispose()
    {
        if (_sessions.IsValueCreated)
        {
            _sessions.Value.Detector.Dispose();
            _sessions.Value.Recognizer.Dispose();
        }
        _inference.Dispose();
    }

    private sealed record Sessions(InferenceSession Detector, InferenceSession Recognizer, FaceModelDescriptor Descriptor);
}
```

(`ThumbnailResizeCalculator.NormalizeOrientation` already exists and is used the same way by `SkiaDHashPerceptualHasher`.)

- [ ] **Step 5: Register and bind**

`InfrastructureServiceCollectionExtensions`: add `using PictureManager.Application.Faces; using PictureManager.Infrastructure.Faces;` and, with the other singletons:

```csharp
        services.AddSingleton<IFaceAnalyzer, OnnxFaceAnalyzer>();
```

`Program.cs`: after the `ScanningOptions` registration add (with `using PictureManager.Application.Faces;`):

```csharp
    var faceRecognitionOptions = new FaceRecognitionOptions();
    builder.Configuration.GetSection("FaceRecognition").Bind(faceRecognitionOptions);
    faceRecognitionOptions.ModelDirectory = Path.GetFullPath(faceRecognitionOptions.ModelDirectory, builder.Environment.ContentRootPath);
    builder.Services.AddSingleton(faceRecognitionOptions);
```

`src/PictureManager.Api/appsettings.json`: add a top-level section (the default resolves to `<repo>/models/buffalo_l` from `src/PictureManager.Api` during `dotnet run`):

```json
  "FaceRecognition": {
    "ModelDirectory": "../../models/buffalo_l"
  },
```

- [ ] **Step 6: Add the dev download script and ignore the models**

`tools/download-face-models.ps1`:

```powershell
# Downloads InsightFace buffalo_l (SCRFD-10G detector + ArcFace R50 recognizer) for local development.
# The pretrained weights are for non-commercial use only (see https://github.com/deepinsight/insightface).
param([string]$Destination = (Join-Path $PSScriptRoot '..\models\buffalo_l'))

$ErrorActionPreference = 'Stop'
$url = 'https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_l.zip'
$zip = Join-Path ([IO.Path]::GetTempPath()) 'buffalo_l.zip'
$extract = Join-Path ([IO.Path]::GetTempPath()) 'buffalo_l'

Invoke-WebRequest -Uri $url -OutFile $zip
Write-Host "buffalo_l.zip SHA-256: $((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant())"
Expand-Archive -Path $zip -DestinationPath $extract -Force
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
foreach ($name in 'det_10g.onnx', 'w600k_r50.onnx') {
    $file = Get-ChildItem -Path $extract -Recurse -Filter $name | Select-Object -First 1
    if (-not $file) { throw "$name not found in buffalo_l.zip" }
    Copy-Item $file.FullName (Join-Path $Destination $name) -Force
}
Remove-Item $zip, $extract -Recurse -Force
Write-Host "Models copied to $((Resolve-Path $Destination).Path)"
```

Append to `.gitignore`:

```
# Face recognition models (downloaded by tools/download-face-models.ps1; non-commercial license)
/models/
```

Run: `pwsh tools/download-face-models.ps1` (or `powershell -File tools/download-face-models.ps1`)
Expected: prints the zip's SHA-256 (**write it down; Task 17 pins it**) and `Models copied to ...\models\buffalo_l`.

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~FaceQualityTests|FullyQualifiedName~OnnxFaceAnalyzerTests"`
Expected: PASS (the smoke tests pass trivially without the env vars).

Then, with models downloaded and four photos of your own in a scratch folder (never committed):

```bash
PICTUREMANAGER_FACE_MODELS="$PWD/models/buffalo_l" PICTUREMANAGER_FACE_FIXTURES="/path/to/your/fixtures" dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~OnnxFaceAnalyzerTests"
```

Expected: PASS with real assertions. If `Analyze_SamePersonIsCloserThanDifferentPerson` fails with a tensor-shape error, print `detector.OutputMetadata` names and dimensions: some det_10g exports carry a leading batch dimension, which `AsEnumerable` already flattens; only the output **order** matters.

- [ ] **Step 8: Commit**

```bash
git add src/PictureManager.Application/Faces src/PictureManager.Infrastructure/Faces src/PictureManager.Infrastructure/Imaging/SkiaBitmapOps.cs src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs src/PictureManager.Infrastructure/PictureManager.Infrastructure.csproj src/PictureManager.Api/Program.cs src/PictureManager.Api/appsettings.json tools/download-face-models.ps1 .gitignore tests/PictureManager.Infrastructure.Tests/Faces
git commit -m "feat: in-process ONNX face analyzer (SCRFD + ArcFace) with quality score

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Face repository (candidates, results, state, model registration)

**Files:**
- Create: `src/PictureManager.Application/Repositories/IFaceRepository.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Repositories/FaceRepository.cs`
- Modify: `src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FaceRepositoryTests.cs`

**Interfaces:**
- Consumes: the entities (Task 5) and `DetectedFace` / `FaceModelDescriptor` (Task 8).
- Produces:

```csharp
public sealed record FaceFailure(int ImageId, string FileName, string Extension, int Attempts, string? ErrorMessage, DateTime ProcessedUtc);
public interface IFaceRepository
{
    Task<int> GetOrCreateModelIdAsync(FaceModelDescriptor model, DateTime nowUtc, CancellationToken ct = default);
    Task<IReadOnlyList<int>> GetCandidateImageIdsAsync(int faceModelId, int? folderId, bool isRecursive, CancellationToken ct = default);
    Task<FaceProcessingState?> GetStateAsync(int imageId, CancellationToken ct = default);
    Task<bool> SaveResultAsync(int imageId, int faceModelId, string fingerprint, IReadOnlyList<DetectedFace> faces, DateTime nowUtc, CancellationToken ct = default);
    Task SaveFailureAsync(int imageId, int faceModelId, string fingerprint, FaceProcessingStatus status, int attempts, string? errorMessage, DateTime nowUtc, CancellationToken ct = default);
    Task<IReadOnlyList<FaceFailure>> GetPermanentFailuresAsync(int faceModelId, CancellationToken ct = default);
}
```

(Task 12 adds the clustering methods to this interface.)

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Faces;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class FaceRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static DetectedFace Face(int axis) =>
        new(0.1f, 0.2f, 0.3f, 0.3f, 0.95f, 0.8f, FaceTestData.Embedding(axis));

    [Fact]
    public async Task GetOrCreateModelIdAsync_IsIdempotentByHash()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var repository = new FaceRepository(db.Context);
        var descriptor = new FaceModelDescriptor("insightface-buffalo_l", "v", 512, "abc");

        var first = await repository.GetOrCreateModelIdAsync(descriptor, Now);
        var second = await repository.GetOrCreateModelIdAsync(descriptor, Now);

        second.Should().Be(first);
        (await db.Context.FaceModels.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetCandidateImageIdsAsync_AppliesIndexStateVisibilityScopeAndState()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var trips = await FaceTestData.AddFolderAsync(db.Context, top, "Trips");
        var madeira = await FaceTestData.AddFolderAsync(db.Context, trips, "Madeira");
        var tripsBis = await FaceTestData.AddFolderAsync(db.Context, top, "Trips2"); // prefix trap: "Trips" must not match "Trips2"
        var model = await FaceTestData.AddModelAsync(db.Context);

        var inTrips = await FaceTestData.AddImageAsync(db.Context, trips, "a");
        var inMadeira = await FaceTestData.AddImageAsync(db.Context, madeira, "b");
        var inTripsBis = await FaceTestData.AddImageAsync(db.Context, tripsBis, "c");
        await FaceTestData.AddImageAsync(db.Context, trips, "pending", IndexState.Pending);
        var missing = await FaceTestData.AddImageAsync(db.Context, trips, "missing");
        missing.MissingSinceUtc = Now;
        var done = await FaceTestData.AddImageAsync(db.Context, trips, "done", hash: "h1");
        var changed = await FaceTestData.AddImageAsync(db.Context, trips, "changed", hash: "new");
        var failed = await FaceTestData.AddImageAsync(db.Context, trips, "failed", hash: "h2");
        var givenUp = await FaceTestData.AddImageAsync(db.Context, trips, "gaveup", hash: "h3");
        db.Context.FaceProcessingStates.AddRange(
            new FaceProcessingState { ImageId = done.Id, FaceModelId = model, ImageFingerprint = "h1", Status = FaceProcessingStatus.Completed, ProcessedUtc = Now },
            new FaceProcessingState { ImageId = changed.Id, FaceModelId = model, ImageFingerprint = "old", Status = FaceProcessingStatus.Completed, ProcessedUtc = Now },
            new FaceProcessingState { ImageId = failed.Id, FaceModelId = model, ImageFingerprint = "h2", Status = FaceProcessingStatus.Failed, Attempts = 1, ProcessedUtc = Now },
            new FaceProcessingState { ImageId = givenUp.Id, FaceModelId = model, ImageFingerprint = "h3", Status = FaceProcessingStatus.PermanentlyFailed, Attempts = 3, ProcessedUtc = Now });
        await db.Context.SaveChangesAsync();
        var repository = new FaceRepository(db.CreateContext());

        (await repository.GetCandidateImageIdsAsync(model, trips.Id, isRecursive: true))
            .Should().BeEquivalentTo(new[] { inTrips.Id, inMadeira.Id, changed.Id, failed.Id });
        (await repository.GetCandidateImageIdsAsync(model, trips.Id, isRecursive: false))
            .Should().BeEquivalentTo(new[] { inTrips.Id, changed.Id, failed.Id });
        (await repository.GetCandidateImageIdsAsync(model, null, isRecursive: true))
            .Should().BeEquivalentTo(new[] { inTrips.Id, inMadeira.Id, inTripsBis.Id, changed.Id, failed.Id });
        (await repository.GetCandidateImageIdsAsync(model, top.Id, isRecursive: true))
            .Should().HaveCount(5);
        (await repository.GetCandidateImageIdsAsync(model, folderId: 999_999, isRecursive: true)).Should().BeEmpty();
    }

    [Fact]
    public async Task SaveResultAsync_ReplacesFacesAndRecordsCompletedState()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a", hash: "h");
        var model = await FaceTestData.AddModelAsync(db.Context);
        await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(9));
        var repository = new FaceRepository(db.CreateContext());

        (await repository.SaveResultAsync(image.Id, model, "h", new[] { Face(0), Face(1) }, Now)).Should().BeTrue();

        await using var read = db.CreateContext();
        var faces = await read.Faces.Where(f => f.ImageId == image.Id).ToListAsync();
        faces.Should().HaveCount(2).And.OnlyContain(f => f.AssignmentState == FaceAssignmentState.Unassigned && f.FaceModelId == model);
        var state = await read.FaceProcessingStates.SingleAsync(s => s.ImageId == image.Id);
        state.Status.Should().Be(FaceProcessingStatus.Completed);
        state.ImageFingerprint.Should().Be("h");
        state.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task SaveResultAsync_NoFaces_RecordsCompletedState()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a", hash: "h");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var repository = new FaceRepository(db.CreateContext());

        (await repository.SaveResultAsync(image.Id, model, "h", Array.Empty<DetectedFace>(), Now)).Should().BeTrue();

        (await new FaceRepository(db.CreateContext()).GetCandidateImageIdsAsync(model, null, true)).Should().BeEmpty();
    }

    [Fact]
    public async Task SaveResultAsync_ImageDeleted_ReturnsFalse()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context);
        await db.Context.Images.Where(i => i.Id == image.Id).ExecuteDeleteAsync();

        var saved = await new FaceRepository(db.CreateContext()).SaveResultAsync(image.Id, model, "h", new[] { Face(0) }, Now);

        saved.Should().BeFalse();
    }

    [Fact]
    public async Task SaveFailureAsync_UpsertsState_AndPermanentFailuresAreListed()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "broken", hash: "h");
        var model = await FaceTestData.AddModelAsync(db.Context);

        await new FaceRepository(db.CreateContext()).SaveFailureAsync(image.Id, model, "h", FaceProcessingStatus.Failed, 1, "io", Now);
        await new FaceRepository(db.CreateContext()).SaveFailureAsync(image.Id, model, "h", FaceProcessingStatus.PermanentlyFailed, 2, "undecodable", Now);

        var failures = await new FaceRepository(db.CreateContext()).GetPermanentFailuresAsync(model);
        failures.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ImageId = image.Id, FileName = "broken", Attempts = 2, ErrorMessage = "undecodable" });
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~FaceRepositoryTests"`
Expected: build FAILS (`FaceRepository` not found).

- [ ] **Step 3: Implement**

`src/PictureManager.Application/Repositories/IFaceRepository.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Faces;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public sealed record FaceFailure(int ImageId, string FileName, string Extension, int Attempts, string? ErrorMessage, DateTime ProcessedUtc);

public interface IFaceRepository
{
    /// <summary>The FaceModel row for this descriptor (matched by ModelHash), created on first use.</summary>
    Task<int> GetOrCreateModelIdAsync(FaceModelDescriptor model, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ids (ascending) of visible Indexed images in scope that still need analysis by this model: no state, a
    /// state for another model or an older ContentHash, or a retryable Failed state. folderId null = everything;
    /// an unknown folderId = nothing.
    /// </summary>
    Task<IReadOnlyList<int>> GetCandidateImageIdsAsync(int faceModelId, int? folderId, bool isRecursive, CancellationToken cancellationToken = default);

    Task<FaceProcessingState?> GetStateAsync(int imageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// In one transaction: replaces all of the image's faces with these (Unassigned) and records Completed state.
    /// False when the image no longer exists (deleted while the job ran).
    /// </summary>
    Task<bool> SaveResultAsync(int imageId, int faceModelId, string fingerprint, IReadOnlyList<DetectedFace> faces, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task SaveFailureAsync(int imageId, int faceModelId, string fingerprint, FaceProcessingStatus status, int attempts, string? errorMessage, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FaceFailure>> GetPermanentFailuresAsync(int faceModelId, CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Infrastructure/Persistence/Repositories/FaceRepository.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Infrastructure.Persistence.Queries;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class FaceRepository : IFaceRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public FaceRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> GetOrCreateModelIdAsync(FaceModelDescriptor model, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.FaceModels.AsNoTracking()
            .Where(m => m.ModelHash == model.ModelHash).Select(m => (int?)m.Id).FirstOrDefaultAsync(cancellationToken);
        if (existing is int id)
            return id;

        var row = new FaceModel
        {
            Name = model.Name, Version = model.Version, EmbeddingDimensions = model.EmbeddingDimensions,
            ModelHash = model.ModelHash, CreatedUtc = nowUtc
        };
        _dbContext.FaceModels.Add(row);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<IReadOnlyList<int>> GetCandidateImageIdsAsync(int faceModelId, int? folderId, bool isRecursive, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Images.AsNoTracking().WhereVisible().Where(i => i.IndexState == IndexState.Indexed);

        if (folderId is int id)
        {
            var folder = await _dbContext.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
            if (folder is null)
                return Array.Empty<int>();

            if (!isRecursive)
                query = query.Where(i => i.FolderId == id);
            else if (folder.RelativePath.Length == 0)
                query = query.Where(i => i.Folder!.RootId == folder.RootId);
            else
            {
                var prefix = folder.RelativePath + "/";
                query = query.Where(i => i.Folder!.RootId == folder.RootId
                                         && (i.FolderId == id || i.Folder.RelativePath.StartsWith(prefix)));
            }
        }

        // Done = a Completed or PermanentlyFailed state for this model AND this exact content.
        query = query.Where(i => !_dbContext.FaceProcessingStates.Any(s =>
            s.ImageId == i.Id && s.FaceModelId == faceModelId && s.ImageFingerprint == i.ContentHash
            && s.Status != FaceProcessingStatus.Failed));

        return await query.OrderBy(i => i.Id).Select(i => i.Id).ToListAsync(cancellationToken);
    }

    public Task<FaceProcessingState?> GetStateAsync(int imageId, CancellationToken cancellationToken = default) =>
        _dbContext.FaceProcessingStates.AsNoTracking().FirstOrDefaultAsync(s => s.ImageId == imageId, cancellationToken);

    public async Task<bool> SaveResultAsync(
        int imageId, int faceModelId, string fingerprint, IReadOnlyList<DetectedFace> faces, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await _dbContext.Images.AnyAsync(i => i.Id == imageId, cancellationToken))
            return false;

        await _dbContext.Faces.Where(f => f.ImageId == imageId).ExecuteDeleteAsync(cancellationToken);
        _dbContext.Faces.AddRange(faces.Select(f => new Face
        {
            ImageId = imageId,
            FaceModelId = faceModelId,
            X = f.X, Y = f.Y, Width = f.Width, Height = f.Height,
            DetectionConfidence = f.DetectionConfidence,
            QualityScore = f.QualityScore,
            Embedding = new Vector(f.Embedding),
            CreatedUtc = nowUtc
        }));
        await UpsertStateAsync(imageId, faceModelId, fingerprint, FaceProcessingStatus.Completed, 0, null, nowUtc, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // Deleted between the existence check and the insert.
            _dbContext.ChangeTracker.Clear();
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
        return true;
    }

    public async Task SaveFailureAsync(
        int imageId, int faceModelId, string fingerprint, FaceProcessingStatus status, int attempts, string? errorMessage, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await UpsertStateAsync(imageId, faceModelId, fingerprint, status, attempts, errorMessage, nowUtc, cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // Image deleted meanwhile: nothing to record.
        }
        _dbContext.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<FaceFailure>> GetPermanentFailuresAsync(int faceModelId, CancellationToken cancellationToken = default) =>
        await _dbContext.FaceProcessingStates.AsNoTracking()
            .Where(s => s.FaceModelId == faceModelId && s.Status == FaceProcessingStatus.PermanentlyFailed)
            .OrderByDescending(s => s.ProcessedUtc)
            .Select(s => new FaceFailure(s.ImageId, s.Image!.FileName, s.Image.Extension, s.Attempts, s.ErrorMessage, s.ProcessedUtc))
            .ToListAsync(cancellationToken);

    private async Task UpsertStateAsync(
        int imageId, int faceModelId, string fingerprint, FaceProcessingStatus status, int attempts, string? errorMessage, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var state = await _dbContext.FaceProcessingStates.FirstOrDefaultAsync(s => s.ImageId == imageId, cancellationToken);
        if (state is null)
        {
            state = new FaceProcessingState { ImageId = imageId };
            _dbContext.FaceProcessingStates.Add(state);
        }

        state.FaceModelId = faceModelId;
        state.ImageFingerprint = fingerprint;
        state.Status = status;
        state.Attempts = attempts;
        state.ErrorMessage = errorMessage;
        state.ProcessedUtc = nowUtc;
    }
}
```

Register in `InfrastructureServiceCollectionExtensions`: `services.AddScoped<IFaceRepository, FaceRepository>();`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~FaceRepositoryTests"`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add src/PictureManager.Application/Repositories/IFaceRepository.cs src/PictureManager.Infrastructure/Persistence/Repositories/FaceRepository.cs src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FaceRepositoryTests.cs
git commit -m "feat: face repository with resumable candidate selection

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Per-image processor and the face recognition service

**Files:**
- Create: `src/PictureManager.Application/Faces/IFaceImageProcessor.cs`, `FaceImageProcessor.cs`
- Create: `src/PictureManager.Application/Faces/QueuedFaceRecognition.cs`, `IFaceRecognitionQueue.cs`, `FaceRecognitionAlreadyInProgressException.cs`, `IFaceRecognitionService.cs`, `FaceRecognitionService.cs`
- Modify: `src/PictureManager.Application/Repositories/IJobRepository.cs`, `src/PictureManager.Infrastructure/Persistence/Repositories/JobRepository.cs` (add `IncrementFaceProgressAsync`)
- Modify: `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Application.Tests/Faces/FaceImageProcessorTests.cs`, `FaceRecognitionServiceTests.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/JobRepositoryCompletionTests.cs` (add a test)

**Interfaces:**
- Consumes: `IFaceAnalyzer`, `FaceRecognitionOptions` (Task 8); `IFaceRepository` (Task 9); `IJobCancellationRegistry` (Task 1); `ScanTargets.ResolveJobFolderIdAsync` (Task 3); `IJobRepository.TryMarkCompletedAsync` (Task 2).
- Produces:

```csharp
public enum FaceImageOutcome { Processed, Skipped, Failed }
public sealed record FaceImageResult(FaceImageOutcome Outcome, int FacesFound);
public interface IFaceImageProcessor { Task<FaceImageResult> ProcessAsync(int imageId, int faceModelId, CancellationToken ct = default); }
public sealed record QueuedFaceRecognition(int JobId, int? FolderId, bool IsRecursive);
public interface IFaceRecognitionQueue { void Enqueue(QueuedFaceRecognition item); IAsyncEnumerable<QueuedFaceRecognition> ReadAllAsync(CancellationToken ct = default); }
public interface IFaceRecognitionService
{
    Task<int> QueueAsync(int? rootId, int? folderId, bool isRecursive, CancellationToken ct = default);
    Task RunAsync(QueuedFaceRecognition job, CancellationToken ct = default);
    Task<IReadOnlyList<FaceFailure>> GetPermanentFailuresAsync(CancellationToken ct = default);
}
// IJobRepository:
Task IncrementFaceProgressAsync(int jobId, int facesFound, CancellationToken ct = default); // FilesEnriched += 1, FacesFound += facesFound
```

- [ ] **Step 1: Write the failing processor tests**

`tests/PictureManager.Application.Tests/Faces/FaceImageProcessorTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PictureManager.Application.Common;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Faces;

public sealed class FaceImageProcessorTests : IDisposable
{
    private const int ModelId = 3;
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("pm-faces-");
    private readonly IImageRepository _images = Substitute.For<IImageRepository>();
    private readonly IFaceRepository _faces = Substitute.For<IFaceRepository>();
    private readonly IFaceAnalyzer _analyzer = Substitute.For<IFaceAnalyzer>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly Image _image;

    public FaceImageProcessorTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        _image = new Image
        {
            Id = 7, FileName = "IMG1", Extension = ".jpg", ContentHash = "hash", Orientation = 1,
            Folder = new Folder { RelativePath = string.Empty, Root = new ImageRoot { MountPath = _root.FullName, IsActive = true } }
        };
        _images.GetByIdWithFolderAsync(7, Arg.Any<CancellationToken>()).Returns(_image);
        _faces.SaveResultAsync(default, default, default!, default!, default, default).ReturnsForAnyArgs(true);
    }

    public void Dispose() => _root.Delete(recursive: true);

    private string Physical => Path.Combine(_root.FullName, "IMG1.jpg");

    private FaceImageProcessor Create() =>
        new(_images, _faces, _analyzer, new FaceRecognitionOptions { MaxAttempts = 3 }, _clock, NullLogger<FaceImageProcessor>.Instance);

    private static DetectedFace AFace() => new(0, 0, 0.5f, 0.5f, 0.9f, 0.8f, new float[512]);

    [Fact]
    public async Task ProcessAsync_FacesFound_SavesThemAndReportsCount()
    {
        File.WriteAllText(Physical, "x");
        _analyzer.AnalyzeAsync(Physical, 1, Arg.Any<CancellationToken>()).Returns(new FaceAnalysisResult(new[] { AFace(), AFace() }));

        var result = await Create().ProcessAsync(7, ModelId);

        result.Should().Be(new FaceImageResult(FaceImageOutcome.Processed, 2));
        await _faces.Received(1).SaveResultAsync(7, ModelId, "hash", Arg.Is<IReadOnlyList<DetectedFace>>(f => f.Count == 2), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_NoFaces_SavesEmptyResult()
    {
        File.WriteAllText(Physical, "x");
        _analyzer.AnalyzeAsync(Physical, 1, Arg.Any<CancellationToken>()).Returns(new FaceAnalysisResult(Array.Empty<DetectedFace>()));

        var result = await Create().ProcessAsync(7, ModelId);

        result.Should().Be(new FaceImageResult(FaceImageOutcome.Processed, 0));
        await _faces.Received(1).SaveResultAsync(7, ModelId, "hash", Arg.Is<IReadOnlyList<DetectedFace>>(f => f.Count == 0), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_FileMissing_SkipsWithoutWritingState()
    {
        var result = await Create().ProcessAsync(7, ModelId);

        result.Outcome.Should().Be(FaceImageOutcome.Skipped);
        await _analyzer.DidNotReceiveWithAnyArgs().AnalyzeAsync(default!, default, default);
        await _faces.DidNotReceiveWithAnyArgs().SaveFailureAsync(default, default, default!, default, default, default, default, default);
        await _faces.DidNotReceiveWithAnyArgs().SaveResultAsync(default, default, default!, default!, default, default);
    }

    [Fact]
    public async Task ProcessAsync_IoError_RecordsRetryableFailure()
    {
        File.WriteAllText(Physical, "x");
        _analyzer.AnalyzeAsync(Physical, 1, Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("network name no longer available"));

        var result = await Create().ProcessAsync(7, ModelId);

        result.Outcome.Should().Be(FaceImageOutcome.Failed);
        await _faces.Received(1).SaveFailureAsync(7, ModelId, "hash", FaceProcessingStatus.Failed, 1, "network name no longer available", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_ThirdFailureForSameContent_BecomesPermanent()
    {
        File.WriteAllText(Physical, "x");
        _faces.GetStateAsync(7, Arg.Any<CancellationToken>()).Returns(new FaceProcessingState
        {
            ImageId = 7, FaceModelId = ModelId, ImageFingerprint = "hash", Status = FaceProcessingStatus.Failed, Attempts = 2
        });
        _analyzer.AnalyzeAsync(Physical, 1, Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("again"));

        await Create().ProcessAsync(7, ModelId);

        await _faces.Received(1).SaveFailureAsync(7, ModelId, "hash", FaceProcessingStatus.PermanentlyFailed, 3, "again", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_PreviousFailureForOtherContent_RestartsTheCount()
    {
        File.WriteAllText(Physical, "x");
        _faces.GetStateAsync(7, Arg.Any<CancellationToken>()).Returns(new FaceProcessingState
        {
            ImageId = 7, FaceModelId = ModelId, ImageFingerprint = "older", Status = FaceProcessingStatus.Failed, Attempts = 2
        });
        _analyzer.AnalyzeAsync(Physical, 1, Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("x"));

        await Create().ProcessAsync(7, ModelId);

        await _faces.Received(1).SaveFailureAsync(7, ModelId, "hash", FaceProcessingStatus.Failed, 1, "x", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_Undecodable_IsPermanentImmediately()
    {
        File.WriteAllText(Physical, "x");
        _analyzer.AnalyzeAsync(Physical, 1, Arg.Any<CancellationToken>()).Returns((FaceAnalysisResult?)null);

        await Create().ProcessAsync(7, ModelId);

        await _faces.Received(1).SaveFailureAsync(7, ModelId, "hash", FaceProcessingStatus.PermanentlyFailed, 1, "The image could not be decoded.", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_ImageDeletedBeforeSave_IsSkipped()
    {
        File.WriteAllText(Physical, "x");
        _analyzer.AnalyzeAsync(Physical, 1, Arg.Any<CancellationToken>()).Returns(new FaceAnalysisResult(new[] { AFace() }));
        _faces.SaveResultAsync(default, default, default!, default!, default, default).ReturnsForAnyArgs(false);

        (await Create().ProcessAsync(7, ModelId)).Should().Be(new FaceImageResult(FaceImageOutcome.Skipped, 0));
    }

    [Fact]
    public async Task ProcessAsync_ModelUnavailable_Propagates()
    {
        File.WriteAllText(Physical, "x");
        _analyzer.AnalyzeAsync(Physical, 1, Arg.Any<CancellationToken>()).ThrowsAsync(new FaceModelUnavailableException("missing"));

        var act = () => Create().ProcessAsync(7, ModelId);

        await act.Should().ThrowAsync<FaceModelUnavailableException>();
    }
}
```

- [ ] **Step 2: Write the failing service tests**

`tests/PictureManager.Application.Tests/Faces/FaceRecognitionServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PictureManager.Application.Common;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Faces;

public class FaceRecognitionServiceTests
{
    private static readonly FaceModelDescriptor Descriptor = new("m", "1", 512, "hash");
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();
    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly IFaceRepository _faces = Substitute.For<IFaceRepository>();
    private readonly IFaceAnalyzer _analyzer = Substitute.For<IFaceAnalyzer>();
    private readonly IFaceRecognitionQueue _queue = Substitute.For<IFaceRecognitionQueue>();
    private readonly IFaceImageProcessor _processor = Substitute.For<IFaceImageProcessor>();
    private readonly JobCancellationRegistry _cancellations = new();
    private readonly IClock _clock = Substitute.For<IClock>();

    public FaceRecognitionServiceTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        _analyzer.Model.Returns(Descriptor);
        _faces.GetOrCreateModelIdAsync(Descriptor, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(3);
        _jobs.AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var job = call.Arg<Job>();
            job.Id = 50;
            return job;
        });
        _processor.ProcessAsync(Arg.Any<int>(), 3, Arg.Any<CancellationToken>()).Returns(new FaceImageResult(FaceImageOutcome.Processed, 1));
    }

    private FaceRecognitionService Create()
    {
        var provider = new ServiceCollection()
            .AddScoped(_ => _processor)
            .AddScoped(_ => _jobs)
            .BuildServiceProvider();
        return new FaceRecognitionService(
            _folders, _roots, _jobs, _faces, _analyzer, _queue, _cancellations,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FaceRecognitionOptions { ReadConcurrency = 1, InferenceConcurrency = 1 }, _clock);
    }

    [Fact]
    public async Task QueueAsync_CreatesEnumeratingFaceJob_RegistersCancellation_AndEnqueues()
    {
        var jobId = await Create().QueueAsync(rootId: null, folderId: null, isRecursive: true);

        jobId.Should().Be(50);
        await _jobs.Received(1).AddAsync(
            Arg.Is<Job>(j => j.Kind == JobKind.FaceRecognition && j.Status == JobStatus.Enumerating && j.FolderId == null),
            Arg.Any<CancellationToken>());
        _queue.Received(1).Enqueue(new QueuedFaceRecognition(50, null, true));
        _cancellations.Cancel(50).Should().BeTrue();
    }

    [Fact]
    public async Task QueueAsync_AnotherJobActive_Throws()
    {
        _jobs.HasActiveJobAsync(Arg.Any<CancellationToken>()).Returns(true);

        var act = () => Create().QueueAsync(null, null, true);

        await act.Should().ThrowAsync<FaceRecognitionAlreadyInProgressException>();
        _queue.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Fact]
    public async Task RunAsync_ProcessesEveryCandidate_ReportsProgress_AndCompletes()
    {
        _faces.GetCandidateImageIdsAsync(3, 20, true, Arg.Any<CancellationToken>()).Returns(new List<int> { 1, 2, 3 });

        await Create().RunAsync(new QueuedFaceRecognition(50, 20, true));

        await _jobs.Received(1).SetEnumerationResultAsync(50, 0, 3, Arg.Any<CancellationToken>());
        await _jobs.Received(1).TryTransitionToEnrichingAsync(50, Arg.Any<CancellationToken>());
        await _processor.Received(3).ProcessAsync(Arg.Any<int>(), 3, Arg.Any<CancellationToken>());
        await _jobs.Received(3).IncrementFaceProgressAsync(50, 1, Arg.Any<CancellationToken>());
        await _jobs.Received(1).TryMarkCompletedAsync(50, JobStatus.Enriching, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_CancelledMidway_RecordsCancelledWithCandidateCount_AndRethrows()
    {
        _faces.GetCandidateImageIdsAsync(3, null, true, Arg.Any<CancellationToken>()).Returns(new List<int> { 1, 2, 3, 4 });
        using var cts = new CancellationTokenSource();
        _processor.ProcessAsync(1, 3, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cts.Cancel();
            return new FaceImageResult(FaceImageOutcome.Processed, 0);
        });

        var act = () => Create().RunAsync(new QueuedFaceRecognition(50, null, true), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await _jobs.Received(1).SetFailureResultAsync(50, 0, 4, null, JobStatus.Cancelled, Arg.Any<DateTime>(), CancellationToken.None);
        await _jobs.DidNotReceiveWithAnyArgs().TryMarkCompletedAsync(default, default, default, default);
    }

    [Fact]
    public async Task RunAsync_AlreadyCancelled_RecordsCancelledWithoutProcessing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Create().RunAsync(new QueuedFaceRecognition(50, null, true), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await _processor.DidNotReceiveWithAnyArgs().ProcessAsync(default, default, default);
        await _jobs.Received(1).SetFailureResultAsync(50, 0, 0, null, JobStatus.Cancelled, Arg.Any<DateTime>(), CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_ModelsMissing_RecordsFailedWithTheMessage()
    {
        _analyzer.Model.Throws(new FaceModelUnavailableException("Face recognition models not found"));

        var act = () => Create().RunAsync(new QueuedFaceRecognition(50, null, true));

        await act.Should().ThrowAsync<FaceModelUnavailableException>();
        await _jobs.Received(1).SetFailureResultAsync(50, 0, 0, "Face recognition models not found", JobStatus.Failed, Arg.Any<DateTime>(), CancellationToken.None);
    }
}
```

- [ ] **Step 3: Write the failing repository test for the progress counter**

Append to `JobRepositoryCompletionTests`:

```csharp
    [Fact]
    public async Task IncrementFaceProgressAsync_AddsOneImageAndTheFaces()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var job = new Job { Kind = JobKind.FaceRecognition, Status = JobStatus.Enriching, StartedUtc = DateTime.UtcNow };
        db.Context.Jobs.Add(job);
        await db.Context.SaveChangesAsync();
        var repository = new JobRepository(db.Context);

        await repository.IncrementFaceProgressAsync(job.Id, 3);
        await repository.IncrementFaceProgressAsync(job.Id, 0);

        var reloaded = await repository.GetByIdAsync(job.Id);
        reloaded!.FilesEnriched.Should().Be(2);
        reloaded.FacesFound.Should().Be(3);
    }
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~Faces"`
Expected: build FAILS (types not found).

- [ ] **Step 5: Implement the contracts**

`IFaceImageProcessor.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Faces;

public enum FaceImageOutcome
{
    Processed,
    Skipped,
    Failed
}

public sealed record FaceImageResult(FaceImageOutcome Outcome, int FacesFound);

/// <summary>Analyzes one image and records the outcome. Scoped: one instance (and DbContext) per image.</summary>
public interface IFaceImageProcessor
{
    Task<FaceImageResult> ProcessAsync(int imageId, int faceModelId, CancellationToken cancellationToken = default);
}
```

`QueuedFaceRecognition.cs`:

```csharp
namespace PictureManager.Application.Faces;

/// <summary>A face job whose row exists (Enumerating). FolderId null = every active root.</summary>
public sealed record QueuedFaceRecognition(int JobId, int? FolderId, bool IsRecursive);
```

`IFaceRecognitionQueue.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;

namespace PictureManager.Application.Faces;

public interface IFaceRecognitionQueue
{
    void Enqueue(QueuedFaceRecognition item);
    IAsyncEnumerable<QueuedFaceRecognition> ReadAllAsync(CancellationToken cancellationToken = default);
}
```

`FaceRecognitionAlreadyInProgressException.cs`:

```csharp
using System;

namespace PictureManager.Application.Faces;

public sealed class FaceRecognitionAlreadyInProgressException()
    : Exception("Another job (scan, discovery or face recognition) is already in progress.");
```

`IFaceRecognitionService.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Faces;

public interface IFaceRecognitionService
{
    /// <summary>
    /// Validates the scope, creates the job (Enumerating, so any other job is refused), registers it for
    /// cancellation and queues it. folderId = that folder; rootId = that root; neither = all active roots.
    /// Throws FaceRecognitionAlreadyInProgressException, ScanRootUnavailableException or FolderUnavailableException.
    /// </summary>
    Task<int> QueueAsync(int? rootId, int? folderId, bool isRecursive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects candidates, analyzes them, clusters, and records the outcome (Completed, Failed or Cancelled).
    /// Rethrows the failure after recording it.
    /// </summary>
    Task RunAsync(QueuedFaceRecognition job, CancellationToken cancellationToken = default);

    /// <summary>Images the current model gave up on. Empty when the models are unavailable.</summary>
    Task<IReadOnlyList<FaceFailure>> GetPermanentFailuresAsync(CancellationToken cancellationToken = default);
}
```

Add to `IJobRepository` (below `IncrementFilesEnrichedAsync`):

```csharp
    /// <summary>Face recognition: atomically FilesEnriched += 1 and FacesFound += facesFound.</summary>
    Task IncrementFaceProgressAsync(int jobId, int facesFound, CancellationToken cancellationToken = default);
```

And to `JobRepository`:

```csharp
    public async Task IncrementFaceProgressAsync(int jobId, int facesFound, CancellationToken cancellationToken = default)
    {
        await _dbContext.Jobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.FilesEnriched, j => j.FilesEnriched + 1)
                .SetProperty(j => j.FacesFound, j => j.FacesFound + facesFound),
                cancellationToken);
    }
```

- [ ] **Step 6: Implement the processor**

`FaceImageProcessor.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;

namespace PictureManager.Application.Faces;

public sealed class FaceImageProcessor : IFaceImageProcessor
{
    private const string UndecodableMessage = "The image could not be decoded.";

    private readonly IImageRepository _images;
    private readonly IFaceRepository _faces;
    private readonly IFaceAnalyzer _analyzer;
    private readonly FaceRecognitionOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<FaceImageProcessor> _logger;

    public FaceImageProcessor(
        IImageRepository images, IFaceRepository faces, IFaceAnalyzer analyzer, FaceRecognitionOptions options, IClock clock,
        ILogger<FaceImageProcessor> logger)
    {
        _images = images;
        _faces = faces;
        _analyzer = analyzer;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public async Task<FaceImageResult> ProcessAsync(int imageId, int faceModelId, CancellationToken cancellationToken = default)
    {
        var image = await _images.GetByIdWithFolderAsync(imageId, cancellationToken);
        if (image?.Folder?.Root is null)
            return new FaceImageResult(FaceImageOutcome.Skipped, 0);

        var path = ImagePathResolver.ResolvePhysicalPath(
            image.Folder.Root.MountPath, image.Folder.RelativePath, image.FileName, image.Extension);

        // Gone, or the share is offline: not this job's business (a scan marks it missing). No state is written,
        // so an unmounted NAS never burns attempts.
        if (!File.Exists(path))
            return new FaceImageResult(FaceImageOutcome.Skipped, 0);

        FaceAnalysisResult? analysis;
        try
        {
            analysis = await _analyzer.AnalyzeAsync(path, image.Orientation, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not FaceModelUnavailableException)
        {
            _logger.LogWarning(ex, "Face analysis failed for image {ImageId}", imageId);
            await RecordFailureAsync(image, faceModelId, ScanTargets.TruncateErrorMessage(ex.Message), permanent: false, cancellationToken);
            return new FaceImageResult(FaceImageOutcome.Failed, 0);
        }

        if (analysis is null)
        {
            await RecordFailureAsync(image, faceModelId, UndecodableMessage, permanent: true, cancellationToken);
            return new FaceImageResult(FaceImageOutcome.Failed, 0);
        }

        var saved = await _faces.SaveResultAsync(imageId, faceModelId, image.ContentHash, analysis.Faces, _clock.UtcNow, cancellationToken);
        return saved
            ? new FaceImageResult(FaceImageOutcome.Processed, analysis.Faces.Count)
            : new FaceImageResult(FaceImageOutcome.Skipped, 0);
    }

    private async Task RecordFailureAsync(Image image, int faceModelId, string? message, bool permanent, CancellationToken cancellationToken)
    {
        // Attempts only accumulate for the same model and the same content; anything else restarts the count.
        var previous = await _faces.GetStateAsync(image.Id, cancellationToken);
        var priorAttempts = previous is not null && previous.FaceModelId == faceModelId && previous.ImageFingerprint == image.ContentHash
            ? previous.Attempts
            : 0;
        var attempts = priorAttempts + 1;
        var status = permanent || attempts >= _options.MaxAttempts
            ? FaceProcessingStatus.PermanentlyFailed
            : FaceProcessingStatus.Failed;

        await _faces.SaveFailureAsync(image.Id, faceModelId, image.ContentHash, status, attempts, message, _clock.UtcNow, cancellationToken);
    }
}
```

- [ ] **Step 7: Implement the service**

`FaceRecognitionService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;

namespace PictureManager.Application.Faces;

/// <summary>
/// The face recognition job: DB-selected candidates only (never a NAS walk), analyzed in parallel with one DI
/// scope per image, so each image commits on its own and a cancelled or interrupted job resumes on re-run.
/// </summary>
public sealed class FaceRecognitionService : IFaceRecognitionService
{
    private readonly IFolderRepository _folderRepository;
    private readonly IImageRootRepository _imageRootRepository;
    private readonly IJobRepository _jobRepository;
    private readonly IFaceRepository _faceRepository;
    private readonly IFaceAnalyzer _analyzer;
    private readonly IFaceRecognitionQueue _queue;
    private readonly IJobCancellationRegistry _cancellations;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FaceRecognitionOptions _options;
    private readonly IClock _clock;

    public FaceRecognitionService(
        IFolderRepository folderRepository,
        IImageRootRepository imageRootRepository,
        IJobRepository jobRepository,
        IFaceRepository faceRepository,
        IFaceAnalyzer analyzer,
        IFaceRecognitionQueue queue,
        IJobCancellationRegistry cancellations,
        IServiceScopeFactory scopeFactory,
        FaceRecognitionOptions options,
        IClock clock)
    {
        _folderRepository = folderRepository;
        _imageRootRepository = imageRootRepository;
        _jobRepository = jobRepository;
        _faceRepository = faceRepository;
        _analyzer = analyzer;
        _queue = queue;
        _cancellations = cancellations;
        _scopeFactory = scopeFactory;
        _options = options;
        _clock = clock;
    }

    public async Task<int> QueueAsync(int? rootId, int? folderId, bool isRecursive, CancellationToken cancellationToken = default)
    {
        if (await _jobRepository.HasActiveJobAsync(cancellationToken))
            throw new FaceRecognitionAlreadyInProgressException();

        var jobFolderId = await ScanTargets.ResolveJobFolderIdAsync(_folderRepository, _imageRootRepository, rootId, folderId, cancellationToken);

        // Created as Enumerating (not Pending) so HasActiveJobAsync refuses any other job while this one waits.
        var job = await _jobRepository.AddAsync(new Job
        {
            Kind = JobKind.FaceRecognition,
            FolderId = jobFolderId,
            IsRecursive = isRecursive,
            Status = JobStatus.Enumerating,
            StartedUtc = _clock.UtcNow
        }, cancellationToken);

        // Registered before enqueueing, so a cancel that arrives while the job still waits in the queue works.
        _cancellations.Register(job.Id);
        _queue.Enqueue(new QueuedFaceRecognition(job.Id, jobFolderId, isRecursive));
        return job.Id;
    }

    public async Task RunAsync(QueuedFaceRecognition job, CancellationToken cancellationToken = default)
    {
        // Declared outside the try so a failure doesn't zero the progress the UI already showed.
        var candidates = 0;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var faceModelId = await _faceRepository.GetOrCreateModelIdAsync(_analyzer.Model, _clock.UtcNow, cancellationToken);

            var imageIds = await _faceRepository.GetCandidateImageIdsAsync(faceModelId, job.FolderId, job.IsRecursive, cancellationToken);
            candidates = imageIds.Count;
            await _jobRepository.SetEnumerationResultAsync(job.JobId, foldersScanned: 0, filesFound: candidates, cancellationToken);
            await _jobRepository.TryTransitionToEnrichingAsync(job.JobId, cancellationToken);

            var parallel = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, _options.ReadConcurrency + _options.InferenceConcurrency),
                CancellationToken = cancellationToken
            };
            await Parallel.ForEachAsync(imageIds, parallel, async (imageId, token) =>
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IFaceImageProcessor>();
                var jobs = scope.ServiceProvider.GetRequiredService<IJobRepository>();

                var result = await processor.ProcessAsync(imageId, faceModelId, token);
                await jobs.IncrementFaceProgressAsync(job.JobId, result.FacesFound, token);
            });

            await _jobRepository.TryMarkCompletedAsync(job.JobId, JobStatus.Enriching, _clock.UtcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            await FinalizeFailureAsync(job.JobId, ex, candidates);
            throw;
        }
    }

    public async Task<IReadOnlyList<FaceFailure>> GetPermanentFailuresAsync(CancellationToken cancellationToken = default)
    {
        FaceModelDescriptor model;
        try
        {
            model = _analyzer.Model;
        }
        catch (FaceModelUnavailableException)
        {
            return Array.Empty<FaceFailure>();
        }

        var faceModelId = await _faceRepository.GetOrCreateModelIdAsync(model, _clock.UtcNow, cancellationToken);
        return await _faceRepository.GetPermanentFailuresAsync(faceModelId, cancellationToken);
    }

    private async Task FinalizeFailureAsync(int jobId, Exception ex, int candidates)
    {
        var isCancellation = ex is OperationCanceledException;
        var status = isCancellation ? JobStatus.Cancelled : JobStatus.Failed;
        var errorMessage = isCancellation ? null : ScanTargets.TruncateErrorMessage(ex.Message);

        // CancellationToken.None: the job's own token may be the one that just fired.
        await _jobRepository.SetFailureResultAsync(
            jobId, foldersScanned: 0, filesFound: candidates, errorMessage, status, _clock.UtcNow, CancellationToken.None);
    }
}
```

`ApplicationServiceCollectionExtensions.AddApplication`: add `using PictureManager.Application.Faces;` and:

```csharp
        services.AddScoped<IFaceImageProcessor, FaceImageProcessor>();
        services.AddScoped<IFaceRecognitionService, FaceRecognitionService>();
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~Faces"`
Expected: PASS (15 tests).
Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~JobRepositoryCompletionTests"`
Expected: PASS (2 tests).

- [ ] **Step 9: Commit**

```bash
git add src/PictureManager.Application/Faces src/PictureManager.Application/Repositories/IJobRepository.cs src/PictureManager.Infrastructure/Persistence/Repositories/JobRepository.cs src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs tests/PictureManager.Application.Tests/Faces tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/JobRepositoryCompletionTests.cs
git commit -m "feat: face recognition job service with per-image retry and resume

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Worker queue and background service

**Files:**
- Create: `src/PictureManager.Worker/Faces/ChannelFaceRecognitionQueue.cs`, `FaceRecognitionBackgroundService.cs`
- Modify: `src/PictureManager.Worker/DependencyInjection/WorkerServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Worker.Tests/Faces/FaceRecognitionBackgroundServiceTests.cs`

**Interfaces:**
- Consumes: `IFaceRecognitionQueue`, `QueuedFaceRecognition`, `IFaceRecognitionService` (Task 10); `IJobCancellationRegistry` (Task 1).
- Produces: hosted processing of queued face jobs. A user cancel ends only that job, and the loop keeps serving the queue.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Worker.Faces;
using Xunit;

namespace PictureManager.Worker.Tests.Faces;

public class FaceRecognitionBackgroundServiceTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private static (FaceRecognitionBackgroundService Service, ChannelFaceRecognitionQueue Queue, JobCancellationRegistry Registry)
        Create(IFaceRecognitionService faceService)
    {
        var queue = new ChannelFaceRecognitionQueue();
        var registry = new JobCancellationRegistry();
        var provider = new ServiceCollection()
            .AddScoped(_ => faceService)
            .AddScoped(_ => Substitute.For<IJobRepository>())
            .BuildServiceProvider();
        var service = new FaceRecognitionBackgroundService(
            queue, registry, provider.GetRequiredService<IServiceScopeFactory>(), Substitute.For<IClock>(),
            NullLogger<FaceRecognitionBackgroundService>.Instance);
        return (service, queue, registry);
    }

    [Fact]
    public async Task CancelledWhileQueued_RunsWithCancelledToken_AndLoopContinues()
    {
        var faceService = Substitute.For<IFaceRecognitionService>();
        var firstToken = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        faceService.RunAsync(new QueuedFaceRecognition(1, null, true), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var token = call.Arg<CancellationToken>();
            firstToken.SetResult(token);
            return Task.FromCanceled(token);
        });
        faceService.RunAsync(new QueuedFaceRecognition(2, null, true), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            secondRan.SetResult();
            return Task.CompletedTask;
        });
        var (service, queue, registry) = Create(faceService);
        registry.Register(1);
        registry.Cancel(1);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(new QueuedFaceRecognition(1, null, true));
        queue.Enqueue(new QueuedFaceRecognition(2, null, true));

        (await firstToken.Task.WaitAsync(WaitTimeout)).IsCancellationRequested.Should().BeTrue();
        await secondRan.Task.WaitAsync(WaitTimeout);
        await service.StopAsync(CancellationToken.None);
        registry.Cancel(1).Should().BeFalse("the runner releases the job when it finishes");
    }

    [Fact]
    public async Task UnexpectedFailure_MarksActiveJobFailed_AndLoopContinues()
    {
        var faceService = Substitute.For<IFaceRecognitionService>();
        faceService.RunAsync(new QueuedFaceRecognition(1, null, true), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));
        var secondRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        faceService.RunAsync(new QueuedFaceRecognition(2, null, true), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            secondRan.SetResult();
            return Task.CompletedTask;
        });
        var (service, queue, _) = Create(faceService);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(new QueuedFaceRecognition(1, null, true));
        queue.Enqueue(new QueuedFaceRecognition(2, null, true));

        await secondRan.Task.WaitAsync(WaitTimeout);
        await service.StopAsync(CancellationToken.None);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PictureManager.Worker.Tests --filter "FullyQualifiedName~FaceRecognitionBackgroundServiceTests"`
Expected: build FAILS (types not found).

- [ ] **Step 3: Implement**

`ChannelFaceRecognitionQueue.cs`:

```csharp
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using PictureManager.Application.Faces;

namespace PictureManager.Worker.Faces;

public sealed class ChannelFaceRecognitionQueue : IFaceRecognitionQueue
{
    private readonly Channel<QueuedFaceRecognition> _channel = Channel.CreateUnbounded<QueuedFaceRecognition>();

    public void Enqueue(QueuedFaceRecognition item) => _channel.Writer.TryWrite(item);

    public async IAsyncEnumerable<QueuedFaceRecognition> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }
}
```

`FaceRecognitionBackgroundService.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PictureManager.Application.Common;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;

namespace PictureManager.Worker.Faces;

/// <summary>
/// Runs queued face recognition jobs one at a time. Each runs under its user-cancellation token linked with
/// shutdown: a user cancel ends that job and the loop moves on; shutdown ends the loop.
/// </summary>
public sealed class FaceRecognitionBackgroundService : BackgroundService
{
    private const int MaxFailureRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly IFaceRecognitionQueue _queue;
    private readonly IJobCancellationRegistry _cancellations;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<FaceRecognitionBackgroundService> _logger;

    public FaceRecognitionBackgroundService(
        IFaceRecognitionQueue queue, IJobCancellationRegistry cancellations, IServiceScopeFactory scopeFactory, IClock clock,
        ILogger<FaceRecognitionBackgroundService> logger)
    {
        _queue = queue;
        _cancellations = cancellations;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.ReadAllAsync(stoppingToken))
        {
            // Register is idempotent: QueueAsync already registered it, so a cancel while queued is honored here.
            using var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _cancellations.Register(job.JobId));
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var faceService = scope.ServiceProvider.GetRequiredService<IFaceRecognitionService>();
                await faceService.RunAsync(job, jobCancellation.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down: RunAsync already recorded the job as Cancelled.
                break;
            }
            catch (OperationCanceledException)
            {
                // Cancelled by the user: RunAsync recorded it. Keep serving the queue.
                _logger.LogInformation("Face recognition {JobId} cancelled by the user", job.JobId);
            }
            catch (Exception ex) when (ex is FaceModelUnavailableException or ScanRootsUnavailableException
                                            or ScanRootUnavailableException or FolderUnavailableException)
            {
                // Expected: the job already carries the message for the UI.
                _logger.LogWarning("Face recognition {JobId} failed: {Message}", job.JobId, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Face recognition {JobId} failed", job.JobId);
                await MarkFailedWithRetryAsync(job.JobId, ex, stoppingToken);
            }
            finally
            {
                _cancellations.Release(job.JobId);
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
                // Only one job is active at a time, so this targets exactly the job that failed to record itself.
                await jobs.FailActiveJobsAsync(ex.Message, _clock.UtcNow, CancellationToken.None);
                return;
            }
            catch (Exception retryEx) when (attempt < MaxFailureRetries)
            {
                _logger.LogWarning(retryEx, "Retry {Attempt} failed to record face recognition {JobId} failure", attempt, jobId);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }
}
```

`WorkerServiceCollectionExtensions.AddWorker`: add `using PictureManager.Application.Faces; using PictureManager.Worker.Faces;` and:

```csharp
        services.AddSingleton<IFaceRecognitionQueue, ChannelFaceRecognitionQueue>();
        services.AddHostedService<FaceRecognitionBackgroundService>();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Worker.Tests`
Expected: PASS (all, including the 2 new tests).

- [ ] **Step 5: Commit**

```bash
git add src/PictureManager.Worker/Faces src/PictureManager.Worker/DependencyInjection/WorkerServiceCollectionExtensions.cs tests/PictureManager.Worker.Tests/Faces
git commit -m "feat: background worker for face recognition jobs with user cancel

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Clustering into people

**Files:**
- Create: `src/PictureManager.Application/Faces/FaceClustering.cs`, `IFaceClusterer.cs`, `FaceClusterer.cs`
- Modify: `src/PictureManager.Application/Repositories/IFaceRepository.cs`, `src/PictureManager.Infrastructure/Persistence/Repositories/FaceRepository.cs` (clustering methods)
- Modify: `src/PictureManager.Application/Faces/FaceRecognitionService.cs` (call the clusterer), `ApplicationServiceCollectionExtensions.cs`
- Modify: `tests/PictureManager.Application.Tests/Faces/FaceRecognitionServiceTests.cs` (pass the clusterer)
- Test: `tests/PictureManager.Application.Tests/Faces/FaceClusteringTests.cs`, `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FaceClusterRepositoryTests.cs`

**Interfaces:**
- Consumes: `IFaceRepository` (Task 9) and `FaceRecognitionOptions` (Task 8).
- Produces:

```csharp
// IFaceRepository additions:
public sealed record FaceCandidate(int Id, float Quality);
public sealed record FaceNeighbor(int FaceId, int? PersonId, float Distance);
public enum NeighborPool { Assigned, Unassigned }
Task<IReadOnlyList<FaceCandidate>> GetUnassignedFacesAsync(int faceModelId, float minQuality, CancellationToken ct = default);
Task<IReadOnlyList<FaceNeighbor>> GetNearestAsync(int faceId, int faceModelId, NeighborPool pool, int k, CancellationToken ct = default);
Task AssignAsync(IReadOnlyCollection<int> faceIds, int personId, CancellationToken ct = default); // only Unassigned rows; sets Auto
Task<int> CreateUnnamedPersonAsync(int coverFaceId, DateTime nowUtc, CancellationToken ct = default);
Task<int> DeleteEmptyUnnamedPeopleAsync(CancellationToken ct = default);
// Application:
public static class FaceClustering { int? MajorityPerson(IReadOnlyList<FaceNeighbor>, float maxDistance, int minVotes); Task<List<List<int>>> DbscanAsync(IReadOnlyList<int> ids, Func<int, Task<IReadOnlyList<int>>> neighbors, int minPoints, CancellationToken); }
public interface IFaceClusterer { Task ClusterAsync(int faceModelId, CancellationToken ct = default); }
```

- [ ] **Step 1: Write the failing pure-logic tests**

`tests/PictureManager.Application.Tests/Faces/FaceClusteringTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using Xunit;

namespace PictureManager.Application.Tests.Faces;

public class FaceClusteringTests
{
    [Fact]
    public void MajorityPerson_NeedsMinVotesAndStrictMajorityWithinDistance()
    {
        var neighbors = new List<FaceNeighbor>
        {
            new(1, 10, 0.2f), new(2, 10, 0.3f), new(3, 11, 0.35f), new(4, 11, 0.9f), new(5, 11, 0.95f)
        };

        FaceClustering.MajorityPerson(neighbors, maxDistance: 0.4f, minVotes: 2).Should().Be(10);
        FaceClustering.MajorityPerson(neighbors, maxDistance: 0.4f, minVotes: 3).Should().BeNull();
        FaceClustering.MajorityPerson(neighbors.Take(1).ToList(), maxDistance: 0.4f, minVotes: 2).Should().BeNull();
    }

    [Fact]
    public void MajorityPerson_TieIsNotAMajority()
    {
        var neighbors = new List<FaceNeighbor> { new(1, 10, 0.1f), new(2, 10, 0.1f), new(3, 11, 0.1f), new(4, 11, 0.1f) };

        FaceClustering.MajorityPerson(neighbors, 0.4f, 2).Should().BeNull();
    }

    [Fact]
    public async Task Dbscan_GroupsDenseComponents_AndLeavesNoiseOut()
    {
        // 1-2-3-4 chain (dense), 5-6 pair (too small for minPoints 3), 7 isolated, 8 outside the input set.
        var graph = new Dictionary<int, int[]>
        {
            [1] = new[] { 2, 3 }, [2] = new[] { 1, 3 }, [3] = new[] { 1, 2, 4 }, [4] = new[] { 3, 8 },
            [5] = new[] { 6 }, [6] = new[] { 5 }, [7] = new int[0]
        };

        var clusters = await FaceClustering.DbscanAsync(
            new[] { 1, 2, 3, 4, 5, 6, 7 },
            id => Task.FromResult<IReadOnlyList<int>>(graph[id]),
            minPoints: 3,
            CancellationToken.None);

        clusters.Should().ContainSingle().Which.Should().BeEquivalentTo(new[] { 1, 2, 3, 4 });
    }
}
```

- [ ] **Step 2: Write the failing repository tests**

`tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FaceClusterRepositoryTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class FaceClusterRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetNearestAsync_FiltersByModelAndPool_OrderedByDistance()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context, "a");
        var other = await FaceTestData.AddModelAsync(db.Context, "b");
        var person = new Person { CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.Add(person);
        await db.Context.SaveChangesAsync();
        var query = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0));
        var assignedNear = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0, 0.1), person.Id, FaceAssignmentState.Confirmed);
        var assignedFar = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0, 0.5), person.Id, FaceAssignmentState.Auto);
        var unassignedNear = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0, 0.05));
        await FaceTestData.AddFaceAsync(db.Context, image.Id, other, FaceTestData.Embedding(0), person.Id, FaceAssignmentState.Confirmed);
        await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0, 0.01), person.Id, FaceAssignmentState.Rejected);
        var repository = new FaceRepository(db.CreateContext());

        var assigned = await repository.GetNearestAsync(query.Id, model, NeighborPool.Assigned, 5);
        var unassigned = await repository.GetNearestAsync(query.Id, model, NeighborPool.Unassigned, 5);

        assigned.Select(n => n.FaceId).Should().Equal(assignedNear.Id, assignedFar.Id);
        assigned[0].PersonId.Should().Be(person.Id);
        unassigned.Select(n => n.FaceId).Should().Equal(unassignedNear.Id);
    }

    [Fact]
    public async Task AssignAsync_OnlyTouchesUnassignedFaces()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var named = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        var group = new Person { CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.AddRange(named, group);
        await db.Context.SaveChangesAsync();
        var confirmed = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0), named.Id, FaceAssignmentState.Confirmed);
        var free = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(1));
        var repository = new FaceRepository(db.CreateContext());

        await repository.AssignAsync(new[] { confirmed.Id, free.Id }, group.Id);

        await using var read = db.CreateContext();
        (await read.Faces.SingleAsync(f => f.Id == confirmed.Id)).PersonId.Should().Be(named.Id);
        var assigned = await read.Faces.SingleAsync(f => f.Id == free.Id);
        assigned.PersonId.Should().Be(group.Id);
        assigned.AssignmentState.Should().Be(FaceAssignmentState.Auto);
    }

    [Fact]
    public async Task DeleteEmptyUnnamedPeopleAsync_KeepsNamedPeopleAndNonEmptyGroups()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var emptyNamed = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        var emptyGroup = new Person { CreatedUtc = Now, ModifiedUtc = Now };
        var group = new Person { CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.AddRange(emptyNamed, emptyGroup, group);
        await db.Context.SaveChangesAsync();
        await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0), group.Id, FaceAssignmentState.Auto);

        var deleted = await new FaceRepository(db.CreateContext()).DeleteEmptyUnnamedPeopleAsync();

        deleted.Should().Be(1);
        await using var read = db.CreateContext();
        (await read.People.Select(p => p.Id).ToListAsync()).Should().BeEquivalentTo(new[] { emptyNamed.Id, group.Id });
    }

    [Fact]
    public async Task GetUnassignedFacesAsync_FiltersByQualityAndModel()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context, "a");
        var other = await FaceTestData.AddModelAsync(db.Context, "b");
        var good = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0), quality: 0.8f);
        await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(1), quality: 0.2f);
        await FaceTestData.AddFaceAsync(db.Context, image.Id, other, FaceTestData.Embedding(2), quality: 0.9f);

        var faces = await new FaceRepository(db.CreateContext()).GetUnassignedFacesAsync(model, 0.5f);

        faces.Should().ContainSingle().Which.Id.Should().Be(good.Id);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~FaceClusteringTests"`
Expected: build FAILS (`FaceClustering`, `FaceNeighbor` not found).

- [ ] **Step 4: Extend the repository**

Add to `IFaceRepository.cs` (above the interface):

```csharp
public sealed record FaceCandidate(int Id, float Quality);
public sealed record FaceNeighbor(int FaceId, int? PersonId, float Distance);

/// <summary>Assigned = has a person, Auto or Confirmed. Unassigned = no person yet. Rejected faces are in neither.</summary>
public enum NeighborPool
{
    Assigned,
    Unassigned
}
```

and inside the interface:

```csharp
    /// <summary>Unassigned faces of this model with QualityScore ≥ minQuality, best quality first.</summary>
    Task<IReadOnlyList<FaceCandidate>> GetUnassignedFacesAsync(int faceModelId, float minQuality, CancellationToken cancellationToken = default);

    /// <summary>The k nearest faces (cosine distance) of the same model in the given pool, nearest first, excluding faceId.</summary>
    Task<IReadOnlyList<FaceNeighbor>> GetNearestAsync(int faceId, int faceModelId, NeighborPool pool, int k, CancellationToken cancellationToken = default);

    /// <summary>Sets PersonId and Auto on those of these faces that are still Unassigned. Never touches user decisions.</summary>
    Task AssignAsync(IReadOnlyCollection<int> faceIds, int personId, CancellationToken cancellationToken = default);

    Task<int> CreateUnnamedPersonAsync(int coverFaceId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Deletes unnamed people with no faces left. Named people are kept even when empty. Returns how many.</summary>
    Task<int> DeleteEmptyUnnamedPeopleAsync(CancellationToken cancellationToken = default);
```

Add to `FaceRepository` (with `using Pgvector.EntityFrameworkCore;`):

```csharp
    public async Task<IReadOnlyList<FaceCandidate>> GetUnassignedFacesAsync(int faceModelId, float minQuality, CancellationToken cancellationToken = default) =>
        await _dbContext.Faces.AsNoTracking()
            .Where(f => f.FaceModelId == faceModelId && f.AssignmentState == FaceAssignmentState.Unassigned && f.QualityScore >= minQuality)
            .OrderByDescending(f => f.QualityScore)
            .Select(f => new FaceCandidate(f.Id, f.QualityScore))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FaceNeighbor>> GetNearestAsync(
        int faceId, int faceModelId, NeighborPool pool, int k, CancellationToken cancellationToken = default)
    {
        var embedding = await _dbContext.Faces.AsNoTracking()
            .Where(f => f.Id == faceId).Select(f => f.Embedding).FirstOrDefaultAsync(cancellationToken);
        if (embedding is null)
            return Array.Empty<FaceNeighbor>();

        var faces = _dbContext.Faces.AsNoTracking().Where(f => f.FaceModelId == faceModelId && f.Id != faceId);
        faces = pool == NeighborPool.Assigned
            ? faces.Where(f => f.PersonId != null
                               && (f.AssignmentState == FaceAssignmentState.Auto || f.AssignmentState == FaceAssignmentState.Confirmed))
            : faces.Where(f => f.AssignmentState == FaceAssignmentState.Unassigned);

        return await faces
            .OrderBy(f => f.Embedding.CosineDistance(embedding))
            .Take(k)
            .Select(f => new FaceNeighbor(f.Id, f.PersonId, (float)f.Embedding.CosineDistance(embedding)))
            .ToListAsync(cancellationToken);
    }

    public async Task AssignAsync(IReadOnlyCollection<int> faceIds, int personId, CancellationToken cancellationToken = default)
    {
        await _dbContext.Faces
            .Where(f => faceIds.Contains(f.Id) && f.AssignmentState == FaceAssignmentState.Unassigned)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.PersonId, personId)
                .SetProperty(f => f.AssignmentState, FaceAssignmentState.Auto),
                cancellationToken);
    }

    public async Task<int> CreateUnnamedPersonAsync(int coverFaceId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var person = new Person { CoverFaceId = coverFaceId, CreatedUtc = nowUtc, ModifiedUtc = nowUtc };
        _dbContext.People.Add(person);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();
        return person.Id;
    }

    public Task<int> DeleteEmptyUnnamedPeopleAsync(CancellationToken cancellationToken = default) =>
        _dbContext.People
            .Where(p => p.Name == null && !_dbContext.Faces.Any(f => f.PersonId == p.Id))
            .ExecuteDeleteAsync(cancellationToken);
```

- [ ] **Step 5: Implement the clustering logic and clusterer**

`FaceClustering.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Faces;

/// <summary>Pure clustering rules, unit-testable without a database.</summary>
public static class FaceClustering
{
    /// <summary>
    /// The person that wins among the neighbours within maxDistance: at least minVotes of them, and strictly more
    /// than half of those within distance (a tie is no decision). Null when nobody qualifies.
    /// </summary>
    public static int? MajorityPerson(IReadOnlyList<FaceNeighbor> neighbors, float maxDistance, int minVotes)
    {
        var within = neighbors.Where(n => n.PersonId is not null && n.Distance <= maxDistance).ToList();
        if (within.Count == 0)
            return null;

        var best = within.GroupBy(n => n.PersonId!.Value).OrderByDescending(g => g.Count()).First();
        return best.Count() >= minVotes && best.Count() * 2 > within.Count ? best.Key : null;
    }

    /// <summary>
    /// DBSCAN over ids. neighbors(id) returns ids within eps (it may include ids outside the set, which are
    /// ignored). A point is core when it has ≥ minPoints − 1 neighbours in the set (so the cluster, including
    /// itself, reaches minPoints). Clusters smaller than minPoints are dropped as noise.
    /// </summary>
    public static async Task<List<List<int>>> DbscanAsync(
        IReadOnlyList<int> ids, Func<int, Task<IReadOnlyList<int>>> neighbors, int minPoints, CancellationToken cancellationToken)
    {
        var inSet = ids.ToHashSet();
        var visited = new HashSet<int>();
        var clustered = new HashSet<int>();
        var clusters = new List<List<int>>();

        foreach (var id in ids)
        {
            if (!visited.Add(id))
                continue;

            var seeds = (await neighbors(id)).Where(inSet.Contains).ToList();
            if (seeds.Count + 1 < minPoints)
                continue;

            var cluster = new List<int> { id };
            clustered.Add(id);
            var queue = new Queue<int>(seeds);
            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = queue.Dequeue();
                if (clustered.Add(current))
                    cluster.Add(current);
                if (!visited.Add(current))
                    continue;

                var next = (await neighbors(current)).Where(inSet.Contains).ToList();
                if (next.Count + 1 >= minPoints)
                    foreach (var n in next.Where(n => !clustered.Contains(n)))
                        queue.Enqueue(n);
            }

            if (cluster.Count >= minPoints)
                clusters.Add(cluster);
        }

        return clusters;
    }
}
```

`IFaceClusterer.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Faces;

public interface IFaceClusterer
{
    /// <summary>Attaches unassigned faces to existing people, groups the rest into unnamed people, removes empty groups.</summary>
    Task ClusterAsync(int faceModelId, CancellationToken cancellationToken = default);
}
```

`FaceClusterer.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Faces;

/// <summary>Idempotent: re-running only touches Unassigned faces; Confirmed/Rejected are never changed.</summary>
public sealed class FaceClusterer : IFaceClusterer
{
    private const int MatchNeighbors = 5;
    private const int MatchMinVotes = 2;
    private const int ClusterNeighbors = 32;

    private readonly IFaceRepository _faces;
    private readonly FaceRecognitionOptions _options;
    private readonly IClock _clock;

    public FaceClusterer(IFaceRepository faces, FaceRecognitionOptions options, IClock clock)
    {
        _faces = faces;
        _options = options;
        _clock = clock;
    }

    public async Task ClusterAsync(int faceModelId, CancellationToken cancellationToken = default)
    {
        // (a) Join existing people (named or unnamed groups) when a clear majority of near neighbours agree.
        foreach (var face in await _faces.GetUnassignedFacesAsync(faceModelId, minQuality: 0f, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var neighbors = await _faces.GetNearestAsync(face.Id, faceModelId, NeighborPool.Assigned, MatchNeighbors, cancellationToken);
            if (FaceClustering.MajorityPerson(neighbors, _options.AutoMatchDistance, MatchMinVotes) is int personId)
                await _faces.AssignAsync(new[] { face.Id }, personId, cancellationToken);
        }

        // (b) Group what's left (good-quality faces only) into new unnamed people.
        var remaining = await _faces.GetUnassignedFacesAsync(faceModelId, _options.MinQualityForClustering, cancellationToken);
        var quality = remaining.ToDictionary(f => f.Id, f => f.Quality);
        var clusters = await FaceClustering.DbscanAsync(
            remaining.Select(f => f.Id).ToList(),
            async id => (await _faces.GetNearestAsync(id, faceModelId, NeighborPool.Unassigned, ClusterNeighbors, cancellationToken))
                .Where(n => n.Distance <= _options.ClusterDistance)
                .Select(n => n.FaceId)
                .ToList(),
            _options.MinFacesPerGroup,
            cancellationToken);

        foreach (var cluster in clusters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cover = cluster.MaxBy(id => quality[id]);
            var personId = await _faces.CreateUnnamedPersonAsync(cover, _clock.UtcNow, cancellationToken);
            await _faces.AssignAsync(cluster, personId, cancellationToken);
        }

        // (c) Groups emptied by re-processing (content changed, image deleted) disappear; named people stay.
        await _faces.DeleteEmptyUnnamedPeopleAsync(cancellationToken);
    }
}
```

- [ ] **Step 6: Call the clusterer from the job**

In `FaceRecognitionService`:
- Add an `IFaceClusterer clusterer` constructor parameter (after `IFaceAnalyzer analyzer`) and a `_clusterer` field.
- Right before `await _jobRepository.TryMarkCompletedAsync(...)` add:

```csharp
            // Global, current model only. Runs after every job so newly detected faces join people at once.
            await _clusterer.ClusterAsync(faceModelId, cancellationToken);
```

In `FaceRecognitionServiceTests`:
- Add `private readonly IFaceClusterer _clusterer = Substitute.For<IFaceClusterer>();` and pass `_clusterer` after `_analyzer` in `Create()`.
- In `RunAsync_ProcessesEveryCandidate_ReportsProgress_AndCompletes`, add `await _clusterer.Received(1).ClusterAsync(3, Arg.Any<CancellationToken>());`.

`ApplicationServiceCollectionExtensions`: `services.AddScoped<IFaceClusterer, FaceClusterer>();`.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~Faces"`
Expected: PASS.
Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~FaceClusterRepositoryTests"`
Expected: PASS, 4 tests.

- [ ] **Step 8: Commit**

```bash
git add src/PictureManager.Application/Faces src/PictureManager.Application/Repositories/IFaceRepository.cs src/PictureManager.Infrastructure/Persistence/Repositories/FaceRepository.cs src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs tests/PictureManager.Application.Tests/Faces tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FaceClusterRepositoryTests.cs
git commit -m "feat: match faces to people and group the rest with DBSCAN

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 13: Face recognition endpoints

**Files:**
- Create: `src/PictureManager.Api/Endpoints/FaceRecognitionEndpoints.cs`
- Modify: `src/PictureManager.Api/Endpoints/JobEndpoints.cs` (`ActiveJobDto` gains `FacesFound`)
- Modify: `src/PictureManager.Api/Program.cs` (map)
- Test: `tests/PictureManager.Api.Tests/Endpoints/FaceRecognitionEndpointsTests.cs`; modify `JobEndpointsTests.cs` (DTO expectation)

**Interfaces:**
- Consumes: `IFaceRecognitionService` (Task 10).
- Produces:
  - `POST /api/face-recognitions` `{ rootId?, folderId?, isRecursive }` → `{ faceRecognitionJobId }` (200), 409 or 400.
  - `GET /api/face-recognitions/{id}/events`: SSE with PascalCase `{ Id, Status, ImagesFound, ImagesProcessed, FacesFound, ErrorMessage }`.
  - `GET /api/face-recognitions/failures` → `FaceFailure[]` (camelCase JSON).
  - `ActiveJobDto(..., string? ErrorMessage, int FacesFound)`.

- [ ] **Step 1: Write the failing tests**

`FaceRecognitionEndpointsTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Faces;
using PictureManager.Application.Scanning;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class FaceRecognitionEndpointsTests
{
    [Fact]
    public async Task StartAsync_Queued_ReturnsJobId()
    {
        var service = Substitute.For<IFaceRecognitionService>();
        service.QueueAsync(null, 20, true, Arg.Any<CancellationToken>()).Returns(77);

        var result = await FaceRecognitionEndpoints.StartAsync(new FaceRecognitionRequest(null, 20, true), service, CancellationToken.None);

        result.Should().BeOfType<Ok<FaceRecognitionStartedResponse>>().Which.Value.Should().Be(new FaceRecognitionStartedResponse(77));
    }

    [Fact]
    public async Task StartAsync_AnotherJobActive_ReturnsConflict()
    {
        var service = Substitute.For<IFaceRecognitionService>();
        service.QueueAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new FaceRecognitionAlreadyInProgressException());

        var result = await FaceRecognitionEndpoints.StartAsync(new FaceRecognitionRequest(null, null, true), service, CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task StartAsync_UnavailableFolder_ReturnsValidationProblemOnFolderId()
    {
        var service = Substitute.For<IFaceRecognitionService>();
        service.QueueAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(FolderUnavailableException.Missing(20));

        var result = await FaceRecognitionEndpoints.StartAsync(new FaceRecognitionRequest(null, 20, true), service, CancellationToken.None);

        result.Should().BeOfType<ValidationProblem>().Which.ProblemDetails.Errors.Should().ContainKey("folderId");
    }
}
```

In `JobEndpointsTests.GetActiveJobAsync_ActiveJob_ReturnsItsDto`, change the expectation to `new ActiveJobDto("Scan", 7, 20, "Enumerating", 3, 5, 1, null, 0)`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~FaceRecognitionEndpointsTests|FullyQualifiedName~JobEndpointsTests"`
Expected: build FAILS (`FaceRecognitionEndpoints` not found; `ActiveJobDto` arity).

- [ ] **Step 3: Implement**

`FaceRecognitionEndpoints.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;

namespace PictureManager.Api.Endpoints;

public static class FaceRecognitionEndpoints
{
    public static IEndpointRouteBuilder MapFaceRecognitionEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapPost("/face-recognitions", StartAsync);
        admin.MapGet("/face-recognitions/{id:int}/events", StreamEventsAsync);
        admin.MapGet("/face-recognitions/failures", GetFailuresAsync);
        return admin;
    }

    public static async Task<IResult> StartAsync(FaceRecognitionRequest request, IFaceRecognitionService service, CancellationToken cancellationToken)
    {
        try
        {
            var jobId = await service.QueueAsync(request.RootId, request.FolderId, request.IsRecursive, cancellationToken);
            return TypedResults.Ok(new FaceRecognitionStartedResponse(jobId));
        }
        catch (FaceRecognitionAlreadyInProgressException ex)
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

    public static async Task StreamEventsAsync(HttpContext context, int id, IJobRepository jobRepository, CancellationToken cancellationToken)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        while (!cancellationToken.IsCancellationRequested)
        {
            var job = await jobRepository.GetByIdAsync(id, cancellationToken);
            if (job is null)
            {
                await context.Response.WriteAsync("event: error\ndata: not found\n\n", cancellationToken);
                return;
            }

            var payload = JsonSerializer.Serialize(new FaceRecognitionProgress(
                job.Id, job.Status.ToString(), job.FilesFound, job.FilesEnriched, job.FacesFound, job.ErrorMessage));
            await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);

            if (job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                return;

            await Task.Delay(1000, cancellationToken);
        }
    }

    public static async Task<IResult> GetFailuresAsync(IFaceRecognitionService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetPermanentFailuresAsync(cancellationToken));
}

public sealed record FaceRecognitionRequest(int? RootId, int? FolderId, bool IsRecursive = true);
public sealed record FaceRecognitionStartedResponse(int FaceRecognitionJobId);
public sealed record FaceRecognitionProgress(int Id, string Status, int ImagesFound, int ImagesProcessed, int FacesFound, string? ErrorMessage);
```

In `JobEndpoints.cs`, append `job.FacesFound` to the `ActiveJobDto` construction and the record:

```csharp
public sealed record ActiveJobDto(
    string Kind, int Id, int? FolderId, string Status,
    int FoldersProcessed, int FilesFound, int FilesEnriched, string? ErrorMessage, int FacesFound);
```

`Program.cs`, after `admin.MapDiscoveryEndpoints();`:

```csharp
    admin.MapFaceRecognitionEndpoints();
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Api.Tests`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add src/PictureManager.Api/Endpoints/FaceRecognitionEndpoints.cs src/PictureManager.Api/Endpoints/JobEndpoints.cs src/PictureManager.Api/Program.cs tests/PictureManager.Api.Tests/Endpoints/FaceRecognitionEndpointsTests.cs tests/PictureManager.Api.Tests/Endpoints/JobEndpointsTests.cs
git commit -m "feat: face recognition start, progress and failure endpoints

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 14: People backend: list, name/merge, face crops, person filter

**Files:**
- Create: `src/PictureManager.Application/Repositories/IPeopleRepository.cs`, `src/PictureManager.Infrastructure/Persistence/Repositories/PeopleRepository.cs`
- Create: `src/PictureManager.Application/Faces/IPeopleService.cs`, `PeopleService.cs`, `IFaceCropService.cs`
- Create: `src/PictureManager.Infrastructure/Faces/FaceCropService.cs`
- Create: `src/PictureManager.Api/Endpoints/PeopleEndpoints.cs`
- Modify: `src/PictureManager.Application/Images/ImageQueryModels.cs:19`, `ImageDtos.cs:54-62`, `ImageQueryService.cs:69`
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/ImageQueryRepository.cs:30-46`
- Modify: `src/PictureManager.Api/Endpoints/ImageQueryEndpoints.cs` (the `ListAsync` parameters)
- Modify: DI registrations (Application, Infrastructure), `Program.cs`
- Test: `tests/PictureManager.Application.Tests/Faces/PeopleServiceTests.cs`, `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/PeopleRepositoryTests.cs`

**Interfaces:**
- Consumes: entities (Task 5), `FaceTestData` (Task 5), `IThumbnailService` (existing) and `ThumbnailCacheOptions` (existing).
- Produces:

```csharp
public sealed record PersonSummary(int Id, string? Name, int FaceCount, int PhotoCount, int? CoverFaceId);
public sealed record FaceCropSource(int FaceId, string ContentHash, string MountPath, string RelativePath, string FileName, string Extension, int? Orientation, float X, float Y, float Width, float Height);
public interface IPeopleRepository
{
    Task<IReadOnlyList<PersonSummary>> GetAllAsync(CancellationToken ct = default);
    Task<PersonSummary?> GetAsync(int id, CancellationToken ct = default);
    Task<int?> FindIdByNameAsync(string name, CancellationToken ct = default); // case-insensitive
    Task<bool> SetNameAsync(int id, string name, DateTime nowUtc, CancellationToken ct = default); // also Confirms its Auto faces
    Task MergeAsync(int sourceId, int targetId, DateTime nowUtc, CancellationToken ct = default); // move faces (Confirmed), delete source
    Task<FaceCropSource?> GetFaceCropSourceAsync(int faceId, CancellationToken ct = default);
}
public interface IPeopleService
{
    Task<IReadOnlyList<PersonSummary>> GetAllAsync(CancellationToken ct = default);
    Task<Result<PersonSummary>> GetAsync(int id, CancellationToken ct = default);
    Task<Result<PersonSummary>> NameAsync(int id, string? name, CancellationToken ct = default); // returns the surviving person
}
public interface IFaceCropService { Task<string?> GetOrCreateCropPathAsync(int faceId, CancellationToken ct = default); }
// ImageListFilter gains int? PersonId = null (last); ImageListRequest gains int? PersonId = null (last).
```

- Endpoints: `GET /api/people`, `GET /api/people/{id}`, `PATCH /api/people/{id}` `{ name }`, `GET /api/faces/{id}/thumbnail`, and `GET /api/images?personId=`.

- [ ] **Step 1: Write the failing service tests**

`PeopleServiceTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using Xunit;

namespace PictureManager.Application.Tests.Faces;

public class PeopleServiceTests
{
    private readonly IPeopleRepository _people = Substitute.For<IPeopleRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public PeopleServiceTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        _people.GetAsync(1, Arg.Any<CancellationToken>()).Returns(new PersonSummary(1, null, 5, 4, 100));
        _people.GetAsync(2, Arg.Any<CancellationToken>()).Returns(new PersonSummary(2, "Anna", 9, 8, 200));
    }

    private PeopleService Create() => new(_people, _clock);

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task NameAsync_Blank_IsInvalid(string? name)
    {
        var result = await Create().NameAsync(1, name);

        result.Status.Should().Be(ResultStatus.Invalid);
        await _people.DidNotReceiveWithAnyArgs().SetNameAsync(default, default!, default, default);
    }

    [Fact]
    public async Task NameAsync_UnknownPerson_IsNotFound()
    {
        (await Create().NameAsync(99, "Anna")).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task NameAsync_NewName_TrimsAndSetsIt()
    {
        _people.FindIdByNameAsync("Bela", Arg.Any<CancellationToken>()).Returns((int?)null);

        var result = await Create().NameAsync(1, "  Bela ");

        result.Status.Should().Be(ResultStatus.Success);
        await _people.Received(1).SetNameAsync(1, "Bela", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NameAsync_ExistingNameOtherCase_MergesIntoThatPerson()
    {
        _people.FindIdByNameAsync("anna", Arg.Any<CancellationToken>()).Returns(2);

        var result = await Create().NameAsync(1, "anna");

        result.Value!.Id.Should().Be(2);
        await _people.Received(1).MergeAsync(1, 2, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _people.DidNotReceiveWithAnyArgs().SetNameAsync(default, default!, default, default);
    }

    [Fact]
    public async Task NameAsync_SamePersonsOwnName_JustRenames()
    {
        _people.FindIdByNameAsync("ANNA", Arg.Any<CancellationToken>()).Returns(2);

        await Create().NameAsync(2, "ANNA");

        await _people.Received(1).SetNameAsync(2, "ANNA", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _people.DidNotReceiveWithAnyArgs().MergeAsync(default, default, default, default);
    }

    [Fact]
    public async Task NameAsync_TooLong_IsInvalid()
    {
        (await Create().NameAsync(1, new string('x', 201))).Status.Should().Be(ResultStatus.Invalid);
    }
}
```

- [ ] **Step 2: Write the failing repository tests**

`PeopleRepositoryTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class PeopleRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetAllAsync_CountsAssignedFacesAndDistinctPhotos_SkipsEmptyUnnamed()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var a = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var b = await FaceTestData.AddImageAsync(db.Context, top, "b");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var anna = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        var empty = new Person { CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.AddRange(anna, empty);
        await db.Context.SaveChangesAsync();
        await FaceTestData.AddFaceAsync(db.Context, a.Id, model, FaceTestData.Embedding(0), anna.Id, FaceAssignmentState.Confirmed);
        await FaceTestData.AddFaceAsync(db.Context, a.Id, model, FaceTestData.Embedding(1), anna.Id, FaceAssignmentState.Auto);
        await FaceTestData.AddFaceAsync(db.Context, b.Id, model, FaceTestData.Embedding(2), anna.Id, FaceAssignmentState.Auto);
        await FaceTestData.AddFaceAsync(db.Context, b.Id, model, FaceTestData.Embedding(3), anna.Id, FaceAssignmentState.Rejected);

        var people = await new PeopleRepository(db.CreateContext()).GetAllAsync();

        people.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = anna.Id, Name = "Anna", FaceCount = 3, PhotoCount = 2 });
    }

    [Fact]
    public async Task SetNameAsync_NamesAndConfirmsAutoFaces()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var group = new Person { CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.Add(group);
        await db.Context.SaveChangesAsync();
        var face = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0), group.Id, FaceAssignmentState.Auto);

        (await new PeopleRepository(db.CreateContext()).SetNameAsync(group.Id, "Bela", Now)).Should().BeTrue();

        await using var read = db.CreateContext();
        (await read.People.SingleAsync(p => p.Id == group.Id)).Name.Should().Be("Bela");
        (await read.Faces.SingleAsync(f => f.Id == face.Id)).AssignmentState.Should().Be(FaceAssignmentState.Confirmed);
    }

    [Fact]
    public async Task FindIdByNameAsync_IsCaseInsensitive()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var anna = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.Add(anna);
        await db.Context.SaveChangesAsync();

        (await new PeopleRepository(db.CreateContext()).FindIdByNameAsync("aNNA")).Should().Be(anna.Id);
    }

    [Fact]
    public async Task MergeAsync_MovesFacesAsConfirmed_AndDeletesSource()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var source = new Person { CreatedUtc = Now, ModifiedUtc = Now };
        var target = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.AddRange(source, target);
        await db.Context.SaveChangesAsync();
        var face = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0), source.Id, FaceAssignmentState.Auto);

        await new PeopleRepository(db.CreateContext()).MergeAsync(source.Id, target.Id, Now);

        await using var read = db.CreateContext();
        (await read.People.AnyAsync(p => p.Id == source.Id)).Should().BeFalse();
        var moved = await read.Faces.SingleAsync(f => f.Id == face.Id);
        moved.PersonId.Should().Be(target.Id);
        moved.AssignmentState.Should().Be(FaceAssignmentState.Confirmed);
    }

    [Fact]
    public async Task ImageList_PersonFilter_ReturnsOnlyThatPersonsPhotos()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var withAnna = await FaceTestData.AddImageAsync(db.Context, top, "with");
        var rejected = await FaceTestData.AddImageAsync(db.Context, top, "rejected");
        await FaceTestData.AddImageAsync(db.Context, top, "without");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var anna = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.Add(anna);
        await db.Context.SaveChangesAsync();
        await FaceTestData.AddFaceAsync(db.Context, withAnna.Id, model, FaceTestData.Embedding(0), anna.Id, FaceAssignmentState.Auto);
        await FaceTestData.AddFaceAsync(db.Context, rejected.Id, model, FaceTestData.Embedding(1), anna.Id, FaceAssignmentState.Rejected);
        var repository = new ImageQueryRepository(db.CreateContext());

        var rows = await repository.ListAsync(
            new PictureManager.Application.Images.ImageListFilter(null, null, null, false, anna.Id),
            PictureManager.Application.Images.ImageSort.Name, PictureManager.Application.Images.SortDirection.Asc, null, 10);

        rows.Select(r => r.Id).Should().Equal(withAnna.Id);
    }
}
```

> Before running, open `ImageQueryRepository.cs:24-26` and copy the real `ListAsync` method name and parameter list into the last test. The call above assumes `ListAsync(filter, sort, direction, after, take, ...)`; add any trailing arguments the method requires (for example a `CancellationToken`) and keep the rest.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~PeopleServiceTests"`
Expected: build FAILS (types not found).

- [ ] **Step 4: Implement the repository**

`IPeopleRepository.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Repositories;

/// <summary>FaceCount/PhotoCount count Auto and Confirmed faces only.</summary>
public sealed record PersonSummary(int Id, string? Name, int FaceCount, int PhotoCount, int? CoverFaceId);

public sealed record FaceCropSource(
    int FaceId, string ContentHash, string MountPath, string RelativePath, string FileName, string Extension, int? Orientation,
    float X, float Y, float Width, float Height);

public interface IPeopleRepository
{
    /// <summary>Named people (even with no faces) and unnamed groups with at least one face; largest first.</summary>
    Task<IReadOnlyList<PersonSummary>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PersonSummary?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<int?> FindIdByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Sets the name and turns the person's Auto faces into Confirmed. False if the person doesn't exist.</summary>
    Task<bool> SetNameAsync(int id, string name, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Moves every Auto/Confirmed face of source to target as Confirmed, then deletes source.</summary>
    Task MergeAsync(int sourceId, int targetId, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<FaceCropSource?> GetFaceCropSourceAsync(int faceId, CancellationToken cancellationToken = default);
}
```

`PeopleRepository.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class PeopleRepository : IPeopleRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public PeopleRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private IQueryable<PersonSummary> Summaries() =>
        _dbContext.People.AsNoTracking().Select(p => new PersonSummary(
            p.Id,
            p.Name,
            p.Faces.Count(f => f.AssignmentState == FaceAssignmentState.Auto || f.AssignmentState == FaceAssignmentState.Confirmed),
            p.Faces.Where(f => f.AssignmentState == FaceAssignmentState.Auto || f.AssignmentState == FaceAssignmentState.Confirmed)
                .Select(f => f.ImageId).Distinct().Count(),
            p.CoverFaceId));

    public async Task<IReadOnlyList<PersonSummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Summaries()
            .Where(s => s.Name != null || s.FaceCount > 0)
            .OrderByDescending(s => s.FaceCount).ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

    public Task<PersonSummary?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        Summaries().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<int?> FindIdByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var lowered = name.ToLower();
        return await _dbContext.People.AsNoTracking()
            .Where(p => p.Name != null && p.Name.ToLower() == lowered)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> SetNameAsync(int id, string name, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var rows = await _dbContext.People.Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, name).SetProperty(p => p.ModifiedUtc, nowUtc), cancellationToken);
        if (rows == 0)
            return false;

        await _dbContext.Faces.Where(f => f.PersonId == id && f.AssignmentState == FaceAssignmentState.Auto)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.AssignmentState, FaceAssignmentState.Confirmed), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task MergeAsync(int sourceId, int targetId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.Faces
            .Where(f => f.PersonId == sourceId
                        && (f.AssignmentState == FaceAssignmentState.Auto || f.AssignmentState == FaceAssignmentState.Confirmed))
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.PersonId, targetId)
                .SetProperty(f => f.AssignmentState, FaceAssignmentState.Confirmed),
                cancellationToken);
        await _dbContext.People.Where(p => p.Id == targetId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ModifiedUtc, nowUtc), cancellationToken);
        await _dbContext.People.Where(p => p.Id == sourceId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<FaceCropSource?> GetFaceCropSourceAsync(int faceId, CancellationToken cancellationToken = default) =>
        _dbContext.Faces.AsNoTracking()
            .Where(f => f.Id == faceId)
            .Select(f => new FaceCropSource(
                f.Id, f.Image!.ContentHash, f.Image.Folder!.Root!.MountPath, f.Image.Folder.RelativePath,
                f.Image.FileName, f.Image.Extension, f.Image.Orientation, f.X, f.Y, f.Width, f.Height))
            .FirstOrDefaultAsync(cancellationToken);
}
```

- [ ] **Step 5: Implement the services and crop**

`IPeopleService.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Faces;

public interface IPeopleService
{
    Task<IReadOnlyList<PersonSummary>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Result<PersonSummary>> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Names a person or group (trimmed, 1-200 chars). If another person already has that name (case-insensitive),
    /// this one is merged into them and the surviving person is returned.
    /// </summary>
    Task<Result<PersonSummary>> NameAsync(int id, string? name, CancellationToken cancellationToken = default);
}
```

`PeopleService.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Faces;

public sealed class PeopleService : IPeopleService
{
    private const int MaxNameLength = 200;

    private readonly IPeopleRepository _people;
    private readonly IClock _clock;

    public PeopleService(IPeopleRepository people, IClock clock)
    {
        _people = people;
        _clock = clock;
    }

    public Task<IReadOnlyList<PersonSummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _people.GetAllAsync(cancellationToken);

    public async Task<Result<PersonSummary>> GetAsync(int id, CancellationToken cancellationToken = default) =>
        await _people.GetAsync(id, cancellationToken) is { } person ? Result<PersonSummary>.Ok(person) : Result.NotFound();

    public async Task<Result<PersonSummary>> NameAsync(int id, string? name, CancellationToken cancellationToken = default)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return Result.Invalid("name", "Enter a name.");
        if (trimmed.Length > MaxNameLength)
            return Result.Invalid("name", $"Names can be at most {MaxNameLength} characters.");

        if (await _people.GetAsync(id, cancellationToken) is null)
            return Result.NotFound();

        var existingId = await _people.FindIdByNameAsync(trimmed, cancellationToken);
        if (existingId is int targetId && targetId != id)
        {
            await _people.MergeAsync(id, targetId, _clock.UtcNow, cancellationToken);
            return await GetAsync(targetId, cancellationToken);
        }

        await _people.SetNameAsync(id, trimmed, _clock.UtcNow, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }
}
```

`IFaceCropService.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Faces;

public interface IFaceCropService
{
    /// <summary>Path to a cached JPEG crop of the face, created on first request. Null if the face or its image is gone.</summary>
    Task<string?> GetOrCreateCropPathAsync(int faceId, CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Infrastructure/Faces/FaceCropService.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Application.Thumbnails;
using SkiaSharp;

namespace PictureManager.Infrastructure.Faces;

/// <summary>
/// Face crops cut from the cached preview derivative (already orientation-corrected, like the face box) into the
/// thumbnail cache. Face ids are never reused, so a crop never goes stale.
/// </summary>
public sealed class FaceCropService : IFaceCropService
{
    private const int CropSide = 160;
    private const float Padding = 0.3f;

    private readonly IPeopleRepository _people;
    private readonly IThumbnailService _thumbnails;
    private readonly ThumbnailCacheOptions _cacheOptions;

    public FaceCropService(IPeopleRepository people, IThumbnailService thumbnails, ThumbnailCacheOptions cacheOptions)
    {
        _people = people;
        _thumbnails = thumbnails;
        _cacheOptions = cacheOptions;
    }

    public async Task<string?> GetOrCreateCropPathAsync(int faceId, CancellationToken cancellationToken = default)
    {
        var cacheRoot = _cacheOptions.RootPath ?? Path.Combine(Path.GetTempPath(), "picturemanager-cache");
        var cropPath = Path.Combine(cacheRoot, "faces", $"{faceId}.jpg");
        if (File.Exists(cropPath))
            return cropPath;

        var source = await _people.GetFaceCropSourceAsync(faceId, cancellationToken);
        if (source is null)
            return null;

        var physical = ImagePathResolver.ResolvePhysicalPath(source.MountPath, source.RelativePath, source.FileName, source.Extension);
        var previewPath = await _thumbnails.GetOrCreateDerivativePathAsync(
            source.ContentHash, physical, source.Orientation, DerivativeSize.Preview, cancellationToken);
        if (previewPath is null)
            return null;

        using var preview = SKBitmap.Decode(previewPath);
        if (preview is null)
            return null;

        var padX = source.Width * Padding; var padY = source.Height * Padding;
        var rect = SKRectI.Round(new SKRect(
            Math.Max(0, source.X - padX) * preview.Width,
            Math.Max(0, source.Y - padY) * preview.Height,
            Math.Min(1, source.X + source.Width + padX) * preview.Width,
            Math.Min(1, source.Y + source.Height + padY) * preview.Height));
        if (rect.Width <= 0 || rect.Height <= 0)
            return null;

        var scale = Math.Min(1f, (float)CropSide / Math.Max(rect.Width, rect.Height));
        using var crop = new SKBitmap(Math.Max(1, (int)(rect.Width * scale)), Math.Max(1, (int)(rect.Height * scale)));
        using (var canvas = new SKCanvas(crop))
        {
            using var image = SKImage.FromBitmap(preview);
            canvas.DrawImage(image, rect, new SKRect(0, 0, crop.Width, crop.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cropPath)!);
        var tempPath = cropPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var output = File.Create(tempPath))
        {
            using var data = crop.Encode(SKEncodedImageFormat.Jpeg, 85);
            data.SaveTo(output);
        }
        File.Move(tempPath, cropPath, overwrite: true);
        return cropPath;
    }
}
```

- [ ] **Step 6: Add the person filter to image listing**

`ImageQueryModels.cs:19`:

```csharp
/// <summary>AND-combined listing filters. FolderId = images directly in that folder only. PersonId = photos with that person's Auto/Confirmed face.</summary>
public sealed record ImageListFilter(int? FolderId, string? FolderName, string? FileName, bool FavoritesOnly, int? PersonId = null);
```

`ImageDtos.cs` `ImageListRequest`: add `int? PersonId = null` as the last parameter (after `int? Limit = null`).

`ImageQueryService.cs:69`:

```csharp
        var filter = new ImageListFilter(request.FolderId, request.Folder, request.FileName, request.FavoritesOnly, request.PersonId);
```

`ImageQueryRepository.cs`: after the `if (filter.FavoritesOnly)` block add:

```csharp
        if (filter.PersonId is int personId)
            query = query.Where(i => _dbContext.Faces.Any(f => f.ImageId == i.Id && f.PersonId == personId
                && (f.AssignmentState == FaceAssignmentState.Auto || f.AssignmentState == FaceAssignmentState.Confirmed)));
```

(add `using PictureManager.Model;` if not present; use the repository's actual DbContext field name).

`ImageQueryEndpoints.ListAsync`: add `int? personId` after `bool? favoritesOnly`, and pass it as the last `ImageListRequest` argument:

```csharp
        int? folderId, string? folder, string? fileName, bool? favoritesOnly, int? personId, string? sort, string? order, string? cursor, int? limit,
        IImageQueryService service, CancellationToken cancellationToken)
    {
        var request = new ImageListRequest(folderId, folder, fileName, favoritesOnly ?? false, sort, order, cursor, limit, personId);
```

(Update the call in `tests/PictureManager.Api.Tests/Endpoints/ImageQueryEndpointsTests.cs` if it calls `ListAsync` positionally: insert `null` for `personId` after the `favoritesOnly` argument.)

- [ ] **Step 7: Endpoints and registrations**

`PeopleEndpoints.cs`:

```csharp
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;

namespace PictureManager.Api.Endpoints;

public static class PeopleEndpoints
{
    public static IEndpointRouteBuilder MapPeopleEndpoints(this IEndpointRouteBuilder user)
    {
        user.MapGet("/people", GetAllAsync);
        user.MapGet("/people/{id:int}", GetAsync);
        user.MapPatch("/people/{id:int}", NameAsync);
        user.MapGet("/faces/{id:int}/thumbnail", GetFaceThumbnailAsync);
        return user;
    }

    public static async Task<Ok<IReadOnlyList<PersonSummary>>> GetAllAsync(IPeopleService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAllAsync(cancellationToken));

    public static async Task<Results<Ok<PersonSummary>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> GetAsync(
        int id, IPeopleService service, CancellationToken cancellationToken) =>
        (await service.GetAsync(id, cancellationToken)).ToOk();

    public static async Task<Results<Ok<PersonSummary>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> NameAsync(
        int id, PersonNameRequest request, IPeopleService service, CancellationToken cancellationToken) =>
        (await service.NameAsync(id, request.Name, cancellationToken)).ToOk();

    public static async Task<IResult> GetFaceThumbnailAsync(int id, IFaceCropService crops, CancellationToken cancellationToken)
    {
        var path = await crops.GetOrCreateCropPathAsync(id, cancellationToken);
        return path is null ? Results.NotFound() : Results.File(path, "image/jpeg");
    }
}

public sealed record PersonNameRequest(string? Name);
```

(`ImageCacheControlMiddleware` already marks any 200 response whose path ends in `/thumbnail` as immutable.)

Registrations:
- Application: `services.AddScoped<IPeopleService, PeopleService>();`
- Infrastructure: `services.AddScoped<IPeopleRepository, PeopleRepository>();` and `services.AddScoped<IFaceCropService, FaceCropService>();`
- `Program.cs`: after `user.MapDuplicateEndpoints();` add `user.MapPeopleEndpoints();`.

- [ ] **Step 8: Run all backend tests**

Run: `dotnet test`
Expected: PASS (every project).

- [ ] **Step 9: Commit**

```bash
git add src tests
git commit -m "feat: people API with naming, merging, face crops and a person photo filter

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

(`git add src tests` is fine here because this task touches many files under both folders; first run `git status` and confirm nothing unrelated is staged.)

---

### Task 15: Frontend: start, progress and cancel a face job

**Files:**
- Modify: `web/src/api/types.ts`, `web/src/api/jobs.ts`
- Modify: `web/src/tree/FolderJobsContext.tsx`, `web/src/tree/JobStatusBanner.tsx`, `web/src/tree/FolderActionsMenu.tsx`
- Modify: `web/src/test/jobHandlers.ts`
- Test: `web/src/tree/JobStatusBanner.test.tsx` (add tests), `web/src/tree/FolderActions.test.tsx` (add a test)

**Interfaces:**
- Consumes: `POST /api/face-recognitions`, `GET /api/face-recognitions/{id}/events`, `POST /api/jobs/{id}/cancel`, `GET /api/jobs/active` (`kind: 'FaceRecognition'`, `facesFound`).
- Produces:
  - `FaceRecognitionProgress` type.
  - `JobKind` gains `'face-recognitions'`.
  - `startFaceRecognition(folderId: number | null, isRecursive: boolean): Promise<number>` and `cancelJob(id: number): Promise<void>`.
  - `useFolderJobs()` adds `recognizeFaces(folderId: number | null): void` and `cancelActiveJob(): void`.

- [ ] **Step 1: Add the test handlers**

In `web/src/test/jobHandlers.ts`, widen the `activeJob` parameter's `kind` to `'Scan' | 'Discovery' | 'FaceRecognition'`, add `facesFound?: number`, and include `facesFound: 0,` in the default JSON (before the `...dto` spread). Add:

```ts
/** Overrides the face recognition job `id`'s event stream with `events`, sent one SSE message per item. */
export function faceRecognitionEvents(id: number, events: Array<Record<string, unknown>>) {
  return http.get(
    `/api/face-recognitions/${id}/events`,
    () => new HttpResponse(sseStream(events), { headers: { 'Content-Type': 'text/event-stream' } }),
  )
}
```

- [ ] **Step 2: Write the failing tests**

Append inside `describe('JobStatusBanner', ...)` in `JobStatusBanner.test.tsx` (add `fireEvent` to the testing-library import, `http, HttpResponse` from `msw`, and `faceRecognitionEvents` to the jobHandlers import):

```tsx
  it('shows face recognition progress and cancels it', async () => {
    let cancelled = false
    server.use(
      activeJob({ kind: 'FaceRecognition', id: 4, folderId: null, status: 'Enriching', filesFound: 10 }),
      faceRecognitionEvents(4, [
        { Id: 4, Status: 'Enriching', ImagesFound: 10, ImagesProcessed: 3, FacesFound: 7, ErrorMessage: null },
      ]),
      http.post('/api/jobs/4/cancel', () => {
        cancelled = true
        return new HttpResponse(null, { status: 202 })
      }),
    )
    renderApp('/folders/1')

    const banner = await screen.findByRole('alert')
    await waitFor(() => expect(banner).toHaveTextContent('Recognizing faces… 3/10 images, 7 faces'))
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(cancelled).toBe(true))
  })

  it('says it is grouping once every image is processed', async () => {
    server.use(
      activeJob({ kind: 'FaceRecognition', id: 4, folderId: null }),
      faceRecognitionEvents(4, [
        { Id: 4, Status: 'Enriching', ImagesFound: 2, ImagesProcessed: 2, FacesFound: 3, ErrorMessage: null },
      ]),
    )
    renderApp('/folders/1')

    const banner = await screen.findByRole('alert')
    await waitFor(() => expect(banner).toHaveTextContent('Grouping faces…'))
  })

  it('offers no cancel button for a scan', async () => {
    server.use(
      activeJob({ kind: 'Scan', id: 1, folderId: null }),
      scanEvents(1, [
        { Id: 1, Status: 'Enumerating', FoldersScanned: 0, FilesFound: 0, FilesEnriched: 0, ErrorMessage: null },
      ]),
    )
    renderApp('/folders/1')

    await screen.findByRole('alert')
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
  })
```

Append to `FolderActions.test.tsx`, following the existing tests in that file that open a folder's actions menu and click "Scan folder" (reuse their exact menu-opening steps and folder fixture; the assertion is what's new):

```tsx
  it('starts face recognition for the folder and its subfolders', async () => {
    let body: unknown = null
    server.use(
      http.post('/api/face-recognitions', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({ faceRecognitionJobId: 9 })
      }),
      faceRecognitionEvents(9, [
        { Id: 9, Status: 'Completed', ImagesFound: 0, ImagesProcessed: 0, FacesFound: 0, ErrorMessage: null },
      ]),
    )
    // ...open the "Holidays" folder's actions menu exactly as the "Scan folder" test does...
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Recognize faces' }))

    await waitFor(() => expect(body).toEqual({ folderId: 2, isRecursive: true }))
  })
```

- [ ] **Step 3: Run the tests to verify they fail**

Run (from `web`): `npm run test -- src/tree`
Expected: FAIL. The face tests don't find "Recognizing faces…", the Cancel button, or the "Recognize faces" menu item.

- [ ] **Step 4: Implement the API layer**

`api/types.ts`: change `ActiveJobDto.kind` to `'Scan' | 'Discovery' | 'FaceRecognition'`, add `facesFound: number`, and add:

```ts
export type FaceRecognitionProgress = {
  id: number
  status: JobStatus
  imagesFound: number
  imagesProcessed: number
  facesFound: number
  errorMessage: string | null
}
```

`api/jobs.ts`:
- Import `FaceRecognitionProgress`.
- `export type JobKind = 'discoveries' | 'scans' | 'face-recognitions'`.
- `type Progress = DiscoveryProgress | ScanProgress | FaceRecognitionProgress`.
- Add the functions and mapper:

```ts
export function startFaceRecognition(folderId: number | null, isRecursive: boolean): Promise<number> {
  return apiFetch<{ faceRecognitionJobId: number }>('/api/face-recognitions', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(folderId === null ? { isRecursive } : { folderId, isRecursive }),
  }).then((response) => response.faceRecognitionJobId)
}

/** Asks the backend to cancel a running face recognition job; it reports Cancelled on its event stream. */
export function cancelJob(id: number): Promise<void> {
  return apiFetch<void>(`/api/jobs/${id}/cancel`, { method: 'POST' })
}

type RawFaceEvent = {
  Id: number
  Status: JobStatus
  ImagesFound: number
  ImagesProcessed: number
  FacesFound: number
  ErrorMessage: string | null
}

function toFaceProgress(raw: RawFaceEvent): FaceRecognitionProgress {
  return {
    id: raw.Id,
    status: raw.Status,
    imagesFound: raw.ImagesFound,
    imagesProcessed: raw.ImagesProcessed,
    facesFound: raw.FacesFound,
    errorMessage: raw.ErrorMessage,
  }
}
```

In `useJobEvents`, replace the event-mapping expression with:

```ts
          const raw = JSON.parse(dataLine.slice('data: '.length)) as
            RawDiscoveryEvent | RawScanEvent | RawFaceEvent
          const event =
            kind === 'discoveries'
              ? toDiscoveryProgress(raw as RawDiscoveryEvent)
              : kind === 'scans'
                ? toScanProgress(raw as RawScanEvent)
                : toFaceProgress(raw as RawFaceEvent)
```

(If `apiFetch` can't handle a 202 with an empty body, check `api/client.ts`: it already handles 204 for `getActiveJob`. Make it treat any response with `content-length: 0` or no JSON content type as `undefined`, and add a client test for that.)

- [ ] **Step 5: Implement the context, banner and menu item**

`FolderJobsContext.tsx`:
- Import `cancelJob, startFaceRecognition` and `FaceRecognitionProgress`.
- Extend the union with `| { kind: 'face-recognitions'; folderId: number | null; jobId: number; progress: FaceRecognitionProgress | null }`, and add `jobId: number` to the other two variants as well (the banner needs it to cancel).
- Extend `FolderJobs` with `recognizeFaces: (folderId: number | null) => void` and `cancelActiveJob: () => void`.
- In the restore effect, map kinds:

```ts
      const kind: JobKind =
        active.kind === 'Discovery' ? 'discoveries' : active.kind === 'Scan' ? 'scans' : 'face-recognitions'
      setJob({ kind, folderId: active.folderId, jobId: active.id })
```

- `startErrorMessage`'s 409 text becomes `'Another job is already in progress.'`.
- In the terminal-event handler, add `void queryClient.invalidateQueries({ queryKey: ['people'] })` next to the `['folders']` invalidation.
- Change `run` to accept `folderId: number | null`, and add:

```ts
  const recognizeFaces = useCallback(
    (folderId: number | null) =>
      run('face-recognitions', folderId, () => startFaceRecognition(folderId, true)),
    [run],
  )
  const cancelActiveJob = useCallback(() => {
    if (job === null) return
    cancelJob(job.jobId).catch(() => notify("Couldn't cancel the job."))
  }, [job, notify])
```

- Build `activeJob` with a `switch (job.kind)` that returns each variant with `jobId: job.jobId` and the matching progress cast, and pass `recognizeFaces` and `cancelActiveJob` in the provider value.

`JobStatusBanner.tsx`:
- In `progressLabel` add, before the scan branch:

```ts
  if (activeJob.kind === 'face-recognitions') {
    const progress = activeJob.progress
    if (progress !== null && progress.imagesFound > 0 && progress.imagesProcessed >= progress.imagesFound) {
      return 'Grouping faces…'
    }
    return `Recognizing faces… ${progress?.imagesProcessed ?? 0}/${progress?.imagesFound ?? 0} images, ${progress?.facesFound ?? 0} faces`
  }
```

- In `JobStatusBanner`, read `cancelActiveJob` from `useFolderJobs()` and give the `Alert` an action for face jobs only (import `Button` from MUI):

```tsx
      action={
        activeJob.kind === 'face-recognitions' ? (
          <Button color="inherit" size="small" onClick={cancelActiveJob}>
            Cancel
          </Button>
        ) : undefined
      }
```

`FolderActionsMenu.tsx`: read `recognizeFaces` from `useFolderJobs()` and add after the "Scan folder + subfolders" item:

```tsx
        <MenuItem onClick={() => runAction(() => recognizeFaces(folderId))} disabled={scanDisabled}>
          Recognize faces
        </MenuItem>
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `npm run test -- src/tree`
Expected: PASS (existing and new).
Run: `npm run build`
Expected: succeeds with no type errors.

- [ ] **Step 7: Commit**

```bash
git add web/src/api/types.ts web/src/api/jobs.ts web/src/api/client.ts web/src/tree web/src/test/jobHandlers.ts
git commit -m "feat(web): start, follow and cancel face recognition jobs

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 16: Frontend: People page and person view

**Files:**
- Create: `web/src/api/people.ts`, `web/src/people/PeoplePage.tsx`, `web/src/people/PersonView.tsx`, `web/src/test/peopleHandlers.ts`
- Modify: `web/src/api/imageFilter.ts`, `web/src/app/routes.tsx`, `web/src/app/AppShell.tsx`
- Test: `web/src/people/PeoplePage.test.tsx`, `web/src/people/PersonView.test.tsx`, `web/src/api/imageFilter.test.ts` (add a case)

**Interfaces:**
- Consumes: `GET /api/people`, `GET /api/people/{id}`, `PATCH /api/people/{id}`, `GET /api/faces/{id}/thumbnail` and `GET /api/images?personId=`; `useFolderJobs().recognizeFaces` (Task 15).
- Produces:
  - `PersonSummary` type, `usePeople()`, `usePerson(id)` and `useNamePerson()` (mutation `{ id, name }` → `PersonSummary`).
  - Routes `/people` and `/people/:personId`.
  - `ImageFilter` variant `{ kind: 'person'; personId: number }`.

- [ ] **Step 1: Write the failing tests**

`web/src/test/peopleHandlers.ts`:

```ts
import { http, HttpResponse } from 'msw'

export const peopleFixture = [
  { id: 1, name: 'Anna', faceCount: 12, photoCount: 10, coverFaceId: 101 },
  { id: 2, name: null, faceCount: 5, photoCount: 5, coverFaceId: 102 },
]

export function peopleList(people = peopleFixture) {
  return http.get('/api/people', () => HttpResponse.json(people))
}

export function person(dto: (typeof peopleFixture)[number]) {
  return http.get(`/api/people/${dto.id}`, () => HttpResponse.json(dto))
}
```

`web/src/api/imageFilter.test.ts`, add:

```ts
  it('filters by person', () => {
    const params = toImageQuery({ kind: 'person', personId: 7, sort: 'date', order: 'desc' }, null)
    expect(params.get('personId')).toBe('7')
  })
```

`web/src/people/PeoplePage.test.tsx`:

```tsx
import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { peopleList } from '../test/peopleHandlers'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('PeoplePage', () => {
  it('lists named people and unnamed groups in separate sections', async () => {
    server.use(peopleList())
    renderApp('/people')

    expect(await screen.findByRole('heading', { name: 'Named people' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Unnamed groups' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Anna.*10 photos/ })).toHaveAttribute('href', '/people/1')
    expect(screen.getByRole('link', { name: /Unnamed.*5 photos/ })).toHaveAttribute('href', '/people/2')
    expect(screen.getAllByRole('img')[0]).toHaveAttribute('src', '/api/faces/101/thumbnail')
  })

  it('explains what to do when there are no people yet', async () => {
    server.use(peopleList([]))
    renderApp('/people')

    expect(await screen.findByText(/No faces recognized yet/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Recognize faces in all libraries' })).toBeInTheDocument()
  })
})
```

`web/src/people/PersonView.test.tsx`:

```tsx
import { fireEvent, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { peopleFixture, person } from '../test/peopleHandlers'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('PersonView', () => {
  it('names an unnamed group and follows a merge to the surviving person', async () => {
    let sentName: unknown = null
    server.use(
      person(peopleFixture[1]),
      person(peopleFixture[0]),
      http.patch('/api/people/2', async ({ request }) => {
        sentName = ((await request.json()) as { name: string }).name
        return HttpResponse.json(peopleFixture[0])
      }),
    )
    renderApp('/people/2')

    fireEvent.change(await screen.findByLabelText('Name this person'), { target: { value: 'Anna' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save name' }))

    await waitFor(() => expect(sentName).toBe('Anna'))
    expect(await screen.findByDisplayValue('Anna')).toBeInTheDocument()
  })

  it('shows the person photo grid filtered by person', async () => {
    let requested: string | null = null
    server.use(
      person(peopleFixture[0]),
      http.get('/api/images', ({ request }) => {
        requested = new URL(request.url).searchParams.get('personId')
        return HttpResponse.json({ items: [], nextCursor: null })
      }),
    )
    renderApp('/people/1')

    await waitFor(() => expect(requested).toBe('1'))
    expect(await screen.findByText('12 faces · 10 photos')).toBeInTheDocument()
  })
})
```

(If `GET /api/images`'s paged JSON shape differs from `{ items, nextCursor }`, copy it from `pagedImages` in `test/handlers.ts`.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `npm run test -- src/people src/api/imageFilter.test.ts`
Expected: FAIL (modules not found; the `person` kind is not handled).

- [ ] **Step 3: Implement the API hooks and filter**

`api/imageFilter.ts`: add `| { kind: 'person'; personId: number }` to the union and the case:

```ts
    case 'person':
      params.set('personId', String(filter.personId))
      break
```

`api/people.ts`:

```ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from './client'
import { queryKeys } from './queries'

export type PersonSummary = {
  id: number
  name: string | null
  faceCount: number
  photoCount: number
  coverFaceId: number | null
}

export const peopleKeys = {
  all: () => ['people'] as const,
  one: (id: number) => ['people', id] as const,
}

export function faceThumbnailUrl(faceId: number): string {
  return `/api/faces/${faceId}/thumbnail`
}

export function usePeople() {
  return useQuery({
    queryKey: peopleKeys.all(),
    queryFn: () => apiFetch<PersonSummary[]>('/api/people'),
  })
}

export function usePerson(id: number) {
  return useQuery({
    queryKey: peopleKeys.one(id),
    queryFn: () => apiFetch<PersonSummary>(`/api/people/${id}`),
  })
}

/** Names a person; naming one after an existing person merges them, so the response may be a different id. */
export function useNamePerson() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, name }: { id: number; name: string }) =>
      apiFetch<PersonSummary>(`/api/people/${id}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name }),
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: peopleKeys.all() })
      void queryClient.invalidateQueries({ queryKey: queryKeys.imageLists() })
    },
  })
}
```

- [ ] **Step 4: Implement the pages**

`people/PeoplePage.tsx`:

```tsx
import { Box, Button, Typography } from '@mui/material'
import { Link as RouterLink } from 'react-router'
import { faceThumbnailUrl, usePeople, type PersonSummary } from '../api/people'
import { HEADING_SX } from '../design/accent'
import { EmptyMessage } from '../shared/EmptyMessage'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { useFolderJobs } from '../tree/FolderJobsContext'

type PersonCardProps = { person: PersonSummary }

function PersonCard({ person }: PersonCardProps) {
  const label = person.name ?? 'Unnamed'
  return (
    <RouterLink
      to={`/people/${person.id}`}
      aria-label={`${label}, ${person.photoCount} photos`}
      className="flex w-32 flex-col items-center gap-2 rounded-xl p-2 no-underline hover:bg-zinc-100 dark:hover:bg-zinc-800"
    >
      {person.coverFaceId !== null ? (
        <img
          src={faceThumbnailUrl(person.coverFaceId)}
          alt=""
          className="h-24 w-24 rounded-full object-cover"
          loading="lazy"
        />
      ) : (
        <div className="h-24 w-24 rounded-full bg-zinc-200 dark:bg-zinc-700" />
      )}
      <span className="truncate text-sm font-medium text-zinc-900 dark:text-zinc-100">{label}</span>
      <span className="text-xs text-zinc-500">{person.photoCount} photos</span>
    </RouterLink>
  )
}

type SectionProps = { title: string; people: PersonSummary[] }

function Section({ title, people }: SectionProps) {
  if (people.length === 0) return null
  return (
    <Box component="section" sx={{ mb: 4 }}>
      <Typography variant="h6" component="h2" sx={{ ...HEADING_SX, mb: 1 }}>
        {title}
      </Typography>
      <div className="flex flex-wrap gap-2">
        {people.map((person) => (
          <PersonCard key={person.id} person={person} />
        ))}
      </div>
    </Box>
  )
}

export function PeoplePage() {
  const people = usePeople()
  const { activeJob, recognizeFaces } = useFolderJobs()

  return (
    <Box sx={{ p: 3, overflow: 'auto', height: '100%' }}>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 3 }}>
        <Typography variant="h5" component="h1" sx={HEADING_SX}>
          People
        </Typography>
        <Button variant="outlined" disabled={activeJob !== null} onClick={() => recognizeFaces(null)}>
          Recognize faces in all libraries
        </Button>
      </Box>
      {people.isError && <QueryErrorAlert error={people.error} />}
      {people.data?.length === 0 && (
        <EmptyMessage>
          No faces recognized yet. Scan your folders, then run face recognition from a folder&apos;s menu or
          with the button above.
        </EmptyMessage>
      )}
      {people.data && (
        <>
          <Section title="Named people" people={people.data.filter((p) => p.name !== null)} />
          <Section title="Unnamed groups" people={people.data.filter((p) => p.name === null)} />
        </>
      )}
    </Box>
  )
}
```

(The page `h1` is "People", so the named section is titled "Named people" to keep heading names unique. Check `QueryErrorAlert`'s and `EmptyMessage`'s real props in `shared/` and adjust the call sites to match.)

`people/PersonView.tsx`:

```tsx
import { Box, Button, TextField, Typography } from '@mui/material'
import { useState, type FormEvent } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router'
import { useNamePerson, usePerson } from '../api/people'
import { useNotify } from '../app/notify'
import { parseGridParams } from '../routing/urlState'
import { EmptyMessage } from '../shared/EmptyMessage'
import { GridHeader } from '../views/GridHeader'
import { ImageBrowser } from '../views/ImageBrowser'

type NameFormProps = { personId: number; name: string | null }

function NameForm({ personId, name }: NameFormProps) {
  const [draft, setDraft] = useState(name ?? '')
  const namePerson = useNamePerson()
  const navigate = useNavigate()
  const notify = useNotify()

  const submit = (event: FormEvent) => {
    event.preventDefault()
    namePerson.mutate(
      { id: personId, name: draft },
      {
        onSuccess: (survivor) => {
          setDraft(survivor.name ?? '')
          if (survivor.id !== personId) navigate(`/people/${survivor.id}`, { replace: true })
        },
        onError: () => notify("Couldn't save the name."),
      },
    )
  }

  return (
    <form onSubmit={submit} className="flex items-center gap-2">
      <TextField
        size="small"
        label={name === null ? 'Name this person' : 'Name'}
        value={draft}
        onChange={(event) => setDraft(event.target.value)}
      />
      <Button type="submit" variant="contained" disabled={draft.trim() === '' || namePerson.isPending}>
        Save name
      </Button>
    </form>
  )
}

export function PersonView() {
  const params = useParams()
  const personId = Number(params.personId)
  const person = usePerson(personId)
  const [searchParams] = useSearchParams()
  const { sort, order } = parseGridParams(searchParams)

  return (
    <ImageBrowser
      filter={{ kind: 'person', personId, sort, order }}
      header={
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
          {person.data && (
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, px: 2, pt: 2 }}>
              <NameForm key={person.data.id} personId={person.data.id} name={person.data.name} />
              <Typography variant="body2" color="text.secondary">
                {person.data.faceCount} faces · {person.data.photoCount} photos
              </Typography>
            </Box>
          )}
          <GridHeader title={person.data?.name ?? 'Unnamed person'} sort={sort} order={order} />
        </Box>
      }
      emptyState={<EmptyMessage>No photos for this person.</EmptyMessage>}
    />
  )
}
```

(If `ImageBrowser` requires more props than `filter`, `header` and `emptyState`, copy them from `FavoritesView`.)

- [ ] **Step 5: Routes and nav**

`app/routes.tsx`: import both pages and add inside the `AppShell` children, before `'search'`:

```tsx
      { path: 'people', element: <PeoplePage /> },
      { path: 'people/:personId', element: <PersonView /> },
```

`app/AppShell.tsx`: `import PeopleOutlineIcon from '@mui/icons-material/PeopleOutline'` and add to `NAV_ITEMS` after Favorites:

```ts
    { to: '/people', label: 'People', icon: PeopleOutlineIcon },
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `npm run test`
Expected: PASS (all; also confirm `AppShell.test.tsx` still passes with the extra nav item, and update its nav-item expectations if it lists them).
Run: `npm run build`
Expected: succeeds.

- [ ] **Step 7: Commit**

```bash
git add web/src/api/people.ts web/src/api/imageFilter.ts web/src/api/imageFilter.test.ts web/src/people web/src/app/routes.tsx web/src/app/AppShell.tsx web/src/app/AppShell.test.tsx web/src/test/peopleHandlers.ts
git commit -m "feat(web): People page and person view with naming

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 17: Ship the models in the Docker image, and docs

**Files:**
- Modify: `Dockerfile`
- Modify: `Documents/PictureManager-Server-Migration-Guide.md` and the Docker deployment guide (`Documents/PictureManager-Docker-Deployment-Guide.md`, referenced from `docker-compose.prod.yml`)

**Interfaces:**
- Consumes: the zip SHA-256 printed in Task 8, Step 6, and `FaceRecognition:ModelDirectory` (Task 8).
- Produces: an image with `/app/models/buffalo_l/{det_10g.onnx,w600k_r50.onnx}` and `FaceRecognition__ModelDirectory=/app/models/buffalo_l`.

- [ ] **Step 1: Add the models stage**

In `Dockerfile`, after the `ARG NODE_VERSION=22-alpine` line add `ARG BUFFALO_L_SHA256=<the hash printed in Task 8>`. Then add this stage before `FROM mcr.microsoft.com/dotnet/aspnet...`:

```dockerfile
# InsightFace buffalo_l: SCRFD-10G detector + ArcFace R50 recognizer (non-commercial use only).
FROM alpine:3.20 AS models
ARG BUFFALO_L_SHA256
RUN apk add --no-cache curl unzip \
    && curl -fsSL -o /tmp/buffalo_l.zip https://github.com/deepinsight/insightface/releases/download/v0.7/buffalo_l.zip \
    && echo "${BUFFALO_L_SHA256}  /tmp/buffalo_l.zip" | sha256sum -c - \
    && mkdir -p /models/buffalo_l /tmp/buffalo_l \
    && unzip -q /tmp/buffalo_l.zip -d /tmp/buffalo_l \
    && find /tmp/buffalo_l -name det_10g.onnx -exec cp {} /models/buffalo_l/ \; \
    && find /tmp/buffalo_l -name w600k_r50.onnx -exec cp {} /models/buffalo_l/ \; \
    && test -f /models/buffalo_l/det_10g.onnx && test -f /models/buffalo_l/w600k_r50.onnx
```

In the runtime stage, after `COPY --from=web-build ...` add:

```dockerfile
COPY --from=models /models/ ./models/
```

and extend the `ENV` block:

```dockerfile
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    FaceRecognition__ModelDirectory=/app/models/buffalo_l
```

- [ ] **Step 2: Build and smoke-test the container**

```bash
docker compose -f docker-compose.prod.yml up -d --build
docker compose -f docker-compose.prod.yml exec api ls -la /app/models/buffalo_l
```

Expected: the build passes the `sha256sum -c` line, and both `.onnx` files are listed. Then follow the end-to-end checks in Step 4.

- [ ] **Step 3: Update the guides**

In the Docker deployment guide and `Documents/PictureManager-Server-Migration-Guide.md`, add a "Face recognition" section covering:
- The `db` service is now built from `docker/postgres/Dockerfile` (postgres:17-alpine + pgvector). Existing volumes keep working because the OS family and collation are unchanged. A migration between servers still needs the same Postgres major version **and** pgvector installed on the target, before restoring a dump that contains `vector` columns.
- The InsightFace `buffalo_l` weights are for non-commercial use only.
- Face embeddings are biometric data. Treat database backups as sensitive. Nothing is sent off the server.
- Tuning: `FaceRecognition__ReadConcurrency` (NAS reads) and `FaceRecognition__InferenceConcurrency` (CPU), plus the matching and grouping thresholds and their defaults (copy them from `FaceRecognitionOptions`).
- Usage: scan first, then use "Recognize faces" from a folder's menu (or the People page for everything). A cancelled or interrupted job resumes where it stopped when started again.

- [ ] **Step 4: End-to-end verification**

With the stack running:
1. Scan a small folder of photos with people in it, then use "Recognize faces" from its menu. The banner shows "Recognizing faces… n/N images, m faces", then "Grouping faces…", then disappears.
2. Start "Recognize faces" on a large folder and press **Cancel** midway. The banner disappears (job Cancelled). Run it again: the banner's image total N is smaller (only the remainder).
3. While a face job runs, try "Scan folder". You get the "Another job is already in progress." notification (409).
4. On `/people`, open an unnamed group, name it "Anna" and save. It moves to Named people. Open a second group of the same person and name it "anna": you land on Anna's page and the group is gone (merged).
5. On Anna's page, the grid shows only photos with Anna.
6. Run `curl http://localhost:5080/api/face-recognitions/failures`. It returns a JSON list (possibly empty).

- [ ] **Step 5: Commit**

```bash
git add Dockerfile Documents/PictureManager-Server-Migration-Guide.md Documents/PictureManager-Docker-Deployment-Guide.md
git commit -m "build: ship face models in the image; document face recognition

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

## Self-review notes (done while writing)

- **Spec coverage:**
  - Three job kinds sharing the single-job rule: Tasks 2 and 10.
  - Scanned images only: Task 9 query, Task 10.
  - Cancellation: Tasks 1, 2, 10 and 11.
  - FaceModel / FaceProcessingState / Face / Person schema: Task 5.
  - pgvector on Alpine: Task 4.
  - Analyzer with quality score: Tasks 6–8.
  - Retries and permanent failures: Tasks 9, 10 and 13.
  - Clustering (match, DBSCAN, cleanup): Task 12.
  - People API, naming and merge, crops, person filter: Task 14.
  - UI banner, cancel and menu: Task 15. People UI: Task 16.
  - Docker models and docs: Task 17.
- **Type consistency:**
  - `QueuedFaceRecognition(JobId, FolderId, IsRecursive)` is the same in Tasks 10 and 11.
  - `FaceImageResult` / `FaceImageOutcome` are defined in Task 10 and used only there.
  - `IFaceRepository` is extended in Task 12 only (additive).
  - `ActiveJobDto` gains `FacesFound` last, in Task 13; the Task 2 test is updated in Task 13.
  - `ImageListFilter` and `ImageListRequest` gain `PersonId` as an optional last parameter, so existing call sites compile.
- **Known judgment calls an executor should know about:**
  - Task 14 Step 2 asks you to align one test call with `ImageQueryRepository.ListAsync`'s real signature.
  - Task 16 relies on shared components whose exact props you must check.
  - In both places the plan names the file to copy from.
