# Phase 2 — Data Model & Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give PictureManager a real, migration-backed Postgres schema — all 8 entities from the brief plus
this phase's two schema additions (`ImageRoot`, and the design spec's `ScanJob`/`AppSettings`), EF Core
configurations that encode the brief's relational rules (cascade deletes, timestamp semantics, jsonb),
one generated initial migration, the seeded placeholder `AppUser`, and repository interfaces/implementations
for the five aggregates later phases will actually read and write through in this phase (`Folder`, `Image`,
`Album`, `AppUser`, `ImageRoot`).

**Architecture:** Plain POCO entities in `PictureManager.Model` (no EF attributes, per the brief).
`IEntityTypeConfiguration<T>` classes in `PictureManager.Infrastructure` encode every mapping decision,
applied via `ApplyConfigurationsFromAssembly` — `PictureManagerDbContext` itself stays a thin composition
of `DbSet`s. Repository interfaces live in `PictureManager.Application` (EF-agnostic); their EF Core
implementations live in `PictureManager.Infrastructure` and are registered in `AddInfrastructure`.
`ScanJob` and `AppSettings` get entities + configurations now (the schema is a unit — one migration should
carry the whole thing), but not repositories yet: nothing consumes them until phase 3 (scanner) and
phase 5 (settings API) respectively, so building their repositories now would be speculative.

**Tech Stack:** EF Core 10 + Npgsql (already wired in phase 1), `Microsoft.EntityFrameworkCore.InMemory`
(new, test-only) for schema/repository verification without a live database, xUnit + FluentAssertions.

**Spec:** [`docs/superpowers/specs/2026-09-21-picturemanager-v1-design.md`](../specs/2026-09-21-picturemanager-v1-design.md)
(data model, `ImageRoot`/`ScanJob`/`AppSettings` decisions), and the source brief's Data Model, Folder
management, Albums, and "Known gotchas" sections in
[`Documents/PictureManager-brief.md`](../../../Documents/PictureManager-brief.md).

## Global Constraints

- Target framework: `net10.0` everywhere on the backend (unchanged from phase 1).
- **No live database in this phase.** The `db` container from phase 1 stays stopped throughout — migrations
  are generated and reviewed (`dotnet ef migrations add` builds the model at design time, it doesn't need a
  connection), and all tests run against EF Core's InMemory provider. Phase 3 is the first phase to run
  `dotnet ef database update` against a live Postgres.
- **FluentAssertions pinned to `[7.0.0,8.0.0)`**, **NSubstitute** for mocking (not Moq) — unchanged global
  constraints from phase 1, still binding.
- **Timestamp columns**: every `*Utc`/`*At` column is a real UTC instant → `HasColumnType("timestamp with
  time zone")`. The one exception is `Image.DateTaken` (EXIF `DateTimeOriginal` has no timezone) →
  `HasColumnType("timestamp without time zone")`, stored local-naive, per the brief's timezone gotcha.
- **`Folder.RootId` is denormalized onto every `Folder` row** (top-level and nested alike), not just
  top-level folders — set once at creation by copying the parent's `RootId`. See the design spec's
  `ImageRoot` decision for why.
- **`RawMetadata`** (`Image`) and the two extension-list columns (`AppSettings`) map to Postgres `jsonb`
  via `HasColumnType("jsonb")`.
- **Cascade rule** (brief: "if a folder is deleted, all images from folder and sub-folders are deleted from
  database (images, albumimages)"): `Folder→Folder` (parent/children) cascade, `Folder→Image` cascade,
  `Image→AlbumImage` cascade, `Album→AlbumImage` cascade. `ImageRoot→Folder` and `AppUser→Album` are
  `Restrict` (not exercised by any user-facing flow in v1, but should never silently cascade).
- **Placeholder `AppUser`**: seeded via `HasData` with fixed `Id = 1`, `DisplayName = "System"`,
  `Role = UserRole.User`, `ZitadelSubjectId = null`. Every `Album.OwnerUserId` created in v1 points at this
  row (nothing in this phase creates albums yet — that's phase 5 — but the seed must exist now so later
  phases have a real FK target).
- **No uniqueness constraints tied to path normalization yet** (e.g. a case-insensitive unique index on
  `Folder.RelativePath`) — phase 3 owns path normalization (NFC, case-insensitive compare) and will add the
  correct constraint once that logic exists. Adding one now with the wrong (case-sensitive) semantics would
  just mean redesigning it in phase 3. Plain non-unique indexes are added now for query performance.

---

## Task 1: Model entities and enums

**Files:**
- Create: `src/PictureManager.Model/IndexState.cs`
- Create: `src/PictureManager.Model/UserRole.cs`
- Create: `src/PictureManager.Model/ScanJobStatus.cs`
- Create: `src/PictureManager.Model/ImageRoot.cs`
- Create: `src/PictureManager.Model/Folder.cs`
- Create: `src/PictureManager.Model/Image.cs`
- Create: `src/PictureManager.Model/Album.cs`
- Create: `src/PictureManager.Model/AlbumImage.cs`
- Create: `src/PictureManager.Model/AppUser.cs`
- Create: `src/PictureManager.Model/ScanJob.cs`
- Create: `src/PictureManager.Model/AppSettings.cs`

**Interfaces:**
- Produces: every type Task 2 (configurations), Task 3 (migration), and Task 4 (repositories) depend on.
  Exact property names/types below are load-bearing — later tasks reference them verbatim.

No tests in this task: these are plain data-holder POCOs with no logic (auto-properties only). Testing a
getter/setter would be a vacuous test — the model's correctness is verified by Task 2/3's configuration and
migration-generation steps, and Task 4's repository tests exercising real persistence behavior.

- [ ] **Step 1: Create the three enums**

`src/PictureManager.Model/IndexState.cs`:

```csharp
namespace PictureManager.Model;

public enum IndexState
{
    Pending,
    Indexed
}
```

`src/PictureManager.Model/UserRole.cs`:

```csharp
namespace PictureManager.Model;

public enum UserRole
{
    User,
    Admin
}
```

`src/PictureManager.Model/ScanJobStatus.cs`:

```csharp
namespace PictureManager.Model;

public enum ScanJobStatus
{
    Pending,
    Enumerating,
    Enriching,
    Completed,
    Failed,
    Cancelled
}
```

- [ ] **Step 2: Create `ImageRoot`**

`src/PictureManager.Model/ImageRoot.cs`:

```csharp
using System;

namespace PictureManager.Model;

public class ImageRoot
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MountPath { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
}
```

- [ ] **Step 3: Create `Folder`**

`src/PictureManager.Model/Folder.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace PictureManager.Model;

public class Folder
{
    public int Id { get; set; }
    public int RootId { get; set; }
    public int? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }

    public ImageRoot? Root { get; set; }
    public Folder? Parent { get; set; }
    public ICollection<Folder> Children { get; set; } = new List<Folder>();
    public ICollection<Image> Images { get; set; } = new List<Image>();
}
```

- [ ] **Step 4: Create `Image`**

`src/PictureManager.Model/Image.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace PictureManager.Model;

public class Image
{
    public int Id { get; set; }
    public int FolderId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string? PerceptualHash { get; set; }
    public long FileSize { get; set; }
    public DateTime FileModified { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? Orientation { get; set; }

    // Local-naive: EXIF DateTimeOriginal carries no timezone. See Global Constraints.
    public DateTime? DateTaken { get; set; }

    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }
    public string? LensModel { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? RawMetadata { get; set; }
    public bool IsFavorite { get; set; }
    public IndexState IndexState { get; set; } = IndexState.Pending;
    public DateTime FirstSeenUtc { get; set; }
    public DateTime? MissingSinceUtc { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Folder? Folder { get; set; }
    public ICollection<AlbumImage> AlbumImages { get; set; } = new List<AlbumImage>();
}
```

- [ ] **Step 5: Create `Album` and `AlbumImage`**

`src/PictureManager.Model/Album.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace PictureManager.Model;

public class Album
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int OwnerUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public AppUser? OwnerUser { get; set; }
    public ICollection<AlbumImage> AlbumImages { get; set; } = new List<AlbumImage>();
}
```

`src/PictureManager.Model/AlbumImage.cs`:

```csharp
using System;

namespace PictureManager.Model;

public class AlbumImage
{
    public int AlbumId { get; set; }
    public int ImageId { get; set; }
    public int SortOrder { get; set; }
    public DateTime AddedAt { get; set; }

    public Album? Album { get; set; }
    public Image? Image { get; set; }
}
```

- [ ] **Step 6: Create `AppUser`**

`src/PictureManager.Model/AppUser.cs`:

```csharp
using System.Collections.Generic;

namespace PictureManager.Model;

public class AppUser
{
    public int Id { get; set; }
    public string? ZitadelSubjectId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;

    public ICollection<Album> Albums { get; set; } = new List<Album>();
}
```

- [ ] **Step 7: Create `ScanJob`**

`src/PictureManager.Model/ScanJob.cs`:

```csharp
using System;

namespace PictureManager.Model;

public class ScanJob
{
    public int Id { get; set; }
    public int? RootFolderId { get; set; }
    public bool IsRecursive { get; set; }
    public ScanJobStatus Status { get; set; } = ScanJobStatus.Pending;
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public int FoldersScanned { get; set; }
    public int FilesFound { get; set; }
    public int FilesEnriched { get; set; }
    public string? ErrorMessage { get; set; }

    public Folder? RootFolder { get; set; }
}
```

- [ ] **Step 8: Create `AppSettings`**

`src/PictureManager.Model/AppSettings.cs`:

```csharp
using System.Collections.Generic;

namespace PictureManager.Model;

public class AppSettings
{
    public int Id { get; set; }
    public List<string> ExcludedFolderNames { get; set; } = new();
    public List<string> ExcludedExtensions { get; set; } = new();
    public List<string>? IncludedExtensions { get; set; }
}
```

- [ ] **Step 9: Build**

Run: `dotnet build src/PictureManager.Model`
Expected: Build succeeds, 0 warnings, 0 errors.

- [ ] **Step 10: Commit**

```bash
git add src/PictureManager.Model
git commit -m "feat: add PictureManager.Model entities and enums"
```

---

## Task 2: EF Core configurations, `DbContext` wiring, and seed data

**Files:**
- Create: `src/PictureManager.Infrastructure/Persistence/Configurations/ImageRootConfiguration.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Configurations/FolderConfiguration.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Configurations/ImageConfiguration.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Configurations/AlbumConfiguration.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Configurations/AlbumImageConfiguration.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Configurations/AppUserConfiguration.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Configurations/ScanJobConfiguration.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Configurations/AppSettingsConfiguration.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/PictureManagerDbContext.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/ModelSeedDataTests.cs`

**Interfaces:**
- Consumes: every entity from Task 1.
- Produces: `PictureManagerDbContext.ImageRoots/Folders/Images/Albums/AlbumImages/AppUsers/ScanJobs/Settings`
  (all `DbSet<T>`, `Settings` — not `AppSettings` — is the property name for the `AppSettings` `DbSet`, to
  avoid a property/type name collision). `AppUserConfiguration.SystemUserId` (`const int = 1`) and
  `AppSettingsConfiguration.SingletonId` (`const int = 1`) — Task 4 and later phases reference these
  constants instead of the magic number `1`.

- [ ] **Step 1: Add the InMemory test package**

```bash
dotnet add tests/PictureManager.Infrastructure.Tests package Microsoft.EntityFrameworkCore.InMemory
```

- [ ] **Step 2: Write the failing seed-data test**

`tests/PictureManager.Infrastructure.Tests/Persistence/ModelSeedDataTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence;

public class ModelSeedDataTests
{
    private static PictureManagerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PictureManagerDbContext(options);
    }

    [Fact]
    public async Task Database_SeedsSystemAppUserAndDefaultAppSettings()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var user = await context.AppUsers.SingleAsync();
        user.Id.Should().Be(1);
        user.DisplayName.Should().Be("System");
        user.Role.Should().Be(UserRole.User);
        user.ZitadelSubjectId.Should().BeNull();

        var settings = await context.Settings.SingleAsync();
        settings.Id.Should().Be(1);
        settings.ExcludedFolderNames.Should().Contain("raw");
        settings.ExcludedExtensions.Should().Contain(".heic");
    }

    [Fact]
    public void Model_RegistersAllEightEntityTypes()
    {
        using var context = CreateContext();

        context.Model.GetEntityTypes().Select(e => e.ClrType).Should().BeEquivalentTo(new[]
        {
            typeof(ImageRoot), typeof(Folder), typeof(Image), typeof(Album),
            typeof(AlbumImage), typeof(AppUser), typeof(ScanJob), typeof(AppSettings)
        });
    }
}
```

- [ ] **Step 3: Run the test to verify it fails to compile**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter ModelSeedDataTests`
Expected: FAIL — the configuration types don't exist yet, `DbContext` has no `DbSet`s beyond the
already-present empty context, `Settings` property doesn't exist.

- [ ] **Step 4: Create the configurations**

`src/PictureManager.Infrastructure/Persistence/Configurations/ImageRootConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class ImageRootConfiguration : IEntityTypeConfiguration<ImageRoot>
{
    public void Configure(EntityTypeBuilder<ImageRoot> builder)
    {
        builder.ToTable("ImageRoots");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.MountPath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.CreatedUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(x => x.Name)
            .IsUnique();
    }
}
```

`src/PictureManager.Infrastructure/Persistence/Configurations/FolderConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("Folders");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.RelativePath)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(x => x.CreatedUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.ModifiedUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(x => x.Root)
            .WithMany()
            .HasForeignKey(x => x.RootId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.RootId);
        builder.HasIndex(x => x.ParentId);
        builder.HasIndex(x => x.RelativePath);
    }
}
```

`src/PictureManager.Infrastructure/Persistence/Configurations/ImageConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class ImageConfiguration : IEntityTypeConfiguration<Image>
{
    public void Configure(EntityTypeBuilder<Image> builder)
    {
        builder.ToTable("Images");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FileName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.Extension)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.ContentHash)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.PerceptualHash)
            .HasMaxLength(200);

        builder.Property(x => x.CameraMake)
            .HasMaxLength(200);

        builder.Property(x => x.CameraModel)
            .HasMaxLength(200);

        builder.Property(x => x.LensModel)
            .HasMaxLength(200);

        builder.Property(x => x.RawMetadata)
            .HasColumnType("jsonb");

        // EXIF DateTimeOriginal has no timezone - store as local-naive, not a UTC instant.
        builder.Property(x => x.DateTaken)
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.FileModified)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.FirstSeenUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.MissingSinceUtc)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(x => x.Folder)
            .WithMany(x => x.Images)
            .HasForeignKey(x => x.FolderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.FolderId);
        builder.HasIndex(x => x.ContentHash);
        builder.HasIndex(x => x.IsFavorite);
    }
}
```

`src/PictureManager.Infrastructure/Persistence/Configurations/AlbumConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> builder)
    {
        builder.ToTable("Albums");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(x => x.OwnerUser)
            .WithMany(x => x.Albums)
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.OwnerUserId);
    }
}
```

`src/PictureManager.Infrastructure/Persistence/Configurations/AlbumImageConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class AlbumImageConfiguration : IEntityTypeConfiguration<AlbumImage>
{
    public void Configure(EntityTypeBuilder<AlbumImage> builder)
    {
        builder.ToTable("AlbumImages");

        builder.HasKey(x => new { x.AlbumId, x.ImageId });

        builder.Property(x => x.AddedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(x => x.Album)
            .WithMany(x => x.AlbumImages)
            .HasForeignKey(x => x.AlbumId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Image)
            .WithMany(x => x.AlbumImages)
            .HasForeignKey(x => x.ImageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ImageId);
    }
}
```

`src/PictureManager.Infrastructure/Persistence/Configurations/AppUserConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public const int SystemUserId = 1;

    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("AppUsers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ZitadelSubjectId)
            .HasMaxLength(200);

        builder.Property(x => x.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(x => x.ZitadelSubjectId)
            .IsUnique();

        // v1 placeholder owner: every Album points at this single seeded system user until v2 auth lands.
        builder.HasData(new AppUser
        {
            Id = SystemUserId,
            DisplayName = "System",
            Role = UserRole.User,
            ZitadelSubjectId = null
        });
    }
}
```

`src/PictureManager.Infrastructure/Persistence/Configurations/ScanJobConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class ScanJobConfiguration : IEntityTypeConfiguration<ScanJob>
{
    public void Configure(EntityTypeBuilder<ScanJob> builder)
    {
        builder.ToTable("ScanJobs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(4000);

        builder.Property(x => x.StartedUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.CompletedUtc)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(x => x.RootFolder)
            .WithMany()
            .HasForeignKey(x => x.RootFolderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.Status);
    }
}
```

`src/PictureManager.Infrastructure/Persistence/Configurations/AppSettingsConfiguration.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class AppSettingsConfiguration : IEntityTypeConfiguration<AppSettings>
{
    public const int SingletonId = 1;

    public void Configure(EntityTypeBuilder<AppSettings> builder)
    {
        builder.ToTable("AppSettings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExcludedFolderNames)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

        builder.Property(x => x.ExcludedExtensions)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

        builder.Property(x => x.IncludedExtensions)
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null));

        builder.HasData(new AppSettings
        {
            Id = SingletonId,
            ExcludedFolderNames = new List<string> { "raw", "backup", "@eaDir" },
            ExcludedExtensions = new List<string> { ".heic" },
            IncludedExtensions = null
        });
    }
}
```

- [ ] **Step 5: Wire the `DbContext`**

Replace `src/PictureManager.Infrastructure/Persistence/PictureManagerDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence;

public sealed class PictureManagerDbContext : DbContext
{
    public PictureManagerDbContext(DbContextOptions<PictureManagerDbContext> options)
        : base(options)
    {
    }

    public DbSet<ImageRoot> ImageRoots => Set<ImageRoot>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<Image> Images => Set<Image>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<AlbumImage> AlbumImages => Set<AlbumImage>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<ScanJob> ScanJobs => Set<ScanJob>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PictureManagerDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter ModelSeedDataTests`
Expected: PASS, 2 tests.

- [ ] **Step 7: Run the full Infrastructure test suite (regression check)**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests`
Expected: PASS, all tests (the phase 1 DI-registration tests plus these two).

- [ ] **Step 8: Commit**

```bash
git add src/PictureManager.Infrastructure tests/PictureManager.Infrastructure.Tests
git commit -m "feat: add EF Core configurations, DbContext wiring, and seed data"
```

---

## Task 3: Generate the initial migration and verify cascade-delete behavior

**Files:**
- Create: `src/PictureManager.Infrastructure/Migrations/*_InitialCreate.cs` (tool-generated)
- Create: `src/PictureManager.Infrastructure/Migrations/*_InitialCreate.Designer.cs` (tool-generated)
- Create: `src/PictureManager.Infrastructure/Migrations/PictureManagerDbContextModelSnapshot.cs` (tool-generated)
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/CascadeDeleteTests.cs`

**Interfaces:**
- Consumes: the fully-configured model from Task 2.
- Produces: nothing new for later tasks — this is a verification checkpoint. Phase 3 will apply this
  migration against a live database with `dotnet ef database update`.

- [ ] **Step 1: Generate the migration**

```bash
dotnet ef migrations add InitialCreate --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api
```

Expected: succeeds without needing a database connection (migration generation is a design-time model
diff, not a live operation), and creates the three files listed above under
`src/PictureManager.Infrastructure/Migrations/`.

- [ ] **Step 2: Inspect the generated migration against this checklist**

Open the generated `*_InitialCreate.cs` and confirm:
- All 8 tables are created: `ImageRoots`, `Folders`, `Images`, `Albums`, `AlbumImages`, `AppUsers`,
  `ScanJobs`, `AppSettings`.
- `AlbumImages` has a composite primary key on `(AlbumId, ImageId)`, not a surrogate `Id`.
- Foreign keys exist: `Folders.RootId → ImageRoots.Id`, `Folders.ParentId → Folders.Id` (self-referencing),
  `Images.FolderId → Folders.Id`, `Albums.OwnerUserId → AppUsers.Id`, `AlbumImages.AlbumId → Albums.Id`,
  `AlbumImages.ImageId → Images.Id`, `ScanJobs.RootFolderId → Folders.Id` (nullable).
- `Images.RawMetadata`, `AppSettings.ExcludedFolderNames`, `AppSettings.ExcludedExtensions`,
  `AppSettings.IncludedExtensions` are typed `jsonb`.
- `Images.DateTaken` is `timestamp without time zone`; every other timestamp column is
  `timestamp with time zone`.
- An `InsertData` (or equivalent seed) call exists for `AppUsers` with `Id = 1, DisplayName = "System"` and
  for `AppSettings` with `Id = 1`.

If anything on this checklist is missing or wrong, fix the relevant `IEntityTypeConfiguration` from Task 2,
delete the migration (`dotnet ef migrations remove --project src/PictureManager.Infrastructure
--startup-project src/PictureManager.Api`), and regenerate it — don't hand-edit the generated migration
file.

- [ ] **Step 3: Write the failing cascade-delete tests**

`tests/PictureManager.Infrastructure.Tests/Persistence/CascadeDeleteTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence;

public class CascadeDeleteTests
{
    private static DbContextOptions<PictureManagerDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    [Fact]
    public async Task DeletingFolder_CascadesToItsImagesAndTheirAlbumImages()
    {
        var options = CreateOptions();
        int folderId;

        await using (var context = new PictureManagerDbContext(options))
        {
            var root = new ImageRoot { Name = "root", MountPath = "/images", CreatedUtc = DateTime.UtcNow };
            var folder = new Folder
            {
                Name = "Vacation",
                RelativePath = "Vacation",
                Root = root,
                CreatedUtc = DateTime.UtcNow,
                ModifiedUtc = DateTime.UtcNow
            };
            var image = new Image
            {
                FileName = "IMG001",
                Extension = ".jpg",
                ContentHash = "hash1",
                FileSize = 100,
                FileModified = DateTime.UtcNow,
                FirstSeenUtc = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Folder = folder
            };
            var owner = new AppUser { Id = 500, DisplayName = "Owner", Role = UserRole.User };
            var album = new Album
            {
                Name = "Album1",
                OwnerUser = owner,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var albumImage = new AlbumImage { Album = album, Image = image, SortOrder = 0, AddedAt = DateTime.UtcNow };

            context.AddRange(root, folder, image, owner, album, albumImage);
            await context.SaveChangesAsync();
            folderId = folder.Id;
        }

        await using (var context = new PictureManagerDbContext(options))
        {
            var folder = await context.Folders
                .Include(f => f.Images)
                .ThenInclude(i => i.AlbumImages)
                .SingleAsync(f => f.Id == folderId);

            context.Folders.Remove(folder);
            await context.SaveChangesAsync();
        }

        await using (var context = new PictureManagerDbContext(options))
        {
            (await context.Images.CountAsync()).Should().Be(0);
            (await context.AlbumImages.CountAsync()).Should().Be(0);
        }
    }

    [Fact]
    public async Task DeletingParentFolder_CascadesToChildFolders()
    {
        var options = CreateOptions();
        int parentId;

        await using (var context = new PictureManagerDbContext(options))
        {
            var root = new ImageRoot { Name = "root", MountPath = "/images", CreatedUtc = DateTime.UtcNow };
            var parent = new Folder
            {
                Name = "Parent",
                RelativePath = "Parent",
                Root = root,
                CreatedUtc = DateTime.UtcNow,
                ModifiedUtc = DateTime.UtcNow
            };
            var child = new Folder
            {
                Name = "Child",
                RelativePath = "Parent/Child",
                Root = root,
                Parent = parent,
                CreatedUtc = DateTime.UtcNow,
                ModifiedUtc = DateTime.UtcNow
            };

            context.AddRange(root, parent, child);
            await context.SaveChangesAsync();
            parentId = parent.Id;
        }

        await using (var context = new PictureManagerDbContext(options))
        {
            var parent = await context.Folders
                .Include(f => f.Children)
                .SingleAsync(f => f.Id == parentId);

            context.Folders.Remove(parent);
            await context.SaveChangesAsync();
        }

        await using (var context = new PictureManagerDbContext(options))
        {
            (await context.Folders.CountAsync()).Should().Be(0);
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter CascadeDeleteTests`
Expected: at this point the configurations already exist (from Task 2), so these tests may already pass —
if so, that's fine, it confirms Task 2's cascade configuration is correct; if either fails, fix the
relevant `OnDelete(DeleteBehavior.Cascade)` call in the Task 2 configuration files before continuing (this
is a genuine regression, not a step to skip).

- [ ] **Step 5: Run the full Infrastructure test suite**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests`
Expected: PASS, all tests.

- [ ] **Step 6: Commit**

```bash
git add src/PictureManager.Infrastructure/Migrations tests/PictureManager.Infrastructure.Tests
git commit -m "feat: add InitialCreate migration and cascade-delete tests"
```

---

## Task 4: Repository interfaces and implementations

**Files:**
- Create: `src/PictureManager.Application/Repositories/IFolderRepository.cs`
- Create: `src/PictureManager.Application/Repositories/IImageRepository.cs`
- Create: `src/PictureManager.Application/Repositories/IAlbumRepository.cs`
- Create: `src/PictureManager.Application/Repositories/IAppUserRepository.cs`
- Create: `src/PictureManager.Application/Repositories/IImageRootRepository.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Repositories/FolderRepository.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Repositories/ImageRepository.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Repositories/AlbumRepository.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Repositories/AppUserRepository.cs`
- Create: `src/PictureManager.Infrastructure/Persistence/Repositories/ImageRootRepository.cs`
- Modify: `src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FolderRepositoryTests.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ImageRepositoryTests.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AlbumRepositoryTests.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AppUserRepositoryTests.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ImageRootRepositoryTests.cs`
- Modify: `tests/PictureManager.Infrastructure.Tests/DependencyInjection/InfrastructureServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes: `PictureManagerDbContext` (Task 2), `AppUserConfiguration.SystemUserId` (Task 2, used by the
  `AppUserRepositoryTests` to assert the seeded row is retrievable).
- Produces: `IFolderRepository`, `IImageRepository`, `IAlbumRepository`, `IAppUserRepository`,
  `IImageRootRepository` (all in `PictureManager.Application.Repositories`) — the exact interfaces phase 3
  (scanner), phase 5 (REST API), and phase 5's album endpoints will inject and call.

- [ ] **Step 1: Write the failing repository interfaces and their tests together**

`src/PictureManager.Application/Repositories/IFolderRepository.cs`:

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
}
```

`src/PictureManager.Application/Repositories/IImageRepository.cs`:

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
}
```

`src/PictureManager.Application/Repositories/IAlbumRepository.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IAlbumRepository
{
    Task<Album?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Album>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Album> AddAsync(Album album, CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Application/Repositories/IAppUserRepository.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IAppUserRepository
{
    Task<AppUser?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
}
```

`src/PictureManager.Application/Repositories/IImageRootRepository.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IImageRootRepository
{
    Task<ImageRoot?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImageRoot>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ImageRoot> AddAsync(ImageRoot imageRoot, CancellationToken cancellationToken = default);
}
```

`tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/FolderRepositoryTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class FolderRepositoryTests
{
    private static PictureManagerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task AddAsync_PersistsFolder_AndGetByIdAsync_ReturnsIt()
    {
        await using var context = CreateContext();
        var root = new ImageRoot { Name = "root", MountPath = "/images", CreatedUtc = DateTime.UtcNow };
        context.ImageRoots.Add(root);
        await context.SaveChangesAsync();

        var repository = new FolderRepository(context);
        var folder = new Folder
        {
            Name = "Vacation",
            RelativePath = "Vacation",
            RootId = root.Id,
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow
        };

        var added = await repository.AddAsync(folder);
        var fetched = await repository.GetByIdAsync(added.Id);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Vacation");
    }

    [Fact]
    public async Task GetChildrenAsync_ReturnsOnlyDirectChildrenOfGivenParent()
    {
        await using var context = CreateContext();
        var root = new ImageRoot { Name = "root", MountPath = "/images", CreatedUtc = DateTime.UtcNow };
        var parent = new Folder
        {
            Name = "Parent",
            RelativePath = "Parent",
            Root = root,
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow
        };
        context.AddRange(root, parent);
        await context.SaveChangesAsync();

        var child1 = new Folder { Name = "Child1", RelativePath = "Parent/Child1", RootId = root.Id, ParentId = parent.Id, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        var child2 = new Folder { Name = "Child2", RelativePath = "Parent/Child2", RootId = root.Id, ParentId = parent.Id, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        var unrelated = new Folder { Name = "Other", RelativePath = "Other", RootId = root.Id, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        context.Folders.AddRange(child1, child2, unrelated);
        await context.SaveChangesAsync();

        var repository = new FolderRepository(context);
        var children = await repository.GetChildrenAsync(parent.Id);

        children.Should().HaveCount(2);
        children.Select(f => f.Name).Should().BeEquivalentTo("Child1", "Child2");
    }
}
```

`tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ImageRepositoryTests.cs`:

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

public class ImageRepositoryTests
{
    private static PictureManagerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<Folder> SeedFolderAsync(PictureManagerDbContext context)
    {
        var root = new ImageRoot { Name = "root", MountPath = "/images", CreatedUtc = DateTime.UtcNow };
        var folder = new Folder
        {
            Name = "Vacation",
            RelativePath = "Vacation",
            Root = root,
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow
        };
        context.AddRange(root, folder);
        await context.SaveChangesAsync();
        return folder;
    }

    [Fact]
    public async Task AddAsync_PersistsImage_AndGetByIdAsync_ReturnsIt()
    {
        await using var context = CreateContext();
        var folder = await SeedFolderAsync(context);
        var repository = new ImageRepository(context);

        var image = new Image
        {
            FolderId = folder.Id,
            FileName = "IMG001",
            Extension = ".jpg",
            ContentHash = "hash1",
            FileSize = 100,
            FileModified = DateTime.UtcNow,
            FirstSeenUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var added = await repository.AddAsync(image);
        var fetched = await repository.GetByIdAsync(added.Id);

        fetched.Should().NotBeNull();
        fetched!.FileName.Should().Be("IMG001");
    }

    [Fact]
    public async Task GetByFolderIdAsync_ReturnsOnlyImagesInThatFolder()
    {
        await using var context = CreateContext();
        var folder = await SeedFolderAsync(context);
        var otherFolder = new Folder { Name = "Other", RelativePath = "Other", RootId = folder.RootId, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        context.Folders.Add(otherFolder);
        await context.SaveChangesAsync();

        context.Images.AddRange(
            new Image { FolderId = folder.Id, FileName = "A", Extension = ".jpg", ContentHash = "h1", FileSize = 1, FileModified = DateTime.UtcNow, FirstSeenUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Image { FolderId = folder.Id, FileName = "B", Extension = ".jpg", ContentHash = "h2", FileSize = 1, FileModified = DateTime.UtcNow, FirstSeenUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Image { FolderId = otherFolder.Id, FileName = "C", Extension = ".jpg", ContentHash = "h3", FileSize = 1, FileModified = DateTime.UtcNow, FirstSeenUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var repository = new ImageRepository(context);
        var images = await repository.GetByFolderIdAsync(folder.Id);

        images.Should().HaveCount(2);
    }
}
```

`tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AlbumRepositoryTests.cs`:

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

public class AlbumRepositoryTests
{
    private static PictureManagerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task AddAsync_PersistsAlbum_AndGetByIdAsync_ReturnsIt()
    {
        await using var context = CreateContext();
        var owner = new AppUser { Id = 1, DisplayName = "System", Role = UserRole.User };
        context.AppUsers.Add(owner);
        await context.SaveChangesAsync();

        var repository = new AlbumRepository(context);
        var album = new Album
        {
            Name = "Best of 2026",
            OwnerUserId = owner.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var added = await repository.AddAsync(album);
        var fetched = await repository.GetByIdAsync(added.Id);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Best of 2026");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEveryAlbum()
    {
        await using var context = CreateContext();
        var owner = new AppUser { Id = 1, DisplayName = "System", Role = UserRole.User };
        context.AppUsers.Add(owner);
        context.Albums.AddRange(
            new Album { Name = "A", OwnerUserId = owner.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Album { Name = "B", OwnerUserId = owner.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var repository = new AlbumRepository(context);
        var albums = await repository.GetAllAsync();

        albums.Should().HaveCount(2);
    }
}
```

`tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AppUserRepositoryTests.cs`:

```csharp
using System;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using System.Threading.Tasks;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Infrastructure.Persistence.Configurations;
using PictureManager.Infrastructure.Persistence.Repositories;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class AppUserRepositoryTests
{
    [Fact]
    public async Task GetByIdAsync_ReturnsSeededSystemUser()
    {
        var options = new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new PictureManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var repository = new AppUserRepository(context);
        var user = await repository.GetByIdAsync(AppUserConfiguration.SystemUserId);

        user.Should().NotBeNull();
        user!.DisplayName.Should().Be("System");
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var options = new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new PictureManagerDbContext(options);

        var repository = new AppUserRepository(context);
        var user = await repository.GetByIdAsync(999);

        user.Should().BeNull();
    }
}
```

`tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ImageRootRepositoryTests.cs`:

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

public class ImageRootRepositoryTests
{
    private static PictureManagerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task AddAsync_PersistsImageRoot_AndGetByIdAsync_ReturnsIt()
    {
        await using var context = CreateContext();
        var repository = new ImageRootRepository(context);

        var root = new ImageRoot { Name = "holidays-nas", MountPath = "/images/holidays", CreatedUtc = DateTime.UtcNow };
        var added = await repository.AddAsync(root);
        var fetched = await repository.GetByIdAsync(added.Id);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("holidays-nas");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEveryRegisteredRoot()
    {
        await using var context = CreateContext();
        context.ImageRoots.AddRange(
            new ImageRoot { Name = "root-a", MountPath = "/images/a", CreatedUtc = DateTime.UtcNow },
            new ImageRoot { Name = "root-b", MountPath = "/images/b", CreatedUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var repository = new ImageRootRepository(context);
        var roots = await repository.GetAllAsync();

        roots.Should().HaveCount(2);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests`
Expected: FAIL — the five repository implementation classes don't exist yet.

- [ ] **Step 3: Implement the repositories**

`src/PictureManager.Infrastructure/Persistence/Repositories/FolderRepository.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class FolderRepository : IFolderRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public FolderRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Folder?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Folders.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Folder>> GetChildrenAsync(int? parentId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Folders.Where(f => f.ParentId == parentId).ToListAsync(cancellationToken);
    }

    public async Task<Folder> AddAsync(Folder folder, CancellationToken cancellationToken = default)
    {
        _dbContext.Folders.Add(folder);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return folder;
    }
}
```

`src/PictureManager.Infrastructure/Persistence/Repositories/ImageRepository.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class ImageRepository : IImageRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public ImageRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Image?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Images.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Image>> GetByFolderIdAsync(int folderId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Images.Where(i => i.FolderId == folderId).ToListAsync(cancellationToken);
    }

    public async Task<Image> AddAsync(Image image, CancellationToken cancellationToken = default)
    {
        _dbContext.Images.Add(image);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return image;
    }
}
```

`src/PictureManager.Infrastructure/Persistence/Repositories/AlbumRepository.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class AlbumRepository : IAlbumRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public AlbumRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Album?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Albums.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Album>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Albums.ToListAsync(cancellationToken);
    }

    public async Task<Album> AddAsync(Album album, CancellationToken cancellationToken = default)
    {
        _dbContext.Albums.Add(album);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return album;
    }
}
```

`src/PictureManager.Infrastructure/Persistence/Repositories/AppUserRepository.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class AppUserRepository : IAppUserRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public AppUserRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AppUser?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AppUsers.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }
}
```

`src/PictureManager.Infrastructure/Persistence/Repositories/ImageRootRepository.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class ImageRootRepository : IImageRootRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public ImageRootRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ImageRoot?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ImageRoots.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ImageRoot>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.ImageRoots.ToListAsync(cancellationToken);
    }

    public async Task<ImageRoot> AddAsync(ImageRoot imageRoot, CancellationToken cancellationToken = default)
    {
        _dbContext.ImageRoots.Add(imageRoot);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return imageRoot;
    }
}
```

- [ ] **Step 4: Register the repositories in DI**

In `src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`,
add the five registrations inside `AddInfrastructure`, after the existing `AddDbContext` call and before
`return services;`:

```csharp
        services.AddScoped<IFolderRepository, FolderRepository>();
        services.AddScoped<IImageRepository, ImageRepository>();
        services.AddScoped<IAlbumRepository, AlbumRepository>();
        services.AddScoped<IAppUserRepository, AppUserRepository>();
        services.AddScoped<IImageRootRepository, ImageRootRepository>();
```

Add the two new `using` statements this needs at the top of the file:

```csharp
using PictureManager.Application.Repositories;
using PictureManager.Infrastructure.Persistence.Repositories;
```

- [ ] **Step 5: Extend the DI registration test**

Add these two test methods to the existing
`tests/PictureManager.Infrastructure.Tests/DependencyInjection/InfrastructureServiceCollectionExtensionsTests.cs`
(inside the existing `InfrastructureServiceCollectionExtensionsTests` class, alongside the two tests already
there from phase 1 — add the needed `using PictureManager.Application.Repositories;` to the file's usings):

```csharp
    [Fact]
    public void AddInfrastructure_RegistersAllFiveRepositories()
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

        provider.GetService<IFolderRepository>().Should().NotBeNull();
        provider.GetService<IImageRepository>().Should().NotBeNull();
        provider.GetService<IAlbumRepository>().Should().NotBeNull();
        provider.GetService<IAppUserRepository>().Should().NotBeNull();
        provider.GetService<IImageRootRepository>().Should().NotBeNull();
    }
```

- [ ] **Step 6: Run the full Infrastructure test suite to verify everything passes**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests`
Expected: PASS, all tests (phase 1's DI tests + this phase's seed-data, cascade-delete, and repository
tests, plus the new DI test).

- [ ] **Step 7: Run the full solution build and test suite (final regression check)**

Run: `dotnet build`
Expected: Build succeeds, 0 warnings, 0 errors, all 7 (now effectively 7, unchanged project count) projects.

Run: `dotnet test`
Expected: PASS, all tests across both test projects.

- [ ] **Step 8: Commit**

```bash
git add src/PictureManager.Application src/PictureManager.Infrastructure tests/PictureManager.Infrastructure.Tests
git commit -m "feat: add repository interfaces and implementations for Folder, Image, Album, AppUser, ImageRoot"
```

---

## Phase 2 exit criteria

- `dotnet build` succeeds across the whole solution, 0 warnings, 0 errors.
- `dotnet test` passes across both test projects, with no live database involved anywhere.
- A single `InitialCreate` migration exists, generated cleanly, matching the checklist in Task 3 Step 2.
- The `db` container from phase 1 was never started during this phase.
- Everything committed to git on the phase's feature branch.
