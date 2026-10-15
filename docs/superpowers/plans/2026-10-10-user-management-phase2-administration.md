# User Management Phase 2: User Administration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an admin list, create, edit, disable, reset the password of, and delete user accounts from an Admin → Users page, with a user directory endpoint for the later share picker.

**Architecture:** A new `IUserService` in Application (validation, guards, security-stamp rotation) over new `IAppUserRepository` methods. A new `UserEndpoints` maps `/api/users` onto the existing `admin` group, plus `GET /api/users/directory` on the `user` group. Endpoints evict the `UserSessionCache` entry after every change, so a disabled, reset or deleted user is signed out immediately instead of within 60 s. The SPA gets a `UsersPage` tab in `AdminLayout` with create, edit, reset-password and delete dialogs. No schema change and no migration.

**Tech Stack:** .NET 10 Minimal APIs, EF Core 10 + PostgreSQL, xUnit + FluentAssertions + NSubstitute; React 19 + TypeScript + MUI + TanStack Query, Vitest + MSW.

**Spec:** `docs/superpowers/specs/2026-10-09-user-management-design.md`, section "Phase 2: User administration". Phase 1 is `docs/superpowers/plans/2026-10-09-user-management-phase1-auth.md` and is already merged.

## Global Constraints

- Usernames: at most 64 characters, unique on `lower(Username)` (`AppUser.NormalizedUsername`). Display names: at most 200 characters. Passwords: at least `PasswordRules.MinLength` (8) characters, no other rules.
- A user created by an admin has `MustChangePassword = true`. An admin reset also sets `MustChangePassword = true` and rotates `SecurityStamp`.
- Role, active, folder-actions and password changes rotate `SecurityStamp`. Display-name changes do not.
- Guards (all answer 409 with a message): an admin cannot disable, demote or delete themselves; the last active admin can never be disabled, demoted or deleted.
- `DELETE /api/users/{id}?albums=transfer|delete`: `transfer` reassigns owned albums to the calling admin and, on a name clash (case-insensitive), appends " (from {username})". Album names stay at most 300 characters.
- Duplicate username is 409. Validation failures are 400 `ValidationProblem` keyed by field name (`username`, `displayName`, `password`, `newPassword`, `role`, `albums`).
- Admin endpoints are on the `admin` MapGroup (policy `AdminOnly`); the directory is on the `user` group. Every `/api` endpoint must carry `ApiSurfaceMetadata` through its group.
- C#: file-scoped namespaces, primary constructors, `Result`/`Result<T>` for service outcomes, `ResultHttpExtensions` for HTTP mapping. Frontend: strict TypeScript, explicit prop types, MUI styling like `RootFormDialog`.
- Do not run full builds only to look for errors; build/test the project you changed. Keep terminal output short (`| tail -20`).
- Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Self-lockout:** an admin PATCHes their own account to `isActive: false` or `role: "User"`, or DELETEs themselves. Expect 409 and the account unchanged (Task 1 unit tests, Task 2 smoke test).
2. **Username that differs only by case or surrounding whitespace** (`" Bob "` when `bob` exists). Expect 409, not a second account and not a database exception (Task 1 unit test, Task 2 smoke test).
3. **Album name clashes on transfer**, including two of the deleted user's albums that clash with each other once suffixed (`Trip` and `TRIP` against an existing `Trip`). Expect unique names for the receiving admin (Task 1 Postgres repository test).
4. **Open sessions after disable, reset or delete.** The user's next request is 401 immediately, not up to 60 s later (Task 2 smoke test).
5. **Bad `albums` value on delete** (missing, `"nope"`). Expect 400 and the user still exists; nothing is deleted by default (Task 2 smoke test).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/PictureManager.Application/Users/UserModels.cs` (new) | `AlbumDisposition`, `UserDto`, `UserRow`, `DirectoryEntry`, `UserCreateInput`, `UserUpdateInput` |
| `src/PictureManager.Application/Users/IUserService.cs` (new) | Service contract |
| `src/PictureManager.Application/Users/UserService.cs` (new) | Validation, guards, stamp rotation |
| `src/PictureManager.Application/Repositories/IAppUserRepository.cs` (modify) | List, directory, admin count, add, album count, delete-with-albums |
| `src/PictureManager.Infrastructure/Persistence/Repositories/AppUserRepository.cs` (modify) | Implementations |
| `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs` (modify) | Register `IUserService` |
| `src/PictureManager.Api/Endpoints/UserEndpoints.cs` (new) | `/api/users` routes, session eviction |
| `src/PictureManager.Api/Program.cs` (modify, ~line 180) | `admin.MapUserEndpoints(user)` call |
| `web/src/api/users.ts` (new) | Query and mutation hooks, error mapping |
| `web/src/api/types.ts` (modify) | `UserSummary` |
| `web/src/test/userHandlers.ts` (new) | MSW fake of the users API |
| `web/src/admin/UsersPage.tsx` (new) | Table and row actions |
| `web/src/admin/UserCreateDialog.tsx`, `UserEditDialog.tsx`, `ResetPasswordDialog.tsx`, `DeleteUserDialog.tsx` (new) | Dialogs |
| `web/src/admin/AdminLayout.tsx`, `web/src/app/routes.tsx` (modify) | Tab and route |

---

### Task 1: User service and repository

**Files:**
- Create: `src/PictureManager.Application/Users/UserModels.cs`, `IUserService.cs`, `UserService.cs`
- Modify: `src/PictureManager.Application/Repositories/IAppUserRepository.cs`, `src/PictureManager.Infrastructure/Persistence/Repositories/AppUserRepository.cs`, `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Application.Tests/Users/UserServiceTests.cs` (new), `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AppUserRepositoryTests.cs` (modify, add tests; keep the existing `SeededContextAsync` helper)

**Interfaces:**
- Consumes: `IAppUserRepository` existing methods (`GetByIdAsync`, `GetByNormalizedUsernameAsync`, `UpdateAsync`), `IPasswordHasher.Hash`, `PasswordRules.MinLength`, `IClock.UtcNow`, `ICurrentUser.UserId`, `Result`/`Result<T>`, `AppUser.NormalizeUsername`.
- Produces (Task 2 relies on these exact names):
  - `enum AlbumDisposition { Transfer, Delete }`
  - `record UserDto(int Id, string Username, string DisplayName, string Role, bool IsActive, bool CanRunFolderActions, bool MustChangePassword, DateTime CreatedAt, DateTime? LastLoginAt, int AlbumCount)`
  - `record UserCreateInput(string? Username, string? DisplayName, string? Password, string? Role, bool CanRunFolderActions)`
  - `record UserUpdateInput(string? DisplayName, string? Role, bool? IsActive, bool? CanRunFolderActions)`
  - `record DirectoryEntry(int Id, string DisplayName)`
  - `IUserService`: `Task<IReadOnlyList<UserDto>> ListAsync(ct)`, `Task<Result<UserDto>> CreateAsync(UserCreateInput, ct)`, `Task<Result<UserDto>> UpdateAsync(int id, UserUpdateInput, ct)`, `Task<Result> ResetPasswordAsync(int id, string? newPassword, ct)`, `Task<Result> DeleteAsync(int id, AlbumDisposition albums, ct)`, `Task<IReadOnlyList<DirectoryEntry>> DirectoryAsync(ct)`
  - `IAppUserRepository` additions: `ListAsync(ct)` → `IReadOnlyList<UserRow>`, `ListActiveDirectoryAsync(int excludingUserId, ct)` → `IReadOnlyList<DirectoryEntry>`, `CountActiveAdminsAsync(ct)` → `int`, `AddAsync(AppUser, ct)` → `AppUser`, `CountAlbumsAsync(int userId, ct)` → `int`, `DeleteAsync(AppUser user, int? transferAlbumsToUserId, ct)` → `Task`.

- [ ] **Step 1: Write the failing service tests.** Create `tests/PictureManager.Application.Tests/Users/UserServiceTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Users;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Users;

public class UserServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private readonly IAppUserRepository _users = Substitute.For<IAppUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ICurrentUser _caller = Substitute.For<ICurrentUser>();
    private readonly AppUser _bob = new()
    {
        Id = 5, Username = "bob", NormalizedUsername = "bob", DisplayName = "Bob", Role = UserRole.User,
        IsActive = true, PasswordHash = "hash:old-password", SecurityStamp = Guid.NewGuid()
    };
    private readonly UserService _service;

    public UserServiceTests()
    {
        _clock.UtcNow.Returns(Now);
        _caller.UserId.Returns(1);
        _caller.IsAdmin.Returns(true);
        _hasher.Hash(Arg.Any<string>()).Returns(c => "hash:" + c.Arg<string>());
        _users.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(_bob);
        _users.AddAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            var user = c.Arg<AppUser>();
            user.Id = 9;
            return user;
        });
        _users.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(2);
        _service = new UserService(_users, _hasher, _clock, _caller);
    }

    private static UserCreateInput NewUser(string? username = "carol", string? password = "long-enough-pw", string? role = "User") =>
        new(username, "Carol C", password, role, false);

    [Fact]
    public async Task Create_ValidInput_HashesPassword_ForcesPasswordChange_AndReturnsDto()
    {
        var result = await _service.CreateAsync(NewUser());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(9);
        result.Value.Username.Should().Be("carol");
        result.Value.Role.Should().Be("User");
        result.Value.MustChangePassword.Should().BeTrue();
        result.Value.AlbumCount.Should().Be(0);
        await _users.Received(1).AddAsync(Arg.Is<AppUser>(u =>
            u.NormalizedUsername == "carol" && u.PasswordHash == "hash:long-enough-pw" && u.MustChangePassword
            && u.IsActive && u.CreatedAt == Now && !u.CanRunFolderActions), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_UsernameTakenIgnoringCaseAndWhitespace_IsConflict()
    {
        _users.GetByNormalizedUsernameAsync("bob", Arg.Any<CancellationToken>()).Returns(_bob);

        var result = await _service.CreateAsync(NewUser(username: "  BoB "));

        result.Status.Should().Be(ResultStatus.Conflict);
        await _users.DidNotReceive().AddAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "username")]
    [InlineData("   ", "username")]
    [InlineData("has space", "username")]
    [InlineData("ünïcode", "username")]
    public async Task Create_BadUsername_IsInvalid(string? username, string field)
    {
        var result = await _service.CreateAsync(NewUser(username: username));

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors.Should().ContainKey(field);
    }

    [Fact]
    public async Task Create_UsernameOver64Characters_IsInvalid() =>
        (await _service.CreateAsync(NewUser(username: new string('a', 65)))).Errors.Should().ContainKey("username");

    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    public async Task Create_ShortPassword_IsInvalid(string? password) =>
        (await _service.CreateAsync(NewUser(password: password))).Errors.Should().ContainKey("password");

    [Theory]
    [InlineData(null)]
    [InlineData("root")]
    [InlineData("1")]
    public async Task Create_UnknownRole_IsInvalid(string? role) =>
        (await _service.CreateAsync(NewUser(role: role))).Errors.Should().ContainKey("role");

    [Fact]
    public async Task Create_BlankOrOverlongDisplayName_IsInvalid()
    {
        (await _service.CreateAsync(NewUser() with { DisplayName = "  " })).Errors.Should().ContainKey("displayName");
        (await _service.CreateAsync(NewUser() with { DisplayName = new string('x', 201) })).Errors.Should().ContainKey("displayName");
    }

    [Fact]
    public async Task Update_RoleChange_RotatesStamp_AndSaves()
    {
        var before = _bob.SecurityStamp;

        var result = await _service.UpdateAsync(5, new UserUpdateInput(null, "Admin", null, null));

        result.IsSuccess.Should().BeTrue();
        _bob.Role.Should().Be(UserRole.Admin);
        _bob.SecurityStamp.Should().NotBe(before);
        await _users.Received(1).UpdateAsync(_bob, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_DisplayNameOnly_DoesNotRotateStamp()
    {
        var before = _bob.SecurityStamp;

        var result = await _service.UpdateAsync(5, new UserUpdateInput("  Robert  ", null, null, null));

        result.Value!.DisplayName.Should().Be("Robert");
        _bob.SecurityStamp.Should().Be(before);
    }

    [Fact]
    public async Task Update_FolderActionsAndActiveFlags_RotateStamp()
    {
        var before = _bob.SecurityStamp;
        await _service.UpdateAsync(5, new UserUpdateInput(null, null, null, true));
        var afterFlag = _bob.SecurityStamp;
        await _service.UpdateAsync(5, new UserUpdateInput(null, null, false, null));

        _bob.CanRunFolderActions.Should().BeTrue();
        _bob.IsActive.Should().BeFalse();
        afterFlag.Should().NotBe(before);
        _bob.SecurityStamp.Should().NotBe(afterFlag);
    }

    [Fact]
    public async Task Update_UnknownUser_IsNotFound() =>
        (await _service.UpdateAsync(77, new UserUpdateInput("x", null, null, null))).Status.Should().Be(ResultStatus.NotFound);

    [Fact]
    public async Task Update_UnknownRole_IsInvalid() =>
        (await _service.UpdateAsync(5, new UserUpdateInput(null, "root", null, null))).Errors.Should().ContainKey("role");

    [Theory]
    [InlineData(false, null)]
    [InlineData(null, "User")]
    public async Task Update_AdminDisablingOrDemotingThemselves_IsConflict(bool? isActive, string? role)
    {
        var self = new AppUser { Id = 1, Username = "admin", Role = UserRole.Admin, IsActive = true };
        _users.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(self);

        var result = await _service.UpdateAsync(1, new UserUpdateInput(null, role, isActive, null));

        result.Status.Should().Be(ResultStatus.Conflict);
        self.Role.Should().Be(UserRole.Admin);
        self.IsActive.Should().BeTrue();
        await _users.DidNotReceive().UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_AdminKeepingRoleAndActive_StillAllowedOnThemselves()
    {
        var self = new AppUser { Id = 1, Username = "admin", Role = UserRole.Admin, IsActive = true };
        _users.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(self);

        var result = await _service.UpdateAsync(1, new UserUpdateInput("Boss", "Admin", true, null));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Update_DemotingTheLastActiveAdmin_IsConflict()
    {
        var other = new AppUser { Id = 6, Username = "root2", Role = UserRole.Admin, IsActive = true };
        _users.GetByIdAsync(6, Arg.Any<CancellationToken>()).Returns(other);
        _users.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(1);

        var result = await _service.UpdateAsync(6, new UserUpdateInput(null, "User", null, null));

        result.Status.Should().Be(ResultStatus.Conflict);
        other.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public async Task ResetPassword_HashesForcesChangeAndRotatesStamp()
    {
        var before = _bob.SecurityStamp;

        var result = await _service.ResetPasswordAsync(5, "brand-new-pw");

        result.IsSuccess.Should().BeTrue();
        _bob.PasswordHash.Should().Be("hash:brand-new-pw");
        _bob.MustChangePassword.Should().BeTrue();
        _bob.SecurityStamp.Should().NotBe(before);
        await _users.Received(1).UpdateAsync(_bob, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetPassword_ShortPassword_IsInvalid_UnknownUser_IsNotFound()
    {
        (await _service.ResetPasswordAsync(5, "short")).Errors.Should().ContainKey("newPassword");
        (await _service.ResetPasswordAsync(77, "long-enough-pw")).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Delete_Transfer_PassesCallerAsTarget()
    {
        var result = await _service.DeleteAsync(5, AlbumDisposition.Transfer);

        result.IsSuccess.Should().BeTrue();
        await _users.Received(1).DeleteAsync(_bob, 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_DeleteAlbums_PassesNoTarget()
    {
        await _service.DeleteAsync(5, AlbumDisposition.Delete);

        await _users.Received(1).DeleteAsync(_bob, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_Self_IsConflict()
    {
        _users.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new AppUser { Id = 1, Role = UserRole.Admin, IsActive = true });

        (await _service.DeleteAsync(1, AlbumDisposition.Transfer)).Status.Should().Be(ResultStatus.Conflict);
        await _users.DidNotReceive().DeleteAsync(Arg.Any<AppUser>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_LastActiveAdmin_IsConflict_UnknownUser_IsNotFound()
    {
        var other = new AppUser { Id = 6, Username = "root2", Role = UserRole.Admin, IsActive = true };
        _users.GetByIdAsync(6, Arg.Any<CancellationToken>()).Returns(other);
        _users.CountActiveAdminsAsync(Arg.Any<CancellationToken>()).Returns(1);

        (await _service.DeleteAsync(6, AlbumDisposition.Transfer)).Status.Should().Be(ResultStatus.Conflict);
        (await _service.DeleteAsync(77, AlbumDisposition.Transfer)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task List_MapsRowsToDtos()
    {
        _users.ListAsync(Arg.Any<CancellationToken>()).Returns(new[] { new UserRow(_bob, 3) });

        var list = await _service.ListAsync();

        list.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Id = 5, Username = "bob", Role = "User", IsActive = true, AlbumCount = 3
        });
    }

    [Fact]
    public async Task Directory_ExcludesTheCaller()
    {
        _users.ListActiveDirectoryAsync(1, Arg.Any<CancellationToken>()).Returns(new[] { new DirectoryEntry(5, "Bob") });

        (await _service.DirectoryAsync()).Should().ContainSingle().Which.DisplayName.Should().Be("Bob");
    }
}
```

- [ ] **Step 2: Write the failing repository tests.** Append to `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AppUserRepositoryTests.cs` (inside the class; add `using System.Linq; using System.Collections.Generic; using PictureManager.Application.Users; using PictureManager.Tests.Support;` at the top if missing). In-memory tests cover list, directory, counts and add; Postgres tests cover delete with albums, because album deletion relies on the database cascade to `AlbumImages`.

```csharp
    private static AppUser NewAccount(string username, UserRole role = UserRole.User, bool active = true) => new()
    {
        Username = username, NormalizedUsername = username.ToLowerInvariant(), DisplayName = username + " display",
        Role = role, IsActive = active, CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task AddAsync_ListAsync_OrderedByUsername_WithAlbumCounts()
    {
        await using var context = await SeededContextAsync();
        var repository = new AppUserRepository(context);
        var zed = await repository.AddAsync(NewAccount("zed"));
        context.Albums.Add(new Album { Name = "A", OwnerUserId = zed.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var rows = await repository.ListAsync();

        zed.Id.Should().BeGreaterThan(0);
        rows.Select(r => r.User.Username).Should().Equal("admin", "zed");
        rows.Single(r => r.User.Username == "zed").AlbumCount.Should().Be(1);
        (await repository.CountAlbumsAsync(zed.Id)).Should().Be(1);
    }

    [Fact]
    public async Task ListActiveDirectoryAsync_SkipsInactiveAndTheCaller_OrderedByDisplayName()
    {
        await using var context = await SeededContextAsync();
        var repository = new AppUserRepository(context);
        await repository.AddAsync(NewAccount("bob"));
        await repository.AddAsync(NewAccount("ann"));
        await repository.AddAsync(NewAccount("off", active: false));

        var entries = await repository.ListActiveDirectoryAsync(AppUser.InitialAdminId);

        entries.Select(e => e.DisplayName).Should().Equal("ann display", "bob display");
    }

    [Fact]
    public async Task CountActiveAdminsAsync_CountsOnlyActiveAdmins()
    {
        await using var context = await SeededContextAsync();
        var repository = new AppUserRepository(context);
        await repository.AddAsync(NewAccount("root2", UserRole.Admin));
        await repository.AddAsync(NewAccount("root3", UserRole.Admin, active: false));
        await repository.AddAsync(NewAccount("plain"));

        (await repository.CountActiveAdminsAsync()).Should().Be(2);
    }

    [Fact]
    public async Task DeleteAsync_Transfer_MovesAlbumsToTarget_SuffixingEveryClashingName()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = NewAccount("bob");
        db.Context.AppUsers.Add(bob);
        await db.Context.SaveChangesAsync();
        db.Context.Albums.AddRange(
            TestData.Album("Trip", AppUser.InitialAdminId),
            TestData.Album("Beach", bob.Id), TestData.Album("Trip", bob.Id), TestData.Album("TRIP", bob.Id));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
        {
            var repository = new AppUserRepository(context);
            await repository.DeleteAsync((await repository.GetByIdAsync(bob.Id))!, AppUser.InitialAdminId);
        }

        await using var check = db.CreateContext();
        (await check.AppUsers.AnyAsync(u => u.Id == bob.Id)).Should().BeFalse();
        var names = await check.Albums.Where(a => a.OwnerUserId == AppUser.InitialAdminId).Select(a => a.Name).ToListAsync();
        names.Should().BeEquivalentTo("Trip", "Beach", "Trip (from bob)", "TRIP (from bob) 2");
        names.Select(n => n.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task DeleteAsync_TransferWithVeryLongName_StaysWithinAlbumNameLimit()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = NewAccount("bob");
        db.Context.AppUsers.Add(bob);
        await db.Context.SaveChangesAsync();
        var longName = new string('x', 300);
        db.Context.Albums.AddRange(TestData.Album(longName, AppUser.InitialAdminId), TestData.Album(longName, bob.Id));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
        {
            var repository = new AppUserRepository(context);
            await repository.DeleteAsync((await repository.GetByIdAsync(bob.Id))!, AppUser.InitialAdminId);
        }

        await using var check = db.CreateContext();
        (await check.Albums.Where(a => a.OwnerUserId == AppUser.InitialAdminId).Select(a => a.Name.Length).ToListAsync())
            .Should().OnlyContain(length => length <= 300).And.HaveCount(2);
    }

    [Fact]
    public async Task DeleteAsync_NoTarget_DeletesTheUsersAlbumsAndTheirEntries_ButNotOthers()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = NewAccount("bob");
        db.Context.AppUsers.Add(bob);
        await db.Context.SaveChangesAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var image = TestData.Image(folder, "pic", contentHash: "H");
        var bobs = TestData.Album("Bob's", bob.Id);
        var admins = TestData.Album("Admin's", AppUser.InitialAdminId);
        db.Context.AlbumImages.AddRange(TestData.AlbumImage(bobs, image, 0), TestData.AlbumImage(admins, image, 0));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
        {
            var repository = new AppUserRepository(context);
            await repository.DeleteAsync((await repository.GetByIdAsync(bob.Id))!, null);
        }

        await using var check = db.CreateContext();
        (await check.Albums.Select(a => a.Name).ToListAsync()).Should().Equal("Admin's");
        (await check.AlbumImages.CountAsync()).Should().Be(1);
        (await check.Images.CountAsync()).Should().Be(1);
    }
```

- [ ] **Step 3: Run the tests and confirm they fail to compile.**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~UserServiceTests" 2>&1 | tail -15`
Expected: build errors for `UserService`, `UserRow`, `DirectoryEntry`, `AlbumDisposition` and the new repository methods.

- [ ] **Step 4: Implement the models.** Create `src/PictureManager.Application/Users/UserModels.cs`:

```csharp
using System;
using PictureManager.Model;

namespace PictureManager.Application.Users;

/// <summary>What happens to a deleted user's albums.</summary>
public enum AlbumDisposition
{
    Transfer,
    Delete
}

public sealed record UserDto(
    int Id, string Username, string DisplayName, string Role, bool IsActive, bool CanRunFolderActions,
    bool MustChangePassword, DateTime CreatedAt, DateTime? LastLoginAt, int AlbumCount);

/// <summary>A user with the number of albums they own, as the repository lists them.</summary>
public sealed record UserRow(AppUser User, int AlbumCount);

/// <summary>An active user as offered by the share picker.</summary>
public sealed record DirectoryEntry(int Id, string DisplayName);

public sealed record UserCreateInput(string? Username, string? DisplayName, string? Password, string? Role, bool CanRunFolderActions);

/// <summary>Null means "leave unchanged".</summary>
public sealed record UserUpdateInput(string? DisplayName, string? Role, bool? IsActive, bool? CanRunFolderActions);
```

Create `src/PictureManager.Application/Users/IUserService.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Users;

/// <summary>Admin operations on accounts. Callers evict the user's cached session afterwards (the Api layer owns that cache).</summary>
public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Conflict when the username is taken (ignoring case).</summary>
    Task<Result<UserDto>> CreateAsync(UserCreateInput input, CancellationToken cancellationToken = default);

    /// <summary>Conflict when it would disable or demote the caller, or remove the last active admin.</summary>
    Task<Result<UserDto>> UpdateAsync(int id, UserUpdateInput input, CancellationToken cancellationToken = default);

    Task<Result> ResetPasswordAsync(int id, string? newPassword, CancellationToken cancellationToken = default);

    /// <summary>Conflict when it would delete the caller or the last active admin. Albums go to the caller or are deleted.</summary>
    Task<Result> DeleteAsync(int id, AlbumDisposition albums, CancellationToken cancellationToken = default);

    /// <summary>Active users other than the caller, for choosing who to share with.</summary>
    Task<IReadOnlyList<DirectoryEntry>> DirectoryAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Implement the repository.** In `IAppUserRepository.cs` add (with `using System.Collections.Generic; using PictureManager.Application.Users;`):

```csharp
    /// <summary>Every account ordered by username, with the number of albums each owns.</summary>
    Task<IReadOnlyList<UserRow>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Active accounts except <paramref name="excludingUserId"/>, ordered by display name.</summary>
    Task<IReadOnlyList<DirectoryEntry>> ListActiveDirectoryAsync(int excludingUserId, CancellationToken cancellationToken = default);

    Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken = default);

    Task<AppUser> AddAsync(AppUser user, CancellationToken cancellationToken = default);

    Task<int> CountAlbumsAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the user in one save. Their albums move to <paramref name="transferAlbumsToUserId"/> (names that clash
    /// there, ignoring case, get " (from {username})" appended) or, when null, are deleted with their entries.
    /// </summary>
    Task DeleteAsync(AppUser user, int? transferAlbumsToUserId, CancellationToken cancellationToken = default);
```

In `AppUserRepository.cs` add `using System.Collections.Generic; using PictureManager.Application.Users;` and these members:

```csharp
    private const int MaxAlbumNameLength = 300;

    public async Task<IReadOnlyList<UserRow>> ListAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.AppUsers.AsNoTracking()
            .OrderBy(u => u.NormalizedUsername)
            .Select(u => new UserRow(u, u.Albums.Count))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DirectoryEntry>> ListActiveDirectoryAsync(int excludingUserId, CancellationToken cancellationToken = default) =>
        await _dbContext.AppUsers.AsNoTracking()
            .Where(u => u.IsActive && u.Id != excludingUserId)
            .OrderBy(u => u.DisplayName).ThenBy(u => u.Id)
            .Select(u => new DirectoryEntry(u.Id, u.DisplayName))
            .ToListAsync(cancellationToken);

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken = default) =>
        _dbContext.AppUsers.CountAsync(u => u.IsActive && u.Role == UserRole.Admin, cancellationToken);

    public async Task<AppUser> AddAsync(AppUser user, CancellationToken cancellationToken = default)
    {
        _dbContext.AppUsers.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    public Task<int> CountAlbumsAsync(int userId, CancellationToken cancellationToken = default) =>
        _dbContext.Albums.CountAsync(a => a.OwnerUserId == userId, cancellationToken);

    public async Task DeleteAsync(AppUser user, int? transferAlbumsToUserId, CancellationToken cancellationToken = default)
    {
        var albums = await _dbContext.Albums.Where(a => a.OwnerUserId == user.Id).ToListAsync(cancellationToken);
        if (transferAlbumsToUserId is int target)
        {
            var taken = (await _dbContext.Albums.Where(a => a.OwnerUserId == target).Select(a => a.Name).ToListAsync(cancellationToken))
                .Select(name => name.ToLowerInvariant()).ToHashSet();
            foreach (var album in albums.OrderBy(a => a.Id))
            {
                var name = taken.Contains(album.Name.ToLowerInvariant()) ? ClashFreeName(album.Name, user.Username, taken) : album.Name;
                taken.Add(name.ToLowerInvariant());
                album.Name = name;
                album.OwnerUserId = target;
            }
        }
        else
        {
            // The database cascades the albums' entries (AlbumImages) with them.
            _dbContext.Albums.RemoveRange(albums);
        }

        _dbContext.AppUsers.Remove(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string ClashFreeName(string name, string username, HashSet<string> taken)
    {
        var suffix = $" (from {username})";
        // Leave room for a counter so the result still fits the column.
        var stemLength = MaxAlbumNameLength - suffix.Length - 10;
        var stem = name.Length > stemLength ? name[..stemLength] : name;
        var candidate = stem + suffix;
        for (var n = 2; taken.Contains(candidate.ToLowerInvariant()); n++)
            candidate = $"{stem}{suffix} {n}";
        return candidate;
    }
```

If the Postgres delete test fails with a foreign-key error from a table other than `Albums`, another table references `AppUsers`; find it with `tokensave_search {"query": "AppUser", "literal": true}` in `Persistence/Configurations`, then delete or reassign those rows in `DeleteAsync` and add a test for it.

- [ ] **Step 6: Implement the service.** Create `src/PictureManager.Application/Users/UserService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Users;

public sealed partial class UserService(IAppUserRepository users, IPasswordHasher hasher, IClock clock, ICurrentUser caller) : IUserService
{
    public const int MaxUsernameLength = 64;
    public const int MaxDisplayNameLength = 200;

    [GeneratedRegex(@"^[A-Za-z0-9._@-]+$")]
    private static partial Regex UsernamePattern();

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken = default) =>
        (await users.ListAsync(cancellationToken)).Select(row => ToDto(row.User, row.AlbumCount)).ToList();

    public async Task<Result<UserDto>> CreateAsync(UserCreateInput input, CancellationToken cancellationToken = default)
    {
        if (ValidateUsername(input.Username, out var username) is { } badUsername) return badUsername;
        if (ValidateDisplayName(input.DisplayName, out var displayName) is { } badName) return badName;
        if (ValidatePassword(input.Password, "password") is { } badPassword) return badPassword;
        if (ParseRole(input.Role, out var role) is { } badRole) return badRole;

        var normalized = AppUser.NormalizeUsername(username);
        if (await users.GetByNormalizedUsernameAsync(normalized, cancellationToken) is not null)
            return Result.Conflict($"A user named '{username}' already exists.");

        var user = await users.AddAsync(new AppUser
        {
            Username = username,
            NormalizedUsername = normalized,
            DisplayName = displayName,
            Role = role,
            PasswordHash = hasher.Hash(input.Password!),
            IsActive = true,
            MustChangePassword = true,
            CanRunFolderActions = input.CanRunFolderActions,
            CreatedAt = clock.UtcNow
        }, cancellationToken);
        return Result<UserDto>.Ok(ToDto(user, 0));
    }

    public async Task<Result<UserDto>> UpdateAsync(int id, UserUpdateInput input, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(id, cancellationToken);
        if (user is null)
            return Result.NotFound();

        var displayName = user.DisplayName;
        if (input.DisplayName is not null && ValidateDisplayName(input.DisplayName, out displayName) is { } badName) return badName;
        var role = user.Role;
        if (input.Role is not null && ParseRole(input.Role, out role) is { } badRole) return badRole;
        var isActive = input.IsActive ?? user.IsActive;
        var canRunFolderActions = input.CanRunFolderActions ?? user.CanRunFolderActions;

        if (id == caller.UserId && (!isActive || role != UserRole.Admin))
            return Result.Conflict("You can't disable or demote your own account.");
        var removesAnAdmin = user is { IsActive: true, Role: UserRole.Admin } && (!isActive || role != UserRole.Admin);
        if (removesAnAdmin && await users.CountActiveAdminsAsync(cancellationToken) <= 1)
            return Result.Conflict("At least one active administrator is required.");

        var stampChanged = role != user.Role || isActive != user.IsActive || canRunFolderActions != user.CanRunFolderActions;
        user.DisplayName = displayName;
        user.Role = role;
        user.IsActive = isActive;
        user.CanRunFolderActions = canRunFolderActions;
        if (stampChanged)
            user.SecurityStamp = Guid.NewGuid();
        await users.UpdateAsync(user, cancellationToken);
        return Result<UserDto>.Ok(ToDto(user, await users.CountAlbumsAsync(id, cancellationToken)));
    }

    public async Task<Result> ResetPasswordAsync(int id, string? newPassword, CancellationToken cancellationToken = default)
    {
        if (ValidatePassword(newPassword, "newPassword") is { } bad) return bad;
        var user = await users.GetByIdAsync(id, cancellationToken);
        if (user is null)
            return Result.NotFound();

        user.PasswordHash = hasher.Hash(newPassword!);
        user.MustChangePassword = true;
        user.SecurityStamp = Guid.NewGuid();
        await users.UpdateAsync(user, cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> DeleteAsync(int id, AlbumDisposition albums, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(id, cancellationToken);
        if (user is null)
            return Result.NotFound();
        if (id == caller.UserId)
            return Result.Conflict("You can't delete your own account.");
        if (user is { IsActive: true, Role: UserRole.Admin } && await users.CountActiveAdminsAsync(cancellationToken) <= 1)
            return Result.Conflict("At least one active administrator is required.");

        await users.DeleteAsync(user, albums == AlbumDisposition.Transfer ? caller.UserId : null, cancellationToken);
        return Result.Ok();
    }

    public Task<IReadOnlyList<DirectoryEntry>> DirectoryAsync(CancellationToken cancellationToken = default) =>
        users.ListActiveDirectoryAsync(caller.UserId, cancellationToken);

    private static UserDto ToDto(AppUser user, int albumCount) => new(
        user.Id, user.Username, user.DisplayName, user.Role.ToString(), user.IsActive, user.CanRunFolderActions,
        user.MustChangePassword, user.CreatedAt, user.LastLoginAt, albumCount);

    private static Result? ValidateUsername(string? raw, out string username)
    {
        username = raw?.Trim() ?? string.Empty;
        if (username.Length == 0) return Result.Invalid("username", "Username is required.");
        if (username.Length > MaxUsernameLength) return Result.Invalid("username", $"Must be at most {MaxUsernameLength} characters.");
        if (!UsernamePattern().IsMatch(username)) return Result.Invalid("username", "Use letters, digits, '.', '_', '-' or '@' only.");
        return null;
    }

    private static Result? ValidateDisplayName(string? raw, out string displayName)
    {
        displayName = raw?.Trim() ?? string.Empty;
        if (displayName.Length == 0) return Result.Invalid("displayName", "Display name is required.");
        if (displayName.Length > MaxDisplayNameLength) return Result.Invalid("displayName", $"Must be at most {MaxDisplayNameLength} characters.");
        return null;
    }

    private static Result? ValidatePassword(string? password, string field) =>
        password is null || password.Length < PasswordRules.MinLength
            ? Result.Invalid(field, $"Must be at least {PasswordRules.MinLength} characters.")
            : null;

    private static Result? ParseRole(string? raw, out UserRole role)
    {
        role = UserRole.User;
        // Enum.TryParse also accepts numbers ("1"); only the names are valid here.
        if (raw is not null && !int.TryParse(raw, out _) && Enum.TryParse(raw, ignoreCase: true, out UserRole parsed) && Enum.IsDefined(parsed))
        {
            role = parsed;
            return null;
        }
        return Result.Invalid("role", "Must be User or Admin.");
    }
}
```

Register it: in `ApplicationServiceCollectionExtensions.cs` after `services.AddScoped<IAuthService, AuthService>();` add `services.AddScoped<IUserService, UserService>();`.

- [ ] **Step 7: Run the tests and confirm they pass.**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~UserServiceTests" 2>&1 | tail -15`
Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~AppUserRepositoryTests" 2>&1 | tail -15`
Expected: PASS. (The Postgres tests need the same local Postgres the other repository tests use.)

- [ ] **Step 8: Commit**

```bash
git add src/PictureManager.Application src/PictureManager.Infrastructure tests/PictureManager.Application.Tests tests/PictureManager.Infrastructure.Tests
git commit -m "feat(users): user service with create, edit, reset, delete and guards; repository list, directory and delete-with-albums

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `/api/users` endpoints

**Files:**
- Create: `src/PictureManager.Api/Endpoints/UserEndpoints.cs`
- Modify: `src/PictureManager.Api/Program.cs` (the `admin.MapRootEndpoints();` area, ~line 180)
- Test: `tests/PictureManager.Api.Tests/Smoke/UserManagementSmokeTests.cs` (new)

**Interfaces:**
- Consumes: `IUserService` and the records from Task 1; `UserSessionCache.Evict(int)` (`PictureManager.Api.Auth`); `ResultHttpExtensions.ToOk/ToNoContent/Invalid`; `ApiSmokeFixture.Client` (admin), `CreateUserClientAsync(username, canRunFolderActions, mustChangePassword)`, `LoginAsync`, `UserPassword`.
- Produces (Tasks 3-4 call these): `GET /api/users` → `UserDto[]`; `POST /api/users` body `{username, displayName, password, role, canRunFolderActions}` → 201 `UserDto`; `PATCH /api/users/{id}` body `{displayName?, role?, isActive?, canRunFolderActions?}` → 200 `UserDto`; `POST /api/users/{id}/reset-password` body `{newPassword}` → 204; `DELETE /api/users/{id}?albums=transfer|delete` → 204; `GET /api/users/directory` → `{id, displayName}[]`. JSON is camelCase; `role` is `"Admin"` or `"User"`.

- [ ] **Step 1: Write the failing smoke tests.** Create `tests/PictureManager.Api.Tests/Smoke/UserManagementSmokeTests.cs`:

```csharp
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace PictureManager.Api.Tests.Smoke;

public partial class ApiSmokeTests
{
    private static object NewUserBody(string username, string password = "long-enough-pw") =>
        new { username, displayName = username + " D", password, role = "User", canRunFolderActions = false };

    private async Task<int> UserIdAsync(string username)
    {
        var users = await _fixture.Client.GetFromJsonAsync<JsonElement>("/api/users");
        return users.EnumerateArray().Single(u => u.GetProperty("username").GetString() == username).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Admin_CreatesUser_WhoMustChangeThePasswordAtFirstLogin()
    {
        var created = await _fixture.Client.PostAsJsonAsync("/api/users", NewUserBody("created-user"));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("role").GetString().Should().Be("User");
        body.GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();

        var client = await _fixture.LoginAsync("created-user", "long-enough-pw");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        me.GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("dup-user-a", "dup-user-a")]
    [InlineData("dup-user-b", "  DUP-USER-B ")]
    public async Task Admin_CreatingADuplicateUsername_IgnoringCaseAndWhitespace_Is409(string first, string second)
    {
        (await _fixture.Client.PostAsJsonAsync("/api/users", NewUserBody(first))).EnsureSuccessStatusCode();

        var response = await _fixture.Client.PostAsJsonAsync("/api/users", NewUserBody(second));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Admin_CreatingAUserWithAShortPassword_Is400()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/users", NewUserBody("short-pw-user", "short"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("password");
    }

    [Fact]
    public async Task Admin_DisablingAUser_EndsTheirOpenSessionImmediately()
    {
        var client = await _fixture.CreateUserClientAsync("to-disable");
        (await client.GetAsync("/api/users/directory")).StatusCode.Should().Be(HttpStatusCode.OK);
        var id = await UserIdAsync("to-disable");

        var patched = await _fixture.Client.PatchAsJsonAsync($"/api/users/{id}", new { isActive = false });

        patched.StatusCode.Should().Be(HttpStatusCode.OK);
        (await patched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeFalse();
        (await client.GetAsync("/api/users/directory")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_ResettingAPassword_EndsTheirSession_AndForcesAChangeAtNextLogin()
    {
        var client = await _fixture.CreateUserClientAsync("to-reset");
        var id = await UserIdAsync("to-reset");

        var reset = await _fixture.Client.PostAsJsonAsync($"/api/users/{id}/reset-password", new { newPassword = "reset-by-admin" });

        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/users/directory")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var fresh = await _fixture.LoginAsync("to-reset", "reset-by-admin");
        (await fresh.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Admin_CannotDisableDemoteOrDeleteThemselves()
    {
        var me = await _fixture.Client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var id = me.GetProperty("id").GetInt32();

        (await _fixture.Client.PatchAsJsonAsync($"/api/users/{id}", new { isActive = false })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _fixture.Client.PatchAsJsonAsync($"/api/users/{id}", new { role = "User" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _fixture.Client.DeleteAsync($"/api/users/{id}?albums=transfer")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _fixture.Client.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_DeletingAUserWithTransfer_MovesTheirAlbumsToTheAdmin()
    {
        var client = await _fixture.CreateUserClientAsync("to-transfer");
        (await client.PostAsJsonAsync("/api/albums", new { name = "Transferred trip" })).EnsureSuccessStatusCode();
        var id = await UserIdAsync("to-transfer");

        var deleted = await _fixture.Client.DeleteAsync($"/api/users/{id}?albums=transfer");

        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var albums = await _fixture.Client.GetFromJsonAsync<JsonElement>("/api/albums");
        albums.EnumerateArray().Select(a => a.GetProperty("name").GetString()).Should().Contain("Transferred trip");
        (await client.GetAsync("/api/albums")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_DeletingAUserWithDeleteAlbums_RemovesTheirAlbums()
    {
        var client = await _fixture.CreateUserClientAsync("to-purge");
        (await client.PostAsJsonAsync("/api/albums", new { name = "Purged trip" })).EnsureSuccessStatusCode();
        var id = await UserIdAsync("to-purge");

        (await _fixture.Client.DeleteAsync($"/api/users/{id}?albums=delete")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var albums = await _fixture.Client.GetFromJsonAsync<JsonElement>("/api/albums");
        albums.EnumerateArray().Select(a => a.GetProperty("name").GetString()).Should().NotContain("Purged trip");
    }

    [Theory]
    [InlineData("")]
    [InlineData("?albums=nope")]
    public async Task Admin_DeletingWithoutAValidAlbumsChoice_Is400_AndDeletesNothing(string query)
    {
        await _fixture.CreateUserClientAsync("keep-me" + query.Length);
        var id = await UserIdAsync("keep-me" + query.Length);

        var response = await _fixture.Client.DeleteAsync($"/api/users/{id}{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await UserIdAsync("keep-me" + query.Length)).Should().Be(id);
    }

    [Fact]
    public async Task Admin_UpdatingOrDeletingAnUnknownUser_Is404()
    {
        (await _fixture.Client.PatchAsJsonAsync("/api/users/999999", new { displayName = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _fixture.Client.DeleteAsync("/api/users/999999?albums=transfer")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Directory_ListsActiveUsersExceptTheCaller_ForAPlainUser()
    {
        var client = await _fixture.CreateUserClientAsync("directory-reader");
        await _fixture.CreateUserClientAsync("directory-other");
        var offId = await UserIdAsync("directory-other");
        (await _fixture.Client.PostAsJsonAsync("/api/users", NewUserBody("directory-off"))).EnsureSuccessStatusCode();
        await _fixture.Client.PatchAsJsonAsync($"/api/users/{await UserIdAsync("directory-off")}", new { isActive = false });

        var entries = await client.GetFromJsonAsync<JsonElement>("/api/users/directory");
        var names = entries.EnumerateArray().Select(e => e.GetProperty("displayName").GetString()).ToList();

        names.Should().Contain("directory-other").And.NotContain("directory-reader").And.NotContain("directory-off D");
        entries.EnumerateArray().First().TryGetProperty("username", out _).Should().BeFalse("the directory exposes only id and display name");
        offId.Should().BeGreaterThan(0);
    }
}
```

(`CreateUserClientAsync` sets `DisplayName = username`.)

- [ ] **Step 2: Run the tests and confirm they fail.**

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~Admin_|FullyQualifiedName~Directory_" 2>&1 | tail -20`
Expected: FAIL (404 for `/api/users`).

- [ ] **Step 3: Implement the endpoints.** Create `src/PictureManager.Api/Endpoints/UserEndpoints.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Api.Auth;
using PictureManager.Application.Users;

namespace PictureManager.Api.Endpoints;

public sealed record UserCreateRequest(string? Username, string? DisplayName, string? Password, string? Role, bool CanRunFolderActions);

public sealed record UserUpdateRequest(string? DisplayName, string? Role, bool? IsActive, bool? CanRunFolderActions);

public sealed record ResetPasswordRequest(string? NewPassword);

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder admin, IEndpointRouteBuilder user)
    {
        user.MapGet("/users/directory", DirectoryAsync);
        admin.MapGet("/users", ListAsync);
        admin.MapPost("/users", CreateAsync);
        admin.MapPatch("/users/{id:int}", UpdateAsync);
        admin.MapPost("/users/{id:int}/reset-password", ResetPasswordAsync);
        admin.MapDelete("/users/{id:int}", DeleteAsync);
    }

    public static async Task<Ok<IReadOnlyList<DirectoryEntry>>> DirectoryAsync(IUserService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.DirectoryAsync(cancellationToken));

    public static async Task<Ok<IReadOnlyList<UserDto>>> ListAsync(IUserService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListAsync(cancellationToken));

    public static async Task<Results<Created<UserDto>, ValidationProblem, Conflict<ProblemDetails>>> CreateAsync(
        UserCreateRequest body, IUserService service, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(
            new UserCreateInput(body.Username, body.DisplayName, body.Password, body.Role, body.CanRunFolderActions), cancellationToken);
        return result.Status switch
        {
            ResultStatus.Success => TypedResults.Created($"/api/users/{result.Value!.Id}", result.Value),
            ResultStatus.Invalid => TypedResults.ValidationProblem(ResultHttpExtensions.ToErrors(result.Errors)),
            _ => TypedResults.Conflict(ResultHttpExtensions.ConflictProblem(result.Message))
        };
    }

    public static async Task<Results<Ok<UserDto>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> UpdateAsync(
        int id, UserUpdateRequest body, IUserService service, UserSessionCache sessions, CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, new UserUpdateInput(body.DisplayName, body.Role, body.IsActive, body.CanRunFolderActions), cancellationToken);
        sessions.Evict(id);
        return result.ToOk();
    }

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> ResetPasswordAsync(
        int id, ResetPasswordRequest body, IUserService service, UserSessionCache sessions, CancellationToken cancellationToken)
    {
        var result = await service.ResetPasswordAsync(id, body.NewPassword, cancellationToken);
        sessions.Evict(id);
        return result.ToNoContent();
    }

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> DeleteAsync(
        int id, string? albums, IUserService service, UserSessionCache sessions, CancellationToken cancellationToken)
    {
        AlbumDisposition disposition;
        if (string.Equals(albums, "transfer", StringComparison.OrdinalIgnoreCase)) disposition = AlbumDisposition.Transfer;
        else if (string.Equals(albums, "delete", StringComparison.OrdinalIgnoreCase)) disposition = AlbumDisposition.Delete;
        else return ResultHttpExtensions.Invalid("albums", "Must be 'transfer' or 'delete'.");

        var result = await service.DeleteAsync(id, disposition, cancellationToken);
        sessions.Evict(id);
        return result.ToNoContent();
    }
}
```

`ToErrors` and `ConflictProblem` are `internal` in the same assembly, so `CreateAsync` can call them. Add `using System;` and `using PictureManager.Application.Common;` (for `StringComparison` and `ResultStatus`). `Create` can only fail as Invalid or Conflict (the service never returns NotFound there), so the `_` arm is Conflict.

In `Program.cs`, after `admin.MapSettingsEndpoints();` add: `admin.MapUserEndpoints(user);`.

- [ ] **Step 4: Run the tests and confirm they pass.**

Run: `dotnet test tests/PictureManager.Api.Tests 2>&1 | tail -20`
Expected: PASS, including the existing authorization sweeps (the new admin routes answer 403 to a plain user and 401 anonymously; the directory route answers 403 to a must-change-password user).

- [ ] **Step 5: Commit**

```bash
git add src/PictureManager.Api tests/PictureManager.Api.Tests
git commit -m "feat(users): admin /api/users endpoints and user directory; sessions end immediately on change

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Frontend data layer and Users page (list + create)

**Files:**
- Create: `web/src/api/users.ts`, `web/src/test/userHandlers.ts`, `web/src/admin/UsersPage.tsx`, `web/src/admin/UserAvatar.tsx`, `web/src/admin/adminStyles.ts`, `web/src/admin/UserCreateDialog.tsx`, `web/src/admin/UsersPage.test.tsx`
- Modify: `web/src/api/types.ts` (add after `Me`), `web/src/test/handlers.ts` (spread `userHandlers` wherever `adminHandlers` is spread), `web/src/test/setup.ts` (call `resetUserStore()` in `beforeEach`), `web/src/admin/AdminLayout.tsx` (add tab `{ to: '/admin/users', label: 'Users' }`), `web/src/app/routes.tsx` (add `{ path: 'users', element: <UsersPage /> }` and its import)

**Interfaces:**
- Consumes: Task 2's endpoints; `apiFetch`, `ApiError` (`web/src/api/client.ts`); `useCurrentUser` (`web/src/api/auth.ts`); `useNotify` (`../app/notify`); `QueryErrorAlert`; `renderRoutes` (`web/src/test/render.tsx`); `adminMe` (id 1) from `web/src/test/fixtures.ts`.
- Produces:
  - `type UserSummary = { id: number; username: string; displayName: string; role: 'Admin' | 'User'; isActive: boolean; canRunFolderActions: boolean; mustChangePassword: boolean; createdAt: string; lastLoginAt: string | null; albumCount: number }` in `types.ts`.
  - `users.ts`: `usersQueryKey = ['users'] as const`; `useUsers()`; `useCreateUser()`; `useUpdateUser(id: number)`; `useResetPassword(id: number)`; `useDeleteUser()` (variables `{ id: number; albums: 'transfer' | 'delete' }`); types `UserCreateInput`, `UserUpdateInput`; `userFormErrors(error: unknown, fields: string[]): UserFormErrors` where `UserFormErrors = Record<string, string | undefined> & { form?: string }`.
  - MSW seed (used by Tasks 3-4 tests): `admin` (id 1, Admin, "Administrator", active), `bob` (id 5, User, "Bob B", active, 2 albums), `carol` (id 6, User, "Carol C", disabled, 0 albums).
  - `UsersPage` with row action buttons labelled `Edit {username}`, `Reset password for {username}`, `Delete {username}` (the last two are added in Task 4; this task renders only `Edit`-less rows plus the `New user` button).

- [ ] **Step 1: Write the failing tests.** Create `web/src/admin/UsersPage.test.tsx`:

```tsx
import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { renderRoutes } from '../test/render'
import { UsersPage } from './UsersPage'

function renderPage() {
  return renderRoutes([{ path: '/admin/users', element: <UsersPage /> }], '/admin/users')
}

describe('UsersPage', () => {
  it('lists users with role, folder-actions permission, status and album count', async () => {
    renderPage()
    const bob = (await screen.findByText('bob')).closest('tr')!
    expect(within(bob).getByText('Bob B')).toBeInTheDocument()
    expect(within(bob).getByText('User')).toBeInTheDocument()
    expect(within(bob).getByText('Active')).toBeInTheDocument()
    expect(within(bob).getByText('2')).toBeInTheDocument()

    const admin = screen.getByText('admin').closest('tr')!
    expect(within(admin).getByText('Admin')).toBeInTheDocument()
    expect(within(admin).getByText('Always')).toBeInTheDocument()

    expect(within(screen.getByText('carol').closest('tr')!).getByText('Disabled')).toBeInTheDocument()
    expect(screen.getByText('3 users')).toBeInTheDocument()
  })

  it('creates a user and shows them in the table', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'New user' }))
    await user.type(screen.getByRole('textbox', { name: 'Username' }), 'dave')
    await user.type(screen.getByRole('textbox', { name: 'Display name' }), 'Dave D')
    await user.type(screen.getByLabelText('Initial password'), 'long-enough-pw')
    await user.click(screen.getByRole('button', { name: 'Create' }))

    expect(await screen.findByText('dave')).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'New user' })).not.toBeInTheDocument()
  })

  it('shows a duplicate username on the dialog and keeps it open', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'New user' }))
    await user.type(screen.getByRole('textbox', { name: 'Username' }), 'BOB')
    await user.type(screen.getByRole('textbox', { name: 'Display name' }), 'Other Bob')
    await user.type(screen.getByLabelText('Initial password'), 'long-enough-pw')
    await user.click(screen.getByRole('button', { name: 'Create' }))

    expect(await screen.findByText("A user named 'BOB' already exists.")).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'New user' })).toBeInTheDocument()
  })

  it('shows a short password as a field error', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'New user' }))
    await user.type(screen.getByRole('textbox', { name: 'Username' }), 'erin')
    await user.type(screen.getByRole('textbox', { name: 'Display name' }), 'Erin E')
    await user.type(screen.getByLabelText('Initial password'), 'short')
    await user.click(screen.getByRole('button', { name: 'Create' }))

    expect(await screen.findByText('Must be at least 8 characters.')).toBeInTheDocument()
  })

  it('shows the folder-actions checkbox checked and disabled when the role is Admin', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'New user' }))
    const checkbox = screen.getByRole('checkbox', { name: /Can run folder actions/ })
    expect(checkbox).not.toBeChecked()
    expect(checkbox).toBeEnabled()

    await user.click(screen.getByRole('combobox', { name: 'Role' }))
    await user.click(screen.getByRole('option', { name: 'Admin' }))

    expect(checkbox).toBeChecked()
    expect(checkbox).toBeDisabled()
  })

  it('offers a retry when the list cannot be loaded', async () => {
    const { server } = await import('../test/server')
    const { http, HttpResponse } = await import('msw')
    server.use(http.get('/api/users', () => HttpResponse.json({ title: 'x', status: 500 }, { status: 500 })))
    renderPage()
    expect(await screen.findByText("Couldn't load users.")).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run the tests and confirm they fail.**

Run (in `web/`): `npm run test -- src/admin/UsersPage.test.tsx 2>&1 | tail -15`
Expected: FAIL, cannot resolve `./UsersPage`.

- [ ] **Step 3: Implement types, hooks and the MSW fake.** In `web/src/api/types.ts` after the `Me` type add:

```ts
export type UserSummary = {
  id: number
  username: string
  displayName: string
  role: 'Admin' | 'User'
  isActive: boolean
  canRunFolderActions: boolean
  mustChangePassword: boolean
  createdAt: string
  lastLoginAt: string | null
  albumCount: number
}
```

Create `web/src/api/users.ts`:

```ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, apiFetch } from './client'
import type { UserSummary } from './types'

export const usersQueryKey = ['users'] as const

export type UserCreateInput = {
  username: string
  displayName: string
  password: string
  role: 'Admin' | 'User'
  canRunFolderActions: boolean
}

export type UserUpdateInput = {
  displayName?: string
  role?: 'Admin' | 'User'
  isActive?: boolean
  canRunFolderActions?: boolean
}

const json = (method: string, body: unknown): RequestInit => ({
  method,
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
})

export function useUsers() {
  return useQuery({
    queryKey: usersQueryKey,
    queryFn: ({ signal }) => apiFetch<UserSummary[]>('/api/users', { signal }),
  })
}

export function useCreateUser() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: UserCreateInput) => apiFetch<UserSummary>('/api/users', json('POST', input)),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: usersQueryKey }),
  })
}

export function useUpdateUser(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: UserUpdateInput) => apiFetch<UserSummary>(`/api/users/${id}`, json('PATCH', input)),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: usersQueryKey }),
  })
}

export function useResetPassword(id: number) {
  return useMutation({
    mutationFn: (newPassword: string) =>
      apiFetch<void>(`/api/users/${id}/reset-password`, json('POST', { newPassword })),
  })
}

export function useDeleteUser() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, albums }: { id: number; albums: 'transfer' | 'delete' }) =>
      apiFetch<void>(`/api/users/${id}?albums=${albums}`, { method: 'DELETE' }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: usersQueryKey }),
  })
}

export type UserFormErrors = Record<string, string | undefined> & { form?: string }

/**
 * Maps a failed user request onto the form: a 400 lands on its field (only names in `fields`),
 * anything else (including a 409 guard message) on the form as a whole.
 */
export function userFormErrors(error: unknown, fields: string[]): UserFormErrors {
  if (error instanceof ApiError) {
    if (error.status === 400) {
      const mapped: UserFormErrors = {}
      for (const [key, messages] of Object.entries(error.problem?.errors ?? {})) {
        const field = fields.find((f) => f.toLowerCase() === key.toLowerCase())
        if (field && messages[0]) mapped[field] = messages[0]
      }
      if (Object.keys(mapped).length > 0) return mapped
    }
    if (error.status === 409 && error.problem?.detail) return { form: error.problem.detail }
  }
  return { form: "Couldn't save the user." }
}
```

Create `web/src/test/userHandlers.ts`. It must mirror the server rules the tests rely on (duplicate → 409 `A user named '{username}' already exists.`, short password → 400 on `password`/`newPassword`, self guards → 409, delete needs a valid `albums`):

```ts
import { http, HttpResponse } from 'msw'
import type { UserSummary } from '../api/types'

const seed: UserSummary[] = [
  { id: 1, username: 'admin', displayName: 'Administrator', role: 'Admin', isActive: true, canRunFolderActions: false, mustChangePassword: false, createdAt: '2026-09-21T00:00:00Z', lastLoginAt: '2026-10-10T08:00:00Z', albumCount: 3 },
  { id: 5, username: 'bob', displayName: 'Bob B', role: 'User', isActive: true, canRunFolderActions: false, mustChangePassword: false, createdAt: '2026-10-01T00:00:00Z', lastLoginAt: null, albumCount: 2 },
  { id: 6, username: 'carol', displayName: 'Carol C', role: 'User', isActive: false, canRunFolderActions: true, mustChangePassword: false, createdAt: '2026-10-02T00:00:00Z', lastLoginAt: null, albumCount: 0 },
]
const SELF_ID = 1

let users: UserSummary[]
let nextId: number
/** What the last reset-password call received, for assertions. */
export const resetCalls: { id: number; newPassword: string }[] = []
/** The `albums` value of every delete call, for assertions. */
export const deleteCalls: { id: number; albums: string | null }[] = []

/** Called before every test (setup.ts). */
export function resetUserStore(): void {
  users = seed.map((u) => ({ ...u }))
  nextId = 100
  resetCalls.length = 0
  deleteCalls.length = 0
}
resetUserStore()

const problem = (status: number, extra: Record<string, unknown>) =>
  HttpResponse.json({ title: 'Problem', status, ...extra }, { status })
const invalid = (field: string, message: string) => problem(400, { errors: { [field]: [message] } })

export const userHandlers = [
  http.get('/api/users', () => HttpResponse.json(users)),
  http.post('/api/users', async ({ request }) => {
    const body = (await request.json()) as {
      username: string; displayName: string; password: string; role: 'Admin' | 'User'; canRunFolderActions: boolean
    }
    const username = body.username.trim()
    if (users.some((u) => u.username.toLowerCase() === username.toLowerCase()))
      return problem(409, { detail: `A user named '${username}' already exists.` })
    if (body.password.length < 8) return invalid('password', 'Must be at least 8 characters.')
    const created: UserSummary = {
      id: nextId++, username, displayName: body.displayName.trim(), role: body.role, isActive: true,
      canRunFolderActions: body.canRunFolderActions, mustChangePassword: true,
      createdAt: new Date().toISOString(), lastLoginAt: null, albumCount: 0,
    }
    users.push(created)
    return HttpResponse.json(created, { status: 201 })
  }),
  http.patch('/api/users/:id', async ({ params, request }) => {
    const id = Number(params.id)
    const user = users.find((u) => u.id === id)
    if (!user) return problem(404, {})
    const body = (await request.json()) as Partial<Pick<UserSummary, 'displayName' | 'role' | 'isActive' | 'canRunFolderActions'>>
    if (id === SELF_ID && (body.isActive === false || body.role === 'User'))
      return problem(409, { detail: "You can't disable or demote your own account." })
    if (body.displayName !== undefined && body.displayName.trim() === '')
      return invalid('displayName', 'Display name is required.')
    Object.assign(user, { ...body, displayName: body.displayName?.trim() ?? user.displayName })
    return HttpResponse.json(user)
  }),
  http.post('/api/users/:id/reset-password', async ({ params, request }) => {
    const id = Number(params.id)
    if (!users.some((u) => u.id === id)) return problem(404, {})
    const { newPassword } = (await request.json()) as { newPassword: string }
    if (newPassword.length < 8) return invalid('newPassword', 'Must be at least 8 characters.')
    resetCalls.push({ id, newPassword })
    return new HttpResponse(null, { status: 204 })
  }),
  http.delete('/api/users/:id', ({ params, request }) => {
    const id = Number(params.id)
    const albums = new URL(request.url).searchParams.get('albums')
    deleteCalls.push({ id, albums })
    if (albums !== 'transfer' && albums !== 'delete') return invalid('albums', "Must be 'transfer' or 'delete'.")
    if (id === SELF_ID) return problem(409, { detail: "You can't delete your own account." })
    if (!users.some((u) => u.id === id)) return problem(404, {})
    users = users.filter((u) => u.id !== id)
    return new HttpResponse(null, { status: 204 })
  }),
]
```

Wire it: in `handlers.ts` add `import { userHandlers } from './userHandlers'` and spread `...userHandlers` next to `...adminHandlers`; in `setup.ts` import `resetUserStore` and call it in `beforeEach`.

- [ ] **Step 4: Implement the dialog and page.** Create `web/src/admin/UserCreateDialog.tsx`:

```tsx
import {
  Alert,
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  MenuItem,
  TextField,
} from '@mui/material'
import { useState, type FormEvent } from 'react'
import { useCreateUser, userFormErrors, type UserFormErrors } from '../api/users'
import { BUTTON_SX, DIALOG_PAPER_SLOT, FIELD_SX, PRIMARY_BUTTON_SX } from './adminStyles'

const FIELDS = ['username', 'displayName', 'password', 'role']

type Props = { onClose: () => void }

export function UserCreateDialog({ onClose }: Props) {
  const [username, setUsername] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState<'Admin' | 'User'>('User')
  const [canRunFolderActions, setCanRunFolderActions] = useState(false)
  const [errors, setErrors] = useState<UserFormErrors>({})
  const create = useCreateUser()

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setErrors({})
    try {
      await create.mutateAsync({
        username: username.trim(),
        displayName: displayName.trim(),
        password,
        role,
        canRunFolderActions: role === 'Admin' || canRunFolderActions,
      })
      onClose()
    } catch (error) {
      setErrors(userFormErrors(error, FIELDS))
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs" slotProps={DIALOG_PAPER_SLOT}>
      <form onSubmit={(event) => void submit(event)}>
        <DialogTitle sx={{ fontWeight: 600 }}>New user</DialogTitle>
        <DialogContent>
          {errors.form !== undefined && (
            <Alert severity="error" sx={{ mb: 2, borderRadius: '10px' }}>{errors.form}</Alert>
          )}
          <TextField label="Username" value={username} onChange={(e) => setUsername(e.target.value)} autoFocus fullWidth margin="dense" error={errors.username !== undefined} helperText={errors.username} sx={FIELD_SX} />
          <TextField label="Display name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} fullWidth margin="dense" error={errors.displayName !== undefined} helperText={errors.displayName} sx={FIELD_SX} />
          <TextField label="Initial password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} fullWidth margin="dense" autoComplete="new-password" error={errors.password !== undefined} helperText={errors.password ?? 'At least 8 characters. They must change it at first login.'} sx={FIELD_SX} />
          <TextField select label="Role" value={role} onChange={(e) => setRole(e.target.value as 'Admin' | 'User')} fullWidth margin="dense" error={errors.role !== undefined} helperText={errors.role} sx={FIELD_SX}>
            <MenuItem value="User">User</MenuItem>
            <MenuItem value="Admin">Admin</MenuItem>
          </TextField>
          <FormControlLabel
            control={
              <Checkbox
                checked={role === 'Admin' || canRunFolderActions}
                disabled={role === 'Admin'}
                onChange={(e) => setCanRunFolderActions(e.target.checked)}
              />
            }
            label="Can run folder actions (scan, refresh, face recognition)"
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2.5 }}>
          <Button onClick={onClose} sx={BUTTON_SX}>Cancel</Button>
          <Button
            type="submit"
            variant="contained"
            disableElevation
            disabled={create.isPending || username.trim() === '' || displayName.trim() === '' || password === ''}
            sx={PRIMARY_BUTTON_SX}
          >
            Create
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
```

Create `web/src/admin/adminStyles.ts`. It is shared by the page and all four dialogs so they read as one family. Colours come from the MUI theme or `alpha(ACCENT, …)` so light and dark both work (the constants in `design/accent.ts` are light-only):

```ts
import { alpha } from '@mui/material/styles'
import { ACCENT } from '../design/accent'

export const FIELD_SX = { '& .MuiOutlinedInput-root': { borderRadius: '10px' } } as const
export const DIALOG_PAPER_SLOT = { paper: { className: 'rounded-2xl' } } as const
export const BUTTON_SX = {
  borderRadius: '10px',
  textTransform: 'none',
  transition: 'all 200ms ease-in-out',
} as const
export const PRIMARY_BUTTON_SX = { ...BUTTON_SX, fontWeight: 600 } as const

/** The rounded, hairline-bordered surface every admin list sits on. */
export const CARD_SX = {
  bgcolor: 'background.paper',
  border: 1,
  borderColor: 'divider',
  borderRadius: '16px',
  boxShadow: '0 1px 2px rgba(16, 24, 40, 0.04)',
  overflow: 'hidden',
} as const

export const ACCENT_CHIP_SX = {
  bgcolor: alpha(ACCENT, 0.14),
  color: ACCENT,
  fontWeight: 600,
  border: 'none',
} as const
```

Create `web/src/admin/UserAvatar.tsx`:

```tsx
import { Avatar } from '@mui/material'
import { alpha } from '@mui/material/styles'
import { ACCENT } from '../design/accent'

/** Up to two initials from the display name. */
export function initials(displayName: string): string {
  const words = displayName.trim().split(/\s+/).filter(Boolean)
  return words.slice(0, 2).map((word) => word[0]!.toUpperCase()).join('') || '?'
}

/** Decorative (the name sits next to it), so it is hidden from assistive tech. */
export function UserAvatar({ displayName, muted = false }: { displayName: string; muted?: boolean }) {
  return (
    <Avatar
      aria-hidden
      sx={{
        width: 36,
        height: 36,
        fontSize: 13,
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

Create `web/src/admin/UsersPage.tsx`. Design intent: one rounded card with a header (title, user count, accent "New user" button); a quiet uppercase table head; rows with an avatar, the display name in bold and the username as muted secondary text; a soft accent chip for Admin and a neutral chip for User; a status chip; tabular-nums album counts; hover-tinted rows; disabled accounts dimmed. On narrow screens the table scrolls horizontally inside the card instead of squeezing.

```tsx
import {
  Box,
  Button,
  Chip,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { useUsers } from '../api/users'
import type { UserSummary } from '../api/types'
import { ACCENT, HEADING_SX } from '../design/accent'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { ACCENT_CHIP_SX, CARD_SX, PRIMARY_BUTTON_SX } from './adminStyles'
import { UserAvatar } from './UserAvatar'
import { UserCreateDialog } from './UserCreateDialog'

const HEAD_CELL_SX = {
  color: 'text.secondary',
  fontSize: 12,
  fontWeight: 600,
  letterSpacing: '0.04em',
  textTransform: 'uppercase',
  whiteSpace: 'nowrap',
  py: 1.25,
  bgcolor: 'action.hover',
} as const

function lastLogin(value: string | null): string {
  return value === null ? 'Never' : new Date(value).toLocaleString()
}

function UserRow({ user }: { user: UserSummary }) {
  const dimmed = !user.isActive
  return (
    <TableRow
      hover
      sx={{
        transition: 'background-color 200ms ease-in-out',
        '&:last-child td': { borderBottom: 0 },
        '& td': { py: 1.5 },
      }}
    >
      <TableCell>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, minWidth: 220 }}>
          <UserAvatar displayName={user.displayName} muted={dimmed} />
          <Box sx={{ minWidth: 0 }}>
            <Typography
              variant="body2"
              sx={{ fontWeight: 600 }}
              color={dimmed ? 'text.secondary' : 'text.primary'}
              noWrap
            >
              {user.displayName}
            </Typography>
            <Typography variant="caption" color="text.secondary" noWrap component="div">
              {user.username}
            </Typography>
          </Box>
        </Box>
      </TableCell>
      <TableCell>
        <Chip
          size="small"
          label={user.role}
          sx={user.role === 'Admin' ? ACCENT_CHIP_SX : { bgcolor: 'action.selected', fontWeight: 500 }}
        />
      </TableCell>
      <TableCell>
        <Typography variant="body2" color="text.secondary">
          {user.role === 'Admin' ? 'Always' : user.canRunFolderActions ? 'Yes' : 'No'}
        </Typography>
      </TableCell>
      <TableCell>
        <Chip
          size="small"
          variant="outlined"
          color={user.isActive ? 'success' : 'default'}
          label={user.isActive ? 'Active' : 'Disabled'}
        />
      </TableCell>
      <TableCell>
        <Typography variant="body2" color="text.secondary" noWrap>
          {lastLogin(user.lastLoginAt)}
        </Typography>
      </TableCell>
      <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>
        {user.albumCount}
      </TableCell>
      <TableCell align="right" sx={{ whiteSpace: 'nowrap' }} />
    </TableRow>
  )
}

export function UsersPage() {
  const users = useUsers()
  const [creating, setCreating] = useState(false)

  if (users.isPending) return null
  if (users.isError) {
    return <QueryErrorAlert message="Couldn't load users." onRetry={() => void users.refetch()} />
  }

  const count = users.data.length
  return (
    <Box sx={{ p: { xs: 2, sm: 3 }, maxWidth: 1100, mx: 'auto' }}>
      <Box sx={CARD_SX}>
        <Box
          sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 2, px: 3, py: 2.5 }}
        >
          <Box>
            <Typography variant="h6" component="h2" sx={HEADING_SX}>
              Users
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {count} {count === 1 ? 'user' : 'users'}
            </Typography>
          </Box>
          <Button
            variant="contained"
            size="small"
            disableElevation
            onClick={() => setCreating(true)}
            sx={{ ...PRIMARY_BUTTON_SX, bgcolor: ACCENT, px: 2, '&:hover': { bgcolor: '#4b4bc4' } }}
          >
            New user
          </Button>
        </Box>
        <TableContainer sx={{ borderTop: 1, borderColor: 'divider' }}>
          <Table size="small" aria-label="Users">
            <TableHead>
              <TableRow>
                <TableCell sx={HEAD_CELL_SX}>User</TableCell>
                <TableCell sx={HEAD_CELL_SX}>Role</TableCell>
                <TableCell sx={HEAD_CELL_SX}>Folder actions</TableCell>
                <TableCell sx={HEAD_CELL_SX}>Status</TableCell>
                <TableCell sx={HEAD_CELL_SX}>Last login</TableCell>
                <TableCell align="right" sx={HEAD_CELL_SX}>Albums</TableCell>
                <TableCell align="right" sx={HEAD_CELL_SX}>Actions</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {users.data.map((user) => (
                <UserRow key={user.id} user={user} />
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      </Box>
      {creating && <UserCreateDialog onClose={() => setCreating(false)} />}
    </Box>
  )
}
```

Add the tab to `AdminLayout.tsx` `TABS` (after Roots) and the route to `routes.tsx` plus `import { UsersPage } from '../admin/UsersPage'`.

- [ ] **Step 5: Run the tests and confirm they pass.**

Run (in `web/`): `npm run test -- src/admin/UsersPage.test.tsx 2>&1 | tail -15`
Expected: PASS. Then `npm run lint 2>&1 | tail -10` (if the script exists) and `npx tsc -b 2>&1 | head -20`: no errors. Existing admin tests (`npm run test -- src/admin`) still pass.

- [ ] **Step 6: Commit**

```bash
git add web
git commit -m "feat(web): admin Users page with list and create dialog

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Edit, reset-password and delete dialogs

**Files:**
- Create: `web/src/admin/UserEditDialog.tsx`, `ResetPasswordDialog.tsx`, `DeleteUserDialog.tsx`
- Modify: `web/src/admin/UsersPage.tsx` (row actions, take `selfId`), `web/src/admin/UsersPage.test.tsx` (add tests)

**Interfaces:**
- Consumes: Task 3's `useUpdateUser`, `useResetPassword`, `useDeleteUser`, `userFormErrors`, `UserSummary`, MSW `userHandlers` (`resetCalls`, `deleteCalls` exported from `../test/userHandlers`), `useCurrentUser().data?.id` for the self row.
- Produces: row buttons `Edit {username}`, `Reset password for {username}`, `Delete {username}` (Delete disabled on the signed-in admin's own row).

- [ ] **Step 1: Write the failing tests.** Append to `web/src/admin/UsersPage.test.tsx` (add `import { waitFor } from '@testing-library/react'` and `import { deleteCalls, resetCalls } from '../test/userHandlers'`) inside `describe('UsersPage', …)`:

```tsx
  it('edits a user: name, role and folder-actions permission', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Edit bob' }))
    const name = screen.getByRole('textbox', { name: 'Display name' })
    await user.clear(name)
    await user.type(name, 'Robert')
    await user.click(screen.getByRole('checkbox', { name: /Can run folder actions/ }))
    await user.click(screen.getByRole('button', { name: 'Save' }))

    const row = (await screen.findByText('Robert')).closest('tr')!
    expect(within(row).getByText('Yes')).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Edit bob' })).not.toBeInTheDocument()
  })

  it('disables a user with the Active switch', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Edit bob' }))
    await user.click(screen.getByRole('switch', { name: 'Active' }))
    await user.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(within(screen.getByText('bob').closest('tr')!).getByText('Disabled')).toBeInTheDocument())
  })

  it('locks role and Active on your own account', async () => {
    const { user } = renderPage()
    expect(await screen.findByRole('button', { name: 'Delete admin' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Edit admin' }))

    expect(screen.getByRole('switch', { name: 'Active' })).toBeDisabled()
    expect(screen.getByRole('combobox', { name: 'Role' })).toHaveAttribute('aria-disabled', 'true')
  })

  it('resets a password and shows a short one as an error', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Reset password for bob' }))
    await user.type(screen.getByLabelText('New password'), 'short')
    await user.click(screen.getByRole('button', { name: 'Reset' }))
    expect(await screen.findByText('Must be at least 8 characters.')).toBeInTheDocument()
    expect(resetCalls).toEqual([])

    await user.clear(screen.getByLabelText('New password'))
    await user.type(screen.getByLabelText('New password'), 'fresh-password')
    await user.click(screen.getByRole('button', { name: 'Reset' }))

    await waitFor(() => expect(resetCalls).toEqual([{ id: 5, newPassword: 'fresh-password' }]))
    expect(screen.queryByRole('dialog', { name: 'Reset password' })).not.toBeInTheDocument()
  })

  it('deletes a user, transferring their albums by default', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Delete bob' }))
    expect(screen.getByText(/owns 2 albums/)).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /Transfer their albums to me/ })).toBeChecked()
    await user.click(screen.getByRole('button', { name: 'Delete user' }))

    await waitFor(() => expect(screen.queryByText('bob')).not.toBeInTheDocument())
    expect(deleteCalls).toEqual([{ id: 5, albums: 'transfer' }])
  })

  it('deletes a user together with their albums when chosen', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Delete bob' }))
    await user.click(screen.getByRole('radio', { name: /Delete their albums/ }))
    await user.click(screen.getByRole('button', { name: 'Delete user' }))

    await waitFor(() => expect(deleteCalls).toEqual([{ id: 5, albums: 'delete' }]))
  })

  it('does not offer an album choice for a user with no albums', async () => {
    const { user } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Delete carol' }))

    expect(screen.queryByRole('radio')).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Delete user' }))
    await waitFor(() => expect(deleteCalls).toEqual([{ id: 6, albums: 'transfer' }]))
  })
```

- [ ] **Step 2: Run the tests and confirm they fail.**

Run (in `web/`): `npm run test -- src/admin/UsersPage.test.tsx 2>&1 | tail -15`
Expected: the new tests FAIL (no `Edit bob` button).

- [ ] **Step 3: Implement the dialogs.**

`web/src/admin/UserEditDialog.tsx`:

```tsx
import {
  Alert, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle,
  FormControlLabel, MenuItem, Switch, TextField,
} from '@mui/material'
import { useState, type FormEvent } from 'react'
import type { UserSummary } from '../api/types'
import { useUpdateUser, userFormErrors, type UserFormErrors } from '../api/users'
import { BUTTON_SX, DIALOG_PAPER_SLOT, FIELD_SX, PRIMARY_BUTTON_SX } from './adminStyles'

type Props = { user: UserSummary; isSelf: boolean; onClose: () => void }

export function UserEditDialog({ user, isSelf, onClose }: Props) {
  const [displayName, setDisplayName] = useState(user.displayName)
  const [role, setRole] = useState(user.role)
  const [isActive, setIsActive] = useState(user.isActive)
  const [canRunFolderActions, setCanRunFolderActions] = useState(user.canRunFolderActions)
  const [errors, setErrors] = useState<UserFormErrors>({})
  const update = useUpdateUser(user.id)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setErrors({})
    try {
      await update.mutateAsync({
        displayName: displayName.trim(),
        role,
        isActive,
        canRunFolderActions: role === 'Admin' || canRunFolderActions,
      })
      onClose()
    } catch (error) {
      setErrors(userFormErrors(error, ['displayName', 'role']))
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs" aria-label={`Edit ${user.username}`} slotProps={DIALOG_PAPER_SLOT}>
      <form onSubmit={(event) => void submit(event)}>
        <DialogTitle sx={{ fontWeight: 600 }}>Edit {user.username}</DialogTitle>
        <DialogContent>
          {errors.form !== undefined && (
            <Alert severity="error" sx={{ mb: 2, borderRadius: '10px' }}>{errors.form}</Alert>
          )}
          <TextField label="Display name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} autoFocus fullWidth margin="dense" error={errors.displayName !== undefined} helperText={errors.displayName} sx={FIELD_SX} />
          <TextField select label="Role" value={role} onChange={(e) => setRole(e.target.value as 'Admin' | 'User')} disabled={isSelf} fullWidth margin="dense" error={errors.role !== undefined} helperText={errors.role ?? (isSelf ? "You can't demote yourself." : undefined)} sx={FIELD_SX}>
            <MenuItem value="User">User</MenuItem>
            <MenuItem value="Admin">Admin</MenuItem>
          </TextField>
          <FormControlLabel
            control={<Switch checked={isActive} disabled={isSelf} onChange={(e) => setIsActive(e.target.checked)} slotProps={{ input: { 'aria-label': 'Active' } }} />}
            label="Active"
          />
          <FormControlLabel
            control={<Checkbox checked={role === 'Admin' || canRunFolderActions} disabled={role === 'Admin'} onChange={(e) => setCanRunFolderActions(e.target.checked)} />}
            label="Can run folder actions (scan, refresh, face recognition)"
            sx={{ display: 'flex' }}
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2.5 }}>
          <Button onClick={onClose} sx={BUTTON_SX}>Cancel</Button>
          <Button type="submit" variant="contained" disableElevation disabled={update.isPending || displayName.trim() === ''} sx={PRIMARY_BUTTON_SX}>
            Save
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
```

Note the dialog is found by `name: 'Edit bob'` in tests via `aria-label`; the title text "Edit bob" matches too. The Active switch in a `FormControlLabel` named "Active" collides with the aria-label; keep only the `aria-label` on the input and use a plain `<span>` label if the accessible-name query returns two matches.

`web/src/admin/ResetPasswordDialog.tsx`:

```tsx
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, TextField } from '@mui/material'
import { useState, type FormEvent } from 'react'
import type { UserSummary } from '../api/types'
import { useResetPassword, userFormErrors, type UserFormErrors } from '../api/users'
import { BUTTON_SX, DIALOG_PAPER_SLOT, FIELD_SX, PRIMARY_BUTTON_SX } from './adminStyles'

type Props = { user: UserSummary; onClose: () => void }

export function ResetPasswordDialog({ user, onClose }: Props) {
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<UserFormErrors>({})
  const reset = useResetPassword(user.id)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setErrors({})
    try {
      await reset.mutateAsync(password)
      onClose()
    } catch (error) {
      setErrors(userFormErrors(error, ['newPassword']))
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs" aria-label="Reset password" slotProps={DIALOG_PAPER_SLOT}>
      <form onSubmit={(event) => void submit(event)}>
        <DialogTitle sx={{ fontWeight: 600 }}>Reset password</DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 1 }}>
            Sets a new password for {user.username}, signs them out everywhere and makes them change it at their next login.
          </DialogContentText>
          {errors.form !== undefined && (
            <Alert severity="error" sx={{ mb: 2, borderRadius: '10px' }}>{errors.form}</Alert>
          )}
          <TextField label="New password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoFocus fullWidth margin="dense" autoComplete="new-password" error={errors.newPassword !== undefined} helperText={errors.newPassword ?? 'At least 8 characters.'} sx={FIELD_SX} />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2.5 }}>
          <Button onClick={onClose} sx={BUTTON_SX}>Cancel</Button>
          <Button type="submit" variant="contained" disableElevation disabled={reset.isPending || password === ''} sx={PRIMARY_BUTTON_SX}>
            Reset
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
```

`web/src/admin/DeleteUserDialog.tsx`:

```tsx
import {
  Alert, Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle,
  FormControl, FormControlLabel, Radio, RadioGroup,
} from '@mui/material'
import { useState } from 'react'
import type { UserSummary } from '../api/types'
import { useDeleteUser, userFormErrors } from '../api/users'
import { BUTTON_SX, DIALOG_PAPER_SLOT, PRIMARY_BUTTON_SX } from './adminStyles'

type Props = { user: UserSummary; onClose: () => void }

export function DeleteUserDialog({ user, onClose }: Props) {
  const [albums, setAlbums] = useState<'transfer' | 'delete'>('transfer')
  const [error, setError] = useState<string | null>(null)
  const remove = useDeleteUser()

  const confirm = async () => {
    setError(null)
    try {
      await remove.mutateAsync({ id: user.id, albums })
      onClose()
    } catch (e) {
      setError(userFormErrors(e, []).form ?? "Couldn't delete the user.")
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs" aria-label="Delete user" slotProps={DIALOG_PAPER_SLOT}>
      <DialogTitle sx={{ fontWeight: 600 }}>Delete {user.username}?</DialogTitle>
      <DialogContent>
        {error !== null && <Alert severity="error" sx={{ mb: 2, borderRadius: '10px' }}>{error}</Alert>}
        <DialogContentText sx={{ mb: user.albumCount > 0 ? 1 : 0 }}>
          This permanently removes the account. Disable it instead if you may need it again.
          {user.albumCount > 0 && ` ${user.displayName} owns ${user.albumCount} ${user.albumCount === 1 ? 'album' : 'albums'}.`}
        </DialogContentText>
        {user.albumCount > 0 && (
          <FormControl>
            <RadioGroup value={albums} onChange={(e) => setAlbums(e.target.value as 'transfer' | 'delete')}>
              <FormControlLabel value="transfer" control={<Radio />} label="Transfer their albums to me" />
              <FormControlLabel value="delete" control={<Radio />} label="Delete their albums" />
            </RadioGroup>
          </FormControl>
        )}
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2.5 }}>
        <Button onClick={onClose} sx={BUTTON_SX}>Cancel</Button>
        <Button color="error" variant="contained" disableElevation disabled={remove.isPending} onClick={() => void confirm()} sx={PRIMARY_BUTTON_SX}>
          Delete user
        </Button>
      </DialogActions>
    </Dialog>
  )
}
```

In `UsersPage.tsx`: read `const selfId = useCurrentUser().data?.id` (import from `../api/auth`), pass `isSelf={user.id === selfId}` to `UserRow`, and replace the empty actions cell with three `IconButton`s (`EditOutlined`, `LockResetOutlined` and `DeleteOutlined` icons from `@mui/icons-material`), `size="small"`, in a `Box` with `display: 'flex', justifyContent: 'flex-end', gap: 0.5`. Each has `aria-label` `Edit {username}`, `Reset password for {username}`, `Delete {username}` and `sx={{ color: 'text.secondary', transition: 'all 200ms ease-in-out', '&:hover': { color: ACCENT, bgcolor: alpha(ACCENT, 0.1) } }}` (Delete hovers with `error.main` and a 10% `alpha` of it instead), and is wrapped in a MUI `Tooltip` with the same text. Wrap the disabled self-row Delete in a `<span>` so its tooltip still shows (title: "You can't delete your own account"); Delete has `disabled={isSelf}`. Each opens its dialog through row-local state (`const [dialog, setDialog] = useState<'edit' | 'reset' | 'delete' | null>(null)`), rendering `UserEditDialog` (pass `isSelf`), `ResetPasswordDialog` or `DeleteUserDialog` with `onClose={() => setDialog(null)}`. Follow the `RootRow` pattern: render the dialogs as children of the `TableRow`'s last cell so they portal out of the table.

- [ ] **Step 4: Run the tests and confirm they pass.**

Run (in `web/`): `npm run test -- src/admin 2>&1 | tail -15` then `npx tsc -b 2>&1 | head -20`.
Expected: PASS and no type errors. Fix the two accessibility-name collisions noted above if a query reports multiple matches.

- [ ] **Step 5: Commit**

```bash
git add web
git commit -m "feat(web): edit, reset-password and delete dialogs for user administration

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Docs and full verification

**Files:**
- Modify: `Documents/PictureManager-brief.md` (the "Authentication" section added in Phase 1)

- [ ] **Step 1:** In the "Authentication" section of `Documents/PictureManager-brief.md`, add a short "User administration" paragraph: admins manage accounts at Admin → Users (create, edit name/role/active/folder-actions, reset password, delete with album transfer or delete); a new or reset account must change its password at first login; disabling, resetting or deleting a user ends their sessions immediately; the last active admin and your own account are protected; `GET /api/users/directory` lists active users for sharing (used from Phase 3).

- [ ] **Step 2: Full verification.**

Run: `dotnet test 2>&1 | tail -15` and, in `web/`, `npm run test 2>&1 | tail -15` and `npm run build 2>&1 | tail -10`.
Expected: all PASS.

- [ ] **Step 3: Manual end-to-end check** (Verification steps 3 and 5 of the spec that Phase 2 enables):
  1. Start the app, log in as `admin`, open Admin → Users.
  2. Create `bob` with a password; in a private window log in as bob. Expect the forced password change, no Admin menu.
  3. As admin, tick "Can run folder actions" for bob. Within about a minute bob sees the folder actions menu (after a reload).
  4. As admin, disable bob. Bob's next click returns to the login page; bob cannot log in again.
  5. Re-enable bob, reset his password, confirm his open session ends and the new password forces a change.
  6. Create an album as bob, then delete bob with "Transfer"; the album appears under the admin, with " (from bob)" only if the admin already had one with that name.
  7. Try disabling, demoting and deleting your own row: the controls are disabled; a direct API call answers 409.

- [ ] **Step 4: Commit**

```bash
git add Documents/PictureManager-brief.md
git commit -m "docs: user administration in the brief

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

## Self-Review Notes

- **Spec coverage (Phase 2):** list/create/patch/reset/delete endpoints and `directory` → Tasks 1-2; guards (self, last admin) → Task 1 service + Task 2 smoke; transfer with name-clash suffix → Task 1 repository; frontend `UsersPage`, create/edit/reset/delete dialogs, folder-actions checkbox shown checked and disabled for Admin, list column, route and `AdminLayout` tab, `web/src/api/users.ts` → Tasks 3-4. Immediate session end after changes is an addition over the spec's "within about 60 s", done by evicting the cache in the endpoint.
- **Not in this plan:** the directory has no frontend consumer until the Phase 3 share dialog; favorites and shares cascade rows do not exist yet, so delete only handles albums.
- **Known limitation:** two simultaneous creates of the same username can both pass the pre-check; the unique index then rejects the second with a database exception (500). Acceptable for a single-admin LAN tool; not covered by a test.
