# Phase 5: REST API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose folders, images (keyset listing, search, detail, favorites), albums (CRUD, contents, move,
export), duplicates, settings and root administration as REST endpoints for the phase 6 SPA. The endpoints
sit in user/admin route groups so v2 authorization is one line per group.

**Architecture:** Minimal API endpoint classes hold thin handlers that return `TypedResults`, mapped from
`Result`/`Result<T>` values. Application services in `PictureManager.Application` behind interfaces carry
the logic. Infrastructure repositories project list queries straight into slim records. One EF migration
adds `Image.SortDate` (a stored generated column), `ImageRoot.Alias` and the new indexes. The scanner
learns to skip inactive roots and tombstoned folders, and to prune newly excluded items. Repository tests
move to a real-Postgres fixture.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core 10 + Npgsql 10, PostgreSQL 17, xUnit,
FluentAssertions 7, NSubstitute, `Microsoft.AspNetCore.Mvc.Testing`.

**Spec:** [`docs/superpowers/specs/2026-09-23-phase5-rest-api-design.md`](../specs/2026-09-23-phase5-rest-api-design.md)
(the phase 5 decisions), with the parent [`2026-09-21-picturemanager-v1-design.md`](../specs/2026-09-21-picturemanager-v1-design.md)
and [`Documents/PictureManager-brief.md`](../../../Documents/PictureManager-brief.md).

## Global Constraints

**Frameworks and packages**
- Target framework is `net10.0` everywhere (unchanged).
- Test stack is unchanged: xUnit, **FluentAssertions pinned to `[7.0.0,8.0.0)`**, **NSubstitute** (not Moq).
- New packages are installed with plain `dotnet add package <name>` (no version pin, the same style as
  phases 2–4; record the resolved version in the commit message):
  - `Microsoft.Extensions.Logging.Abstractions` → `PictureManager.Application`;
  - `Microsoft.AspNetCore.Mvc.Testing` → `PictureManager.Api.Tests`.
- `Microsoft.EntityFrameworkCore.Sqlite` is **removed** from `PictureManager.Infrastructure.Tests`.

**Endpoints and wiring**
- **Minimal API endpoints only**, no controllers.
- New handlers are `public static` methods returning `TypedResults` unions, such as
  `Results<Ok<T>, NotFound, ValidationProblem, Conflict<ProblemDetails>>`, so tests can call them
  directly.
- The phase 4 `ImageEndpoints` and the phase 3 `ScanEndpoints` keep returning `IResult`.
- **Every `/api` endpoint belongs to exactly one route group:** `user` or `admin`. The groups are
  created in `Program.cs` as `app.MapGroup("/api")` and tagged with `ApiSurfaceMetadata`. The only
  exceptions are `/api/health` and `/api/ping`. Public paths never gain an `/admin/` segment.
- **No `IOptions<T>`.** Config POCOs are bound in `Program.cs` and registered as singletons, the same way
  as `ThumbnailCacheOptions`.
- JSON uses ASP.NET Core's defaults (camelCase). `DateTaken` is `timestamp without time zone`: values
  written to it **must** have `DateTimeKind.Unspecified`, and it serializes without an offset.
  `timestamptz` columns take `DateTimeKind.Utc`.

**Data rules**
- **Visibility rule.** Used by every image list, folder count and duplicate query, and by favorites and
  detail. An image is visible when `MissingSinceUtc == null && Folder.IsActive && Folder.Root.IsActive`.
  Album contents are the single exception: they list non-visible entries with `isMissing = true`.
- **Owner scoping.** Albums are always scoped to `ICurrentUser.UserId`. The v1 implementation returns
  `AppUser.SystemUserId` (= 1). Another owner's album behaves exactly like an unknown id (`404`).
- **Image URLs.** `thumbnailUrl`/`previewUrl` are `/api/images/{id}/thumbnail?v={ContentHash}` and
  `/api/images/{id}/preview?v={ContentHash}`, or `null` when `ContentHash` is empty.
- **Export line:**
  `{prefix.TrimEnd('/')}/{Alias ?? Name}/{RelativePath}/{FileName}{Extension}`, where an empty
  `RelativePath` drops its segment. Every line ends with `\n`, and the text is UTF-8 without a BOM.

**Errors**
- `400` → `ValidationProblem` (field → messages).
- Unknown or not-visible resource → `404`.
- Name, alias or export-segment clash → `409` `ProblemDetails`.

**Testing**
- Postgres-backed tests need the compose database: `docker compose up -d db`.
- The connection defaults to `Host=localhost;Port=5432;Username=picturemanager;Password=picturemanager`
  and can be overridden with the environment variable `PICTUREMANAGER_TEST_POSTGRES`.
- Existing InMemory tests stay as they are.

## Review Focus

- **Search text containing `%` or `_`** (e.g. searching `IMG_` or `100%`) matches those characters
  literally, not as SQL wildcards. Test: Task 4, `ListAsync_FileNameFilter_TreatsLikeWildcardsLiterally`.
- **Many images sharing one `SortDate`** (burst shots, a copied folder with identical mtimes) still page
  with every image exactly once. Ties break on `Id`. Test: Task 4,
  `ListAsync_ManyImagesWithSameSortDate_PagesEachImageExactlyOnce`.
- **Name sort over accented or mixed-case names** (`Ádám`, `zebra`, `Apple`) pages every image exactly
  once. The cursor key comes from the database's own `lower()`, not C#'s. Test: Task 4,
  `ListAsync_NameSortAcrossPages_WithAccentedAndMixedCaseNames_ReturnsEachImageOnce`.
- **Adding the same image id twice in one request** (`[5, 5]`, e.g. a double-clicked selection) adds it
  once, with no primary-key violation. Test: Task 10,
  `AddImagesAsync_DuplicateIdsInRequest_AddsEachImageOnce`.
- **Exporting an album with a non-ASCII name** (`Nyaralás 2025`) downloads with a correct filename and a
  BOM-less UTF-8 body. Tests: Task 11, `ExportAsync_ReturnsUtf8TextWithoutBom_AndAlbumFileName`, and
  Task 15, `Export_NonAsciiAlbumName_SetsRfc5987FileName`.

## File map

| Area | Files |
|---|---|
| Test support | `tests/PictureManager.Infrastructure.Tests/Support/PostgresTestDatabase.cs`, `.../Support/TestData.cs` (both linked into Api.Tests in Task 15) |
| Schema | `Model/Image.cs`, `Model/ImageRoot.cs`, `Model/AppUser.cs`, `Infrastructure/Persistence/Configurations/{Image,ImageRoot,AppUser}Configuration.cs`, new migration `Phase5RestApi` |
| Common | `Application/Common/{Result,PagedResult,CursorCodec,ICurrentUser,SystemCurrentUser,ImageUrls,FolderDisplayPath}.cs`, `Api/Endpoints/{ResultHttpExtensions,ApiSurface,PatchJson}.cs` |
| Images | `Application/Images/*`, `Application/Repositories/IImageQueryRepository.cs`, `Infrastructure/Persistence/Queries/VisibilityExtensions.cs`, `Infrastructure/Persistence/Repositories/ImageQueryRepository.cs`, `Api/Endpoints/ImageQueryEndpoints.cs` |
| Folders | `Application/Folders/*`, `IFolderRepository`/`FolderRepository` additions, `Api/Endpoints/FolderEndpoints.cs` |
| Roots | `Application/Roots/*` (replaces `Application/Scanning/DevImageRoot*`), `IImageRootRepository`/`ImageRootRepository` additions, `Api/Endpoints/RootEndpoints.cs` |
| Albums | `Application/Albums/*`, `IAlbumRepository`/`AlbumRepository` additions, `Api/Endpoints/AlbumEndpoints.cs` |
| Duplicates | `Application/Duplicates/*`, `IImageQueryRepository` additions, `Api/Endpoints/DuplicateEndpoints.cs` |
| Settings | `Application/Settings/*`, `IAppSettingsRepository`/`AppSettingsRepository` additions, `Api/Endpoints/SettingsEndpoints.cs` |
| Scanner | `Application/Scanning/ScanService.cs`, `Application/Scanning/ScanRootUnavailableException.cs`, `Api/Endpoints/ScanEndpoints.cs` |
| Host | `Api/Program.cs`, `Api/appsettings.Development.json`, DI extension classes |

`Application/…` means `src/PictureManager.Application/…`, and likewise for the other projects.

---

## Task 1: Real-Postgres test fixture (replacing SQLite)

**Why first:** Task 2 adds a Postgres-only generated column. SQLite's `EnsureCreated` would then fail to
parse the table, so the SQLite-based `ScanJobRepositoryTests` must move to Postgres before the schema
changes.

**Files:**
- Create: `tests/PictureManager.Infrastructure.Tests/Support/PostgresTestDatabase.cs`
- Create: `tests/PictureManager.Infrastructure.Tests/Support/PostgresTestDatabaseTests.cs`
- Modify: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ScanJobRepositoryTests.cs`
  (lines 1-60: remove the `SqliteTestDatabase` class and the Sqlite `using`; rename usages)
- Modify: `tests/PictureManager.Infrastructure.Tests/PictureManager.Infrastructure.Tests.csproj` (remove
  the Sqlite package)

**Interfaces:**
- Produces: `PictureManager.Tests.Support.PostgresTestDatabase`, with
  - `static Task<PostgresTestDatabase> CreateAsync()`
  - `PictureManagerDbContext Context { get; }`
  - `PictureManagerDbContext CreateContext()`
  - `string ConnectionString { get; }`
  - `ValueTask DisposeAsync()`

  Each call gives a fresh, fully migrated database; dispose drops it.

- [ ] **Step 1: Make sure Postgres is running**

Run: `docker compose up -d db` and then `docker compose ps db`
Expected: the `db` service is `running (healthy)`. (If a container from another worktree already
publishes `127.0.0.1:5432`, e.g. `phase3-scanning-pipeline-db-1`, that one serves just as well. Leave it
running.)

- [ ] **Step 2: Write the failing fixture tests**

Create `tests/PictureManager.Infrastructure.Tests/Support/PostgresTestDatabaseTests.cs`:

```csharp
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Tests.Support;

public class PostgresTestDatabaseTests
{
    [Fact]
    public async Task CreateAsync_ReturnsMigratedDatabase_WithSeedData()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();

        (await db.Context.AppUsers.AnyAsync(u => u.Id == AppUserSeed.SystemUserId)).Should().BeTrue();
        (await db.Context.Settings.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_TwoDatabases_AreIsolated()
    {
        await using var first = await PostgresTestDatabase.CreateAsync();
        await using var second = await PostgresTestDatabase.CreateAsync();

        first.Context.ImageRoots.Add(new ImageRoot
        {
            Name = "only-in-first", MountPath = "/x", IsActive = true,
            CreatedUtc = System.DateTime.UtcNow
        });
        await first.Context.SaveChangesAsync();

        (await second.Context.ImageRoots.AnyAsync(r => r.Name == "only-in-first")).Should().BeFalse();
        first.ConnectionString.Should().NotBe(second.ConnectionString);
    }

    [Fact]
    public async Task CreateContext_ReturnsIndependentContextOnTheSameDatabase()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        db.Context.ImageRoots.Add(new ImageRoot
        {
            Name = "shared", MountPath = "/s", IsActive = true, CreatedUtc = System.DateTime.UtcNow
        });
        await db.Context.SaveChangesAsync();

        await using var other = db.CreateContext();
        (await other.ImageRoots.Select(r => r.Name).ToListAsync()).Should().Contain("shared");
    }

    // The system user id lives in the Infrastructure configuration today; Task 2 moves the constant
    // to the Model. Referencing it through this alias keeps this test file stable across that move.
    private static class AppUserSeed
    {
        public const int SystemUserId = PictureManager.Infrastructure.Persistence.Configurations.AppUserConfiguration.SystemUserId;
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~PostgresTestDatabaseTests"`
Expected: build FAILS with `The type or namespace name 'PostgresTestDatabase' could not be found`.

- [ ] **Step 4: Implement the fixture**

Create `tests/PictureManager.Infrastructure.Tests/Support/PostgresTestDatabase.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PictureManager.Infrastructure.Persistence;

namespace PictureManager.Tests.Support;

/// <summary>
/// A throwaway, fully migrated Postgres database for one test. The first use in a test process
/// migrates a template database; every test then gets a cheap copy of it via
/// CREATE DATABASE ... TEMPLATE, and disposing drops the copy. Needs the compose `db` service
/// (`docker compose up -d db`); override the server with PICTUREMANAGER_TEST_POSTGRES.
/// </summary>
public sealed class PostgresTestDatabase : IAsyncDisposable
{
    public const string ConnectionStringVariable = "PICTUREMANAGER_TEST_POSTGRES";
    private const string DefaultServerConnectionString =
        "Host=localhost;Port=5432;Username=picturemanager;Password=picturemanager";

    // Each test assembly (Infrastructure.Tests, Api.Tests) links this file, and `dotnet test` runs
    // assemblies in parallel, so the template name is per assembly to keep them from racing.
    private static readonly string AssemblyTag = typeof(PostgresTestDatabase).Assembly.GetName().Name!
        .Replace("PictureManager.", string.Empty).Replace(".", string.Empty).ToLowerInvariant();
    private static readonly string TemplateName = $"pm_tpl_{AssemblyTag}";
    private static readonly SemaphoreSlim TemplateLock = new(1, 1);
    private static bool _templateReady;

    private readonly string _databaseName;

    private PostgresTestDatabase(string databaseName)
    {
        _databaseName = databaseName;
        ConnectionString = BuildConnectionString(databaseName);
        Context = CreateContext();
    }

    public string ConnectionString { get; }

    public PictureManagerDbContext Context { get; }

    public PictureManagerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PictureManagerDbContext>().UseNpgsql(ConnectionString).Options);

    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        await EnsureTemplateAsync();
        var databaseName = $"pm_t_{AssemblyTag}_{Guid.NewGuid():N}";
        await ExecuteOnServerAsync($"CREATE DATABASE \"{databaseName}\" TEMPLATE \"{TemplateName}\"");
        return new PostgresTestDatabase(databaseName);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));
        await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    private static async Task EnsureTemplateAsync()
    {
        if (_templateReady)
            return;

        await TemplateLock.WaitAsync();
        try
        {
            if (_templateReady)
                return;

            await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{TemplateName}\" WITH (FORCE)");
            await ExecuteOnServerAsync($"CREATE DATABASE \"{TemplateName}\"");

            var templateConnectionString = BuildConnectionString(TemplateName);
            await using (var context = new PictureManagerDbContext(
                new DbContextOptionsBuilder<PictureManagerDbContext>().UseNpgsql(templateConnectionString).Options))
            {
                await context.Database.MigrateAsync();
            }

            // CREATE DATABASE ... TEMPLATE fails while any connection to the template is open.
            NpgsqlConnection.ClearPool(new NpgsqlConnection(templateConnectionString));
            _templateReady = true;
        }
        finally
        {
            TemplateLock.Release();
        }
    }

    private static async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(BuildConnectionString("postgres"));
        try
        {
            await connection.OpenAsync();
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            throw new InvalidOperationException(
                $"Postgres-backed tests need a reachable server ({ServerConnectionString()}). " +
                $"Start it with `docker compose up -d db`, or set {ConnectionStringVariable}.", ex);
        }

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string ServerConnectionString() =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable) ?? DefaultServerConnectionString;

    private static string BuildConnectionString(string databaseName) =>
        new NpgsqlConnectionStringBuilder(ServerConnectionString()) { Database = databaseName }.ConnectionString;
}
```

- [ ] **Step 5: Run the fixture tests to verify they pass**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~PostgresTestDatabaseTests"`
Expected: PASS, 3 tests, 0 failed.

- [ ] **Step 6: Move `ScanJobRepositoryTests` off SQLite**

In `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ScanJobRepositoryTests.cs`:
1. Delete the whole `private sealed class SqliteTestDatabase : IAsyncDisposable { ... }` block
   (currently lines 20-60), **including** the comment block directly above it that explains why SQLite is
   used. Replace that comment with:

```csharp
    // ExecuteUpdateAsync-based methods need a real relational provider (InMemory doesn't implement
    // ExecuteUpdate). These tests use a throwaway real-Postgres database per test.
```

2. Replace `using Microsoft.Data.Sqlite;` with `using PictureManager.Tests.Support;`.
3. Replace every `await using var db = await SqliteTestDatabase.CreateAsync();` with
   `await using var db = await PostgresTestDatabase.CreateAsync();` (8 occurrences). `db.Context` keeps
   working unchanged.

Then remove the Sqlite package:

Run: `dotnet remove tests/PictureManager.Infrastructure.Tests package Microsoft.EntityFrameworkCore.Sqlite`
Expected: `info : Removing PackageReference for package 'Microsoft.EntityFrameworkCore.Sqlite'`.

- [ ] **Step 7: Run the whole Infrastructure test project**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests`
Expected: PASS, 0 failed (the previous 62 tests plus the 3 new fixture tests = 65).

- [ ] **Step 8: Commit**

```bash
git add tests/PictureManager.Infrastructure.Tests
git commit -m "test: add real-Postgres per-test database fixture, move ScanJob repo tests off SQLite"
```

---

## Task 2: Schema: SortDate, root Alias, indexes (migration `Phase5RestApi`)

**Files:**
- Modify: `src/PictureManager.Model/Image.cs`, `src/PictureManager.Model/ImageRoot.cs`,
  `src/PictureManager.Model/AppUser.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Configurations/ImageConfiguration.cs`,
  `.../ImageRootConfiguration.cs`, `.../AppUserConfiguration.cs`
- Modify: `src/PictureManager.Api/Program.cs` (make the top-level `catch` ignore `HostAbortedException`
  so `dotnet ef` design-time host building isn't logged as a crash)
- Create: `src/PictureManager.Infrastructure/Migrations/<timestamp>_Phase5RestApi.cs` (+ `.Designer.cs`,
  updated snapshot), generated by the tool
- Create: `tests/PictureManager.Infrastructure.Tests/Support/TestData.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Phase5SchemaTests.cs`

**Interfaces:**
- Produces:
  - `Image.SortDate` (`DateTime`, UTC, database-computed, read-only by convention);
  - `ImageRoot.Alias` (`string?`);
  - `AppUser.SystemUserId` (`const int = 1`);
  - `TestData` helpers:
    - `TestData.Utc`
    - `TestData.Root(string name, string? alias = null, bool isActive = true)`
    - `TestData.Folder(ImageRoot root, string relativePath, Folder? parent = null, bool isActive = true)`
    - `TestData.Image(Folder folder, string fileName, string extension = ".jpg", DateTime? dateTaken = null, DateTime? fileModified = null, string? contentHash = null, bool isFavorite = false, DateTime? missingSinceUtc = null)`
    - `TestData.Album(string name, int ownerUserId = AppUser.SystemUserId)`
    - `TestData.AlbumImage(Album album, Image image, int sortOrder)`

- [ ] **Step 1: Add the test-data helpers**

Create `tests/PictureManager.Infrastructure.Tests/Support/TestData.cs`:

```csharp
using System;
using PictureManager.Model;

namespace PictureManager.Tests.Support;

/// <summary>Terse builders for Postgres-backed tests. Add the returned graph to a context and save.</summary>
public static class TestData
{
    public static readonly DateTime Utc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static ImageRoot Root(string name, string? alias = null, bool isActive = true) => new()
    {
        Name = name,
        Alias = alias,
        MountPath = "/mnt/" + name,
        IsActive = isActive,
        CreatedUtc = Utc
    };

    public static Folder Folder(ImageRoot root, string relativePath, Folder? parent = null, bool isActive = true) => new()
    {
        Root = root,
        Parent = parent,
        Name = relativePath.Length == 0 ? root.Name : relativePath[(relativePath.LastIndexOf('/') + 1)..],
        RelativePath = relativePath,
        IsActive = isActive,
        CreatedUtc = Utc,
        ModifiedUtc = Utc
    };

    /// <param name="dateTaken">Local-naive camera time; must have DateTimeKind.Unspecified.</param>
    /// <param name="contentHash">Defaults to a unique hash so tests never create accidental duplicates.</param>
    public static Image Image(
        Folder folder,
        string fileName,
        string extension = ".jpg",
        DateTime? dateTaken = null,
        DateTime? fileModified = null,
        string? contentHash = null,
        bool isFavorite = false,
        DateTime? missingSinceUtc = null) => new()
    {
        Folder = folder,
        FileName = fileName,
        Extension = extension,
        ContentHash = contentHash ?? Guid.NewGuid().ToString("N")[..16].ToUpperInvariant(),
        FileSize = 1234,
        FileModified = fileModified ?? Utc,
        DateTaken = dateTaken,
        IsFavorite = isFavorite,
        IndexState = IndexState.Indexed,
        FirstSeenUtc = Utc,
        MissingSinceUtc = missingSinceUtc,
        CreatedAt = Utc,
        UpdatedAt = Utc
    };

    public static Album Album(string name, int ownerUserId = AppUser.SystemUserId) => new()
    {
        Name = name,
        OwnerUserId = ownerUserId,
        CreatedAt = Utc,
        UpdatedAt = Utc
    };

    public static AlbumImage AlbumImage(Album album, Image image, int sortOrder) => new()
    {
        Album = album,
        Image = image,
        SortOrder = sortOrder,
        AddedAt = Utc
    };
}
```

- [ ] **Step 2: Write the failing schema tests**

Create `tests/PictureManager.Infrastructure.Tests/Persistence/Phase5SchemaTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence;

public class Phase5SchemaTests
{
    [Fact]
    public async Task SortDate_UsesDateTakenAsUtcWallClock_WhenPresent()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var folder = TestData.Folder(root, "");
        var image = TestData.Image(folder, "a", dateTaken: new DateTime(2025, 8, 14, 18, 32, 5),
            fileModified: new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc));
        db.Context.Images.Add(image);
        await db.Context.SaveChangesAsync();

        await using var read = db.CreateContext();
        var stored = await read.Images.SingleAsync(i => i.Id == image.Id);
        stored.SortDate.Should().Be(new DateTime(2025, 8, 14, 18, 32, 5, DateTimeKind.Utc));
    }

    [Fact]
    public async Task SortDate_FallsBackToFileModified_WhenNoDateTaken()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var modified = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var image = TestData.Image(folder, "b", fileModified: modified);
        db.Context.Images.Add(image);
        await db.Context.SaveChangesAsync();

        await using var read = db.CreateContext();
        (await read.Images.SingleAsync(i => i.Id == image.Id)).SortDate.Should().Be(modified);
    }

    [Fact]
    public async Task ImageRootAlias_RoundTrips()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        db.Context.ImageRoots.Add(TestData.Root("nas-photos", alias: "family_photos"));
        await db.Context.SaveChangesAsync();

        await using var read = db.CreateContext();
        (await read.ImageRoots.SingleAsync(r => r.Name == "nas-photos")).Alias.Should().Be("family_photos");
    }

    [Fact]
    public async Task ImageRootName_IsUniqueCaseInsensitively()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        db.Context.ImageRoots.Add(TestData.Root("Photos"));
        await db.Context.SaveChangesAsync();

        await using var second = db.CreateContext();
        second.ImageRoots.Add(TestData.Root("photos"));
        var act = () => second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ImageRootExportSegment_AliasOrName_IsUniqueAcrossRoots()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        db.Context.ImageRoots.Add(TestData.Root("nas-photos", alias: "Family"));
        await db.Context.SaveChangesAsync();

        await using var second = db.CreateContext();
        second.ImageRoots.Add(TestData.Root("family"));
        var act = () => second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AlbumName_IsUniquePerOwnerCaseInsensitively()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        db.Context.Albums.Add(TestData.Album("Holidays"));
        await db.Context.SaveChangesAsync();

        await using var second = db.CreateContext();
        second.Albums.Add(TestData.Album("HOLIDAYS"));
        var act = () => second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AlbumName_SameNameForDifferentOwners_IsAllowed()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var otherUser = new AppUser { DisplayName = "Other", Role = UserRole.User };
        db.Context.AppUsers.Add(otherUser);
        await db.Context.SaveChangesAsync();

        db.Context.Albums.Add(TestData.Album("Holidays"));
        db.Context.Albums.Add(TestData.Album("Holidays", otherUser.Id));
        var act = () => db.Context.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~Phase5SchemaTests"`
Expected: build FAILS: `'Image' does not contain a definition for 'SortDate'`, `'ImageRoot' does not
contain a definition for 'Alias'`, `'AppUser' does not contain a definition for 'SystemUserId'`.

- [ ] **Step 4: Change the model**

`src/PictureManager.Model/Image.cs`: add after `DateTaken`:

```csharp
    // Database-computed (stored generated column): COALESCE(DateTaken read as UTC wall-clock,
    // FileModified). The keyset key for date sorting. Never assign it from application code.
    public DateTime SortDate { get; set; }
```

`src/PictureManager.Model/ImageRoot.cs`: add after `MountPath`:

```csharp
    // Export-only name for this root's first path segment; exports use Alias ?? Name.
    public string? Alias { get; set; }
```

`src/PictureManager.Model/AppUser.cs`: add as the first member of the class:

```csharp
    // v1 placeholder owner seeded by the initial migration; every album belongs to it until v2 auth.
    public const int SystemUserId = 1;
```

- [ ] **Step 5: Change the EF configurations**

`ImageConfiguration.cs`: add after the `DateTaken` property block:

```csharp
        // Stored generated column. "DateTaken" is local-naive; AT TIME ZONE 'UTC' reads its wall
        // clock as UTC so it can be COALESCEd with the timestamptz "FileModified" (immutable, as
        // generated columns require). Undated images therefore sort by file mtime.
        builder.Property(x => x.SortDate)
            .HasColumnType("timestamp with time zone")
            .HasComputedColumnSql("COALESCE(\"DateTaken\" AT TIME ZONE 'UTC', \"FileModified\")", stored: true);
```

and add at the end of `Configure`, after `builder.HasIndex(x => x.IsFavorite);`:

```csharp
        // Folder grid, date sort. The lower(FileName) and favorites indexes are expression/partial
        // indexes that EF's fluent API can't express; they are raw SQL in the Phase5RestApi migration.
        builder.HasIndex(x => new { x.FolderId, x.SortDate, x.Id });
```

`ImageRootConfiguration.cs`: add after the `MountPath` block:

```csharp
        builder.Property(x => x.Alias)
            .HasMaxLength(200);
```

and **delete** the case-sensitive unique index (it's replaced by a raw-SQL `lower("Name")` unique index in
the migration):

```csharp
        builder.HasIndex(x => x.Name)
            .IsUnique();
```

`AppUserConfiguration.cs`: replace `public const int SystemUserId = 1;` with:

```csharp
    public const int SystemUserId = AppUser.SystemUserId;
```

- [ ] **Step 6: Let `dotnet ef` build the host cleanly**

In `src/PictureManager.Api/Program.cs` change the top-level catch from

```csharp
catch (Exception ex)
{
```

to

```csharp
catch (Exception ex) when (ex is not HostAbortedException)
{
```

(`dotnet ef` stops the host after `Build()` by throwing `HostAbortedException`. Without the filter, that
gets logged as "terminated unexpectedly" with exit code 1.) `HostAbortedException` lives in
`Microsoft.Extensions.Hosting`, which the Web SDK's implicit usings already import.

- [ ] **Step 7: Generate the migration**

Run: `dotnet ef migrations add Phase5RestApi --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api`
Expected: `Done. To undo this action, use 'ef migrations remove'`, and a new
`src/PictureManager.Infrastructure/Migrations/*_Phase5RestApi.cs`.

Open the generated `Up` method and check it contains exactly these schema operations (the order may
differ):
- `AddColumn<string>` for `Alias`, `maxLength: 200`, nullable;
- `AddColumn<DateTime>` for `SortDate` with `computedColumnSql: "COALESCE(\"DateTaken\" AT TIME ZONE 'UTC', \"FileModified\")", stored: true`;
- `DropIndex` `IX_ImageRoots_Name`;
- `CreateIndex` `IX_Images_FolderId_SortDate_Id`.

If it contains anything else (e.g. changes to tables this task didn't touch), stop: the model snapshot
has drifted, and that needs a ruling before continuing.

- [ ] **Step 8: Add the raw-SQL indexes to the migration**

At the **end** of `Up(MigrationBuilder migrationBuilder)` add:

```csharp
            // Expression / partial indexes EF's fluent API can't express (phase 5 spec, "Data model changes").
            migrationBuilder.Sql("CREATE INDEX \"IX_Images_FolderId_LowerFileName_Id\" ON \"Images\" (\"FolderId\", lower(\"FileName\"), \"Id\");");
            migrationBuilder.Sql("CREATE INDEX \"IX_Images_Favorites_SortDate_Id\" ON \"Images\" (\"SortDate\", \"Id\") WHERE \"IsFavorite\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_Albums_OwnerUserId_LowerName\" ON \"Albums\" (\"OwnerUserId\", lower(\"Name\"));");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_ImageRoots_LowerName\" ON \"ImageRoots\" (lower(\"Name\"));");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_ImageRoots_ExportSegment\" ON \"ImageRoots\" (lower(COALESCE(\"Alias\", \"Name\")));");
```

At the **start** of `Down(MigrationBuilder migrationBuilder)` add:

```csharp
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ImageRoots_ExportSegment\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ImageRoots_LowerName\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Albums_OwnerUserId_LowerName\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Images_Favorites_SortDate_Id\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Images_FolderId_LowerFileName_Id\";");
```

- [ ] **Step 9: Run the schema tests to verify they pass**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~Phase5SchemaTests"`
Expected: PASS, 7 tests, 0 failed. (The fixture migrates its template from the new migration, so
this also proves the migration applies to a real Postgres.)

- [ ] **Step 10: Apply the migration to the dev database and run the full suite**

Run: `dotnet ef database update --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api`
Expected: `Applying migration '..._Phase5RestApi'.` then `Done.` If it fails with a unique-index
violation on `ImageRoots`, the dev database has two roots whose names or export segments differ only by
case. Rename one with `psql` and re-run.

Run: `dotnet test`
Expected: PASS, 0 failed across all four test projects.

- [ ] **Step 11: Commit**

```bash
git add src/PictureManager.Model src/PictureManager.Infrastructure src/PictureManager.Api/Program.cs tests/PictureManager.Infrastructure.Tests
git commit -m "feat: add Image.SortDate, ImageRoot.Alias and phase 5 indexes (migration Phase5RestApi)"
```

---

## Task 3: Common building blocks: Result, cursors, URLs, current user, route surfaces

**Files:**
- Create: `src/PictureManager.Application/Common/Result.cs`
- Create: `src/PictureManager.Application/Common/PagedResult.cs`
- Create: `src/PictureManager.Application/Common/CursorCodec.cs`
- Create: `src/PictureManager.Application/Common/ICurrentUser.cs`
- Create: `src/PictureManager.Application/Common/SystemCurrentUser.cs`
- Create: `src/PictureManager.Application/Common/ImageUrls.cs`
- Create: `src/PictureManager.Application/Common/FolderDisplayPath.cs`
- Modify: `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- Create: `src/PictureManager.Api/Endpoints/ResultHttpExtensions.cs`
- Create: `src/PictureManager.Api/Endpoints/ApiSurface.cs`
- Create: `src/PictureManager.Api/Endpoints/PatchJson.cs`
- Test: `tests/PictureManager.Application.Tests/Common/CommonBuildingBlocksTests.cs`
- Test: `tests/PictureManager.Api.Tests/Endpoints/ResultHttpExtensionsTests.cs`

**Interfaces:**
- Produces (Application, namespace `PictureManager.Application.Common`):
  - `enum ResultStatus { Success, NotFound, Invalid, Conflict }`
  - `sealed record Result(ResultStatus Status, IReadOnlyDictionary<string, string[]>? Errors = null, string? Message = null)`,
    with `IsSuccess` and the static factories `Ok()`, `NotFound()`, `Invalid(string field, string message)`,
    `Conflict(string message)`
  - `sealed record Result<T>(ResultStatus Status, T? Value, IReadOnlyDictionary<string, string[]>? Errors = null, string? Message = null)`,
    with `IsSuccess`, `static Ok(T value)`, and an implicit conversion from a *failed* `Result`
  - `sealed record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor)`
  - `static class CursorCodec` with `string Encode<T>(T payload)` and
    `bool TryDecode<T>(string? cursor, out T? payload) where T : class`
  - `interface ICurrentUser { int UserId { get; } }`, and `SystemCurrentUser : ICurrentUser`
    (registered as a singleton)
  - `static class ImageUrls` with `string? Thumbnail(int id, string contentHash)` and
    `string? Preview(int id, string contentHash)`
  - `static class FolderDisplayPath` with `string For(string rootName, string relativePath)`
- Produces (Api, namespace `PictureManager.Api.Endpoints`):
  - `ResultHttpExtensions.ToOk<T>(this Result<T>)` → `Results<Ok<T>, NotFound, ValidationProblem, Conflict<ProblemDetails>>`
  - `ResultHttpExtensions.ToNoContent(this Result)` → `Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>`
  - internal helpers `ResultHttpExtensions.ToErrors(IReadOnlyDictionary<string, string[]>?)` and
    `ResultHttpExtensions.ConflictProblem(string?)`, reused by handlers that build non-standard unions
    (album create in Task 11)
  - `enum ApiSurface { User, Admin }` and `sealed record ApiSurfaceMetadata(ApiSurface Surface)`
  - `static class PatchJson` with
    `bool TryReadString(JsonElement body, string property, out bool present, out string? value)` and
    `bool TryReadBool(JsonElement body, string property, out bool present, out bool? value)`

- [ ] **Step 1: Write the failing Application tests**

Create `tests/PictureManager.Application.Tests/Common/CommonBuildingBlocksTests.cs`:

```csharp
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Application.Common;
using PictureManager.Application.DependencyInjection;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Common;

public class CommonBuildingBlocksTests
{
    private sealed record SampleCursor(
        [property: JsonPropertyName("k")] string Key,
        [property: JsonPropertyName("i")] int Id);

    [Fact]
    public void CursorCodec_RoundTrips()
    {
        var encoded = CursorCodec.Encode(new SampleCursor("abc", 42));

        CursorCodec.TryDecode<SampleCursor>(encoded, out var decoded).Should().BeTrue();
        decoded.Should().Be(new SampleCursor("abc", 42));
    }

    [Fact]
    public void CursorCodec_EncodedCursorIsUrlSafe()
    {
        var encoded = CursorCodec.Encode(new SampleCursor(new string('?', 40) + "~~>>", 7));

        encoded.Should().NotContainAny("+", "/", "=");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("bm90IGpzb24")] // base64url of "not json"
    public void CursorCodec_TryDecode_Garbage_ReturnsFalse(string? cursor)
    {
        CursorCodec.TryDecode<SampleCursor>(cursor, out var decoded).Should().BeFalse();
        decoded.Should().BeNull();
    }

    [Fact]
    public void ImageUrls_WithHash_AreVersioned()
    {
        ImageUrls.Thumbnail(12, "D5A2").Should().Be("/api/images/12/thumbnail?v=D5A2");
        ImageUrls.Preview(12, "D5A2").Should().Be("/api/images/12/preview?v=D5A2");
    }

    [Fact]
    public void ImageUrls_WithoutHash_AreNull()
    {
        ImageUrls.Thumbnail(12, string.Empty).Should().BeNull();
        ImageUrls.Preview(12, string.Empty).Should().BeNull();
    }

    [Theory]
    [InlineData("nas", "", "nas")]
    [InlineData("nas", "Holidays/Madeira", "nas/Holidays/Madeira")]
    public void FolderDisplayPath_JoinsRootNameAndRelativePath(string root, string relative, string expected)
    {
        FolderDisplayPath.For(root, relative).Should().Be(expected);
    }

    [Fact]
    public void Result_FailureConvertsToGenericResult_KeepingStatusAndErrors()
    {
        Result<int> converted = Result.Invalid("limit", "Must be between 1 and 200.");

        converted.Status.Should().Be(ResultStatus.Invalid);
        converted.Errors!["limit"].Should().Equal("Must be between 1 and 200.");
    }

    [Fact]
    public void Result_SuccessDoesNotConvertToGenericResult()
    {
        var act = () => { Result<int> _ = Result.Ok(); };

        act.Should().Throw<System.InvalidOperationException>();
    }

    [Fact]
    public void AddApplication_RegistersSystemCurrentUser_AsSystemUser()
    {
        var provider = new ServiceCollection().AddApplication().BuildServiceProvider();

        provider.GetRequiredService<ICurrentUser>().UserId.Should().Be(AppUser.SystemUserId);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~CommonBuildingBlocksTests"`
Expected: build FAILS (`CursorCodec`, `ImageUrls`, `FolderDisplayPath`, `Result`, `ICurrentUser` not found).

- [ ] **Step 3: Implement the Application building blocks**

`src/PictureManager.Application/Common/Result.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace PictureManager.Application.Common;

public enum ResultStatus
{
    Success,
    NotFound,
    Invalid,
    Conflict
}

/// <summary>Outcome of a service operation that has no value. Endpoints map it to HTTP.</summary>
public sealed record Result(ResultStatus Status, IReadOnlyDictionary<string, string[]>? Errors = null, string? Message = null)
{
    public bool IsSuccess => Status == ResultStatus.Success;

    public static Result Ok() => new(ResultStatus.Success);

    public static Result NotFound() => new(ResultStatus.NotFound);

    public static Result Invalid(string field, string message) =>
        new(ResultStatus.Invalid, new Dictionary<string, string[]> { [field] = new[] { message } });

    public static Result Conflict(string message) => new(ResultStatus.Conflict, Message: message);
}

/// <summary>Outcome of a service operation that returns a value on success.</summary>
public sealed record Result<T>(ResultStatus Status, T? Value, IReadOnlyDictionary<string, string[]>? Errors = null, string? Message = null)
{
    public bool IsSuccess => Status == ResultStatus.Success;

    public static Result<T> Ok(T value) => new(ResultStatus.Success, value);

    // Lets a method returning Result<T> write `return Result.NotFound();`.
    public static implicit operator Result<T>(Result failure) =>
        failure.IsSuccess
            ? throw new InvalidOperationException("Only a failed Result converts to Result<T>; use Result<T>.Ok(value).")
            : new Result<T>(failure.Status, default, failure.Errors, failure.Message);
}
```

`src/PictureManager.Application/Common/PagedResult.cs`:

```csharp
using System.Collections.Generic;

namespace PictureManager.Application.Common;

/// <summary>One keyset page. NextCursor is null on the last page.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor);
```

`src/PictureManager.Application/Common/CursorCodec.cs`:

```csharp
using System;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PictureManager.Application.Common;

/// <summary>Opaque keyset cursors: base64url(JSON). Clients never build or parse them.</summary>
public static class CursorCodec
{
    public static string Encode<T>(T payload) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));

    public static bool TryDecode<T>(string? cursor, [NotNullWhen(true)] out T? payload) where T : class
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(cursor))
            return false;

        try
        {
            payload = JsonSerializer.Deserialize<T>(Base64Url.DecodeFromChars(cursor));
            return payload is not null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or NotSupportedException)
        {
            payload = null;
            return false;
        }
    }
}
```

`src/PictureManager.Application/Common/ICurrentUser.cs`:

```csharp
namespace PictureManager.Application.Common;

/// <summary>
/// Who is calling. The v2 auth seam: v1 always answers the seeded system user; v2 swaps only this
/// implementation to read the authenticated principal.
/// </summary>
public interface ICurrentUser
{
    int UserId { get; }
}
```

`src/PictureManager.Application/Common/SystemCurrentUser.cs`:

```csharp
using PictureManager.Model;

namespace PictureManager.Application.Common;

public sealed class SystemCurrentUser : ICurrentUser
{
    public int UserId => AppUser.SystemUserId;
}
```

`src/PictureManager.Application/Common/ImageUrls.cs`:

```csharp
using System;

namespace PictureManager.Application.Common;

/// <summary>
/// Derivative URLs carry ?v={ContentHash}: a changed file gets a new URL, which is what makes the
/// phase 4 `immutable` Cache-Control safe on these id-keyed routes. Null until enrichment hashed the file.
/// </summary>
public static class ImageUrls
{
    public static string? Thumbnail(int id, string contentHash) => Build(id, "thumbnail", contentHash);

    public static string? Preview(int id, string contentHash) => Build(id, "preview", contentHash);

    private static string? Build(int id, string kind, string contentHash) =>
        string.IsNullOrEmpty(contentHash) ? null : $"/api/images/{id}/{kind}?v={Uri.EscapeDataString(contentHash)}";
}
```

`src/PictureManager.Application/Common/FolderDisplayPath.cs`:

```csharp
namespace PictureManager.Application.Common;

public static class FolderDisplayPath
{
    public static string For(string rootName, string relativePath) =>
        string.IsNullOrEmpty(relativePath) ? rootName : $"{rootName}/{relativePath}";
}
```

In `ApplicationServiceCollectionExtensions.AddApplication` add after the `IClock` registration:

```csharp
        services.AddSingleton<ICurrentUser, SystemCurrentUser>();
```

- [ ] **Step 4: Run the Application tests to verify they pass**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~CommonBuildingBlocksTests"`
Expected: PASS, 13 tests (7 facts + 4 + 2 theory rows), 0 failed.

- [ ] **Step 5: Write the failing Api tests**

Create `tests/PictureManager.Api.Tests/Endpoints/ResultHttpExtensionsTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Common;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class ResultHttpExtensionsTests
{
    [Fact]
    public void ToOk_Success_ReturnsOkWithValue()
    {
        var http = Result<string>.Ok("hi").ToOk();

        http.Result.Should().BeOfType<Ok<string>>().Which.Value.Should().Be("hi");
    }

    [Fact]
    public void ToOk_NotFound_ReturnsNotFound()
    {
        Result<string> result = Result.NotFound();

        result.ToOk().Result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public void ToOk_Invalid_ReturnsValidationProblemWithFieldErrors()
    {
        Result<string> result = Result.Invalid("limit", "Must be between 1 and 200.");

        var problem = result.ToOk().Result.Should().BeOfType<ValidationProblem>().Subject;
        problem.ProblemDetails.Errors["limit"].Should().Equal("Must be between 1 and 200.");
    }

    [Fact]
    public void ToOk_Conflict_Returns409ProblemDetailsWithMessage()
    {
        Result<string> result = Result.Conflict("Name taken.");

        var conflict = result.ToOk().Result.Should().BeOfType<Conflict<ProblemDetails>>().Subject;
        conflict.Value!.Detail.Should().Be("Name taken.");
        conflict.Value.Status.Should().Be(409);
    }

    [Fact]
    public void ToNoContent_Success_ReturnsNoContent()
    {
        Result.Ok().ToNoContent().Result.Should().BeOfType<NoContent>();
    }

    [Fact]
    public void ToNoContent_NotFound_ReturnsNotFound()
    {
        Result.NotFound().ToNoContent().Result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public void PatchJson_DistinguishesAbsentNullAndValue()
    {
        var body = JsonDocument.Parse("""{ "name": "x", "alias": null, "isActive": false }""").RootElement;

        PatchJson.TryReadString(body, "name", out var namePresent, out var name).Should().BeTrue();
        namePresent.Should().BeTrue();
        name.Should().Be("x");

        PatchJson.TryReadString(body, "alias", out var aliasPresent, out var alias).Should().BeTrue();
        aliasPresent.Should().BeTrue();
        alias.Should().BeNull();

        PatchJson.TryReadString(body, "description", out var descriptionPresent, out _).Should().BeTrue();
        descriptionPresent.Should().BeFalse();

        PatchJson.TryReadBool(body, "isActive", out var activePresent, out var active).Should().BeTrue();
        activePresent.Should().BeTrue();
        active.Should().BeFalse();
    }

    [Fact]
    public void PatchJson_WrongType_ReturnsFalse()
    {
        var body = JsonDocument.Parse("""{ "name": 5, "isActive": "yes" }""").RootElement;

        PatchJson.TryReadString(body, "name", out _, out _).Should().BeFalse();
        PatchJson.TryReadBool(body, "isActive", out _, out _).Should().BeFalse();
    }
}
```

- [ ] **Step 6: Run to verify it fails**

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~ResultHttpExtensionsTests"`
Expected: build FAILS (`ResultHttpExtensions`, `PatchJson` not found).

- [ ] **Step 7: Implement the Api building blocks**

`src/PictureManager.Api/Endpoints/ResultHttpExtensions.cs`:

```csharp
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Common;

namespace PictureManager.Api.Endpoints;

/// <summary>Maps service Results onto TypedResults unions, so each handler's responses are in its signature.</summary>
public static class ResultHttpExtensions
{
    public static Results<Ok<T>, NotFound, ValidationProblem, Conflict<ProblemDetails>> ToOk<T>(this Result<T> result) =>
        result.Status switch
        {
            ResultStatus.Success => TypedResults.Ok(result.Value!),
            ResultStatus.NotFound => TypedResults.NotFound(),
            ResultStatus.Invalid => TypedResults.ValidationProblem(ToErrors(result.Errors)),
            ResultStatus.Conflict => TypedResults.Conflict(ConflictProblem(result.Message)),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, null)
        };

    public static Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>> ToNoContent(this Result result) =>
        result.Status switch
        {
            ResultStatus.Success => TypedResults.NoContent(),
            ResultStatus.NotFound => TypedResults.NotFound(),
            ResultStatus.Invalid => TypedResults.ValidationProblem(ToErrors(result.Errors)),
            ResultStatus.Conflict => TypedResults.Conflict(ConflictProblem(result.Message)),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, null)
        };

    internal static Dictionary<string, string[]> ToErrors(IReadOnlyDictionary<string, string[]>? errors) =>
        errors is null ? new Dictionary<string, string[]>() : new Dictionary<string, string[]>(errors);

    internal static ProblemDetails ConflictProblem(string? message) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Conflict",
        Detail = message
    };
}
```

`src/PictureManager.Api/Endpoints/ApiSurface.cs`:

```csharp
namespace PictureManager.Api.Endpoints;

/// <summary>Which authorization surface an endpoint belongs to (the v2 auth seam).</summary>
public enum ApiSurface
{
    User,
    Admin
}

/// <summary>Endpoint metadata stamped by the user/admin route groups in Program.cs.</summary>
public sealed record ApiSurfaceMetadata(ApiSurface Surface);
```

`src/PictureManager.Api/Endpoints/PatchJson.cs`:

```csharp
using System;
using System.Text.Json;

namespace PictureManager.Api.Endpoints;

/// <summary>
/// PATCH bodies need "absent" vs "explicit null" (e.g. `alias: null` clears the alias), which normal
/// record binding can't express. Each reader returns false only when the property has the wrong JSON type.
/// </summary>
public static class PatchJson
{
    public static bool TryReadString(JsonElement body, string property, out bool present, out string? value)
    {
        present = false;
        value = null;
        if (!TryFind(body, property, out var element))
            return true;

        present = true;
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return true;
            case JsonValueKind.String:
                value = element.GetString();
                return true;
            default:
                return false;
        }
    }

    public static bool TryReadBool(JsonElement body, string property, out bool present, out bool? value)
    {
        present = false;
        value = null;
        if (!TryFind(body, property, out var element))
            return true;

        present = true;
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                value = false;
                return true;
            default:
                return false;
        }
    }

    private static bool TryFind(JsonElement body, string property, out JsonElement element)
    {
        element = default;
        if (body.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var candidate in body.EnumerateObject())
        {
            if (string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase))
            {
                element = candidate.Value;
                return true;
            }
        }

        return false;
    }
}
```

- [ ] **Step 8: Run the Api tests to verify they pass**

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~ResultHttpExtensionsTests"`
Expected: PASS, 8 tests, 0 failed.

- [ ] **Step 9: Commit**

```bash
git add src/PictureManager.Application src/PictureManager.Api/Endpoints tests/PictureManager.Application.Tests tests/PictureManager.Api.Tests
git commit -m "feat: add Result/TypedResults mapping, cursor codec, image URLs, current user and API surfaces"
```

---

## Task 4: Image query repository (listing, keyset, filters, detail, favorites)

**Files:**
- Create: `src/PictureManager.Application/Images/ImageQueryModels.cs`
- Create: `src/PictureManager.Application/Repositories/IImageQueryRepository.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Queries/VisibilityExtensions.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Queries/ImageProjections.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Queries/LikePatterns.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Repositories/ImageQueryRepository.cs`
- Modify: `src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ImageQueryRepositoryTests.cs`

**Interfaces:**
- Consumes: `PostgresTestDatabase`, `TestData` (Task 1/2), `Image.SortDate` (Task 2).
- Produces (namespace `PictureManager.Application.Images`):
  - `enum ImageSort { Date, Name }`
  - `enum SortDirection { Asc, Desc }`
  - `sealed record ImageListFilter(int? FolderId, string? FolderName, string? FileName, bool FavoritesOnly)`
  - `sealed record ImageKeyset(DateTime? SortDate, string? SortName, int Id)`
  - `sealed record ImageRow(int Id, int FolderId, string FileName, string Extension, int? Width, int? Height, DateTime? DateTaken, bool IsFavorite, string ContentHash, DateTime SortDate, string SortName)`.
    `SortName` is the database's `lower(FileName)`, the only valid name-sort cursor key.
  - `sealed record ImageDetailRow(ImageRow Image, long FileSize, DateTime FileModified, int? Orientation, string? CameraMake, string? CameraModel, string? LensModel, double? Latitude, double? Longitude, string? RawMetadata, string RootName, string RelativePath)`
  - `sealed record AlbumRef(int Id, string Name)`
- Produces (namespace `PictureManager.Application.Repositories`), `interface IImageQueryRepository`:
  - `Task<IReadOnlyList<ImageRow>> ListAsync(ImageListFilter filter, ImageSort sort, SortDirection direction, ImageKeyset? after, int take, CancellationToken cancellationToken = default)`
  - `Task<ImageDetailRow?> GetVisibleDetailAsync(int id, CancellationToken cancellationToken = default)`
  - `Task<IReadOnlyList<AlbumRef>> GetAlbumsContainingAsync(int imageId, int ownerUserId, CancellationToken cancellationToken = default)`
  - `Task<bool> SetFavoriteAsync(int id, bool isFavorite, DateTime updatedAtUtc, CancellationToken cancellationToken = default)`.
    Returns false when no visible image has that id.
  - `Task<IReadOnlyList<int>> GetVisibleIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)`
  - `Task<IReadOnlyList<int>> GetVisibleIdsInFolderAsync(int folderId, CancellationToken cancellationToken = default)`.
    Ordered by `(SortDate, Id)` ascending.
- Produces (Infrastructure, `internal`, namespace `PictureManager.Infrastructure.Persistence.Queries`):
  - `VisibilityExtensions.WhereVisible(this IQueryable<Image>)` and `WhereVisible(this IQueryable<Folder>)`
  - `ImageProjections.ToRow` (`Expression<Func<Image, ImageRow>>`)
  - `LikePatterns.Contains(string term)`

- [ ] **Step 1: Write the failing repository tests**

Create `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ImageQueryRepositoryTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Application.Images;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class ImageQueryRepositoryTests
{
    private static readonly ImageListFilter NoFilter = new(null, null, null, false);

    private static async Task<List<ImageRow>> PageAllAsync(
        ImageQueryRepository repository, ImageListFilter filter, ImageSort sort, SortDirection direction, int pageSize)
    {
        var all = new List<ImageRow>();
        ImageKeyset? after = null;
        for (var guard = 0; guard < 100; guard++)
        {
            var page = await repository.ListAsync(filter, sort, direction, after, pageSize);
            all.AddRange(page);
            if (page.Count < pageSize)
                return all;
            var last = page[^1];
            after = new ImageKeyset(last.SortDate, last.SortName, last.Id);
        }

        throw new InvalidOperationException("Paging did not terminate.");
    }

    [Fact]
    public async Task ListAsync_ExcludesMissingImages_InactiveFolders_AndInactiveRoots()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("active");
        var folder = TestData.Folder(root, "");
        var inactiveFolder = TestData.Folder(root, "gone", folder, isActive: false);
        var inactiveRoot = TestData.Root("offline", isActive: false);
        var offlineFolder = TestData.Folder(inactiveRoot, "");
        var visible = TestData.Image(folder, "visible");
        db.Context.Images.AddRange(
            visible,
            TestData.Image(folder, "missing", missingSinceUtc: TestData.Utc),
            TestData.Image(inactiveFolder, "in-removed-folder"),
            TestData.Image(offlineFolder, "on-offline-root"));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new ImageQueryRepository(context).ListAsync(NoFilter, ImageSort.Date, SortDirection.Desc, null, 50);

        rows.Select(r => r.Id).Should().Equal(visible.Id);
    }

    [Fact]
    public async Task ListAsync_FolderIdFilter_ReturnsOnlyDirectImages()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var parent = TestData.Folder(root, "");
        var child = TestData.Folder(root, "child", parent);
        var direct = TestData.Image(parent, "direct");
        db.Context.Images.AddRange(direct, TestData.Image(child, "nested"));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new ImageQueryRepository(context)
            .ListAsync(new ImageListFilter(parent.Id, null, null, false), ImageSort.Date, SortDirection.Desc, null, 50);

        rows.Select(r => r.Id).Should().Equal(direct.Id);
    }

    [Fact]
    public async Task ListAsync_DateDesc_OrdersBySortDateWithFileModifiedFallback_AndKeysetContinues()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var newest = TestData.Image(folder, "a", dateTaken: new DateTime(2025, 1, 3, 12, 0, 0));
        var undated = TestData.Image(folder, "b", fileModified: new DateTime(2025, 1, 2, 12, 0, 0, DateTimeKind.Utc));
        var oldest = TestData.Image(folder, "c", dateTaken: new DateTime(2025, 1, 1, 12, 0, 0));
        db.Context.Images.AddRange(oldest, undated, newest);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);
        var first = await repository.ListAsync(NoFilter, ImageSort.Date, SortDirection.Desc, null, 2);
        var last = first[^1];
        var second = await repository.ListAsync(NoFilter, ImageSort.Date, SortDirection.Desc,
            new ImageKeyset(last.SortDate, last.SortName, last.Id), 2);

        first.Select(r => r.Id).Should().Equal(newest.Id, undated.Id);
        second.Select(r => r.Id).Should().Equal(oldest.Id);
    }

    [Fact]
    public async Task ListAsync_NameAsc_IsCaseInsensitive_AndKeysetContinues()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var b = TestData.Image(folder, "b");
        var a = TestData.Image(folder, "A");
        var c = TestData.Image(folder, "c");
        db.Context.Images.AddRange(b, a, c);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);
        var first = await repository.ListAsync(NoFilter, ImageSort.Name, SortDirection.Asc, null, 2);
        var last = first[^1];
        var second = await repository.ListAsync(NoFilter, ImageSort.Name, SortDirection.Asc,
            new ImageKeyset(last.SortDate, last.SortName, last.Id), 2);

        first.Select(r => r.Id).Should().Equal(a.Id, b.Id);
        first[0].SortName.Should().Be("a");
        second.Select(r => r.Id).Should().Equal(c.Id);
    }

    [Fact]
    public async Task ListAsync_FileNameAndFolderNameFilters_AreCaseInsensitiveSubstrings()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var madeira = TestData.Folder(root, "Madeira 2025", top);
        var inMadeira = TestData.Image(madeira, "IMG_4471");
        var elsewhere = TestData.Image(top, "img_9999");
        db.Context.Images.AddRange(inMadeira, elsewhere);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);

        var byFile = await repository.ListAsync(new ImageListFilter(null, null, "img_44", false), ImageSort.Name, SortDirection.Asc, null, 50);
        var byFolder = await repository.ListAsync(new ImageListFilter(null, "madeira", null, false), ImageSort.Name, SortDirection.Asc, null, 50);

        byFile.Select(r => r.Id).Should().Equal(inMadeira.Id);
        byFolder.Select(r => r.Id).Should().Equal(inMadeira.Id);
    }

    [Fact]
    public async Task ListAsync_FileNameFilter_TreatsLikeWildcardsLiterally()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var underscore = TestData.Image(folder, "IMG_1");
        var percent = TestData.Image(folder, "100%");
        db.Context.Images.AddRange(underscore, percent, TestData.Image(folder, "IMGX1"), TestData.Image(folder, "1000"));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);

        (await repository.ListAsync(new ImageListFilter(null, null, "_", false), ImageSort.Name, SortDirection.Asc, null, 50))
            .Select(r => r.Id).Should().Equal(underscore.Id);
        (await repository.ListAsync(new ImageListFilter(null, null, "%", false), ImageSort.Name, SortDirection.Asc, null, 50))
            .Select(r => r.Id).Should().Equal(percent.Id);
    }

    [Fact]
    public async Task ListAsync_FavoritesOnly_ReturnsOnlyFavorites()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var favorite = TestData.Image(folder, "fav", isFavorite: true);
        db.Context.Images.AddRange(favorite, TestData.Image(folder, "plain"));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new ImageQueryRepository(context)
            .ListAsync(new ImageListFilter(null, null, null, true), ImageSort.Date, SortDirection.Desc, null, 50);

        rows.Select(r => r.Id).Should().Equal(favorite.Id);
    }

    [Fact]
    public async Task ListAsync_ManyImagesWithSameSortDate_PagesEachImageExactlyOnce()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var images = Enumerable.Range(0, 7).Select(n => TestData.Image(folder, $"burst{n}")).ToList();
        db.Context.Images.AddRange(images);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);

        foreach (var direction in new[] { SortDirection.Desc, SortDirection.Asc })
        {
            var paged = await PageAllAsync(repository, NoFilter, ImageSort.Date, direction, 3);
            paged.Select(r => r.Id).Should().OnlyHaveUniqueItems().And.BeEquivalentTo(images.Select(i => i.Id));
        }
    }

    [Fact]
    public async Task ListAsync_NameSortAcrossPages_WithAccentedAndMixedCaseNames_ReturnsEachImageOnce()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var images = new[] { "Ádám", "zebra", "Apple", "ádám2", "Zulu", "apple", "Éva", "eva" }
            .Select(name => TestData.Image(folder, name)).ToList();
        db.Context.Images.AddRange(images);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);

        foreach (var direction in new[] { SortDirection.Asc, SortDirection.Desc })
        {
            var paged = await PageAllAsync(repository, NoFilter, ImageSort.Name, direction, 3);
            paged.Select(r => r.Id).Should().OnlyHaveUniqueItems().And.BeEquivalentTo(images.Select(i => i.Id));
        }
    }

    [Fact]
    public async Task GetVisibleDetailAsync_ReturnsMetadataAndLocation_AndNullForMissingImage()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("nas");
        var top = TestData.Folder(root, "");
        var folder = TestData.Folder(root, "Holidays/Madeira", top);
        var image = TestData.Image(folder, "IMG_1");
        image.CameraMake = "Canon";
        image.RawMetadata = """{"Exif IFD0.Make":"Canon"}""";
        var missing = TestData.Image(folder, "IMG_2", missingSinceUtc: TestData.Utc);
        db.Context.Images.AddRange(image, missing);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);
        var detail = await repository.GetVisibleDetailAsync(image.Id);

        detail.Should().NotBeNull();
        detail!.RootName.Should().Be("nas");
        detail.RelativePath.Should().Be("Holidays/Madeira");
        detail.CameraMake.Should().Be("Canon");
        detail.RawMetadata.Should().Contain("Canon");
        (await repository.GetVisibleDetailAsync(missing.Id)).Should().BeNull();
    }

    [Fact]
    public async Task SetFavoriteAsync_SetsAndClears_AndReturnsFalseForMissingImage()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var image = TestData.Image(folder, "a");
        var missing = TestData.Image(folder, "b", missingSinceUtc: TestData.Utc);
        db.Context.Images.AddRange(image, missing);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);
        var favoritesOnly = new ImageListFilter(null, null, null, true);

        (await repository.SetFavoriteAsync(image.Id, true, TestData.Utc)).Should().BeTrue();
        (await repository.ListAsync(favoritesOnly, ImageSort.Date, SortDirection.Desc, null, 5))
            .Select(r => r.Id).Should().Equal(image.Id);
        (await repository.SetFavoriteAsync(image.Id, false, TestData.Utc)).Should().BeTrue();
        (await repository.ListAsync(favoritesOnly, ImageSort.Date, SortDirection.Desc, null, 5)).Should().BeEmpty();
        (await repository.SetFavoriteAsync(missing.Id, true, TestData.Utc)).Should().BeFalse();
        (await repository.SetFavoriteAsync(999_999, true, TestData.Utc)).Should().BeFalse();
    }

    [Fact]
    public async Task GetVisibleIdsAsync_ReturnsOnlyVisibleRequestedIds()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var visible = TestData.Image(folder, "a");
        var missing = TestData.Image(folder, "b", missingSinceUtc: TestData.Utc);
        db.Context.Images.AddRange(visible, missing);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var ids = await new ImageQueryRepository(context).GetVisibleIdsAsync(new[] { visible.Id, missing.Id, 999_999 });

        ids.Should().Equal(visible.Id);
    }

    [Fact]
    public async Task GetVisibleIdsInFolderAsync_OrdersBySortDateThenId()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var later = TestData.Image(folder, "later", dateTaken: new DateTime(2025, 5, 1, 0, 0, 0));
        var earlier = TestData.Image(folder, "earlier", dateTaken: new DateTime(2024, 5, 1, 0, 0, 0));
        db.Context.Images.AddRange(later, earlier);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var ids = await new ImageQueryRepository(context).GetVisibleIdsInFolderAsync(folder.Id);

        ids.Should().Equal(earlier.Id, later.Id);
    }

    [Fact]
    public async Task GetAlbumsContainingAsync_IsScopedToOwner_AndOrderedByName()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var other = new AppUser { DisplayName = "Other", Role = UserRole.User };
        db.Context.AppUsers.Add(other);
        await db.Context.SaveChangesAsync();

        var folder = TestData.Folder(TestData.Root("r"), "");
        var image = TestData.Image(folder, "a");
        var zoo = TestData.Album("Zoo");
        var beach = TestData.Album("beach");
        var foreign = TestData.Album("Foreign", other.Id);
        db.Context.AlbumImages.AddRange(
            TestData.AlbumImage(zoo, image, 0),
            TestData.AlbumImage(beach, image, 0),
            TestData.AlbumImage(foreign, image, 0));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var albums = await new ImageQueryRepository(context).GetAlbumsContainingAsync(image.Id, AppUser.SystemUserId);

        albums.Should().Equal(new AlbumRef(beach.Id, "beach"), new AlbumRef(zoo.Id, "Zoo"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~ImageQueryRepositoryTests"`
Expected: build FAILS (`ImageQueryRepository`, `ImageListFilter`, `ImageRow` … not found).

- [ ] **Step 3: Add the Application contracts**

`src/PictureManager.Application/Images/ImageQueryModels.cs`:

```csharp
using System;

namespace PictureManager.Application.Images;

public enum ImageSort
{
    Date,
    Name
}

public enum SortDirection
{
    Asc,
    Desc
}

/// <summary>AND-combined listing filters. FolderId = images directly in that folder only.</summary>
public sealed record ImageListFilter(int? FolderId, string? FolderName, string? FileName, bool FavoritesOnly);

/// <summary>"Continue after this row". Date sorts use SortDate; name sorts use SortName (the DB's lower(FileName)).</summary>
public sealed record ImageKeyset(DateTime? SortDate, string? SortName, int Id);

/// <summary>Slim list row projected in SQL (never loads RawMetadata).</summary>
public sealed record ImageRow(
    int Id,
    int FolderId,
    string FileName,
    string Extension,
    int? Width,
    int? Height,
    DateTime? DateTaken,
    bool IsFavorite,
    string ContentHash,
    DateTime SortDate,
    string SortName);

public sealed record ImageDetailRow(
    ImageRow Image,
    long FileSize,
    DateTime FileModified,
    int? Orientation,
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    double? Latitude,
    double? Longitude,
    string? RawMetadata,
    string RootName,
    string RelativePath);

public sealed record AlbumRef(int Id, string Name);
```

`src/PictureManager.Application/Repositories/IImageQueryRepository.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Images;

namespace PictureManager.Application.Repositories;

/// <summary>Read-side image queries. Every method applies the visibility rule (see phase 5 spec).</summary>
public interface IImageQueryRepository
{
    Task<IReadOnlyList<ImageRow>> ListAsync(
        ImageListFilter filter, ImageSort sort, SortDirection direction, ImageKeyset? after, int take,
        CancellationToken cancellationToken = default);

    Task<ImageDetailRow?> GetVisibleDetailAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlbumRef>> GetAlbumsContainingAsync(int imageId, int ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Returns false when no visible image has this id.</summary>
    Task<bool> SetFavoriteAsync(int id, bool isFavorite, DateTime updatedAtUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> GetVisibleIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Visible images directly in the folder, ordered by (SortDate, Id) ascending.</summary>
    Task<IReadOnlyList<int>> GetVisibleIdsInFolderAsync(int folderId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement the Infrastructure side**

`src/PictureManager.Infrastructure/Persistence/Queries/VisibilityExtensions.cs`:

```csharp
using System.Linq;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Queries;

/// <summary>The phase 5 visibility rule, in one place.</summary>
internal static class VisibilityExtensions
{
    public static IQueryable<Image> WhereVisible(this IQueryable<Image> images) =>
        images.Where(i => i.MissingSinceUtc == null && i.Folder!.IsActive && i.Folder.Root!.IsActive);

    public static IQueryable<Folder> WhereVisible(this IQueryable<Folder> folders) =>
        folders.Where(f => f.IsActive && f.Root!.IsActive);
}
```

`src/PictureManager.Infrastructure/Persistence/Queries/ImageProjections.cs`:

```csharp
using System;
using System.Linq.Expressions;
using PictureManager.Application.Images;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Queries;

internal static class ImageProjections
{
    public static readonly Expression<Func<Image, ImageRow>> ToRow = i => new ImageRow(
        i.Id, i.FolderId, i.FileName, i.Extension, i.Width, i.Height, i.DateTaken, i.IsFavorite,
        i.ContentHash, i.SortDate, i.FileName.ToLower());
}
```

`src/PictureManager.Infrastructure/Persistence/Queries/LikePatterns.cs`:

```csharp
namespace PictureManager.Infrastructure.Persistence.Queries;

internal static class LikePatterns
{
    // Postgres LIKE/ILIKE use backslash as the default escape character, so escaping it plus the two
    // wildcards makes user input match literally ("IMG_" must not match "IMGX").
    public static string Contains(string term) =>
        "%" + term.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
}
```

`src/PictureManager.Infrastructure/Persistence/Repositories/ImageQueryRepository.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Images;
using PictureManager.Application.Repositories;
using PictureManager.Infrastructure.Persistence.Queries;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class ImageQueryRepository : IImageQueryRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public ImageQueryRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ImageRow>> ListAsync(
        ImageListFilter filter, ImageSort sort, SortDirection direction, ImageKeyset? after, int take,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Images.AsNoTracking().WhereVisible();

        if (filter.FolderId is int folderId)
            query = query.Where(i => i.FolderId == folderId);

        if (!string.IsNullOrWhiteSpace(filter.FolderName))
        {
            var pattern = LikePatterns.Contains(filter.FolderName);
            query = query.Where(i => EF.Functions.ILike(i.Folder!.Name, pattern));
        }

        if (!string.IsNullOrWhiteSpace(filter.FileName))
        {
            var pattern = LikePatterns.Contains(filter.FileName);
            query = query.Where(i => EF.Functions.ILike(i.FileName, pattern));
        }

        if (filter.FavoritesOnly)
            query = query.Where(i => i.IsFavorite);

        query = ApplyKeyset(query, sort, direction, after);
        query = ApplyOrder(query, sort, direction);

        return await query.Take(take).Select(ImageProjections.ToRow).ToListAsync(cancellationToken);
    }

    public async Task<ImageDetailRow?> GetVisibleDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Images.AsNoTracking().WhereVisible()
            .Where(i => i.Id == id)
            .Select(i => new ImageDetailRow(
                new ImageRow(i.Id, i.FolderId, i.FileName, i.Extension, i.Width, i.Height, i.DateTaken, i.IsFavorite,
                    i.ContentHash, i.SortDate, i.FileName.ToLower()),
                i.FileSize, i.FileModified, i.Orientation, i.CameraMake, i.CameraModel, i.LensModel,
                i.Latitude, i.Longitude, i.RawMetadata, i.Folder!.Root!.Name, i.Folder.RelativePath))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AlbumRef>> GetAlbumsContainingAsync(int imageId, int ownerUserId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AlbumImages.AsNoTracking()
            .Where(ai => ai.ImageId == imageId && ai.Album!.OwnerUserId == ownerUserId)
            .OrderBy(ai => ai.Album!.Name.ToLower()).ThenBy(ai => ai.AlbumId)
            .Select(ai => new AlbumRef(ai.AlbumId, ai.Album!.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> SetFavoriteAsync(int id, bool isFavorite, DateTime updatedAtUtc, CancellationToken cancellationToken = default)
    {
        var updated = await _dbContext.Images.WhereVisible()
            .Where(i => i.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.IsFavorite, isFavorite)
                .SetProperty(i => i.UpdatedAt, updatedAtUtc),
                cancellationToken);
        return updated > 0;
    }

    public async Task<IReadOnlyList<int>> GetVisibleIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        return await _dbContext.Images.AsNoTracking().WhereVisible()
            .Where(i => idList.Contains(i.Id))
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<int>> GetVisibleIdsInFolderAsync(int folderId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Images.AsNoTracking().WhereVisible()
            .Where(i => i.FolderId == folderId)
            .OrderBy(i => i.SortDate).ThenBy(i => i.Id)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<Image> ApplyKeyset(IQueryable<Image> query, ImageSort sort, SortDirection direction, ImageKeyset? after)
    {
        if (after is null)
            return query;

        var id = after.Id;
        if (sort == ImageSort.Date)
        {
            var date = after.SortDate ?? throw new ArgumentException("A date keyset needs SortDate.", nameof(after));
            return direction == SortDirection.Desc
                ? query.Where(i => i.SortDate < date || (i.SortDate == date && i.Id < id))
                : query.Where(i => i.SortDate > date || (i.SortDate == date && i.Id > id));
        }

        var name = after.SortName ?? throw new ArgumentException("A name keyset needs SortName.", nameof(after));
        return direction == SortDirection.Desc
            ? query.Where(i => string.Compare(i.FileName.ToLower(), name) < 0 || (i.FileName.ToLower() == name && i.Id < id))
            : query.Where(i => string.Compare(i.FileName.ToLower(), name) > 0 || (i.FileName.ToLower() == name && i.Id > id));
    }

    private static IQueryable<Image> ApplyOrder(IQueryable<Image> query, ImageSort sort, SortDirection direction) =>
        (sort, direction) switch
        {
            (ImageSort.Date, SortDirection.Desc) => query.OrderByDescending(i => i.SortDate).ThenByDescending(i => i.Id),
            (ImageSort.Date, SortDirection.Asc) => query.OrderBy(i => i.SortDate).ThenBy(i => i.Id),
            (ImageSort.Name, SortDirection.Desc) => query.OrderByDescending(i => i.FileName.ToLower()).ThenByDescending(i => i.Id),
            _ => query.OrderBy(i => i.FileName.ToLower()).ThenBy(i => i.Id)
        };
}
```

The name keyset relies on EF Core translating `string.Compare(a, b) < 0` into a plain SQL `a < b` (it
does, for comparisons of `string.Compare` against `0`). Ordering and comparison then both use the
database collation, which is what keeps the name-sort cursor consistent. If that translation ever
fails at runtime ("could not be translated"), record a ruling and express the same predicate in SQL
another way. Both the comparison and the `ORDER BY` must stay in the database; never compare names
in C#.

In `InfrastructureServiceCollectionExtensions.AddInfrastructure` add after the `IAppSettingsRepository`
registration:

```csharp
        services.AddScoped<IImageQueryRepository, ImageQueryRepository>();
```

- [ ] **Step 5: Run the repository tests to verify they pass**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~ImageQueryRepositoryTests"`
Expected: PASS, 14 tests, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/PictureManager.Application src/PictureManager.Infrastructure tests/PictureManager.Infrastructure.Tests
git commit -m "feat: add image query repository with keyset listing, filters, detail and favorites"
```

---

## Task 5: `ImageQueryService` (list parameters, cursors, detail, favorites)

**Files:**
- Create: `src/PictureManager.Application/Images/ImageDtos.cs`
- Create: `src/PictureManager.Application/Images/IImageQueryService.cs`
- Create: `src/PictureManager.Application/Images/ImageQueryService.cs`
- Modify: `src/PictureManager.Application/Repositories/IFolderRepository.cs` (add `IsVisibleAsync`)
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/FolderRepository.cs`
- Modify: `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Application.Tests/Images/ImageQueryServiceTests.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FolderQueryRepositoryTests.cs`

**Interfaces:**
- Consumes:
  - `IImageQueryRepository`, `ImageRow`, `ImageKeyset`, `ImageSort`, `SortDirection`, `AlbumRef` (Task 4);
  - `Result`, `PagedResult<T>`, `CursorCodec`, `ICurrentUser`, `ImageUrls`, `FolderDisplayPath` (Task 3);
  - `IClock` (existing).
- Produces:
  - `IFolderRepository.IsVisibleAsync(int id, CancellationToken cancellationToken = default)` → `Task<bool>`
  - DTOs in `PictureManager.Application.Images`:
    - `sealed record ImageListItem(int Id, int FolderId, string FileName, string Extension, int? Width, int? Height, DateTime? DateTaken, bool IsFavorite, string? ThumbnailUrl, string? PreviewUrl)`,
      with `static ImageListItem From(ImageRow row)`
    - `sealed record ImageDetail(int Id, int FolderId, string FileName, string Extension, int? Width, int? Height, DateTime? DateTaken, bool IsFavorite, string? ThumbnailUrl, string? PreviewUrl, long FileSize, DateTime FileModified, int? Orientation, string? CameraMake, string? CameraModel, string? LensModel, double? Latitude, double? Longitude, JsonElement? RawMetadata, string FolderPath, IReadOnlyList<AlbumRef> Albums)`
    - `sealed record ImageListRequest(int? FolderId = null, string? Folder = null, string? FileName = null, bool FavoritesOnly = false, string? Sort = null, string? Order = null, string? Cursor = null, int? Limit = null)`
    - `sealed record ImageCursor(string Sort, string Order, string Key, int Id)`, with JSON names
      `s`, `o`, `k`, `i`
  - `interface IImageQueryService`:
    - `Task<Result<PagedResult<ImageListItem>>> ListAsync(ImageListRequest request, CancellationToken cancellationToken = default)`
    - `Task<Result<ImageDetail>> GetDetailAsync(int id, CancellationToken cancellationToken = default)`
    - `Task<Result> SetFavoriteAsync(int id, bool isFavorite, CancellationToken cancellationToken = default)`
  - `ImageQueryService` (scoped), with `public const int DefaultLimit = 100`, `public const int MaxLimit = 200`

- [ ] **Step 1: Write the failing `IsVisibleAsync` test**

Create `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FolderQueryRepositoryTests.cs`
(Task 7 adds more tests to this class):

```csharp
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class FolderQueryRepositoryTests
{
    [Fact]
    public async Task IsVisibleAsync_TrueOnlyForActiveFolderUnderActiveRoot()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var active = TestData.Folder(root, "");
        var removed = TestData.Folder(root, "removed", active, isActive: false);
        var offline = TestData.Folder(TestData.Root("offline", isActive: false), "");
        db.Context.Folders.AddRange(active, removed, offline);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new FolderRepository(context);

        (await repository.IsVisibleAsync(active.Id)).Should().BeTrue();
        (await repository.IsVisibleAsync(removed.Id)).Should().BeFalse();
        (await repository.IsVisibleAsync(offline.Id)).Should().BeFalse();
        (await repository.IsVisibleAsync(999_999)).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Write the failing service tests**

Create `tests/PictureManager.Application.Tests/Images/ImageQueryServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Images;
using PictureManager.Application.Repositories;
using Xunit;

namespace PictureManager.Application.Tests.Images;

public class ImageQueryServiceTests
{
    private readonly IImageQueryRepository _images = Substitute.For<IImageQueryRepository>();
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public ImageQueryServiceTests()
    {
        _currentUser.UserId.Returns(1);
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _folders.IsVisibleAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        StubRows(Array.Empty<ImageRow>());
    }

    private ImageQueryService CreateService() => new(_images, _folders, _currentUser, _clock);

    private static ImageRow Row(int id, string hash = "H", string name = "img") =>
        new(id, 7, name, ".jpg", 10, 20, null, false, hash,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(-id), name.ToLowerInvariant());

    private void StubRows(IReadOnlyList<ImageRow> rows) =>
        _images.ListAsync(Arg.Any<ImageListFilter>(), Arg.Any<ImageSort>(), Arg.Any<SortDirection>(),
                Arg.Any<ImageKeyset?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(rows);

    [Fact]
    public async Task ListAsync_Defaults_AreDateDescLimit100_AndAskForOneExtraRow()
    {
        var result = await CreateService().ListAsync(new ImageListRequest());

        result.IsSuccess.Should().BeTrue();
        await _images.Received(1).ListAsync(new ImageListFilter(null, null, null, false),
            ImageSort.Date, SortDirection.Desc, null, 101, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_NameSort_DefaultsToAscending()
    {
        await CreateService().ListAsync(new ImageListRequest(Sort: "name"));

        await _images.Received(1).ListAsync(Arg.Any<ImageListFilter>(),
            ImageSort.Name, SortDirection.Asc, null, 101, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("size", null, null, "sort")]
    [InlineData(null, "sideways", null, "order")]
    [InlineData(null, null, 0, "limit")]
    [InlineData(null, null, 201, "limit")]
    public async Task ListAsync_InvalidParameters_ReturnInvalidForThatField(string? sort, string? order, int? limit, string field)
    {
        var result = await CreateService().ListAsync(new ImageListRequest(Sort: sort, Order: order, Limit: limit));

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors!.Keys.Should().Contain(field);
    }

    [Fact]
    public async Task ListAsync_MalformedCursor_ReturnsInvalidCursor()
    {
        var result = await CreateService().ListAsync(new ImageListRequest(Cursor: "%%%not-a-cursor"));

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors!.Keys.Should().Contain("cursor");
    }

    [Fact]
    public async Task ListAsync_CursorFromAnotherSort_ReturnsInvalidCursor()
    {
        var nameCursor = CursorCodec.Encode(new ImageCursor("name", "asc", "abc", 5));

        var result = await CreateService().ListAsync(new ImageListRequest(Sort: "date", Cursor: nameCursor));

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors!.Keys.Should().Contain("cursor");
    }

    [Fact]
    public async Task ListAsync_MoreRowsThanLimit_ReturnsNextCursor_ThatContinuesFromTheLastRow()
    {
        StubRows(new[] { Row(1), Row(2), Row(3) });
        var service = CreateService();

        var first = await service.ListAsync(new ImageListRequest(Limit: 2));

        first.Value!.Items.Select(i => i.Id).Should().Equal(1, 2);
        first.Value.NextCursor.Should().NotBeNull();

        await service.ListAsync(new ImageListRequest(Limit: 2, Cursor: first.Value.NextCursor));

        var expectedAfter = new ImageKeyset(Row(2).SortDate, null, 2);
        await _images.Received(1).ListAsync(Arg.Any<ImageListFilter>(), ImageSort.Date, SortDirection.Desc,
            expectedAfter, 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_NameCursor_UsesTheDatabaseSortName()
    {
        StubRows(new[] { Row(1, name: "Alpha"), Row(2, name: "Beta"), Row(3, name: "Gamma") });
        var service = CreateService();

        var first = await service.ListAsync(new ImageListRequest(Sort: "name", Limit: 2));
        await service.ListAsync(new ImageListRequest(Sort: "name", Limit: 2, Cursor: first.Value!.NextCursor));

        await _images.Received(1).ListAsync(Arg.Any<ImageListFilter>(), ImageSort.Name, SortDirection.Asc,
            new ImageKeyset(null, "beta", 2), 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_LastPage_HasNullCursor()
    {
        StubRows(new[] { Row(1) });

        var result = await CreateService().ListAsync(new ImageListRequest(Limit: 2));

        result.Value!.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_FolderNotVisible_ReturnsNotFound()
    {
        _folders.IsVisibleAsync(12, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateService().ListAsync(new ImageListRequest(FolderId: 12));

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task ListAsync_MapsVersionedUrls_AndNullUrlsWhenNotYetHashed()
    {
        StubRows(new[] { Row(1, hash: "ABC"), Row(2, hash: "") });

        var items = (await CreateService().ListAsync(new ImageListRequest())).Value!.Items;

        items[0].ThumbnailUrl.Should().Be("/api/images/1/thumbnail?v=ABC");
        items[0].PreviewUrl.Should().Be("/api/images/1/preview?v=ABC");
        items[1].ThumbnailUrl.Should().BeNull();
        items[1].PreviewUrl.Should().BeNull();
    }

    [Fact]
    public async Task GetDetailAsync_NotVisible_ReturnsNotFound()
    {
        _images.GetVisibleDetailAsync(5, Arg.Any<CancellationToken>()).Returns((ImageDetailRow?)null);

        (await CreateService().GetDetailAsync(5)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetDetailAsync_MapsFolderPathRawMetadataAndAlbums()
    {
        _images.GetVisibleDetailAsync(1, Arg.Any<CancellationToken>()).Returns(new ImageDetailRow(
            Row(1, hash: "ABC"), 2048, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), 6,
            "Canon", "R6", null, 32.6, -16.9, """{"Exif IFD0.Make":"Canon"}""", "nas", "Holidays/Madeira"));
        _images.GetAlbumsContainingAsync(1, 1, Arg.Any<CancellationToken>()).Returns(new[] { new AlbumRef(3, "Best of") });

        var detail = (await CreateService().GetDetailAsync(1)).Value!;

        detail.FolderPath.Should().Be("nas/Holidays/Madeira");
        detail.RawMetadata!.Value.GetProperty("Exif IFD0.Make").GetString().Should().Be("Canon");
        detail.Albums.Should().Equal(new AlbumRef(3, "Best of"));
        detail.ThumbnailUrl.Should().Be("/api/images/1/thumbnail?v=ABC");
    }

    [Fact]
    public async Task GetDetailAsync_UnparseableRawMetadata_BecomesNull()
    {
        _images.GetVisibleDetailAsync(1, Arg.Any<CancellationToken>()).Returns(new ImageDetailRow(
            Row(1), 1, DateTime.UtcNow, null, null, null, null, null, null, "{not json", "nas", ""));
        _images.GetAlbumsContainingAsync(1, 1, Arg.Any<CancellationToken>()).Returns(Array.Empty<AlbumRef>());

        (await CreateService().GetDetailAsync(1)).Value!.RawMetadata.Should().BeNull();
    }

    [Fact]
    public async Task SetFavoriteAsync_NoVisibleImage_ReturnsNotFound()
    {
        _images.SetFavoriteAsync(9, true, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);

        (await CreateService().SetFavoriteAsync(9, true)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task SetFavoriteAsync_Success_ReturnsOk_AndStampsTheClock()
    {
        _images.SetFavoriteAsync(9, false, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

        (await CreateService().SetFavoriteAsync(9, false)).IsSuccess.Should().BeTrue();
        await _images.Received(1).SetFavoriteAsync(9, false, _clock.UtcNow, Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~ImageQueryServiceTests"`
Expected: build FAILS (`ImageQueryService`, `ImageListRequest`, `IFolderRepository.IsVisibleAsync` … not found).

- [ ] **Step 4: Implement**

Add to `src/PictureManager.Application/Repositories/IFolderRepository.cs`:

```csharp
    /// <summary>True when the folder exists, is active, and its root is active.</summary>
    Task<bool> IsVisibleAsync(int id, CancellationToken cancellationToken = default);
```

Add to `FolderRepository` (plus `using PictureManager.Infrastructure.Persistence.Queries;`):

```csharp
    public async Task<bool> IsVisibleAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Folders.AsNoTracking().WhereVisible().AnyAsync(f => f.Id == id, cancellationToken);
    }
```

`src/PictureManager.Application/Images/ImageDtos.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using PictureManager.Application.Common;

namespace PictureManager.Application.Images;

public sealed record ImageListItem(
    int Id,
    int FolderId,
    string FileName,
    string Extension,
    int? Width,
    int? Height,
    DateTime? DateTaken,
    bool IsFavorite,
    string? ThumbnailUrl,
    string? PreviewUrl)
{
    public static ImageListItem From(ImageRow row) => new(
        row.Id, row.FolderId, row.FileName, row.Extension, row.Width, row.Height, row.DateTaken, row.IsFavorite,
        ImageUrls.Thumbnail(row.Id, row.ContentHash), ImageUrls.Preview(row.Id, row.ContentHash));
}

public sealed record ImageDetail(
    int Id,
    int FolderId,
    string FileName,
    string Extension,
    int? Width,
    int? Height,
    DateTime? DateTaken,
    bool IsFavorite,
    string? ThumbnailUrl,
    string? PreviewUrl,
    long FileSize,
    DateTime FileModified,
    int? Orientation,
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    double? Latitude,
    double? Longitude,
    JsonElement? RawMetadata,
    string FolderPath,
    IReadOnlyList<AlbumRef> Albums);

public sealed record ImageListRequest(
    int? FolderId = null,
    string? Folder = null,
    string? FileName = null,
    bool FavoritesOnly = false,
    string? Sort = null,
    string? Order = null,
    string? Cursor = null,
    int? Limit = null);

/// <summary>Cursor payload. Key = SortDate ticks (date sort) or the DB's lower(FileName) (name sort).</summary>
public sealed record ImageCursor(
    [property: JsonPropertyName("s")] string Sort,
    [property: JsonPropertyName("o")] string Order,
    [property: JsonPropertyName("k")] string Key,
    [property: JsonPropertyName("i")] int Id);
```

`src/PictureManager.Application/Images/IImageQueryService.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Images;

public interface IImageQueryService
{
    Task<Result<PagedResult<ImageListItem>>> ListAsync(ImageListRequest request, CancellationToken cancellationToken = default);

    Task<Result<ImageDetail>> GetDetailAsync(int id, CancellationToken cancellationToken = default);

    Task<Result> SetFavoriteAsync(int id, bool isFavorite, CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Application/Images/ImageQueryService.cs`:

```csharp
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Images;

public sealed class ImageQueryService : IImageQueryService
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 200;

    private readonly IImageQueryRepository _images;
    private readonly IFolderRepository _folders;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public ImageQueryService(IImageQueryRepository images, IFolderRepository folders, ICurrentUser currentUser, IClock clock)
    {
        _images = images;
        _folders = folders;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<PagedResult<ImageListItem>>> ListAsync(ImageListRequest request, CancellationToken cancellationToken = default)
    {
        var sortToken = (request.Sort ?? "date").ToLowerInvariant();
        ImageSort sort;
        switch (sortToken)
        {
            case "date": sort = ImageSort.Date; break;
            case "name": sort = ImageSort.Name; break;
            default: return Result.Invalid("sort", "Must be 'date' or 'name'.");
        }

        var orderToken = (request.Order ?? (sort == ImageSort.Date ? "desc" : "asc")).ToLowerInvariant();
        SortDirection direction;
        switch (orderToken)
        {
            case "asc": direction = SortDirection.Asc; break;
            case "desc": direction = SortDirection.Desc; break;
            default: return Result.Invalid("order", "Must be 'asc' or 'desc'.");
        }

        var limit = request.Limit ?? DefaultLimit;
        if (limit is < 1 or > MaxLimit)
            return Result.Invalid("limit", $"Must be between 1 and {MaxLimit}.");

        ImageKeyset? after = null;
        if (request.Cursor is not null)
        {
            if (!CursorCodec.TryDecode<ImageCursor>(request.Cursor, out var cursor))
                return Result.Invalid("cursor", "The cursor is malformed.");
            if (cursor.Sort != sortToken || cursor.Order != orderToken)
                return Result.Invalid("cursor", "The cursor belongs to a different sort or order.");
            if (!TryToKeyset(cursor, sort, out after))
                return Result.Invalid("cursor", "The cursor is malformed.");
        }

        if (request.FolderId is int folderId && !await _folders.IsVisibleAsync(folderId, cancellationToken))
            return Result.NotFound();

        var filter = new ImageListFilter(request.FolderId, request.Folder, request.FileName, request.FavoritesOnly);
        var rows = await _images.ListAsync(filter, sort, direction, after, limit + 1, cancellationToken);

        var page = rows.Take(limit).ToList();
        string? nextCursor = null;
        if (rows.Count > limit)
        {
            var last = page[^1];
            var key = sort == ImageSort.Date
                ? last.SortDate.Ticks.ToString(CultureInfo.InvariantCulture)
                : last.SortName;
            nextCursor = CursorCodec.Encode(new ImageCursor(sortToken, orderToken, key, last.Id));
        }

        return Result<PagedResult<ImageListItem>>.Ok(
            new PagedResult<ImageListItem>(page.Select(ImageListItem.From).ToList(), nextCursor));
    }

    public async Task<Result<ImageDetail>> GetDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        var row = await _images.GetVisibleDetailAsync(id, cancellationToken);
        if (row is null)
            return Result.NotFound();

        var albums = await _images.GetAlbumsContainingAsync(id, _currentUser.UserId, cancellationToken);
        var image = row.Image;
        return Result<ImageDetail>.Ok(new ImageDetail(
            image.Id, image.FolderId, image.FileName, image.Extension, image.Width, image.Height, image.DateTaken,
            image.IsFavorite, ImageUrls.Thumbnail(image.Id, image.ContentHash), ImageUrls.Preview(image.Id, image.ContentHash),
            row.FileSize, row.FileModified, row.Orientation, row.CameraMake, row.CameraModel, row.LensModel,
            row.Latitude, row.Longitude, ParseJson(row.RawMetadata), FolderDisplayPath.For(row.RootName, row.RelativePath),
            albums));
    }

    public async Task<Result> SetFavoriteAsync(int id, bool isFavorite, CancellationToken cancellationToken = default)
    {
        return await _images.SetFavoriteAsync(id, isFavorite, _clock.UtcNow, cancellationToken)
            ? Result.Ok()
            : Result.NotFound();
    }

    private static bool TryToKeyset(ImageCursor cursor, ImageSort sort, out ImageKeyset? keyset)
    {
        keyset = null;
        if (cursor.Key is null)
            return false;

        if (sort == ImageSort.Name)
        {
            keyset = new ImageKeyset(null, cursor.Key, cursor.Id);
            return true;
        }

        if (!long.TryParse(cursor.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTime.MaxValue.Ticks)
            return false;

        keyset = new ImageKeyset(new DateTime(ticks, DateTimeKind.Utc), null, cursor.Id);
        return true;
    }

    private static JsonElement? ParseJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

In `ApplicationServiceCollectionExtensions.AddApplication` add (with `using PictureManager.Application.Images;`):

```csharp
        services.AddScoped<IImageQueryService, ImageQueryService>();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~ImageQueryServiceTests"`
Expected: PASS, 18 tests (14 facts + 4 theory rows), 0 failed.

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~FolderQueryRepositoryTests"`
Expected: PASS, 1 test.

- [ ] **Step 6: Commit**

```bash
git add src/PictureManager.Application src/PictureManager.Infrastructure tests
git commit -m "feat: add image query service with cursor paging, detail and favorites"
```

---

## Task 6: Image endpoints and the user/admin route groups

**Files:**
- Create: `src/PictureManager.Api/Endpoints/ImageQueryEndpoints.cs`
- Modify: `src/PictureManager.Api/Endpoints/ImageEndpoints.cs` (map onto a route group; relative routes)
- Modify: `src/PictureManager.Api/Endpoints/ScanEndpoints.cs` (map onto a route group; relative routes)
- Modify: `src/PictureManager.Api/Program.cs`
- Test: `tests/PictureManager.Api.Tests/Endpoints/ImageQueryEndpointsTests.cs`

**Interfaces:**
- Consumes: `IImageQueryService`, `ImageListRequest`, `ImageListItem`, `ImageDetail` (Task 5);
  `ResultHttpExtensions`, `ApiSurface`, `ApiSurfaceMetadata` (Task 3).
- Produces:
  - `ImageQueryEndpoints.MapImageQueryEndpoints(this IEndpointRouteBuilder user)` with the static handlers
    `ListAsync`, `GetAsync`, `SetFavoriteAsync`, `ClearFavoriteAsync`;
  - `ImageEndpoints.MapImageEndpoints(this IEndpointRouteBuilder user)`;
  - `ScanEndpoints.MapScanEndpoints(this IEndpointRouteBuilder admin)`.

  **Every later task** maps its endpoints the same way: `Map<Area>Endpoints(this IEndpointRouteBuilder
  user)`, `(… admin)`, or `(this IEndpointRouteBuilder user, IEndpointRouteBuilder admin)`, with routes
  relative to `/api`.

- [ ] **Step 1: Write the failing endpoint tests**

Create `tests/PictureManager.Api.Tests/Endpoints/ImageQueryEndpointsTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Common;
using PictureManager.Application.Images;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class ImageQueryEndpointsTests
{
    private readonly IImageQueryService _service = Substitute.For<IImageQueryService>();

    [Fact]
    public async Task ListAsync_PassesQueryThrough_AndReturnsOk()
    {
        var page = new PagedResult<ImageListItem>(Array.Empty<ImageListItem>(), null);
        _service.ListAsync(Arg.Any<ImageListRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<PagedResult<ImageListItem>>.Ok(page));

        var result = await ImageQueryEndpoints.ListAsync(12, "Madeira", "IMG", true, "name", "desc", "c", 50, _service, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<PagedResult<ImageListItem>>>().Which.Value.Should().BeSameAs(page);
        await _service.Received(1).ListAsync(
            new ImageListRequest(12, "Madeira", "IMG", true, "name", "desc", "c", 50), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_FavoritesOnlyOmitted_MeansFalse()
    {
        _service.ListAsync(Arg.Any<ImageListRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<PagedResult<ImageListItem>>.Ok(new PagedResult<ImageListItem>(Array.Empty<ImageListItem>(), null)));

        await ImageQueryEndpoints.ListAsync(null, null, null, null, null, null, null, null, _service, CancellationToken.None);

        await _service.Received(1).ListAsync(new ImageListRequest(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_InvalidRequest_ReturnsValidationProblem()
    {
        _service.ListAsync(Arg.Any<ImageListRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Invalid("limit", "Must be between 1 and 200."));

        var result = await ImageQueryEndpoints.ListAsync(null, null, null, null, null, null, null, 0, _service, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
    }

    [Fact]
    public async Task GetAsync_Unknown_ReturnsNotFound()
    {
        _service.GetDetailAsync(5, Arg.Any<CancellationToken>()).Returns(Result.NotFound());

        var result = await ImageQueryEndpoints.GetAsync(5, _service, CancellationToken.None);

        result.Result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task SetFavoriteAsync_SetsTrue_AndReturnsNoContent()
    {
        _service.SetFavoriteAsync(5, true, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var result = await ImageQueryEndpoints.SetFavoriteAsync(5, _service, CancellationToken.None);

        result.Result.Should().BeOfType<NoContent>();
    }

    [Fact]
    public async Task ClearFavoriteAsync_SetsFalse_AndReturnsNoContent()
    {
        _service.SetFavoriteAsync(5, false, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var result = await ImageQueryEndpoints.ClearFavoriteAsync(5, _service, CancellationToken.None);

        result.Result.Should().BeOfType<NoContent>();
        await _service.Received(1).SetFavoriteAsync(5, false, Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~ImageQueryEndpointsTests"`
Expected: build FAILS (`ImageQueryEndpoints` not found).

- [ ] **Step 3: Implement the endpoints**

`src/PictureManager.Api/Endpoints/ImageQueryEndpoints.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Common;
using PictureManager.Application.Images;

namespace PictureManager.Api.Endpoints;

public static class ImageQueryEndpoints
{
    public static IEndpointRouteBuilder MapImageQueryEndpoints(this IEndpointRouteBuilder user)
    {
        user.MapGet("/images", ListAsync);
        user.MapGet("/images/{id:int}", GetAsync);
        user.MapPut("/images/{id:int}/favorite", SetFavoriteAsync);
        user.MapDelete("/images/{id:int}/favorite", ClearFavoriteAsync);
        return user;
    }

    public static async Task<Results<Ok<PagedResult<ImageListItem>>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> ListAsync(
        int? folderId, string? folder, string? fileName, bool? favoritesOnly, string? sort, string? order, string? cursor, int? limit,
        IImageQueryService service, CancellationToken cancellationToken)
    {
        var request = new ImageListRequest(folderId, folder, fileName, favoritesOnly ?? false, sort, order, cursor, limit);
        return (await service.ListAsync(request, cancellationToken)).ToOk();
    }

    public static async Task<Results<Ok<ImageDetail>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> GetAsync(
        int id, IImageQueryService service, CancellationToken cancellationToken) =>
        (await service.GetDetailAsync(id, cancellationToken)).ToOk();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> SetFavoriteAsync(
        int id, IImageQueryService service, CancellationToken cancellationToken) =>
        (await service.SetFavoriteAsync(id, true, cancellationToken)).ToNoContent();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> ClearFavoriteAsync(
        int id, IImageQueryService service, CancellationToken cancellationToken) =>
        (await service.SetFavoriteAsync(id, false, cancellationToken)).ToNoContent();
}
```

In `ImageEndpoints.cs` replace the mapping method with:

```csharp
    public static IEndpointRouteBuilder MapImageEndpoints(this IEndpointRouteBuilder user)
    {
        user.MapGet("/images/{id:int}/thumbnail", GetThumbnailAsync);
        user.MapGet("/images/{id:int}/preview", GetPreviewAsync);
        return user;
    }
```

In `ScanEndpoints.cs` replace the mapping method with:

```csharp
    public static IEndpointRouteBuilder MapScanEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapPost("/scans", StartScanAsync);
        admin.MapGet("/scans/{id:int}/events", StreamScanEventsAsync);
        return admin;
    }
```

In `Program.cs` replace

```csharp
    app.MapScanEndpoints();

    app.UseMiddleware<ImageCacheControlMiddleware>();
    app.MapImageEndpoints();
```

with

```csharp
    app.UseMiddleware<ImageCacheControlMiddleware>();

    // Every /api endpoint lives in exactly one of these two groups (ApiSurfaceMetadata). v2 auth seam:
    // user.RequireAuthorization(); admin.RequireAuthorization("AdminOnly"); with no route changes.
    var user = app.MapGroup("/api").WithMetadata(new ApiSurfaceMetadata(ApiSurface.User));
    var admin = app.MapGroup("/api").WithMetadata(new ApiSurfaceMetadata(ApiSurface.Admin));

    user.MapImageEndpoints();
    user.MapImageQueryEndpoints();
    admin.MapScanEndpoints();
```

- [ ] **Step 4: Run the endpoint tests and the whole Api test project**

Run: `dotnet test tests/PictureManager.Api.Tests`
Expected: PASS, 0 failed (the existing phase 3/4 handler tests still pass, since they call the static
handlers directly).

- [ ] **Step 5: Quick manual routing check**

Run the app in the background (`dotnet run --urls http://localhost:5199` from `src/PictureManager.Api`,
so the relative dev paths resolve) and:
- `curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:5199/api/images?limit=2"`. Expected: `200`.
- `curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:5199/api/images?limit=0"`. Expected: `400`.

Stop the app.

- [ ] **Step 6: Commit**

```bash
git add src/PictureManager.Api tests/PictureManager.Api.Tests
git commit -m "feat: add image list/detail/favorite endpoints and user/admin route groups"
```

---

## Task 7: Folders: tree browsing, detail with breadcrumb, remove/restore

**Files:**
- Create: `src/PictureManager.Application/Folders/FolderModels.cs`
- Create: `src/PictureManager.Application/Folders/IFolderService.cs`
- Create: `src/PictureManager.Application/Folders/FolderService.cs`
- Modify: `src/PictureManager.Application/Repositories/IFolderRepository.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/FolderRepository.cs`
- Modify: `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- Create: `src/PictureManager.Api/Endpoints/FolderEndpoints.cs`
- Modify: `src/PictureManager.Api/Program.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FolderQueryRepositoryTests.cs` (extend)
- Test: `tests/PictureManager.Application.Tests/Folders/FolderServiceTests.cs`
- Test: `tests/PictureManager.Api.Tests/Endpoints/FolderEndpointsTests.cs`

**Interfaces:**
- Consumes: `IFolderRepository.IsVisibleAsync` (Task 5); `VisibilityExtensions.WhereVisible` (Task 4);
  `Result`, `ResultHttpExtensions` (Task 3); the `user`/`admin` groups (Task 6).
- Produces (namespace `PictureManager.Application.Folders`):
  - `sealed record FolderNode(int Id, string Name, bool HasChildren, int ImageCount)`
  - `sealed record BreadcrumbItem(int Id, string Name)`
  - `sealed record FolderDetail(int Id, string Name, int RootId, string RootName, string RelativePath, int ImageCount, IReadOnlyList<BreadcrumbItem> Breadcrumb)`
  - `sealed record RemovedFolder(int Id, string Name, string RootName, string RelativePath)`
- Produces, added to `IFolderRepository`:
  - `Task<IReadOnlyList<FolderNode>> GetVisibleRootFoldersAsync(CancellationToken cancellationToken = default)`
  - `Task<IReadOnlyList<FolderNode>> GetVisibleChildrenAsync(int parentId, CancellationToken cancellationToken = default)`
  - `Task<FolderDetail?> GetVisibleDetailAsync(int id, CancellationToken cancellationToken = default)`
  - `Task<IReadOnlyList<RemovedFolder>> GetRemovedAsync(CancellationToken cancellationToken = default)`
  - `Task RemoveFromCollectionAsync(int folderId, CancellationToken cancellationToken = default)`
  - `Task DeleteSubtreeAsync(int folderId, CancellationToken cancellationToken = default)` (used by the
    scanner in Task 14)
  - `Task RenameRootFolderAsync(int rootId, string name, CancellationToken cancellationToken = default)`
    (used by roots in Task 8)
- Produces `interface IFolderService`, implemented by `FolderService(IFolderRepository, IClock)` (scoped):
  - `Task<IReadOnlyList<FolderNode>> GetRootsAsync(CancellationToken cancellationToken = default)`
  - `Task<Result<IReadOnlyList<FolderNode>>> GetChildrenAsync(int id, CancellationToken cancellationToken = default)`
  - `Task<Result<FolderDetail>> GetAsync(int id, CancellationToken cancellationToken = default)`
  - `Task<IReadOnlyList<RemovedFolder>> GetRemovedAsync(CancellationToken cancellationToken = default)`
  - `Task<Result> RemoveAsync(int id, CancellationToken cancellationToken = default)`
  - `Task<Result> RestoreAsync(int id, CancellationToken cancellationToken = default)`
- Produces `FolderEndpoints.MapFolderEndpoints(this IEndpointRouteBuilder user, IEndpointRouteBuilder admin)`.

- [ ] **Step 1: Write the failing repository tests**

Add these tests to `FolderQueryRepositoryTests` (the Task 5 class), and add `using System.Linq;`,
`using Microsoft.EntityFrameworkCore;` and `using PictureManager.Application.Folders;` at the top:

```csharp
    [Fact]
    public async Task GetVisibleRootFoldersAsync_ReturnsTopFoldersOfActiveRoots_OrderedByName()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var zeta = TestData.Folder(TestData.Root("Zeta"), "");
        var alpha = TestData.Folder(TestData.Root("alpha"), "");
        var offline = TestData.Folder(TestData.Root("offline", isActive: false), "");
        db.Context.Folders.AddRange(zeta, alpha, offline);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var nodes = await new FolderRepository(context).GetVisibleRootFoldersAsync();

        nodes.Select(n => n.Id).Should().Equal(alpha.Id, zeta.Id);
    }

    [Fact]
    public async Task GetVisibleChildrenAsync_ActiveOnly_WithHasChildrenAndDirectVisibleImageCount()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var madeira = TestData.Folder(root, "Madeira", top);
        var portugal = TestData.Folder(root, "portugal", top);
        var removed = TestData.Folder(root, "Removed", top, isActive: false);
        var nested = TestData.Folder(root, "Madeira/Day1", madeira);
        db.Context.Folders.AddRange(top, madeira, portugal, removed, nested);
        db.Context.Images.AddRange(
            TestData.Image(madeira, "a"),
            TestData.Image(madeira, "b"),
            TestData.Image(madeira, "gone", missingSinceUtc: TestData.Utc),
            TestData.Image(nested, "deeper"));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var nodes = await new FolderRepository(context).GetVisibleChildrenAsync(top.Id);

        nodes.Should().Equal(
            new FolderNode(madeira.Id, "Madeira", HasChildren: true, ImageCount: 2),
            new FolderNode(portugal.Id, "portugal", HasChildren: false, ImageCount: 0));
    }

    [Fact]
    public async Task GetVisibleDetailAsync_BuildsBreadcrumbFromRootToSelf()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("nas");
        var top = TestData.Folder(root, "");
        var holidays = TestData.Folder(root, "Holidays", top);
        var madeira = TestData.Folder(root, "Holidays/Madeira", holidays);
        db.Context.Folders.AddRange(top, holidays, madeira);
        db.Context.Images.Add(TestData.Image(madeira, "a"));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var detail = await new FolderRepository(context).GetVisibleDetailAsync(madeira.Id);

        detail.Should().NotBeNull();
        detail!.RootName.Should().Be("nas");
        detail.RelativePath.Should().Be("Holidays/Madeira");
        detail.ImageCount.Should().Be(1);
        detail.Breadcrumb.Should().Equal(
            new BreadcrumbItem(top.Id, "nas"),
            new BreadcrumbItem(holidays.Id, "Holidays"),
            new BreadcrumbItem(madeira.Id, "Madeira"));
    }

    [Fact]
    public async Task RemoveFromCollectionAsync_TombstonesFolder_AndPurgesImagesSubfoldersAndAlbumEntries()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var target = TestData.Folder(root, "Target", top);
        var child = TestData.Folder(root, "Target/Child", target);
        var grandchild = TestData.Folder(root, "Target/Child/Grand", child);
        var sibling = TestData.Folder(root, "Sibling", top);
        var inTarget = TestData.Image(target, "t");
        var inGrandchild = TestData.Image(grandchild, "g");
        var inSibling = TestData.Image(sibling, "s");
        var album = TestData.Album("A");
        db.Context.AlbumImages.AddRange(
            TestData.AlbumImage(album, inTarget, 0),
            TestData.AlbumImage(album, inGrandchild, 1),
            TestData.AlbumImage(album, inSibling, 2));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new FolderRepository(context).RemoveFromCollectionAsync(target.Id);

        await using var read = db.CreateContext();
        var tombstone = await read.Folders.SingleAsync(f => f.Id == target.Id);
        tombstone.IsActive.Should().BeFalse();
        (await read.Folders.AnyAsync(f => f.Id == child.Id || f.Id == grandchild.Id)).Should().BeFalse();
        (await read.Images.Select(i => i.Id).ToListAsync()).Should().Equal(inSibling.Id);
        (await read.AlbumImages.Select(ai => ai.ImageId).ToListAsync()).Should().Equal(inSibling.Id);
        (await read.Folders.AnyAsync(f => f.Id == sibling.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task GetRemovedAsync_ListsTombstonesWithRootName()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("nas");
        var top = TestData.Folder(root, "");
        var removed = TestData.Folder(root, "Old", top, isActive: false);
        db.Context.Folders.AddRange(top, removed);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var list = await new FolderRepository(context).GetRemovedAsync();

        list.Should().Equal(new RemovedFolder(removed.Id, "Old", "nas", "Old"));
    }

    [Fact]
    public async Task DeleteSubtreeAsync_DeletesFolderAndEverythingBeneath_WithoutTombstone()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var raw = TestData.Folder(root, "raw", top);
        var rawChild = TestData.Folder(root, "raw/2025", raw);
        db.Context.Images.AddRange(TestData.Image(raw, "a"), TestData.Image(rawChild, "b"), TestData.Image(top, "keep"));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new FolderRepository(context).DeleteSubtreeAsync(raw.Id);

        await using var read = db.CreateContext();
        (await read.Folders.Select(f => f.Id).ToListAsync()).Should().Equal(top.Id);
        (await read.Images.Select(i => i.FileName).ToListAsync()).Should().Equal("keep");
    }

    [Fact]
    public async Task RenameRootFolderAsync_RenamesOnlyTheRootsTopFolder()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("old");
        var top = TestData.Folder(root, "");
        var child = TestData.Folder(root, "child", top);
        db.Context.Folders.AddRange(top, child);
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new FolderRepository(context).RenameRootFolderAsync(root.Id, "Family Photos");

        await using var read = db.CreateContext();
        (await read.Folders.SingleAsync(f => f.Id == top.Id)).Name.Should().Be("Family Photos");
        (await read.Folders.SingleAsync(f => f.Id == child.Id)).Name.Should().Be("child");
    }
```

- [ ] **Step 2: Write the failing service and endpoint tests**

Create `tests/PictureManager.Application.Tests/Folders/FolderServiceTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Folders;
using PictureManager.Application.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Folders;

public class FolderServiceTests
{
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public FolderServiceTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private FolderService CreateService() => new(_folders, _clock);

    [Fact]
    public async Task GetChildrenAsync_ParentNotVisible_ReturnsNotFound()
    {
        _folders.IsVisibleAsync(4, Arg.Any<CancellationToken>()).Returns(false);

        (await CreateService().GetChildrenAsync(4)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetChildrenAsync_ParentVisible_ReturnsChildren()
    {
        var children = new[] { new FolderNode(5, "a", false, 3) };
        _folders.IsVisibleAsync(4, Arg.Any<CancellationToken>()).Returns(true);
        _folders.GetVisibleChildrenAsync(4, Arg.Any<CancellationToken>()).Returns(children);

        (await CreateService().GetChildrenAsync(4)).Value.Should().BeEquivalentTo(children);
    }

    [Fact]
    public async Task GetAsync_NotVisible_ReturnsNotFound()
    {
        _folders.GetVisibleDetailAsync(4, Arg.Any<CancellationToken>()).Returns((FolderDetail?)null);

        (await CreateService().GetAsync(4)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task RemoveAsync_UnknownFolder_ReturnsNotFound()
    {
        _folders.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns((Folder?)null);

        (await CreateService().RemoveAsync(4)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task RemoveAsync_AlreadyRemoved_ReturnsNotFound()
    {
        _folders.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(new Folder { Id = 4, ParentId = 1, IsActive = false });

        (await CreateService().RemoveAsync(4)).Status.Should().Be(ResultStatus.NotFound);
        await _folders.DidNotReceive().RemoveFromCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_RootTopFolder_ReturnsInvalid()
    {
        _folders.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(new Folder { Id = 4, ParentId = null, IsActive = true });

        (await CreateService().RemoveAsync(4)).Status.Should().Be(ResultStatus.Invalid);
        await _folders.DidNotReceive().RemoveFromCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_ActiveSubfolder_RemovesFromCollection()
    {
        _folders.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(new Folder { Id = 4, ParentId = 1, IsActive = true });

        (await CreateService().RemoveAsync(4)).IsSuccess.Should().BeTrue();
        await _folders.Received(1).RemoveFromCollectionAsync(4, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreAsync_NotRemoved_ReturnsInvalid()
    {
        _folders.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(new Folder { Id = 4, ParentId = 1, IsActive = true });

        (await CreateService().RestoreAsync(4)).Status.Should().Be(ResultStatus.Invalid);
    }

    [Fact]
    public async Task RestoreAsync_Tombstone_ReactivatesIt()
    {
        var folder = new Folder { Id = 4, ParentId = 1, IsActive = false };
        _folders.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(folder);

        (await CreateService().RestoreAsync(4)).IsSuccess.Should().BeTrue();
        await _folders.Received(1).UpdateAsync(
            Arg.Is<Folder>(f => f.Id == 4 && f.IsActive && f.ModifiedUtc == _clock.UtcNow), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreAsync_Unknown_ReturnsNotFound()
    {
        _folders.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns((Folder?)null);

        (await CreateService().RestoreAsync(4)).Status.Should().Be(ResultStatus.NotFound);
    }
}
```

Create `tests/PictureManager.Api.Tests/Endpoints/FolderEndpointsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Common;
using PictureManager.Application.Folders;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class FolderEndpointsTests
{
    private readonly IFolderService _service = Substitute.For<IFolderService>();

    [Fact]
    public async Task GetRootsAsync_ReturnsOk()
    {
        _service.GetRootsAsync(Arg.Any<CancellationToken>()).Returns(new[] { new FolderNode(1, "nas", true, 0) });

        var result = await FolderEndpoints.GetRootsAsync(_service, CancellationToken.None);

        result.Value.Should().ContainSingle();
    }

    [Fact]
    public async Task GetChildrenAsync_NotFound_Returns404()
    {
        _service.GetChildrenAsync(9, Arg.Any<CancellationToken>()).Returns(Result.NotFound());

        (await FolderEndpoints.GetChildrenAsync(9, _service, CancellationToken.None)).Result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task RemoveAsync_RootTopFolder_ReturnsValidationProblem()
    {
        _service.RemoveAsync(1, Arg.Any<CancellationToken>()).Returns(Result.Invalid("id", "nope"));

        (await FolderEndpoints.RemoveAsync(1, _service, CancellationToken.None)).Result.Should().BeOfType<ValidationProblem>();
    }

    [Fact]
    public async Task RestoreAsync_Success_ReturnsNoContent()
    {
        _service.RestoreAsync(4, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        (await FolderEndpoints.RestoreAsync(4, _service, CancellationToken.None)).Result.Should().BeOfType<NoContent>();
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet build`
Expected: build FAILS (`FolderNode`, `IFolderService`, `FolderEndpoints`, new repository methods not found).

- [ ] **Step 4: Implement the Application side**

`src/PictureManager.Application/Folders/FolderModels.cs`:

```csharp
using System.Collections.Generic;

namespace PictureManager.Application.Folders;

/// <summary>Tree node. ImageCount = visible images directly in the folder (what its grid shows).</summary>
public sealed record FolderNode(int Id, string Name, bool HasChildren, int ImageCount);

public sealed record BreadcrumbItem(int Id, string Name);

/// <summary>Breadcrumb runs from the root's top folder down to (and including) this folder.</summary>
public sealed record FolderDetail(
    int Id,
    string Name,
    int RootId,
    string RootName,
    string RelativePath,
    int ImageCount,
    IReadOnlyList<BreadcrumbItem> Breadcrumb);

public sealed record RemovedFolder(int Id, string Name, string RootName, string RelativePath);
```

Add to `IFolderRepository` (plus `using PictureManager.Application.Folders;`):

```csharp
    /// <summary>Top folders (ParentId == null) of active roots, ordered by lower(Name).</summary>
    Task<IReadOnlyList<FolderNode>> GetVisibleRootFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>Active children of the folder, ordered by lower(Name).</summary>
    Task<IReadOnlyList<FolderNode>> GetVisibleChildrenAsync(int parentId, CancellationToken cancellationToken = default);

    Task<FolderDetail?> GetVisibleDetailAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Tombstoned folders (IsActive == false).</summary>
    Task<IReadOnlyList<RemovedFolder>> GetRemovedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// In one transaction: hard-deletes the folder's images and every folder beneath it (with their images
    /// and album entries, via the database's cascades), then marks the folder itself IsActive = false.
    /// </summary>
    Task RemoveFromCollectionAsync(int folderId, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes the folder and everything beneath it (no tombstone).</summary>
    Task DeleteSubtreeAsync(int folderId, CancellationToken cancellationToken = default);

    /// <summary>Renames the root's top folder (the tree node that shows the root's name).</summary>
    Task RenameRootFolderAsync(int rootId, string name, CancellationToken cancellationToken = default);
```

`src/PictureManager.Application/Folders/IFolderService.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Folders;

public interface IFolderService
{
    Task<IReadOnlyList<FolderNode>> GetRootsAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<FolderNode>>> GetChildrenAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<FolderDetail>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemovedFolder>> GetRemovedAsync(CancellationToken cancellationToken = default);

    Task<Result> RemoveAsync(int id, CancellationToken cancellationToken = default);

    Task<Result> RestoreAsync(int id, CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Application/Folders/FolderService.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Folders;

public sealed class FolderService : IFolderService
{
    private readonly IFolderRepository _folders;
    private readonly IClock _clock;

    public FolderService(IFolderRepository folders, IClock clock)
    {
        _folders = folders;
        _clock = clock;
    }

    public Task<IReadOnlyList<FolderNode>> GetRootsAsync(CancellationToken cancellationToken = default) =>
        _folders.GetVisibleRootFoldersAsync(cancellationToken);

    public async Task<Result<IReadOnlyList<FolderNode>>> GetChildrenAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!await _folders.IsVisibleAsync(id, cancellationToken))
            return Result.NotFound();

        return Result<IReadOnlyList<FolderNode>>.Ok(await _folders.GetVisibleChildrenAsync(id, cancellationToken));
    }

    public async Task<Result<FolderDetail>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var detail = await _folders.GetVisibleDetailAsync(id, cancellationToken);
        return detail is null ? Result.NotFound() : Result<FolderDetail>.Ok(detail);
    }

    public Task<IReadOnlyList<RemovedFolder>> GetRemovedAsync(CancellationToken cancellationToken = default) =>
        _folders.GetRemovedAsync(cancellationToken);

    public async Task<Result> RemoveAsync(int id, CancellationToken cancellationToken = default)
    {
        var folder = await _folders.GetByIdAsync(id, cancellationToken);
        if (folder is null || !folder.IsActive)
            return Result.NotFound();
        if (folder.ParentId is null)
            return Result.Invalid("id", "A root's top folder cannot be removed; deactivate the root instead.");

        await _folders.RemoveFromCollectionAsync(id, cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> RestoreAsync(int id, CancellationToken cancellationToken = default)
    {
        var folder = await _folders.GetByIdAsync(id, cancellationToken);
        if (folder is null)
            return Result.NotFound();
        if (folder.IsActive)
            return Result.Invalid("id", "The folder has not been removed.");

        folder.IsActive = true;
        folder.ModifiedUtc = _clock.UtcNow;
        await _folders.UpdateAsync(folder, cancellationToken);
        return Result.Ok();
    }
}
```

In `AddApplication` add (with `using PictureManager.Application.Folders;`):

```csharp
        services.AddScoped<IFolderService, FolderService>();
```

- [ ] **Step 5: Implement the repository methods**

Add to `FolderRepository` (plus `using PictureManager.Application.Folders;` and `using PictureManager.Model;`):

```csharp
    public async Task<IReadOnlyList<FolderNode>> GetVisibleRootFoldersAsync(CancellationToken cancellationToken = default)
    {
        return await ToNodes(_dbContext.Folders.AsNoTracking().WhereVisible().Where(f => f.ParentId == null))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FolderNode>> GetVisibleChildrenAsync(int parentId, CancellationToken cancellationToken = default)
    {
        return await ToNodes(_dbContext.Folders.AsNoTracking().WhereVisible().Where(f => f.ParentId == parentId))
            .ToListAsync(cancellationToken);
    }

    public async Task<FolderDetail?> GetVisibleDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        var folder = await _dbContext.Folders.AsNoTracking().WhereVisible()
            .Where(f => f.Id == id)
            .Select(f => new
            {
                f.Id,
                f.Name,
                f.RootId,
                RootName = f.Root!.Name,
                f.RelativePath,
                ImageCount = f.Images.Count(i => i.MissingSinceUtc == null)
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (folder is null)
            return null;

        var ancestorPaths = AncestorPaths(folder.RelativePath);
        var crumbs = await _dbContext.Folders.AsNoTracking()
            .Where(f => f.RootId == folder.RootId && ancestorPaths.Contains(f.RelativePath))
            .Select(f => new { f.Id, f.Name, f.RelativePath })
            .ToListAsync(cancellationToken);

        var breadcrumb = crumbs
            .OrderBy(c => c.RelativePath.Length)
            .Select(c => new BreadcrumbItem(c.Id, c.Name))
            .ToList();

        return new FolderDetail(folder.Id, folder.Name, folder.RootId, folder.RootName, folder.RelativePath,
            folder.ImageCount, breadcrumb);
    }

    public async Task<IReadOnlyList<RemovedFolder>> GetRemovedAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Folders.AsNoTracking()
            .Where(f => !f.IsActive)
            .OrderBy(f => f.Root!.Name).ThenBy(f => f.RelativePath)
            .Select(f => new RemovedFolder(f.Id, f.Name, f.Root!.Name, f.RelativePath))
            .ToListAsync(cancellationToken);
    }

    public async Task RemoveFromCollectionAsync(int folderId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // The folder's own images go explicitly (the folder row survives as a tombstone). Everything
        // beneath goes by deleting the direct children: the database cascades Folder.ParentId ->
        // deeper folders, Image.FolderId -> their images, and AlbumImage.ImageId -> album entries.
        await _dbContext.Images.Where(i => i.FolderId == folderId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Folders.Where(f => f.ParentId == folderId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Folders.Where(f => f.Id == folderId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.IsActive, false), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteSubtreeAsync(int folderId, CancellationToken cancellationToken = default)
    {
        await _dbContext.Folders.Where(f => f.Id == folderId).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task RenameRootFolderAsync(int rootId, string name, CancellationToken cancellationToken = default)
    {
        await _dbContext.Folders.Where(f => f.RootId == rootId && f.ParentId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Name, name), cancellationToken);
    }

    private static IQueryable<FolderNode> ToNodes(IQueryable<Folder> folders) =>
        folders
            .OrderBy(f => f.Name.ToLower()).ThenBy(f => f.Id)
            .Select(f => new FolderNode(
                f.Id,
                f.Name,
                f.Children.Any(c => c.IsActive),
                f.Images.Count(i => i.MissingSinceUtc == null)));

    // "", "a", "a/b" for "a/b": the root's top folder plus every ancestor and the folder itself.
    private static List<string> AncestorPaths(string relativePath)
    {
        var paths = new List<string> { string.Empty };
        if (relativePath.Length == 0)
            return paths;

        var segments = relativePath.Split('/');
        for (var i = 1; i <= segments.Length; i++)
            paths.Add(string.Join('/', segments, 0, i));
        return paths;
    }
```

(The spec describes the purged set as "folders beneath it by `RelativePath` prefix". The `ParentId`
cascade deletes exactly that set, and the repository test above proves it.)

- [ ] **Step 6: Implement the endpoints**

`src/PictureManager.Api/Endpoints/FolderEndpoints.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Folders;

namespace PictureManager.Api.Endpoints;

public static class FolderEndpoints
{
    public static void MapFolderEndpoints(this IEndpointRouteBuilder user, IEndpointRouteBuilder admin)
    {
        user.MapGet("/folders/roots", GetRootsAsync);
        user.MapGet("/folders/{id:int}/children", GetChildrenAsync);
        user.MapGet("/folders/{id:int}", GetAsync);

        admin.MapGet("/folders/removed", GetRemovedAsync);
        admin.MapDelete("/folders/{id:int}", RemoveAsync);
        admin.MapPost("/folders/{id:int}/restore", RestoreAsync);
    }

    public static async Task<Ok<IReadOnlyList<FolderNode>>> GetRootsAsync(IFolderService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetRootsAsync(cancellationToken));

    public static async Task<Results<Ok<IReadOnlyList<FolderNode>>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> GetChildrenAsync(
        int id, IFolderService service, CancellationToken cancellationToken) =>
        (await service.GetChildrenAsync(id, cancellationToken)).ToOk();

    public static async Task<Results<Ok<FolderDetail>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> GetAsync(
        int id, IFolderService service, CancellationToken cancellationToken) =>
        (await service.GetAsync(id, cancellationToken)).ToOk();

    public static async Task<Ok<IReadOnlyList<RemovedFolder>>> GetRemovedAsync(IFolderService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetRemovedAsync(cancellationToken));

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> RemoveAsync(
        int id, IFolderService service, CancellationToken cancellationToken) =>
        (await service.RemoveAsync(id, cancellationToken)).ToNoContent();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> RestoreAsync(
        int id, IFolderService service, CancellationToken cancellationToken) =>
        (await service.RestoreAsync(id, cancellationToken)).ToNoContent();
}
```

In `Program.cs`, after `user.MapImageQueryEndpoints();` add:

```csharp
    user.MapFolderEndpoints(admin);
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~FolderQueryRepositoryTests"`
Expected: PASS, 8 tests.

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~FolderServiceTests"`
Expected: PASS, 10 tests.

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~FolderEndpointsTests"`
Expected: PASS, 4 tests.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat: add folder tree browsing, breadcrumb detail and remove/restore from collection"
```

---

## Task 8: Roots: config seeding with Alias, admin list/patch

**Files:**
- Delete: `src/PictureManager.Application/Scanning/DevImageRootSeeder.cs`,
  `src/PictureManager.Application/Scanning/IDevImageRootSeeder.cs`,
  `src/PictureManager.Application/Scanning/DevImageRootOptions.cs`,
  `tests/PictureManager.Application.Tests/Scanning/DevImageRootSeederTests.cs`
- Create: `src/PictureManager.Application/Roots/RootNameRules.cs`
- Create: `src/PictureManager.Application/Roots/ImageRootsOptions.cs`
- Create: `src/PictureManager.Application/Roots/IImageRootSeeder.cs`
- Create: `src/PictureManager.Application/Roots/ImageRootSeeder.cs`
- Create: `src/PictureManager.Application/Roots/RootModels.cs`
- Create: `src/PictureManager.Application/Roots/IRootService.cs`
- Create: `src/PictureManager.Application/Roots/RootService.cs`
- Modify: `src/PictureManager.Application/Repositories/IImageRootRepository.cs`,
  `src/PictureManager.Infrastructure/Persistence/Repositories/ImageRootRepository.cs` (add `UpdateAsync`)
- Modify: `src/PictureManager.Application/PictureManager.Application.csproj` (add the Logging.Abstractions
  package)
- Modify: `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- Modify: `src/PictureManager.Api/Endpoints/ResultHttpExtensions.cs` (add `Invalid(field, message)`)
- Create: `src/PictureManager.Api/Endpoints/RootEndpoints.cs`
- Modify: `src/PictureManager.Api/Program.cs`, `src/PictureManager.Api/appsettings.Development.json`
- Test: `tests/PictureManager.Application.Tests/Roots/ImageRootSeederTests.cs`
- Test: `tests/PictureManager.Application.Tests/Roots/RootServiceTests.cs`
- Test: `tests/PictureManager.Api.Tests/Endpoints/RootEndpointsTests.cs`

**Interfaces:**
- Consumes: `IFolderRepository.RenameRootFolderAsync` (Task 7); `PatchJson`, `ResultHttpExtensions`
  (Task 3); `ImageRoot.Alias` (Task 2).
- Produces (namespace `PictureManager.Application.Roots`):
  - `static class RootNameRules`, with `const int MaxLength = 200`,
    `string? Validate(string? value)` (returns an error message or null), and
    `string ExportSegment(ImageRoot root)` (= `root.Alias ?? root.Name`)
  - `sealed class ImageRootConfigEntry { string? Name; string? MountPath; string? Alias }`
  - `sealed class ImageRootsOptions { List<ImageRootConfigEntry> Entries }`
  - `interface IImageRootSeeder { Task SeedAsync(CancellationToken cancellationToken = default); }`,
    implemented by `ImageRootSeeder(IImageRootRepository, IClock, ImageRootsOptions, ILogger<ImageRootSeeder>)`
  - `sealed record RootSummary(int Id, string Name, string? Alias, string MountPath, bool IsActive, string ExportSegment)`
  - `sealed record RootUpdate(string? Name, bool AliasSpecified, string? Alias, bool? IsActive)`
  - `interface IRootService`, implemented by `RootService(IImageRootRepository, IFolderRepository)`:
    - `Task<IReadOnlyList<RootSummary>> GetAllAsync(CancellationToken cancellationToken = default)`
    - `Task<Result<RootSummary>> UpdateAsync(int id, RootUpdate update, CancellationToken cancellationToken = default)`
  - `IImageRootRepository.UpdateAsync(ImageRoot root, CancellationToken cancellationToken = default)`
- Produces (Api):
  - `ResultHttpExtensions.Invalid(string field, string message)` → `ValidationProblem`
  - `RootEndpoints.MapRootEndpoints(this IEndpointRouteBuilder admin)`

- [ ] **Step 1: Add the logging abstractions package**

Run: `dotnet add src/PictureManager.Application package Microsoft.Extensions.Logging.Abstractions`
Expected: `PackageReference for package 'Microsoft.Extensions.Logging.Abstractions' version '<x>' added`.
Note the version for the commit message.

- [ ] **Step 2: Write the failing seeder tests**

Delete `tests/PictureManager.Application.Tests/Scanning/DevImageRootSeederTests.cs`. Create
`tests/PictureManager.Application.Tests/Roots/ImageRootSeederTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Roots;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Roots;

public class ImageRootSeederTests
{
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly List<ImageRoot> _existing = new();

    public ImageRootSeederTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _roots.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => _existing);
        _roots.AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<ImageRoot>());
    }

    private Task SeedAsync(params ImageRootConfigEntry[] entries) =>
        new ImageRootSeeder(_roots, _clock, new ImageRootsOptions { Entries = new List<ImageRootConfigEntry>(entries) },
            NullLogger<ImageRootSeeder>.Instance).SeedAsync();

    private static ImageRootConfigEntry Entry(string? name, string? mountPath, string? alias = null) =>
        new() { Name = name, MountPath = mountPath, Alias = alias };

    [Fact]
    public async Task SeedAsync_UnknownMountPath_CreatesActiveRootWithAlias()
    {
        await SeedAsync(Entry("nas-photos", "/images/photos", "family_photos"));

        await _roots.Received(1).AddAsync(
            Arg.Is<ImageRoot>(r => r.Name == "nas-photos" && r.MountPath == "/images/photos" && r.Alias == "family_photos"
                                   && r.IsActive && r.CreatedUtc == _clock.UtcNow),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_ExistingMountPath_LeavesRootUntouched_EvenIfConfigNameDiffers()
    {
        _existing.Add(new ImageRoot { Id = 1, Name = "Family Photos", MountPath = "/images/photos", IsActive = false });

        await SeedAsync(Entry("nas-photos", "/images/photos", "family_photos"));

        await _roots.DidNotReceive().AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
        await _roots.DidNotReceive().UpdateAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_NameClashesCaseInsensitively_SkipsEntry()
    {
        _existing.Add(new ImageRoot { Id = 1, Name = "Photos", MountPath = "/a" });

        await SeedAsync(Entry("photos", "/b"));

        await _roots.DidNotReceive().AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_AliasClashesWithAnotherRootsSegment_DropsAliasButCreatesRoot()
    {
        _existing.Add(new ImageRoot { Id = 1, Name = "family", MountPath = "/a" });

        await SeedAsync(Entry("nas-photos", "/b", "Family"));

        await _roots.Received(1).AddAsync(Arg.Is<ImageRoot>(r => r.Name == "nas-photos" && r.Alias == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_InvalidAlias_DropsAliasButCreatesRoot()
    {
        await SeedAsync(Entry("nas-photos", "/b", "family/photos"));

        await _roots.Received(1).AddAsync(Arg.Is<ImageRoot>(r => r.Name == "nas-photos" && r.Alias == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_NameEqualsAnotherRootsAlias_SkipsEntry()
    {
        _existing.Add(new ImageRoot { Id = 1, Name = "nas-a", Alias = "photos", MountPath = "/a" });

        await SeedAsync(Entry("Photos", "/b"));

        await _roots.DidNotReceive().AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "/x")]
    [InlineData("name", null)]
    [InlineData("  ", "/x")]
    [InlineData("a/b", "/x")]
    public async Task SeedAsync_IncompleteOrInvalidEntry_IsSkipped(string? name, string? mountPath)
    {
        await SeedAsync(Entry(name, mountPath));

        await _roots.DidNotReceive().AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_SecondConfigEntryClashingWithFirst_IsSkipped()
    {
        await SeedAsync(Entry("photos", "/a"), Entry("PHOTOS", "/b"));

        await _roots.Received(1).AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_NoEntries_DoesNotThrowOrAdd()
    {
        _existing.Add(new ImageRoot { Id = 1, Name = "legacy", MountPath = "/old" });

        var act = () => SeedAsync();

        await act.Should().NotThrowAsync();
        await _roots.DidNotReceive().AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 3: Write the failing root service tests**

Create `tests/PictureManager.Application.Tests/Roots/RootServiceTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Roots;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Roots;

public class RootServiceTests
{
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly ImageRoot _photos = new() { Id = 1, Name = "nas-photos", Alias = "family", MountPath = "/images/photos", IsActive = true };
    private readonly ImageRoot _work = new() { Id = 2, Name = "work-nas", MountPath = "/images/work", IsActive = true };

    public RootServiceTests()
    {
        _roots.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ImageRoot> { _photos, _work });
    }

    private RootService CreateService() => new(_roots, _folders);

    [Fact]
    public async Task GetAllAsync_IncludesExportSegment()
    {
        var all = await CreateService().GetAllAsync();

        all.Should().Equal(
            new RootSummary(1, "nas-photos", "family", "/images/photos", true, "family"),
            new RootSummary(2, "work-nas", null, "/images/work", true, "work-nas"));
    }

    [Fact]
    public async Task UpdateAsync_UnknownRoot_ReturnsNotFound()
    {
        (await CreateService().UpdateAsync(99, new RootUpdate("x", false, null, null))).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task UpdateAsync_Rename_SavesRoot_AndRenamesItsTopFolder()
    {
        var result = await CreateService().UpdateAsync(2, new RootUpdate("  Work  ", false, null, null));

        result.Value!.Name.Should().Be("Work");
        await _roots.Received(1).UpdateAsync(Arg.Is<ImageRoot>(r => r.Id == 2 && r.Name == "Work"), Arg.Any<CancellationToken>());
        await _folders.Received(1).RenameRootFolderAsync(2, "Work", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_NameClashesCaseInsensitively_ReturnsConflict()
    {
        (await CreateService().UpdateAsync(2, new RootUpdate("NAS-PHOTOS", false, null, null))).Status.Should().Be(ResultStatus.Conflict);
        await _roots.DidNotReceive().UpdateAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_AliasClashesWithAnotherRootsSegment_ReturnsConflict()
    {
        (await CreateService().UpdateAsync(1, new RootUpdate(null, true, "Work-NAS", null))).Status.Should().Be(ResultStatus.Conflict);
    }

    [Fact]
    public async Task UpdateAsync_AliasNull_ClearsAlias_AndExportFallsBackToName()
    {
        var result = await CreateService().UpdateAsync(1, new RootUpdate(null, true, null, null));

        result.Value!.Alias.Should().BeNull();
        result.Value.ExportSegment.Should().Be("nas-photos");
        await _folders.DidNotReceive().RenameRootFolderAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("   ")]
    public async Task UpdateAsync_InvalidAlias_ReturnsInvalid(string alias)
    {
        var result = await CreateService().UpdateAsync(1, new RootUpdate(null, true, alias, null));

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors!.Keys.Should().Contain("alias");
    }

    [Fact]
    public async Task UpdateAsync_BlankName_ReturnsInvalid()
    {
        var result = await CreateService().UpdateAsync(1, new RootUpdate(" ", false, null, null));

        result.Errors!.Keys.Should().Contain("name");
    }

    [Fact]
    public async Task UpdateAsync_Deactivate_SavesIsActiveFalse()
    {
        var result = await CreateService().UpdateAsync(2, new RootUpdate(null, false, null, false));

        result.Value!.IsActive.Should().BeFalse();
        await _roots.Received(1).UpdateAsync(Arg.Is<ImageRoot>(r => r.Id == 2 && !r.IsActive), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 4: Write the failing endpoint tests**

Create `tests/PictureManager.Api.Tests/Endpoints/RootEndpointsTests.cs`:

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Common;
using PictureManager.Application.Roots;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class RootEndpointsTests
{
    private readonly IRootService _service = Substitute.For<IRootService>();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task UpdateAsync_AliasNull_IsPassedAsAliasSpecified()
    {
        var summary = new RootSummary(1, "nas", null, "/m", true, "nas");
        _service.UpdateAsync(1, Arg.Any<RootUpdate>(), Arg.Any<CancellationToken>()).Returns(Result<RootSummary>.Ok(summary));

        var result = await RootEndpoints.UpdateAsync(1, Json("""{ "alias": null }"""), _service, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<RootSummary>>();
        await _service.Received(1).UpdateAsync(1, new RootUpdate(null, true, null, null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_OmittedAlias_IsNotSpecified()
    {
        _service.UpdateAsync(1, Arg.Any<RootUpdate>(), Arg.Any<CancellationToken>())
            .Returns(Result<RootSummary>.Ok(new RootSummary(1, "x", null, "/m", false, "x")));

        await RootEndpoints.UpdateAsync(1, Json("""{ "name": "x", "isActive": false }"""), _service, CancellationToken.None);

        await _service.Received(1).UpdateAsync(1, new RootUpdate("x", false, null, false), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("""{ "name": 5 }""")]
    [InlineData("""{ "name": null }""")]
    [InlineData("""{ "isActive": "yes" }""")]
    [InlineData("""[1, 2]""")]
    public async Task UpdateAsync_BadBody_ReturnsValidationProblem_WithoutCallingService(string json)
    {
        var result = await RootEndpoints.UpdateAsync(1, Json(json), _service, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
        await _service.DidNotReceive().UpdateAsync(Arg.Any<int>(), Arg.Any<RootUpdate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_Conflict_Returns409()
    {
        _service.UpdateAsync(1, Arg.Any<RootUpdate>(), Arg.Any<CancellationToken>()).Returns(Result.Conflict("taken"));

        var result = await RootEndpoints.UpdateAsync(1, Json("""{ "alias": "x" }"""), _service, CancellationToken.None);

        result.Result.Should().BeOfType<Conflict<Microsoft.AspNetCore.Mvc.ProblemDetails>>();
    }
}
```

- [ ] **Step 5: Run to verify they fail**

Run: `dotnet build`
Expected: build FAILS (`ImageRootSeeder`, `RootService`, `RootEndpoints` … not found; `DevImageRoot*`
references in `Program.cs`/DI once you delete the old files in Step 6).

- [ ] **Step 6: Implement the Application side**

Delete `src/PictureManager.Application/Scanning/DevImageRootSeeder.cs`, `IDevImageRootSeeder.cs` and
`DevImageRootOptions.cs`.

`src/PictureManager.Application/Roots/RootNameRules.cs`:

```csharp
using PictureManager.Model;

namespace PictureManager.Application.Roots;

/// <summary>
/// A root's Name and Alias each end up as one path segment in album exports (Alias ?? Name), so both
/// must be valid single segments. Callers trim before validating.
/// </summary>
public static class RootNameRules
{
    public const int MaxLength = 200;

    /// <returns>An error message, or null when the value is valid.</returns>
    public static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Must not be blank.";
        if (value.Length > MaxLength)
            return $"Must be at most {MaxLength} characters.";
        if (value.IndexOfAny(new[] { '/', '\\' }) >= 0)
            return "Must not contain '/' or '\\'.";
        return null;
    }

    public static string ExportSegment(ImageRoot root) => root.Alias ?? root.Name;
}
```

`src/PictureManager.Application/Roots/ImageRootsOptions.cs`:

```csharp
using System.Collections.Generic;

namespace PictureManager.Application.Roots;

/// <summary>One entry of the "ImageRoots" configuration array.</summary>
public sealed class ImageRootConfigEntry
{
    public string? Name { get; set; }
    public string? MountPath { get; set; }
    public string? Alias { get; set; }
}

public sealed class ImageRootsOptions
{
    public List<ImageRootConfigEntry> Entries { get; set; } = new();
}
```

`src/PictureManager.Application/Roots/IImageRootSeeder.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Roots;

public interface IImageRootSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Application/Roots/ImageRootSeeder.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Roots;

/// <summary>
/// "Config creates, the database is the truth": creates roots whose MountPath isn't registered yet and
/// never changes an existing root (its Name/Alias/IsActive may have been edited through the API).
/// Never fails startup: problems are logged and the entry (or just its alias) is skipped.
/// </summary>
public sealed class ImageRootSeeder : IImageRootSeeder
{
    private readonly IImageRootRepository _roots;
    private readonly IClock _clock;
    private readonly ImageRootsOptions _options;
    private readonly ILogger<ImageRootSeeder> _logger;

    public ImageRootSeeder(IImageRootRepository roots, IClock clock, ImageRootsOptions options, ILogger<ImageRootSeeder> logger)
    {
        _roots = roots;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var roots = (await _roots.GetAllAsync(cancellationToken)).ToList();
        var configuredMountPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in _options.Entries)
        {
            var name = entry.Name?.Trim();
            var mountPath = entry.MountPath?.Trim();
            if (string.IsNullOrEmpty(mountPath) || RootNameRules.Validate(name) is not null)
            {
                _logger.LogWarning(
                    "Skipping ImageRoots entry (Name: {Name}, MountPath: {MountPath}): a MountPath and a valid Name are required.",
                    entry.Name, entry.MountPath);
                continue;
            }

            configuredMountPaths.Add(mountPath);
            if (roots.Any(r => r.MountPath == mountPath))
                continue;

            var alias = string.IsNullOrWhiteSpace(entry.Alias) ? null : entry.Alias.Trim();
            if (alias is not null && (RootNameRules.Validate(alias) is not null || SegmentTaken(roots, alias)))
            {
                _logger.LogWarning(
                    "Ignoring alias {Alias} for new ImageRoot {Name}: it is not a valid path segment or another root already exports under it.",
                    alias, name);
                alias = null;
            }

            if (roots.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
                || SegmentTaken(roots, alias ?? name!))
            {
                _logger.LogWarning(
                    "Skipping ImageRoots entry {Name} ({MountPath}): another root already uses that name or export segment.",
                    name, mountPath);
                continue;
            }

            var created = await _roots.AddAsync(new ImageRoot
            {
                Name = name!,
                Alias = alias,
                MountPath = mountPath,
                IsActive = true,
                CreatedUtc = _clock.UtcNow
            }, cancellationToken);
            roots.Add(created);
        }

        foreach (var root in roots.Where(r => !configuredMountPaths.Contains(r.MountPath)))
        {
            _logger.LogWarning(
                "ImageRoot {Name} ({MountPath}) is not in the ImageRoots configuration; it is left unchanged.",
                root.Name, root.MountPath);
        }
    }

    private static bool SegmentTaken(IEnumerable<ImageRoot> roots, string segment) =>
        roots.Any(r => string.Equals(RootNameRules.ExportSegment(r), segment, StringComparison.OrdinalIgnoreCase));
}
```

`src/PictureManager.Application/Roots/RootModels.cs`:

```csharp
namespace PictureManager.Application.Roots;

public sealed record RootSummary(int Id, string Name, string? Alias, string MountPath, bool IsActive, string ExportSegment);

/// <summary>PATCH input. Name/IsActive null = unchanged; AliasSpecified distinguishes "clear" (null) from "unchanged".</summary>
public sealed record RootUpdate(string? Name, bool AliasSpecified, string? Alias, bool? IsActive);
```

`src/PictureManager.Application/Roots/IRootService.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Roots;

public interface IRootService
{
    Task<IReadOnlyList<RootSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Result<RootSummary>> UpdateAsync(int id, RootUpdate update, CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Application/Roots/RootService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Roots;

public sealed class RootService : IRootService
{
    private readonly IImageRootRepository _roots;
    private readonly IFolderRepository _folders;

    public RootService(IImageRootRepository roots, IFolderRepository folders)
    {
        _roots = roots;
        _folders = folders;
    }

    public async Task<IReadOnlyList<RootSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var roots = await _roots.GetAllAsync(cancellationToken);
        return roots.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Select(ToSummary).ToList();
    }

    public async Task<Result<RootSummary>> UpdateAsync(int id, RootUpdate update, CancellationToken cancellationToken = default)
    {
        var roots = await _roots.GetAllAsync(cancellationToken);
        var root = roots.FirstOrDefault(r => r.Id == id);
        if (root is null)
            return Result.NotFound();

        var name = root.Name;
        if (update.Name is not null)
        {
            name = update.Name.Trim();
            if (RootNameRules.Validate(name) is { } nameError)
                return Result.Invalid("name", nameError);
        }

        var alias = root.Alias;
        if (update.AliasSpecified)
        {
            alias = update.Alias?.Trim();
            if (alias is not null && RootNameRules.Validate(alias) is { } aliasError)
                return Result.Invalid("alias", aliasError);
        }

        var others = roots.Where(r => r.Id != id).ToList();
        if (others.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
            return Result.Conflict($"Another root is already named '{name}'.");

        var segment = alias ?? name;
        if (others.Any(r => string.Equals(RootNameRules.ExportSegment(r), segment, StringComparison.OrdinalIgnoreCase)))
            return Result.Conflict($"Another root already exports under '{segment}'.");

        var renamed = !string.Equals(root.Name, name, StringComparison.Ordinal);
        root.Name = name;
        root.Alias = alias;
        if (update.IsActive is bool isActive)
            root.IsActive = isActive;

        await _roots.UpdateAsync(root, cancellationToken);
        if (renamed)
            await _folders.RenameRootFolderAsync(root.Id, name, cancellationToken);

        return Result<RootSummary>.Ok(ToSummary(root));
    }

    private static RootSummary ToSummary(ImageRoot root) =>
        new(root.Id, root.Name, root.Alias, root.MountPath, root.IsActive, RootNameRules.ExportSegment(root));
}
```

Add to `IImageRootRepository`:

```csharp
    Task UpdateAsync(ImageRoot imageRoot, CancellationToken cancellationToken = default);
```

Add to `ImageRootRepository`:

```csharp
    public async Task UpdateAsync(ImageRoot imageRoot, CancellationToken cancellationToken = default)
    {
        _dbContext.ImageRoots.Update(imageRoot);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
```

In `AddApplication` replace `services.AddScoped<IDevImageRootSeeder, DevImageRootSeeder>();` with
(adding `using PictureManager.Application.Roots;`):

```csharp
        services.AddScoped<IImageRootSeeder, ImageRootSeeder>();
        services.AddScoped<IRootService, RootService>();
```

- [ ] **Step 7: Implement the Api side**

Add to `ResultHttpExtensions`:

```csharp
    public static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = new[] { message } });
```

`src/PictureManager.Api/Endpoints/RootEndpoints.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Roots;

namespace PictureManager.Api.Endpoints;

public static class RootEndpoints
{
    public static IEndpointRouteBuilder MapRootEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapGet("/roots", GetAllAsync);
        admin.MapPatch("/roots/{id:int}", UpdateAsync);
        return admin;
    }

    public static async Task<Ok<IReadOnlyList<RootSummary>>> GetAllAsync(IRootService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAllAsync(cancellationToken));

    public static async Task<Results<Ok<RootSummary>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> UpdateAsync(
        int id, JsonElement body, IRootService service, CancellationToken cancellationToken)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return ResultHttpExtensions.Invalid("body", "Expected a JSON object.");
        if (!PatchJson.TryReadString(body, "name", out var namePresent, out var name) || (namePresent && name is null))
            return ResultHttpExtensions.Invalid("name", "Must be a string.");
        if (!PatchJson.TryReadString(body, "alias", out var aliasPresent, out var alias))
            return ResultHttpExtensions.Invalid("alias", "Must be a string or null.");
        if (!PatchJson.TryReadBool(body, "isActive", out var activePresent, out var isActive) || (activePresent && isActive is null))
            return ResultHttpExtensions.Invalid("isActive", "Must be true or false.");

        var update = new RootUpdate(name, aliasPresent, alias, isActive);
        return (await service.UpdateAsync(id, update, cancellationToken)).ToOk();
    }
}
```

In `Program.cs`:
1. Replace the `DevImageRootOptions` block

```csharp
    var devImageRootOptions = new DevImageRootOptions();
    builder.Configuration.GetSection("DevImageRoot").Bind(devImageRootOptions);
    builder.Services.AddSingleton(devImageRootOptions);
```

with

```csharp
    var imageRootsOptions = new ImageRootsOptions
    {
        Entries = builder.Configuration.GetSection("ImageRoots").Get<List<ImageRootConfigEntry>>() ?? new List<ImageRootConfigEntry>()
    };
    builder.Services.AddSingleton(imageRootsOptions);
```

2. Replace `var seeder = scope.ServiceProvider.GetRequiredService<IDevImageRootSeeder>();` with
   `var seeder = scope.ServiceProvider.GetRequiredService<IImageRootSeeder>();`.
3. After `admin.MapScanEndpoints();` add `admin.MapRootEndpoints();`.
4. Add `using PictureManager.Application.Roots;`, and remove `using PictureManager.Application.Scanning;` if
   nothing else in `Program.cs` uses it.

In `src/PictureManager.Api/appsettings.Development.json` replace

```json
  "DevImageRoot": {
    "Name": "dev",
    "MountPath": "../../dev-data/images"
  },
```

with

```json
  "ImageRoots": [
    { "Name": "dev", "MountPath": "../../dev-data/images" }
  ],
```

(The existing dev database already has `dev` at `../../dev-data/images`, so the seeder matches it by
`MountPath` and leaves it alone.)

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~Roots"`
Expected: PASS, 23 tests (ImageRootSeederTests 12, RootServiceTests 11), 0 failed.

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~RootEndpointsTests"`
Expected: PASS, 7 tests.

Run: `dotnet test`
Expected: PASS, 0 failed across all projects.

- [ ] **Step 9: Commit**

```bash
git add -A src tests
git commit -m "feat: config-seeded image roots with Alias, admin root list/patch (replaces DevImageRootSeeder)"
```

---

## Task 9: Album repository queries (Postgres)

**Files:**
- Create: `src/PictureManager.Application/Albums/AlbumRows.cs`
- Modify: `src/PictureManager.Application/Repositories/IAlbumRepository.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/AlbumRepository.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AlbumQueryRepositoryTests.cs`

**Interfaces:**
- Consumes: `ImageRow` (Task 4); `TestData`/`PostgresTestDatabase` (Tasks 1–2).
- Produces (namespace `PictureManager.Application.Albums`):
  - `sealed record AlbumSummaryRow(int Id, string Name, string? Description, int ImageCount, int? CoverImageId, string? CoverContentHash, DateTime UpdatedAt)`
  - `sealed record AlbumImageRow(ImageRow Image, int SortOrder, bool IsMissing)`
  - `sealed record AlbumExportRow(string RootName, string? RootAlias, string RelativePath, string FileName, string Extension)`
- Produces, added to `IAlbumRepository` (the existing `GetByIdAsync`, `GetAllAsync` and `AddAsync` stay):
  - `Task<IReadOnlyList<AlbumSummaryRow>> GetSummariesAsync(int ownerUserId, CancellationToken cancellationToken = default)`
  - `Task<Album?> GetOwnedAsync(int id, int ownerUserId, CancellationToken cancellationToken = default)` (tracked)
  - `Task<int> CountImagesAsync(int albumId, CancellationToken cancellationToken = default)`
  - `Task<bool> NameExistsAsync(int ownerUserId, string name, int? excludeAlbumId, CancellationToken cancellationToken = default)`
  - `Task UpdateAsync(Album album, CancellationToken cancellationToken = default)`
  - `Task DeleteAsync(Album album, CancellationToken cancellationToken = default)`
  - `Task<IReadOnlyList<AlbumImageRow>> ListImagesAsync(int albumId, int? afterSortOrder, int? afterImageId, int take, CancellationToken cancellationToken = default)`
  - `Task<IReadOnlyList<int>> GetOrderedImageIdsAsync(int albumId, CancellationToken cancellationToken = default)`
  - `Task AppendImagesAsync(int albumId, IReadOnlyList<int> imageIds, DateTime addedAtUtc, CancellationToken cancellationToken = default)`
  - `Task RemoveImagesAsync(int albumId, IReadOnlyCollection<int> imageIds, CancellationToken cancellationToken = default)`
  - `Task ReorderAsync(int albumId, IReadOnlyList<int> orderedImageIds, CancellationToken cancellationToken = default)`
  - `Task TouchAsync(int albumId, DateTime updatedAtUtc, CancellationToken cancellationToken = default)`
  - `Task<IReadOnlyList<AlbumExportRow>> GetExportRowsAsync(int albumId, CancellationToken cancellationToken = default)`

- [ ] **Step 1: Write the failing tests**

Create `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AlbumQueryRepositoryTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Albums;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class AlbumQueryRepositoryTests
{
    [Fact]
    public async Task GetSummariesAsync_OwnerScoped_OrderedByName_WithCountAndCoverFromFirstHashedImage()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var other = new AppUser { DisplayName = "Other", Role = UserRole.User };
        db.Context.AppUsers.Add(other);
        await db.Context.SaveChangesAsync();

        var folder = TestData.Folder(TestData.Root("r"), "");
        var unhashed = TestData.Image(folder, "pending", contentHash: "");
        var cover = TestData.Image(folder, "cover", contentHash: "COVERHASH");
        var zoo = TestData.Album("zoo");
        var beach = TestData.Album("Beach");
        db.Context.AlbumImages.AddRange(TestData.AlbumImage(zoo, unhashed, 0), TestData.AlbumImage(zoo, cover, 1));
        db.Context.Albums.AddRange(beach, TestData.Album("foreign", other.Id));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new AlbumRepository(context).GetSummariesAsync(AppUser.SystemUserId);

        rows.Select(r => r.Name).Should().Equal("Beach", "zoo");
        rows[0].ImageCount.Should().Be(0);
        rows[0].CoverImageId.Should().BeNull();
        rows[1].ImageCount.Should().Be(2);
        rows[1].CoverImageId.Should().Be(cover.Id);
        rows[1].CoverContentHash.Should().Be("COVERHASH");
    }

    [Fact]
    public async Task GetOwnedAsync_OtherOwnersAlbum_ReturnsNull()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var other = new AppUser { DisplayName = "Other", Role = UserRole.User };
        db.Context.AppUsers.Add(other);
        await db.Context.SaveChangesAsync();
        var mine = TestData.Album("mine");
        var theirs = TestData.Album("theirs", other.Id);
        db.Context.Albums.AddRange(mine, theirs);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new AlbumRepository(context);

        (await repository.GetOwnedAsync(mine.Id, AppUser.SystemUserId)).Should().NotBeNull();
        (await repository.GetOwnedAsync(theirs.Id, AppUser.SystemUserId)).Should().BeNull();
    }

    [Fact]
    public async Task NameExistsAsync_IsCaseInsensitive_AndCanExcludeAnAlbum()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var album = TestData.Album("Holidays");
        db.Context.Albums.Add(album);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new AlbumRepository(context);

        (await repository.NameExistsAsync(AppUser.SystemUserId, "holidays", null)).Should().BeTrue();
        (await repository.NameExistsAsync(AppUser.SystemUserId, "HOLIDAYS", album.Id)).Should().BeFalse();
        (await repository.NameExistsAsync(AppUser.SystemUserId, "Other", null)).Should().BeFalse();
    }

    [Fact]
    public async Task ListImagesAsync_OrdersBySortOrder_ContinuesAfterKeyset_AndFlagsMissing()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var folder = TestData.Folder(root, "");
        var a = TestData.Image(folder, "a");
        var b = TestData.Image(folder, "b", missingSinceUtc: TestData.Utc);
        var c = TestData.Image(folder, "c");
        var album = TestData.Album("A");
        db.Context.AlbumImages.AddRange(TestData.AlbumImage(album, c, 0), TestData.AlbumImage(album, a, 1), TestData.AlbumImage(album, b, 2));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new AlbumRepository(context);
        var first = await repository.ListImagesAsync(album.Id, null, null, 2);
        var second = await repository.ListImagesAsync(album.Id, first[^1].SortOrder, first[^1].Image.Id, 2);

        first.Select(r => r.Image.Id).Should().Equal(c.Id, a.Id);
        first.Should().OnlyContain(r => !r.IsMissing);
        second.Should().ContainSingle().Which.Should().Match<AlbumImageRow>(r => r.Image.Id == b.Id && r.IsMissing);
    }

    [Fact]
    public async Task ListImagesAsync_ImageOnInactiveRoot_IsFlaggedMissing()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("offline", isActive: false), "");
        var image = TestData.Image(folder, "a");
        var album = TestData.Album("A");
        db.Context.AlbumImages.Add(TestData.AlbumImage(album, image, 0));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new AlbumRepository(context).ListImagesAsync(album.Id, null, null, 10);

        rows.Should().ContainSingle().Which.IsMissing.Should().BeTrue();
    }

    [Fact]
    public async Task AppendImagesAsync_AppendsAfterCurrentMax_InGivenOrder()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var existing = TestData.Image(folder, "existing");
        var x = TestData.Image(folder, "x");
        var y = TestData.Image(folder, "y");
        var album = TestData.Album("A");
        db.Context.AlbumImages.Add(TestData.AlbumImage(album, existing, 5));
        db.Context.Images.AddRange(x, y);
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new AlbumRepository(context).AppendImagesAsync(album.Id, new[] { y.Id, x.Id }, TestData.Utc);

        await using var read = db.CreateContext();
        (await new AlbumRepository(read).GetOrderedImageIdsAsync(album.Id)).Should().Equal(existing.Id, y.Id, x.Id);
        (await read.AlbumImages.Where(ai => ai.AlbumId == album.Id).OrderBy(ai => ai.SortOrder).Select(ai => ai.SortOrder).ToListAsync())
            .Should().Equal(5, 6, 7);
    }

    [Fact]
    public async Task RemoveImagesAsync_RemovesOnlyTheGivenIds()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var a = TestData.Image(folder, "a");
        var b = TestData.Image(folder, "b");
        var album = TestData.Album("A");
        db.Context.AlbumImages.AddRange(TestData.AlbumImage(album, a, 0), TestData.AlbumImage(album, b, 1));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new AlbumRepository(context).RemoveImagesAsync(album.Id, new[] { a.Id, 999_999 });

        await using var read = db.CreateContext();
        (await new AlbumRepository(read).GetOrderedImageIdsAsync(album.Id)).Should().Equal(b.Id);
        (await read.Images.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ReorderAsync_RenumbersDenselyInTheGivenOrder()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var a = TestData.Image(folder, "a");
        var b = TestData.Image(folder, "b");
        var c = TestData.Image(folder, "c");
        var album = TestData.Album("A");
        db.Context.AlbumImages.AddRange(TestData.AlbumImage(album, a, 3), TestData.AlbumImage(album, b, 10), TestData.AlbumImage(album, c, 42));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new AlbumRepository(context).ReorderAsync(album.Id, new[] { c.Id, a.Id, b.Id });

        await using var read = db.CreateContext();
        var rows = await read.AlbumImages.Where(ai => ai.AlbumId == album.Id).OrderBy(ai => ai.SortOrder)
            .Select(ai => new { ai.ImageId, ai.SortOrder }).ToListAsync();
        rows.Select(r => r.ImageId).Should().Equal(c.Id, a.Id, b.Id);
        rows.Select(r => r.SortOrder).Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task GetExportRowsAsync_InAlbumOrder_WithRootAliasAndFolderPath()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var photos = TestData.Root("nas-photos", alias: "family_photos");
        var work = TestData.Root("work-nas");
        var photosTop = TestData.Folder(photos, "");
        var madeira = TestData.Folder(photos, "Holidays/Madeira", photosTop);
        var workFolder = TestData.Folder(work, "2025/Q3");
        var first = TestData.Image(madeira, "IMG_4471", ".jpg");
        var second = TestData.Image(photosTop, "IMG_0001", ".jpg");
        var third = TestData.Image(workFolder, "whiteboard", ".png");
        var album = TestData.Album("Mixed");
        db.Context.AlbumImages.AddRange(
            TestData.AlbumImage(album, third, 2), TestData.AlbumImage(album, first, 0), TestData.AlbumImage(album, second, 1));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new AlbumRepository(context).GetExportRowsAsync(album.Id);

        rows.Should().Equal(
            new AlbumExportRow("nas-photos", "family_photos", "Holidays/Madeira", "IMG_4471", ".jpg"),
            new AlbumExportRow("nas-photos", "family_photos", "", "IMG_0001", ".jpg"),
            new AlbumExportRow("work-nas", null, "2025/Q3", "whiteboard", ".png"));
    }

    [Fact]
    public async Task DeleteAsync_CascadesAlbumEntries_ButKeepsImages()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var image = TestData.Image(folder, "a");
        var album = TestData.Album("A");
        db.Context.AlbumImages.Add(TestData.AlbumImage(album, image, 0));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
        {
            var repository = new AlbumRepository(context);
            await repository.DeleteAsync((await repository.GetOwnedAsync(album.Id, AppUser.SystemUserId))!);
        }

        await using var read = db.CreateContext();
        (await read.Albums.AnyAsync()).Should().BeFalse();
        (await read.AlbumImages.AnyAsync()).Should().BeFalse();
        (await read.Images.AnyAsync(i => i.Id == image.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task TouchAsync_SetsUpdatedAt_AndCountImagesAsyncCountsEntries()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var album = TestData.Album("A");
        db.Context.AlbumImages.AddRange(
            TestData.AlbumImage(album, TestData.Image(folder, "a"), 0),
            TestData.AlbumImage(album, TestData.Image(folder, "b", missingSinceUtc: TestData.Utc), 1));
        await db.Context.SaveChangesAsync();
        var touchedAt = new DateTime(2026, 5, 5, 5, 5, 5, DateTimeKind.Utc);

        await using var context = db.CreateContext();
        var repository = new AlbumRepository(context);
        await repository.TouchAsync(album.Id, touchedAt);

        (await repository.CountImagesAsync(album.Id)).Should().Be(2);
        await using var read = db.CreateContext();
        (await read.Albums.SingleAsync(a => a.Id == album.Id)).UpdatedAt.Should().Be(touchedAt);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~AlbumQueryRepositoryTests"`
Expected: build FAILS (`AlbumImageRow`, `GetSummariesAsync` … not found).

- [ ] **Step 3: Add the row types and the interface methods**

`src/PictureManager.Application/Albums/AlbumRows.cs`:

```csharp
using System;
using PictureManager.Application.Images;

namespace PictureManager.Application.Albums;

public sealed record AlbumSummaryRow(
    int Id,
    string Name,
    string? Description,
    int ImageCount,
    int? CoverImageId,
    string? CoverContentHash,
    DateTime UpdatedAt);

/// <summary>IsMissing = the image is not visible (missing on disk, or its folder/root is inactive).</summary>
public sealed record AlbumImageRow(ImageRow Image, int SortOrder, bool IsMissing);

public sealed record AlbumExportRow(string RootName, string? RootAlias, string RelativePath, string FileName, string Extension);
```

Add to `IAlbumRepository` (plus `using System;` and `using PictureManager.Application.Albums;`):

```csharp
    /// <summary>The owner's albums ordered by lower(Name); cover = first entry by SortOrder with a content hash.</summary>
    Task<IReadOnlyList<AlbumSummaryRow>> GetSummariesAsync(int ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Tracked album if it exists AND belongs to the owner; otherwise null.</summary>
    Task<Album?> GetOwnedAsync(int id, int ownerUserId, CancellationToken cancellationToken = default);

    Task<int> CountImagesAsync(int albumId, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(int ownerUserId, string name, int? excludeAlbumId, CancellationToken cancellationToken = default);

    Task UpdateAsync(Album album, CancellationToken cancellationToken = default);

    Task DeleteAsync(Album album, CancellationToken cancellationToken = default);

    /// <summary>Album entries in (SortOrder, ImageId) order, after the given keyset when provided.</summary>
    Task<IReadOnlyList<AlbumImageRow>> ListImagesAsync(int albumId, int? afterSortOrder, int? afterImageId, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> GetOrderedImageIdsAsync(int albumId, CancellationToken cancellationToken = default);

    /// <summary>Adds entries after the current maximum SortOrder, in the given order. Ids must not already be in the album.</summary>
    Task AppendImagesAsync(int albumId, IReadOnlyList<int> imageIds, DateTime addedAtUtc, CancellationToken cancellationToken = default);

    Task RemoveImagesAsync(int albumId, IReadOnlyCollection<int> imageIds, CancellationToken cancellationToken = default);

    /// <summary>One statement: SortOrder = position (0..n-1) of each id in orderedImageIds.</summary>
    Task ReorderAsync(int albumId, IReadOnlyList<int> orderedImageIds, CancellationToken cancellationToken = default);

    Task TouchAsync(int albumId, DateTime updatedAtUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlbumExportRow>> GetExportRowsAsync(int albumId, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement them in `AlbumRepository`**

Add `using System;`, `using PictureManager.Application.Albums;` and `using PictureManager.Application.Images;`,
then:

```csharp
    public async Task<IReadOnlyList<AlbumSummaryRow>> GetSummariesAsync(int ownerUserId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Albums.AsNoTracking()
            .Where(a => a.OwnerUserId == ownerUserId)
            .OrderBy(a => a.Name.ToLower()).ThenBy(a => a.Id)
            .Select(a => new AlbumSummaryRow(
                a.Id,
                a.Name,
                a.Description,
                a.AlbumImages.Count(),
                a.AlbumImages.Where(ai => ai.Image!.ContentHash != "")
                    .OrderBy(ai => ai.SortOrder).ThenBy(ai => ai.ImageId)
                    .Select(ai => (int?)ai.ImageId).FirstOrDefault(),
                a.AlbumImages.Where(ai => ai.Image!.ContentHash != "")
                    .OrderBy(ai => ai.SortOrder).ThenBy(ai => ai.ImageId)
                    .Select(ai => ai.Image!.ContentHash).FirstOrDefault(),
                a.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<Album?> GetOwnedAsync(int id, int ownerUserId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Albums.FirstOrDefaultAsync(a => a.Id == id && a.OwnerUserId == ownerUserId, cancellationToken);
    }

    public async Task<int> CountImagesAsync(int albumId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AlbumImages.CountAsync(ai => ai.AlbumId == albumId, cancellationToken);
    }

    public async Task<bool> NameExistsAsync(int ownerUserId, string name, int? excludeAlbumId, CancellationToken cancellationToken = default)
    {
        var lowered = name.ToLower();
        return await _dbContext.Albums.AnyAsync(
            a => a.OwnerUserId == ownerUserId && a.Name.ToLower() == lowered && (excludeAlbumId == null || a.Id != excludeAlbumId),
            cancellationToken);
    }

    public async Task UpdateAsync(Album album, CancellationToken cancellationToken = default)
    {
        _dbContext.Albums.Update(album);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Album album, CancellationToken cancellationToken = default)
    {
        _dbContext.Albums.Remove(album);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AlbumImageRow>> ListImagesAsync(
        int albumId, int? afterSortOrder, int? afterImageId, int take, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.AlbumImages.AsNoTracking().Where(ai => ai.AlbumId == albumId);
        if (afterSortOrder is int sortOrder && afterImageId is int imageId)
            query = query.Where(ai => ai.SortOrder > sortOrder || (ai.SortOrder == sortOrder && ai.ImageId > imageId));

        return await query
            .OrderBy(ai => ai.SortOrder).ThenBy(ai => ai.ImageId)
            .Take(take)
            .Select(ai => new AlbumImageRow(
                new ImageRow(ai.Image!.Id, ai.Image.FolderId, ai.Image.FileName, ai.Image.Extension, ai.Image.Width,
                    ai.Image.Height, ai.Image.DateTaken, ai.Image.IsFavorite, ai.Image.ContentHash, ai.Image.SortDate,
                    ai.Image.FileName.ToLower()),
                ai.SortOrder,
                ai.Image.MissingSinceUtc != null || !ai.Image.Folder!.IsActive || !ai.Image.Folder.Root!.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<int>> GetOrderedImageIdsAsync(int albumId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AlbumImages.AsNoTracking()
            .Where(ai => ai.AlbumId == albumId)
            .OrderBy(ai => ai.SortOrder).ThenBy(ai => ai.ImageId)
            .Select(ai => ai.ImageId)
            .ToListAsync(cancellationToken);
    }

    public async Task AppendImagesAsync(int albumId, IReadOnlyList<int> imageIds, DateTime addedAtUtc, CancellationToken cancellationToken = default)
    {
        var maxSortOrder = await _dbContext.AlbumImages
            .Where(ai => ai.AlbumId == albumId)
            .MaxAsync(ai => (int?)ai.SortOrder, cancellationToken) ?? -1;

        for (var i = 0; i < imageIds.Count; i++)
        {
            _dbContext.AlbumImages.Add(new AlbumImage
            {
                AlbumId = albumId,
                ImageId = imageIds[i],
                SortOrder = maxSortOrder + 1 + i,
                AddedAt = addedAtUtc
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveImagesAsync(int albumId, IReadOnlyCollection<int> imageIds, CancellationToken cancellationToken = default)
    {
        var ids = imageIds.ToList();
        await _dbContext.AlbumImages
            .Where(ai => ai.AlbumId == albumId && ids.Contains(ai.ImageId))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task ReorderAsync(int albumId, IReadOnlyList<int> orderedImageIds, CancellationToken cancellationToken = default)
    {
        var ids = orderedImageIds.ToArray();
        await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "AlbumImages" AS ai
            SET "SortOrder" = o.ord - 1
            FROM unnest({ids}) WITH ORDINALITY AS o(image_id, ord)
            WHERE ai."AlbumId" = {albumId} AND ai."ImageId" = o.image_id
            """, cancellationToken);
    }

    public async Task TouchAsync(int albumId, DateTime updatedAtUtc, CancellationToken cancellationToken = default)
    {
        await _dbContext.Albums.Where(a => a.Id == albumId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UpdatedAt, updatedAtUtc), cancellationToken);
    }

    public async Task<IReadOnlyList<AlbumExportRow>> GetExportRowsAsync(int albumId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AlbumImages.AsNoTracking()
            .Where(ai => ai.AlbumId == albumId)
            .OrderBy(ai => ai.SortOrder).ThenBy(ai => ai.ImageId)
            .Select(ai => new AlbumExportRow(
                ai.Image!.Folder!.Root!.Name,
                ai.Image.Folder.Root.Alias,
                ai.Image.Folder.RelativePath,
                ai.Image.FileName,
                ai.Image.Extension))
            .ToListAsync(cancellationToken);
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~AlbumQueryRepositoryTests"`
Expected: PASS, 11 tests, 0 failed.

Run: `dotnet test tests/PictureManager.Infrastructure.Tests`
Expected: PASS, 0 failed (the existing InMemory `AlbumRepositoryTests` still pass).

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat: add album repository queries for summaries, contents, append, reorder and export rows"
```

---

## Task 10: `AlbumService` and the export formatter

**Files:**
- Create: `src/PictureManager.Application/Albums/AlbumModels.cs`
- Create: `src/PictureManager.Application/Albums/AlbumExportFormatter.cs`
- Create: `src/PictureManager.Application/Albums/IAlbumService.cs`
- Create: `src/PictureManager.Application/Albums/AlbumService.cs`
- Modify: `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Application.Tests/Albums/AlbumExportFormatterTests.cs`
- Test: `tests/PictureManager.Application.Tests/Albums/AlbumServiceTests.cs`

**Interfaces:**
- Consumes:
  - `IAlbumRepository` additions and the row types (Task 9);
  - `IImageQueryRepository.GetVisibleIdsAsync`/`GetVisibleIdsInFolderAsync` (Task 4);
  - `IFolderRepository.IsVisibleAsync` (Task 5);
  - `Result`, `PagedResult`, `CursorCodec`, `ICurrentUser`, `ImageUrls` (Task 3).
- Produces (namespace `PictureManager.Application.Albums`):
  - `sealed record AlbumSummary(int Id, string Name, string? Description, int ImageCount, string? CoverThumbnailUrl, DateTime UpdatedAt)`
  - `sealed record AlbumDetail(int Id, string Name, string? Description, int ImageCount, DateTime CreatedAt, DateTime UpdatedAt)`
  - `sealed record AlbumImageItem(int Id, int FolderId, string FileName, string Extension, int? Width, int? Height, DateTime? DateTaken, bool IsFavorite, string? ThumbnailUrl, string? PreviewUrl, bool IsMissing)`,
    with `static From(AlbumImageRow)`
  - `sealed record AlbumCreate(string? Name, string? Description)`
  - `sealed record AlbumUpdate(string? Name, bool DescriptionSpecified, string? Description)`
  - `sealed record AlbumAddImages(IReadOnlyList<int>? ImageIds, int? FolderId)`
  - `sealed record AlbumAddResult(int Added, int Skipped)`
  - `sealed record AlbumExport(string FileName, string Content)`
  - `sealed record AlbumImageCursor(int SortOrder, int ImageId)`, with JSON names `s`, `i`
  - `static class AlbumExportFormatter`, with
    `string FormatLine(string? prefix, AlbumExportRow row)`,
    `string Format(string? prefix, IEnumerable<AlbumExportRow> rows)` and
    `string FileName(string albumName)`
  - `interface IAlbumService`, implemented by
    `AlbumService(IAlbumRepository, IImageQueryRepository, IFolderRepository, ICurrentUser, IClock)` (scoped):
    - `Task<IReadOnlyList<AlbumSummary>> GetAllAsync(CancellationToken cancellationToken = default)`
    - `Task<Result<AlbumDetail>> CreateAsync(AlbumCreate input, CancellationToken cancellationToken = default)`
    - `Task<Result<AlbumDetail>> GetAsync(int id, CancellationToken cancellationToken = default)`
    - `Task<Result<AlbumDetail>> UpdateAsync(int id, AlbumUpdate input, CancellationToken cancellationToken = default)`
    - `Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)`
    - `Task<Result<PagedResult<AlbumImageItem>>> ListImagesAsync(int id, string? cursor, int? limit, CancellationToken cancellationToken = default)`
    - `Task<Result<AlbumAddResult>> AddImagesAsync(int id, AlbumAddImages input, CancellationToken cancellationToken = default)`
    - `Task<Result> RemoveImagesAsync(int id, IReadOnlyList<int>? imageIds, CancellationToken cancellationToken = default)`
    - `Task<Result> MoveImageAsync(int id, int imageId, int? afterImageId, CancellationToken cancellationToken = default)`
    - `Task<Result<AlbumExport>> ExportAsync(int id, string? prefix, CancellationToken cancellationToken = default)`

- [ ] **Step 1: Write the failing formatter tests**

Create `tests/PictureManager.Application.Tests/Albums/AlbumExportFormatterTests.cs`:

```csharp
using FluentAssertions;
using PictureManager.Application.Albums;
using Xunit;

namespace PictureManager.Application.Tests.Albums;

public class AlbumExportFormatterTests
{
    private static readonly AlbumExportRow Madeira = new("nas-photos", "family_photos", "Holidays/Madeira", "IMG_4471", ".jpg");
    private static readonly AlbumExportRow TopLevel = new("nas-photos", "family_photos", "", "IMG_0001", ".jpg");
    private static readonly AlbumExportRow NoAlias = new("work-nas", null, "2025/Q3", "whiteboard", ".png");

    [Theory]
    [InlineData(null, "/family_photos/IMG_0001.jpg")]
    [InlineData("", "/family_photos/IMG_0001.jpg")]
    [InlineData("/mnt", "/mnt/family_photos/IMG_0001.jpg")]
    [InlineData("/mnt/", "/mnt/family_photos/IMG_0001.jpg")]
    [InlineData("/mnt//", "/mnt/family_photos/IMG_0001.jpg")]
    [InlineData(@"\\nas\share", @"\\nas\share/family_photos/IMG_0001.jpg")]
    public void FormatLine_TopLevelImage_DropsEmptyRelativePath(string? prefix, string expected)
    {
        AlbumExportFormatter.FormatLine(prefix, TopLevel).Should().Be(expected);
    }

    [Fact]
    public void FormatLine_NestedImage_IncludesRelativePath()
    {
        AlbumExportFormatter.FormatLine("/mnt", Madeira).Should().Be("/mnt/family_photos/Holidays/Madeira/IMG_4471.jpg");
    }

    [Fact]
    public void FormatLine_RootWithoutAlias_UsesRootName()
    {
        AlbumExportFormatter.FormatLine(null, NoAlias).Should().Be("/work-nas/2025/Q3/whiteboard.png");
    }

    [Fact]
    public void Format_EndsEveryLineWithNewline_InRowOrder()
    {
        AlbumExportFormatter.Format(null, new[] { Madeira, NoAlias })
            .Should().Be("/family_photos/Holidays/Madeira/IMG_4471.jpg\n/work-nas/2025/Q3/whiteboard.png\n");
    }

    [Fact]
    public void Format_NoRows_IsEmpty()
    {
        AlbumExportFormatter.Format("/mnt", System.Array.Empty<AlbumExportRow>()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Nyaralás 2025", "Nyaralás 2025.txt")]
    [InlineData("a/b:c*?\"<>|d", "a_b_c______d.txt")]
    [InlineData("   ", "album.txt")]
    public void FileName_ReplacesCharactersInvalidInFileNames(string albumName, string expected)
    {
        AlbumExportFormatter.FileName(albumName).Should().Be(expected);
    }
}
```

- [ ] **Step 2: Write the failing service tests**

Create `tests/PictureManager.Application.Tests/Albums/AlbumServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Albums;
using PictureManager.Application.Common;
using PictureManager.Application.Images;
using PictureManager.Application.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Albums;

public class AlbumServiceTests
{
    private const int Owner = 1;
    private readonly IAlbumRepository _albums = Substitute.For<IAlbumRepository>();
    private readonly IImageQueryRepository _images = Substitute.For<IImageQueryRepository>();
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly Album _album = new() { Id = 7, Name = "Holidays", OwnerUserId = Owner };

    public AlbumServiceTests()
    {
        _currentUser.UserId.Returns(Owner);
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _albums.GetOwnedAsync(7, Owner, Arg.Any<CancellationToken>()).Returns(_album);
        _albums.GetOrderedImageIdsAsync(7, Arg.Any<CancellationToken>()).Returns(new List<int>());
        _albums.AddAsync(Arg.Any<Album>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var album = call.Arg<Album>();
            album.Id = 42;
            return album;
        });
    }

    private AlbumService CreateService() => new(_albums, _images, _folders, _currentUser, _clock);

    private static ImageRow Row(int id, string hash = "H") =>
        new(id, 3, $"img{id}", ".jpg", null, null, null, false, hash, DateTime.UtcNow, $"img{id}");

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task CreateAsync_BlankName_ReturnsInvalid(string? name)
    {
        var result = await CreateService().CreateAsync(new AlbumCreate(name, null));

        result.Errors!.Keys.Should().Contain("name");
    }

    [Fact]
    public async Task CreateAsync_NameTooLong_ReturnsInvalid()
    {
        (await CreateService().CreateAsync(new AlbumCreate(new string('x', 201), null))).Errors!.Keys.Should().Contain("name");
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ReturnsConflict()
    {
        _albums.NameExistsAsync(Owner, "Holidays", null, Arg.Any<CancellationToken>()).Returns(true);

        (await CreateService().CreateAsync(new AlbumCreate("Holidays", null))).Status.Should().Be(ResultStatus.Conflict);
    }

    [Fact]
    public async Task CreateAsync_Valid_CreatesTrimmedAlbumOwnedByCurrentUser()
    {
        var result = await CreateService().CreateAsync(new AlbumCreate("  Summer  ", "  sun  "));

        result.Value.Should().Be(new AlbumDetail(42, "Summer", "sun", 0, _clock.UtcNow, _clock.UtcNow));
        await _albums.Received(1).AddAsync(Arg.Is<Album>(a => a.Name == "Summer" && a.OwnerUserId == Owner), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_NotOwned_ReturnsNotFound()
    {
        _albums.GetOwnedAsync(8, Owner, Arg.Any<CancellationToken>()).Returns((Album?)null);

        (await CreateService().GetAsync(8)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task UpdateAsync_Rename_ChecksConflictExcludingItself()
    {
        _albums.NameExistsAsync(Owner, "Trips", 7, Arg.Any<CancellationToken>()).Returns(true);

        (await CreateService().UpdateAsync(7, new AlbumUpdate("Trips", false, null))).Status.Should().Be(ResultStatus.Conflict);
    }

    [Fact]
    public async Task UpdateAsync_DescriptionExplicitlyNull_ClearsIt_AndKeepsName()
    {
        _album.Description = "old";

        var result = await CreateService().UpdateAsync(7, new AlbumUpdate(null, true, null));

        result.Value!.Description.Should().BeNull();
        result.Value.Name.Should().Be("Holidays");
        await _albums.Received(1).UpdateAsync(Arg.Is<Album>(a => a.Description == null && a.UpdatedAt == _clock.UtcNow), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_CoverUrlFromCoverRow_NullWhenNoHashedImage()
    {
        _albums.GetSummariesAsync(Owner, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new AlbumSummaryRow(1, "A", null, 3, 11, "HASH", _clock.UtcNow),
            new AlbumSummaryRow(2, "B", null, 0, null, null, _clock.UtcNow)
        });

        var all = await CreateService().GetAllAsync();

        all[0].CoverThumbnailUrl.Should().Be("/api/images/11/thumbnail?v=HASH");
        all[1].CoverThumbnailUrl.Should().BeNull();
    }

    [Fact]
    public async Task ListImagesAsync_MissingImage_HasNullUrls_AndPagingProducesCursor()
    {
        _albums.ListImagesAsync(7, null, null, 3, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new AlbumImageRow(Row(1), 0, false),
            new AlbumImageRow(Row(2), 1, true),
            new AlbumImageRow(Row(3), 2, false)
        });

        var page = (await CreateService().ListImagesAsync(7, null, 2)).Value!;

        page.Items.Select(i => i.Id).Should().Equal(1, 2);
        page.Items[1].IsMissing.Should().BeTrue();
        page.Items[1].ThumbnailUrl.Should().BeNull();
        page.Items[0].ThumbnailUrl.Should().Be("/api/images/1/thumbnail?v=H");
        CursorCodec.TryDecode<AlbumImageCursor>(page.NextCursor, out var cursor).Should().BeTrue();
        cursor.Should().Be(new AlbumImageCursor(1, 2));
    }

    [Fact]
    public async Task ListImagesAsync_BadCursor_ReturnsInvalid()
    {
        (await CreateService().ListImagesAsync(7, "garbage!!", null)).Errors!.Keys.Should().Contain("cursor");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task AddImagesAsync_BothOrNeitherSource_ReturnsInvalid(bool withIds, bool withFolder)
    {
        var input = new AlbumAddImages(withIds ? new[] { 1 } : null, withFolder ? 3 : null);

        (await CreateService().AddImagesAsync(7, input)).Status.Should().Be(ResultStatus.Invalid);
    }

    [Fact]
    public async Task AddImagesAsync_UnknownIds_ReturnsInvalid_AndAddsNothing()
    {
        _images.GetVisibleIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns(new[] { 1 });

        var result = await CreateService().AddImagesAsync(7, new AlbumAddImages(new[] { 1, 99 }, null));

        result.Errors!["imageIds"].Single().Should().Contain("99");
        await _albums.DidNotReceive().AppendImagesAsync(Arg.Any<int>(), Arg.Any<IReadOnlyList<int>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddImagesAsync_SkipsImagesAlreadyInAlbum_AppendsRestInGivenOrder()
    {
        _images.GetVisibleIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns(new[] { 3, 1, 2 });
        _albums.GetOrderedImageIdsAsync(7, Arg.Any<CancellationToken>()).Returns(new List<int> { 2 });

        var result = await CreateService().AddImagesAsync(7, new AlbumAddImages(new[] { 3, 2, 1 }, null));

        result.Value.Should().Be(new AlbumAddResult(2, 1));
        await _albums.Received(1).AppendImagesAsync(7, Arg.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 3, 1 })),
            _clock.UtcNow, Arg.Any<CancellationToken>());
        await _albums.Received(1).TouchAsync(7, _clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddImagesAsync_DuplicateIdsInRequest_AddsEachImageOnce()
    {
        _images.GetVisibleIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns(new[] { 5 });

        var result = await CreateService().AddImagesAsync(7, new AlbumAddImages(new[] { 5, 5 }, null));

        result.Value.Should().Be(new AlbumAddResult(1, 0));
        await _albums.Received(1).AppendImagesAsync(7, Arg.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 5 })),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddImagesAsync_FolderId_AddsTheFoldersVisibleImagesInRepositoryOrder()
    {
        _folders.IsVisibleAsync(3, Arg.Any<CancellationToken>()).Returns(true);
        _images.GetVisibleIdsInFolderAsync(3, Arg.Any<CancellationToken>()).Returns(new[] { 20, 10 });

        var result = await CreateService().AddImagesAsync(7, new AlbumAddImages(null, 3));

        result.Value.Should().Be(new AlbumAddResult(2, 0));
        await _albums.Received(1).AppendImagesAsync(7, Arg.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 20, 10 })),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddImagesAsync_UnknownFolder_ReturnsNotFound()
    {
        _folders.IsVisibleAsync(3, Arg.Any<CancellationToken>()).Returns(false);

        (await CreateService().AddImagesAsync(7, new AlbumAddImages(null, 3))).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task RemoveImagesAsync_NoIds_ReturnsInvalid()
    {
        (await CreateService().RemoveImagesAsync(7, Array.Empty<int>())).Status.Should().Be(ResultStatus.Invalid);
    }

    [Fact]
    public async Task RemoveImagesAsync_RemovesAndTouches()
    {
        (await CreateService().RemoveImagesAsync(7, new[] { 4, 4, 5 })).IsSuccess.Should().BeTrue();

        await _albums.Received(1).RemoveImagesAsync(7, Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 4, 5 })),
            Arg.Any<CancellationToken>());
        await _albums.Received(1).TouchAsync(7, _clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(104, 101, new[] { 101, 104, 102, 103 })]
    [InlineData(101, 103, new[] { 102, 103, 101, 104 })]
    [InlineData(103, null, new[] { 103, 101, 102, 104 })]
    public async Task MoveImageAsync_ReordersFullAlbum(int imageId, int? afterImageId, int[] expected)
    {
        _albums.GetOrderedImageIdsAsync(7, Arg.Any<CancellationToken>()).Returns(new List<int> { 101, 102, 103, 104 });

        (await CreateService().MoveImageAsync(7, imageId, afterImageId)).IsSuccess.Should().BeTrue();

        await _albums.Received(1).ReorderAsync(7, Arg.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(expected)), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(101, 101, "afterImageId")]
    [InlineData(999, null, "imageId")]
    [InlineData(101, 999, "afterImageId")]
    public async Task MoveImageAsync_InvalidMove_ReturnsInvalid_WithoutReordering(int imageId, int? afterImageId, string field)
    {
        _albums.GetOrderedImageIdsAsync(7, Arg.Any<CancellationToken>()).Returns(new List<int> { 101, 102 });

        var result = await CreateService().MoveImageAsync(7, imageId, afterImageId);

        result.Errors!.Keys.Should().Contain(field);
        await _albums.DidNotReceive().ReorderAsync(Arg.Any<int>(), Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportAsync_FormatsLines_AndUsesAlbumNameForFile()
    {
        _albums.GetExportRowsAsync(7, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new AlbumExportRow("nas-photos", "family_photos", "", "IMG_0001", ".jpg")
        });

        var export = (await CreateService().ExportAsync(7, "/mnt")).Value!;

        export.FileName.Should().Be("Holidays.txt");
        export.Content.Should().Be("/mnt/family_photos/IMG_0001.jpg\n");
    }

    [Fact]
    public async Task DeleteAsync_NotOwned_ReturnsNotFound()
    {
        _albums.GetOwnedAsync(8, Owner, Arg.Any<CancellationToken>()).Returns((Album?)null);

        (await CreateService().DeleteAsync(8)).Status.Should().Be(ResultStatus.NotFound);
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~Albums"`
Expected: build FAILS (`AlbumService`, `AlbumExportFormatter`, … not found).

- [ ] **Step 4: Implement**

`src/PictureManager.Application/Albums/AlbumModels.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using PictureManager.Application.Common;

namespace PictureManager.Application.Albums;

public sealed record AlbumSummary(int Id, string Name, string? Description, int ImageCount, string? CoverThumbnailUrl, DateTime UpdatedAt);

public sealed record AlbumDetail(int Id, string Name, string? Description, int ImageCount, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>The ImageListItem shape plus IsMissing. Missing entries stay listed with null URLs.</summary>
public sealed record AlbumImageItem(
    int Id,
    int FolderId,
    string FileName,
    string Extension,
    int? Width,
    int? Height,
    DateTime? DateTaken,
    bool IsFavorite,
    string? ThumbnailUrl,
    string? PreviewUrl,
    bool IsMissing)
{
    public static AlbumImageItem From(AlbumImageRow row)
    {
        var image = row.Image;
        return new AlbumImageItem(
            image.Id, image.FolderId, image.FileName, image.Extension, image.Width, image.Height, image.DateTaken,
            image.IsFavorite,
            row.IsMissing ? null : ImageUrls.Thumbnail(image.Id, image.ContentHash),
            row.IsMissing ? null : ImageUrls.Preview(image.Id, image.ContentHash),
            row.IsMissing);
    }
}

public sealed record AlbumCreate(string? Name, string? Description);

/// <summary>Name null = unchanged. DescriptionSpecified distinguishes "clear" (null) from "unchanged".</summary>
public sealed record AlbumUpdate(string? Name, bool DescriptionSpecified, string? Description);

/// <summary>Exactly one of ImageIds / FolderId.</summary>
public sealed record AlbumAddImages(IReadOnlyList<int>? ImageIds, int? FolderId);

public sealed record AlbumAddResult(int Added, int Skipped);

public sealed record AlbumExport(string FileName, string Content);

public sealed record AlbumImageCursor(
    [property: JsonPropertyName("s")] int SortOrder,
    [property: JsonPropertyName("i")] int ImageId);
```

`src/PictureManager.Application/Albums/AlbumExportFormatter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PictureManager.Application.Albums;

/// <summary>
/// Export line: {prefix without trailing '/'}/{Alias ?? Name}/{RelativePath}/{FileName}{Extension};
/// an empty RelativePath drops its segment; every line ends with '\n'.
/// </summary>
public static class AlbumExportFormatter
{
    private static readonly char[] InvalidFileNameChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

    public static string FormatLine(string? prefix, AlbumExportRow row)
    {
        var line = new StringBuilder((prefix ?? string.Empty).TrimEnd('/'));
        line.Append('/').Append(row.RootAlias ?? row.RootName);
        if (!string.IsNullOrEmpty(row.RelativePath))
            line.Append('/').Append(row.RelativePath);
        line.Append('/').Append(row.FileName).Append(row.Extension);
        return line.ToString();
    }

    public static string Format(string? prefix, IEnumerable<AlbumExportRow> rows)
    {
        var content = new StringBuilder();
        foreach (var row in rows)
            content.Append(FormatLine(prefix, row)).Append('\n');
        return content.ToString();
    }

    public static string FileName(string albumName)
    {
        var safe = new string(albumName
            .Select(c => char.IsControl(c) || Array.IndexOf(InvalidFileNameChars, c) >= 0 ? '_' : c)
            .ToArray()).Trim();
        return (safe.Length == 0 ? "album" : safe) + ".txt";
    }
}
```

`src/PictureManager.Application/Albums/IAlbumService.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Albums;

/// <summary>All operations are scoped to ICurrentUser; another owner's album behaves as unknown.</summary>
public interface IAlbumService
{
    Task<IReadOnlyList<AlbumSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Result<AlbumDetail>> CreateAsync(AlbumCreate input, CancellationToken cancellationToken = default);

    Task<Result<AlbumDetail>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<AlbumDetail>> UpdateAsync(int id, AlbumUpdate input, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<AlbumImageItem>>> ListImagesAsync(int id, string? cursor, int? limit, CancellationToken cancellationToken = default);

    Task<Result<AlbumAddResult>> AddImagesAsync(int id, AlbumAddImages input, CancellationToken cancellationToken = default);

    Task<Result> RemoveImagesAsync(int id, IReadOnlyList<int>? imageIds, CancellationToken cancellationToken = default);

    Task<Result> MoveImageAsync(int id, int imageId, int? afterImageId, CancellationToken cancellationToken = default);

    Task<Result<AlbumExport>> ExportAsync(int id, string? prefix, CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Application/Albums/AlbumService.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Albums;

public sealed class AlbumService : IAlbumService
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int DefaultLimit = 100;
    public const int MaxLimit = 200;

    private readonly IAlbumRepository _albums;
    private readonly IImageQueryRepository _images;
    private readonly IFolderRepository _folders;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public AlbumService(IAlbumRepository albums, IImageQueryRepository images, IFolderRepository folders, ICurrentUser currentUser, IClock clock)
    {
        _albums = albums;
        _images = images;
        _folders = folders;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<IReadOnlyList<AlbumSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _albums.GetSummariesAsync(_currentUser.UserId, cancellationToken);
        return rows.Select(r => new AlbumSummary(
            r.Id, r.Name, r.Description, r.ImageCount,
            r.CoverImageId is int coverId && r.CoverContentHash is { } hash ? ImageUrls.Thumbnail(coverId, hash) : null,
            r.UpdatedAt)).ToList();
    }

    public async Task<Result<AlbumDetail>> CreateAsync(AlbumCreate input, CancellationToken cancellationToken = default)
    {
        var name = input.Name?.Trim();
        if (ValidateName(name) is { } nameError)
            return Result.Invalid("name", nameError);

        var description = NormalizeDescription(input.Description);
        if (description is { Length: > DescriptionMaxLength })
            return Result.Invalid("description", $"Must be at most {DescriptionMaxLength} characters.");

        if (await _albums.NameExistsAsync(_currentUser.UserId, name!, null, cancellationToken))
            return Result.Conflict($"An album named '{name}' already exists.");

        var now = _clock.UtcNow;
        var album = await _albums.AddAsync(new Album
        {
            Name = name!,
            Description = description,
            OwnerUserId = _currentUser.UserId,
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken);

        return Result<AlbumDetail>.Ok(ToDetail(album, 0));
    }

    public async Task<Result<AlbumDetail>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var album = await _albums.GetOwnedAsync(id, _currentUser.UserId, cancellationToken);
        if (album is null)
            return Result.NotFound();

        return Result<AlbumDetail>.Ok(ToDetail(album, await _albums.CountImagesAsync(id, cancellationToken)));
    }

    public async Task<Result<AlbumDetail>> UpdateAsync(int id, AlbumUpdate input, CancellationToken cancellationToken = default)
    {
        var album = await _albums.GetOwnedAsync(id, _currentUser.UserId, cancellationToken);
        if (album is null)
            return Result.NotFound();

        if (input.Name is not null)
        {
            var name = input.Name.Trim();
            if (ValidateName(name) is { } nameError)
                return Result.Invalid("name", nameError);
            if (await _albums.NameExistsAsync(_currentUser.UserId, name, id, cancellationToken))
                return Result.Conflict($"An album named '{name}' already exists.");
            album.Name = name;
        }

        if (input.DescriptionSpecified)
        {
            var description = NormalizeDescription(input.Description);
            if (description is { Length: > DescriptionMaxLength })
                return Result.Invalid("description", $"Must be at most {DescriptionMaxLength} characters.");
            album.Description = description;
        }

        album.UpdatedAt = _clock.UtcNow;
        await _albums.UpdateAsync(album, cancellationToken);
        return Result<AlbumDetail>.Ok(ToDetail(album, await _albums.CountImagesAsync(id, cancellationToken)));
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var album = await _albums.GetOwnedAsync(id, _currentUser.UserId, cancellationToken);
        if (album is null)
            return Result.NotFound();

        await _albums.DeleteAsync(album, cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<PagedResult<AlbumImageItem>>> ListImagesAsync(int id, string? cursor, int? limit, CancellationToken cancellationToken = default)
    {
        if (await _albums.GetOwnedAsync(id, _currentUser.UserId, cancellationToken) is null)
            return Result.NotFound();

        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit)
            return Result.Invalid("limit", $"Must be between 1 and {MaxLimit}.");

        AlbumImageCursor? after = null;
        if (cursor is not null && !CursorCodec.TryDecode(cursor, out after))
            return Result.Invalid("cursor", "The cursor is malformed.");

        var rows = await _albums.ListImagesAsync(id, after?.SortOrder, after?.ImageId, take + 1, cancellationToken);
        var page = rows.Take(take).ToList();
        string? nextCursor = null;
        if (rows.Count > take)
        {
            var last = page[^1];
            nextCursor = CursorCodec.Encode(new AlbumImageCursor(last.SortOrder, last.Image.Id));
        }

        return Result<PagedResult<AlbumImageItem>>.Ok(
            new PagedResult<AlbumImageItem>(page.Select(AlbumImageItem.From).ToList(), nextCursor));
    }

    public async Task<Result<AlbumAddResult>> AddImagesAsync(int id, AlbumAddImages input, CancellationToken cancellationToken = default)
    {
        if (await _albums.GetOwnedAsync(id, _currentUser.UserId, cancellationToken) is null)
            return Result.NotFound();

        if ((input.ImageIds is null) == (input.FolderId is null))
            return Result.Invalid("body", "Provide either imageIds or folderId, not both.");

        List<int> candidates;
        if (input.ImageIds is not null)
        {
            candidates = input.ImageIds.Distinct().ToList();
            if (candidates.Count == 0)
                return Result.Invalid("imageIds", "Must contain at least one id.");

            var visible = (await _images.GetVisibleIdsAsync(candidates, cancellationToken)).ToHashSet();
            var unknown = candidates.Where(c => !visible.Contains(c)).ToList();
            if (unknown.Count > 0)
                return Result.Invalid("imageIds", $"Unknown or unavailable image ids: {string.Join(", ", unknown)}.");
        }
        else
        {
            if (!await _folders.IsVisibleAsync(input.FolderId!.Value, cancellationToken))
                return Result.NotFound();
            candidates = (await _images.GetVisibleIdsInFolderAsync(input.FolderId.Value, cancellationToken)).ToList();
        }

        var existing = (await _albums.GetOrderedImageIdsAsync(id, cancellationToken)).ToHashSet();
        var toAdd = candidates.Where(c => !existing.Contains(c)).ToList();
        if (toAdd.Count > 0)
        {
            var now = _clock.UtcNow;
            await _albums.AppendImagesAsync(id, toAdd, now, cancellationToken);
            await _albums.TouchAsync(id, now, cancellationToken);
        }

        return Result<AlbumAddResult>.Ok(new AlbumAddResult(toAdd.Count, candidates.Count - toAdd.Count));
    }

    public async Task<Result> RemoveImagesAsync(int id, IReadOnlyList<int>? imageIds, CancellationToken cancellationToken = default)
    {
        if (await _albums.GetOwnedAsync(id, _currentUser.UserId, cancellationToken) is null)
            return Result.NotFound();
        if (imageIds is null || imageIds.Count == 0)
            return Result.Invalid("imageIds", "Must contain at least one id.");

        await _albums.RemoveImagesAsync(id, imageIds.Distinct().ToList(), cancellationToken);
        await _albums.TouchAsync(id, _clock.UtcNow, cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> MoveImageAsync(int id, int imageId, int? afterImageId, CancellationToken cancellationToken = default)
    {
        if (await _albums.GetOwnedAsync(id, _currentUser.UserId, cancellationToken) is null)
            return Result.NotFound();

        var order = (await _albums.GetOrderedImageIdsAsync(id, cancellationToken)).ToList();
        if (!order.Contains(imageId))
            return Result.Invalid("imageId", "The image is not in this album.");
        if (afterImageId == imageId)
            return Result.Invalid("afterImageId", "An image cannot be moved after itself.");
        if (afterImageId is int anchor && !order.Contains(anchor))
            return Result.Invalid("afterImageId", "The anchor image is not in this album.");

        order.Remove(imageId);
        var insertAt = afterImageId is int after ? order.IndexOf(after) + 1 : 0;
        order.Insert(insertAt, imageId);

        await _albums.ReorderAsync(id, order, cancellationToken);
        await _albums.TouchAsync(id, _clock.UtcNow, cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<AlbumExport>> ExportAsync(int id, string? prefix, CancellationToken cancellationToken = default)
    {
        var album = await _albums.GetOwnedAsync(id, _currentUser.UserId, cancellationToken);
        if (album is null)
            return Result.NotFound();

        var rows = await _albums.GetExportRowsAsync(id, cancellationToken);
        return Result<AlbumExport>.Ok(new AlbumExport(
            AlbumExportFormatter.FileName(album.Name), AlbumExportFormatter.Format(prefix, rows)));
    }

    private static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Must not be blank.";
        if (name.Length > NameMaxLength)
            return $"Must be at most {NameMaxLength} characters.";
        return null;
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static AlbumDetail ToDetail(Album album, int imageCount) =>
        new(album.Id, album.Name, album.Description, imageCount, album.CreatedAt, album.UpdatedAt);
}
```

In `AddApplication` add (with `using PictureManager.Application.Albums;`):

```csharp
        services.AddScoped<IAlbumService, AlbumService>();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~Albums"`
Expected: PASS, 41 tests (AlbumExportFormatterTests 13, AlbumServiceTests 28), 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat: add album service (CRUD, contents, add/remove/move, export) and export formatter"
```

---

## Task 11: Album endpoints

**Files:**
- Create: `src/PictureManager.Api/Endpoints/AlbumEndpoints.cs`
- Modify: `src/PictureManager.Api/Program.cs`
- Test: `tests/PictureManager.Api.Tests/Endpoints/AlbumEndpointsTests.cs`

**Interfaces:**
- Consumes: `IAlbumService` and the album DTOs (Task 10); `PatchJson`, `ResultHttpExtensions` (Tasks 3, 8).
- Produces:
  - request records `AlbumCreateRequest(string? Name, string? Description)`,
    `AlbumAddImagesRequest(int[]? ImageIds, int? FolderId)`, `AlbumRemoveImagesRequest(int[]? ImageIds)` and
    `AlbumMoveRequest(int? AfterImageId)`, all in `PictureManager.Api.Endpoints`;
  - `AlbumEndpoints.MapAlbumEndpoints(this IEndpointRouteBuilder user)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/PictureManager.Api.Tests/Endpoints/AlbumEndpointsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Albums;
using PictureManager.Application.Common;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class AlbumEndpointsTests
{
    private readonly IAlbumService _service = Substitute.For<IAlbumService>();
    private static readonly AlbumDetail Detail = new(42, "Summer", null, 0, DateTime.UtcNow, DateTime.UtcNow);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task CreateAsync_Success_Returns201WithLocation()
    {
        _service.CreateAsync(new AlbumCreate("Summer", null), Arg.Any<CancellationToken>()).Returns(Result<AlbumDetail>.Ok(Detail));

        var result = await AlbumEndpoints.CreateAsync(new AlbumCreateRequest("Summer", null), _service, CancellationToken.None);

        var created = result.Result.Should().BeOfType<Created<AlbumDetail>>().Subject;
        created.Location.Should().Be("/api/albums/42");
        created.Value.Should().Be(Detail);
    }

    [Fact]
    public async Task CreateAsync_Conflict_Returns409()
    {
        _service.CreateAsync(Arg.Any<AlbumCreate>(), Arg.Any<CancellationToken>()).Returns(Result.Conflict("taken"));

        var result = await AlbumEndpoints.CreateAsync(new AlbumCreateRequest("Summer", null), _service, CancellationToken.None);

        result.Result.Should().BeOfType<Conflict<ProblemDetails>>();
    }

    [Fact]
    public async Task CreateAsync_Invalid_ReturnsValidationProblem()
    {
        _service.CreateAsync(Arg.Any<AlbumCreate>(), Arg.Any<CancellationToken>()).Returns(Result.Invalid("name", "Must not be blank."));

        var result = await AlbumEndpoints.CreateAsync(new AlbumCreateRequest(" ", null), _service, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
    }

    [Fact]
    public async Task UpdateAsync_DescriptionNull_IsPassedAsSpecified()
    {
        _service.UpdateAsync(42, Arg.Any<AlbumUpdate>(), Arg.Any<CancellationToken>()).Returns(Result<AlbumDetail>.Ok(Detail));

        await AlbumEndpoints.UpdateAsync(42, Json("""{ "description": null }"""), _service, CancellationToken.None);

        await _service.Received(1).UpdateAsync(42, new AlbumUpdate(null, true, null), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("""{ "name": 3 }""")]
    [InlineData("""{ "name": null }""")]
    [InlineData("""{ "description": false }""")]
    [InlineData("\"text\"")]
    public async Task UpdateAsync_BadBody_ReturnsValidationProblem(string json)
    {
        var result = await AlbumEndpoints.UpdateAsync(42, Json(json), _service, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
        await _service.DidNotReceive().UpdateAsync(Arg.Any<int>(), Arg.Any<AlbumUpdate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddImagesAsync_PassesIdsThrough_AndReturnsCounts()
    {
        _service.AddImagesAsync(42, Arg.Any<AlbumAddImages>(), Arg.Any<CancellationToken>())
            .Returns(Result<AlbumAddResult>.Ok(new AlbumAddResult(2, 1)));

        var result = await AlbumEndpoints.AddImagesAsync(42, new AlbumAddImagesRequest(new[] { 1, 2, 3 }, null), _service, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<AlbumAddResult>>().Which.Value.Should().Be(new AlbumAddResult(2, 1));
        await _service.Received(1).AddImagesAsync(42,
            Arg.Is<AlbumAddImages>(a => a.ImageIds!.SequenceEqual(new[] { 1, 2, 3 }) && a.FolderId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MoveImageAsync_PassesAnchor_AndReturnsNoContent()
    {
        _service.MoveImageAsync(42, 7, 9, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var result = await AlbumEndpoints.MoveImageAsync(42, 7, new AlbumMoveRequest(9), _service, CancellationToken.None);

        result.Result.Should().BeOfType<NoContent>();
    }

    [Fact]
    public async Task ExportAsync_ReturnsUtf8TextWithoutBom_AndAlbumFileName()
    {
        _service.ExportAsync(42, "/mnt", Arg.Any<CancellationToken>())
            .Returns(Result<AlbumExport>.Ok(new AlbumExport("Nyaralás 2025.txt", "/mnt/nas/Élet.jpg\n")));

        var result = await AlbumEndpoints.ExportAsync(42, "/mnt", _service, CancellationToken.None);

        var file = result.Result.Should().BeOfType<FileContentHttpResult>().Subject;
        file.ContentType.Should().Be("text/plain; charset=utf-8");
        file.FileDownloadName.Should().Be("Nyaralás 2025.txt");
        var bytes = file.FileContents.ToArray();
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        Encoding.UTF8.GetString(bytes).Should().Be("/mnt/nas/Élet.jpg\n");
    }

    [Fact]
    public async Task ExportAsync_UnknownAlbum_ReturnsNotFound()
    {
        _service.ExportAsync(8, null, Arg.Any<CancellationToken>()).Returns(Result.NotFound());

        (await AlbumEndpoints.ExportAsync(8, null, _service, CancellationToken.None)).Result.Should().BeOfType<NotFound>();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~AlbumEndpointsTests"`
Expected: build FAILS (`AlbumEndpoints`, `AlbumCreateRequest` … not found).

- [ ] **Step 3: Implement**

`src/PictureManager.Api/Endpoints/AlbumEndpoints.cs`:

```csharp
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Albums;
using PictureManager.Application.Common;

namespace PictureManager.Api.Endpoints;

public sealed record AlbumCreateRequest(string? Name, string? Description);

public sealed record AlbumAddImagesRequest(int[]? ImageIds, int? FolderId);

public sealed record AlbumRemoveImagesRequest(int[]? ImageIds);

public sealed record AlbumMoveRequest(int? AfterImageId);

public static class AlbumEndpoints
{
    public static IEndpointRouteBuilder MapAlbumEndpoints(this IEndpointRouteBuilder user)
    {
        user.MapGet("/albums", GetAllAsync);
        user.MapPost("/albums", CreateAsync);
        user.MapGet("/albums/{id:int}", GetAsync);
        user.MapPatch("/albums/{id:int}", UpdateAsync);
        user.MapDelete("/albums/{id:int}", DeleteAsync);
        user.MapGet("/albums/{id:int}/images", ListImagesAsync);
        user.MapPost("/albums/{id:int}/images", AddImagesAsync);
        user.MapPost("/albums/{id:int}/images/remove", RemoveImagesAsync);
        user.MapPost("/albums/{id:int}/images/{imageId:int}/move", MoveImageAsync);
        user.MapGet("/albums/{id:int}/export", ExportAsync);
        return user;
    }

    public static async Task<Ok<IReadOnlyList<AlbumSummary>>> GetAllAsync(IAlbumService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAllAsync(cancellationToken));

    public static async Task<Results<Created<AlbumDetail>, ValidationProblem, Conflict<ProblemDetails>>> CreateAsync(
        AlbumCreateRequest body, IAlbumService service, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(new AlbumCreate(body.Name, body.Description), cancellationToken);
        return result.Status switch
        {
            ResultStatus.Success => TypedResults.Created($"/api/albums/{result.Value!.Id}", result.Value),
            ResultStatus.Conflict => TypedResults.Conflict(ResultHttpExtensions.ConflictProblem(result.Message)),
            _ => TypedResults.ValidationProblem(ResultHttpExtensions.ToErrors(result.Errors))
        };
    }

    public static async Task<Results<Ok<AlbumDetail>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> GetAsync(
        int id, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.GetAsync(id, cancellationToken)).ToOk();

    public static async Task<Results<Ok<AlbumDetail>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> UpdateAsync(
        int id, JsonElement body, IAlbumService service, CancellationToken cancellationToken)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return ResultHttpExtensions.Invalid("body", "Expected a JSON object.");
        if (!PatchJson.TryReadString(body, "name", out var namePresent, out var name) || (namePresent && name is null))
            return ResultHttpExtensions.Invalid("name", "Must be a string.");
        if (!PatchJson.TryReadString(body, "description", out var descriptionPresent, out var description))
            return ResultHttpExtensions.Invalid("description", "Must be a string or null.");

        return (await service.UpdateAsync(id, new AlbumUpdate(name, descriptionPresent, description), cancellationToken)).ToOk();
    }

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> DeleteAsync(
        int id, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(id, cancellationToken)).ToNoContent();

    public static async Task<Results<Ok<PagedResult<AlbumImageItem>>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> ListImagesAsync(
        int id, string? cursor, int? limit, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.ListImagesAsync(id, cursor, limit, cancellationToken)).ToOk();

    public static async Task<Results<Ok<AlbumAddResult>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> AddImagesAsync(
        int id, AlbumAddImagesRequest body, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.AddImagesAsync(id, new AlbumAddImages(body.ImageIds, body.FolderId), cancellationToken)).ToOk();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> RemoveImagesAsync(
        int id, AlbumRemoveImagesRequest body, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.RemoveImagesAsync(id, body.ImageIds, cancellationToken)).ToNoContent();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> MoveImageAsync(
        int id, int imageId, AlbumMoveRequest body, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.MoveImageAsync(id, imageId, body.AfterImageId, cancellationToken)).ToNoContent();

    public static async Task<Results<FileContentHttpResult, NotFound>> ExportAsync(
        int id, string? prefix, IAlbumService service, CancellationToken cancellationToken)
    {
        var result = await service.ExportAsync(id, prefix, cancellationToken);
        if (!result.IsSuccess)
            return TypedResults.NotFound();

        // Encoding.UTF8.GetBytes never writes a BOM. Passing fileDownloadName makes ASP.NET Core emit
        // both filename= and the RFC 5987 filename*= form, so non-ASCII album names survive.
        return TypedResults.File(Encoding.UTF8.GetBytes(result.Value!.Content), "text/plain; charset=utf-8", result.Value.FileName);
    }
}
```

In `Program.cs`, after `user.MapFolderEndpoints(admin);` add `user.MapAlbumEndpoints();`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~AlbumEndpointsTests"`
Expected: PASS, 12 tests (8 facts + 4 theory rows), 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/PictureManager.Api tests/PictureManager.Api.Tests
git commit -m "feat: add album endpoints (CRUD, contents, add/remove/move, text export)"
```

---

## Task 12: Duplicate finder

**Files:**
- Create: `src/PictureManager.Application/Duplicates/DuplicateModels.cs`
- Create: `src/PictureManager.Application/Duplicates/IDuplicateService.cs`
- Create: `src/PictureManager.Application/Duplicates/DuplicateService.cs`
- Modify: `src/PictureManager.Application/Repositories/IImageQueryRepository.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/ImageQueryRepository.cs`
- Modify: `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- Create: `src/PictureManager.Api/Endpoints/DuplicateEndpoints.cs`
- Modify: `src/PictureManager.Api/Program.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/DuplicateQueryRepositoryTests.cs`
- Test: `tests/PictureManager.Application.Tests/Duplicates/DuplicateServiceTests.cs`

**Interfaces:**
- Consumes: `IImageQueryRepository`, `ImageRow`, `WhereVisible` (Task 4); `CursorCodec`, `ImageUrls`,
  `FolderDisplayPath`, `PagedResult` (Task 3).
- Produces (namespace `PictureManager.Application.Duplicates`):
  - `sealed record DuplicateGroupKey(string ContentHash, int Count)`
  - `sealed record DuplicateMemberRow(ImageRow Image, string RootName, string RelativePath)`
  - `sealed record DuplicateImageItem(int Id, int FolderId, string FileName, string Extension, int? Width, int? Height, DateTime? DateTaken, bool IsFavorite, string? ThumbnailUrl, string? PreviewUrl, string FolderPath)`
  - `sealed record DuplicateGroup(string ContentHash, int Count, IReadOnlyList<DuplicateImageItem> Images)`
  - `sealed record DuplicateCursor(int Count, string ContentHash)`, with JSON names `c`, `h`
  - `interface IDuplicateService { Task<Result<PagedResult<DuplicateGroup>>> ListAsync(string? cursor, int? limit, CancellationToken cancellationToken = default); }`,
    implemented by `DuplicateService(IImageQueryRepository)` (scoped). `DefaultLimit = 50`, `MaxLimit = 100`.
- Produces, added to `IImageQueryRepository`:
  - `Task<IReadOnlyList<DuplicateGroupKey>> GetDuplicateGroupsAsync(DuplicateGroupKey? after, int take, CancellationToken cancellationToken = default)`
  - `Task<IReadOnlyList<DuplicateMemberRow>> GetDuplicateMembersAsync(IReadOnlyCollection<string> contentHashes, CancellationToken cancellationToken = default)`
- Produces `DuplicateEndpoints.MapDuplicateEndpoints(this IEndpointRouteBuilder user)`, which maps `GET /duplicates`.

- [ ] **Step 1: Write the failing repository tests**

Create `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/DuplicateQueryRepositoryTests.cs`:

```csharp
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Application.Duplicates;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class DuplicateQueryRepositoryTests
{
    [Fact]
    public async Task GetDuplicateGroupsAsync_CountsOnlyVisibleHashedImages_OrderedByCountDescThenHash()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        db.Context.Images.AddRange(
            TestData.Image(folder, "a1", contentHash: "AAA"),
            TestData.Image(folder, "a2", contentHash: "AAA"),
            TestData.Image(folder, "a3", contentHash: "AAA"),
            TestData.Image(folder, "b1", contentHash: "BBB"),
            TestData.Image(folder, "b2", contentHash: "BBB"),
            TestData.Image(folder, "c1", contentHash: "CCC"),
            TestData.Image(folder, "d1", contentHash: "DDD"),
            TestData.Image(folder, "d2", contentHash: "DDD", missingSinceUtc: TestData.Utc),
            TestData.Image(folder, "p1", contentHash: ""),
            TestData.Image(folder, "p2", contentHash: ""));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var groups = await new ImageQueryRepository(context).GetDuplicateGroupsAsync(null, 10);

        groups.Should().Equal(new DuplicateGroupKey("AAA", 3), new DuplicateGroupKey("BBB", 2));
    }

    [Fact]
    public async Task GetDuplicateGroupsAsync_KeysetContinuesAfterTheLastGroup()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        foreach (var hash in new[] { "CCC", "AAA", "BBB" })
            db.Context.Images.AddRange(TestData.Image(folder, hash + "1", contentHash: hash), TestData.Image(folder, hash + "2", contentHash: hash));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);
        var first = await repository.GetDuplicateGroupsAsync(null, 2);
        var second = await repository.GetDuplicateGroupsAsync(first[^1], 2);

        first.Select(g => g.ContentHash).Should().Equal("AAA", "BBB");
        second.Select(g => g.ContentHash).Should().Equal("CCC");
    }

    [Fact]
    public async Task GetDuplicateMembersAsync_ReturnsVisibleMembersWithLocation()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("nas");
        var top = TestData.Folder(root, "");
        var trip = TestData.Folder(root, "Trip", top);
        var original = TestData.Image(trip, "IMG_1", contentHash: "AAA");
        var copy = TestData.Image(top, "IMG_1 copy", contentHash: "AAA");
        db.Context.Images.AddRange(original, copy, TestData.Image(top, "gone", contentHash: "AAA", missingSinceUtc: TestData.Utc));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var members = await new ImageQueryRepository(context).GetDuplicateMembersAsync(new[] { "AAA" });

        members.Select(m => (m.Image.Id, m.RootName, m.RelativePath)).Should().BeEquivalentTo(new[]
        {
            (original.Id, "nas", "Trip"),
            (copy.Id, "nas", "")
        });
    }
}
```

- [ ] **Step 2: Write the failing service tests**

Create `tests/PictureManager.Application.Tests/Duplicates/DuplicateServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Duplicates;
using PictureManager.Application.Images;
using PictureManager.Application.Repositories;
using Xunit;

namespace PictureManager.Application.Tests.Duplicates;

public class DuplicateServiceTests
{
    private readonly IImageQueryRepository _images = Substitute.For<IImageQueryRepository>();

    private DuplicateService CreateService() => new(_images);

    private static DuplicateMemberRow Member(int id, string hash, string name, string relativePath) =>
        new(new ImageRow(id, 1, name, ".jpg", null, null, null, false, hash, DateTime.UtcNow, name.ToLowerInvariant()), "nas", relativePath);

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task ListAsync_LimitOutOfRange_ReturnsInvalid(int limit)
    {
        (await CreateService().ListAsync(null, limit)).Errors!.Keys.Should().Contain("limit");
    }

    [Fact]
    public async Task ListAsync_MalformedCursor_ReturnsInvalid()
    {
        (await CreateService().ListAsync("!!", null)).Errors!.Keys.Should().Contain("cursor");
    }

    [Fact]
    public async Task ListAsync_AssemblesGroups_MembersOrderedByFolderPathThenName_WithNextCursor()
    {
        _images.GetDuplicateGroupsAsync(null, 2, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new DuplicateGroupKey("AAA", 2), new DuplicateGroupKey("BBB", 2)
        });
        _images.GetDuplicateMembersAsync(Arg.Is<IReadOnlyCollection<string>>(h => h.SequenceEqual(new[] { "AAA" })), Arg.Any<CancellationToken>())
            .Returns(new[] { Member(2, "AAA", "b", "Trip"), Member(1, "AAA", "a", "") });

        var page = (await CreateService().ListAsync(null, 1)).Value!;

        page.Items.Should().ContainSingle();
        var group = page.Items[0];
        group.ContentHash.Should().Be("AAA");
        group.Count.Should().Be(2);
        group.Images.Select(i => i.FolderPath).Should().Equal("nas", "nas/Trip");
        group.Images[0].ThumbnailUrl.Should().Be("/api/images/1/thumbnail?v=AAA");
        CursorCodec.TryDecode<DuplicateCursor>(page.NextCursor, out var cursor).Should().BeTrue();
        cursor.Should().Be(new DuplicateCursor(2, "AAA"));
    }

    [Fact]
    public async Task ListAsync_CursorIsPassedAsKeyset()
    {
        _images.GetDuplicateGroupsAsync(Arg.Any<DuplicateGroupKey?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DuplicateGroupKey>());

        var page = (await CreateService().ListAsync(CursorCodec.Encode(new DuplicateCursor(3, "XYZ")), null)).Value!;

        page.Items.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
        await _images.Received(1).GetDuplicateGroupsAsync(new DuplicateGroupKey("XYZ", 3), 51, Arg.Any<CancellationToken>());
        await _images.DidNotReceive().GetDuplicateMembersAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet build`
Expected: build FAILS (`DuplicateGroupKey`, `DuplicateService`, `GetDuplicateGroupsAsync` … not found).

- [ ] **Step 4: Implement**

`src/PictureManager.Application/Duplicates/DuplicateModels.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using PictureManager.Application.Images;

namespace PictureManager.Application.Duplicates;

public sealed record DuplicateGroupKey(string ContentHash, int Count);

public sealed record DuplicateMemberRow(ImageRow Image, string RootName, string RelativePath);

public sealed record DuplicateImageItem(
    int Id,
    int FolderId,
    string FileName,
    string Extension,
    int? Width,
    int? Height,
    DateTime? DateTaken,
    bool IsFavorite,
    string? ThumbnailUrl,
    string? PreviewUrl,
    string FolderPath);

/// <summary>Images sharing a ContentHash. Read-only review aid; the hash samples size + first/last 64KB.</summary>
public sealed record DuplicateGroup(string ContentHash, int Count, IReadOnlyList<DuplicateImageItem> Images);

public sealed record DuplicateCursor(
    [property: JsonPropertyName("c")] int Count,
    [property: JsonPropertyName("h")] string ContentHash);
```

`src/PictureManager.Application/Duplicates/IDuplicateService.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Duplicates;

public interface IDuplicateService
{
    Task<Result<PagedResult<DuplicateGroup>>> ListAsync(string? cursor, int? limit, CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Application/Duplicates/DuplicateService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Duplicates;

public sealed class DuplicateService : IDuplicateService
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    private readonly IImageQueryRepository _images;

    public DuplicateService(IImageQueryRepository images)
    {
        _images = images;
    }

    public async Task<Result<PagedResult<DuplicateGroup>>> ListAsync(string? cursor, int? limit, CancellationToken cancellationToken = default)
    {
        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit)
            return Result.Invalid("limit", $"Must be between 1 and {MaxLimit}.");

        DuplicateGroupKey? after = null;
        if (cursor is not null)
        {
            if (!CursorCodec.TryDecode<DuplicateCursor>(cursor, out var decoded) || decoded.ContentHash is null)
                return Result.Invalid("cursor", "The cursor is malformed.");
            after = new DuplicateGroupKey(decoded.ContentHash, decoded.Count);
        }

        var keys = await _images.GetDuplicateGroupsAsync(after, take + 1, cancellationToken);
        var pageKeys = keys.Take(take).ToList();

        var members = pageKeys.Count == 0
            ? Array.Empty<DuplicateMemberRow>()
            : await _images.GetDuplicateMembersAsync(pageKeys.Select(k => k.ContentHash).ToList(), cancellationToken);

        var byHash = members
            .GroupBy(m => m.Image.ContentHash)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<DuplicateImageItem>)g.Select(ToItem)
                    .OrderBy(i => i.FolderPath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToList());

        var groups = pageKeys
            .Select(k => new DuplicateGroup(k.ContentHash, k.Count,
                byHash.TryGetValue(k.ContentHash, out var images) ? images : Array.Empty<DuplicateImageItem>()))
            .ToList();

        var nextCursor = keys.Count > take
            ? CursorCodec.Encode(new DuplicateCursor(pageKeys[^1].Count, pageKeys[^1].ContentHash))
            : null;

        return Result<PagedResult<DuplicateGroup>>.Ok(new PagedResult<DuplicateGroup>(groups, nextCursor));
    }

    private static DuplicateImageItem ToItem(DuplicateMemberRow row)
    {
        var image = row.Image;
        return new DuplicateImageItem(
            image.Id, image.FolderId, image.FileName, image.Extension, image.Width, image.Height, image.DateTaken,
            image.IsFavorite, ImageUrls.Thumbnail(image.Id, image.ContentHash), ImageUrls.Preview(image.Id, image.ContentHash),
            FolderDisplayPath.For(row.RootName, row.RelativePath));
    }
}
```

Add to `IImageQueryRepository` (plus `using PictureManager.Application.Duplicates;`):

```csharp
    /// <summary>ContentHash groups with 2+ visible, hashed members; ordered by Count desc, ContentHash asc; after = keyset.</summary>
    Task<IReadOnlyList<DuplicateGroupKey>> GetDuplicateGroupsAsync(DuplicateGroupKey? after, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DuplicateMemberRow>> GetDuplicateMembersAsync(IReadOnlyCollection<string> contentHashes, CancellationToken cancellationToken = default);
```

Add to `ImageQueryRepository` (plus `using PictureManager.Application.Duplicates;`):

```csharp
    public async Task<IReadOnlyList<DuplicateGroupKey>> GetDuplicateGroupsAsync(DuplicateGroupKey? after, int take, CancellationToken cancellationToken = default)
    {
        var groups = _dbContext.Images.AsNoTracking().WhereVisible()
            .Where(i => i.ContentHash != "")
            .GroupBy(i => i.ContentHash)
            .Where(g => g.Count() > 1)
            .Select(g => new { ContentHash = g.Key, Count = g.Count() });

        if (after is not null)
        {
            var afterCount = after.Count;
            var afterHash = after.ContentHash;
            groups = groups.Where(g => g.Count < afterCount || (g.Count == afterCount && string.Compare(g.ContentHash, afterHash) > 0));
        }

        return await groups
            .OrderByDescending(g => g.Count).ThenBy(g => g.ContentHash)
            .Take(take)
            .Select(g => new DuplicateGroupKey(g.ContentHash, g.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DuplicateMemberRow>> GetDuplicateMembersAsync(IReadOnlyCollection<string> contentHashes, CancellationToken cancellationToken = default)
    {
        var hashes = contentHashes.ToList();
        return await _dbContext.Images.AsNoTracking().WhereVisible()
            .Where(i => hashes.Contains(i.ContentHash))
            .Select(i => new DuplicateMemberRow(
                new ImageRow(i.Id, i.FolderId, i.FileName, i.Extension, i.Width, i.Height, i.DateTaken, i.IsFavorite,
                    i.ContentHash, i.SortDate, i.FileName.ToLower()),
                i.Folder!.Root!.Name,
                i.Folder.RelativePath))
            .ToListAsync(cancellationToken);
    }
```

`src/PictureManager.Api/Endpoints/DuplicateEndpoints.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Common;
using PictureManager.Application.Duplicates;

namespace PictureManager.Api.Endpoints;

public static class DuplicateEndpoints
{
    public static IEndpointRouteBuilder MapDuplicateEndpoints(this IEndpointRouteBuilder user)
    {
        user.MapGet("/duplicates", ListAsync);
        return user;
    }

    public static async Task<Results<Ok<PagedResult<DuplicateGroup>>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> ListAsync(
        string? cursor, int? limit, IDuplicateService service, CancellationToken cancellationToken) =>
        (await service.ListAsync(cursor, limit, cancellationToken)).ToOk();
}
```

Register it: in `AddApplication` add `services.AddScoped<IDuplicateService, DuplicateService>();` (with
`using PictureManager.Application.Duplicates;`). In `Program.cs`, after `user.MapAlbumEndpoints();` add
`user.MapDuplicateEndpoints();`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~DuplicateQueryRepositoryTests"`
Expected: PASS, 3 tests.

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~DuplicateServiceTests"`
Expected: PASS, 5 tests (3 facts + 2 theory rows).

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat: add duplicate finder (content-hash groups, keyset paged)"
```

---

## Task 13: Settings (normalized save, prune warning flag)

**Files:**
- Create: `src/PictureManager.Application/Settings/SettingsModels.cs`
- Create: `src/PictureManager.Application/Settings/SettingsNormalizer.cs`
- Create: `src/PictureManager.Application/Settings/ISettingsService.cs`
- Create: `src/PictureManager.Application/Settings/SettingsService.cs`
- Modify: `src/PictureManager.Application/Repositories/IAppSettingsRepository.cs`,
  `src/PictureManager.Infrastructure/Persistence/Repositories/AppSettingsRepository.cs`
- Modify: `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- Create: `src/PictureManager.Api/Endpoints/SettingsEndpoints.cs`
- Modify: `src/PictureManager.Api/Program.cs`
- Test: `tests/PictureManager.Application.Tests/Settings/SettingsNormalizerTests.cs`
- Test: `tests/PictureManager.Application.Tests/Settings/SettingsServiceTests.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AppSettingsPostgresTests.cs`

**Interfaces:**
- Consumes: `Result` (Task 3), `ResultHttpExtensions` (Task 3).
- Produces (namespace `PictureManager.Application.Settings`):
  - `sealed record SettingsDto(IReadOnlyList<string> ExcludedFolderNames, IReadOnlyList<string> ExcludedExtensions, IReadOnlyList<string>? IncludedExtensions)`
  - `sealed record SettingsInput(IReadOnlyList<string?>? ExcludedFolderNames, IReadOnlyList<string?>? ExcludedExtensions, IReadOnlyList<string?>? IncludedExtensions)`
  - `sealed record SettingsSaveResult(IReadOnlyList<string> ExcludedFolderNames, IReadOnlyList<string> ExcludedExtensions, IReadOnlyList<string>? IncludedExtensions, bool PruneOnNextScan)`
  - `static class SettingsNormalizer`, with
    `bool TryNormalizeFolderNames(IEnumerable<string?>? values, out List<string> normalized, out string? error)` and
    `bool TryNormalizeExtensions(IEnumerable<string?>? values, out List<string> normalized, out string? error)`
  - `interface ISettingsService`, implemented by `SettingsService(IAppSettingsRepository)` (scoped):
    - `Task<SettingsDto> GetAsync(CancellationToken cancellationToken = default)`
    - `Task<Result<SettingsSaveResult>> UpdateAsync(SettingsInput input, CancellationToken cancellationToken = default)`
  - `IAppSettingsRepository.UpdateAsync(AppSettings settings, CancellationToken cancellationToken = default)`
- Produces `SettingsEndpoints.MapSettingsEndpoints(this IEndpointRouteBuilder admin)`, with the request record
  `SettingsRequest(string?[]? ExcludedFolderNames, string?[]? ExcludedExtensions, string?[]? IncludedExtensions)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/PictureManager.Application.Tests/Settings/SettingsNormalizerTests.cs`:

```csharp
using FluentAssertions;
using PictureManager.Application.Settings;
using Xunit;

namespace PictureManager.Application.Tests.Settings;

public class SettingsNormalizerTests
{
    [Fact]
    public void TryNormalizeFolderNames_TrimsAndDedupesCaseInsensitively_KeepingFirstSpelling()
    {
        SettingsNormalizer.TryNormalizeFolderNames(new[] { " raw ", "RAW", "@eaDir" }, out var names, out _).Should().BeTrue();

        names.Should().Equal("raw", "@eaDir");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void TryNormalizeFolderNames_RejectsBlankOrSeparators(string? value)
    {
        SettingsNormalizer.TryNormalizeFolderNames(new[] { value }, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TryNormalizeExtensions_LowercasesAddsDotAndDedupes()
    {
        SettingsNormalizer.TryNormalizeExtensions(new[] { "HEIC", ".heic", " .Png " }, out var extensions, out _).Should().BeTrue();

        extensions.Should().Equal(".heic", ".png");
    }

    [Theory]
    [InlineData(".")]
    [InlineData(" ")]
    [InlineData("a/b")]
    public void TryNormalizeExtensions_RejectsBlankDotOrSeparators(string value)
    {
        SettingsNormalizer.TryNormalizeExtensions(new[] { value }, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TryNormalize_NullList_IsEmpty()
    {
        SettingsNormalizer.TryNormalizeFolderNames(null, out var names, out _).Should().BeTrue();
        names.Should().BeEmpty();
    }
}
```

Create `tests/PictureManager.Application.Tests/Settings/SettingsServiceTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Settings;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Settings;

public class SettingsServiceTests
{
    private readonly IAppSettingsRepository _repository = Substitute.For<IAppSettingsRepository>();
    private readonly AppSettings _current = new()
    {
        Id = 1,
        ExcludedFolderNames = new List<string> { "raw" },
        ExcludedExtensions = new List<string> { ".heic" },
        IncludedExtensions = null
    };

    public SettingsServiceTests()
    {
        _repository.GetAsync(Arg.Any<CancellationToken>()).Returns(_current);
    }

    private SettingsService CreateService() => new(_repository);

    private static SettingsInput Input(string?[]? folders, string?[]? excluded, string?[]? included = null) =>
        new(folders, excluded, included);

    [Fact]
    public async Task UpdateAsync_SameRules_DoesNotFlagPrune()
    {
        var result = await CreateService().UpdateAsync(Input(new[] { "RAW" }, new[] { "heic" }));

        result.Value!.PruneOnNextScan.Should().BeFalse();
        await _repository.Received(1).UpdateAsync(_current, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_RemovingAnExclusion_DoesNotFlagPrune()
    {
        (await CreateService().UpdateAsync(Input(new string[0], new string[0]))).Value!.PruneOnNextScan.Should().BeFalse();
    }

    [Theory]
    [InlineData(new[] { "raw", "backup" }, new[] { ".heic" }, null)]
    [InlineData(new[] { "raw" }, new[] { ".heic", ".png" }, null)]
    [InlineData(new[] { "raw" }, new[] { ".heic" }, new[] { ".jpg" })]
    public async Task UpdateAsync_NewlyExcludingSomething_FlagsPrune(string[] folders, string[] excluded, string[]? included)
    {
        (await CreateService().UpdateAsync(Input(folders, excluded, included))).Value!.PruneOnNextScan.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_NarrowingAnExistingIncludeList_FlagsPrune()
    {
        _current.IncludedExtensions = new List<string> { ".jpg", ".png" };

        (await CreateService().UpdateAsync(Input(new[] { "raw" }, new[] { ".heic" }, new[] { ".jpg" })))
            .Value!.PruneOnNextScan.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_EmptyIncludeList_MeansAllowAll()
    {
        var result = await CreateService().UpdateAsync(Input(new[] { "raw" }, new[] { ".heic" }, new string[0]));

        result.Value!.IncludedExtensions.Should().BeNull();
        _current.IncludedExtensions.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_SavesNormalizedValues()
    {
        var result = await CreateService().UpdateAsync(Input(new[] { " raw ", "Backup" }, new[] { "HEIC" }));

        result.Value!.ExcludedFolderNames.Should().Equal("raw", "Backup");
        _current.ExcludedExtensions.Should().Equal(".heic");
    }

    [Theory]
    [InlineData("excludedFolderNames")]
    [InlineData("excludedExtensions")]
    [InlineData("includedExtensions")]
    public async Task UpdateAsync_InvalidValue_ReturnsInvalidForThatField_AndSavesNothing(string field)
    {
        var input = field switch
        {
            "excludedFolderNames" => Input(new[] { "a/b" }, new string[0]),
            "excludedExtensions" => Input(new string[0], new[] { " " }),
            _ => Input(new string[0], new string[0], new[] { "." })
        };

        var result = await CreateService().UpdateAsync(input);

        result.Errors!.Keys.Should().Contain(field);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<AppSettings>(), Arg.Any<CancellationToken>());
    }
}
```

Create `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AppSettingsPostgresTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class AppSettingsPostgresTests
{
    [Fact]
    public async Task UpdateAsync_PersistsAllThreeLists()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        await using (var context = db.CreateContext())
        {
            var repository = new AppSettingsRepository(context);
            var settings = await repository.GetAsync();
            settings.ExcludedFolderNames = new List<string> { "raw", "Thumbs" };
            settings.ExcludedExtensions = new List<string> { ".png" };
            settings.IncludedExtensions = new List<string> { ".jpg" };
            await repository.UpdateAsync(settings);
        }

        await using var read = db.CreateContext();
        var saved = await new AppSettingsRepository(read).GetAsync();
        saved.ExcludedFolderNames.Should().Equal("raw", "Thumbs");
        saved.ExcludedExtensions.Should().Equal(".png");
        saved.IncludedExtensions.Should().Equal(".jpg");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build`
Expected: build FAILS (`SettingsNormalizer`, `SettingsService`, `IAppSettingsRepository.UpdateAsync` … not found).

- [ ] **Step 3: Implement**

`src/PictureManager.Application/Settings/SettingsModels.cs`:

```csharp
using System.Collections.Generic;

namespace PictureManager.Application.Settings;

/// <summary>IncludedExtensions null = every extension is allowed (subject to ExcludedExtensions).</summary>
public sealed record SettingsDto(
    IReadOnlyList<string> ExcludedFolderNames,
    IReadOnlyList<string> ExcludedExtensions,
    IReadOnlyList<string>? IncludedExtensions);

public sealed record SettingsInput(
    IReadOnlyList<string?>? ExcludedFolderNames,
    IReadOnlyList<string?>? ExcludedExtensions,
    IReadOnlyList<string?>? IncludedExtensions);

/// <summary>PruneOnNextScan: the save newly excludes something, so the next scan will delete matching rows.</summary>
public sealed record SettingsSaveResult(
    IReadOnlyList<string> ExcludedFolderNames,
    IReadOnlyList<string> ExcludedExtensions,
    IReadOnlyList<string>? IncludedExtensions,
    bool PruneOnNextScan);
```

`src/PictureManager.Application/Settings/SettingsNormalizer.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace PictureManager.Application.Settings;

public static class SettingsNormalizer
{
    private static readonly char[] Separators = { '/', '\\' };

    /// <summary>Trims, rejects blanks and path separators, de-duplicates case-insensitively (first spelling wins).</summary>
    public static bool TryNormalizeFolderNames(IEnumerable<string?>? values, out List<string> normalized, out string? error)
    {
        normalized = new List<string>();
        error = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in values ?? Array.Empty<string?>())
        {
            var value = raw?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                error = "Values must not be blank.";
                return false;
            }
            if (value.IndexOfAny(Separators) >= 0)
            {
                error = $"'{value}' must not contain '/' or '\\'.";
                return false;
            }
            if (seen.Add(value))
                normalized.Add(value);
        }

        return true;
    }

    /// <summary>Trims, lowercases, adds the leading '.', rejects blanks/"."/separators, de-duplicates.</summary>
    public static bool TryNormalizeExtensions(IEnumerable<string?>? values, out List<string> normalized, out string? error)
    {
        normalized = new List<string>();
        error = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in values ?? Array.Empty<string?>())
        {
            var value = raw?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(value))
            {
                error = "Values must not be blank.";
                return false;
            }
            if (!value.StartsWith('.'))
                value = "." + value;
            if (value == ".")
            {
                error = "'.' is not an extension.";
                return false;
            }
            if (value.IndexOfAny(Separators) >= 0)
            {
                error = $"'{value}' must not contain '/' or '\\'.";
                return false;
            }
            if (seen.Add(value))
                normalized.Add(value);
        }

        return true;
    }
}
```

`src/PictureManager.Application/Settings/ISettingsService.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Settings;

public interface ISettingsService
{
    Task<SettingsDto> GetAsync(CancellationToken cancellationToken = default);

    Task<Result<SettingsSaveResult>> UpdateAsync(SettingsInput input, CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Application/Settings/SettingsService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Settings;

public sealed class SettingsService : ISettingsService
{
    private readonly IAppSettingsRepository _repository;

    public SettingsService(IAppSettingsRepository repository)
    {
        _repository = repository;
    }

    public async Task<SettingsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetAsync(cancellationToken);
        return new SettingsDto(settings.ExcludedFolderNames, settings.ExcludedExtensions,
            settings.IncludedExtensions is { Count: > 0 } ? settings.IncludedExtensions : null);
    }

    public async Task<Result<SettingsSaveResult>> UpdateAsync(SettingsInput input, CancellationToken cancellationToken = default)
    {
        if (!SettingsNormalizer.TryNormalizeFolderNames(input.ExcludedFolderNames, out var folderNames, out var folderError))
            return Result.Invalid("excludedFolderNames", folderError!);
        if (!SettingsNormalizer.TryNormalizeExtensions(input.ExcludedExtensions, out var excluded, out var excludedError))
            return Result.Invalid("excludedExtensions", excludedError!);

        List<string>? included = null;
        if (input.IncludedExtensions is not null)
        {
            if (!SettingsNormalizer.TryNormalizeExtensions(input.IncludedExtensions, out var normalizedIncluded, out var includedError))
                return Result.Invalid("includedExtensions", includedError!);
            included = normalizedIncluded.Count == 0 ? null : normalizedIncluded;
        }

        var settings = await _repository.GetAsync(cancellationToken);
        var prune = NewlyExcludesSomething(settings, folderNames, excluded, included);

        settings.ExcludedFolderNames = folderNames;
        settings.ExcludedExtensions = excluded;
        settings.IncludedExtensions = included;
        await _repository.UpdateAsync(settings, cancellationToken);

        return Result<SettingsSaveResult>.Ok(new SettingsSaveResult(folderNames, excluded, included, prune));
    }

    private static bool NewlyExcludesSomething(AppSettings old, List<string> folderNames, List<string> excluded, List<string>? included)
    {
        var oldFolders = new HashSet<string>(old.ExcludedFolderNames, StringComparer.OrdinalIgnoreCase);
        var oldExcluded = new HashSet<string>(old.ExcludedExtensions, StringComparer.OrdinalIgnoreCase);
        if (folderNames.Any(f => !oldFolders.Contains(f)) || excluded.Any(e => !oldExcluded.Contains(e)))
            return true;

        if (included is null)
            return false;

        // A new include list narrows what's allowed: from "everything" (no old list), or by dropping entries.
        if (old.IncludedExtensions is not { Count: > 0 } oldIncluded)
            return true;

        var newIncluded = new HashSet<string>(included, StringComparer.OrdinalIgnoreCase);
        return oldIncluded.Any(e => !newIncluded.Contains(e));
    }
}
```

Add to `IAppSettingsRepository`:

```csharp
    Task UpdateAsync(AppSettings settings, CancellationToken cancellationToken = default);
```

Add to `AppSettingsRepository`:

```csharp
    public async Task UpdateAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _dbContext.Settings.Update(settings);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
```

`src/PictureManager.Api/Endpoints/SettingsEndpoints.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Settings;

namespace PictureManager.Api.Endpoints;

public sealed record SettingsRequest(string?[]? ExcludedFolderNames, string?[]? ExcludedExtensions, string?[]? IncludedExtensions);

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapGet("/settings", GetAsync);
        admin.MapPut("/settings", UpdateAsync);
        return admin;
    }

    public static async Task<Ok<SettingsDto>> GetAsync(ISettingsService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAsync(cancellationToken));

    public static async Task<Results<Ok<SettingsSaveResult>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> UpdateAsync(
        SettingsRequest body, ISettingsService service, CancellationToken cancellationToken) =>
        (await service.UpdateAsync(new SettingsInput(body.ExcludedFolderNames, body.ExcludedExtensions, body.IncludedExtensions), cancellationToken)).ToOk();
}
```

Register it: in `AddApplication` add `services.AddScoped<ISettingsService, SettingsService>();` (with
`using PictureManager.Application.Settings;`). In `Program.cs`, after `admin.MapRootEndpoints();` add
`admin.MapSettingsEndpoints();`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~Settings"`
Expected: PASS, 22 tests (SettingsNormalizerTests 11, SettingsServiceTests 11), 0 failed.

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~AppSettingsPostgresTests"`
Expected: PASS, 1 test.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: add settings get/put with normalization and prune-on-next-scan flag"
```

---

## Task 14: Scanner: skip inactive roots and tombstones, prune newly excluded items

**Files:**
- Create: `src/PictureManager.Application/Scanning/ScanRootUnavailableException.cs`
- Modify: `src/PictureManager.Application/Scanning/ScanService.cs` (`StartScanAsync` root selection; the
  directory branch and the end-of-folder pass in `ScanRootAsync`)
- Modify: `src/PictureManager.Api/Endpoints/ScanEndpoints.cs` (`StartScanAsync`)
- Test: `tests/PictureManager.Application.Tests/Scanning/ScanServicePhase5Tests.cs`
- Test: `tests/PictureManager.Api.Tests/Endpoints/ScanEndpointsTests.cs` (add one test)

**Interfaces:**
- Consumes: `IFolderRepository.DeleteSubtreeAsync` (Task 7); the existing `IFolderRepository.GetChildrenAsync`,
  `IImageRepository.DeleteAsync` and `ScanExcludeRules`.
- Produces: `ScanRootUnavailableException(int rootId)` (with property `int RootId`), thrown by
  `IScanService.StartScanAsync` when an explicit `rootId` is unknown or inactive. `POST /api/scans` maps it to `400`.

- [ ] **Step 1: Write the failing scanner tests**

Create `tests/PictureManager.Application.Tests/Scanning/ScanServicePhase5Tests.cs`:

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

public sealed class ScanServicePhase5Tests : IDisposable
{
    private readonly DirectoryInfo _tempRoot = Directory.CreateTempSubdirectory("pm-scan-p5-");
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly IImageRepository _images = Substitute.For<IImageRepository>();
    private readonly IAppSettingsRepository _settings = Substitute.For<IAppSettingsRepository>();
    private readonly IScanJobRepository _jobs = Substitute.For<IScanJobRepository>();
    private readonly IEnrichmentQueue _queue = Substitute.For<IEnrichmentQueue>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly Folder _rootFolder = new() { Id = 10, RootId = 1, RelativePath = string.Empty, Name = "dev" };

    public ScanServicePhase5Tests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _roots.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ImageRoot { Id = 1, Name = "dev", MountPath = _tempRoot.FullName, IsActive = true });
        _folders.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>()).Returns(_rootFolder);
        _folders.GetChildrenAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(new List<Folder>());
        _images.GetByFolderIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<Image>());
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings());
        _jobs.AddAsync(Arg.Any<ScanJob>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var job = call.Arg<ScanJob>();
            job.Id = 999;
            return job;
        });
        _jobs.TryTransitionToEnrichingAsync(999, Arg.Any<CancellationToken>()).Returns(true);
    }

    public void Dispose() => _tempRoot.Delete(recursive: true);

    private ScanService CreateService() => new(_roots, _folders, _images, _settings, _jobs, _queue, _clock);

    [Fact]
    public async Task StartScanAsync_ExplicitInactiveRoot_Throws_AndCreatesNoJob()
    {
        _roots.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(new ImageRoot { Id = 2, Name = "off", MountPath = "/x", IsActive = false });

        var act = () => CreateService().StartScanAsync(rootId: 2, isRecursive: true);

        (await act.Should().ThrowAsync<ScanRootUnavailableException>()).Which.RootId.Should().Be(2);
        await _jobs.DidNotReceive().AddAsync(Arg.Any<ScanJob>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_ExplicitUnknownRoot_Throws()
    {
        _roots.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns((ImageRoot?)null);

        var act = () => CreateService().StartScanAsync(rootId: 3, isRecursive: true);

        await act.Should().ThrowAsync<ScanRootUnavailableException>();
    }

    [Fact]
    public async Task StartScanAsync_TombstonedChildFolder_IsNotDescendedIntoOrReindexed()
    {
        var removedDir = Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "removed"));
        await File.WriteAllBytesAsync(Path.Combine(removedDir.FullName, "a.jpg"), new byte[] { 1 });
        _folders.GetByRootAndRelativePathAsync(1, "removed", Arg.Any<CancellationToken>())
            .Returns(new Folder { Id = 20, RootId = 1, ParentId = 10, RelativePath = "removed", Name = "removed", IsActive = false });

        await CreateService().StartScanAsync(rootId: 1, isRecursive: true);

        await _images.DidNotReceive().GetByFolderAndFileNameAsync(20, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _images.DidNotReceive().AddAsync(Arg.Any<Image>(), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetEnumerationResultAsync(999, foldersScanned: 1, filesFound: 0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_ExistingImageWithNowExcludedExtension_IsDeleted_NotMarkedMissing()
    {
        await File.WriteAllBytesAsync(Path.Combine(_tempRoot.FullName, "x.heic"), new byte[] { 1 });
        var heic = new Image { Id = 5, FolderId = 10, FileName = "x", Extension = ".heic" };
        var goneJpg = new Image { Id = 6, FolderId = 10, FileName = "gone", Extension = ".jpg" };
        _images.GetByFolderIdAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Image> { heic, goneJpg });
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings { ExcludedExtensions = new List<string> { ".HEIC" } });

        await CreateService().StartScanAsync(rootId: 1, isRecursive: true);

        await _images.Received(1).DeleteAsync(heic, Arg.Any<CancellationToken>());
        await _images.DidNotReceive().UpdateAsync(Arg.Is<Image>(i => i.Id == 5), Arg.Any<CancellationToken>());
        // A still-allowed image that vanished from disk keeps the old behaviour: marked missing, not deleted.
        await _images.Received(1).UpdateAsync(Arg.Is<Image>(i => i.Id == 6 && i.MissingSinceUtc != null), Arg.Any<CancellationToken>());
        await _images.DidNotReceive().DeleteAsync(goneJpg, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_ExistingChildFolderWithNowExcludedName_IsDeletedWithSubtree()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Raw"));
        _folders.GetChildrenAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Folder>
        {
            new() { Id = 30, RootId = 1, ParentId = 10, Name = "Raw", RelativePath = "Raw" },
            new() { Id = 31, RootId = 1, ParentId = 10, Name = "keep", RelativePath = "keep" }
        });
        _folders.GetByRootAndRelativePathAsync(1, "keep", Arg.Any<CancellationToken>())
            .Returns(new Folder { Id = 31, RootId = 1, ParentId = 10, Name = "keep", RelativePath = "keep" });
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings { ExcludedFolderNames = new List<string> { "raw" } });

        await CreateService().StartScanAsync(rootId: 1, isRecursive: true);

        await _folders.Received(1).DeleteSubtreeAsync(30, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().DeleteSubtreeAsync(31, Arg.Any<CancellationToken>());
    }
}
```

Add to `tests/PictureManager.Api.Tests/Endpoints/ScanEndpointsTests.cs`:

```csharp
    [Fact]
    public async Task StartScanAsync_UnavailableRoot_ReturnsValidationProblem()
    {
        var scanService = Substitute.For<IScanService>();
        scanService.StartScanAsync(2, true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new ScanRootUnavailableException(2)));

        var result = await ScanEndpoints.StartScanAsync(new ScanRequest(2, true), scanService, CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ValidationProblem>();
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet build`
Expected: build FAILS (`ScanRootUnavailableException` not found).

- [ ] **Step 3: Implement**

`src/PictureManager.Application/Scanning/ScanRootUnavailableException.cs`:

```csharp
using System;

namespace PictureManager.Application.Scanning;

/// <summary>An explicitly requested scan root doesn't exist or is inactive (inactive roots are never scanned).</summary>
public sealed class ScanRootUnavailableException : Exception
{
    public ScanRootUnavailableException(int rootId)
        : base($"Image root {rootId} does not exist or is inactive.")
    {
        RootId = rootId;
    }

    public int RootId { get; }
}
```

In `ScanService.StartScanAsync` replace

```csharp
        var roots = rootId.HasValue
            ? new[] { await _imageRootRepository.GetByIdAsync(rootId.Value, cancellationToken)
                ?? throw new InvalidOperationException($"ImageRoot {rootId} not found.") }
            : (await _imageRootRepository.GetAllAsync(cancellationToken)).Where(r => r.IsActive).ToArray();
```

with

```csharp
        var roots = rootId.HasValue
            ? new[] { await GetActiveRootAsync(rootId.Value, cancellationToken) }
            : (await _imageRootRepository.GetAllAsync(cancellationToken)).Where(r => r.IsActive).ToArray();
```

and add the helper method to the class:

```csharp
    private async Task<ImageRoot> GetActiveRootAsync(int rootId, CancellationToken cancellationToken)
    {
        var root = await _imageRootRepository.GetByIdAsync(rootId, cancellationToken);
        return root is { IsActive: true } ? root : throw new ScanRootUnavailableException(rootId);
    }
```

In `ScanRootAsync`, the directory branch becomes:

```csharp
                if (Directory.Exists(entryPath))
                {
                    // Excluded names are skipped here; rows that already exist for them are pruned
                    // after this folder's pass (below).
                    if (excludeRules.IsFolderExcluded(name))
                        continue;

                    var childRelativePath = PathNormalizer.Combine(folder.RelativePath, name);
                    var childFolder = await GetOrCreateFolderAsync(root.Id, folder.Id, childRelativePath, name, cancellationToken);

                    // Removed from the collection (tombstone): never descend into it or re-index it.
                    if (!childFolder.IsActive)
                        continue;

                    // Always create the child Folder row for tree visibility, but stop descending
                    // once isRecursive is false, or once a depth cap is hit (guards against unbounded
                    // growth from a symlink/junction cycle).
                    if (isRecursive && depth < MaxScanDepth)
                        pending.Enqueue((childFolder, entryPath, depth + 1));
                }
```

and the end-of-folder pass becomes:

```csharp
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

            // Prune on scan: child folders whose name is now excluded go with their whole subtree.
            // No tombstone is left, because the exclusion rule itself keeps them out, and removing
            // the rule brings them back on the next scan.
            foreach (var child in await _folderRepository.GetChildrenAsync(folder.Id, cancellationToken))
            {
                if (excludeRules.IsFolderExcluded(child.Name))
                    await _folderRepository.DeleteSubtreeAsync(child.Id, cancellationToken);
            }
```

In `ScanEndpoints.StartScanAsync` add a second catch after the `ScanAlreadyInProgressException` one:

```csharp
        catch (ScanRootUnavailableException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["rootId"] = new[] { ex.Message } });
        }
```

(and `using System.Collections.Generic;` at the top of `ScanEndpoints.cs`, if it's not already there).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~Scanning"`
Expected: PASS, 0 failed. This includes the 5 new tests, and every pre-existing `ScanServiceTests` test
still passes, including `..._ReconcilesMixOfFileStatesAndExcludesExtension` (an excluded file that has
no row still never touches the repository).

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~ScanEndpointsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat: scanner skips inactive roots and removed folders, prunes newly excluded items"
```

---

## Task 15: HTTP smoke tests, surface-membership check, ProblemDetails

**Files:**
- Modify: `tests/PictureManager.Api.Tests/PictureManager.Api.Tests.csproj` (add Mvc.Testing; link the
  Postgres test-support files)
- Create: `tests/PictureManager.Api.Tests/Smoke/ApiSmokeFixture.cs`
- Create: `tests/PictureManager.Api.Tests/Smoke/ApiSmokeTests.cs`
- Modify: `src/PictureManager.Api/Program.cs` (`AddProblemDetails`, `UseExceptionHandler` outside
  Development, `public partial class Program;`)

**Interfaces:**
- Consumes: every endpoint group from Tasks 6–14; `ApiSurfaceMetadata` (Task 3);
  `PostgresTestDatabase`/`TestData` (Tasks 1–2).
- Produces: `public partial class Program`, visible to `WebApplicationFactory<Program>`.

- [ ] **Step 1: Wire the test project**

Run: `dotnet add tests/PictureManager.Api.Tests package Microsoft.AspNetCore.Mvc.Testing`
Expected: package added. Note the version for the commit message.

Add to `tests/PictureManager.Api.Tests/PictureManager.Api.Tests.csproj`:

```xml
  <ItemGroup>
    <!-- Shared real-Postgres fixture; its per-assembly template name keeps the two test assemblies apart. -->
    <Compile Include="..\PictureManager.Infrastructure.Tests\Support\PostgresTestDatabase.cs" Link="Support\PostgresTestDatabase.cs" />
    <Compile Include="..\PictureManager.Infrastructure.Tests\Support\TestData.cs" Link="Support\TestData.cs" />
  </ItemGroup>
```

- [ ] **Step 2: Write the fixture and the failing smoke tests**

`tests/PictureManager.Api.Tests/Smoke/ApiSmokeFixture.cs`:

```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Api.Tests.Smoke;

/// <summary>
/// One real host per test run, against a throwaway Postgres database. It is created once because
/// Program's Serilog bootstrap logger can only be frozen once per process.
/// </summary>
public sealed class ApiSmokeFixture : IAsyncLifetime
{
    private const string ConnectionStringVariable = "ConnectionStrings__PictureManagerDb";
    private const string CacheRootVariable = "ThumbnailCache__RootPath";

    public PostgresTestDatabase Database { get; private set; } = null!;
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Database = await PostgresTestDatabase.CreateAsync();

        // Program.cs reads the connection string while it builds the host, before
        // WebApplicationFactory's configuration callbacks run, so hand settings over through the
        // environment instead of UseSetting/ConfigureAppConfiguration.
        Environment.SetEnvironmentVariable(ConnectionStringVariable, Database.ConnectionString);
        Environment.SetEnvironmentVariable(CacheRootVariable, Path.Combine(Path.GetTempPath(), "pm-smoke-cache-" + Guid.NewGuid().ToString("N")));

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        Client = Factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        Environment.SetEnvironmentVariable(ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(CacheRootVariable, null);
        await Database.DisposeAsync();
    }
}
```

`tests/PictureManager.Api.Tests/Smoke/ApiSmokeTests.cs`:

```csharp
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Api.Endpoints;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Api.Tests.Smoke;

public class ApiSmokeTests : IClassFixture<ApiSmokeFixture>
{
    private readonly ApiSmokeFixture _fixture;

    public ApiSmokeTests(ApiSmokeFixture fixture)
    {
        _fixture = fixture;
    }

    private RouteEndpoint[] ApiEndpoints() =>
        _fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api/")
                        && e.RoutePattern.RawText is not ("/api/health" or "/api/ping"))
            .ToArray();

    [Fact]
    public void EveryApiEndpoint_BelongsToExactlyOneSurface()
    {
        var endpoints = ApiEndpoints();

        endpoints.Should().NotBeEmpty();
        foreach (var endpoint in endpoints)
            endpoint.Metadata.GetOrderedMetadata<ApiSurfaceMetadata>().Should().HaveCount(1, endpoint.DisplayName);
    }

    [Theory]
    [InlineData("GET", "/api/roots", ApiSurface.Admin)]
    [InlineData("PATCH", "/api/roots/{id:int}", ApiSurface.Admin)]
    [InlineData("GET", "/api/settings", ApiSurface.Admin)]
    [InlineData("PUT", "/api/settings", ApiSurface.Admin)]
    [InlineData("POST", "/api/scans", ApiSurface.Admin)]
    [InlineData("DELETE", "/api/folders/{id:int}", ApiSurface.Admin)]
    [InlineData("GET", "/api/folders/removed", ApiSurface.Admin)]
    [InlineData("POST", "/api/folders/{id:int}/restore", ApiSurface.Admin)]
    [InlineData("GET", "/api/images", ApiSurface.User)]
    [InlineData("PUT", "/api/images/{id:int}/favorite", ApiSurface.User)]
    [InlineData("GET", "/api/images/{id:int}/thumbnail", ApiSurface.User)]
    [InlineData("GET", "/api/albums", ApiSurface.User)]
    [InlineData("GET", "/api/albums/{id:int}/export", ApiSurface.User)]
    [InlineData("GET", "/api/duplicates", ApiSurface.User)]
    [InlineData("GET", "/api/folders/roots", ApiSurface.User)]
    public void Endpoint_IsOnTheExpectedSurface(string method, string route, ApiSurface expected)
    {
        var endpoint = ApiEndpoints().Single(e =>
            e.RoutePattern.RawText == route
            && e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Contains(method));

        endpoint.Metadata.GetMetadata<ApiSurfaceMetadata>()!.Surface.Should().Be(expected);
    }

    [Fact]
    public async Task BrowseListFavoriteAndAlbum_RoundTripOverHttp()
    {
        int tripFolderId;
        int[] imageIds;
        await using (var context = _fixture.Database.CreateContext())
        {
            var root = TestData.Root("smoke-browse");
            var top = TestData.Folder(root, "");
            var trip = TestData.Folder(root, "Trip", top);
            var images = new[] { "a", "b", "c" }.Select(n => TestData.Image(trip, n)).ToArray();
            context.Images.AddRange(images);
            await context.SaveChangesAsync();
            tripFolderId = trip.Id;
            imageIds = images.Select(i => i.Id).ToArray();
        }

        var client = _fixture.Client;

        var roots = await client.GetFromJsonAsync<JsonElement>("/api/folders/roots");
        var smokeRoot = roots.EnumerateArray().Single(n => n.GetProperty("name").GetString() == "smoke-browse");
        var children = await client.GetFromJsonAsync<JsonElement>($"/api/folders/{smokeRoot.GetProperty("id").GetInt32()}/children");
        children[0].GetProperty("imageCount").GetInt32().Should().Be(3);

        var firstPage = await client.GetFromJsonAsync<JsonElement>($"/api/images?folderId={tripFolderId}&sort=name&limit=2");
        firstPage.GetProperty("items").GetArrayLength().Should().Be(2);
        var cursor = firstPage.GetProperty("nextCursor").GetString();
        var secondPage = await client.GetFromJsonAsync<JsonElement>($"/api/images?folderId={tripFolderId}&sort=name&limit=2&cursor={cursor}");
        secondPage.GetProperty("items").GetArrayLength().Should().Be(1);
        secondPage.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);

        (await client.PutAsync($"/api/images/{imageIds[0]}/favorite", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var favorites = await client.GetFromJsonAsync<JsonElement>($"/api/images?folderId={tripFolderId}&favoritesOnly=true");
        favorites.GetProperty("items").GetArrayLength().Should().Be(1);

        var created = await client.PostAsJsonAsync("/api/albums", new { name = "Smoke album" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var albumId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var added = await client.PostAsJsonAsync($"/api/albums/{albumId}/images", new { folderId = tripFolderId });
        (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("added").GetInt32().Should().Be(3);

        var export = await client.GetStringAsync($"/api/albums/{albumId}/export?prefix=/mnt");
        export.Should().Be("/mnt/smoke-browse/Trip/a.jpg\n/mnt/smoke-browse/Trip/b.jpg\n/mnt/smoke-browse/Trip/c.jpg\n");
    }

    [Fact]
    public async Task Export_NonAsciiAlbumName_SetsRfc5987FileName()
    {
        var client = _fixture.Client;
        var created = await client.PostAsJsonAsync("/api/albums", new { name = "Nyaralás 2025" });
        var albumId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var response = await client.GetAsync($"/api/albums/{albumId}/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.ToString().Should().Be("text/plain; charset=utf-8");
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("Nyaralás 2025.txt");
    }

    [Fact]
    public async Task BadCursor_Returns400ProblemJson_AndUnknownImage_Returns404()
    {
        var bad = await _fixture.Client.GetAsync("/api/images?cursor=not-a-cursor");
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        bad.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        (await _fixture.Client.GetAsync("/api/images/999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DuplicateAlbumName_Returns409()
    {
        await _fixture.Client.PostAsJsonAsync("/api/albums", new { name = "Twice" });

        var second = await _fixture.Client.PostAsJsonAsync("/api/albums", new { name = "TWICE" });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~ApiSmokeTests"`
Expected: FAILS. Either a build error (`Program` is inaccessible) or the host fails to start: fix it in
Step 4 either way.

- [ ] **Step 4: Finish `Program.cs`**

1. After `builder.Services.AddWorker();` add:

```csharp
    builder.Services.AddProblemDetails();
```

2. Directly after `var app = builder.Build();` add:

```csharp
    // Outside Development, unhandled exceptions become a 500 ProblemDetails with no stack trace.
    if (!app.Environment.IsDevelopment())
        app.UseExceptionHandler();
```

3. At the very end of the file (after the `try/catch/finally`) add:

```csharp
// Exposes the top-level-statement entry point to WebApplicationFactory<Program> in the tests.
public partial class Program;
```

- [ ] **Step 5: Run the smoke tests and the full suite**

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~ApiSmokeTests"`
Expected: PASS, 20 tests (5 facts + 15 theory rows), 0 failed.

Run: `dotnet test`
Expected: PASS, 0 failed across all four test projects.

- [ ] **Step 6: Commit**

```bash
git add src/PictureManager.Api tests/PictureManager.Api.Tests
git commit -m "test: add WebApplicationFactory smoke tests for routing, surfaces and HTTP round trips; add ProblemDetails"
```

---

## Task 16: Live verification against the dev root (controller-performed)

This needs a real running process, real files and a scan, so the controller does it directly, as in
phase 4's Task 4 Step 9. Record every result (the command and what came back) in the task report.

- [ ] **Step 1: Prepare data**

1. Make sure `db` is up and migrated:
   `dotnet ef database update --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api`
   Expected: `No migrations were applied. The database is already up to date.` (or the Phase5RestApi
   migration applies).
2. Generate test photos into `dev-data/images/` (the folder is gitignored except `.gitkeep`):

```bash
python - <<'EOF'
from PIL import Image
import os, shutil
base = "dev-data/images"
os.makedirs(f"{base}/Holidays/Madeira", exist_ok=True)
os.makedirs(f"{base}/raw", exist_ok=True)
def jpg(path, color, taken=None):
    img = Image.new("RGB", (1200, 800), color)
    exif = img.getexif()
    if taken:
        exif.get_ifd(0x8769)[0x9003] = taken  # Exif SubIFD DateTimeOriginal
    img.save(path, quality=85, exif=exif)
jpg(f"{base}/Holidays/Madeira/IMG_0001.jpg", "red", "2025:08:14 18:32:05")
jpg(f"{base}/Holidays/Madeira/IMG_0002.jpg", "green", "2025:08:15 09:00:00")
jpg(f"{base}/Holidays/Madeira/IMG_0003.jpg", "blue")
shutil.copyfile(f"{base}/Holidays/Madeira/IMG_0001.jpg", f"{base}/Holidays/IMG_0001 copy.jpg")
Image.new("RGB", (400, 300), "yellow").save(f"{base}/Holidays/screenshot.png")
jpg(f"{base}/raw/ignored.jpg", "black")
EOF
```

- [ ] **Step 2: Start the app and scan**

From `src/PictureManager.Api`: `ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5199`
(in the background). Then:
- `curl -s localhost:5199/api/roots`. Note the `dev` root's `id`, and check `exportSegment` is `"dev"`.
- `curl -s -X POST localhost:5199/api/scans -H 'Content-Type: application/json' -d '{"rootId":<id>,"isRecursive":true}'`
- `curl -sN localhost:5199/api/scans/<scanJobId>/events`. Wait until the status is `Completed`.

- [ ] **Step 3: Verify, one check at a time**

| # | Request | Expected |
|---|---|---|
| 1 | `GET /api/folders/roots` | the `dev` node, `hasChildren: true` |
| 2 | `GET /api/folders/{dev top}/children` | `Holidays`, and no `raw` (it's in the default excluded names) |
| 3 | `GET /api/folders/{Holidays}/children` | `Madeira` with `imageCount: 3` |
| 4 | `GET /api/images?folderId={Madeira}&limit=2` | 2 items newest-first: `IMG_0003` (no EXIF date, so it sorts by today's file mtime), then `IMG_0002`; versioned URLs; `nextCursor` set. Following the cursor gives `IMG_0001` and `nextCursor: null` |
| 5 | `GET /api/images?folderId={Madeira}&sort=name` | `IMG_0001`, `IMG_0002`, `IMG_0003` |
| 6 | `GET /api/images?fileName=copy` | the single `IMG_0001 copy` |
| 7 | `GET <thumbnailUrl of IMG_0001>` | `200 image/webp`, `Cache-Control: private, max-age=31536000, immutable` |
| 8 | `GET /api/images/{IMG_0001}` | `dateTaken: "2025-08-14T18:32:05"` (no offset), `folderPath: "dev/Holidays/Madeira"`, `rawMetadata` object, `albums: []` |
| 9 | `PUT /api/images/{IMG_0003}/favorite`, then `GET /api/images?favoritesOnly=true` | `204`, then exactly `IMG_0003` |
| 10 | `GET /api/duplicates` | one group of 2: `IMG_0001` in `dev/Holidays/Madeira` and `IMG_0001 copy` in `dev/Holidays` |
| 11 | `POST /api/albums {"name":"Madeira best"}` | `201`, `Location: /api/albums/{id}` |
| 12 | `POST /api/albums/{id}/images {"folderId":{Madeira}}` | `{"added":3,"skipped":0}`; repeating it gives `{"added":0,"skipped":3}` |
| 13 | `POST /api/albums/{id}/images/{IMG_0003}/move {"afterImageId":null}` then `GET /api/albums/{id}/images` | `IMG_0003` first |
| 14 | `PATCH /api/roots/{dev} {"alias":"dev_photos"}`, then `GET /api/albums/{id}/export?prefix=/mnt` | lines `/mnt/dev_photos/Holidays/Madeira/IMG_0003.jpg` … with `\n` endings; header `Content-Disposition` names `Madeira best.txt` |
| 15 | `GET /api/images/{IMG_0001}` | `albums: [{"id":…,"name":"Madeira best"}]` |
| 16 | `PUT /api/settings {"excludedFolderNames":["raw","backup","@eaDir"],"excludedExtensions":[".heic","PNG"],"includedExtensions":null}` | `excludedExtensions: [".heic",".png"]`, `pruneOnNextScan: true`; rescan (Step 2), then `GET /api/images?fileName=screenshot` → `items: []`, and the row is gone from `Images` (check with `psql`) |
| 17 | `DELETE /api/folders/{Madeira}` | `204`; `GET /api/folders/{Holidays}/children` no longer lists it; `GET /api/folders/removed` lists it; `GET /api/albums/{id}/images` → empty (entries purged) |
| 18 | rescan | `Madeira` stays removed (the tombstone is respected) |
| 19 | `POST /api/folders/{Madeira}/restore`, then rescan | `Madeira` is back with `imageCount: 3` (new image ids) |
| 20 | `PATCH /api/roots/{dev} {"isActive":false}` | `GET /api/folders/roots` → `[]`; `POST /api/scans {"rootId":…}` → `400` |
| 21 | `DELETE /api/folders/{dev top}` | `400` (a root's top folder can't be removed) |
| 22 | `GET /api/images?cursor=garbage` / `GET /api/images/999999` | `400` problem+json / `404` |

- [ ] **Step 4: Restore the dev state**

- `PATCH /api/roots/{dev} {"isActive":true,"alias":null}`
- `PUT /api/settings {"excludedFolderNames":["raw","backup","@eaDir"],"excludedExtensions":[".heic"],"includedExtensions":null}`
- Stop the app. Leave the generated photos in `dev-data/images/` (gitignored) for phase 6.
- Record the results in the task report.

---
