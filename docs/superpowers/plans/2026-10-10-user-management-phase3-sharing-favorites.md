# User Management Phase 3: Album Sharing and Per-User Favorites Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Albums can be shared with other users as Viewer or Editor. Favorites belong to each user instead of to the image.

**Architecture:**
- **Favorites:** a new `UserFavorites` table replaces `Images.IsFavorite`. Every image projection and favorites filter takes the caller's user id explicitly (repository parameter, never ambient state).
- **Sharing:** a new `AlbumShares` table. `IAlbumRepository.GetAccessibleAsync` resolves an album to Owner/Editor/Viewer (or null). `AlbumService` checks the access level each operation needs: no access is 404, too little access is a new `ResultStatus.Forbidden` (403).
- **SPA:** splits Albums into "My albums" and "Shared with me", gates album controls on `access`, adds a `ShareDialog` built on the Phase 2 user directory, and offers only editable albums wherever photos are added or removed.

**Tech Stack:** .NET 10 Minimal APIs, EF Core 10 + PostgreSQL (migrations via the local `dotnet-ef` tool), xUnit + FluentAssertions + NSubstitute; React 19 + TypeScript + MUI + TanStack Query, Vitest + MSW.

**Spec:** `docs/superpowers/specs/2026-10-09-user-management-design.md`, section "Phase 3: Album sharing + per-user favorites", plus "Error handling" and "Testing". Phases 1 and 2 are merged (`IUserService`, `GET /api/users/directory`, `AppUserRepository.DeleteAsync` with album transfer).

## Global Constraints

- `AlbumShare {AlbumId, UserId, Permission (Viewer|Editor), CreatedAt}`, PK `(AlbumId, UserId)`. Cascade on album delete and on user delete.
- `UserFavorite {UserId, ImageId, CreatedAt}`, PK `(UserId, ImageId)`, cascade on both sides.
- The migration copies `Images.IsFavorite = true` rows to user #1 (`AppUser.InitialAdminId`), then drops `Image.IsFavorite` and its indexes (`IX_Images_IsFavorite` and the raw-SQL partial `IX_Images_Favorites_SortDate_Id`).
- Access per operation:
  - **Viewer** may get the album, list its images and export it.
  - **Editor** may also add, remove, move and sort images, and set the cover.
  - **Owner** may also rename, describe and delete the album, and manage its shares.
  - Viewers and Editors may leave (remove their own share).
- Inaccessible albums are 404. Insufficient access is 403.
- `GET /albums` returns the caller's owned albums plus albums shared with them. `AlbumSummary`/`AlbumDetail` gain `access` and `ownerDisplayName`. Album names stay unique per owner.
- Share endpoints are on the `user` group:
  - `GET /albums/{id}/shares` (owner only)
  - `PUT /albums/{id}/shares/{userId} {permission}` (owner only)
  - `DELETE /albums/{id}/shares/{userId}`: the owner may remove anyone; any user may remove themselves.
- Sharing with yourself, with an inactive user or with an unknown user is a 400 validation problem.
- The image detail's album list shows only albums the caller can access.
- `PUT/DELETE /images/{id}/favorite` keeps its contract.
- JSON enums are strings in DTOs. The API has no `JsonStringEnumConverter`, so map with `.ToString()`. `access` is `"Owner" | "Editor" | "Viewer"` and `permission` is `"Viewer" | "Editor"`.
- An admin has no special access to other users' albums. Admins see their own albums and those shared with them, like any user.
- C#: file-scoped namespaces, `Result`/`Result<T>`, records for DTOs. Frontend: strict TypeScript, explicit prop types. The UI never shows the words "Viewer"/"Editor"; it says "Can view"/"Can edit".
- Build and test only the project you changed; keep terminal output short (`| tail -20`). **Stop a locally running `PictureManager.Api.exe` before building the Api, Infrastructure or test projects** (it locks `bin/`).
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **User #1 no longer exists when the favorites migration runs** (Phase 2 allows deleting `admin` once another admin exists). Expect the favorites to go to the lowest-id active admin instead, and the migration not to fail on the foreign key. Covered by the second migration test in Task 1.
2. **Favorite toggled twice quickly** (double click, or two tabs). Expect no duplicate-key error and one row. Covered by the `SetFavoriteAsync` idempotence test in Task 1.
3. **A user is deleted with "transfer" while the receiving admin already has a share on one of their albums.** Expect the admin to become owner and the stale share row to be gone, so the admin isn't listed as a sharee of their own album. Covered by the Task 2 Postgres test.
4. **A Viewer presses Shift+D**, in the shared album's view or elsewhere for a photo that is only in view-only albums. Expect nothing removed and a clear message, never a 403 toast saying "Couldn't remove". Covered by Task 4 and Task 6 tests.
5. **An Editor share is downgraded to Viewer while that album is the user's "last used" album.** Expect Shift+A to fall back to the picker instead of failing. Covered by a Task 4 test. Related: sharing with yourself, an inactive user, an unknown id, or the numeric permission `"1"` must answer 400 and store nothing. Covered by Task 3 service and smoke tests.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/PictureManager.Model/UserFavorite.cs` (new) | Per-user favorite entity |
| `src/PictureManager.Model/AlbumShare.cs` (new) | Share entity + `SharePermission` enum |
| `src/PictureManager.Model/Image.cs`, `Album.cs` (modify) | Drop `IsFavorite`; add `Favorites` / `Shares` navigations |
| `src/PictureManager.Infrastructure/Persistence/Configurations/UserFavoriteConfiguration.cs`, `AlbumShareConfiguration.cs` (new) | Keys, cascades, indexes |
| `src/PictureManager.Infrastructure/Migrations/*_UserFavorites.cs`, `*_AlbumShares.cs` (generated, then edited) | Schema + favorites data copy |
| `src/PictureManager.Infrastructure/Persistence/Queries/ImageProjections.cs` (modify) | `ToRow(int userId)` factory |
| `src/PictureManager.Infrastructure/Persistence/Repositories/ImageQueryRepository.cs`, `AlbumRepository.cs`, `AppUserRepository.cs` (modify) | User-scoped favorites, access resolution, shares, transfer clean-up |
| `src/PictureManager.Application/Albums/AlbumAccess.cs` (new) | `AlbumAccess`, `AlbumAccessRules`, `AccessibleAlbum`, `AlbumShareRow` |
| `src/PictureManager.Application/Albums/AlbumService.cs`, `IAlbumService.cs`, `AlbumModels.cs`, `AlbumRows.cs` (modify) | Access matrix, share operations, DTO fields |
| `src/PictureManager.Application/Common/Result.cs` (modify) | `ResultStatus.Forbidden` |
| `src/PictureManager.Api/Endpoints/ResultHttpExtensions.cs`, `AlbumEndpoints.cs` (modify) | 403 mapping, share routes |
| `web/src/albums/access.ts` (new) | `canEdit`, `isOwner`, `PERMISSION_LABEL` |
| `web/src/api/albumShares.ts` (new) | Share and directory hooks |
| `web/src/shared/UserAvatar.tsx` (moved from `admin/`) | Initials avatar, now with a `size` |
| `web/src/albums/ShareDialog.tsx` (new) | Owner's share management |
| `web/src/albums/AlbumsPage.tsx`, `AlbumView.tsx`, `AlbumGrid.tsx`, `AlbumPicker.tsx`, `viewer/PhotoViewer.tsx` (modify) | Sections, gating, editable-only pickers |
| `web/src/test/albumHandlers.ts`, `userHandlers.ts`, `authHandlers.ts`, `fixtures.ts` (modify) | MSW access model, shares, directory |

## UI Design Notes

The app already has a visual system: an indigo accent (`ACCENT #5b5bd6`, `ACCENT_SOFT #ecebfb`), zinc neutrals, `BORDER #e3e5ec`, `HEADING_SX`, `rounded-xl` cards and `rounded-2xl` dialogs. These screens extend it. They don't introduce a new look.

- **One memorable element: the owner's face on the cover.** A shared album's card shows the owner's initials avatar (28px, 2px ring in the paper colour) overlapping the bottom-left edge of the cover. "Someone else's album" reads before any text does. Everything else on the card stays as it is today.
- **Quiet signal on your own shared albums.** A small translucent pill in the cover's top-right corner, with a people icon and a count. It has a tooltip and an `aria-label` like "Shared with 2 people". There's no colour change and no badge on unshared albums.
- **Sections, not tabs.** The two sets are small and people want to see both at once. Each section has a sentence-case `h2` with a muted count beside it (not ALL CAPS, no eyebrow). "Shared with me" is only rendered when it has albums.
- **Words people use.** Access shows as "Can view" / "Can edit". The owner row in the share dialog says "Owner". Buttons say exactly what happens: "Share…", "Share", "Leave…", "Leave", "Done".
- **Share dialog layout:**

```
┌ Share Summer 2025 ──────────────────────────────────────┐
│ [ Add people            ▾ ] [ Can view ▾ ]  [ Share ]   │
│                                                         │
│ People with access                                      │
│ (AD) Administrator                               Owner  │
│ (BB) Bob B                     [ Can edit ▾ ]     (✕)   │
│ (CC) Carol C                   [ Can view ▾ ]     (✕)   │
│                                                         │
│ They find it under Shared with me. People who can edit  │
│ add, remove and reorder photos. Only you can rename,    │
│ delete or share it.                            [ Done ] │
└─────────────────────────────────────────────────────────┘
```

- **Shared album header:** under the title, a 24px owner avatar, "Shared by Bob B", and an outlined chip "Can view" or "Can edit". Controls the user can't use are absent rather than disabled, because the reason is permanent.
- Left-aligned throughout. Motion only where it answers an action (MUI defaults for dialogs and menus). Dark mode works because colours come from the theme or `alpha(ACCENT, …)`.

---

### Task 1: Per-user favorites (backend)

**Files:**
- Create: `src/PictureManager.Model/UserFavorite.cs`, `src/PictureManager.Infrastructure/Persistence/Configurations/UserFavoriteConfiguration.cs`, migration `*_UserFavorites.cs` (generated)
- Modify:
  - `src/PictureManager.Model/Image.cs`
  - `src/PictureManager.Infrastructure/Persistence/PictureManagerDbContext.cs`
  - `Configurations/ImageConfiguration.cs`
  - `Persistence/Queries/ImageProjections.cs`
  - `Repositories/ImageQueryRepository.cs`, `Repositories/AlbumRepository.cs` (`ListImagesAsync` only)
  - `src/PictureManager.Application/Repositories/IImageQueryRepository.cs`, `IAlbumRepository.cs` (`ListImagesAsync` only)
  - `src/PictureManager.Application/Images/ImageQueryService.cs`
  - `src/PictureManager.Application/Duplicates/DuplicateService.cs`
  - `src/PictureManager.Application/Albums/AlbumService.cs` (`ListImagesAsync` only)
- Test:
  - `tests/PictureManager.Infrastructure.Tests/Support/TestData.cs`
  - `Persistence/UserFavoritesMigrationTests.cs` (new)
  - `Persistence/Repositories/ImageQueryRepositoryTests.cs`
  - every test that calls a changed signature (see Step 6)

**Interfaces:**
- Produces:
  - `UserFavorite { int UserId; int ImageId; DateTime CreatedAt; AppUser? User; Image? Image }`
  - `Image.Favorites` (`ICollection<UserFavorite>`)
  - `DbSet<UserFavorite> UserFavorites`
  - `ImageProjections.ToRow(int userId)`
  - `IImageQueryRepository`:
    - `ListAsync(int userId, ImageListFilter filter, ImageSort sort, SortDirection direction, ImageKeyset? after, int take, ct)`
    - `GetVisibleDetailAsync(int id, int userId, ct)`
    - `SetFavoriteAsync(int id, int userId, bool isFavorite, DateTime nowUtc, ct)`
    - `GetDuplicateMembersAsync(IReadOnlyCollection<string> contentHashes, int userId, ct)`
    - `GetMembersByIdsAsync(IReadOnlyCollection<int> ids, int userId, ct)`
  - `IAlbumRepository.ListImagesAsync(int albumId, int userId, int? afterSortOrder, int? afterImageId, int take, ct)`
  - `DuplicateService(IImageQueryRepository images, ICurrentUser currentUser)`
  - Test helpers:
    - `TestData.User(string username, UserRole role = UserRole.User, bool isActive = true)`
    - `TestData.Favorite(int userId, Image image)`
    - `TestData.Image` loses its `isFavorite` parameter.

- [ ] **Step 1: Add the test helpers.** In `tests/PictureManager.Infrastructure.Tests/Support/TestData.cs`:
  - Remove the `bool isFavorite = false,` parameter and the `IsFavorite = isFavorite,` line from `Image(...)`.
  - Add:

```csharp
    /// <summary>An account with a unique username; display name = username.</summary>
    public static AppUser User(string username, UserRole role = UserRole.User, bool isActive = true) => new()
    {
        Username = username,
        NormalizedUsername = username.ToLowerInvariant(),
        DisplayName = username,
        Role = role,
        IsActive = isActive,
        CreatedAt = Utc
    };

    public static UserFavorite Favorite(int userId, Image image) => new()
    {
        UserId = userId,
        Image = image,
        CreatedAt = Utc
    };
```

- [ ] **Step 2: Write the failing repository tests.** Append to `ImageQueryRepositoryTests` (it already has `using PictureManager.Tests.Support;`; add `using System.Collections.Generic;` if missing):

```csharp
    [Fact]
    public async Task Favorites_ArePerUser_InTheFilter_TheRowFlag_TheDetail_AndAlbumEntries()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = TestData.User("bob");
        var folder = TestData.Folder(TestData.Root("r"), "");
        var mine = TestData.Image(folder, "mine");
        var his = TestData.Image(folder, "his");
        var album = TestData.Album("both");
        db.Context.AppUsers.Add(bob);
        db.Context.AlbumImages.AddRange(TestData.AlbumImage(album, mine, 0), TestData.AlbumImage(album, his, 1));
        await db.Context.SaveChangesAsync();
        db.Context.UserFavorites.AddRange(TestData.Favorite(AppUser.InitialAdminId, mine), TestData.Favorite(bob.Id, his));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);
        var favoritesOnly = new ImageListFilter(null, null, null, true);

        (await repository.ListAsync(AppUser.InitialAdminId, favoritesOnly, ImageSort.Name, SortDirection.Asc, null, 10))
            .Select(r => r.Id).Should().Equal(mine.Id);
        (await repository.ListAsync(bob.Id, favoritesOnly, ImageSort.Name, SortDirection.Asc, null, 10))
            .Select(r => r.Id).Should().Equal(his.Id);
        (await repository.ListAsync(bob.Id, NoFilter, ImageSort.Name, SortDirection.Asc, null, 10))
            .ToDictionary(r => r.Id, r => r.IsFavorite)
            .Should().Equal(new Dictionary<int, bool> { [his.Id] = true, [mine.Id] = false });
        (await repository.GetVisibleDetailAsync(mine.Id, bob.Id))!.Image.IsFavorite.Should().BeFalse();
        (await repository.GetVisibleDetailAsync(mine.Id, AppUser.InitialAdminId))!.Image.IsFavorite.Should().BeTrue();
        (await new AlbumRepository(context).ListImagesAsync(album.Id, bob.Id, null, null, 10))
            .Select(r => r.Image.IsFavorite).Should().Equal(false, true);
    }

    [Fact]
    public async Task SetFavoriteAsync_ChangesOnlyTheCallersFavorite_IsIdempotent_AndRefusesUnknownImages()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = TestData.User("bob");
        var image = TestData.Image(TestData.Folder(TestData.Root("r"), ""), "pic");
        db.Context.AppUsers.Add(bob);
        db.Context.UserFavorites.Add(TestData.Favorite(AppUser.InitialAdminId, image));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);

        (await repository.SetFavoriteAsync(image.Id, bob.Id, true, TestData.Utc)).Should().BeTrue();
        (await repository.SetFavoriteAsync(image.Id, bob.Id, true, TestData.Utc)).Should().BeTrue();
        (await context.UserFavorites.CountAsync(f => f.ImageId == image.Id)).Should().Be(2);

        (await repository.SetFavoriteAsync(image.Id, bob.Id, false, TestData.Utc)).Should().BeTrue();
        (await context.UserFavorites.Where(f => f.ImageId == image.Id).Select(f => f.UserId).ToListAsync())
            .Should().Equal(AppUser.InitialAdminId);

        (await repository.SetFavoriteAsync(999_999, bob.Id, true, TestData.Utc)).Should().BeFalse();
    }
```

- [ ] **Step 3: Write the failing migration tests.** Create `tests/PictureManager.Infrastructure.Tests/Persistence/UserFavoritesMigrationTests.cs`:

```csharp
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence;

/// <summary>Rolls the schema back to before per-user favorites, sets the old flag, and migrates forward again.</summary>
public class UserFavoritesMigrationTests
{
    private const string BeforeFavorites = "20261009192921_UserAccounts";

    [Fact]
    public async Task GlobalFavorites_MoveToTheInitialAdmin_AndRollingBackRestoresTheFlag()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var favorite = TestData.Image(folder, "fav");
        var plain = TestData.Image(folder, "plain");
        db.Context.Images.AddRange(favorite, plain);
        db.Context.UserFavorites.Add(TestData.Favorite(AppUser.InitialAdminId, favorite));
        await db.Context.SaveChangesAsync();
        var migrator = db.Context.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeFavorites);
        (await db.Context.Database.SqlQueryRaw<int>("SELECT \"Id\" AS \"Value\" FROM \"Images\" WHERE \"IsFavorite\"").ToListAsync())
            .Should().Equal(favorite.Id);

        await migrator.MigrateAsync();

        await using var check = db.CreateContext();
        (await check.UserFavorites.Select(f => new { f.UserId, f.ImageId }).ToListAsync())
            .Should().Equal(new { UserId = AppUser.InitialAdminId, ImageId = favorite.Id });
    }

    [Fact]
    public async Task GlobalFavorites_GoToTheFirstActiveAdmin_WhenTheInitialAdminWasDeleted()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var favorite = TestData.Image(TestData.Folder(TestData.Root("r"), ""), "fav");
        var successor = TestData.User("root2", UserRole.Admin);
        db.Context.Images.Add(favorite);
        db.Context.AppUsers.Add(successor);
        await db.Context.SaveChangesAsync();
        var migrator = db.Context.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeFavorites);
        await db.Context.Database.ExecuteSqlRawAsync("DELETE FROM \"AppUsers\" WHERE \"Id\" = 1");
        await db.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Images\" SET \"IsFavorite\" = true WHERE \"Id\" = {favorite.Id}");

        await migrator.MigrateAsync();

        await using var check = db.CreateContext();
        (await check.UserFavorites.Select(f => new { f.UserId, f.ImageId }).ToListAsync())
            .Should().Equal(new { UserId = successor.Id, ImageId = favorite.Id });
    }
}
```

- [ ] **Step 4: Run the tests and confirm they fail to compile.**

Run: `dotnet build tests/PictureManager.Infrastructure.Tests 2>&1 | grep -c "error CS"`
Expected: a non-zero count (missing `UserFavorites`, `UserFavorite`, and the new `ListAsync`/`GetVisibleDetailAsync`/`SetFavoriteAsync`/`ListImagesAsync` overloads).

- [ ] **Step 5: Model, configuration and repositories.**

Create `src/PictureManager.Model/UserFavorite.cs`:

```csharp
using System;

namespace PictureManager.Model;

/// <summary>One user's favorite photo. Favorites are per user; the image itself carries no flag.</summary>
public class UserFavorite
{
    public int UserId { get; set; }
    public int ImageId { get; set; }
    public DateTime CreatedAt { get; set; }

    public AppUser? User { get; set; }
    public Image? Image { get; set; }
}
```

In `Image.cs`, delete `public bool IsFavorite { get; set; }` and add after `AlbumImages`:

```csharp
    public ICollection<UserFavorite> Favorites { get; set; } = new List<UserFavorite>();
```

Create `src/PictureManager.Infrastructure/Persistence/Configurations/UserFavoriteConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class UserFavoriteConfiguration : IEntityTypeConfiguration<UserFavorite>
{
    public void Configure(EntityTypeBuilder<UserFavorite> builder)
    {
        builder.ToTable("UserFavorites");

        // (UserId, ImageId) also serves "this user's favorites" lookups.
        builder.HasKey(x => new { x.UserId, x.ImageId });

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Image)
            .WithMany(x => x.Favorites)
            .HasForeignKey(x => x.ImageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ImageId);
    }
}
```

In `ImageConfiguration.cs`, delete `builder.HasIndex(x => x.IsFavorite);`. Change the comment above the composite index so it reads `// Folder grid, date sort. The lower(FileName) index is an expression index EF's fluent API can't express; it is raw SQL in the Phase5RestApi migration.`

In `PictureManagerDbContext.cs`, after `AlbumImages`, add `public DbSet<UserFavorite> UserFavorites => Set<UserFavorite>();`.

Replace `ImageProjections.cs` with:

```csharp
using System;
using System.Linq;
using System.Linq.Expressions;
using PictureManager.Application.Images;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Queries;

internal static class ImageProjections
{
    /// <summary>The list row as <paramref name="userId"/> sees it: IsFavorite is that user's own favorite.</summary>
    public static Expression<Func<Image, ImageRow>> ToRow(int userId) => i => new ImageRow(
        i.Id, i.FolderId, i.FileName, i.Extension, i.Width, i.Height, i.DateTaken, i.Favorites.Any(f => f.UserId == userId),
        i.ContentHash, i.SortDate, i.FileName.ToLower(), i.Folder!.Root!.Name, i.Folder.RelativePath, i.IndexState, null, i.IsHidden, i.ThumbnailRotation);
}
```

In `IImageQueryRepository.cs`, change these signatures:
- `ListAsync(int userId, ImageListFilter filter, ImageSort sort, SortDirection direction, ImageKeyset? after, int take, CancellationToken cancellationToken = default)`
- `GetVisibleDetailAsync(int id, int userId, CancellationToken cancellationToken = default)`
- `GetDuplicateMembersAsync(IReadOnlyCollection<string> contentHashes, int userId, CancellationToken cancellationToken = default)`
- `GetMembersByIdsAsync(IReadOnlyCollection<int> ids, int userId, CancellationToken cancellationToken = default)`

Then replace the `SetFavoriteAsync` declaration and the stray duplicate summary above `SetHiddenAsync` with:

```csharp
    /// <summary>Sets IsHidden on every existing image in `ids`; unknown or missing ids are skipped. Returns the rows changed.</summary>
    Task<int> SetHiddenAsync(IReadOnlyCollection<int> ids, bool isHidden, DateTime updatedAtUtc, CancellationToken cancellationToken = default);
```

(keep `RotateThumbnailsAsync` as is), and

```csharp
    /// <summary>Adds or removes <paramref name="userId"/>'s favorite; idempotent. False when no visible image has this id.</summary>
    Task<bool> SetFavoriteAsync(int id, int userId, bool isFavorite, DateTime nowUtc, CancellationToken cancellationToken = default);
```

Add a doc line on `ListAsync`, `GetVisibleDetailAsync`, `GetDuplicateMembersAsync` and `GetMembersByIdsAsync`: `/// <summary>... IsFavorite is <paramref name="userId"/>'s own favorite.</summary>`. Merge it into the existing summary where one exists.

In `ImageQueryRepository.cs`:
- `ListAsync(int userId, ImageListFilter filter, …)`:
  - `if (filter.FavoritesOnly) query = query.Where(i => i.Favorites.Any(f => f.UserId == userId));`
  - `.Select(ImageProjections.ToRow(userId))`
- `GetVisibleDetailAsync(int id, int userId, …)`: in the `new ImageRow(...)`, replace `i.IsFavorite` with `i.Favorites.Any(f => f.UserId == userId)`.
- Replace the `MemberProjection` field with a method and use it in both member queries (`GetDuplicateMembersAsync(contentHashes, userId, …)` → `.Select(MemberProjection(userId))`, the same for `GetMembersByIdsAsync(ids, userId, …)`):

```csharp
    private static System.Linq.Expressions.Expression<Func<Image, DuplicateMemberRow>> MemberProjection(int userId) => i => new DuplicateMemberRow(
        new ImageRow(i.Id, i.FolderId, i.FileName, i.Extension, i.Width, i.Height, i.DateTaken, i.Favorites.Any(f => f.UserId == userId),
            i.ContentHash, i.SortDate, i.FileName.ToLower(), i.Folder!.Root!.Name, i.Folder.RelativePath, i.IndexState, null, i.IsHidden, i.ThumbnailRotation),
        i.Folder!.Root!.Name,
        i.Folder.RelativePath,
        i.FileSize);
```

- Replace `SetFavoriteAsync` with:

```csharp
    public async Task<bool> SetFavoriteAsync(int id, int userId, bool isFavorite, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (!await _dbContext.Images.WhereVisible().AnyAsync(i => i.Id == id, cancellationToken))
            return false;

        if (isFavorite)
        {
            // ON CONFLICT: a double click or a second tab must not fail on the primary key.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "UserFavorites" ("UserId", "ImageId", "CreatedAt") VALUES ({userId}, {id}, {nowUtc})
                ON CONFLICT DO NOTHING
                """, cancellationToken);
        }
        else
        {
            await _dbContext.UserFavorites.Where(f => f.UserId == userId && f.ImageId == id).ExecuteDeleteAsync(cancellationToken);
        }

        return true;
    }
```

In `IAlbumRepository.cs`, change `ListImagesAsync` to `Task<IReadOnlyList<AlbumImageRow>> ListImagesAsync(int albumId, int userId, int? afterSortOrder, int? afterImageId, int take, CancellationToken cancellationToken = default);`. Extend its summary with `IsFavorite is <paramref name="userId"/>'s own favorite.`

In `AlbumRepository.ListImagesAsync`, add the `int userId` parameter after `albumId`, and replace `ai.Image.IsFavorite` with `ai.Image.Favorites.Any(f => f.UserId == userId)`.

- [ ] **Step 6: Services and existing call sites.**
- `ImageQueryService`:
  - `_images.ListAsync(_currentUser.UserId, filter, sort, direction, after, limit + 1, cancellationToken)`
  - `_images.GetVisibleDetailAsync(id, _currentUser.UserId, cancellationToken)`
  - `_images.SetFavoriteAsync(id, _currentUser.UserId, isFavorite, _clock.UtcNow, cancellationToken)`
- `DuplicateService`: add an `ICurrentUser currentUser` constructor parameter stored as `_currentUser`, and pass `_currentUser.UserId` to `GetDuplicateMembersAsync` and `GetMembersByIdsAsync`. Add `using PictureManager.Application.Common;` if missing.
- `AlbumService.ListImagesAsync`: `_albums.ListImagesAsync(id, _currentUser.UserId, after?.SortOrder, after?.ImageId, take + 1, cancellationToken)`.

Then fix every compile error in the test projects with these exact rules:
- **Infrastructure tests:** pass `AppUser.InitialAdminId` as the new `userId` argument.
  - `repository.ListAsync(filter, …)` becomes `repository.ListAsync(AppUser.InitialAdminId, filter, …)`. This includes the `PageAllAsync` helper.
  - `GetVisibleDetailAsync(x)` becomes `GetVisibleDetailAsync(x, AppUser.InitialAdminId)`.
  - `SetFavoriteAsync(id, flag, now)` becomes `SetFavoriteAsync(id, AppUser.InitialAdminId, flag, now)`.
  - Do the same for `GetDuplicateMembersAsync`, `GetMembersByIdsAsync` and `ListImagesAsync`.
- **Application tests (NSubstitute):** insert the user argument in the same position.
  - `ImageQueryServiceTests`: the current user id the test sets up (its `_currentUser.UserId` value), for example `_images.SetFavoriteAsync(9, CurrentUserId, true, …)`.
  - Elsewhere, `Arg.Any<int>()` where the test does not care.
  - `DuplicateServiceTests.CreateService()` becomes `new(_images, _currentUser)`, with a `private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();` field.
- **Former `isFavorite: true` callers:**
  - `ImageQueryRepositoryTests.ListAsync_FavoritesOnly_ReturnsOnlyFavorites`: create `favorite` with `TestData.Image(folder, "fav")`, then add `db.Context.UserFavorites.Add(TestData.Favorite(AppUser.InitialAdminId, favorite));` before `SaveChangesAsync`.
  - `MissingFolderTests`: same for `hidden` and `shown`.
- `ImageRepositoryTests` (`image.IsFavorite = true;`): replace those two lines with `image.FileName = "IMG002";` and `(await repository.GetByIdAsync(image.Id))!.FileName.Should().Be("IMG002");`.

- [ ] **Step 7: Generate and edit the migration.** Stop any running `PictureManager.Api.exe`, then run:

`dotnet ef migrations add UserFavorites --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api 2>&1 | tail -3`

In the generated `*_UserFavorites.cs`, replace the bodies of `Up` and `Down` (keep the generated class name and Designer file):

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserFavorites",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ImageId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFavorites", x => new { x.UserId, x.ImageId });
                    table.ForeignKey(
                        name: "FK_UserFavorites_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserFavorites_Images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "Images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserFavorites_ImageId",
                table: "UserFavorites",
                column: "ImageId");

            // Favorites were global before accounts existed, so they were the initial admin's (#1). If that account
            // was since deleted, the lowest-id active admin inherits them; with no admin at all they are dropped.
            migrationBuilder.Sql("""
                INSERT INTO "UserFavorites" ("UserId", "ImageId", "CreatedAt")
                SELECT heir."Id", i."Id", now()
                FROM "Images" i
                CROSS JOIN LATERAL (
                    SELECT u."Id" FROM "AppUsers" u
                    WHERE u."Id" = 1 OR (u."Role" = 1 AND u."IsActive")
                    ORDER BY (u."Id" = 1) DESC, u."Id"
                    LIMIT 1
                ) heir
                WHERE i."IsFavorite";
                """);

            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Images_Favorites_SortDate_Id\";");
            migrationBuilder.DropIndex(name: "IX_Images_IsFavorite", table: "Images");
            migrationBuilder.DropColumn(name: "IsFavorite", table: "Images");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "Images",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Only the initial admin's favorites fit the old global flag.
            migrationBuilder.Sql("""
                UPDATE "Images" SET "IsFavorite" = true
                WHERE "Id" IN (SELECT "ImageId" FROM "UserFavorites" WHERE "UserId" = 1);
                """);

            migrationBuilder.CreateIndex(name: "IX_Images_IsFavorite", table: "Images", column: "IsFavorite");
            migrationBuilder.Sql("CREATE INDEX \"IX_Images_Favorites_SortDate_Id\" ON \"Images\" (\"SortDate\", \"Id\") WHERE \"IsFavorite\";");
            migrationBuilder.DropTable(name: "UserFavorites");
        }
```

`UserRole.Admin` is stored as the integer `1` (enum order `User, Admin`).

- [ ] **Step 8: Run the tests and confirm they pass.**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests 2>&1 | tail -5`
Run: `dotnet test tests/PictureManager.Application.Tests 2>&1 | tail -5`
Expected: PASS, including the two new repository tests and both migration tests. If the template database was built before this migration existed, the first run rebuilds it (it is dropped and recreated per test process).

- [ ] **Step 9: Commit**

```bash
git add src tests
git commit -m "feat(favorites): per-user favorites in a UserFavorites table; migrate the global flag to the initial admin

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Album sharing data layer

**Files:**
- Create: `src/PictureManager.Model/AlbumShare.cs`, `src/PictureManager.Infrastructure/Persistence/Configurations/AlbumShareConfiguration.cs`, `src/PictureManager.Application/Albums/AlbumAccess.cs`, migration `*_AlbumShares.cs` (generated, not edited)
- Modify:
  - `src/PictureManager.Model/Album.cs`
  - `PictureManagerDbContext.cs`
  - `src/PictureManager.Application/Albums/AlbumRows.cs`
  - `src/PictureManager.Application/Repositories/IAlbumRepository.cs`
  - `Repositories/AlbumRepository.cs`, `Repositories/AppUserRepository.cs` (`DeleteAsync`)
  - `src/PictureManager.Application/Images/ImageQueryModels.cs` (`AlbumRef`, new `AlbumRefRow`)
  - `IImageQueryRepository.cs`, `ImageQueryRepository.cs` (`GetAlbumsContainingAsync`)
  - `ImageQueryService.cs` (album mapping)
  - `AlbumService.cs` (temporary owner-only helper)
- Test:
  - `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AlbumShareRepositoryTests.cs` (new)
  - `AlbumQueryRepositoryTests.cs` (replace the `GetOwnedAsync` uses)
  - `AppUserRepositoryTests.cs` (add a test)
  - `tests/PictureManager.Application.Tests/Albums/AlbumServiceTests.cs`, `Images/ImageQueryServiceTests.cs` (adapt)

**Interfaces:**
- Consumes: Task 1's `TestData.User`, `TestData.Favorite`, `UserFavorites`.
- Produces:
  - Model:
    - `enum SharePermission { Viewer, Editor }`
    - `AlbumShare { int AlbumId; int UserId; SharePermission Permission; DateTime CreatedAt; Album? Album; AppUser? User }`
    - `Album.Shares`
    - `DbSet<AlbumShare> AlbumShares`
  - Application, `PictureManager.Application.Albums`:
    - `enum AlbumAccess { Viewer, Editor, Owner }` (ordered: compare with `<`)
    - `AlbumAccessRules.From(bool isOwner, bool isEditor)`
    - `record AccessibleAlbum(Album Album, AlbumAccess Access, string OwnerDisplayName)`
    - `record AlbumShareRow(int UserId, string DisplayName, SharePermission Permission)`
  - `AlbumSummaryRow` gains trailing `bool IsOwner, bool IsEditor, string OwnerDisplayName, int ShareCount`.
  - `IAlbumRepository`:
    - `GetAccessibleAsync(int id, int userId, ct)` → `AccessibleAlbum?`, with the album tracked. It replaces `GetOwnedAsync`, which is removed.
    - `GetSummariesAsync(int userId, ct)`: owned plus shared.
    - `GetSharesAsync(int albumId, ct)` → `IReadOnlyList<AlbumShareRow>`
    - `SetShareAsync(int albumId, int userId, SharePermission permission, DateTime nowUtc, ct)`
    - `RemoveShareAsync(int albumId, int userId, ct)` → `bool`
  - `ImageQueryModels`:
    - `record AlbumRef(int Id, string Name, string Access)`
    - `record AlbumRefRow(int Id, string Name, bool IsOwner, bool IsEditor)`
    - `IImageQueryRepository.GetAlbumsContainingAsync(int imageId, int userId, ct)` → `IReadOnlyList<AlbumRefRow>`

- [ ] **Step 1: Write the failing repository tests.** Create `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AlbumShareRepositoryTests.cs`:

```csharp
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

public class AlbumShareRepositoryTests
{
    private static AlbumShare Share(Album album, AppUser user, SharePermission permission) =>
        new() { Album = album, User = user, Permission = permission, CreatedAt = TestData.Utc };

    [Fact]
    public async Task GetAccessibleAsync_ResolvesOwnerEditorViewer_AndNullForStrangers()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = TestData.User("bob");
        var carol = TestData.User("carol");
        var dave = TestData.User("dave");
        var album = TestData.Album("Trip");
        db.Context.AlbumShares.AddRange(Share(album, bob, SharePermission.Viewer), Share(album, carol, SharePermission.Editor));
        db.Context.AppUsers.Add(dave);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new AlbumRepository(context);

        (await repository.GetAccessibleAsync(album.Id, AppUser.InitialAdminId))!.Access.Should().Be(AlbumAccess.Owner);
        var forBob = await repository.GetAccessibleAsync(album.Id, bob.Id);
        forBob!.Access.Should().Be(AlbumAccess.Viewer);
        forBob.OwnerDisplayName.Should().Be("Administrator");
        forBob.Album.Name.Should().Be("Trip");
        (await repository.GetAccessibleAsync(album.Id, carol.Id))!.Access.Should().Be(AlbumAccess.Editor);
        (await repository.GetAccessibleAsync(album.Id, dave.Id)).Should().BeNull();
        (await repository.GetAccessibleAsync(999_999, AppUser.InitialAdminId)).Should().BeNull();
    }

    [Fact]
    public async Task GetSummariesAsync_ListsOwnedAndSharedAlbums_WithAccessOwnerAndShareCount()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = TestData.User("bob");
        var carol = TestData.User("carol");
        var mine = TestData.Album("Mine");
        db.Context.AppUsers.AddRange(bob, carol);
        await db.Context.SaveChangesAsync();
        var bobsShared = TestData.Album("Bob shared", bob.Id);
        var bobsPrivate = TestData.Album("Bob private", bob.Id);
        db.Context.Albums.AddRange(mine, bobsShared, bobsPrivate);
        db.Context.AlbumShares.AddRange(
            new AlbumShare { Album = mine, UserId = bob.Id, Permission = SharePermission.Viewer, CreatedAt = TestData.Utc },
            new AlbumShare { Album = mine, UserId = carol.Id, Permission = SharePermission.Editor, CreatedAt = TestData.Utc },
            new AlbumShare { Album = bobsShared, UserId = AppUser.InitialAdminId, Permission = SharePermission.Editor, CreatedAt = TestData.Utc });
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new AlbumRepository(context).GetSummariesAsync(AppUser.InitialAdminId);

        rows.Select(r => r.Name).Should().Equal("Bob shared", "Mine");
        rows[0].Should().Match<AlbumSummaryRow>(r => !r.IsOwner && r.IsEditor && r.OwnerDisplayName == "bob");
        rows[1].Should().Match<AlbumSummaryRow>(r => r.IsOwner && r.ShareCount == 2 && r.OwnerDisplayName == "Administrator");
    }

    [Fact]
    public async Task SetShareAsync_Upserts_GetSharesAsync_OrdersByName_RemoveShareAsync_ReportsWhetherOneWasThere()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var zoe = TestData.User("zoe");
        var ann = TestData.User("ann");
        var album = TestData.Album("Trip");
        db.Context.AppUsers.AddRange(zoe, ann);
        db.Context.Albums.Add(album);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new AlbumRepository(context);
        await repository.SetShareAsync(album.Id, zoe.Id, SharePermission.Viewer, TestData.Utc);
        await repository.SetShareAsync(album.Id, ann.Id, SharePermission.Viewer, TestData.Utc);
        await repository.SetShareAsync(album.Id, zoe.Id, SharePermission.Editor, TestData.Utc);

        (await repository.GetSharesAsync(album.Id)).Should().Equal(
            new AlbumShareRow(ann.Id, "ann", SharePermission.Viewer),
            new AlbumShareRow(zoe.Id, "zoe", SharePermission.Editor));
        (await repository.RemoveShareAsync(album.Id, ann.Id)).Should().BeTrue();
        (await repository.RemoveShareAsync(album.Id, ann.Id)).Should().BeFalse();
        (await repository.GetSharesAsync(album.Id)).Select(s => s.UserId).Should().Equal(zoe.Id);
    }

    [Fact]
    public async Task DeletingAnAlbumOrAUser_CascadesToSharesAndFavorites()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = TestData.User("bob");
        var kept = TestData.Album("Kept");
        var dropped = TestData.Album("Dropped");
        var image = TestData.Image(TestData.Folder(TestData.Root("r"), ""), "pic");
        db.Context.AlbumShares.AddRange(Share(kept, bob, SharePermission.Viewer), Share(dropped, bob, SharePermission.Viewer));
        db.Context.UserFavorites.Add(new UserFavorite { User = bob, Image = image, CreatedAt = TestData.Utc });
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
        {
            var albums = new AlbumRepository(context);
            await albums.DeleteAsync((await albums.GetAccessibleAsync(dropped.Id, AppUser.InitialAdminId))!.Album);
            (await context.AlbumShares.Select(s => s.AlbumId).ToListAsync()).Should().Equal(kept.Id);

            var users = new AppUserRepository(context);
            await users.DeleteAsync((await users.GetByIdAsync(bob.Id))!, null);
        }

        await using var check = db.CreateContext();
        (await check.AlbumShares.CountAsync()).Should().Be(0);
        (await check.UserFavorites.CountAsync()).Should().Be(0);
        (await check.Images.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetAlbumsContainingAsync_ListsOnlyAlbumsTheCallerCanSee_WithTheirAccess()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = TestData.User("bob");
        var image = TestData.Image(TestData.Folder(TestData.Root("r"), ""), "pic");
        db.Context.AppUsers.Add(bob);
        await db.Context.SaveChangesAsync();
        var mine = TestData.Album("Mine");
        var sharedToMe = TestData.Album("Shared to me", bob.Id);
        var bobsPrivate = TestData.Album("Bob private", bob.Id);
        db.Context.AlbumImages.AddRange(
            TestData.AlbumImage(mine, image, 0), TestData.AlbumImage(sharedToMe, image, 0), TestData.AlbumImage(bobsPrivate, image, 0));
        db.Context.AlbumShares.Add(new AlbumShare { Album = sharedToMe, UserId = AppUser.InitialAdminId, Permission = SharePermission.Viewer, CreatedAt = TestData.Utc });
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var refs = await new ImageQueryRepository(context).GetAlbumsContainingAsync(image.Id, AppUser.InitialAdminId);

        refs.Should().Equal(
            new PictureManager.Application.Images.AlbumRefRow(mine.Id, "Mine", true, false),
            new PictureManager.Application.Images.AlbumRefRow(sharedToMe.Id, "Shared to me", false, false));
    }
}
```

Append to `AppUserRepositoryTests` (Postgres; the delete-and-transfer helpers from Phase 2 are already in that file):

```csharp
    [Fact]
    public async Task DeleteAsync_Transfer_DropsTheReceivingAdminsShareOnTheirNewAlbums_AndKeepsOthersShares()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = NewAccount("bob");
        var carol = NewAccount("carol");
        db.Context.AppUsers.AddRange(bob, carol);
        await db.Context.SaveChangesAsync();
        var trip = TestData.Album("Trip", bob.Id);
        db.Context.Albums.Add(trip);
        db.Context.AlbumShares.AddRange(
            new AlbumShare { Album = trip, UserId = AppUser.InitialAdminId, Permission = SharePermission.Viewer, CreatedAt = TestData.Utc },
            new AlbumShare { Album = trip, UserId = carol.Id, Permission = SharePermission.Editor, CreatedAt = TestData.Utc });
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
        {
            var repository = new AppUserRepository(context);
            await repository.DeleteAsync((await repository.GetByIdAsync(bob.Id))!, AppUser.InitialAdminId);
        }

        await using var check = db.CreateContext();
        (await check.Albums.SingleAsync(a => a.Id == trip.Id)).OwnerUserId.Should().Be(AppUser.InitialAdminId);
        (await check.AlbumShares.Select(s => s.UserId).ToListAsync()).Should().Equal(carol.Id);
    }
```

Ruling written into the plan: shares that third parties hold on a transferred album are kept, because the album keeps existing and the sharing intent with it. Only the new owner's own share is removed.

- [ ] **Step 2: Run the tests and confirm they fail to compile.**

Run: `dotnet build tests/PictureManager.Infrastructure.Tests 2>&1 | grep -c "error CS"`
Expected: non-zero (`AlbumShare`, `SharePermission`, `AlbumShares`, `GetAccessibleAsync`, `AlbumRefRow` missing).

- [ ] **Step 3: Model and configuration.** Create `src/PictureManager.Model/AlbumShare.cs`:

```csharp
using System;

namespace PictureManager.Model;

public enum SharePermission
{
    Viewer,
    Editor
}

/// <summary>An album its owner shared with another user. Viewers browse and export; editors also change the photos.</summary>
public class AlbumShare
{
    public int AlbumId { get; set; }
    public int UserId { get; set; }
    public SharePermission Permission { get; set; }
    public DateTime CreatedAt { get; set; }

    public Album? Album { get; set; }
    public AppUser? User { get; set; }
}
```

In `Album.cs` add `public ICollection<AlbumShare> Shares { get; set; } = new List<AlbumShare>();` after `AlbumImages`. In `PictureManagerDbContext.cs` add `public DbSet<AlbumShare> AlbumShares => Set<AlbumShare>();`.

Create `AlbumShareConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Configurations;

public class AlbumShareConfiguration : IEntityTypeConfiguration<AlbumShare>
{
    public void Configure(EntityTypeBuilder<AlbumShare> builder)
    {
        builder.ToTable("AlbumShares");

        builder.HasKey(x => new { x.AlbumId, x.UserId });

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasOne(x => x.Album)
            .WithMany(x => x.Shares)
            .HasForeignKey(x => x.AlbumId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // "Albums shared with me".
        builder.HasIndex(x => x.UserId);
    }
}
```

Generate the migration (no hand edits): `dotnet ef migrations add AlbumShares --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api 2>&1 | tail -3`. Read the generated `Up`: it must contain only `CreateTable("AlbumShares", …)` and `CreateIndex("IX_AlbumShares_UserId", …)`.

- [ ] **Step 4: Application types.** Create `src/PictureManager.Application/Albums/AlbumAccess.cs`:

```csharp
using PictureManager.Model;

namespace PictureManager.Application.Albums;

/// <summary>What the caller may do with an album. Ordered: each level includes everything before it.</summary>
public enum AlbumAccess
{
    Viewer,
    Editor,
    Owner
}

public static class AlbumAccessRules
{
    public static AlbumAccess From(bool isOwner, bool isEditor) =>
        isOwner ? AlbumAccess.Owner : isEditor ? AlbumAccess.Editor : AlbumAccess.Viewer;
}

/// <summary>An album the caller can reach, how, and whose it is. Album is tracked.</summary>
public sealed record AccessibleAlbum(Album Album, AlbumAccess Access, string OwnerDisplayName);

public sealed record AlbumShareRow(int UserId, string DisplayName, SharePermission Permission);
```

In `AlbumRows.cs`, give `AlbumSummaryRow` four trailing parameters: `bool IsOwner, bool IsEditor, string OwnerDisplayName, int ShareCount`.

In `ImageQueryModels.cs`, replace `public sealed record AlbumRef(int Id, string Name);` with:

```csharp
/// <summary>An album containing an image, as the caller sees it. Access is "Owner", "Editor" or "Viewer".</summary>
public sealed record AlbumRef(int Id, string Name, string Access);

public sealed record AlbumRefRow(int Id, string Name, bool IsOwner, bool IsEditor);
```

In `IImageQueryRepository.cs`, change the declaration to: `/// <summary>Albums containing the image that <paramref name="userId"/> owns or has been shared, ordered by name.</summary> Task<IReadOnlyList<AlbumRefRow>> GetAlbumsContainingAsync(int imageId, int userId, CancellationToken cancellationToken = default);`

In `IAlbumRepository.cs`, remove `GetOwnedAsync` and its summary. Change the `GetSummariesAsync` summary and parameter, and add the share methods:

```csharp
    /// <summary>
    /// Albums <paramref name="userId"/> owns or has been shared, ordered by lower(Name); cover = the chosen CoverImageId while it is
    /// still in the album with a content hash, otherwise the first entry by SortOrder with a content hash.
    /// </summary>
    Task<IReadOnlyList<AlbumSummaryRow>> GetSummariesAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>The album (tracked) with the caller's access, or null when it doesn't exist or isn't theirs or shared with them.</summary>
    Task<AccessibleAlbum?> GetAccessibleAsync(int id, int userId, CancellationToken cancellationToken = default);

    /// <summary>The album's shares, ordered by the user's display name.</summary>
    Task<IReadOnlyList<AlbumShareRow>> GetSharesAsync(int albumId, CancellationToken cancellationToken = default);

    /// <summary>Adds the share, or changes its permission when the user already has one.</summary>
    Task SetShareAsync(int albumId, int userId, SharePermission permission, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>False when the user had no share on the album.</summary>
    Task<bool> RemoveShareAsync(int albumId, int userId, CancellationToken cancellationToken = default);
```

(Add `using PictureManager.Model;` is already present.)

- [ ] **Step 5: Repository implementations.** In `AlbumRepository.cs`, delete `GetOwnedAsync` and change `GetSummariesAsync`:

```csharp
    public async Task<IReadOnlyList<AlbumSummaryRow>> GetSummariesAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Albums.AsNoTracking()
            .Where(a => a.OwnerUserId == userId || a.Shares.Any(s => s.UserId == userId))
            .OrderBy(a => a.Name.ToLower()).ThenBy(a => a.Id)
            .Select(a => new AlbumSummaryRow(
                a.Id,
                a.Name,
                a.Description,
                a.AlbumImages.Count(),
                a.AlbumImages.Where(ai => ai.Image!.ContentHash != "")
                    .OrderByDescending(ai => ai.ImageId == a.CoverImageId).ThenBy(ai => ai.SortOrder).ThenBy(ai => ai.ImageId)
                    .Select(ai => (int?)ai.ImageId).FirstOrDefault(),
                a.AlbumImages.Where(ai => ai.Image!.ContentHash != "")
                    .OrderByDescending(ai => ai.ImageId == a.CoverImageId).ThenBy(ai => ai.SortOrder).ThenBy(ai => ai.ImageId)
                    .Select(ai => ai.Image!.ContentHash).FirstOrDefault(),
                a.UpdatedAt,
                a.OwnerUserId == userId,
                a.Shares.Any(s => s.UserId == userId && s.Permission == SharePermission.Editor),
                a.OwnerUser!.DisplayName,
                a.Shares.Count()))
            .ToListAsync(cancellationToken);
    }

    public async Task<AccessibleAlbum?> GetAccessibleAsync(int id, int userId, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Albums
            .Where(a => a.Id == id && (a.OwnerUserId == userId || a.Shares.Any(s => s.UserId == userId)))
            .Select(a => new
            {
                Album = a,
                OwnerDisplayName = a.OwnerUser!.DisplayName,
                IsEditor = a.Shares.Any(s => s.UserId == userId && s.Permission == SharePermission.Editor)
            })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new AccessibleAlbum(row.Album, AlbumAccessRules.From(row.Album.OwnerUserId == userId, row.IsEditor), row.OwnerDisplayName);
    }

    public async Task<IReadOnlyList<AlbumShareRow>> GetSharesAsync(int albumId, CancellationToken cancellationToken = default) =>
        await _dbContext.AlbumShares.AsNoTracking()
            .Where(s => s.AlbumId == albumId)
            .OrderBy(s => s.User!.DisplayName).ThenBy(s => s.UserId)
            .Select(s => new AlbumShareRow(s.UserId, s.User!.DisplayName, s.Permission))
            .ToListAsync(cancellationToken);

    public async Task SetShareAsync(int albumId, int userId, SharePermission permission, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var share = await _dbContext.AlbumShares.FirstOrDefaultAsync(s => s.AlbumId == albumId && s.UserId == userId, cancellationToken);
        if (share is null)
            _dbContext.AlbumShares.Add(new AlbumShare { AlbumId = albumId, UserId = userId, Permission = permission, CreatedAt = nowUtc });
        else
            share.Permission = permission;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveShareAsync(int albumId, int userId, CancellationToken cancellationToken = default) =>
        await _dbContext.AlbumShares.Where(s => s.AlbumId == albumId && s.UserId == userId).ExecuteDeleteAsync(cancellationToken) > 0;
```

In `AppUserRepository.DeleteAsync`, inside the `if (transferAlbumsToUserId is int target)` branch after the `foreach`, add:

```csharp
            // The new owner may already have had a share on some of these albums; owning replaces it.
            var movedIds = albums.Select(a => a.Id).ToList();
            _dbContext.AlbumShares.RemoveRange(
                await _dbContext.AlbumShares.Where(s => movedIds.Contains(s.AlbumId) && s.UserId == target).ToListAsync(cancellationToken));
```

The user's own shares and favorites go with the user through the database cascade.

In `ImageQueryRepository.cs`:

```csharp
    public async Task<IReadOnlyList<AlbumRefRow>> GetAlbumsContainingAsync(int imageId, int userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AlbumImages.AsNoTracking()
            .Where(ai => ai.ImageId == imageId
                         && (ai.Album!.OwnerUserId == userId || ai.Album.Shares.Any(s => s.UserId == userId)))
            .OrderBy(ai => ai.Album!.Name.ToLower()).ThenBy(ai => ai.AlbumId)
            .Select(ai => new AlbumRefRow(
                ai.AlbumId,
                ai.Album!.Name,
                ai.Album.OwnerUserId == userId,
                ai.Album.Shares.Any(s => s.UserId == userId && s.Permission == SharePermission.Editor)))
            .ToListAsync(cancellationToken);
    }
```

- [ ] **Step 6: Keep the services compiling with today's behaviour.**
- `ImageQueryService.GetDetailAsync`: change the albums line to

```csharp
        var albums = (await _images.GetAlbumsContainingAsync(id, _currentUser.UserId, cancellationToken))
            .Select(a => new AlbumRef(a.Id, a.Name, AlbumAccessRules.From(a.IsOwner, a.IsEditor).ToString()))
            .ToList();
```

  Add `using PictureManager.Application.Albums;`. `ImageDetail` takes `IReadOnlyList<AlbumRef>`, so nothing else changes there.
- `AlbumService`: Task 3 replaces this with the access matrix. Until then, add

```csharp
    // Owner-only until the access matrix lands (Task 3).
    private async Task<Album?> OwnedAsync(int id, CancellationToken cancellationToken) =>
        await _albums.GetAccessibleAsync(id, _currentUser.UserId, cancellationToken) is { Access: AlbumAccess.Owner } owned ? owned.Album : null;
```

  Replace every `_albums.GetOwnedAsync(id, _currentUser.UserId, cancellationToken)` with `OwnedAsync(id, cancellationToken)`. In `GetAllAsync`, iterate `rows.Where(r => r.IsOwner)`.
- Test adaptations:
  - `AlbumServiceTests`:
    - `_albums.GetOwnedAsync(7, Owner, …).Returns(_album)` becomes `_albums.GetAccessibleAsync(7, Owner, Arg.Any<CancellationToken>()).Returns(new AccessibleAlbum(_album, AlbumAccess.Owner, "Administrator"));`.
    - The `GetOwnedAsync(8, …).Returns((Album?)null)` lines become `GetAccessibleAsync(8, Owner, Arg.Any<CancellationToken>()).Returns((AccessibleAlbum?)null)`.
    - Every `new AlbumSummaryRow(...)` gets the trailing `true, false, "Administrator", 0`.
  - `ImageQueryServiceTests`: return `AlbumRefRow(id, name, true, false)` from `GetAlbumsContainingAsync` substitutes, and expect `new AlbumRef(id, name, "Owner")`.
  - `AlbumQueryRepositoryTests`:
    - `GetOwnedAsync_OtherOwnersAlbum_ReturnsNull` becomes `GetAccessibleAsync_OtherOwnersUnsharedAlbum_ReturnsNull`. Assert `(await repository.GetAccessibleAsync(mine.Id, AppUser.InitialAdminId))!.Access.Should().Be(AlbumAccess.Owner)` and `GetAccessibleAsync(theirs.Id, …)` is null.
    - The `DeleteAsync` test uses `(await repository.GetAccessibleAsync(album.Id, AppUser.InitialAdminId))!.Album`.
    - Replace the `new AppUser { DisplayName = "Other", Role = UserRole.User }` literals with `TestData.User("other")`. Two users with an empty username collide on the unique username index once more than one exists.

- [ ] **Step 7: Run the tests and confirm they pass.**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests 2>&1 | tail -5` and `dotnet test tests/PictureManager.Application.Tests 2>&1 | tail -5`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat(albums): AlbumShares table, access resolution, shared summaries and share storage

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Access matrix, share endpoints and 403

**Files:**
- Modify:
  - `src/PictureManager.Application/Common/Result.cs`
  - `src/PictureManager.Application/Albums/AlbumModels.cs`, `IAlbumService.cs`, `AlbumService.cs`
  - `src/PictureManager.Api/Endpoints/ResultHttpExtensions.cs`, `AlbumEndpoints.cs`
- Test:
  - `tests/PictureManager.Application.Tests/Albums/AlbumServiceTests.cs`
  - `tests/PictureManager.Api.Tests/Smoke/SharingSmokeTests.cs` (new)
  - `tests/PictureManager.Api.Tests/Smoke/ApiSmokeTests.cs` (surface rows)

**Interfaces:**
- Consumes: Task 2's `AccessibleAlbum`, `AlbumAccess`, `AlbumAccessRules`, `AlbumShareRow`, repository share methods. Phase 2's `IAppUserRepository.GetByIdAsync`.
- Produces:
  - `ResultStatus.Forbidden` and `Result.Forbidden(string message)`
  - `ResultHttpExtensions.ToOkOrForbidden<T>()` and `ToNoContentOrForbidden()`, which add a `ProblemHttpResult` (403) to the usual union.
  - DTOs:
    - `AlbumSummary(Id, Name, Description, ImageCount, CoverThumbnailUrl, UpdatedAt, string Access, string OwnerDisplayName, int ShareCount)`
    - `AlbumDetail(Id, Name, Description, ImageCount, CreatedAt, UpdatedAt, string Access, string OwnerDisplayName)`
    - `AlbumShareDto(int UserId, string DisplayName, string Permission)`
  - `IAlbumService`:
    - `GetSharesAsync(int id, ct)` → `Result<IReadOnlyList<AlbumShareDto>>`
    - `SetShareAsync(int id, int userId, string? permission, ct)` → `Result<AlbumShareDto>`
    - `RemoveShareAsync(int id, int userId, ct)` → `Result`
  - `AlbumService(IAlbumRepository albums, IImageQueryRepository images, IFolderRepository folders, IAppUserRepository users, ICurrentUser currentUser, IClock clock)`
  - HTTP:
    - `GET /api/albums/{id}/shares` → `[{userId, displayName, permission}]`
    - `PUT /api/albums/{id}/shares/{userId}` body `{permission}` → 200 `{userId, displayName, permission}`
    - `DELETE /api/albums/{id}/shares/{userId}` → 204
    - 403 bodies are ProblemDetails with `detail`.

- [ ] **Step 1: Write the failing service tests.** In `AlbumServiceTests`:
  - Add a field `private readonly IAppUserRepository _users = Substitute.For<IAppUserRepository>();`.
  - Change `CreateService()` to `new(_albums, _images, _folders, _users, _currentUser, _clock)`.
  - In the constructor add `_users.GetByIdAsync(Owner, Arg.Any<CancellationToken>()).Returns(new AppUser { Id = Owner, DisplayName = "Administrator", IsActive = true });`.
  - Update the existing `CreateAsync_Valid_…` expectation to `new AlbumDetail(42, "Summer", "sun", 0, _clock.UtcNow, _clock.UtcNow, "Owner", "Administrator")`.
  - Delete the Task 2 `OwnedAsync`-era assumptions: none remain in tests.
  - Add:

```csharp
    private void GrantAccess(AlbumAccess access) =>
        _albums.GetAccessibleAsync(7, Owner, Arg.Any<CancellationToken>())
            .Returns(new AccessibleAlbum(_album, access, "Bob B"));

    [Theory]
    [InlineData("get", AlbumAccess.Viewer, true)]
    [InlineData("listImages", AlbumAccess.Viewer, true)]
    [InlineData("export", AlbumAccess.Viewer, true)]
    [InlineData("add", AlbumAccess.Viewer, false)]
    [InlineData("add", AlbumAccess.Editor, true)]
    [InlineData("remove", AlbumAccess.Viewer, false)]
    [InlineData("remove", AlbumAccess.Editor, true)]
    [InlineData("move", AlbumAccess.Viewer, false)]
    [InlineData("move", AlbumAccess.Editor, true)]
    [InlineData("sort", AlbumAccess.Viewer, false)]
    [InlineData("sort", AlbumAccess.Editor, true)]
    [InlineData("cover", AlbumAccess.Viewer, false)]
    [InlineData("cover", AlbumAccess.Editor, true)]
    [InlineData("rename", AlbumAccess.Editor, false)]
    [InlineData("rename", AlbumAccess.Owner, true)]
    [InlineData("delete", AlbumAccess.Editor, false)]
    [InlineData("delete", AlbumAccess.Owner, true)]
    [InlineData("shares", AlbumAccess.Editor, false)]
    [InlineData("shares", AlbumAccess.Owner, true)]
    [InlineData("share", AlbumAccess.Editor, false)]
    public async Task EachOperation_NeedsItsAccessLevel(string operation, AlbumAccess access, bool allowed)
    {
        GrantAccess(access);
        _albums.ListImagesAsync(7, Owner, Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<AlbumImageRow>());
        _albums.GetExportRowsAsync(7, Arg.Any<CancellationToken>()).Returns(new List<AlbumExportRow>());
        _albums.GetSharesAsync(7, Arg.Any<CancellationToken>()).Returns(new List<AlbumShareRow>());
        _albums.GetImageIdsSortedAsync(7, Arg.Any<AlbumSortKey>(), Arg.Any<CancellationToken>()).Returns(new List<int>());
        _images.GetVisibleIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });
        var service = CreateService();

        var status = operation switch
        {
            "get" => (await service.GetAsync(7)).Status,
            "listImages" => (await service.ListImagesAsync(7, null, null)).Status,
            "export" => (await service.ExportAsync(7, null)).Status,
            "add" => (await service.AddImagesAsync(7, new AlbumAddImages(new[] { 1 }, null))).Status,
            "remove" => (await service.RemoveImagesAsync(7, new[] { 1 })).Status,
            "move" => (await service.MoveImageAsync(7, 1, null)).Status,
            "sort" => (await service.SortAsync(7, "name")).Status,
            "cover" => (await service.SetCoverAsync(7, 1)).Status,
            "rename" => (await service.UpdateAsync(7, new AlbumUpdate("Renamed", false, null))).Status,
            "delete" => (await service.DeleteAsync(7)).Status,
            "shares" => (await service.GetSharesAsync(7)).Status,
            "share" => (await service.SetShareAsync(7, 5, "Viewer")).Status,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        // "Allowed" means the access check passed; the operation may still reject its input (e.g. move of an image not in the album).
        if (allowed)
            status.Should().NotBe(ResultStatus.Forbidden).And.NotBe(ResultStatus.NotFound);
        else
            status.Should().Be(ResultStatus.Forbidden);
    }

    [Fact]
    public async Task GetAsync_SharedAlbum_ReportsAccessAndOwner()
    {
        GrantAccess(AlbumAccess.Editor);

        var detail = (await CreateService().GetAsync(7)).Value!;

        detail.Access.Should().Be("Editor");
        detail.OwnerDisplayName.Should().Be("Bob B");
    }

    [Fact]
    public async Task GetAllAsync_MapsAccess_AndShowsTheShareCountOnlyToTheOwner()
    {
        _albums.GetSummariesAsync(Owner, Arg.Any<CancellationToken>()).Returns(new List<AlbumSummaryRow>
        {
            new(1, "Mine", null, 0, null, null, DateTime.UtcNow, true, false, "Administrator", 3),
            new(2, "Theirs", null, 0, null, null, DateTime.UtcNow, false, true, "Bob B", 4)
        });

        var albums = await CreateService().GetAllAsync();

        albums.Select(a => (a.Access, a.OwnerDisplayName, a.ShareCount))
            .Should().Equal(("Owner", "Administrator", 3), ("Editor", "Bob B", 0));
    }

    [Theory]
    [InlineData(Owner, "Viewer", "userId")]      // yourself
    [InlineData(5, "Viewer", "userId")]          // inactive
    [InlineData(6, "Viewer", "userId")]          // unknown
    [InlineData(9, "Admin", "permission")]
    [InlineData(9, "1", "permission")]
    [InlineData(9, null, "permission")]
    public async Task SetShareAsync_RejectsBadTargetsAndPermissions_WithoutStoring(int userId, string? permission, string field)
    {
        _users.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(new AppUser { Id = 5, DisplayName = "Off", IsActive = false });
        _users.GetByIdAsync(9, Arg.Any<CancellationToken>()).Returns(new AppUser { Id = 9, DisplayName = "Carol", IsActive = true });

        var result = await CreateService().SetShareAsync(7, userId, permission);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors!.Keys.Should().Contain(field);
        await _albums.DidNotReceive().SetShareAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<SharePermission>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetShareAsync_Valid_StoresAndReturnsTheShare()
    {
        _users.GetByIdAsync(9, Arg.Any<CancellationToken>()).Returns(new AppUser { Id = 9, DisplayName = "Carol", IsActive = true });

        var result = await CreateService().SetShareAsync(7, 9, "editor");

        result.Value.Should().Be(new AlbumShareDto(9, "Carol", "Editor"));
        await _albums.Received(1).SetShareAsync(7, 9, SharePermission.Editor, _clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveShareAsync_ANonOwnerMayRemoveOnlyThemselves()
    {
        GrantAccess(AlbumAccess.Viewer);
        _albums.RemoveShareAsync(7, Owner, Arg.Any<CancellationToken>()).Returns(true);

        (await CreateService().RemoveShareAsync(7, 9)).Status.Should().Be(ResultStatus.Forbidden);
        (await CreateService().RemoveShareAsync(7, Owner)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveShareAsync_OwnerRemovesAnyone_UnknownShareIsNotFound()
    {
        _albums.RemoveShareAsync(7, 9, Arg.Any<CancellationToken>()).Returns(true);

        (await CreateService().RemoveShareAsync(7, 9)).IsSuccess.Should().BeTrue();
        (await CreateService().RemoveShareAsync(7, 10)).Status.Should().Be(ResultStatus.NotFound);
    }
```

(`AlbumServiceTests` needs `using PictureManager.Model;` for `SharePermission`/`AppUser`; it is already imported.)

- [ ] **Step 2: Write the failing smoke tests.** Create `tests/PictureManager.Api.Tests/Smoke/SharingSmokeTests.cs`:

```csharp
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Api.Tests.Smoke;

public partial class ApiSmokeTests
{
    private async Task<int> CreateAlbumAsync(HttpClient client, string name)
    {
        var created = await client.PostAsJsonAsync("/api/albums", new { name });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Sharing_ViewerReads_EditorChanges_StrangerSeesNothing_AndASharerCanLeave()
    {
        var bob = await _fixture.CreateUserClientAsync("share-bob");
        var stranger = await _fixture.CreateUserClientAsync("share-stranger");
        var bobId = await UserIdAsync("share-bob");
        var albumId = await CreateAlbumAsync(_fixture.Client, "Shared smoke");

        var shared = await _fixture.Client.PutAsJsonAsync($"/api/albums/{albumId}/shares/{bobId}", new { permission = "Viewer" });
        shared.StatusCode.Should().Be(HttpStatusCode.OK);
        (await shared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("permission").GetString().Should().Be("Viewer");

        var listed = (await bob.GetFromJsonAsync<JsonElement>("/api/albums")).EnumerateArray()
            .Single(a => a.GetProperty("id").GetInt32() == albumId);
        listed.GetProperty("access").GetString().Should().Be("Viewer");
        listed.GetProperty("ownerDisplayName").GetString().Should().Be("Administrator");
        (await bob.GetAsync($"/api/albums/{albumId}/images")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await bob.GetAsync($"/api/albums/{albumId}/export")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await bob.PostAsJsonAsync($"/api/albums/{albumId}/sort", new { by = "name" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await bob.PatchAsJsonAsync($"/api/albums/{albumId}", new { name = "Mine now" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await bob.GetAsync($"/api/albums/{albumId}/shares")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.GetAsync($"/api/albums/{albumId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await _fixture.Client.PutAsJsonAsync($"/api/albums/{albumId}/shares/{bobId}", new { permission = "Editor" })).EnsureSuccessStatusCode();
        (await bob.PostAsJsonAsync($"/api/albums/{albumId}/sort", new { by = "name" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await bob.DeleteAsync($"/api/albums/{albumId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var shares = await _fixture.Client.GetFromJsonAsync<JsonElement>($"/api/albums/{albumId}/shares");
        shares.EnumerateArray().Select(s => s.GetProperty("displayName").GetString()).Should().Equal("share-bob");

        (await bob.DeleteAsync($"/api/albums/{albumId}/shares/{bobId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await bob.GetAsync($"/api/albums/{albumId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sharing_WithYourself_AnInactiveOrUnknownUser_OrABadPermission_Is400()
    {
        var me = (await _fixture.Client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt32();
        await _fixture.CreateUserClientAsync("share-off");
        var offId = await UserIdAsync("share-off");
        await _fixture.Client.PatchAsJsonAsync($"/api/users/{offId}", new { isActive = false });
        await _fixture.CreateUserClientAsync("share-target");
        var targetId = await UserIdAsync("share-target");
        var albumId = await CreateAlbumAsync(_fixture.Client, "Share validation");

        foreach (var (userId, permission) in new[] { (me, "Viewer"), (offId, "Viewer"), (999_999, "Viewer"), (targetId, "1"), (targetId, "Owner") })
            (await _fixture.Client.PutAsJsonAsync($"/api/albums/{albumId}/shares/{userId}", new { permission }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest, $"user {userId}, permission {permission}");

        (await _fixture.Client.GetFromJsonAsync<JsonElement>($"/api/albums/{albumId}/shares")).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Favorites_ArePerUser()
    {
        int imageId;
        await using (var context = _fixture.Database.CreateContext())
        {
            var image = TestData.Image(TestData.Folder(TestData.Root("smoke-favorites"), ""), "fav");
            context.Images.Add(image);
            await context.SaveChangesAsync();
            imageId = image.Id;
        }
        var bob = await _fixture.CreateUserClientAsync("fav-bob");

        (await _fixture.Client.PutAsync($"/api/images/{imageId}/favorite", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _fixture.Client.GetFromJsonAsync<JsonElement>($"/api/images/{imageId}")).GetProperty("isFavorite").GetBoolean().Should().BeTrue();
        (await bob.GetFromJsonAsync<JsonElement>($"/api/images/{imageId}")).GetProperty("isFavorite").GetBoolean().Should().BeFalse();
        (await bob.GetFromJsonAsync<JsonElement>("/api/images?favoritesOnly=true")).GetProperty("items").GetArrayLength().Should().Be(0);
    }
}
```

In `ApiSmokeTests.Endpoint_IsOnTheExpectedSurface`, add:

```csharp
    [InlineData("GET", "/api/albums/{id:int}/shares", ApiSurface.User)]
    [InlineData("PUT", "/api/albums/{id:int}/shares/{userId:int}", ApiSurface.User)]
    [InlineData("DELETE", "/api/albums/{id:int}/shares/{userId:int}", ApiSurface.User)]
```

Before writing the favorites smoke test, check that the image detail route is `GET /api/images/{id:int}` with `tokensave_search {"query": "/images/{id:int}\"", "literal": true}`. If it differs, use the real route.

- [ ] **Step 3: Run the tests and confirm they fail.**

Run: `dotnet build tests/PictureManager.Application.Tests 2>&1 | grep -c "error CS"`
Expected: non-zero (`GetSharesAsync`, `SetShareAsync`, `RemoveShareAsync`, `AlbumShareDto`, `ResultStatus.Forbidden`, and the 6-argument constructor are missing).

- [ ] **Step 4: Result and HTTP mapping.** In `Result.cs`, add `Forbidden` as the last `ResultStatus` member and:

```csharp
    /// <summary>The caller can see the resource but may not do this to it (HTTP 403).</summary>
    public static Result Forbidden(string message) => new(ResultStatus.Forbidden, Message: message);
```

In `ResultHttpExtensions.cs` add:

```csharp
    /// <summary>ToOk for resources with access levels: Forbidden becomes a 403 ProblemDetails carrying the message.</summary>
    public static Results<Ok<T>, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult> ToOkOrForbidden<T>(this Result<T> result) =>
        result.Status switch
        {
            ResultStatus.Success => TypedResults.Ok(result.Value!),
            ResultStatus.NotFound => TypedResults.NotFound(),
            ResultStatus.Invalid => TypedResults.ValidationProblem(ToErrors(result.Errors)),
            ResultStatus.Conflict => TypedResults.Conflict(ConflictProblem(result.Message)),
            ResultStatus.Forbidden => ForbiddenProblem(result.Message),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, null)
        };

    public static Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult> ToNoContentOrForbidden(this Result result) =>
        result.Status switch
        {
            ResultStatus.Success => TypedResults.NoContent(),
            ResultStatus.NotFound => TypedResults.NotFound(),
            ResultStatus.Invalid => TypedResults.ValidationProblem(ToErrors(result.Errors)),
            ResultStatus.Conflict => TypedResults.Conflict(ConflictProblem(result.Message)),
            ResultStatus.Forbidden => ForbiddenProblem(result.Message),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, null)
        };

    internal static ProblemHttpResult ForbiddenProblem(string? message) =>
        TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: message);
```

- [ ] **Step 5: DTOs and the service.** In `AlbumModels.cs`:

```csharp
/// <summary>Access is "Owner", "Editor" or "Viewer". ShareCount is how many people the caller shared it with (0 unless the caller owns it).</summary>
public sealed record AlbumSummary(
    int Id, string Name, string? Description, int ImageCount, string? CoverThumbnailUrl, DateTime UpdatedAt,
    string Access, string OwnerDisplayName, int ShareCount);

public sealed record AlbumDetail(
    int Id, string Name, string? Description, int ImageCount, DateTime CreatedAt, DateTime UpdatedAt,
    string Access, string OwnerDisplayName);

/// <summary>Permission is "Viewer" or "Editor".</summary>
public sealed record AlbumShareDto(int UserId, string DisplayName, string Permission);
```

In `IAlbumService.cs` add:

```csharp
    /// <summary>Owner only.</summary>
    Task<Result<IReadOnlyList<AlbumShareDto>>> GetSharesAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Owner only. Adds the share or changes its permission. Invalid for yourself, an inactive or unknown user, or a permission other than Viewer/Editor.</summary>
    Task<Result<AlbumShareDto>> SetShareAsync(int id, int userId, string? permission, CancellationToken cancellationToken = default);

    /// <summary>The owner may remove anyone's share; anyone else only their own (leaving the album).</summary>
    Task<Result> RemoveShareAsync(int id, int userId, CancellationToken cancellationToken = default);
```

In `AlbumService.cs`:
- Add the `IAppUserRepository users` constructor parameter (after `folders`) stored as `_users`.
- Delete Task 2's `OwnedAsync`.
- Add:

```csharp
    /// <summary>The album when the caller has at least <paramref name="needed"/>; otherwise NotFound (no access at all) or Forbidden.</summary>
    private async Task<(AccessibleAlbum? Album, Result? Failure)> RequireAsync(int id, AlbumAccess needed, CancellationToken cancellationToken)
    {
        var accessible = await _albums.GetAccessibleAsync(id, _currentUser.UserId, cancellationToken);
        if (accessible is null)
            return (null, Result.NotFound());
        if (accessible.Access < needed)
            return (null, Result.Forbidden(needed == AlbumAccess.Owner
                ? "Only the album's owner can do this."
                : "You can only view this album."));
        return (accessible, null);
    }

    private static AlbumDetail ToDetail(Album album, int imageCount, AlbumAccess access, string ownerDisplayName) =>
        new(album.Id, album.Name, album.Description, imageCount, album.CreatedAt, album.UpdatedAt, access.ToString(), ownerDisplayName);

    private static SharePermission? ParsePermission(string? raw) =>
        // Enum.TryParse also accepts numbers ("1"); only the names are valid here.
        raw is not null && !int.TryParse(raw, out _) && Enum.TryParse(raw, ignoreCase: true, out SharePermission parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;
```

  Replace the old `ToDetail`. Add `using System;` if missing.
- Apply the matrix. In each method, replace the owner lookup with `var (accessible, failure) = await RequireAsync(id, AlbumAccess.<Level>, cancellationToken); if (failure is not null) return failure;` and use `accessible!.Album` where the method used `album`:
  - `GetAsync`, `ListImagesAsync`, `ExportAsync`: **Viewer**. `GetAsync` returns `ToDetail(accessible!.Album, count, accessible.Access, accessible.OwnerDisplayName)`.
  - `AddImagesAsync`, `RemoveImagesAsync`, `MoveImageAsync`, `SortAsync`, `SetCoverAsync`: **Editor**.
  - `UpdateAsync`, `DeleteAsync`: **Owner**. `UpdateAsync` returns `ToDetail(album, count, AlbumAccess.Owner, accessible.OwnerDisplayName)`.
- `CreateAsync`:

```csharp
        var ownerName = (await _users.GetByIdAsync(_currentUser.UserId, cancellationToken))?.DisplayName ?? string.Empty;
        return Result<AlbumDetail>.Ok(ToDetail(album, 0, AlbumAccess.Owner, ownerName));
```

- `GetAllAsync`:

```csharp
    public async Task<IReadOnlyList<AlbumSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _albums.GetSummariesAsync(_currentUser.UserId, cancellationToken);
        return rows.Select(r =>
        {
            var access = AlbumAccessRules.From(r.IsOwner, r.IsEditor);
            return new AlbumSummary(
                r.Id, r.Name, r.Description, r.ImageCount,
                r.CoverImageId is int coverId && r.CoverContentHash is { } hash ? ImageUrls.Thumbnail(coverId, hash) : null,
                r.UpdatedAt, access.ToString(), r.OwnerDisplayName, access == AlbumAccess.Owner ? r.ShareCount : 0);
        }).ToList();
    }
```

- Share operations:

```csharp
    public async Task<Result<IReadOnlyList<AlbumShareDto>>> GetSharesAsync(int id, CancellationToken cancellationToken = default)
    {
        var (_, failure) = await RequireAsync(id, AlbumAccess.Owner, cancellationToken);
        if (failure is not null)
            return failure;

        var rows = await _albums.GetSharesAsync(id, cancellationToken);
        return Result<IReadOnlyList<AlbumShareDto>>.Ok(
            rows.Select(r => new AlbumShareDto(r.UserId, r.DisplayName, r.Permission.ToString())).ToList());
    }

    public async Task<Result<AlbumShareDto>> SetShareAsync(int id, int userId, string? permission, CancellationToken cancellationToken = default)
    {
        var (_, failure) = await RequireAsync(id, AlbumAccess.Owner, cancellationToken);
        if (failure is not null)
            return failure;
        if (ParsePermission(permission) is not { } parsed)
            return Result.Invalid("permission", "Must be Viewer or Editor.");
        if (userId == _currentUser.UserId)
            return Result.Invalid("userId", "You can't share an album with yourself.");
        var target = await _users.GetByIdAsync(userId, cancellationToken);
        if (target is not { IsActive: true })
            return Result.Invalid("userId", "Choose an active user.");

        await _albums.SetShareAsync(id, userId, parsed, _clock.UtcNow, cancellationToken);
        return Result<AlbumShareDto>.Ok(new AlbumShareDto(target.Id, target.DisplayName, parsed.ToString()));
    }

    public async Task<Result> RemoveShareAsync(int id, int userId, CancellationToken cancellationToken = default)
    {
        var (accessible, failure) = await RequireAsync(id, AlbumAccess.Viewer, cancellationToken);
        if (failure is not null)
            return failure;
        if (accessible!.Access != AlbumAccess.Owner && userId != _currentUser.UserId)
            return Result.Forbidden("Only the album's owner can do this.");

        return await _albums.RemoveShareAsync(id, userId, cancellationToken) ? Result.Ok() : Result.NotFound();
    }
```

  The owner equals the caller whenever `SetShareAsync` gets past `RequireAsync(Owner)`, so `userId == _currentUser.UserId` is the "share with yourself" check.

- [ ] **Step 6: Endpoints.** In `AlbumEndpoints.cs`:
  - Add `public sealed record AlbumShareRequest(string? Permission);`.
  - Map in `MapAlbumEndpoints`:

```csharp
        user.MapGet("/albums/{id:int}/shares", GetSharesAsync);
        user.MapPut("/albums/{id:int}/shares/{userId:int}", SetShareAsync);
        user.MapDelete("/albums/{id:int}/shares/{userId:int}", RemoveShareAsync);
```

  - In every handler that calls `.ToOk()` / `.ToNoContent()` (`GetAsync`, `UpdateAsync`, `DeleteAsync`, `ListImagesAsync`, `AddImagesAsync`, `RemoveImagesAsync`, `MoveImageAsync`, `SortAsync`, `SetCoverAsync`):
    - Call `.ToOkOrForbidden()` / `.ToNoContentOrForbidden()` instead.
    - Append `, ProblemHttpResult` to the `Results<…>` return type.
    - In `UpdateAsync`, the early `ResultHttpExtensions.Invalid(...)` returns still convert implicitly.
  - Add:

```csharp
    public static async Task<Results<Ok<IReadOnlyList<AlbumShareDto>>, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> GetSharesAsync(
        int id, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.GetSharesAsync(id, cancellationToken)).ToOkOrForbidden();

    public static async Task<Results<Ok<AlbumShareDto>, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> SetShareAsync(
        int id, int userId, AlbumShareRequest body, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.SetShareAsync(id, userId, body.Permission, cancellationToken)).ToOkOrForbidden();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> RemoveShareAsync(
        int id, int userId, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.RemoveShareAsync(id, userId, cancellationToken)).ToNoContentOrForbidden();
```

  If `tests/PictureManager.Api.Tests/Endpoints/AlbumEndpointsTests.cs` exists and fails to compile on the wider union, adapt its declared types only; its assertions on `.Result` stay.

- [ ] **Step 7: Run the tests and confirm they pass.** Stop any running API first.

Run: `dotnet test tests/PictureManager.Application.Tests 2>&1 | tail -5`
Run: `dotnet test tests/PictureManager.Api.Tests 2>&1 | tail -5`
Expected: PASS, including `EveryApiEndpoint_BelongsToExactlyOneSurface` and the new surface rows.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat(albums): viewer/editor/owner access matrix, share endpoints, 403 for too little access, per-user favorite smoke

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Frontend data layer, MSW access model, editable-only pickers

**Files:**
- Create: `web/src/albums/access.ts`, `web/src/api/albumShares.ts`, `web/src/shared/UserAvatar.tsx` (moved)
- Delete: `web/src/admin/UserAvatar.tsx`
- Modify:
  - `web/src/api/types.ts`
  - `web/src/admin/UsersPage.tsx` (import path)
  - `web/src/test/authHandlers.ts`, `albumHandlers.ts`, `userHandlers.ts`, `fixtures.ts`
  - `web/src/albums/AlbumPicker.tsx`
  - `web/src/viewer/PhotoViewer.tsx`
- Test: `web/src/albums/AlbumPicker.test.tsx`, `web/src/viewer/ViewerAlbums.test.tsx` (add tests)

**Interfaces:**
- Consumes: Task 3's HTTP contract. Phase 2's `GET /api/users/directory`.
- Produces:
  - `types.ts`:
    - `AlbumAccess = 'Owner' | 'Editor' | 'Viewer'`
    - `SharePermission = 'Viewer' | 'Editor'`
    - `AlbumRef = { id; name; access: AlbumAccess }`
    - `AlbumSummary` adds `access`, `ownerDisplayName`, `shareCount`
    - `AlbumDetail` adds `access`, `ownerDisplayName`
    - `AlbumShare = { userId: number; displayName: string; permission: SharePermission }`
    - `DirectoryUser = { id: number; displayName: string }`
  - `access.ts`: `canEdit(album)`, `isOwner(album)`, `PERMISSION_LABEL`
  - `albumShares.ts`: `albumSharesKey(albumId)`, `useAlbumShares(albumId)`, `useUserDirectory()`, `useSetAlbumShare(albumId)` (variables `{ userId, permission }`), `useRemoveAlbumShare(albumId)` (variable `userId`)
  - `shared/UserAvatar.tsx`: `UserAvatar({ displayName, muted?, size? })`, `initials()`
  - MSW:
    - `currentMe()` exported from `authHandlers`
    - `albumStore.addShared({ id, name, permission, imageIds? })`: owner Bob B (id 5), shared with the signed-in admin
    - `StoredAlbum.shares` and `StoredAlbum.owner`
    - share and directory handlers

- [ ] **Step 1: Write the failing tests.** Append to `web/src/albums/AlbumPicker.test.tsx`, inside its `describe` (add `import { albumStore } from '../test/albumHandlers'` if missing):

```tsx
  it('offers only albums you can add to, naming the owner of shared ones', async () => {
    albumStore.addShared({ id: 7, name: 'Bob view only', permission: 'Viewer' })
    albumStore.addShared({ id: 8, name: 'Bob editable', permission: 'Editor' })
    renderPicker()
    const list = await screen.findByRole('list', { name: 'Albums' })
    expect(within(list).getByRole('button', { name: /Bob editable/ })).toBeInTheDocument()
    expect(within(list).getByText('Shared by Bob B · 0 photos')).toBeInTheDocument()
    expect(within(list).queryByRole('button', { name: /Bob view only/ })).not.toBeInTheDocument()
  })
```

(`renderPicker` is the file's existing render helper. If it is named differently, use that name. Add `within` to the testing-library import.)

Append to `web/src/viewer/ViewerAlbums.test.tsx` (new `describe` at the end; add imports `import { http, HttpResponse } from 'msw'`, `import { server } from '../test/server'`, `import { imageDetail, holidaysImages } from '../test/fixtures'` as needed):

```tsx
describe('albums you can only view', () => {
  it('Shift+D does not remove a photo that is only in view-only albums', async () => {
    albumStore.addShared({ id: 7, name: 'Bob view only', permission: 'Viewer', imageIds: [22] })
    server.use(
      http.get('/api/images/22', () =>
        HttpResponse.json({
          ...imageDetail(holidaysImages.find((i) => i.id === 22)!),
          albums: [{ id: 7, name: 'Bob view only', access: 'Viewer' }],
        }),
      ),
    )
    const { user } = renderApp('/folders/3?image=22')
    await viewerImage('IMG_0003.jpg')
    await user.keyboard('{Shift>}D{/Shift}')
    expect(await screen.findByText('This photo is only in albums you can view.')).toBeInTheDocument()
    expect(albumStore.get(7)!.imageIds).toEqual([22])
  })

  it('Shift+A opens the picker when the last used album became view-only', async () => {
    albumStore.addShared({ id: 7, name: 'Bob view only', permission: 'Viewer' })
    localStorage.setItem('pm.albums.lastUsed', '7')
    const { user } = renderApp('/folders/3?image=22')
    await viewerImage('IMG_0003.jpg')
    await user.keyboard('{Shift>}A{/Shift}')
    expect(await screen.findByRole('dialog', { name: 'Add to album' })).toBeInTheDocument()
    expect(albumStore.get(7)!.imageIds).toEqual([])
  })
})
```

Image 22 is `holidaysImages` in folder 3 (see `fixtures.ts`). If it sits in `madeiraImages`, use that array.

- [ ] **Step 2: Run them and confirm they fail.**

Run (in `web/`): `npm run test -- src/albums/AlbumPicker.test.tsx src/viewer/ViewerAlbums.test.tsx 2>&1 | tail -15`
Expected: FAIL (`albumStore.addShared` is not a function).

- [ ] **Step 3: Types, helpers and hooks.** In `web/src/api/types.ts`:
  - Replace `export type AlbumRef = { id: number; name: string }` with:

```ts
export type AlbumAccess = 'Owner' | 'Editor' | 'Viewer'
export type SharePermission = 'Viewer' | 'Editor'

export type AlbumRef = { id: number; name: string; access: AlbumAccess }
```

  - Add `access: AlbumAccess`, `ownerDisplayName: string` and `shareCount: number` to `AlbumSummary`.
  - Add `access: AlbumAccess` and `ownerDisplayName: string` to `AlbumDetail`.
  - Add after `AlbumAddResult`:

```ts
export type AlbumShare = { userId: number; displayName: string; permission: SharePermission }

/** An active user other than the caller, as offered for sharing. */
export type DirectoryUser = { id: number; displayName: string }
```

Create `web/src/albums/access.ts`:

```ts
import type { AlbumAccess, SharePermission } from '../api/types'

type HasAccess = { access: AlbumAccess }

/** Owners and editors may add, remove, reorder and pick the cover. */
export const canEdit = (album: HasAccess): boolean => album.access !== 'Viewer'

export const isOwner = (album: HasAccess): boolean => album.access === 'Owner'

/** How a share level reads in the UI; "Viewer"/"Editor" stay API words. */
export const PERMISSION_LABEL: Record<SharePermission, string> = {
  Viewer: 'Can view',
  Editor: 'Can edit',
}
```

Create `web/src/api/albumShares.ts`:

```ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from './client'
import { queryKeys } from './queries'
import type { AlbumShare, DirectoryUser, SharePermission } from './types'

export const albumSharesKey = (albumId: number) => ['albums', albumId, 'shares'] as const

export function useAlbumShares(albumId: number) {
  return useQuery({
    queryKey: albumSharesKey(albumId),
    queryFn: ({ signal }) => apiFetch<AlbumShare[]>(`/api/albums/${albumId}/shares`, { signal }),
  })
}

export function useUserDirectory() {
  return useQuery({
    queryKey: ['users', 'directory'] as const,
    queryFn: ({ signal }) => apiFetch<DirectoryUser[]>('/api/users/directory', { signal }),
  })
}

/** Shares, or changes an existing share's permission. The album list's share counts follow. */
export function useSetAlbumShare(albumId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ userId, permission }: { userId: number; permission: SharePermission }) =>
      apiFetch<AlbumShare>(`/api/albums/${albumId}/shares/${userId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ permission }),
      }),
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: albumSharesKey(albumId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.albums() }),
      ]),
  })
}

/** The owner removing someone, or a sharer leaving (their own id). */
export function useRemoveAlbumShare(albumId: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (userId: number) =>
      apiFetch<void>(`/api/albums/${albumId}/shares/${userId}`, { method: 'DELETE' }),
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: albumSharesKey(albumId) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.albums() }),
      ]),
  })
}
```

Move `web/src/admin/UserAvatar.tsx` to `web/src/shared/UserAvatar.tsx` with `git mv`, then give it a `size`:

```tsx
/** Decorative (the name sits next to it), so it is hidden from assistive tech. */
export function UserAvatar({
  displayName,
  muted = false,
  size = 36,
}: {
  displayName: string
  muted?: boolean
  size?: number
}) {
  return (
    <Avatar
      aria-hidden
      sx={{
        width: size,
        height: size,
        fontSize: Math.round(size * 0.36),
        fontWeight: 600,
        bgcolor: muted ? 'action.hover' : alpha(ACCENT, 0.14),
        color: muted ? 'text.disabled' : ACCENT,
      }}
    >
      {initials(displayName)}
    </Avatar>
  )
}
```

In `admin/UsersPage.tsx` change the import to `import { UserAvatar } from '../shared/UserAvatar'`.

- [ ] **Step 4: MSW access model.**
- In `test/authHandlers.ts` add `/** The signed-in user, or null. */ export const currentMe = (): Me | null => current`.
- In `test/userHandlers.ts`:
  - Add `export const userById = (id: number) => users.find((u) => u.id === id)`.
  - Add the directory handler as the **first** entry of `userHandlers`:

```ts
  http.get('/api/users/directory', () => {
    const me = currentMe()
    return HttpResponse.json(
      users.filter((u) => u.isActive && u.id !== me?.id).map((u) => ({ id: u.id, displayName: u.displayName })),
    )
  }),
```

  (import `currentMe` from `./authHandlers`).
- In `test/fixtures.ts`, change `albums: [{ id: 5, name: 'Best of 2025' }]` to `albums: [{ id: 5, name: 'Best of 2025', access: 'Owner' }]`.
- In `test/albumHandlers.ts`:
  - `StoredAlbum` gains `owner: { id: number; displayName: string }` and `shares: AlbumShare[]`.
  - `resetAlbumStore` seeds `owner: { id: 1, displayName: 'Administrator' }, shares: []`.
  - The `POST /api/albums` handler sets `owner` from `currentMe()` and `shares: []`.
  - Add the helpers and use them:

```ts
const access = (album: StoredAlbum): AlbumAccess | null => {
  const me = currentMe()
  if (me === null) return null
  if (album.owner.id === me.id) return 'Owner'
  return album.shares.find((share) => share.userId === me.id)?.permission ?? null
}
const RANK: Record<AlbumAccess, number> = { Viewer: 0, Editor: 1, Owner: 2 }
const forbidden = () =>
  HttpResponse.json({ title: 'Forbidden', status: 403, detail: 'You can only view this album.' }, { status: 403 })

/** The album when the signed-in user may do this; otherwise the 404/403 response the server would send. */
function guard(params: PathParams, needed: AlbumAccess): StoredAlbum | Response {
  const album = albumFor(params)
  const level = album === undefined ? null : access(album)
  if (album === undefined || level === null) return notFound()
  return RANK[level] >= RANK[needed] ? album : forbidden()
}
```

    Every existing per-album handler starts with `const album = guard(params, '<level>'); if (album instanceof Response) return album` in place of `albumFor` + `notFound()`:
    - `Viewer` for `GET :id`, `GET :id/images`, `GET :id/export`
    - `Editor` for `POST :id/images`, `remove`, `move`, `sort`, `PUT cover`
    - `Owner` for `PATCH` and `DELETE`
  - `summary(album)` adds `access: access(album)!`, `ownerDisplayName: album.owner.displayName`, `shareCount: access(album) === 'Owner' ? album.shares.length : 0`. `detail(album)` adds `access` and `ownerDisplayName`. `GET /api/albums` returns `albums.filter((a) => access(a) !== null).map(summary)`. `nameProblem` only compares albums owned by the current user.
  - Add to `albumStore`:

```ts
  /** An album Bob B (id 5) owns and shared with the signed-in admin. */
  addShared: (album: { id: number; name: string; permission: SharePermission; imageIds?: number[] }) => {
    albums.push({
      id: album.id,
      name: album.name,
      description: null,
      imageIds: [...(album.imageIds ?? [])],
      missing: new Set<number>(),
      createdAt: '2026-09-01T10:00:00.000Z',
      updatedAt: '2026-09-01T10:00:00.000Z',
      owner: { id: 5, displayName: 'Bob B' },
      shares: [{ userId: 1, displayName: 'Administrator', permission: album.permission }],
    })
  },
```

  - Add share handlers to `albumHandlers`:

```ts
  http.get('/api/albums/:id/shares', ({ params }) => {
    const album = guard(params, 'Owner')
    return album instanceof Response ? album : HttpResponse.json(album.shares)
  }),
  http.put('/api/albums/:id/shares/:userId', async ({ params, request }) => {
    const album = guard(params, 'Owner')
    if (album instanceof Response) return album
    const userId = Number(params.userId)
    const { permission } = (await request.json()) as { permission: SharePermission }
    const target = userById(userId)
    if (target === undefined || !target.isActive || userId === currentMe()?.id) {
      return HttpResponse.json(
        { title: 'One or more validation errors occurred.', status: 400, errors: { userId: ['Choose an active user.'] } },
        { status: 400 },
      )
    }
    const share = { userId, displayName: target.displayName, permission }
    album.shares = [...album.shares.filter((s) => s.userId !== userId), share].sort((a, b) =>
      a.displayName.localeCompare(b.displayName),
    )
    return HttpResponse.json(share)
  }),
  http.delete('/api/albums/:id/shares/:userId', ({ params }) => {
    const album = guard(params, 'Viewer')
    if (album instanceof Response) return album
    const userId = Number(params.userId)
    if (access(album) !== 'Owner' && userId !== currentMe()?.id) return forbidden()
    if (!album.shares.some((s) => s.userId === userId)) return notFound()
    album.shares = album.shares.filter((s) => s.userId !== userId)
    return new HttpResponse(null, { status: 204 })
  }),
```

  (imports: `AlbumAccess`, `AlbumShare`, `SharePermission` types; `currentMe` from `./authHandlers`; `userById` from `./userHandlers`).

- [ ] **Step 5: Editable-only picker and viewer shortcuts.**
- `AlbumPicker.tsx`:
  - `const editable = (albums.data ?? []).filter(canEdit)`, and filter `editable` by the needle instead of `albums.data`.
  - The empty text compares `editable.length === 0`.
  - The secondary text is:

```tsx
            <ListItemText
              primary={album.name}
              secondary={
                isOwner(album)
                  ? photoCount(album.imageCount)
                  : `Shared by ${album.ownerDisplayName} · ${photoCount(album.imageCount)}`
              }
            />
```

- `PhotoViewer.tsx` (import `canEdit` from `../albums/access`):
  - In `quickAdd`, `const album = albums.find((a) => a.id === lastId && canEdit(a))`. A last-used album that became view-only then falls back to the picker, through the existing `album === undefined` branch, and is forgotten.
  - In `quickRemove`, replace the block from `const albums = detail.data.albums` through the empty check with:

```tsx
    const albums = detail.data.albums.filter(canEdit)
    if (albums.length === 0) {
      notify(
        detail.data.albums.length === 0
          ? "This photo isn't in any album."
          : 'This photo is only in albums you can view.',
      )
      return
    }
```

- [ ] **Step 6: Run the tests and type-check.**

Run (in `web/`): `npm run test -- src/albums src/viewer src/admin 2>&1 | tail -15` then `npx tsc -b 2>&1 | head -20`
Expected: PASS and no type errors. Fix any test fixture that builds an `AlbumSummary`/`AlbumDetail`/`AlbumRef` literal by adding the new fields (`access: 'Owner'`, `ownerDisplayName: 'Administrator'`, `shareCount: 0`).

- [ ] **Step 7: Commit**

```bash
git add web
git commit -m "feat(web): album access types, share hooks, MSW access model; pickers and shortcuts use only editable albums

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Albums page with "My albums" and "Shared with me"

**Files:**
- Modify: `web/src/albums/AlbumsPage.tsx`
- Test: `web/src/albums/AlbumsPage.test.tsx`

**Interfaces:**
- Consumes: Task 4's `isOwner`, `UserAvatar`, `albumStore.addShared`, summary `shareCount`.
- Produces: section headings "My albums" / "Shared with me" (`h2`), lists named by those headings, card titles as `h3`, an owner avatar on shared covers, and a "Shared with N person/people" pill on owned covers.

- [ ] **Step 1: Write the failing tests.** Append inside `describe('AlbumsPage', …)` (add `within` to the testing-library import and `import { albumStore } from '../test/albumHandlers'`):

```tsx
  it('splits my albums from albums shared with me, naming who shared them', async () => {
    albumStore.addShared({ id: 7, name: 'Bob trip', permission: 'Viewer', imageIds: [20] })
    renderPage()
    const shared = await screen.findByRole('list', { name: 'Shared with me' })
    expect(within(shared).getByRole('link', { name: 'Bob trip' })).toBeInTheDocument()
    expect(within(shared).getByText('1 photo · Shared by Bob B')).toBeInTheDocument()
    const mine = screen.getByRole('list', { name: 'My albums' })
    expect(within(mine).getAllByRole('link').map((c) => c.getAttribute('aria-label'))).toEqual([
      'Best of 2025',
      'Empty',
    ])
  })

  it('leaves out Shared with me when nothing is shared', async () => {
    renderPage()
    expect(await screen.findByRole('heading', { name: 'My albums' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Shared with me' })).not.toBeInTheDocument()
  })

  it('marks my albums that I shared, with how many people', async () => {
    albumStore.get(5)!.shares.push(
      { userId: 5, displayName: 'Bob B', permission: 'Viewer' },
      { userId: 6, displayName: 'Carol C', permission: 'Editor' },
    )
    renderPage()
    expect(await screen.findByLabelText('Shared with 2 people')).toBeInTheDocument()
  })

  it('says what to do when I have no albums of my own but some are shared', async () => {
    server.use(
      http.get('/api/albums', () =>
        HttpResponse.json([
          {
            id: 7, name: 'Bob trip', description: null, imageCount: 0, coverThumbnailUrl: null,
            updatedAt: '2026-09-01T10:00:00.000Z', access: 'Viewer', ownerDisplayName: 'Bob B', shareCount: 0,
          },
        ]),
      ),
    )
    renderPage()
    expect(
      await screen.findByText("You haven't made any albums yet. Select photos anywhere and choose Add to album."),
    ).toBeInTheDocument()
  })
```

The existing first test (`shows album cards…`) keeps working: it reads all links, and with nothing shared there are only the two owned ones.

- [ ] **Step 2: Run them and confirm they fail.**

Run (in `web/`): `npm run test -- src/albums/AlbumsPage.test.tsx 2>&1 | tail -15`
Expected: the new tests FAIL (no list named "Shared with me").

- [ ] **Step 3: Implement.** In `AlbumsPage.tsx`:
- Imports: `PeopleAltOutlinedIcon` from `@mui/icons-material/PeopleAltOutlined`, `Tooltip` from `@mui/material`, `useId` from `react`, `HEADING_SX` from `../design/accent`, `isOwner` from `./access`, `UserAvatar` from `../shared/UserAvatar`.
- Replace the non-empty `else` branch and `AlbumCard` with:

```tsx
  } else {
    const mine = albums.data.filter(isOwner)
    const shared = albums.data.filter((album) => !isOwner(album))
    body = (
      <Box
        className="bg-zinc-50/60 dark:bg-transparent"
        sx={{ flex: 1, overflowY: 'auto', p: 3, display: 'flex', flexDirection: 'column', gap: 4 }}
      >
        <AlbumSection
          title="My albums"
          albums={mine}
          empty="You haven't made any albums yet. Select photos anywhere and choose Add to album."
        />
        {shared.length > 0 && <AlbumSection title="Shared with me" albums={shared} />}
      </Box>
    )
  }
```

```tsx
type AlbumSectionProps = { title: string; albums: AlbumSummary[]; empty?: string }

function AlbumSection({ title, albums, empty }: AlbumSectionProps) {
  const headingId = useId()
  return (
    <Box component="section" aria-labelledby={headingId}>
      <Box sx={{ display: 'flex', alignItems: 'baseline', gap: 1, mb: 1.5 }}>
        <Typography id={headingId} variant="subtitle1" component="h2" sx={HEADING_SX}>
          {title}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ fontVariantNumeric: 'tabular-nums' }}>
          {albums.length}
        </Typography>
      </Box>
      {albums.length === 0 ? (
        <Typography variant="body2" color="text.secondary">
          {empty}
        </Typography>
      ) : (
        <Box
          component="ul"
          aria-labelledby={headingId}
          sx={{
            listStyle: 'none',
            m: 0,
            p: 0,
            display: 'grid',
            gap: 3,
            gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
          }}
        >
          {albums.map((album) => (
            <li key={album.id}>
              <AlbumCard album={album} />
            </li>
          ))}
        </Box>
      )}
    </Box>
  )
}

const COVER_HEIGHT = 160

function AlbumCard({ album }: { album: AlbumSummary }) {
  const owned = isOwner(album)
  const people = album.shareCount === 1 ? '1 person' : `${album.shareCount} people`
  return (
    <Card
      variant="outlined"
      className="overflow-hidden rounded-xl border-zinc-100 shadow-sm transition-all duration-200 ease-in-out hover:-translate-y-0.5 hover:shadow-md dark:border-zinc-800"
    >
      <CardActionArea component={RouterLink} to={`/albums/${album.id}`} aria-label={album.name}>
        <Box sx={{ position: 'relative' }}>
          {album.coverThumbnailUrl ? (
            <CardMedia component="img" image={album.coverThumbnailUrl} alt="" sx={{ height: COVER_HEIGHT, objectFit: 'cover' }} />
          ) : (
            <Box
              sx={{
                height: COVER_HEIGHT,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                bgcolor: 'action.hover',
                color: 'text.secondary',
              }}
            >
              <PhotoLibraryOutlinedIcon fontSize="large" />
            </Box>
          )}
          {owned && album.shareCount > 0 && (
            <Tooltip title={`Shared with ${people}`}>
              <Box
                aria-label={`Shared with ${people}`}
                role="img"
                sx={{
                  position: 'absolute',
                  top: 8,
                  right: 8,
                  display: 'flex',
                  alignItems: 'center',
                  gap: 0.5,
                  px: 0.75,
                  py: 0.25,
                  borderRadius: '999px',
                  bgcolor: 'rgba(17, 17, 26, 0.55)',
                  color: '#fff',
                  fontSize: 12,
                  fontWeight: 600,
                  backdropFilter: 'blur(4px)',
                }}
              >
                <PeopleAltOutlinedIcon sx={{ fontSize: 14 }} />
                {album.shareCount}
              </Box>
            </Tooltip>
          )}
          {!owned && (
            <Box
              sx={{
                position: 'absolute',
                left: 12,
                bottom: -14,
                borderRadius: '50%',
                border: 2,
                borderColor: 'background.paper',
                lineHeight: 0,
              }}
            >
              <UserAvatar displayName={album.ownerDisplayName} size={28} />
            </Box>
          )}
        </Box>
        <CardContent sx={!owned ? { pt: 2.5 } : undefined}>
          <Typography variant="subtitle1" component="h3" noWrap sx={{ fontWeight: 600 }}>
            {album.name}
          </Typography>
          <Typography variant="body2" color="text.secondary" noWrap>
            {owned
              ? `${photoCount(album.imageCount)} · Updated ${album.updatedAt.slice(0, 10)}`
              : `${photoCount(album.imageCount)} · Shared by ${album.ownerDisplayName}`}
          </Typography>
        </CardContent>
      </CardActionArea>
    </Card>
  )
}
```

The old outer `<Box component="ul" aria-label="Albums" …>` is gone. If another test queried the list named "Albums" on this page, point it at "My albums".

- [ ] **Step 4: Run the tests and type-check.**

Run (in `web/`): `npm run test -- src/albums 2>&1 | tail -15` then `npx tsc -b 2>&1 | head -20`
Expected: PASS, no type errors.

- [ ] **Step 5: Commit**

```bash
git add web
git commit -m "feat(web): Albums page splits My albums and Shared with me; owner avatar on shared covers, share count on mine

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Album view gated on access, Share dialog, Leave

**Files:**
- Create: `web/src/albums/ShareDialog.tsx`, `web/src/albums/ShareDialog.test.tsx`
- Modify: `web/src/albums/AlbumView.tsx`, `web/src/albums/AlbumGrid.tsx`
- Test: `web/src/albums/AlbumView.test.tsx` (add tests)

**Interfaces:**
- Consumes: Task 4's hooks (`useAlbumShares`, `useUserDirectory`, `useSetAlbumShare`, `useRemoveAlbumShare`), `canEdit`, `isOwner`, `PERMISSION_LABEL`, `UserAvatar`; `useCurrentUser` (`../api/auth`); MSW `albumStore`.
- Produces:
  - `ShareDialog({ album: AlbumDetail; onClose: () => void })`
  - Album header buttons: "Share…" (owner), "Leave…" (non-owner). "Sort by…" for editors and the owner. "Edit…" and "Delete…" for the owner. "Export…" for everyone.
  - `AlbumGrid` gains a `reorderable: boolean` prop.

- [ ] **Step 1: Write the failing tests.** Create `web/src/albums/ShareDialog.test.tsx`:

```tsx
import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { albumStore } from '../test/albumHandlers'
import { renderApp } from '../test/render'

async function openShare() {
  const view = renderApp('/albums/5')
  await view.user.click(await screen.findByRole('button', { name: 'Share…' }))
  return { ...view, dialog: await screen.findByRole('dialog', { name: 'Share Best of 2025' }) }
}

describe('ShareDialog', () => {
  it('starts with only the owner and says so', async () => {
    const { dialog } = await openShare()
    const people = within(dialog).getByRole('list', { name: 'People with access' })
    expect(within(people).getByText('Administrator')).toBeInTheDocument()
    expect(within(people).getByText('Owner')).toBeInTheDocument()
    expect(within(dialog).getByText('Only you can see this album.')).toBeInTheDocument()
  })

  it('shares with a person who can view, and lists them', async () => {
    const { user, dialog } = await openShare()
    await user.click(within(dialog).getByRole('combobox', { name: 'Add people' }))
    await user.click(await screen.findByRole('option', { name: 'Bob B' }))
    await user.click(within(dialog).getByRole('button', { name: 'Share' }))

    expect(await within(dialog).findByRole('combobox', { name: 'Access for Bob B' })).toHaveTextContent('Can view')
    expect(albumStore.get(5)!.shares).toEqual([{ userId: 5, displayName: 'Bob B', permission: 'Viewer' }])
  })

  it('shares as an editor when chosen', async () => {
    const { user, dialog } = await openShare()
    await user.click(within(dialog).getByRole('combobox', { name: 'Add people' }))
    await user.click(await screen.findByRole('option', { name: 'Bob B' }))
    await user.click(within(dialog).getByRole('combobox', { name: 'Access' }))
    await user.click(await screen.findByRole('option', { name: 'Can edit' }))
    await user.click(within(dialog).getByRole('button', { name: 'Share' }))

    await waitFor(() => expect(albumStore.get(5)!.shares[0]?.permission).toBe('Editor'))
  })

  it('changes a person’s access and removes them', async () => {
    albumStore.get(5)!.shares.push({ userId: 5, displayName: 'Bob B', permission: 'Viewer' })
    const { user, dialog } = await openShare()
    await user.click(await within(dialog).findByRole('combobox', { name: 'Access for Bob B' }))
    await user.click(await screen.findByRole('option', { name: 'Can edit' }))
    await waitFor(() => expect(albumStore.get(5)!.shares[0]?.permission).toBe('Editor'))

    await user.click(within(dialog).getByRole('button', { name: 'Remove Bob B' }))
    await waitFor(() => expect(albumStore.get(5)!.shares).toEqual([]))
    expect(await within(dialog).findByText('Only you can see this album.')).toBeInTheDocument()
  })

  it('does not offer people who already have access, or disabled accounts', async () => {
    albumStore.get(5)!.shares.push({ userId: 5, displayName: 'Bob B', permission: 'Viewer' })
    const { user, dialog } = await openShare()
    await within(dialog).findByRole('combobox', { name: 'Access for Bob B' })
    await user.click(within(dialog).getByRole('combobox', { name: 'Add people' }))
    expect(await screen.findByText('Everyone already has access.')).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'Carol C' })).not.toBeInTheDocument()
  })
})
```

Append inside `describe` in `web/src/albums/AlbumView.test.tsx` (add `import { albumStore } from '../test/albumHandlers'`, and `waitFor` if missing):

```tsx
  it('a viewer can browse and export a shared album but not change it', async () => {
    albumStore.addShared({ id: 7, name: 'Bob trip', permission: 'Viewer', imageIds: [20, 21] })
    renderApp('/albums/7')
    expect(await screen.findByRole('heading', { name: 'Bob trip' })).toBeInTheDocument()
    expect(screen.getByText('Shared by Bob B')).toBeInTheDocument()
    expect(screen.getByText('Can view')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Export…' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Leave…' })).toBeInTheDocument()
    for (const name of ['Sort by…', 'Edit…', 'Delete…', 'Share…'])
      expect(screen.queryByRole('button', { name })).not.toBeInTheDocument()
  })

  it('an editor can sort a shared album but not rename, delete or share it', async () => {
    albumStore.addShared({ id: 8, name: 'Bob edits', permission: 'Editor', imageIds: [20, 21] })
    renderApp('/albums/8')
    expect(await screen.findByRole('button', { name: 'Sort by…' })).toBeEnabled()
    expect(screen.getByText('Can edit')).toBeInTheDocument()
    for (const name of ['Edit…', 'Delete…', 'Share…'])
      expect(screen.queryByRole('button', { name })).not.toBeInTheDocument()
  })

  it('a viewer selecting photos gets Add to album only, and Shift+D in the viewer explains why nothing happened', async () => {
    albumStore.addShared({ id: 7, name: 'Bob trip', permission: 'Viewer', imageIds: [20, 21] })
    const { user } = renderApp('/albums/7?image=20')
    expect(await screen.findByRole('img', { name: /\.jpg$/ })).toBeInTheDocument()
    await user.keyboard('{Shift>}D{/Shift}')
    expect(await screen.findByText('You can only view this album.')).toBeInTheDocument()
    expect(albumStore.get(7)!.imageIds).toEqual([20, 21])
  })

  it('leaving a shared album confirms, returns to Albums and drops it', async () => {
    albumStore.addShared({ id: 7, name: 'Bob trip', permission: 'Viewer' })
    const { user, router } = renderApp('/albums/7')
    await user.click(await screen.findByRole('button', { name: 'Leave…' }))
    expect(screen.getByText('Leave Bob trip? It disappears from your albums until Bob B shares it again.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Leave' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/albums'))
    expect(albumStore.get(7)!.shares).toEqual([])
  })
```

(If the viewer's image `alt` for image 20 is a known file name in `fixtures.ts`, use that name instead of the regex.)

- [ ] **Step 2: Run them and confirm they fail.**

Run (in `web/`): `npm run test -- src/albums/ShareDialog.test.tsx src/albums/AlbumView.test.tsx 2>&1 | tail -15`
Expected: FAIL (no "Share…" / "Leave…" buttons).

- [ ] **Step 3: ShareDialog.** Create `web/src/albums/ShareDialog.tsx`:

```tsx
import CloseIcon from '@mui/icons-material/Close'
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  List,
  ListItem,
  MenuItem,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material'
import { useId, useState } from 'react'
import {
  useAlbumShares,
  useRemoveAlbumShare,
  useSetAlbumShare,
  useUserDirectory,
} from '../api/albumShares'
import { ApiError } from '../api/client'
import type { AlbumDetail, DirectoryUser, SharePermission } from '../api/types'
import { ACCENT } from '../design/accent'
import { UserAvatar } from '../shared/UserAvatar'
import { PERMISSION_LABEL } from './access'

const FIELD_SX = { '& .MuiOutlinedInput-root': { borderRadius: '10px' } } as const
const PERMISSIONS: SharePermission[] = ['Viewer', 'Editor']

type Props = { album: AlbumDetail; onClose: () => void }

function shareError(error: unknown): string {
  if (error instanceof ApiError) {
    const first = Object.values(error.problem?.errors ?? {})[0]?.[0]
    if (first) return first
    if (error.problem?.detail) return error.problem.detail
  }
  return "Couldn't update sharing."
}

export function ShareDialog({ album, onClose }: Props) {
  const shares = useAlbumShares(album.id)
  const directory = useUserDirectory()
  const setShare = useSetAlbumShare(album.id)
  const removeShare = useRemoveAlbumShare(album.id)
  const [person, setPerson] = useState<DirectoryUser | null>(null)
  const [permission, setPermission] = useState<SharePermission>('Viewer')
  const [error, setError] = useState<string | null>(null)
  const listId = useId()

  const sharedIds = new Set((shares.data ?? []).map((share) => share.userId))
  const candidates = (directory.data ?? []).filter((user) => !sharedIds.has(user.id))

  const run = async (action: () => Promise<unknown>) => {
    setError(null)
    try {
      await action()
    } catch (e) {
      setError(shareError(e))
    }
  }

  const share = () =>
    run(async () => {
      if (person === null) return
      await setShare.mutateAsync({ userId: person.id, permission })
      setPerson(null)
    })

  return (
    <Dialog
      open
      onClose={onClose}
      fullWidth
      maxWidth="sm"
      aria-label={`Share ${album.name}`}
      slotProps={{ paper: { className: 'rounded-2xl' } }}
    >
      <DialogTitle sx={{ fontWeight: 600 }}>Share {album.name}</DialogTitle>
      <DialogContent>
        {error !== null && (
          <Alert severity="error" sx={{ mb: 2, borderRadius: '10px' }}>
            {error}
          </Alert>
        )}
        <Box
          component="form"
          onSubmit={(event) => {
            event.preventDefault()
            void share()
          }}
          sx={{ display: 'flex', gap: 1, alignItems: 'flex-start', flexWrap: { xs: 'wrap', sm: 'nowrap' }, pt: 1 }}
        >
          <Autocomplete
            options={candidates}
            value={person}
            onChange={(_event, value) => setPerson(value)}
            getOptionLabel={(option) => option.displayName}
            isOptionEqualToValue={(option, value) => option.id === value.id}
            loading={directory.isPending}
            noOptionsText="Everyone already has access."
            renderOption={({ key, ...props }, option) => (
              <li key={key} {...props}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25 }}>
                  <UserAvatar displayName={option.displayName} size={28} />
                  {option.displayName}
                </Box>
              </li>
            )}
            renderInput={(params) => <TextField {...params} label="Add people" size="small" sx={FIELD_SX} />}
            sx={{ flex: 1, minWidth: 200 }}
          />
          <TextField
            select
            size="small"
            label="Access"
            value={permission}
            onChange={(event) => setPermission(event.target.value as SharePermission)}
            sx={{ ...FIELD_SX, width: 140 }}
          >
            {PERMISSIONS.map((p) => (
              <MenuItem key={p} value={p}>
                {PERMISSION_LABEL[p]}
              </MenuItem>
            ))}
          </TextField>
          <Button
            type="submit"
            variant="contained"
            disableElevation
            disabled={person === null || setShare.isPending}
            sx={{
              borderRadius: '10px',
              textTransform: 'none',
              fontWeight: 600,
              bgcolor: ACCENT,
              height: 40,
              '&:hover': { bgcolor: '#4b4bc4' },
            }}
          >
            Share
          </Button>
        </Box>

        <Typography id={listId} variant="subtitle2" component="h3" sx={{ mt: 3, mb: 0.5, fontWeight: 600 }}>
          People with access
        </Typography>
        <List aria-labelledby={listId} disablePadding>
          <ListItem disableGutters sx={{ gap: 1.5, py: 1 }}>
            <UserAvatar displayName={album.ownerDisplayName} size={32} />
            <Typography variant="body2" sx={{ flex: 1, fontWeight: 600 }}>
              {album.ownerDisplayName}
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={{ pr: 1 }}>
              Owner
            </Typography>
          </ListItem>
          {(shares.data ?? []).map((s) => (
            <ListItem key={s.userId} disableGutters sx={{ gap: 1.5, py: 1 }}>
              <UserAvatar displayName={s.displayName} size={32} />
              <Typography variant="body2" sx={{ flex: 1, fontWeight: 600 }} noWrap>
                {s.displayName}
              </Typography>
              <TextField
                select
                size="small"
                value={s.permission}
                onChange={(event) =>
                  void run(() =>
                    setShare.mutateAsync({ userId: s.userId, permission: event.target.value as SharePermission }),
                  )
                }
                slotProps={{ select: { inputProps: { 'aria-label': `Access for ${s.displayName}` } } }}
                sx={{ ...FIELD_SX, width: 130 }}
              >
                {PERMISSIONS.map((p) => (
                  <MenuItem key={p} value={p}>
                    {PERMISSION_LABEL[p]}
                  </MenuItem>
                ))}
              </TextField>
              <Tooltip title={`Remove ${s.displayName}`}>
                <IconButton
                  size="small"
                  aria-label={`Remove ${s.displayName}`}
                  onClick={() => void run(() => removeShare.mutateAsync(s.userId))}
                  sx={{
                    color: 'text.secondary',
                    transition: 'all 200ms ease-in-out',
                    '&:hover': { color: 'error.main' },
                  }}
                >
                  <CloseIcon fontSize="small" />
                </IconButton>
              </Tooltip>
            </ListItem>
          ))}
        </List>
        {shares.isSuccess && shares.data.length === 0 && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
            Only you can see this album.
          </Typography>
        )}
        {shares.isError && (
          <Alert severity="error" sx={{ mt: 1, borderRadius: '10px' }}>
            Couldn't load who has access.
          </Alert>
        )}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2.5, gap: 2, alignItems: 'flex-end' }}>
        <Typography variant="caption" color="text.secondary" sx={{ flex: 1, maxWidth: '60ch' }}>
          They find it under Shared with me. People who can edit add, remove and reorder photos. Only you can
          rename, delete or share it.
        </Typography>
        <Button onClick={onClose} sx={{ borderRadius: '10px', textTransform: 'none', fontWeight: 600 }}>
          Done
        </Button>
      </DialogActions>
    </Dialog>
  )
}
```

The accessible-name note: if `getByRole('combobox', { name: 'Access for Bob B' })` finds nothing because MUI 7 puts the label on a different node, set the label with `slotProps={{ htmlInput: { 'aria-label': … } }}` instead. Keep whichever makes the role query match. Same for the add row's "Access" select, which is labelled by its `label`.

- [ ] **Step 4: AlbumView and AlbumGrid.**
- In `AlbumGrid.tsx`, add `reorderable: boolean` to `Props`, destructure it, and set `disabled={selection.isSelecting || !reorderable}` on `SortableContext`.
- In `AlbumView.tsx`:
  - Imports: `Chip`, `useCurrentUser` from `../api/auth`, `useRemoveAlbumShare` from `../api/albumShares`, `canEdit`, `isOwner`, `PERMISSION_LABEL` from `./access`, `UserAvatar` from `../shared/UserAvatar`, `ShareDialog` from `./ShareDialog`.
  - `type OpenDialog = 'edit' | 'delete' | 'remove' | 'export' | 'sort' | 'share' | 'leave' | null`.
  - After the other hooks, add `const me = useCurrentUser().data` and `const leave = useRemoveAlbumShare(albumId ?? 0)`. Hooks must stay above the early returns.
  - After `const detail = album.data`, add `const editable = canEdit(detail)` and `const owner = isOwner(detail)`.
  - `removeFromViewer` first checks `if (!editable) { notify('You can only view this album.'); return false }`.
  - `SelectionBar`: `onRemove={editable ? () => setDialog('remove') : undefined}`, and `onSetCover={editable && selectedIds.length === 1 ? … : undefined}`.
  - `AlbumGrid` gets `reorderable={editable}`.
  - Add:

```tsx
  const confirmLeave = async () => {
    if (me === undefined || me === null) return
    try {
      await leave.mutateAsync(me.id)
      notify(`Left ${detail.name}.`)
      void navigate('/albums')
    } catch {
      notify("Couldn't leave the album.")
      setDialog(null)
    }
  }
```

  - In the header's title `Box`, after the description, render for non-owners:

```tsx
        {!owner && (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 0.5 }}>
            <UserAvatar displayName={detail.ownerDisplayName} size={24} />
            <Typography variant="body2" color="text.secondary" noWrap>
              Shared by {detail.ownerDisplayName}
            </Typography>
            <Chip
              size="small"
              variant="outlined"
              label={PERMISSION_LABEL[detail.access === 'Editor' ? 'Editor' : 'Viewer']}
              sx={{ height: 22, fontWeight: 500 }}
            />
          </Box>
        )}
```

  - Header buttons:
    - Wrap the "Sort by…" `Button` and its `Menu` in `{editable && (<>…</>)}`.
    - Wrap "Edit…" and "Delete…" in `{owner && (…)}` and add a "Share…" button before "Edit…" inside the same condition: `<Button size="small" onClick={() => setDialog('share')} sx={HEADER_BUTTON_SX}>Share…</Button>`.
    - After "Export…", add `{!owner && (<Button size="small" onClick={() => setDialog('leave')} sx={HEADER_BUTTON_SX}>Leave…</Button>)}`.
  - Dialogs:

```tsx
      {dialog === 'share' && <ShareDialog album={detail} onClose={() => setDialog(null)} />}
      {dialog === 'leave' && (
        <ConfirmDialog
          title="Leave album"
          message={`Leave ${detail.name}? It disappears from your albums until ${detail.ownerDisplayName} shares it again.`}
          confirmLabel="Leave"
          busy={leave.isPending}
          onConfirm={() => void confirmLeave()}
          onClose={() => setDialog(null)}
        />
      )}
```

- [ ] **Step 5: Run the tests and type-check.**

Run (in `web/`): `npm run test -- src/albums src/viewer 2>&1 | tail -15` then `npx tsc -b 2>&1 | head -20` then `npm run lint 2>&1 | grep -E "albums/|shared/UserAvatar" | head`
Expected: PASS, no type errors, no new lint errors in the touched files (the two pre-existing errors in `AppShell.tsx` and `urlState.ts` are not ours).

- [ ] **Step 6: Commit**

```bash
git add web
git commit -m "feat(web): share dialog, leave album, and album controls gated on access

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Docs and full verification

**Files:**
- Modify: `Documents/PictureManager-brief.md`, `Documents/Database-Schema.md`

- [ ] **Step 1: Docs.**
- In `Documents/PictureManager-brief.md`:
  - In the data model list, replace `IsFavorite (bool — global, NOT per-user),` with a note that favorites are per user in `UserFavorites`.
  - In the "Authentication" section, add after the user-administration bullet:
    - **Sharing:** owners share albums as Can view (Viewer) or Can edit (Editor) from the album's Share dialog. Shared albums appear under "Shared with me". Viewers browse and export. Editors also add, remove, reorder and set the cover. Only the owner renames, deletes or shares. Anyone can leave an album shared with them. No access is 404 and too little access is 403.
    - **Favorites:** favorites are per user.
- In `Documents/Database-Schema.md`:
  - Remove the `IsFavorite` row, and `IsFavorite` and "favorites" from the index sentence of the Images table.
  - Add `AlbumShares (AlbumId, UserId, Permission 0=Viewer/1=Editor, CreatedAt; PK (AlbumId, UserId); cascades from Albums and AppUsers; index UserId)` and `UserFavorites (UserId, ImageId, CreatedAt; PK (UserId, ImageId); cascades from AppUsers and Images; index ImageId)` in the same table style as the file's other entries.

- [ ] **Step 2: Full verification.** Stop any running API first.

Run: `dotnet test 2>&1 | tail -15`, then in `web/`: `npm run test 2>&1 | tail -8` and `npm run build 2>&1 | tail -5`.
Expected: all PASS; the build succeeds (the >500 kB chunk warning is pre-existing).

- [ ] **Step 3: Manual end-to-end check** (spec verification for Phase 3):
  1. Start the app with the migration applied. As `admin`, favorite a photo. Albums show under "My albums".
  2. Log in as `bob` in a private window. Bob sees no favorites, and the admin's favorite is not starred.
  3. As admin, open an album, then Share… and add Bob with Can view. The card on the Albums page shows the share pill with 1.
  4. As Bob, the album is under "Shared with me" with the admin's avatar. Inside, Export works. Sort, Edit, Delete and Share are absent. Dragging does not reorder. Shift+D says "You can only view this album."
  5. As admin, change Bob to Can edit. After a reload Bob can sort and remove photos, and the Add to album picker offers the album as "Shared by Administrator · N photos".
  6. As Bob, choose Leave…. The album disappears from Bob's list. As admin, Share… shows only the owner again.
  7. As admin, delete Bob with Transfer (Admin → Users). Bob's own albums arrive under "My albums".

- [ ] **Step 4: Commit**

```bash
git add Documents
git commit -m "docs: album sharing and per-user favorites in the brief and schema

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Self-Review Notes

- **Spec coverage:**
  - `AlbumShare` model, PK and cascades: Task 2.
  - `UserFavorite` model, migration copy to #1 and dropping the flag and indexes: Task 1.
  - `GetAccessibleAsync` replacing `GetOwnedAsync`, and the access matrix with 404 vs 403: Tasks 2 and 3.
  - `GET /albums` returning owned plus shared albums with `access`/`ownerDisplayName`, and names unique per owner (unchanged `NameExistsAsync` keyed on the owner): Tasks 2 and 3.
  - Share endpoints, and the 400 for self, inactive or unknown users: Task 3.
  - The image detail album list limited to accessible albums: Task 2.
  - Every projection and filter taking the user (`ImageProjections` factory, `ImageQueryRepository`, `AlbumRepository.ListImagesAsync`, `DuplicateService`/`ImageQueryService`): Task 1.
  - Favorite endpoints unchanged: Task 1.
  - Frontend:
    - `AlbumsPage` sections: Task 5.
    - `AlbumView` gating, `ShareDialog`, Leave: Task 6.
    - `AlbumPicker`/`useAlbumAdder` offering only editable albums (`useAlbumAdder` takes the album from its callers, which now filter): Task 4.
  - Spec Testing list: the `AlbumService` matrix with 404 vs 403 (Task 3), `GetAccessibleAsync` and the share cascade (Task 2), per-user favorite isolation (Task 1), and the favorites data migration (Task 1).
- **Additions beyond the spec, each small:**
  - `shareCount` on summaries, so owners see an album is shared.
  - The owner's own share is removed when Phase 2's delete-with-transfer hands them an album.
  - Favorites fall back to the first active admin when #1 is gone.
- **Rulings made in the plan:**
  - Toggling a favorite no longer bumps `Image.UpdatedAt`. It is per-user state now, and nothing reads `UpdatedAt` for favorites.
  - Third parties' shares survive a transfer.
  - In an album the user can only view, Shift+D explains itself instead of falling back to "remove from some other album".
- **Not in this plan:**
  - The checked-in OpenAPI files (`src/PictureManager.Api/PictureManager.Api.json`/`.yaml`) were not updated in Phase 2 either. They stay stale until someone regenerates them.
  - Notifications to people when something is shared with them.
