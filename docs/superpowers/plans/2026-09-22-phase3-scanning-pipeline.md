# Phase 3 — Scanning Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give PictureManager a working two-phase scanner — enumerate (walk an `ImageRoot`, reconcile `Folder`/`Image`
rows against the filesystem) and enrich (extract EXIF, compute a partial content hash) — with `ScanJob` progress
tracking, a minimal trigger + SSE endpoint, and the first real Postgres database behind it.

**Architecture:** `PictureManager.Application.Scanning.ScanService` orchestrates enumeration using only
Application-level abstractions (repositories, `IEnrichmentQueue`). New `Image`/`ExifData` rows are enriched by
`PictureManager.Worker.EnrichmentBackgroundService`, a long-running `BackgroundService` that drains a
`Channel`-backed `IEnrichmentQueue` and calls `PictureManager.Application.Scanning.ImageEnrichmentService`
per item. Filesystem-touching primitives (content hashing, EXIF reading) are Infrastructure implementations of
Application-defined interfaces — the same seam already used for repositories. `PictureManager.Api` exposes
`POST /api/scans` and `GET /api/scans/{id}/events` (SSE).

**Tech Stack:** `System.IO.Hashing` (XxHash64) for the partial content hash, `MetadataExtractor` for EXIF,
`System.Threading.Channels` for the enrichment queue — all new this phase. EF Core + Npgsql (existing), now
against a real database for the first time.

**Spec:** [`docs/superpowers/specs/2026-09-21-picturemanager-v1-design.md`](../specs/2026-09-21-picturemanager-v1-design.md)
(phase 3 scope, `ScanJob`/`AppSettings` decisions), and the source brief's Scanning pipeline, Folder management,
and "Known gotchas" sections in [`Documents/PictureManager-brief.md`](../../../Documents/PictureManager-brief.md).

## Global Constraints

- Target framework: `net10.0` everywhere on the backend (unchanged).
- **FluentAssertions pinned to `[7.0.0,8.0.0)`**, **NSubstitute** for mocking (not Moq) — unchanged.
- **First live-database phase.** The phase-1 `db` container is started for real (`docker compose up -d db`) and
  `dotnet ef database update` applies phase 2's `InitialCreate` migration before Task 11's integration/smoke
  checks. Tasks 1–10's automated tests still run against EF Core's InMemory provider (fast, deterministic,
  no live DB needed for TDD) — only Task 11 exercises real Postgres.
- **Case-insensitive path/filename comparisons use `.ToLower()` inside repository LINQ queries** (translates to
  SQL `lower(...)` on Postgres, runs as plain LINQ-to-Objects on InMemory — identical behavior on both
  providers), not a DB-level computed column or unique constraint. Concurrency-safe uniqueness enforcement is
  deliberately deferred: v1 has no concurrent-scan support (scans and enrichment are effectively sequential),
  so a check-then-act repository pattern is sufficient for now. Revisit if/when concurrent scans are supported.
- **File extensions are always stored lowercase**, normalized at write time in `ScanService` — so `Extension`
  participates directly in lookups/indexes without its own normalization step.
- **Filesystem-touching library integrations live in `PictureManager.Infrastructure`**, behind
  Application-defined interfaces — same pattern as repositories, per the brief's stated layering (Infrastructure
  owns filesystem/EXIF/imaging concerns). Plain `System.IO` directory-walking (no third-party library) stays
  directly in `PictureManager.Application.Scanning.ScanService` — a BCL primitive needs no abstraction, and its
  tests use real temporary directories rather than a fake filesystem.
- **New packages this phase** (installed via `dotnet add package <name>`, no pinned version — same style as
  phase 2's `Microsoft.EntityFrameworkCore.InMemory` addition; record whatever version resolves in the commit):
  `System.IO.Hashing` (Infrastructure), `MetadataExtractor` (Infrastructure).
- **`PictureManager.Worker` already exists** (empty project scaffolded in phase 1, referenced by
  `PictureManager.Api`, itself referencing `PictureManager.Application`) — no new project needed this phase.
- **Reconciliation rules** (brief): same path+size+mtime → skip; same path+different size or mtime → re-extract;
  file not observed this scan → `MissingSinceUtc` set (never deleted by a scan); a newly-enriched file whose
  content hash matches an existing row that is currently missing is treated as "moved" — the stale missing row
  is deleted (its `AlbumImage` rows cascade-delete with it). **Documented v1 limitation**: an image's album
  memberships do not survive being detected as moved — the newly-enriched row is the one that's kept, not the
  old row with the album links. Acceptable per YAGNI; revisit only if it causes real friction.

---

## Task 1: Repository extensions for scanning

**Files:**
- Create: `src/PictureManager.Application/Repositories/IScanJobRepository.cs`
- Create: `src/PictureManager.Application/Repositories/IAppSettingsRepository.cs`
- Modify: `src/PictureManager.Application/Repositories/IFolderRepository.cs`
- Modify: `src/PictureManager.Application/Repositories/IImageRepository.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Repositories/ScanJobRepository.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Repositories/AppSettingsRepository.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/FolderRepository.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/ImageRepository.cs`
- Modify: `src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ScanJobRepositoryTests.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AppSettingsRepositoryTests.cs`
- Modify: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FolderRepositoryTests.cs`
- Modify: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ImageRepositoryTests.cs`
- Modify: `tests/PictureManager.Infrastructure.Tests/DependencyInjection/InfrastructureServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes: `PictureManagerDbContext` (phase 2), `AppSettingsConfiguration.SingletonId` (phase 2).
- Produces: `IScanJobRepository`, `IAppSettingsRepository`, plus the new `IFolderRepository`/`IImageRepository`
  members below — everything Task 8 (`ScanService`) and Task 6 (`ImageEnrichmentService`) call.
  - `IFolderRepository.GetByRootAndRelativePathAsync(int rootId, string relativePath, CancellationToken)`
  - `IFolderRepository.UpdateAsync(Folder folder, CancellationToken)`
  - `IImageRepository.GetByFolderAndFileNameAsync(int folderId, string fileName, string extension, CancellationToken)`
  - `IImageRepository.GetByIdWithFolderAsync(int id, CancellationToken)` — includes `Folder.Root`
  - `IImageRepository.GetByContentHashAsync(string contentHash, CancellationToken)`
  - `IImageRepository.UpdateAsync(Image image, CancellationToken)`
  - `IImageRepository.DeleteAsync(Image image, CancellationToken)`

- [ ] **Step 1: Write the failing interface + repository tests together**

`src/PictureManager.Application/Repositories/IScanJobRepository.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IScanJobRepository
{
    Task<ScanJob?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ScanJob> AddAsync(ScanJob scanJob, CancellationToken cancellationToken = default);
    Task UpdateAsync(ScanJob scanJob, CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Application/Repositories/IAppSettingsRepository.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IAppSettingsRepository
{
    Task<AppSettings> GetAsync(CancellationToken cancellationToken = default);
}
```

Add to `src/PictureManager.Application/Repositories/IFolderRepository.cs` (inside the existing interface):

```csharp
    Task<Folder?> GetByRootAndRelativePathAsync(int rootId, string relativePath, CancellationToken cancellationToken = default);
    Task UpdateAsync(Folder folder, CancellationToken cancellationToken = default);
```

Add to `src/PictureManager.Application/Repositories/IImageRepository.cs` (inside the existing interface):

```csharp
    Task<Image?> GetByFolderAndFileNameAsync(int folderId, string fileName, string extension, CancellationToken cancellationToken = default);
    Task<Image?> GetByIdWithFolderAsync(int id, CancellationToken cancellationToken = default);
    Task<Image?> GetByContentHashAsync(string contentHash, CancellationToken cancellationToken = default);
    Task UpdateAsync(Image image, CancellationToken cancellationToken = default);
    Task DeleteAsync(Image image, CancellationToken cancellationToken = default);
```

`tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ScanJobRepositoryTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class ScanJobRepositoryTests
{
    private static PictureManagerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task AddAsync_PersistsScanJob_AndGetByIdAsync_ReturnsIt()
    {
        await using var context = CreateContext();
        var repository = new ScanJobRepository(context);

        var scanJob = new ScanJob { IsRecursive = true, Status = ScanJobStatus.Pending, StartedUtc = DateTime.UtcNow };

        var added = await repository.AddAsync(scanJob);
        var fetched = await repository.GetByIdAsync(added.Id);

        fetched.Should().NotBeNull();
        fetched!.Status.Should().Be(ScanJobStatus.Pending);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChangesToExistingScanJob()
    {
        await using var context = CreateContext();
        var repository = new ScanJobRepository(context);

        var scanJob = await repository.AddAsync(new ScanJob
        {
            IsRecursive = false,
            Status = ScanJobStatus.Pending,
            StartedUtc = DateTime.UtcNow
        });

        scanJob.Status = ScanJobStatus.Completed;
        scanJob.FilesFound = 10;
        scanJob.CompletedUtc = DateTime.UtcNow;
        await repository.UpdateAsync(scanJob);

        var fetched = await repository.GetByIdAsync(scanJob.Id);
        fetched!.Status.Should().Be(ScanJobStatus.Completed);
        fetched.FilesFound.Should().Be(10);
    }
}
```

`tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AppSettingsRepositoryTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Infrastructure.Persistence.Configurations;
using PictureManager.Infrastructure.Persistence.Repositories;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class AppSettingsRepositoryTests
{
    [Fact]
    public async Task GetAsync_ReturnsSeededSingletonSettings()
    {
        var options = new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new PictureManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var repository = new AppSettingsRepository(context);
        var settings = await repository.GetAsync();

        settings.Id.Should().Be(AppSettingsConfiguration.SingletonId);
        settings.ExcludedExtensions.Should().Contain(".heic");
    }
}
```

Add to `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FolderRepositoryTests.cs` (inside the
existing `FolderRepositoryTests` class):

```csharp
    [Fact]
    public async Task GetByRootAndRelativePathAsync_IsCaseInsensitive()
    {
        await using var context = CreateContext();
        var root = new ImageRoot { Name = "root", MountPath = "/images", CreatedUtc = DateTime.UtcNow };
        var folder = new Folder { Name = "Vacation", RelativePath = "Vacation", Root = root, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        context.AddRange(root, folder);
        await context.SaveChangesAsync();

        var repository = new FolderRepository(context);
        var found = await repository.GetByRootAndRelativePathAsync(root.Id, "vacation");

        found.Should().NotBeNull();
        found!.Id.Should().Be(folder.Id);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChangesToExistingFolder()
    {
        await using var context = CreateContext();
        var root = new ImageRoot { Name = "root", MountPath = "/images", CreatedUtc = DateTime.UtcNow };
        var folder = new Folder { Name = "Vacation", RelativePath = "Vacation", Root = root, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        context.AddRange(root, folder);
        await context.SaveChangesAsync();

        var repository = new FolderRepository(context);
        folder.ModifiedUtc = DateTime.UtcNow.AddMinutes(5);
        await repository.UpdateAsync(folder);

        var fetched = await repository.GetByIdAsync(folder.Id);
        fetched!.ModifiedUtc.Should().Be(folder.ModifiedUtc);
    }
```

Add to `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ImageRepositoryTests.cs` (inside the
existing `ImageRepositoryTests` class):

```csharp
    [Fact]
    public async Task GetByFolderAndFileNameAsync_IsCaseInsensitive()
    {
        await using var context = CreateContext();
        var folder = await SeedFolderAsync(context);
        context.Images.Add(new Image
        {
            FolderId = folder.Id, FileName = "IMG001", Extension = ".jpg", ContentHash = "h1",
            FileSize = 1, FileModified = DateTime.UtcNow, FirstSeenUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var repository = new ImageRepository(context);
        var found = await repository.GetByFolderAndFileNameAsync(folder.Id, "img001", ".JPG");

        found.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByContentHashAsync_ReturnsMatchingImage()
    {
        await using var context = CreateContext();
        var folder = await SeedFolderAsync(context);
        context.Images.Add(new Image
        {
            FolderId = folder.Id, FileName = "IMG001", Extension = ".jpg", ContentHash = "abc123",
            FileSize = 1, FileModified = DateTime.UtcNow, FirstSeenUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var repository = new ImageRepository(context);
        var found = await repository.GetByContentHashAsync("abc123");

        found.Should().NotBeNull();
        found!.FileName.Should().Be("IMG001");
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges_AndDeleteAsync_RemovesRow()
    {
        await using var context = CreateContext();
        var folder = await SeedFolderAsync(context);
        var image = new Image
        {
            FolderId = folder.Id, FileName = "IMG001", Extension = ".jpg", ContentHash = "h1",
            FileSize = 1, FileModified = DateTime.UtcNow, FirstSeenUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        context.Images.Add(image);
        await context.SaveChangesAsync();

        var repository = new ImageRepository(context);
        image.IsFavorite = true;
        await repository.UpdateAsync(image);
        (await repository.GetByIdAsync(image.Id))!.IsFavorite.Should().BeTrue();

        await repository.DeleteAsync(image);
        (await repository.GetByIdAsync(image.Id)).Should().BeNull();
    }
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests`
Expected: FAIL — new repository types and interface members don't exist yet.

- [ ] **Step 3: Implement the interfaces and repositories**

Full replacement `src/PictureManager.Application/Repositories/IFolderRepository.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IFolderRepository
{
    Task<Folder?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Folder>> GetChildrenAsync(int? parentId, CancellationToken cancellationToken = default);
    Task<Folder> AddAsync(Folder folder, CancellationToken cancellationToken = default);
    Task<Folder?> GetByRootAndRelativePathAsync(int rootId, string relativePath, CancellationToken cancellationToken = default);
    Task UpdateAsync(Folder folder, CancellationToken cancellationToken = default);
}
```

Full replacement `src/PictureManager.Application/Repositories/IImageRepository.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IImageRepository
{
    Task<Image?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Image>> GetByFolderIdAsync(int folderId, CancellationToken cancellationToken = default);
    Task<Image> AddAsync(Image image, CancellationToken cancellationToken = default);
    Task<Image?> GetByFolderAndFileNameAsync(int folderId, string fileName, string extension, CancellationToken cancellationToken = default);
    Task<Image?> GetByIdWithFolderAsync(int id, CancellationToken cancellationToken = default);
    Task<Image?> GetByContentHashAsync(string contentHash, CancellationToken cancellationToken = default);
    Task UpdateAsync(Image image, CancellationToken cancellationToken = default);
    Task DeleteAsync(Image image, CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Infrastructure/Persistence/Repositories/ScanJobRepository.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class ScanJobRepository : IScanJobRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public ScanJobRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ScanJob?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ScanJobs.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<ScanJob> AddAsync(ScanJob scanJob, CancellationToken cancellationToken = default)
    {
        _dbContext.ScanJobs.Add(scanJob);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return scanJob;
    }

    public async Task UpdateAsync(ScanJob scanJob, CancellationToken cancellationToken = default)
    {
        _dbContext.ScanJobs.Update(scanJob);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

`src/PictureManager.Infrastructure/Persistence/Repositories/AppSettingsRepository.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Infrastructure.Persistence.Configurations;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class AppSettingsRepository : IAppSettingsRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public AppSettingsRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AppSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Settings.SingleAsync(s => s.Id == AppSettingsConfiguration.SingletonId, cancellationToken);
    }
}
```

Add to `src/PictureManager.Infrastructure/Persistence/Repositories/FolderRepository.cs` (inside the class):

```csharp
    public async Task<Folder?> GetByRootAndRelativePathAsync(int rootId, string relativePath, CancellationToken cancellationToken = default)
    {
        var normalized = relativePath.ToLowerInvariant();
        return await _dbContext.Folders.FirstOrDefaultAsync(
            f => f.RootId == rootId && f.RelativePath.ToLower() == normalized, cancellationToken);
    }

    public async Task UpdateAsync(Folder folder, CancellationToken cancellationToken = default)
    {
        _dbContext.Folders.Update(folder);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
```

Add to `src/PictureManager.Infrastructure/Persistence/Repositories/ImageRepository.cs` (inside the class):

```csharp
    public async Task<Image?> GetByFolderAndFileNameAsync(int folderId, string fileName, string extension, CancellationToken cancellationToken = default)
    {
        var normalizedName = fileName.ToLowerInvariant();
        var normalizedExtension = extension.ToLowerInvariant();
        return await _dbContext.Images.FirstOrDefaultAsync(
            i => i.FolderId == folderId && i.FileName.ToLower() == normalizedName && i.Extension.ToLower() == normalizedExtension,
            cancellationToken);
    }

    public async Task<Image?> GetByIdWithFolderAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Images
            .Include(i => i.Folder).ThenInclude(f => f!.Root)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public async Task<Image?> GetByContentHashAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Images.FirstOrDefaultAsync(i => i.ContentHash == contentHash, cancellationToken);
    }

    public async Task UpdateAsync(Image image, CancellationToken cancellationToken = default)
    {
        _dbContext.Images.Update(image);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Image image, CancellationToken cancellationToken = default)
    {
        _dbContext.Images.Remove(image);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
```

- [ ] **Step 4: Register the new repositories in DI**

In `src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`, add
after the existing five `AddScoped` repository lines:

```csharp
        services.AddScoped<IScanJobRepository, ScanJobRepository>();
        services.AddScoped<IAppSettingsRepository, AppSettingsRepository>();
```

- [ ] **Step 5: Extend the DI registration test**

Add to `tests/PictureManager.Infrastructure.Tests/DependencyInjection/InfrastructureServiceCollectionExtensionsTests.cs`
(inside the existing test class):

```csharp
    [Fact]
    public void AddInfrastructure_RegistersScanJobAndAppSettingsRepositories()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PictureManagerDb"] =
                    "Host=localhost;Database=picturemanager;Username=test;Password=test"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        var provider = services.BuildServiceProvider();

        provider.GetService<IScanJobRepository>().Should().NotBeNull();
        provider.GetService<IAppSettingsRepository>().Should().NotBeNull();
    }
```

- [ ] **Step 6: Run the full Infrastructure test suite**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests`
Expected: PASS, all tests.

- [ ] **Step 7: Commit**

```bash
git add src/PictureManager.Application src/PictureManager.Infrastructure tests/PictureManager.Infrastructure.Tests
git commit -m "feat: add ScanJob/AppSettings repositories and scanning-support repo methods"
```

---

## Task 2: Path normalization and physical path resolution

**Files:**
- Create: `src/PictureManager.Application/Scanning/PathNormalizer.cs`
- Create: `src/PictureManager.Application/Scanning/ImagePathResolver.cs`
- Test: `tests/PictureManager.Application.Tests/Scanning/PathNormalizerTests.cs`
- Test: `tests/PictureManager.Application.Tests/Scanning/ImagePathResolverTests.cs`

**Interfaces:**
- Consumes: nothing (pure, no dependencies).
- Produces: `PathNormalizer.Normalize(string)`, `PathNormalizer.Combine(string basePath, string segment)`,
  `PathNormalizer.NormalizedEquals(string?, string?)`, `ImagePathResolver.ResolvePhysicalPath(string mountPath,
  string relativeFolderPath, string fileName, string extension)` — used by Task 8 (`ScanService`) and Task 6
  (`ImageEnrichmentService`).

- [ ] **Step 1: Write the failing tests**

`tests/PictureManager.Application.Tests/Scanning/PathNormalizerTests.cs`:

```csharp
using FluentAssertions;
using PictureManager.Application.Scanning;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public class PathNormalizerTests
{
    [Fact]
    public void Normalize_ConvertsToNfc()
    {
        var decomposed = "é"; // "e" + combining acute accent
        var result = PathNormalizer.Normalize(decomposed);
        result.Should().Be("é"); // precomposed "é"
    }

    [Fact]
    public void Combine_WithEmptyBase_ReturnsJustTheSegment()
    {
        PathNormalizer.Combine(string.Empty, "Vacation").Should().Be("Vacation");
    }

    [Fact]
    public void Combine_WithNonEmptyBase_JoinsWithForwardSlash()
    {
        PathNormalizer.Combine("Vacation", "Madeira").Should().Be("Vacation/Madeira");
    }

    [Theory]
    [InlineData("Vacation", "vacation", true)]
    [InlineData("Vacation", "Vacation ", false)]
    [InlineData(null, "", true)]
    public void NormalizedEquals_IsCaseInsensitive(string? a, string? b, bool expected)
    {
        PathNormalizer.NormalizedEquals(a, b).Should().Be(expected);
    }
}
```

`tests/PictureManager.Application.Tests/Scanning/ImagePathResolverTests.cs`:

```csharp
using System.IO;
using FluentAssertions;
using PictureManager.Application.Scanning;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public class ImagePathResolverTests
{
    [Fact]
    public void ResolvePhysicalPath_AtRoot_CombinesMountAndFileName()
    {
        var result = ImagePathResolver.ResolvePhysicalPath("/images", string.Empty, "IMG001", ".jpg");
        result.Should().Be(Path.Combine("/images", "IMG001.jpg"));
    }

    [Fact]
    public void ResolvePhysicalPath_InSubfolder_CombinesAllSegments()
    {
        var result = ImagePathResolver.ResolvePhysicalPath("/images", "Vacation/Madeira", "IMG001", ".jpg");
        result.Should().Be(Path.Combine("/images", "Vacation", "Madeira", "IMG001.jpg"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/PictureManager.Application.Tests`
Expected: FAIL — `PathNormalizer`/`ImagePathResolver` don't exist yet.

- [ ] **Step 3: Implement**

`src/PictureManager.Application/Scanning/PathNormalizer.cs`:

```csharp
using System;
using System.Text;

namespace PictureManager.Application.Scanning;

public static class PathNormalizer
{
    public static string Normalize(string value) => value.Normalize(NormalizationForm.FormC);

    public static string Combine(string basePath, string segment)
    {
        var normalizedSegment = Normalize(segment);
        return string.IsNullOrEmpty(basePath) ? normalizedSegment : $"{basePath}/{normalizedSegment}";
    }

    public static bool NormalizedEquals(string? a, string? b) =>
        string.Equals(Normalize(a ?? string.Empty), Normalize(b ?? string.Empty), StringComparison.OrdinalIgnoreCase);
}
```

`src/PictureManager.Application/Scanning/ImagePathResolver.cs`:

```csharp
using System.IO;

namespace PictureManager.Application.Scanning;

public static class ImagePathResolver
{
    public static string ResolvePhysicalPath(string mountPath, string relativeFolderPath, string fileName, string extension)
    {
        var fullFileName = fileName + extension;
        return string.IsNullOrEmpty(relativeFolderPath)
            ? Path.Combine(mountPath, fullFileName)
            : Path.Combine(mountPath, relativeFolderPath.Replace('/', Path.DirectorySeparatorChar), fullFileName);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Application.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PictureManager.Application/Scanning tests/PictureManager.Application.Tests/Scanning
git commit -m "feat: add path normalization and physical path resolution"
```

---

## Task 3: Content hasher (xxHash partial hash)

**Files:**
- Create: `src/PictureManager.Application/Scanning/IContentHasher.cs`
- Create: `src/PictureManager.Infrastructure/Scanning/XxHashContentHasher.cs`
- Modify: `src/PictureManager.Infrastructure/PictureManager.Infrastructure.csproj`
- Modify: `src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Scanning/XxHashContentHasherTests.cs`
- Modify: `tests/PictureManager.Infrastructure.Tests/DependencyInjection/InfrastructureServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes: nothing beyond the file path/size it's given.
- Produces: `IContentHasher.ComputeAsync(string filePath, long fileSize, CancellationToken)` → hex string. Used
  by Task 6 (`ImageEnrichmentService`).

- [ ] **Step 1: Add the package**

```bash
dotnet add src/PictureManager.Infrastructure package System.IO.Hashing
```

- [ ] **Step 2: Write the failing tests**

`tests/PictureManager.Infrastructure.Tests/Scanning/XxHashContentHasherTests.cs`:

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Infrastructure.Scanning;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Scanning;

public class XxHashContentHasherTests
{
    [Fact]
    public async Task ComputeAsync_SameContent_ProducesSameHash()
    {
        var path = Path.GetTempFileName();
        try
        {
            var bytes = new byte[10_000];
            new Random(42).NextBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes);

            var hasher = new XxHashContentHasher();
            var hash1 = await hasher.ComputeAsync(path, bytes.Length);
            var hash2 = await hasher.ComputeAsync(path, bytes.Length);

            hash1.Should().Be(hash2);
            hash1.Should().NotBeNullOrEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ComputeAsync_DifferentContent_ProducesDifferentHash()
    {
        var pathA = Path.GetTempFileName();
        var pathB = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(pathA, new byte[] { 1, 2, 3, 4, 5 });
            await File.WriteAllBytesAsync(pathB, new byte[] { 9, 9, 9, 9, 9 });

            var hasher = new XxHashContentHasher();
            var hashA = await hasher.ComputeAsync(pathA, 5);
            var hashB = await hasher.ComputeAsync(pathB, 5);

            hashA.Should().NotBe(hashB);
        }
        finally
        {
            File.Delete(pathA);
            File.Delete(pathB);
        }
    }

    [Fact]
    public async Task ComputeAsync_FileLargerThan128KB_OnlyReadsHeadAndTailSamples()
    {
        var path = Path.GetTempFileName();
        try
        {
            var bytes = new byte[200_000];
            new Random(7).NextBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes);

            var hasher = new XxHashContentHasher();
            var hash = await hasher.ComputeAsync(path, bytes.Length);

            hash.Should().NotBeNullOrEmpty();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail to compile**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter XxHashContentHasherTests`
Expected: FAIL — `IContentHasher`/`XxHashContentHasher` don't exist yet.

- [ ] **Step 4: Implement**

`src/PictureManager.Application/Scanning/IContentHasher.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Scanning;

public interface IContentHasher
{
    Task<string> ComputeAsync(string filePath, long fileSize, CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Infrastructure/Scanning/XxHashContentHasher.cs`:

```csharp
using System;
using System.IO;
using System.IO.Hashing;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Scanning;

namespace PictureManager.Infrastructure.Scanning;

public sealed class XxHashContentHasher : IContentHasher
{
    private const int SampleSize = 64 * 1024;

    public async Task<string> ComputeAsync(string filePath, long fileSize, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);

        var hasher = new XxHash64();
        hasher.Append(BitConverter.GetBytes(fileSize));

        var headSize = (int)Math.Min(SampleSize, fileSize);
        var headBuffer = new byte[headSize];
        await ReadExactAsync(stream, headBuffer, cancellationToken);
        hasher.Append(headBuffer);

        if (fileSize > SampleSize)
        {
            var tailSize = (int)Math.Min(SampleSize, fileSize - headSize);
            stream.Seek(-tailSize, SeekOrigin.End);
            var tailBuffer = new byte[tailSize];
            await ReadExactAsync(stream, tailBuffer, cancellationToken);
            hasher.Append(tailBuffer);
        }

        return Convert.ToHexString(hasher.GetCurrentHash());
    }

    private static async Task ReadExactAsync(FileStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
                break;
            offset += read;
        }
    }
}
```

- [ ] **Step 5: Register in DI**

Add to `InfrastructureServiceCollectionExtensions.AddInfrastructure`:

```csharp
        services.AddSingleton<IContentHasher, XxHashContentHasher>();
```

- [ ] **Step 6: Extend the DI test, then run and commit**

Add to `InfrastructureServiceCollectionExtensionsTests`:

```csharp
    [Fact]
    public void AddInfrastructure_RegistersContentHasher()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PictureManagerDb"] =
                    "Host=localhost;Database=picturemanager;Username=test;Password=test"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        var provider = services.BuildServiceProvider();

        provider.GetService<IContentHasher>().Should().NotBeNull();
    }
```

Run: `dotnet test tests/PictureManager.Infrastructure.Tests`
Expected: PASS, all tests.

```bash
git add src/PictureManager.Application/Scanning/IContentHasher.cs src/PictureManager.Infrastructure tests/PictureManager.Infrastructure.Tests
git commit -m "feat: add xxHash partial-hash content hasher"
```

---

## Task 4: EXIF reader

**Files:**
- Create: `src/PictureManager.Application/Scanning/ExifData.cs`
- Create: `src/PictureManager.Application/Scanning/IExifReader.cs`
- Create: `src/PictureManager.Infrastructure/Scanning/MetadataExtractorExifReader.cs`
- Modify: `src/PictureManager.Infrastructure/PictureManager.Infrastructure.csproj`
- Modify: `src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Scanning/MetadataExtractorExifReaderTests.cs`
- Modify: `tests/PictureManager.Infrastructure.Tests/DependencyInjection/InfrastructureServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes: nothing beyond the file path it's given.
- Produces: `ExifData` record, `IExifReader.ReadAsync(string filePath, CancellationToken)`. Used by Task 6
  (`ImageEnrichmentService`).

**Note on test coverage:** hand-crafting a binary JPEG with real EXIF tags for a unit test fixture is
impractical to author correctly inline. This task's automated tests verify the reader's *contract* (returns
image dimensions when available, degrades to `ExifData.Empty` instead of throwing on unreadable/non-image
input) using a tiny well-known valid JPEG with no EXIF segment. **Exact EXIF tag extraction (camera make/model,
GPS, date taken) is verified manually in Task 11's smoke test against real photos**, not by an automated unit
test — this mirrors phase 2's own deferral of Postgres-specific verification to this phase.

- [ ] **Step 1: Add the package**

```bash
dotnet add src/PictureManager.Infrastructure package MetadataExtractor
```

- [ ] **Step 2: Write the failing tests**

`tests/PictureManager.Infrastructure.Tests/Scanning/MetadataExtractorExifReaderTests.cs`:

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Infrastructure.Scanning;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Scanning;

public class MetadataExtractorExifReaderTests
{
    // Smallest known-valid 1x1 white-pixel JPEG, no EXIF segment.
    private const string MinimalJpegBase64 =
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAj/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAAX/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCdABmX/9k=";

    [Fact]
    public async Task ReadAsync_ValidJpegWithoutExif_ReturnsDimensions_AndNullExifFields()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, Convert.FromBase64String(MinimalJpegBase64));

            var reader = new MetadataExtractorExifReader();
            var result = await reader.ReadAsync(path);

            result.Width.Should().Be(1);
            result.Height.Should().Be(1);
            result.CameraMake.Should().BeNull();
            result.DateTaken.Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAsync_UnreadableFile_ReturnsEmptyExifData_DoesNotThrow()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 0x00, 0x01, 0x02, 0x03 });

            var reader = new MetadataExtractorExifReader();
            var result = await reader.ReadAsync(path);

            result.Should().Be(ExifData.Empty);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail to compile**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter MetadataExtractorExifReaderTests`
Expected: FAIL — types don't exist yet.

- [ ] **Step 4: Implement**

`src/PictureManager.Application/Scanning/ExifData.cs`:

```csharp
using System;

namespace PictureManager.Application.Scanning;

public sealed record ExifData(
    int? Width,
    int? Height,
    int? Orientation,
    DateTime? DateTaken,
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    double? Latitude,
    double? Longitude,
    string? RawMetadataJson)
{
    public static readonly ExifData Empty = new(null, null, null, null, null, null, null, null, null, null);
}
```

`src/PictureManager.Application/Scanning/IExifReader.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Scanning;

public interface IExifReader
{
    Task<ExifData> ReadAsync(string filePath, CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Infrastructure/Scanning/MetadataExtractorExifReader.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using PictureManager.Application.Scanning;

namespace PictureManager.Infrastructure.Scanning;

public sealed class MetadataExtractorExifReader : IExifReader
{
    public Task<ExifData> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(filePath);
        }
        catch (Exception)
        {
            return Task.FromResult(ExifData.Empty);
        }

        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        var jpeg = directories.OfType<JpegDirectory>().FirstOrDefault();

        int? width = jpeg is not null && jpeg.TryGetInt32(JpegDirectory.TagImageWidth, out var w) ? w : null;
        int? height = jpeg is not null && jpeg.TryGetInt32(JpegDirectory.TagImageHeight, out var h) ? h : null;
        int? orientation = ifd0 is not null && ifd0.TryGetInt32(ExifIfd0Directory.TagOrientation, out var o) ? o : null;
        DateTime? dateTaken = subIfd is not null && subIfd.TryGetDateTime(ExifSubIfdDirectory.TagDateTimeOriginal, out var dt) ? dt : null;
        var geoLocation = gps?.GetGeoLocation();

        var rawMetadata = new Dictionary<string, string>();
        foreach (var directory in directories)
        {
            foreach (var tag in directory.Tags)
            {
                rawMetadata[$"{directory.Name}.{tag.Name}"] = tag.Description ?? string.Empty;
            }
        }

        var result = new ExifData(
            width, height, orientation, dateTaken,
            ifd0?.GetDescription(ExifIfd0Directory.TagMake),
            ifd0?.GetDescription(ExifIfd0Directory.TagModel),
            subIfd?.GetDescription(ExifSubIfdDirectory.TagLensModel),
            geoLocation?.Latitude,
            geoLocation?.Longitude,
            JsonSerializer.Serialize(rawMetadata));

        return Task.FromResult(result);
    }
}
```

If `MetadataExtractor`'s exact tag constant names differ slightly from the above (library API surface can
shift between versions), adjust to match IntelliSense/compiler errors — the shape (dimensions, orientation,
date taken, make/model/lens, GPS, a raw tag dump serialized to JSON) is what matters, not the exact constant
spelling.

- [ ] **Step 5: Register in DI, extend the DI test, run, and commit**

Add to `InfrastructureServiceCollectionExtensions.AddInfrastructure`:

```csharp
        services.AddSingleton<IExifReader, MetadataExtractorExifReader>();
```

Add to `InfrastructureServiceCollectionExtensionsTests`:

```csharp
    [Fact]
    public void AddInfrastructure_RegistersExifReader()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PictureManagerDb"] =
                    "Host=localhost;Database=picturemanager;Username=test;Password=test"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        var provider = services.BuildServiceProvider();

        provider.GetService<IExifReader>().Should().NotBeNull();
    }
```

Run: `dotnet test tests/PictureManager.Infrastructure.Tests`
Expected: PASS, all tests.

```bash
git add src/PictureManager.Application/Scanning src/PictureManager.Infrastructure tests/PictureManager.Infrastructure.Tests
git commit -m "feat: add MetadataExtractor-based EXIF reader"
```

---

## Task 5: Scan exclude rules and image reconciler

**Files:**
- Create: `src/PictureManager.Application/Scanning/ScanExcludeRules.cs`
- Create: `src/PictureManager.Application/Scanning/ImageReconciler.cs`
- Test: `tests/PictureManager.Application.Tests/Scanning/ScanExcludeRulesTests.cs`
- Test: `tests/PictureManager.Application.Tests/Scanning/ImageReconcilerTests.cs`

**Interfaces:**
- Consumes: `AppSettings` (phase 2), `Image` (phase 2).
- Produces: `ScanExcludeRules(AppSettings)` with `IsFolderExcluded(string)`/`IsExtensionAllowed(string)`;
  `ReconcileAction` enum `{ New, Unchanged, Modified }` and `ImageReconciler.Decide(Image? existing, long
  observedSize, DateTime observedModifiedUtc)`. Both consumed by Task 8 (`ScanService`).

- [ ] **Step 1: Write the failing tests**

`tests/PictureManager.Application.Tests/Scanning/ScanExcludeRulesTests.cs`:

```csharp
using System.Collections.Generic;
using FluentAssertions;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public class ScanExcludeRulesTests
{
    [Fact]
    public void IsFolderExcluded_IsCaseInsensitive()
    {
        var rules = new ScanExcludeRules(new AppSettings { ExcludedFolderNames = new List<string> { "raw", "@eaDir" } });

        rules.IsFolderExcluded("RAW").Should().BeTrue();
        rules.IsFolderExcluded("@eadir").Should().BeTrue();
        rules.IsFolderExcluded("Vacation").Should().BeFalse();
    }

    [Fact]
    public void IsExtensionAllowed_ExcludedExtension_IsAlwaysRejected()
    {
        var rules = new ScanExcludeRules(new AppSettings { ExcludedExtensions = new List<string> { ".heic" } });

        rules.IsExtensionAllowed(".HEIC").Should().BeFalse();
        rules.IsExtensionAllowed(".jpg").Should().BeTrue();
    }

    [Fact]
    public void IsExtensionAllowed_WithIncludedExtensionsSet_OnlyThoseAreAllowed()
    {
        var rules = new ScanExcludeRules(new AppSettings { IncludedExtensions = new List<string> { ".jpg", ".png" } });

        rules.IsExtensionAllowed(".jpg").Should().BeTrue();
        rules.IsExtensionAllowed(".gif").Should().BeFalse();
    }

    [Fact]
    public void IsExtensionAllowed_NoIncludedExtensionsConfigured_AllowsAnythingNotExcluded()
    {
        var rules = new ScanExcludeRules(new AppSettings());

        rules.IsExtensionAllowed(".anything").Should().BeTrue();
    }
}
```

`tests/PictureManager.Application.Tests/Scanning/ImageReconcilerTests.cs`:

```csharp
using System;
using FluentAssertions;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public class ImageReconcilerTests
{
    [Fact]
    public void Decide_NoExistingImage_ReturnsNew()
    {
        ImageReconciler.Decide(null, 100, DateTime.UtcNow).Should().Be(ReconcileAction.New);
    }

    [Fact]
    public void Decide_SameSizeAndModifiedTime_ReturnsUnchanged()
    {
        var modified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = new Image { FileSize = 100, FileModified = modified, MissingSinceUtc = null };

        ImageReconciler.Decide(existing, 100, modified).Should().Be(ReconcileAction.Unchanged);
    }

    [Fact]
    public void Decide_DifferentSize_ReturnsModified()
    {
        var modified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = new Image { FileSize = 100, FileModified = modified };

        ImageReconciler.Decide(existing, 200, modified).Should().Be(ReconcileAction.Modified);
    }

    [Fact]
    public void Decide_DifferentModifiedTime_ReturnsModified()
    {
        var existing = new Image { FileSize = 100, FileModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

        ImageReconciler.Decide(existing, 100, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)).Should().Be(ReconcileAction.Modified);
    }

    [Fact]
    public void Decide_PreviouslyMissingFileReappears_ReturnsModified()
    {
        var modified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = new Image { FileSize = 100, FileModified = modified, MissingSinceUtc = modified.AddDays(1) };

        ImageReconciler.Decide(existing, 100, modified).Should().Be(ReconcileAction.Modified);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/PictureManager.Application.Tests`
Expected: FAIL — types don't exist yet.

- [ ] **Step 3: Implement**

`src/PictureManager.Application/Scanning/ScanExcludeRules.cs`:

```csharp
using System;
using System.Collections.Generic;
using PictureManager.Model;

namespace PictureManager.Application.Scanning;

public sealed class ScanExcludeRules
{
    private readonly HashSet<string> _excludedFolderNames;
    private readonly HashSet<string> _excludedExtensions;
    private readonly HashSet<string>? _includedExtensions;

    public ScanExcludeRules(AppSettings settings)
    {
        _excludedFolderNames = new HashSet<string>(settings.ExcludedFolderNames, StringComparer.OrdinalIgnoreCase);
        _excludedExtensions = new HashSet<string>(settings.ExcludedExtensions, StringComparer.OrdinalIgnoreCase);
        _includedExtensions = settings.IncludedExtensions is { Count: > 0 }
            ? new HashSet<string>(settings.IncludedExtensions, StringComparer.OrdinalIgnoreCase)
            : null;
    }

    public bool IsFolderExcluded(string folderName) => _excludedFolderNames.Contains(folderName);

    public bool IsExtensionAllowed(string extension)
    {
        if (_excludedExtensions.Contains(extension))
            return false;

        return _includedExtensions is null || _includedExtensions.Contains(extension);
    }
}
```

`src/PictureManager.Application/Scanning/ImageReconciler.cs`:

```csharp
using System;
using PictureManager.Model;

namespace PictureManager.Application.Scanning;

public enum ReconcileAction
{
    New,
    Unchanged,
    Modified
}

public static class ImageReconciler
{
    public static ReconcileAction Decide(Image? existing, long observedSize, DateTime observedModifiedUtc)
    {
        if (existing is null)
            return ReconcileAction.New;

        var unchanged = existing.FileSize == observedSize
            && existing.FileModified == observedModifiedUtc
            && existing.MissingSinceUtc is null;

        return unchanged ? ReconcileAction.Unchanged : ReconcileAction.Modified;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass, then commit**

Run: `dotnet test tests/PictureManager.Application.Tests`
Expected: PASS, all tests.

```bash
git add src/PictureManager.Application/Scanning tests/PictureManager.Application.Tests/Scanning
git commit -m "feat: add scan exclude rules and image reconciliation logic"
```

---

## Task 6: Enrichment queue interface and image enrichment service

**Files:**
- Create: `src/PictureManager.Application/Scanning/IEnrichmentQueue.cs`
- Create: `src/PictureManager.Application/Scanning/IImageEnrichmentService.cs`
- Create: `src/PictureManager.Application/Scanning/ImageEnrichmentService.cs`
- Modify: `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Application.Tests/Scanning/ImageEnrichmentServiceTests.cs`

**Interfaces:**
- Consumes: `IImageRepository` (Task 1), `IContentHasher` (Task 3), `IExifReader` (Task 4), `ImagePathResolver`
  (Task 2), `IClock` (existing).
- Produces: `IEnrichmentQueue.Enqueue(int scanJobId, int imageId)` /
  `IAsyncEnumerable<(int ScanJobId, int ImageId)> ReadAllAsync(CancellationToken)` — implemented in Task 7 by
  `PictureManager.Worker`. `IImageEnrichmentService.EnrichAsync(int imageId, CancellationToken)` — called by
  Task 7's `EnrichmentBackgroundService`.

- [ ] **Step 1: Write the failing test**

`tests/PictureManager.Application.Tests/Scanning/ImageEnrichmentServiceTests.cs`:

```csharp
using System;
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

public class ImageEnrichmentServiceTests
{
    [Fact]
    public async Task EnrichAsync_ExistingFile_UpdatesHashExifAndIndexState()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempFile, new byte[] { 1, 2, 3 });
            var tempDir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileNameWithoutExtension(tempFile);
            var extension = Path.GetExtension(tempFile);

            var image = new Image
            {
                Id = 1,
                FileName = fileName,
                Extension = extension,
                FileSize = 3,
                Folder = new Folder { Id = 5, RelativePath = string.Empty, Root = new ImageRoot { Id = 1, MountPath = tempDir } }
            };

            var imageRepository = Substitute.For<IImageRepository>();
            imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(image);
            imageRepository.GetByContentHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Image?)null);

            var contentHasher = Substitute.For<IContentHasher>();
            contentHasher.ComputeAsync(tempFile, 3, Arg.Any<CancellationToken>()).Returns("hash-abc");

            var exifReader = Substitute.For<IExifReader>();
            exifReader.ReadAsync(tempFile, Arg.Any<CancellationToken>()).Returns(new ExifData(
                100, 200, 1, new DateTime(2026, 1, 1), "Canon", "EOS R5", "24-70mm", 1.0, 2.0, "{}"));

            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc));

            var service = new ImageEnrichmentService(imageRepository, contentHasher, exifReader, clock);
            await service.EnrichAsync(1);

            await imageRepository.Received(1).UpdateAsync(
                Arg.Is<Image>(i => i.ContentHash == "hash-abc" && i.Width == 100 && i.CameraMake == "Canon"
                    && i.IndexState == IndexState.Indexed && i.MissingSinceUtc == null),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task EnrichAsync_FileNoLongerExists_MarksImageMissing()
    {
        var image = new Image
        {
            Id = 2,
            FileName = "gone",
            Extension = ".jpg",
            Folder = new Folder { Id = 5, RelativePath = string.Empty, Root = new ImageRoot { Id = 1, MountPath = Path.GetTempPath() } }
        };

        var imageRepository = Substitute.For<IImageRepository>();
        imageRepository.GetByIdWithFolderAsync(2, Arg.Any<CancellationToken>()).Returns(image);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc));

        var service = new ImageEnrichmentService(imageRepository, Substitute.For<IContentHasher>(), Substitute.For<IExifReader>(), clock);
        await service.EnrichAsync(2);

        await imageRepository.Received(1).UpdateAsync(
            Arg.Is<Image>(i => i.MissingSinceUtc == clock.UtcNow), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrichAsync_HashMatchesAnExistingMissingImage_DeletesTheStaleRow()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempFile, new byte[] { 1, 2, 3 });
            var tempDir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileNameWithoutExtension(tempFile);
            var extension = Path.GetExtension(tempFile);

            var image = new Image
            {
                Id = 1,
                FileName = fileName,
                Extension = extension,
                FileSize = 3,
                Folder = new Folder { Id = 5, RelativePath = string.Empty, Root = new ImageRoot { Id = 1, MountPath = tempDir } }
            };
            var staleMissingImage = new Image { Id = 99, MissingSinceUtc = DateTime.UtcNow };

            var imageRepository = Substitute.For<IImageRepository>();
            imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(image);
            imageRepository.GetByContentHashAsync("hash-abc", Arg.Any<CancellationToken>()).Returns(staleMissingImage);

            var contentHasher = Substitute.For<IContentHasher>();
            contentHasher.ComputeAsync(tempFile, 3, Arg.Any<CancellationToken>()).Returns("hash-abc");

            var exifReader = Substitute.For<IExifReader>();
            exifReader.ReadAsync(tempFile, Arg.Any<CancellationToken>()).Returns(ExifData.Empty);

            var service = new ImageEnrichmentService(imageRepository, contentHasher, exifReader, Substitute.For<IClock>());
            await service.EnrichAsync(1);

            await imageRepository.Received(1).DeleteAsync(staleMissingImage, Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet test tests/PictureManager.Application.Tests --filter ImageEnrichmentServiceTests`
Expected: FAIL — types don't exist yet.

- [ ] **Step 3: Implement**

`src/PictureManager.Application/Scanning/IEnrichmentQueue.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;

namespace PictureManager.Application.Scanning;

public interface IEnrichmentQueue
{
    void Enqueue(int scanJobId, int imageId);
    IAsyncEnumerable<(int ScanJobId, int ImageId)> ReadAllAsync(CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Application/Scanning/IImageEnrichmentService.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Scanning;

public interface IImageEnrichmentService
{
    Task EnrichAsync(int imageId, CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Application/Scanning/ImageEnrichmentService.cs`:

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Scanning;

public sealed class ImageEnrichmentService : IImageEnrichmentService
{
    private readonly IImageRepository _imageRepository;
    private readonly IContentHasher _contentHasher;
    private readonly IExifReader _exifReader;
    private readonly IClock _clock;

    public ImageEnrichmentService(IImageRepository imageRepository, IContentHasher contentHasher, IExifReader exifReader, IClock clock)
    {
        _imageRepository = imageRepository;
        _contentHasher = contentHasher;
        _exifReader = exifReader;
        _clock = clock;
    }

    public async Task EnrichAsync(int imageId, CancellationToken cancellationToken = default)
    {
        var image = await _imageRepository.GetByIdWithFolderAsync(imageId, cancellationToken);
        if (image?.Folder?.Root is null)
            return;

        var physicalPath = ImagePathResolver.ResolvePhysicalPath(
            image.Folder.Root.MountPath, image.Folder.RelativePath, image.FileName, image.Extension);

        if (!File.Exists(physicalPath))
        {
            image.MissingSinceUtc = _clock.UtcNow;
            image.UpdatedAt = _clock.UtcNow;
            await _imageRepository.UpdateAsync(image, cancellationToken);
            return;
        }

        var hash = await _contentHasher.ComputeAsync(physicalPath, image.FileSize, cancellationToken);
        var exif = await _exifReader.ReadAsync(physicalPath, cancellationToken);

        var possibleMove = await _imageRepository.GetByContentHashAsync(hash, cancellationToken);
        if (possibleMove is not null && possibleMove.Id != image.Id && possibleMove.MissingSinceUtc is not null)
        {
            await _imageRepository.DeleteAsync(possibleMove, cancellationToken);
        }

        image.ContentHash = hash;
        image.Width = exif.Width;
        image.Height = exif.Height;
        image.Orientation = exif.Orientation;
        image.DateTaken = exif.DateTaken;
        image.CameraMake = exif.CameraMake;
        image.CameraModel = exif.CameraModel;
        image.LensModel = exif.LensModel;
        image.Latitude = exif.Latitude;
        image.Longitude = exif.Longitude;
        image.RawMetadata = exif.RawMetadataJson;
        image.IndexState = IndexState.Indexed;
        image.MissingSinceUtc = null;
        image.UpdatedAt = _clock.UtcNow;

        await _imageRepository.UpdateAsync(image, cancellationToken);
    }
}
```

- [ ] **Step 4: Register in DI**

Add to `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`:

```csharp
        services.AddScoped<IImageEnrichmentService, ImageEnrichmentService>();
```

(Add `using PictureManager.Application.Scanning;` at the top of the file.)

- [ ] **Step 5: Run the tests, then commit**

Run: `dotnet test tests/PictureManager.Application.Tests`
Expected: PASS, all tests.

```bash
git add src/PictureManager.Application tests/PictureManager.Application.Tests
git commit -m "feat: add enrichment queue interface and image enrichment service"
```

---

## Task 7: Worker background service (channel queue, hosted service, DI)

**Files:**
- Create: `src/PictureManager.Worker/Scanning/ChannelEnrichmentQueue.cs`
- Create: `src/PictureManager.Worker/Scanning/EnrichmentBackgroundService.cs`
- Create: `src/PictureManager.Worker/DependencyInjection/WorkerServiceCollectionExtensions.cs`
- Modify: `src/PictureManager.Worker/PictureManager.Worker.csproj`
- Create: `tests/PictureManager.Worker.Tests/PictureManager.Worker.Tests.csproj`
- Test: `tests/PictureManager.Worker.Tests/Scanning/ChannelEnrichmentQueueTests.cs`
- Test: `tests/PictureManager.Worker.Tests/Scanning/EnrichmentBackgroundServiceTests.cs`
- Modify: `PictureManager.slnx`

**Interfaces:**
- Consumes: `IEnrichmentQueue`, `IImageEnrichmentService`, `IScanJobRepository` (all Application interfaces).
- Produces: `ChannelEnrichmentQueue : IEnrichmentQueue`, `EnrichmentBackgroundService : BackgroundService`,
  `WorkerServiceCollectionExtensions.AddWorker(IServiceCollection)` — called from `PictureManager.Api`'s
  `Program.cs` in Task 11.

- [ ] **Step 1: Add package references and the new test project**

`src/PictureManager.Worker/PictureManager.Worker.csproj` needs `Microsoft.Extensions.Hosting.Abstractions` for
`BackgroundService`. Replace its contents:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\PictureManager.Application\PictureManager.Application.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.12" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.12" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

Create `tests/PictureManager.Worker.Tests/PictureManager.Worker.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="FluentAssertions" Version="[7.0.0,8.0.0)" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.12" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.12" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="NSubstitute" Version="6.2.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\PictureManager.Worker\PictureManager.Worker.csproj" />
  </ItemGroup>

</Project>
```

Add the new test project to `PictureManager.slnx` (open the file, add a `<Project>` entry alongside the
existing test projects, matching their exact syntax).

- [ ] **Step 2: Write the failing tests**

`tests/PictureManager.Worker.Tests/Scanning/ChannelEnrichmentQueueTests.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Worker.Scanning;
using Xunit;

namespace PictureManager.Worker.Tests.Scanning;

public class ChannelEnrichmentQueueTests
{
    [Fact]
    public async Task Enqueue_ThenReadAllAsync_YieldsTheItem()
    {
        var queue = new ChannelEnrichmentQueue();
        queue.Enqueue(scanJobId: 1, imageId: 42);

        using var cts = new CancellationTokenSource();
        await foreach (var item in queue.ReadAllAsync(cts.Token))
        {
            item.Should().Be((1, 42));
            break;
        }
    }
}
```

`tests/PictureManager.Worker.Tests/Scanning/EnrichmentBackgroundServiceTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using PictureManager.Worker.Scanning;
using Xunit;

namespace PictureManager.Worker.Tests.Scanning;

public class EnrichmentBackgroundServiceTests
{
    [Fact]
    public async Task ProcessesQueuedItems_UpdatesScanJobCounters_AndCompletesWhenAllDone()
    {
        var enrichmentService = Substitute.For<IImageEnrichmentService>();
        var scanJobRepository = Substitute.For<IScanJobRepository>();
        var scanJob = new ScanJob { Id = 1, FilesFound = 1, FilesEnriched = 0, Status = ScanJobStatus.Enriching };
        scanJobRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(scanJob);

        var services = new ServiceCollection();
        services.AddSingleton(enrichmentService);
        services.AddSingleton(scanJobRepository);
        var provider = services.BuildServiceProvider();

        var queue = new ChannelEnrichmentQueue();
        var service = new EnrichmentBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<EnrichmentBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(1, 100);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (scanJob.FilesEnriched == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        await service.StopAsync(CancellationToken.None);

        await enrichmentService.Received(1).EnrichAsync(100, Arg.Any<CancellationToken>());
        scanJob.FilesEnriched.Should().Be(1);
        scanJob.Status.Should().Be(ScanJobStatus.Completed);
    }
}
```

Note: `IServiceScopeFactory` needs a real `ServiceProvider` (not a bare substitute) because
`EnrichmentBackgroundService` creates a scope per item — the test above registers the fakes as singletons on a
real `ServiceCollection`/`ServiceProvider` so `CreateScope()` works normally.

- [ ] **Step 3: Run the tests to verify they fail to compile**

Run: `dotnet test tests/PictureManager.Worker.Tests`
Expected: FAIL — types don't exist yet.

- [ ] **Step 4: Implement**

`src/PictureManager.Worker/Scanning/ChannelEnrichmentQueue.cs`:

```csharp
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using PictureManager.Application.Scanning;

namespace PictureManager.Worker.Scanning;

public sealed class ChannelEnrichmentQueue : IEnrichmentQueue
{
    private readonly Channel<(int ScanJobId, int ImageId)> _channel = Channel.CreateUnbounded<(int, int)>();

    public void Enqueue(int scanJobId, int imageId) => _channel.Writer.TryWrite((scanJobId, imageId));

    public async IAsyncEnumerable<(int ScanJobId, int ImageId)> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }
}
```

`src/PictureManager.Worker/Scanning/EnrichmentBackgroundService.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;

namespace PictureManager.Worker.Scanning;

public sealed class EnrichmentBackgroundService : BackgroundService
{
    private readonly IEnrichmentQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EnrichmentBackgroundService> _logger;

    public EnrichmentBackgroundService(IEnrichmentQueue queue, IServiceScopeFactory scopeFactory, ILogger<EnrichmentBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (scanJobId, imageId) in _queue.ReadAllAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var enrichmentService = scope.ServiceProvider.GetRequiredService<IImageEnrichmentService>();
            var scanJobRepository = scope.ServiceProvider.GetRequiredService<IScanJobRepository>();

            try
            {
                await enrichmentService.EnrichAsync(imageId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enrich image {ImageId}", imageId);
            }

            await MarkOneEnrichedAsync(scanJobRepository, scanJobId, stoppingToken);
        }
    }

    private static async Task MarkOneEnrichedAsync(IScanJobRepository scanJobRepository, int scanJobId, CancellationToken cancellationToken)
    {
        var scanJob = await scanJobRepository.GetByIdAsync(scanJobId, cancellationToken);
        if (scanJob is null)
            return;

        scanJob.FilesEnriched++;
        if (scanJob.FilesEnriched >= scanJob.FilesFound && scanJob.Status != ScanJobStatus.Completed)
        {
            scanJob.Status = ScanJobStatus.Completed;
            scanJob.CompletedUtc = DateTime.UtcNow;
        }

        await scanJobRepository.UpdateAsync(scanJob, cancellationToken);
    }
}
```

`src/PictureManager.Worker/DependencyInjection/WorkerServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PictureManager.Application.Scanning;
using PictureManager.Worker.Scanning;

namespace PictureManager.Worker.DependencyInjection;

public static class WorkerServiceCollectionExtensions
{
    public static IServiceCollection AddWorker(this IServiceCollection services)
    {
        services.AddSingleton<IEnrichmentQueue, ChannelEnrichmentQueue>();
        services.AddHostedService<EnrichmentBackgroundService>();

        return services;
    }
}
```

- [ ] **Step 5: Run the tests, then commit**

Run: `dotnet test tests/PictureManager.Worker.Tests`
Expected: PASS, all tests.

Run: `dotnet build` (full solution, confirms the new test project and slnx edit are wired correctly)
Expected: Build succeeds, 0 warnings, 0 errors.

```bash
git add src/PictureManager.Worker tests/PictureManager.Worker.Tests PictureManager.slnx
git commit -m "feat: add channel-backed enrichment queue and background service"
```

---

## Task 8: Scan service (enumerate phase orchestration)

**Files:**
- Create: `src/PictureManager.Application/Scanning/IScanService.cs`
- Create: `src/PictureManager.Application/Scanning/ScanService.cs`
- Modify: `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Application.Tests/Scanning/ScanServiceTests.cs`

**Interfaces:**
- Consumes: `IImageRootRepository`, `IFolderRepository`, `IImageRepository`, `IAppSettingsRepository`,
  `IScanJobRepository` (all Task 1 / existing), `IEnrichmentQueue` (Task 6), `IClock` (existing),
  `PathNormalizer` (Task 2), `ScanExcludeRules`/`ImageReconciler` (Task 5).
- Produces: `IScanService.StartScanAsync(int? rootId, bool isRecursive, CancellationToken)` → `int` (the new
  `ScanJob.Id`). Called by Task 10's `POST /api/scans` endpoint.

- [ ] **Step 1: Write the failing tests**

`tests/PictureManager.Application.Tests/Scanning/ScanServiceTests.cs`:

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

public class ScanServiceTests
{
    [Fact]
    public async Task StartScanAsync_NewFileInRoot_CreatesPendingImage_AndEnqueuesForEnrichment()
    {
        var tempRoot = Directory.CreateTempSubdirectory("pm-scan-test-");
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(tempRoot.FullName, "photo.jpg"), new byte[] { 1, 2, 3 });

            var imageRoot = new ImageRoot { Id = 1, Name = "dev", MountPath = tempRoot.FullName, IsActive = true };
            var rootFolder = new Folder { Id = 10, RootId = 1, RelativePath = string.Empty, Name = "dev" };

            var imageRootRepository = Substitute.For<IImageRootRepository>();
            imageRootRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(imageRoot);

            var folderRepository = Substitute.For<IFolderRepository>();
            folderRepository.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>()).Returns(rootFolder);

            var imageRepository = Substitute.For<IImageRepository>();
            imageRepository.GetByFolderAndFileNameAsync(10, "photo", ".jpg", Arg.Any<CancellationToken>()).Returns((Image?)null);
            imageRepository.GetByFolderIdAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Image>());
            imageRepository.AddAsync(Arg.Any<Image>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
            {
                var image = callInfo.Arg<Image>();
                image.Id = 100;
                return image;
            });

            var appSettingsRepository = Substitute.For<IAppSettingsRepository>();
            appSettingsRepository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings());

            var scanJobRepository = Substitute.For<IScanJobRepository>();
            scanJobRepository.AddAsync(Arg.Any<ScanJob>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
            {
                var job = callInfo.Arg<ScanJob>();
                job.Id = 999;
                return job;
            });

            var enrichmentQueue = Substitute.For<IEnrichmentQueue>();
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var scanService = new ScanService(
                imageRootRepository, folderRepository, imageRepository,
                appSettingsRepository, scanJobRepository, enrichmentQueue, clock);

            var scanJobId = await scanService.StartScanAsync(rootId: 1, isRecursive: true);

            scanJobId.Should().Be(999);
            await imageRepository.Received(1).AddAsync(
                Arg.Is<Image>(i => i.FolderId == 10 && i.FileName == "photo" && i.Extension == ".jpg" && i.IndexState == IndexState.Pending),
                Arg.Any<CancellationToken>());
            enrichmentQueue.Received(1).Enqueue(999, 100);
            await scanJobRepository.Received(1).UpdateAsync(
                Arg.Is<ScanJob>(j => j.FilesFound == 1 && j.Status == ScanJobStatus.Enriching),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task StartScanAsync_FileNoLongerPresent_MarksExistingImageMissing()
    {
        var tempRoot = Directory.CreateTempSubdirectory("pm-scan-test-");
        try
        {
            var imageRoot = new ImageRoot { Id = 1, Name = "dev", MountPath = tempRoot.FullName, IsActive = true };
            var rootFolder = new Folder { Id = 10, RootId = 1, RelativePath = string.Empty, Name = "dev" };
            var missingImage = new Image { Id = 5, FolderId = 10, FileName = "gone", Extension = ".jpg", MissingSinceUtc = null };

            var imageRootRepository = Substitute.For<IImageRootRepository>();
            imageRootRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(imageRoot);

            var folderRepository = Substitute.For<IFolderRepository>();
            folderRepository.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>()).Returns(rootFolder);

            var imageRepository = Substitute.For<IImageRepository>();
            imageRepository.GetByFolderIdAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Image> { missingImage });

            var appSettingsRepository = Substitute.For<IAppSettingsRepository>();
            appSettingsRepository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings());

            var scanJobRepository = Substitute.For<IScanJobRepository>();
            scanJobRepository.AddAsync(Arg.Any<ScanJob>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
            {
                var job = callInfo.Arg<ScanJob>();
                job.Id = 999;
                return job;
            });

            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var scanService = new ScanService(
                imageRootRepository, folderRepository, imageRepository,
                appSettingsRepository, scanJobRepository, Substitute.For<IEnrichmentQueue>(), clock);

            await scanService.StartScanAsync(rootId: 1, isRecursive: true);

            await imageRepository.Received(1).UpdateAsync(
                Arg.Is<Image>(i => i.Id == 5 && i.MissingSinceUtc == clock.UtcNow),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempRoot.FullName, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/PictureManager.Application.Tests --filter ScanServiceTests`
Expected: FAIL — `ScanService` doesn't exist yet.

- [ ] **Step 3: Implement**

`src/PictureManager.Application/Scanning/IScanService.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Scanning;

public interface IScanService
{
    Task<int> StartScanAsync(int? rootId, bool isRecursive, CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Application/Scanning/ScanService.cs`:

```csharp
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
    private readonly IImageRootRepository _imageRootRepository;
    private readonly IFolderRepository _folderRepository;
    private readonly IImageRepository _imageRepository;
    private readonly IAppSettingsRepository _appSettingsRepository;
    private readonly IScanJobRepository _scanJobRepository;
    private readonly IEnrichmentQueue _enrichmentQueue;
    private readonly IClock _clock;

    public ScanService(
        IImageRootRepository imageRootRepository,
        IFolderRepository folderRepository,
        IImageRepository imageRepository,
        IAppSettingsRepository appSettingsRepository,
        IScanJobRepository scanJobRepository,
        IEnrichmentQueue enrichmentQueue,
        IClock clock)
    {
        _imageRootRepository = imageRootRepository;
        _folderRepository = folderRepository;
        _imageRepository = imageRepository;
        _appSettingsRepository = appSettingsRepository;
        _scanJobRepository = scanJobRepository;
        _enrichmentQueue = enrichmentQueue;
        _clock = clock;
    }

    public async Task<int> StartScanAsync(int? rootId, bool isRecursive, CancellationToken cancellationToken = default)
    {
        var roots = rootId.HasValue
            ? new[] { await _imageRootRepository.GetByIdAsync(rootId.Value, cancellationToken)
                ?? throw new InvalidOperationException($"ImageRoot {rootId} not found.") }
            : (await _imageRootRepository.GetAllAsync(cancellationToken)).Where(r => r.IsActive).ToArray();

        var scanJob = await _scanJobRepository.AddAsync(new ScanJob
        {
            IsRecursive = isRecursive,
            Status = ScanJobStatus.Enumerating,
            StartedUtc = _clock.UtcNow
        }, cancellationToken);

        var settings = await _appSettingsRepository.GetAsync(cancellationToken);
        var excludeRules = new ScanExcludeRules(settings);

        var foldersScanned = 0;
        var filesFound = 0;

        foreach (var root in roots)
        {
            var (rootFoldersScanned, rootFilesFound) = await ScanRootAsync(root, isRecursive, excludeRules, scanJob.Id, cancellationToken);
            foldersScanned += rootFoldersScanned;
            filesFound += rootFilesFound;
        }

        scanJob.FoldersScanned = foldersScanned;
        scanJob.FilesFound = filesFound;
        scanJob.Status = filesFound == 0 ? ScanJobStatus.Completed : ScanJobStatus.Enriching;
        if (filesFound == 0)
            scanJob.CompletedUtc = _clock.UtcNow;

        await _scanJobRepository.UpdateAsync(scanJob, cancellationToken);

        return scanJob.Id;
    }

    private async Task<(int FoldersScanned, int FilesFound)> ScanRootAsync(
        ImageRoot root, bool isRecursive, ScanExcludeRules excludeRules, int scanJobId, CancellationToken cancellationToken)
    {
        var foldersScanned = 0;
        var filesFound = 0;

        var rootFolder = await GetOrCreateFolderAsync(root.Id, parentId: null, relativePath: string.Empty, name: root.Name, cancellationToken);
        var pending = new Queue<(Folder Folder, string PhysicalPath)>();
        pending.Enqueue((rootFolder, root.MountPath));

        while (pending.Count > 0)
        {
            var (folder, physicalPath) = pending.Dequeue();
            foldersScanned++;

            if (!Directory.Exists(physicalPath))
                continue;

            var observedFiles = new HashSet<(string FileName, string Extension)>();

            foreach (var entryPath in Directory.EnumerateFileSystemEntries(physicalPath))
            {
                var name = Path.GetFileName(entryPath);

                if (Directory.Exists(entryPath))
                {
                    if (excludeRules.IsFolderExcluded(name))
                        continue;

                    var childRelativePath = PathNormalizer.Combine(folder.RelativePath, name);
                    var childFolder = await GetOrCreateFolderAsync(root.Id, folder.Id, childRelativePath, name, cancellationToken);

                    if (isRecursive)
                        pending.Enqueue((childFolder, entryPath));
                }
                else
                {
                    var extension = Path.GetExtension(name).ToLowerInvariant();
                    var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(name);

                    if (!excludeRules.IsExtensionAllowed(extension))
                        continue;

                    observedFiles.Add((fileNameWithoutExtension, extension));
                    var fileInfo = new FileInfo(entryPath);
                    var existingImage = await _imageRepository.GetByFolderAndFileNameAsync(folder.Id, fileNameWithoutExtension, extension, cancellationToken);
                    var decision = ImageReconciler.Decide(existingImage, fileInfo.Length, fileInfo.LastWriteTimeUtc);

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
                                FileModified = fileInfo.LastWriteTimeUtc,
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
                            existingImage.FileModified = fileInfo.LastWriteTimeUtc;
                            existingImage.IndexState = IndexState.Pending;
                            existingImage.MissingSinceUtc = null;
                            existingImage.UpdatedAt = _clock.UtcNow;
                            await _imageRepository.UpdateAsync(existingImage, cancellationToken);
                            _enrichmentQueue.Enqueue(scanJobId, existingImage.Id);
                            filesFound++;
                            break;

                        case ReconcileAction.Unchanged:
                            filesFound++;
                            break;
                    }
                }
            }

            var existingImages = await _imageRepository.GetByFolderIdAsync(folder.Id, cancellationToken);
            foreach (var image in existingImages)
            {
                if (image.MissingSinceUtc is null && !observedFiles.Contains((image.FileName, image.Extension)))
                {
                    image.MissingSinceUtc = _clock.UtcNow;
                    image.UpdatedAt = _clock.UtcNow;
                    await _imageRepository.UpdateAsync(image, cancellationToken);
                }
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
}
```

Add `using System;` at the top if not already implicit (target uses `ImplicitUsings`, so `InvalidOperationException` resolves without an explicit `using System;` — leave as-is unless the compiler disagrees).

- [ ] **Step 4: Register in DI**

Add to `ApplicationServiceCollectionExtensions.AddApplication`:

```csharp
        services.AddScoped<IScanService, ScanService>();
```

- [ ] **Step 5: Run the tests, then commit**

Run: `dotnet test tests/PictureManager.Application.Tests`
Expected: PASS, all tests.

```bash
git add src/PictureManager.Application tests/PictureManager.Application.Tests
git commit -m "feat: add scan service enumerate-phase orchestration"
```

---

## Task 9: Dev ImageRoot startup seeding

**Files:**
- Create: `src/PictureManager.Application/Scanning/DevImageRootOptions.cs`
- Create: `src/PictureManager.Application/Scanning/IDevImageRootSeeder.cs`
- Create: `src/PictureManager.Application/Scanning/DevImageRootSeeder.cs`
- Modify: `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Application.Tests/Scanning/DevImageRootSeederTests.cs`

**Interfaces:**
- Consumes: `IImageRootRepository` (existing), `IClock` (existing).
- Produces: `IDevImageRootSeeder.SeedAsync(CancellationToken)`. Called from `Program.cs` in Task 11.

- [ ] **Step 1: Write the failing tests**

`tests/PictureManager.Application.Tests/Scanning/DevImageRootSeederTests.cs`:

```csharp
using System;
using System.Collections.Generic;
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

public class DevImageRootSeederTests
{
    [Fact]
    public async Task SeedAsync_OptionsConfigured_AndNoExistingRootWithThatName_CreatesOne()
    {
        var repository = Substitute.For<IImageRootRepository>();
        repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ImageRoot>());
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var seeder = new DevImageRootSeeder(repository, clock, new DevImageRootOptions { Name = "dev", MountPath = "/dev-data/images" });
        await seeder.SeedAsync();

        await repository.Received(1).AddAsync(
            Arg.Is<ImageRoot>(r => r.Name == "dev" && r.MountPath == "/dev-data/images" && r.IsActive),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_RootWithThatNameAlreadyExists_DoesNotCreateAnother()
    {
        var repository = Substitute.For<IImageRootRepository>();
        repository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ImageRoot> { new() { Id = 1, Name = "dev", MountPath = "/dev-data/images" } });

        var seeder = new DevImageRootSeeder(repository, Substitute.For<IClock>(), new DevImageRootOptions { Name = "dev", MountPath = "/dev-data/images" });
        await seeder.SeedAsync();

        await repository.DidNotReceive().AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_OptionsNotConfigured_DoesNothing()
    {
        var repository = Substitute.For<IImageRootRepository>();

        var seeder = new DevImageRootSeeder(repository, Substitute.For<IClock>(), new DevImageRootOptions());
        await seeder.SeedAsync();

        await repository.DidNotReceive().AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/PictureManager.Application.Tests --filter DevImageRootSeederTests`
Expected: FAIL — types don't exist yet.

- [ ] **Step 3: Implement**

`src/PictureManager.Application/Scanning/DevImageRootOptions.cs`:

```csharp
namespace PictureManager.Application.Scanning;

public sealed class DevImageRootOptions
{
    public string? Name { get; set; }
    public string? MountPath { get; set; }
}
```

`src/PictureManager.Application/Scanning/IDevImageRootSeeder.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Scanning;

public interface IDevImageRootSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Application/Scanning/DevImageRootSeeder.cs`:

```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Scanning;

public sealed class DevImageRootSeeder : IDevImageRootSeeder
{
    private readonly IImageRootRepository _imageRootRepository;
    private readonly IClock _clock;
    private readonly DevImageRootOptions _options;

    public DevImageRootSeeder(IImageRootRepository imageRootRepository, IClock clock, DevImageRootOptions options)
    {
        _imageRootRepository = imageRootRepository;
        _clock = clock;
        _options = options;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Name) || string.IsNullOrWhiteSpace(_options.MountPath))
            return;

        var existingRoots = await _imageRootRepository.GetAllAsync(cancellationToken);
        if (existingRoots.Any(r => r.Name == _options.Name))
            return;

        await _imageRootRepository.AddAsync(new ImageRoot
        {
            Name = _options.Name,
            MountPath = _options.MountPath,
            IsActive = true,
            CreatedUtc = _clock.UtcNow
        }, cancellationToken);
    }
}
```

- [ ] **Step 4: Register in DI**

Add to `ApplicationServiceCollectionExtensions.AddApplication`:

```csharp
        services.AddScoped<IDevImageRootSeeder, DevImageRootSeeder>();
```

`DevImageRootOptions` itself is registered as a singleton instance from `Program.cs` in Task 11 (bound from
configuration there, not via `AddApplication`, to avoid adding an `Microsoft.Extensions.Options` package
dependency to `PictureManager.Application` just for this one POCO).

- [ ] **Step 5: Run the tests, then commit**

Run: `dotnet test tests/PictureManager.Application.Tests`
Expected: PASS, all tests.

```bash
git add src/PictureManager.Application tests/PictureManager.Application.Tests
git commit -m "feat: add dev ImageRoot startup seeder"
```

---

## Task 10: Scan trigger and SSE endpoints

**Files:**
- Create: `src/PictureManager.Api/Endpoints/ScanEndpoints.cs`
- Create: `tests/PictureManager.Api.Tests/PictureManager.Api.Tests.csproj`
- Test: `tests/PictureManager.Api.Tests/Endpoints/ScanEndpointsTests.cs`
- Modify: `PictureManager.slnx`

**Interfaces:**
- Consumes: `IScanService` (Task 8), `IScanJobRepository` (Task 1).
- Produces: `ScanEndpoints.MapScanEndpoints(WebApplication)` — called from `Program.cs` in Task 11.

- [ ] **Step 1: Create the Api test project**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="FluentAssertions" Version="[7.0.0,8.0.0)" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="NSubstitute" Version="6.2.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\PictureManager.Api\PictureManager.Api.csproj" />
  </ItemGroup>

</Project>
```

Save as `tests/PictureManager.Api.Tests/PictureManager.Api.Tests.csproj`, and add it to `PictureManager.slnx`
alongside the other test projects.

- [ ] **Step 2: Write the failing tests**

`tests/PictureManager.Api.Tests/Endpoints/ScanEndpointsTests.cs`:

```csharp
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class ScanEndpointsTests
{
    [Fact]
    public async Task StartScanAsync_CallsScanService_AndReturnsScanJobId()
    {
        var scanService = Substitute.For<IScanService>();
        scanService.StartScanAsync(1, true, Arg.Any<CancellationToken>()).Returns(42);

        var result = await ScanEndpoints.StartScanAsync(new ScanRequest(1, true), scanService, CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<ScanStartedResponse>>();
    }

    [Fact]
    public async Task StreamScanEventsAsync_JobAlreadyCompleted_WritesOneEventThenStops()
    {
        var scanJobRepository = Substitute.For<IScanJobRepository>();
        scanJobRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new ScanJob
        {
            Id = 1,
            Status = ScanJobStatus.Completed,
            FoldersScanned = 3,
            FilesFound = 5,
            FilesEnriched = 5
        });

        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        await ScanEndpoints.StreamScanEventsAsync(context, 1, scanJobRepository, CancellationToken.None);

        body.Position = 0;
        var written = Encoding.UTF8.GetString(body.ToArray());
        written.Should().Contain("\"Status\":\"Completed\"");
    }

    [Fact]
    public async Task StreamScanEventsAsync_UnknownJob_WritesErrorEvent()
    {
        var scanJobRepository = Substitute.For<IScanJobRepository>();
        scanJobRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((ScanJob?)null);

        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        await ScanEndpoints.StreamScanEventsAsync(context, 1, scanJobRepository, CancellationToken.None);

        body.Position = 0;
        var written = Encoding.UTF8.GetString(body.ToArray());
        written.Should().Contain("event: error");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail to compile**

Run: `dotnet test tests/PictureManager.Api.Tests`
Expected: FAIL — `ScanEndpoints` doesn't exist yet.

- [ ] **Step 4: Implement**

`src/PictureManager.Api/Endpoints/ScanEndpoints.cs`:

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;

namespace PictureManager.Api.Endpoints;

public static class ScanEndpoints
{
    public static void MapScanEndpoints(this WebApplication app)
    {
        app.MapPost("/api/scans", StartScanAsync);
        app.MapGet("/api/scans/{id:int}/events", StreamScanEventsAsync);
    }

    public static async Task<IResult> StartScanAsync(ScanRequest request, IScanService scanService, CancellationToken cancellationToken)
    {
        var scanJobId = await scanService.StartScanAsync(request.RootId, request.IsRecursive, cancellationToken);
        return Results.Ok(new ScanStartedResponse(scanJobId));
    }

    public static async Task StreamScanEventsAsync(HttpContext context, int id, IScanJobRepository scanJobRepository, CancellationToken cancellationToken)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        while (!cancellationToken.IsCancellationRequested)
        {
            var scanJob = await scanJobRepository.GetByIdAsync(id, cancellationToken);
            if (scanJob is null)
            {
                await context.Response.WriteAsync("event: error\ndata: not found\n\n", cancellationToken);
                return;
            }

            var payload = JsonSerializer.Serialize(new ScanProgress(
                scanJob.Id, scanJob.Status.ToString(), scanJob.FoldersScanned, scanJob.FilesFound, scanJob.FilesEnriched));
            await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);

            if (scanJob.Status is ScanJobStatus.Completed or ScanJobStatus.Failed or ScanJobStatus.Cancelled)
                return;

            await Task.Delay(1000, cancellationToken);
        }
    }
}

public sealed record ScanRequest(int? RootId, bool IsRecursive);
public sealed record ScanStartedResponse(int ScanJobId);
public sealed record ScanProgress(int Id, string Status, int FoldersScanned, int FilesFound, int FilesEnriched);
```

- [ ] **Step 5: Run the tests, then commit**

Run: `dotnet test tests/PictureManager.Api.Tests`
Expected: PASS, all tests.

```bash
git add src/PictureManager.Api tests/PictureManager.Api.Tests PictureManager.slnx
git commit -m "feat: add scan trigger and SSE progress endpoints"
```

---

## Task 11: Wire everything together, apply the migration to live Postgres, full regression

**Files:**
- Modify: `src/PictureManager.Api/Program.cs`
- Modify: `src/PictureManager.Api/appsettings.Development.json`
- No test file — this task is wiring plus manual/integration verification.

**Interfaces:**
- Consumes: `AddWorker` (Task 7), `IDevImageRootSeeder` (Task 9), `MapScanEndpoints` (Task 10).
- Produces: a running application with the full scan pipeline wired end-to-end.

- [ ] **Step 1: Wire DI, dev seeding, and endpoints in `Program.cs`**

Add these `using` statements to `src/PictureManager.Api/Program.cs`:

```csharp
using PictureManager.Api.Endpoints;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Worker.DependencyInjection;
```

After the existing `builder.Services.AddInfrastructure(builder.Configuration);` line, add:

```csharp
    builder.Services.AddWorker();

    var devImageRootOptions = new DevImageRootOptions();
    builder.Configuration.GetSection("DevImageRoot").Bind(devImageRootOptions);
    builder.Services.AddSingleton(devImageRootOptions);
```

After `var app = builder.Build();`, before `app.Run();`, add:

```csharp
    using (var scope = app.Services.CreateScope())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<IDevImageRootSeeder>();
        await seeder.SeedAsync();
    }

    app.MapScanEndpoints();
```

(`GetRequiredService` needs `using Microsoft.Extensions.DependencyInjection;` — already implicitly available
via the Web SDK's implicit usings, but add it explicitly if the compiler complains.)

- [ ] **Step 2: Point the dev ImageRoot at the repo's `dev-data/images` folder**

Add to `src/PictureManager.Api/appsettings.Development.json`:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug"
    }
  },
  "DevImageRoot": {
    "Name": "dev",
    "MountPath": "../../dev-data/images"
  }
}
```

(`../../dev-data/images` is relative to `src/PictureManager.Api`, the working directory `dotnet run` uses by
default — two levels up reaches the repo root.)

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build`
Expected: Build succeeds, 0 warnings, 0 errors, across all projects (including the two new test projects).

- [ ] **Step 4: Run the full test suite (still InMemory, no live DB yet)**

Run: `dotnet test`
Expected: PASS, all tests across all four test projects.

- [ ] **Step 5: Start Postgres and apply the migration for real**

```bash
docker compose up -d db
dotnet ef database update --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api
```

Expected: the `InitialCreate` migration (from phase 2) applies cleanly against the running container; no new
migration is needed this phase (Task 1's case-insensitive lookups are query-level, not schema-level — see
Global Constraints).

- [ ] **Step 6: Manual smoke test**

This step is manual (not automated) — it's the first time the pipeline runs against a real database and real
files, matching this phase's role in the overall plan.

1. Drop at least one real photo with EXIF data (a phone/camera JPEG) into `dev-data/images/` (gitignored, so
   this is a local-only step — see `.gitignore`'s `dev-data/images/*` rule).
2. `dotnet run --project src/PictureManager.Api`
3. `curl -X POST http://localhost:<port>/api/scans -H "Content-Type: application/json" -d "{}"` (empty body
   scans all active roots recursively) — note the returned `scanJobId`.
4. `curl -N http://localhost:<port>/api/scans/<scanJobId>/events` — confirm SSE events stream and the job
   reaches `Completed`.
5. Query the database directly (`psql` via `docker exec`, or any Postgres client) and confirm: a `Folders` row
   for `dev-data/images` exists, an `Images` row exists for the dropped photo with a non-empty `ContentHash`
   and populated `Width`/`Height`/`DateTaken`/`CameraMake` (verifying `MetadataExtractorExifReader`'s real-world
   behavior, per Task 4's deferred-verification note).
6. Stop the app, delete the photo from `dev-data/images/`, run another scan, and confirm the corresponding
   `Images` row now has `MissingSinceUtc` set (not deleted).

If EXIF fields come back empty for a photo you know has EXIF data, revisit `MetadataExtractorExifReader`'s tag
constants (Task 4's implementation note) before considering this phase done.

- [ ] **Step 7: Commit**

```bash
git add src/PictureManager.Api
git commit -m "feat: wire scanning pipeline end-to-end and apply migration to live Postgres"
```

---

## Phase 3 exit criteria

- `dotnet build` succeeds across the whole solution (now 5 src projects + 4 test projects), 0 warnings, 0 errors.
- `dotnet test` passes across all four test projects.
- The phase-1 `db` container was started and the `InitialCreate` migration applied against it for the first time.
- A scan triggered via `POST /api/scans` against a real `dev-data/images` folder reaches `ScanJobStatus.Completed`,
  observable live via `GET /api/scans/{id}/events`.
- At least one real photo was indexed end-to-end with correct EXIF data verified manually (Task 11, Step 6).
- A deleted file is marked `MissingSinceUtc`, not removed from the database.
- Everything committed to git.
