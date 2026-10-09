# User Management Phase 1: Authentication Foundation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every `/api` call requires a logged-in local account. Admin-only and folder-action endpoints are
enforced on the server. The SPA has a login screen, a forced password change, and hides the controls a
user is not allowed to use.

**Architecture:**
- Cookie authentication (ASP.NET Core cookie handler) against `AppUser` rows hashed with Identity's
  `PasswordHasher`.
- The existing `user`/`admin` route groups get authorization policies. A third `folderActions` group
  holds folder actions, and an anonymous `auth` group holds login, logout, me and password.
- `ICurrentUser` changes from a hardcoded singleton to a scoped claims reader.
- The SPA wraps all routes in an `AuthGate` that renders the login page or the forced password change
  until there is a usable session.

**Tech Stack:**
- Backend: .NET 10 minimal APIs, EF Core 10 + Npgsql, `Microsoft.Extensions.Identity.Core`
  (PasswordHasher), `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`, the built-in rate limiter.
- Frontend: React 19 + TanStack Query + MUI.
- Tests: xUnit/FluentAssertions/NSubstitute; Vitest + MSW.

**Spec:** `docs/superpowers/specs/2026-10-09-user-management-design.md`. This plan covers Phase 1 only.
Phase 2 (user admin UI) and Phase 3 (sharing, per-user favorites) get their own plans.

## Global Constraints

- Local accounts only. No registration endpoint exists.
- Cookie `pm.auth`: `HttpOnly`, `SameSite=Strict`, `SecurePolicy=SameAsRequest`, 14-day sliding expiry.
  The API answers 401 or 403 and never redirects.
- Passwords: minimum 8 characters, no other rules.
- Login failure is always 401 with title `Invalid username or password.`, including for unknown,
  disabled and password-less users.
- Login rate limit: 10 per minute per remote IP (configurable through `Auth:LoginAttemptsPerMinute`),
  returning 429.
- Initial admin password comes from `Auth:InitialAdmin:Password` (env `Auth__InitialAdmin__Password`)
  and forces a change at first login.
- User #1 becomes `admin`/`Administrator`/`Admin` and keeps all existing albums.
- Admins always have folder actions. For users, `CanRunFolderActions` defaults to false.
- Session re-validation (security stamp + active flag) is cached for at most 60 s per user.
- Data Protection keys are persisted to Postgres (`DataProtectionKeys` table).
- C#: file-scoped namespaces, match surrounding style. TS: strict, explicit prop types.
- Build errors must not dump more than 20 lines into responses. Run single test projects or single
  test files while iterating.

## Review Focus

These five failure modes are the most likely to bite and are not covered by any obvious test. Each one
has a pinning test in the task named in parentheses.

1. **An endpoint added later without a group.** It would silently be anonymous. *(Task 5: the
   "every /api endpoint returns 401 anonymous" sweep.)*
2. **A user is disabled or their password is reset while they have an open tab.** Their old cookie
   must stop working within 60 s, not after 14 days. *(Task 4: the disabled-user test evicts the
   cache and expects a 401.)*
3. **A must-change-password user calls the API directly.** It must be refused everywhere except
   `/api/auth/*`. *(Task 5: the mcp sweep.)*
4. **A non-admin hand-crafts `?includeHidden=true`.** It must not reveal hidden photos.
   *(Task 5: `ListAsync_NonAdmin_IgnoresIncludeHidden`.)*
5. **A session expires mid-use in the SPA.** Any 401 must drop back to the login screen, and the next
   login must not show the previous user's cached data. *(Task 6: "a 401 from any API call shows the
   login page" and "login clears cached queries".)*

---

## File Structure

**Backend: new files**
- `src/PictureManager.Application/Users/AuthenticatedUser.cs`: immutable snapshot of a signed-in user.
- `src/PictureManager.Application/Users/IPasswordHasher.cs`: hashing seam, plus `PasswordRules`.
- `src/PictureManager.Application/Users/AuthOptions.cs`: initial admin password and login rate limit.
- `src/PictureManager.Application/Users/IAuthService.cs`, `AuthService.cs`: login, change password,
  load the active user.
- `src/PictureManager.Application/Users/IAdminBootstrapper.cs`, `AdminBootstrapper.cs`: sets the
  initial admin password at startup.
- `src/PictureManager.Infrastructure/Users/IdentityPasswordHasher.cs`: wraps `PasswordHasher<AppUser>`.
- `src/PictureManager.Api/Auth/AuthClaims.cs`: claim names and principal creation.
- `src/PictureManager.Api/Auth/AuthSetup.cs`: policies, cookie, rate limiter, data protection and DI
  (`AddPictureManagerAuth`).
- `src/PictureManager.Api/Auth/HttpCurrentUser.cs`: `ICurrentUser` from claims.
- `src/PictureManager.Api/Auth/UserSessionCache.cs`: 60 s cache of `AuthenticatedUser?` per user id.
- `src/PictureManager.Api/Auth/CookieSessionValidator.cs`: the `OnValidatePrincipal` hook.
- `src/PictureManager.Api/Endpoints/AuthEndpoints.cs`: `/api/auth/*`.

**Backend: modified files**
- `AppUser.cs`, `AppUserConfiguration.cs`, `PictureManagerDbContext.cs`, a new migration.
- `IAppUserRepository.cs`, `AppUserRepository.cs`.
- `ICurrentUser.cs`. Delete `SystemCurrentUser.cs`.
- `ApplicationServiceCollectionExtensions.cs`, `InfrastructureServiceCollectionExtensions.cs`.
- `ApiSurface.cs`, `Program.cs`.
- `ImageQueryEndpoints.cs`, `FolderEndpoints.cs`, `PeopleEndpoints.cs`, `ScanEndpoints.cs`,
  `DiscoveryEndpoints.cs`, `FaceRecognitionEndpoints.cs`, `JobEndpoints.cs`.
- Csproj files: Infrastructure gains two packages.
- `appsettings.json`, `docker-compose.yml`, `docker-compose.prod.yml`, `Documents/PictureManager-brief.md`.

**Frontend: new files**
- `web/src/api/auth.ts`: `Me` queries and mutations, `usePermissions`.
- `web/src/auth/AuthGate.tsx`, `LoginPage.tsx`, `ChangePasswordDialog.tsx`, `RequireAdmin.tsx`,
  `UserMenu.tsx`, and `Auth.test.tsx`, `Permissions.test.tsx`.
- `web/src/test/authHandlers.ts`.

**Frontend: modified files**
- `api/client.ts`, `api/types.ts`, `app/routes.tsx`, `app/AppShell.tsx`.
- `tree/FolderJobsContext.tsx`, `tree/FolderTreeNode.tsx`, `tree/JobStatusBanner.tsx`.
- `views/FolderView.tsx`, `views/FolderMenu.tsx`, `viewer/PhotoViewer.tsx`.
- `people/PeoplePage.tsx`, `people/PersonView.tsx`.
- `test/handlers.ts`, `test/fixtures.ts`, `test/setup.ts`.

---

### Task 1: User account columns, initial admin seed, Data Protection key table

**Files:**
- Modify: `src/PictureManager.Model/AppUser.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Configurations/AppUserConfiguration.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/PictureManagerDbContext.cs`
- Modify: `src/PictureManager.Infrastructure/PictureManager.Infrastructure.csproj`
- Create: `src/PictureManager.Infrastructure/Migrations/<timestamp>_UserAccounts.cs` (generated)
- Modify (rename `SystemUserId` → `InitialAdminId`):
  - `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AlbumQueryRepositoryTests.cs`
  - `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/ImageQueryRepositoryTests.cs`
  - `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AppUserRepositoryTests.cs`
  - `tests/PictureManager.Infrastructure.Tests/Support/TestData.cs`
  - `tests/PictureManager.Infrastructure.Tests/Support/PostgresTestDatabaseTests.cs`
  - `src/PictureManager.Application/Common/SystemCurrentUser.cs` (deleted in Task 4; keep it compiling until then)
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/ModelSeedDataTests.cs`

**Interfaces:**
- Produces:
  - `AppUser.InitialAdminId` (= 1).
  - `AppUser.NormalizeUsername(string) : string`.
  - New properties: `Username`, `NormalizedUsername`, `PasswordHash?`, `IsActive`,
    `MustChangePassword`, `CanRunFolderActions`, `SecurityStamp`, `CreatedAt`, `LastLoginAt?`.
  - `PictureManagerDbContext : IDataProtectionKeyContext` with a `DataProtectionKeys` set.

- [ ] **Step 1: Write the failing test.** Replace the body of
  `Database_SeedsSystemAppUserAndDefaultAppSettings` and rename the test to
  `Database_SeedsInitialAdminAndDefaultAppSettings`:

```csharp
var user = await context.AppUsers.SingleAsync();
user.Id.Should().Be(AppUser.InitialAdminId);
user.Username.Should().Be("admin");
user.NormalizedUsername.Should().Be("admin");
user.DisplayName.Should().Be("Administrator");
user.Role.Should().Be(UserRole.Admin);
user.IsActive.Should().BeTrue();
user.PasswordHash.Should().BeNull();
user.SecurityStamp.Should().NotBe(Guid.Empty);
```

Keep the settings assertions. In `Model_RegistersAllTwelveEntityTypes`, rename the test to
`Model_RegistersAllEntityTypes` and add
`typeof(Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey)` to the expected list.

- [ ] **Step 2: Run the test and confirm it fails to compile.**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~ModelSeedDataTests"`

Expected: build fails (`InitialAdminId` / `Username` not found).

- [ ] **Step 3: Implement.** Add the packages:

```bash
dotnet add src/PictureManager.Infrastructure package Microsoft.AspNetCore.DataProtection.EntityFrameworkCore --version 10.0.12
dotnet add src/PictureManager.Infrastructure package Microsoft.Extensions.Identity.Core --version 10.0.12
```

`src/PictureManager.Model/AppUser.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace PictureManager.Model;

public class AppUser
{
    /// <summary>The account the initial migration seeds; it owns every album created before user accounts existed.</summary>
    public const int InitialAdminId = 1;

    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;

    /// <summary>Lower-cased <see cref="Username"/>; the unique lookup key, so "Bob" and "bob" are one account.</summary>
    public string NormalizedUsername { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;

    /// <summary>Null until a password is set; such an account cannot log in.</summary>
    public string? PasswordHash { get; set; }

    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }

    /// <summary>May start scans, discovery and face recognition and exclude/remove folders. Admins always may.</summary>
    public bool CanRunFolderActions { get; set; }

    /// <summary>Changes whenever the password, role, active flag or folder-actions permission changes; older cookies stop validating.</summary>
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public ICollection<Album> Albums { get; set; } = new List<Album>();

    public static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();
}
```

`AppUserConfiguration.cs`. Drop the `ZitadelSubjectId` property and index, and the `SystemUserId` const:

```csharp
public void Configure(EntityTypeBuilder<AppUser> builder)
{
    builder.ToTable("AppUsers");
    builder.HasKey(x => x.Id);

    builder.Property(x => x.Username).IsRequired().HasMaxLength(64);
    builder.Property(x => x.NormalizedUsername).IsRequired().HasMaxLength(64);
    builder.HasIndex(x => x.NormalizedUsername).IsUnique();
    builder.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
    builder.Property(x => x.PasswordHash).HasMaxLength(500);
    // No HasDefaultValue(true) on IsActive: EF would treat an explicit `false` as "unset" on insert and
    // let the database default win, creating disabled users as active.

    // The pre-accounts "System" owner becomes the initial admin, keeping every existing album.
    // Its password is set at startup from Auth:InitialAdmin:Password (AdminBootstrapper).
    builder.HasData(new AppUser
    {
        Id = AppUser.InitialAdminId,
        Username = "admin",
        NormalizedUsername = "admin",
        DisplayName = "Administrator",
        Role = UserRole.Admin,
        IsActive = true,
        PasswordHash = null,
        MustChangePassword = false,
        CanRunFolderActions = false,
        SecurityStamp = new Guid("6f1d2c3b-4a59-4e6f-8a7b-9c0d1e2f3a4b"),
        CreatedAt = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
    });
}
```

`PictureManagerDbContext.cs`. Make the class implement `IDataProtectionKeyContext` (namespace
`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`) and add:

```csharp
/// <summary>ASP.NET Core Data Protection keys (they encrypt the auth cookie); kept here so restarts don't log everyone out.</summary>
public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
```

Rename every `AppUser.SystemUserId`, `AppUserConfiguration.SystemUserId` and `AppUserSeed.SystemUserId`
reference to `AppUser.InitialAdminId` (see the file list above). In `AppUserRepositoryTests` change the
display-name assertion to `"Administrator"`. In `SystemCurrentUser.cs`, temporarily point it at
`AppUser.InitialAdminId`.

Generate the migration:

```bash
dotnet ef migrations add UserAccounts --project src/PictureManager.Infrastructure --startup-project src/PictureManager.Api
```

Open the generated `Up()`. It must:
- add the new columns,
- drop `ZitadelSubjectId` and its index,
- create `DataProtectionKeys`,
- run `UpdateData` on row 1.

`Username`/`NormalizedUsername` are required strings, so EF adds them with `defaultValue: ""`, and `IsActive` with
`defaultValue: false`. Check that the generated `UpdateData` for row 1 sets `Username`,
`NormalizedUsername`, `DisplayName`, `Role`, `IsActive = true`, `SecurityStamp` and `CreatedAt`. If any
of them is missing, add it to the `UpdateData` call by hand. No other rows exist in v1.

- [ ] **Step 4: Run the tests and confirm they pass.**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests`

Expected: PASS. This includes the Postgres-backed tests, which run the new migration through the
template database.

- [ ] **Step 5: Commit**

```bash
git add src/PictureManager.Model src/PictureManager.Infrastructure tests/PictureManager.Infrastructure.Tests src/PictureManager.Application/Common/SystemCurrentUser.cs
git commit -m "feat(users): local account columns on AppUser; user #1 becomes the initial admin; Data Protection key table"
```

---

### Task 2: Password hashing, user repository lookups, initial admin bootstrap

**Files:**
- Create: `src/PictureManager.Application/Users/IPasswordHasher.cs`
- Create: `src/PictureManager.Application/Users/AuthOptions.cs`
- Create: `src/PictureManager.Application/Users/IAdminBootstrapper.cs`
- Create: `src/PictureManager.Application/Users/AdminBootstrapper.cs`
- Create: `src/PictureManager.Infrastructure/Users/IdentityPasswordHasher.cs`
- Modify: `src/PictureManager.Application/Repositories/IAppUserRepository.cs`
- Modify: `src/PictureManager.Infrastructure/Persistence/Repositories/AppUserRepository.cs`
- Modify: `ApplicationServiceCollectionExtensions.cs` (register `IAdminBootstrapper`, scoped)
- Modify: `InfrastructureServiceCollectionExtensions.cs` (register `IPasswordHasher` → `IdentityPasswordHasher`, singleton)
- Test: `tests/PictureManager.Application.Tests/Users/AdminBootstrapperTests.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Users/IdentityPasswordHasherTests.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/Persistence/Repositories/AppUserRepositoryTests.cs`

**Interfaces:**
- Consumes: the Task 1 `AppUser` fields.
- Produces:

```csharp
public interface IPasswordHasher { string Hash(string password); bool Verify(string hash, string password); }
public static class PasswordRules { public const int MinLength = 8; }
public sealed class AuthOptions { public string? InitialAdminPassword { get; init; } public int LoginAttemptsPerMinute { get; init; } = 10; }
public interface IAdminBootstrapper { Task<bool> EnsureInitialAdminPasswordAsync(CancellationToken cancellationToken = default); }
// IAppUserRepository additions:
Task<AppUser?> GetByNormalizedUsernameAsync(string normalizedUsername, CancellationToken cancellationToken = default);
Task<AppUser?> GetFirstActiveAdminAsync(CancellationToken cancellationToken = default);
Task<bool> AnyActiveAdminWithPasswordAsync(CancellationToken cancellationToken = default);
Task UpdateAsync(AppUser user, CancellationToken cancellationToken = default);
```

- [ ] **Step 1: Write the failing tests.**

`tests/PictureManager.Infrastructure.Tests/Users/IdentityPasswordHasherTests.cs`:

```csharp
using FluentAssertions;
using PictureManager.Infrastructure.Users;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Users;

public class IdentityPasswordHasherTests
{
    private readonly IdentityPasswordHasher _hasher = new();

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var hash = _hasher.Hash("correct horse");
        hash.Should().NotContain("correct horse");
        _hasher.Verify(hash, "correct horse").Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse() =>
        _hasher.Verify(_hasher.Hash("correct horse"), "battery staple").Should().BeFalse();

    [Fact]
    public void Verify_GarbageHash_ReturnsFalse() =>
        _hasher.Verify("not-a-hash", "anything").Should().BeFalse();
}
```

Append to `AppUserRepositoryTests` (EF InMemory, the same style as the existing tests):

```csharp
private static async Task<PictureManagerDbContext> SeededContextAsync()
{
    var options = new DbContextOptionsBuilder<PictureManagerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    var context = new PictureManagerDbContext(options);
    await context.Database.EnsureCreatedAsync();
    return context;
}

[Fact]
public async Task GetByNormalizedUsernameAsync_FindsSeededAdmin()
{
    await using var context = await SeededContextAsync();
    (await new AppUserRepository(context).GetByNormalizedUsernameAsync("admin"))!.Id.Should().Be(AppUser.InitialAdminId);
}

[Fact]
public async Task AnyActiveAdminWithPasswordAsync_FalseUntilAPasswordIsSet_AndIgnoresInactiveAdmins()
{
    await using var context = await SeededContextAsync();
    var repository = new AppUserRepository(context);
    (await repository.AnyActiveAdminWithPasswordAsync()).Should().BeFalse();

    context.AppUsers.Add(new AppUser { Username = "old", NormalizedUsername = "old", DisplayName = "Old", Role = UserRole.Admin, IsActive = false, PasswordHash = "x" });
    await context.SaveChangesAsync();
    (await repository.AnyActiveAdminWithPasswordAsync()).Should().BeFalse();

    var admin = (await repository.GetFirstActiveAdminAsync())!;
    admin.Id.Should().Be(AppUser.InitialAdminId);
    admin.PasswordHash = "hash";
    await repository.UpdateAsync(admin);
    (await repository.AnyActiveAdminWithPasswordAsync()).Should().BeTrue();
}
```

`tests/PictureManager.Application.Tests/Users/AdminBootstrapperTests.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PictureManager.Application.Repositories;
using PictureManager.Application.Users;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Users;

public class AdminBootstrapperTests
{
    private readonly IAppUserRepository _users = Substitute.For<IAppUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly AppUser _admin = new() { Id = 1, Username = "admin", NormalizedUsername = "admin", Role = UserRole.Admin };

    private AdminBootstrapper Create(string? password) =>
        new(_users, _hasher, new AuthOptions { InitialAdminPassword = password }, NullLogger<AdminBootstrapper>.Instance);

    public AdminBootstrapperTests()
    {
        _users.GetFirstActiveAdminAsync(Arg.Any<CancellationToken>()).Returns(_admin);
        _hasher.Hash(Arg.Any<string>()).Returns(c => "hashed:" + c.Arg<string>());
    }

    [Fact]
    public async Task NoAdminHasPassword_AndOneIsConfigured_SetsItAndForcesAChange()
    {
        var stamp = _admin.SecurityStamp;

        (await Create("initial-pass").EnsureInitialAdminPasswordAsync()).Should().BeTrue();

        _admin.PasswordHash.Should().Be("hashed:initial-pass");
        _admin.MustChangePassword.Should().BeTrue();
        _admin.SecurityStamp.Should().NotBe(stamp);
        await _users.Received(1).UpdateAsync(_admin, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAdminAlreadyHasAPassword_ChangesNothing()
    {
        _users.AnyActiveAdminWithPasswordAsync(Arg.Any<CancellationToken>()).Returns(true);

        (await Create("initial-pass").EnsureInitialAdminPasswordAsync()).Should().BeFalse();

        await _users.DidNotReceive().UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("short")]
    public async Task MissingOrTooShortPassword_ChangesNothing(string? password)
    {
        (await Create(password).EnsureInitialAdminPasswordAsync()).Should().BeFalse();

        _admin.PasswordHash.Should().BeNull();
        await _users.DidNotReceive().UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~AdminBootstrapper"`
and `dotnet test tests/PictureManager.Infrastructure.Tests --filter "FullyQualifiedName~IdentityPasswordHasher|FullyQualifiedName~AppUserRepository"`

Expected: build fails because the types don't exist yet.

- [ ] **Step 3: Implement.**

`IPasswordHasher.cs` (Application/Users):

```csharp
namespace PictureManager.Application.Users;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>False for a wrong password or an unreadable hash; never throws.</summary>
    bool Verify(string hash, string password);
}

public static class PasswordRules
{
    public const int MinLength = 8;
}
```

`AuthOptions.cs`:

```csharp
namespace PictureManager.Application.Users;

/// <summary>Bound in Program.cs from the "Auth" configuration section.</summary>
public sealed class AuthOptions
{
    /// <summary>Auth:InitialAdmin:Password — applied only while no active admin has a password.</summary>
    public string? InitialAdminPassword { get; init; }

    public int LoginAttemptsPerMinute { get; init; } = 10;
}
```

`IAdminBootstrapper.cs` + `AdminBootstrapper.cs`:

```csharp
public interface IAdminBootstrapper
{
    /// <summary>Gives the first active admin the configured initial password when no admin can log in yet. True when it did.</summary>
    Task<bool> EnsureInitialAdminPasswordAsync(CancellationToken cancellationToken = default);
}

public sealed class AdminBootstrapper(
    IAppUserRepository users, IPasswordHasher hasher, AuthOptions options, ILogger<AdminBootstrapper> logger) : IAdminBootstrapper
{
    public async Task<bool> EnsureInitialAdminPasswordAsync(CancellationToken cancellationToken = default)
    {
        if (await users.AnyActiveAdminWithPasswordAsync(cancellationToken))
            return false;

        var admin = await users.GetFirstActiveAdminAsync(cancellationToken);
        if (admin is null)
        {
            logger.LogError("No active admin account exists; nobody can log in");
            return false;
        }

        var password = options.InitialAdminPassword;
        if (string.IsNullOrWhiteSpace(password) || password.Length < PasswordRules.MinLength)
        {
            logger.LogError(
                "No admin can log in yet. Set Auth__InitialAdmin__Password (at least {MinLength} characters) and restart to enable the '{Username}' account",
                PasswordRules.MinLength, admin.Username);
            return false;
        }

        admin.PasswordHash = hasher.Hash(password);
        admin.MustChangePassword = true;
        admin.SecurityStamp = Guid.NewGuid();
        await users.UpdateAsync(admin, cancellationToken);
        logger.LogInformation("Initial password set for admin '{Username}'; it must be changed at first login", admin.Username);
        return true;
    }
}
```

`IdentityPasswordHasher.cs` (Infrastructure/Users):

```csharp
using Microsoft.AspNetCore.Identity;
using PictureManager.Model;
using AppPasswordHasher = PictureManager.Application.Users.IPasswordHasher;

namespace PictureManager.Infrastructure.Users;

/// <summary>ASP.NET Core Identity's PBKDF2 hasher (the format also records its own iteration count).</summary>
public sealed class IdentityPasswordHasher : AppPasswordHasher
{
    private static readonly AppUser NoUser = new();
    private readonly PasswordHasher<AppUser> _inner = new();

    public string Hash(string password) => _inner.HashPassword(NoUser, password);

    public bool Verify(string hash, string password)
    {
        try
        {
            return _inner.VerifyHashedPassword(NoUser, hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
```

`AppUserRepository` additions:

```csharp
public Task<AppUser?> GetByNormalizedUsernameAsync(string normalizedUsername, CancellationToken cancellationToken = default) =>
    _dbContext.AppUsers.FirstOrDefaultAsync(u => u.NormalizedUsername == normalizedUsername, cancellationToken);

public Task<AppUser?> GetFirstActiveAdminAsync(CancellationToken cancellationToken = default) =>
    _dbContext.AppUsers.Where(u => u.IsActive && u.Role == UserRole.Admin).OrderBy(u => u.Id).FirstOrDefaultAsync(cancellationToken);

public Task<bool> AnyActiveAdminWithPasswordAsync(CancellationToken cancellationToken = default) =>
    _dbContext.AppUsers.AnyAsync(u => u.IsActive && u.Role == UserRole.Admin && u.PasswordHash != null, cancellationToken);

public async Task UpdateAsync(AppUser user, CancellationToken cancellationToken = default)
{
    _dbContext.AppUsers.Update(user);
    await _dbContext.SaveChangesAsync(cancellationToken);
}
```

Register the services: `services.AddScoped<IAdminBootstrapper, AdminBootstrapper>();` in
`AddApplication`, and `services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();` in
`AddInfrastructure`. `AuthOptions` is registered in Program.cs in Task 4.

- [ ] **Step 4: Run the tests and confirm they pass.** Use the same commands as Step 2. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(users): password hashing, user lookups and initial admin password bootstrap"
```

---

### Task 3: AuthService (login, change password, active user)

**Files:**
- Create: `src/PictureManager.Application/Users/AuthenticatedUser.cs`
- Create: `src/PictureManager.Application/Users/IAuthService.cs`
- Create: `src/PictureManager.Application/Users/AuthService.cs`
- Modify: `ApplicationServiceCollectionExtensions.cs` (`services.AddScoped<IAuthService, AuthService>();`)
- Test: `tests/PictureManager.Application.Tests/Users/AuthServiceTests.cs`

**Interfaces:**
- Consumes: `IAppUserRepository`, `IPasswordHasher`, `PasswordRules`, `IClock` (existing `UtcNow`).
- Produces:

```csharp
public sealed record AuthenticatedUser(int Id, string Username, string DisplayName, UserRole Role,
    bool MustChangePassword, bool CanRunFolderActions, Guid SecurityStamp)
{
    public bool IsAdmin => Role == UserRole.Admin;
    public static AuthenticatedUser From(AppUser user); // CanRunFolderActions = Admin || user.CanRunFolderActions
}

public interface IAuthService
{
    /// <summary>Null for any failure: unknown, inactive, no password or wrong password.</summary>
    Task<AuthenticatedUser?> LoginAsync(string? username, string? password, CancellationToken cancellationToken = default);
    Task<Result<AuthenticatedUser>> ChangePasswordAsync(int userId, string? currentPassword, string? newPassword, CancellationToken cancellationToken = default);
    /// <summary>The user's current snapshot, or null when the account is gone or disabled.</summary>
    Task<AuthenticatedUser?> GetActiveUserAsync(int userId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1: Write the failing tests.** `AuthServiceTests.cs`:

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

public class AuthServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    private readonly IAppUserRepository _users = Substitute.For<IAppUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly AppUser _bob = new()
    {
        Id = 5, Username = "Bob", NormalizedUsername = "bob", DisplayName = "Bob B", Role = UserRole.User,
        PasswordHash = "hash:secret-pw", IsActive = true
    };
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _clock.UtcNow.Returns(Now);
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(c => c.ArgAt<string>(0) == "hash:" + c.ArgAt<string>(1));
        _hasher.Hash(Arg.Any<string>()).Returns(c => "hash:" + c.Arg<string>());
        _users.GetByNormalizedUsernameAsync("bob", Arg.Any<CancellationToken>()).Returns(_bob);
        _users.GetByIdAsync(5, Arg.Any<CancellationToken>()).Returns(_bob);
        _service = new AuthService(_users, _hasher, _clock);
    }

    [Fact]
    public async Task Login_CorrectPassword_CaseInsensitiveUsername_ReturnsUserAndStampsLastLogin()
    {
        var user = await _service.LoginAsync("  BOB ", "secret-pw");

        user.Should().NotBeNull();
        user!.Id.Should().Be(5);
        user.CanRunFolderActions.Should().BeFalse();
        _bob.LastLoginAt.Should().Be(Now);
        await _users.Received(1).UpdateAsync(_bob, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("bob", "wrong-pw")]
    [InlineData("nobody", "secret-pw")]
    [InlineData(null, "secret-pw")]
    [InlineData("bob", null)]
    public async Task Login_BadCredentials_ReturnsNull(string? username, string? password) =>
        (await _service.LoginAsync(username, password)).Should().BeNull();

    [Fact]
    public async Task Login_DisabledUser_ReturnsNull()
    {
        _bob.IsActive = false;
        (await _service.LoginAsync("bob", "secret-pw")).Should().BeNull();
    }

    [Fact]
    public async Task Login_UserWithoutPassword_ReturnsNull()
    {
        _bob.PasswordHash = null;
        (await _service.LoginAsync("bob", "secret-pw")).Should().BeNull();
    }

    [Fact]
    public async Task AdminSnapshot_AlwaysCanRunFolderActions()
    {
        _bob.Role = UserRole.Admin;
        (await _service.LoginAsync("bob", "secret-pw"))!.CanRunFolderActions.Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_Valid_RehashesClearsFlagAndRotatesStamp()
    {
        _bob.MustChangePassword = true;
        var stamp = _bob.SecurityStamp;

        var result = await _service.ChangePasswordAsync(5, "secret-pw", "brand-new-pw");

        result.IsSuccess.Should().BeTrue();
        result.Value!.MustChangePassword.Should().BeFalse();
        _bob.PasswordHash.Should().Be("hash:brand-new-pw");
        _bob.SecurityStamp.Should().NotBe(stamp);
        result.Value.SecurityStamp.Should().Be(_bob.SecurityStamp);
    }

    [Theory]
    [InlineData("wrong-pw", "brand-new-pw", "currentPassword")]
    [InlineData("secret-pw", "short", "newPassword")]
    [InlineData("secret-pw", "secret-pw", "newPassword")]
    [InlineData("secret-pw", null, "newPassword")]
    public async Task ChangePassword_Invalid_ReportsTheField(string? current, string? next, string field)
    {
        var result = await _service.ChangePasswordAsync(5, current, next);

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors!.Should().ContainKey(field);
        await _users.DidNotReceive().UpdateAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetActiveUser_DisabledOrMissing_ReturnsNull()
    {
        (await _service.GetActiveUserAsync(99)).Should().BeNull();
        _bob.IsActive = false;
        (await _service.GetActiveUserAsync(5)).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**

Run: `dotnet test tests/PictureManager.Application.Tests --filter "FullyQualifiedName~AuthServiceTests"`

Expected: build fails because the types don't exist yet.

- [ ] **Step 3: Implement.**

`AuthenticatedUser.cs`:

```csharp
using System;
using PictureManager.Model;

namespace PictureManager.Application.Users;

/// <summary>What the API knows about a signed-in user; also what the session cookie is validated against.</summary>
public sealed record AuthenticatedUser(
    int Id, string Username, string DisplayName, UserRole Role,
    bool MustChangePassword, bool CanRunFolderActions, Guid SecurityStamp)
{
    public bool IsAdmin => Role == UserRole.Admin;

    public static AuthenticatedUser From(AppUser user) => new(
        user.Id, user.Username, user.DisplayName, user.Role, user.MustChangePassword,
        user.Role == UserRole.Admin || user.CanRunFolderActions, user.SecurityStamp);
}
```

`AuthService.cs`:

```csharp
public sealed class AuthService(IAppUserRepository users, IPasswordHasher hasher, IClock clock) : IAuthService
{
    public async Task<AuthenticatedUser?> LoginAsync(string? username, string? password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return null;

        var user = await users.GetByNormalizedUsernameAsync(AppUser.NormalizeUsername(username), cancellationToken);
        if (user is not { IsActive: true, PasswordHash: { } hash } || !hasher.Verify(hash, password))
            return null;

        user.LastLoginAt = clock.UtcNow;
        await users.UpdateAsync(user, cancellationToken);
        return AuthenticatedUser.From(user);
    }

    public async Task<Result<AuthenticatedUser>> ChangePasswordAsync(
        int userId, string? currentPassword, string? newPassword, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is not { IsActive: true })
            return Result.NotFound();

        if (user.PasswordHash is null || string.IsNullOrEmpty(currentPassword) || !hasher.Verify(user.PasswordHash, currentPassword))
            return Result.Invalid("currentPassword", "Current password is incorrect.");
        if (newPassword is null || newPassword.Length < PasswordRules.MinLength)
            return Result.Invalid("newPassword", $"Must be at least {PasswordRules.MinLength} characters.");
        if (newPassword == currentPassword)
            return Result.Invalid("newPassword", "Must differ from the current password.");

        user.PasswordHash = hasher.Hash(newPassword);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid();
        await users.UpdateAsync(user, cancellationToken);
        return Result<AuthenticatedUser>.Ok(AuthenticatedUser.From(user));
    }

    public async Task<AuthenticatedUser?> GetActiveUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is { IsActive: true } ? AuthenticatedUser.From(user) : null;
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass.** Use the same command as Step 2. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PictureManager.Application tests/PictureManager.Application.Tests
git commit -m "feat(auth): AuthService for login, password change and active-user lookup"
```

---

### Task 4: Cookie auth, policies, `HttpCurrentUser`, `/api/auth/*` endpoints

**Files:**
- Create: `src/PictureManager.Api/Auth/AuthClaims.cs`, `AuthSetup.cs`, `HttpCurrentUser.cs`,
  `UserSessionCache.cs`, `CookieSessionValidator.cs`
- Create: `src/PictureManager.Api/Endpoints/AuthEndpoints.cs`
- Modify: `src/PictureManager.Application/Common/ICurrentUser.cs`
- Delete: `src/PictureManager.Application/Common/SystemCurrentUser.cs`
- Modify: `ApplicationServiceCollectionExtensions.cs` (remove the `ICurrentUser` registration)
- Modify: `src/PictureManager.Api/Endpoints/ApiSurface.cs`, `src/PictureManager.Api/Program.cs`,
  `src/PictureManager.Api/appsettings.json`
- Modify: `tests/PictureManager.Application.Tests/Common/CommonBuildingBlocksTests.cs` (delete
  `AddApplication_RegistersSystemCurrentUser_AsSystemUser`)
- Modify: `tests/PictureManager.Api.Tests/Smoke/ApiSmokeFixture.cs`
- Create: `tests/PictureManager.Api.Tests/Smoke/AuthSmokeTests.cs` (a `partial class ApiSmokeTests`,
  because the host fixture can only exist once per process)
- Modify: `tests/PictureManager.Api.Tests/Smoke/ApiSmokeTests.cs` (make it `public partial class`)

**Interfaces:**
- Consumes: `IAuthService`, `AuthenticatedUser`, `AuthOptions`, `IAdminBootstrapper`, `IPasswordHasher`.
- Produces:
  - `ICurrentUser { int UserId; bool IsAdmin; bool CanRunFolderActions; }`.
  - `AuthPolicies.SignedIn | Active | AdminOnly | FolderActions` (string consts) and
    `AuthPolicies.LoginRateLimit`.
  - `ApiSurface.Auth`, `ApiSurface.FolderActions`.
  - `UserSessionCache.Evict(int userId)` (Phase 2 calls it after admin edits).
  - `MeDto(int Id, string Username, string DisplayName, string Role, bool MustChangePassword, bool CanRunFolderActions)`.
  - Smoke fixture helpers: `ApiSmokeFixture.Client` (signed-in admin), `LoginAsync(username, password)`,
    `CreateUserClientAsync(username, canRunFolderActions, mustChangePassword)`, `UserPassword`.

- [ ] **Step 1: Write the failing tests.**

In `ApiSmokeTests.cs` change `public class ApiSmokeTests` to `public partial class ApiSmokeTests`.

Rewrite `ApiSmokeFixture.InitializeAsync` and add the helpers:

```csharp
private const string InitialAdminPasswordVariable = "Auth__InitialAdmin__Password";
private const string LoginLimitVariable = "Auth__LoginAttemptsPerMinute";
public const string InitialAdminPassword = "initial-admin-pw";
public const string AdminPassword = "smoke-admin-pw";
public const string UserPassword = "smoke-user-pw";

public async Task InitializeAsync()
{
    Database = await PostgresTestDatabase.CreateAsync();
    Environment.SetEnvironmentVariable(ConnectionStringVariable, Database.ConnectionString);
    Environment.SetEnvironmentVariable(CacheRootVariable, _cacheRoot);
    Environment.SetEnvironmentVariable(InitialAdminPasswordVariable, InitialAdminPassword);
    // Every helper logs in from the same test-server "IP"; the real 10/minute limit would trip.
    Environment.SetEnvironmentVariable(LoginLimitVariable, "10000");

    Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));

    // Startup gave "admin" the initial password with a forced change; do it once so Client is a normal admin session.
    Client = await LoginAsync("admin", InitialAdminPassword);
    var changed = await Client.PostAsJsonAsync("/api/auth/password", new { currentPassword = InitialAdminPassword, newPassword = AdminPassword });
    changed.EnsureSuccessStatusCode();
}

/// <summary>A client with its own cookie jar, signed in as <paramref name="username"/>.</summary>
public async Task<HttpClient> LoginAsync(string username, string password)
{
    var client = Factory.CreateClient();
    var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
    response.EnsureSuccessStatusCode();
    return client;
}

/// <summary>Inserts a User-role account (password <see cref="UserPassword"/>) and signs it in.</summary>
public async Task<HttpClient> CreateUserClientAsync(string username, bool canRunFolderActions = false, bool mustChangePassword = false)
{
    var hasher = Factory.Services.GetRequiredService<IPasswordHasher>();
    await using (var context = Database.CreateContext())
    {
        context.AppUsers.Add(new AppUser
        {
            Username = username, NormalizedUsername = AppUser.NormalizeUsername(username), DisplayName = username,
            Role = UserRole.User, PasswordHash = hasher.Hash(UserPassword), IsActive = true,
            CanRunFolderActions = canRunFolderActions, MustChangePassword = mustChangePassword,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }
    return await LoginAsync(username, UserPassword);
}
```

In `DisposeAsync`, reset both new variables to null as well. Add these usings:
`System.Net.Http.Json`, `Microsoft.Extensions.DependencyInjection`, `PictureManager.Application.Users`,
`PictureManager.Model`.

`AuthSmokeTests.cs`:

```csharp
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Api.Auth;
using Xunit;

namespace PictureManager.Api.Tests.Smoke;

public partial class ApiSmokeTests
{
    [Fact]
    public async Task Me_SignedInAdmin_ReturnsProfile()
    {
        var me = await _fixture.Client.GetFromJsonAsync<JsonElement>("/api/auth/me");

        me.GetProperty("username").GetString().Should().Be("admin");
        me.GetProperty("role").GetString().Should().Be("Admin");
        me.GetProperty("mustChangePassword").GetBoolean().Should().BeFalse();
        me.GetProperty("canRunFolderActions").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Me_Anonymous_Returns401()
    {
        using var anonymous = _fixture.Factory.CreateClient();
        (await anonymous.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401WithGenericTitle()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "nope-nope" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()
            .Should().Be("Invalid username or password.");
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        var client = await _fixture.CreateUserClientAsync("logout-user");
        (await client.GetAsync("/api/albums")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsync("/api/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetAsync("/api/albums")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DisabledUser_OpenSession_IsRejectedOnceTheCacheIsEvicted()
    {
        var client = await _fixture.CreateUserClientAsync("soon-disabled");
        await using (var context = _fixture.Database.CreateContext())
        {
            var user = await context.AppUsers.SingleAsync(u => u.NormalizedUsername == "soon-disabled");
            user.IsActive = false;
            await context.SaveChangesAsync();
            _fixture.Factory.Services.GetRequiredService<UserSessionCache>().Evict(user.Id);
        }

        (await client.GetAsync("/api/albums")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MustChangePassword_OnlyAuthEndpointsWork_UntilChanged()
    {
        var client = await _fixture.CreateUserClientAsync("pending-change", mustChangePassword: true);

        (await client.GetAsync("/api/albums")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();

        var changed = await client.PostAsJsonAsync("/api/auth/password",
            new { currentPassword = ApiSmokeFixture.UserPassword, newPassword = "a-fresh-password" });
        changed.StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.GetAsync("/api/albums")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~Smoke"` (needs `docker compose up -d db`).

Expected: build fails (`PictureManager.Api.Auth` doesn't exist, `IPasswordHasher` isn't resolvable).

- [ ] **Step 3: Implement.**

`ICurrentUser.cs`:

```csharp
namespace PictureManager.Application.Common;

/// <summary>Who is calling: the signed-in user of the current HTTP request (HttpCurrentUser in the Api project).</summary>
public interface ICurrentUser
{
    int UserId { get; }
    bool IsAdmin { get; }

    /// <summary>True for admins, and for users an admin allowed to run folder actions.</summary>
    bool CanRunFolderActions { get; }
}
```

Delete `SystemCurrentUser.cs` and its registration line in `AddApplication`.

`ApiSurface.cs`:

```csharp
/// <summary>Which authorization surface an endpoint belongs to; each maps to one route group and policy in Program.cs.</summary>
public enum ApiSurface
{
    User,
    Admin,
    FolderActions,
    Auth
}
```

`src/PictureManager.Api/Auth/AuthClaims.cs`:

```csharp
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using PictureManager.Application.Users;

namespace PictureManager.Api.Auth;

public static class AuthClaims
{
    public const string SecurityStamp = "pm:stamp";
    public const string MustChangePassword = "pm:mcp";
    public const string FolderActions = "pm:fa";

    public static ClaimsPrincipal CreatePrincipal(AuthenticatedUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(SecurityStamp, user.SecurityStamp.ToString())
        };
        if (user.MustChangePassword)
            claims.Add(new Claim(MustChangePassword, "true"));
        if (user.CanRunFolderActions)
            claims.Add(new Claim(FolderActions, "true"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static int? GetUserId(ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
}
```

`UserSessionCache.cs`:

```csharp
using Microsoft.Extensions.Caching.Memory;
using PictureManager.Application.Users;

namespace PictureManager.Api.Auth;

/// <summary>
/// Per-user snapshot the cookie is re-validated against, so a grid of thumbnails doesn't cost a query each.
/// Changes become visible within <see cref="Lifetime"/>; anything that changes a user calls <see cref="Evict"/>.
/// </summary>
public sealed class UserSessionCache(IMemoryCache cache)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public async Task<AuthenticatedUser?> GetAsync(int userId, Func<Task<AuthenticatedUser?>> load)
    {
        if (cache.TryGetValue(Key(userId), out AuthenticatedUser? cached))
            return cached;

        var user = await load();
        cache.Set(Key(userId), user, Lifetime);
        return user;
    }

    public void Evict(int userId) => cache.Remove(Key(userId));

    private static string Key(int userId) => "pm:session:" + userId;
}
```

`CookieSessionValidator.cs`:

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PictureManager.Application.Users;

namespace PictureManager.Api.Auth;

/// <summary>Rejects a cookie whose user was disabled or whose security stamp moved on (password/role/permission change).</summary>
public static class CookieSessionValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var userId = principal is null ? null : AuthClaims.GetUserId(principal);
        if (userId is not int id)
        {
            await RejectAsync(context);
            return;
        }

        var services = context.HttpContext.RequestServices;
        var current = await services.GetRequiredService<UserSessionCache>().GetAsync(
            id, () => services.GetRequiredService<IAuthService>().GetActiveUserAsync(id, context.HttpContext.RequestAborted));

        if (current is null || principal!.FindFirst(AuthClaims.SecurityStamp)?.Value != current.SecurityStamp.ToString())
            await RejectAsync(context);
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
```

`HttpCurrentUser.cs`:

```csharp
using System.Security.Claims;
using PictureManager.Application.Common;
using PictureManager.Model;

namespace PictureManager.Api.Auth;

/// <summary>Reads the caller from the request's claims. Only valid inside an authorized endpoint.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal Principal =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
            ? user
            : throw new InvalidOperationException("ICurrentUser used outside an authenticated request.");

    public int UserId => AuthClaims.GetUserId(Principal) ?? throw new InvalidOperationException("Session has no user id.");
    public bool IsAdmin => Principal.IsInRole(nameof(UserRole.Admin));
    public bool CanRunFolderActions => Principal.HasClaim(AuthClaims.FolderActions, "true");
}
```

`AuthSetup.cs`:

```csharp
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using PictureManager.Application.Common;
using PictureManager.Application.Users;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Model;

namespace PictureManager.Api.Auth;

public static class AuthPolicies
{
    /// <summary>Any valid session, including one that must change its password first (/api/auth/password).</summary>
    public const string SignedIn = "SignedIn";
    public const string Active = "Active";
    public const string AdminOnly = "AdminOnly";
    public const string FolderActions = "FolderActions";
    public const string LoginRateLimit = "login";
}

public static class AuthSetup
{
    public static IServiceCollection AddPictureManagerAuth(this IServiceCollection services, AuthOptions options)
    {
        services.AddSingleton(options);
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddSingleton<UserSessionCache>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddDataProtection()
            .SetApplicationName("PictureManager")
            .PersistKeysToDbContext<PictureManagerDbContext>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = "pm.auth";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Strict;
                // Home LAN deployments are often plain http; behind TLS the cookie is Secure automatically.
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.ExpireTimeSpan = TimeSpan.FromDays(14);
                cookie.SlidingExpiration = true;
                // An API: answer with a status code, never a redirect to a login page.
                cookie.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                cookie.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
                cookie.Events.OnValidatePrincipal = CookieSessionValidator.ValidateAsync;
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.SignedIn, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.Active, policy => policy.RequireAuthenticatedUser().RequireAssertion(HasNoPendingPasswordChange))
            .AddPolicy(AuthPolicies.AdminOnly, policy => policy.RequireAuthenticatedUser().RequireAssertion(HasNoPendingPasswordChange)
                .RequireRole(nameof(UserRole.Admin)))
            .AddPolicy(AuthPolicies.FolderActions, policy => policy.RequireAuthenticatedUser().RequireAssertion(HasNoPendingPasswordChange)
                .RequireClaim(AuthClaims.FolderActions, "true"));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(AuthPolicies.LoginRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = options.LoginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }

    private static bool HasNoPendingPasswordChange(AuthorizationHandlerContext context) =>
        !context.User.HasClaim(AuthClaims.MustChangePassword, "true");
}
```

`src/PictureManager.Api/Endpoints/AuthEndpoints.cs`:

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Api.Auth;
using PictureManager.Application.Common;
using PictureManager.Application.Users;

namespace PictureManager.Api.Endpoints;

public sealed record MeDto(int Id, string Username, string DisplayName, string Role, bool MustChangePassword, bool CanRunFolderActions)
{
    public static MeDto From(AuthenticatedUser user) =>
        new(user.Id, user.Username, user.DisplayName, user.Role.ToString(), user.MustChangePassword, user.CanRunFolderActions);
}

public static class AuthEndpoints
{
    public sealed record LoginRequest(string? Username, string? Password);
    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder auth)
    {
        auth.MapPost("/login", LoginAsync).RequireRateLimiting(AuthPolicies.LoginRateLimit);
        auth.MapPost("/logout", LogoutAsync);
        auth.MapGet("/me", MeAsync);
        auth.MapPost("/password", ChangePasswordAsync).RequireAuthorization(AuthPolicies.SignedIn);
    }

    public static async Task<Results<Ok<MeDto>, ProblemHttpResult>> LoginAsync(
        LoginRequest request, HttpContext http, IAuthService service, CancellationToken cancellationToken)
    {
        var user = await service.LoginAsync(request.Username, request.Password, cancellationToken);
        if (user is null)
            return TypedResults.Problem(title: "Invalid username or password.", statusCode: StatusCodes.Status401Unauthorized);

        await SignInAsync(http, user);
        return TypedResults.Ok(MeDto.From(user));
    }

    public static async Task<NoContent> LogoutAsync(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }

    public static async Task<Results<Ok<MeDto>, UnauthorizedHttpResult>> MeAsync(
        HttpContext http, IAuthService service, CancellationToken cancellationToken)
    {
        if (AuthClaims.GetUserId(http.User) is not int id || await service.GetActiveUserAsync(id, cancellationToken) is not { } user)
            return TypedResults.Unauthorized();
        return TypedResults.Ok(MeDto.From(user));
    }

    public static async Task<Results<Ok<MeDto>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> ChangePasswordAsync(
        ChangePasswordRequest request, HttpContext http, ICurrentUser currentUser, IAuthService service,
        UserSessionCache sessions, CancellationToken cancellationToken)
    {
        var result = await service.ChangePasswordAsync(currentUser.UserId, request.CurrentPassword, request.NewPassword, cancellationToken);
        if (!result.IsSuccess)
            return new Result<MeDto>(result.Status, null, result.Errors, result.Message).ToOk();

        // The stamp rotated: re-issue this browser's cookie (other sessions of the user stop validating).
        sessions.Evict(result.Value!.Id);
        await SignInAsync(http, result.Value);
        return Result<MeDto>.Ok(MeDto.From(result.Value)).ToOk();
    }

    private static Task SignInAsync(HttpContext http, AuthenticatedUser user) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthClaims.CreatePrincipal(user),
            new AuthenticationProperties { IsPersistent = true });
}
```

`Program.cs` changes:

- After `builder.Services.AddWorker();` add:

```csharp
var authOptions = new AuthOptions
{
    InitialAdminPassword = builder.Configuration["Auth:InitialAdmin:Password"],
    LoginAttemptsPerMinute = builder.Configuration.GetValue("Auth:LoginAttemptsPerMinute", 10)
};
builder.Services.AddPictureManagerAuth(authOptions);
```

- In the startup scope, right after `await seeder.SeedAsync();`, add:

```csharp
await scope.ServiceProvider.GetRequiredService<IAdminBootstrapper>().EnsureInitialAdminPasswordAsync();
```

- After `app.UseMiddleware<ImageCacheControlMiddleware>();` add:

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
```

- Replace the two group lines and their comment:

```csharp
// Every /api endpoint lives in exactly one of these groups (ApiSurfaceMetadata); each carries its policy,
// so nothing under /api is reachable anonymously except /api/auth, /api/health and /api/ping.
var user = app.MapGroup("/api").WithMetadata(new ApiSurfaceMetadata(ApiSurface.User)).RequireAuthorization(AuthPolicies.Active);
var folderActions = app.MapGroup("/api").WithMetadata(new ApiSurfaceMetadata(ApiSurface.FolderActions)).RequireAuthorization(AuthPolicies.FolderActions);
var admin = app.MapGroup("/api").WithMetadata(new ApiSurfaceMetadata(ApiSurface.Admin)).RequireAuthorization(AuthPolicies.AdminOnly);
var auth = app.MapGroup("/api/auth").WithMetadata(new ApiSurfaceMetadata(ApiSurface.Auth));

auth.MapAuthEndpoints();
```

The existing `Map*Endpoints` calls stay as they are in this task. Task 5 regroups them.

`appsettings.json`: add

```json
"Auth": { "InitialAdmin": { "Password": "" }, "LoginAttemptsPerMinute": 10 }
```

For local dev, put a dev password in `appsettings.Development.json`:
`"Auth": { "InitialAdmin": { "Password": "admin-dev-pw" } }`.

Fix the compile breaks in existing test substitutes of `ICurrentUser` (`AlbumServiceTests`,
`ImageQueryServiceTests`). NSubstitute returns `false` for the new bools, which keeps the current
behaviour.

- [ ] **Step 4: Run the tests and confirm they pass.**

Run: `dotnet test tests/PictureManager.Api.Tests` and then `dotnet test tests/PictureManager.Application.Tests`.

Expected: PASS, including the pre-existing smoke tests, which now run through the admin's cookie.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(auth): cookie sessions, authorization policies and /api/auth endpoints; ICurrentUser reads the signed-in user"
```

---

### Task 5: Regroup endpoints (folder actions, admin-only library edits, shared status reads)

**Files:**
- Modify: `src/PictureManager.Api/Endpoints/ImageQueryEndpoints.cs`, `FolderEndpoints.cs`,
  `PeopleEndpoints.cs`, `ScanEndpoints.cs`, `DiscoveryEndpoints.cs`, `FaceRecognitionEndpoints.cs`,
  `JobEndpoints.cs`
- Modify: `src/PictureManager.Api/Program.cs`
- Modify: `tests/PictureManager.Api.Tests/Endpoints/ImageQueryEndpointsTests.cs` (`ListAsync` gains `ICurrentUser`)
- Modify: `tests/PictureManager.Api.Tests/Smoke/ApiSmokeTests.cs` (the surface theory rows)
- Create: `tests/PictureManager.Api.Tests/Smoke/AuthorizationSmokeTests.cs` (`partial class ApiSmokeTests`)

**Interfaces:**
- Consumes: `ICurrentUser.IsAdmin`, `ApiSurface.FolderActions`, and the `folderActions` group from Task 4.
- Produces these route-to-surface assignments. The endpoints and signatures are unchanged.

| Surface | Endpoints |
|---|---|
| FolderActions | `POST /discoveries`, `POST /scans`, `POST /face-recognitions`, `POST /jobs/{id}/cancel`, `PUT /folders/{id}/exclusion`, `DELETE /folders/{id}` |
| User (read-only, moved from Admin) | `GET /jobs/active`, `GET /discoveries/{id}/events`, `GET /scans/{id}/events`, `GET /face-recognitions/{id}/events`, `GET /face-recognitions/coverage` |
| Admin (moved from User) | `PUT /images/hidden`, `PUT /images/thumbnail-rotation`, and every non-GET route in `PeopleEndpoints` |
| Admin (unchanged) | `GET /folders/removed`, `POST /folders/{id}/restore`, `DELETE /folders/{id}/removed`, `GET /face-recognitions/failures`, roots, settings |

- [ ] **Step 1: Write the failing tests.**

In `ApiSmokeTests.Endpoint_IsOnTheExpectedSurface`:
- Change `POST /api/scans` and `DELETE /api/folders/{id:int}` to `ApiSurface.FolderActions`.
- Add these rows:

```csharp
[InlineData("POST", "/api/discoveries", ApiSurface.FolderActions)]
[InlineData("POST", "/api/face-recognitions", ApiSurface.FolderActions)]
[InlineData("POST", "/api/jobs/{id:int}/cancel", ApiSurface.FolderActions)]
[InlineData("PUT", "/api/folders/{id:int}/exclusion", ApiSurface.FolderActions)]
[InlineData("GET", "/api/jobs/active", ApiSurface.User)]
[InlineData("GET", "/api/scans/{id:int}/events", ApiSurface.User)]
[InlineData("GET", "/api/face-recognitions/coverage", ApiSurface.User)]
[InlineData("GET", "/api/face-recognitions/failures", ApiSurface.Admin)]
[InlineData("PUT", "/api/images/hidden", ApiSurface.Admin)]
[InlineData("PUT", "/api/images/thumbnail-rotation", ApiSurface.Admin)]
[InlineData("PATCH", "/api/people/{id:int}", ApiSurface.Admin)]
[InlineData("POST", "/api/faces/{id:int}/accept", ApiSurface.Admin)]
[InlineData("GET", "/api/people", ApiSurface.User)]
[InlineData("GET", "/api/images/{id:int}/faces", ApiSurface.User)]
[InlineData("POST", "/api/auth/login", ApiSurface.Auth)]
```

Also change `ApiEndpoints()` so that `/api/auth/*` routes are included. They already start with `/api/`,
so nothing needs to change there. Only confirm the surface assertion holds for them.

`AuthorizationSmokeTests.cs`:

```csharp
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PictureManager.Api.Endpoints;
using Xunit;

namespace PictureManager.Api.Tests.Smoke;

public partial class ApiSmokeTests
{
    /// <summary>(method, concrete path) for every endpoint on the given surfaces. Route values become 999999 (never a real row).</summary>
    private (string Method, string Path)[] Requests(params ApiSurface[] surfaces) =>
        ApiEndpoints()
            .Where(e => surfaces.Contains(e.Metadata.GetMetadata<ApiSurfaceMetadata>()!.Surface))
            .SelectMany(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods
                .Select(m => (m, Regex.Replace(e.RoutePattern.RawText!, @"\{[^}]+\}", "999999"))))
            .ToArray();

    private static HttpRequestMessage Request(string method, string path) =>
        new(new HttpMethod(method), path) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Anonymous_IsRejectedWith401_OnEveryNonAuthEndpoint()
    {
        using var anonymous = _fixture.Factory.CreateClient();
        foreach (var (method, path) in Requests(ApiSurface.User, ApiSurface.Admin, ApiSurface.FolderActions))
            (await anonymous.SendAsync(Request(method, path))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{method} {path}");
    }

    [Fact]
    public async Task PlainUser_IsForbidden_OnAdminAndFolderActionEndpoints()
    {
        var client = await _fixture.CreateUserClientAsync("plain-user");
        foreach (var (method, path) in Requests(ApiSurface.Admin, ApiSurface.FolderActions))
            (await client.SendAsync(Request(method, path))).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {path}");
    }

    [Fact]
    public async Task MustChangePasswordUser_IsForbidden_OnEveryNonAuthEndpoint()
    {
        var client = await _fixture.CreateUserClientAsync("mcp-sweep", mustChangePassword: true);
        foreach (var (method, path) in Requests(ApiSurface.User, ApiSurface.Admin, ApiSurface.FolderActions))
            (await client.SendAsync(Request(method, path))).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {path}");
    }

    [Fact]
    public async Task PlainUser_CanReadJobStatus_AndFaceCoverage()
    {
        var client = await _fixture.CreateUserClientAsync("status-reader");
        (await client.GetAsync("/api/jobs/active")).StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        (await client.GetAsync("/api/face-recognitions/coverage")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UserWithFolderActions_PassesThePolicy()
    {
        var client = await _fixture.CreateUserClientAsync("folder-operator", canRunFolderActions: true);
        // A job that doesn't exist: the handler answers 404, so authorization let the call through. Side-effect free.
        (await client.PostAsync("/api/jobs/999999/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PutAsync("/api/images/hidden", new StringContent("{}", Encoding.UTF8, "application/json")))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

In `ImageQueryEndpointsTests`, add a test and update the existing `ListAsync` call sites to pass an
`ICurrentUser` substitute with `IsAdmin = true`:

```csharp
[Fact]
public async Task ListAsync_NonAdmin_IgnoresIncludeHidden()
{
    var service = Substitute.For<IImageQueryService>();
    service.ListAsync(Arg.Any<ImageListRequest>(), Arg.Any<CancellationToken>())
        .Returns(Result<PagedResult<ImageListItem>>.Ok(new PagedResult<ImageListItem>([], null)));
    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.IsAdmin.Returns(false);

    await ImageQueryEndpoints.ListAsync(5, null, null, null, null, null, null, includeHidden: true, null, null, null, null,
        service, currentUser, CancellationToken.None);

    await service.Received(1).ListAsync(Arg.Is<ImageListRequest>(r => !r.IncludeHidden), Arg.Any<CancellationToken>());
}
```

Before writing this test, check `ImageListRequest`'s last positional parameter name and the
`PagedResult` constructor in `src/PictureManager.Application/Images/ImageQueryModels.cs` and
`Common/PagedResult.cs`, and use those exact names.

- [ ] **Step 2: Run the tests and confirm they fail.**

Run: `dotnet test tests/PictureManager.Api.Tests --filter "FullyQualifiedName~Smoke|FullyQualifiedName~ImageQueryEndpointsTests"`

Expected: the surface theory rows fail, the plain-user sweep gets 200/404 instead of 403 on people
mutations and hide/rotate, and the `ListAsync` test fails to compile.

- [ ] **Step 3: Implement.** Apply the same signature pattern to each file:

```csharp
// ImageQueryEndpoints
public static void MapImageQueryEndpoints(this IEndpointRouteBuilder user, IEndpointRouteBuilder admin)
{
    user.MapGet("/images", ListAsync);
    admin.MapPut("/images/hidden", SetHiddenAsync);
    admin.MapPut("/images/thumbnail-rotation", RotateThumbnailsAsync);
    user.MapGet("/images/{id:int}", GetAsync);
    user.MapPut("/images/{id:int}/favorite", SetFavoriteAsync);
    user.MapDelete("/images/{id:int}/favorite", ClearFavoriteAsync);
}
// ListAsync gains `ICurrentUser currentUser` after `IImageQueryService service`, and builds the request with:
//   includeHidden: (includeHidden ?? false) && currentUser.IsAdmin   // hidden photos are an admin view

// FolderEndpoints
public static void MapFolderEndpoints(this IEndpointRouteBuilder user, IEndpointRouteBuilder folderActions, IEndpointRouteBuilder admin)
{
    user.MapGet("/folders/roots", GetRootsAsync);
    user.MapGet("/folders/{id:int}/children", GetChildrenAsync);
    user.MapGet("/folders/{id:int}", GetAsync);
    admin.MapGet("/folders/removed", GetRemovedAsync);
    folderActions.MapDelete("/folders/{id:int}", RemoveAsync);
    admin.MapPost("/folders/{id:int}/restore", RestoreAsync);
    admin.MapDelete("/folders/{id:int}/removed", DeleteAsync);
    folderActions.MapPut("/folders/{id:int}/exclusion", SetExcludedAsync);
}

// PeopleEndpoints: GETs on user, everything else on admin
public static void MapPeopleEndpoints(this IEndpointRouteBuilder user, IEndpointRouteBuilder admin)
{
    user.MapGet("/people", GetAllAsync);
    user.MapGet("/people/{id:int}", GetAsync);
    admin.MapPatch("/people/{id:int}", NameAsync);
    admin.MapPut("/people/{id:int}/cover", SetCoverAsync);
    admin.MapDelete("/people/{id:int}", DeleteAsync);
    admin.MapPost("/people/{id:int}/ignore", IgnoreGroupAsync);
    admin.MapPost("/people/{id:int}/assign", AssignGroupAsync);
    user.MapGet("/faces/{id:int}/thumbnail", GetFaceThumbnailAsync);
    user.MapGet("/images/{id:int}/faces", GetImageFacesAsync);
    admin.MapPost("/images/{id:int}/faces/recheck", RecheckFacesAsync);
    admin.MapPost("/images/{id:int}/faces/reanalyze", ReanalyzeImageAsync);
    admin.MapPost("/faces/{id:int}/accept", AcceptAsync);
    admin.MapPost("/faces/{id:int}/reject", RejectAsync);
    admin.MapPost("/faces/{id:int}/unknown", MarkUnknownAsync);
    admin.MapPost("/faces/{id:int}/ignore", IgnoreAsync);
    admin.MapPost("/faces/{id:int}/restore", RestoreAsync);
    admin.MapPost("/faces/{id:int}/assign", AssignAsync);
    admin.MapPost("/people/{id:int}/suggestions/accept", AcceptAllAsync);
    admin.MapPost("/people/{id:int}/images/{imageId:int}/accept", AcceptImageAsync);
    admin.MapPost("/people/{id:int}/images/{imageId:int}/reject", RejectImageAsync);
}

// ScanEndpoints
public static void MapScanEndpoints(this IEndpointRouteBuilder user, IEndpointRouteBuilder folderActions)
{
    folderActions.MapPost("/scans", StartScanAsync);
    user.MapGet("/scans/{id:int}/events", StreamScanEventsAsync);
}

// DiscoveryEndpoints
public static void MapDiscoveryEndpoints(this IEndpointRouteBuilder user, IEndpointRouteBuilder folderActions)
{
    folderActions.MapPost("/discoveries", StartDiscoveryAsync);
    user.MapGet("/discoveries/{id:int}/events", StreamDiscoveryEventsAsync);
}

// FaceRecognitionEndpoints
public static void MapFaceRecognitionEndpoints(this IEndpointRouteBuilder user, IEndpointRouteBuilder folderActions, IEndpointRouteBuilder admin)
{
    folderActions.MapPost("/face-recognitions", StartAsync);
    user.MapGet("/face-recognitions/{id:int}/events", StreamEventsAsync);
    admin.MapGet("/face-recognitions/failures", GetFailuresAsync);
    user.MapGet("/face-recognitions/coverage", GetCoverageAsync);
}

// JobEndpoints
public static void MapJobEndpoints(this IEndpointRouteBuilder user, IEndpointRouteBuilder folderActions)
{
    user.MapGet("/jobs/active", GetActiveJobAsync);
    folderActions.MapPost("/jobs/{id:int}/cancel", CancelJobAsync);
}
```

`Program.cs` mapping block (replaces the existing `user.Map...`/`admin.Map...` lines):

```csharp
user.MapImageEndpoints();
user.MapImageQueryEndpoints(admin);
user.MapFolderEndpoints(folderActions, admin);
user.MapAlbumEndpoints();
user.MapDuplicateEndpoints();
user.MapPeopleEndpoints(admin);
user.MapScanEndpoints(folderActions);
user.MapDiscoveryEndpoints(folderActions);
user.MapFaceRecognitionEndpoints(folderActions, admin);
user.MapJobEndpoints(folderActions);
admin.MapRootEndpoints();
admin.MapSettingsEndpoints();
```

Existing endpoint unit tests call the static handlers directly and don't use the `Map*` return values.
If any test calls a `Map*Endpoints` method, update its arguments to match.

- [ ] **Step 4: Run the tests and confirm they pass.**

Run: `dotnet test tests/PictureManager.Api.Tests`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PictureManager.Api tests/PictureManager.Api.Tests
git commit -m "feat(auth): folder-actions surface; library edits admin-only; job status and face coverage readable by every user"
```

---

### Task 6: Frontend session (login page, forced password change, 401 handling)

**Files:**
- Modify: `web/src/api/client.ts`, `web/src/api/types.ts`
- Create: `web/src/api/auth.ts`
- Create: `web/src/auth/AuthGate.tsx`, `web/src/auth/LoginPage.tsx`, `web/src/auth/ChangePasswordDialog.tsx`
- Modify: `web/src/app/routes.tsx`, `web/src/tree/FolderJobsContext.tsx`
- Create: `web/src/test/authHandlers.ts`
- Modify: `web/src/test/fixtures.ts`, `web/src/test/handlers.ts`, `web/src/test/setup.ts`
- Test: `web/src/auth/Auth.test.tsx`

**Interfaces:**
- Consumes: `GET /api/auth/me`, `POST /api/auth/login|logout|password` from Task 4.
- Produces:

```ts
// types.ts
export type Me = { id: number; username: string; displayName: string; role: 'Admin' | 'User'; mustChangePassword: boolean; canRunFolderActions: boolean }
// client.ts
export function setUnauthorizedHandler(handler: (() => void) | null): void
// api/auth.ts
export const meQueryKey: readonly ['me']
export function useCurrentUser(): UseQueryResult<Me | null>
export function usePermissions(): { isAdmin: boolean; canRunFolderActions: boolean }
export function useLogin(): UseMutationResult<Me, unknown, { username: string; password: string }>
export function useLogout(): UseMutationResult<void, unknown, void>
export function useChangePassword(): UseMutationResult<Me, unknown, { currentPassword: string; newPassword: string }>
// test/authHandlers.ts
export const authHandlers: HttpHandler[]
export function signInAs(me: Me | null): void      // what GET /api/auth/me answers
export function resetAuthStore(): void             // back to adminMe (called in setup.ts beforeEach)
// test/fixtures.ts
export const adminMe: Me, userMe: Me
```

- [ ] **Step 1: Write the failing tests.**

`web/src/test/fixtures.ts`, append:

```ts
export const adminMe: Me = { id: 1, username: 'admin', displayName: 'Administrator', role: 'Admin', mustChangePassword: false, canRunFolderActions: true }
export const userMe: Me = { id: 5, username: 'bob', displayName: 'Bob', role: 'User', mustChangePassword: false, canRunFolderActions: false }
```

(import `Me` from `../api/types`).

`web/src/test/authHandlers.ts`:

```ts
import { http, HttpResponse } from 'msw'
import type { Me } from '../api/types'
import { adminMe } from './fixtures'

let current: Me | null = adminMe

/** What GET /api/auth/me answers; null means signed out (401). */
export function signInAs(me: Me | null): void {
  current = me
}

/** Called before every test (setup.ts): signed in as the admin, so every control is visible. */
export function resetAuthStore(): void {
  current = adminMe
}

const unauthorized = () => HttpResponse.json({ title: 'Unauthorized', status: 401 }, { status: 401 })

export const authHandlers = [
  http.get('/api/auth/me', () => (current ? HttpResponse.json(current) : unauthorized())),
  http.post('/api/auth/login', async ({ request }) => {
    const body = (await request.json()) as { username: string; password: string }
    if (body.password !== 'correct-password')
      return HttpResponse.json({ title: 'Invalid username or password.', status: 401 }, { status: 401 })
    current = { ...adminMe, username: body.username }
    return HttpResponse.json(current)
  }),
  http.post('/api/auth/logout', () => {
    current = null
    return new HttpResponse(null, { status: 204 })
  }),
  http.post('/api/auth/password', async ({ request }) => {
    const body = (await request.json()) as { currentPassword: string; newPassword: string }
    if (body.newPassword.length < 8)
      return HttpResponse.json({ title: 'Invalid', status: 400, errors: { newPassword: ['Must be at least 8 characters.'] } }, { status: 400 })
    current = { ...(current ?? adminMe), mustChangePassword: false }
    return HttpResponse.json(current)
  }),
]
```

In `handlers.ts`, add `...authHandlers` as the first entry of `handlers`. In `setup.ts`, call
`resetAuthStore()` in `beforeEach`.

`web/src/auth/Auth.test.tsx`:

```tsx
import { screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { signInAs } from '../test/authHandlers'
import { adminMe } from '../test/fixtures'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('authentication', () => {
  it('shows the login page when signed out, and the app after a successful login', async () => {
    signInAs(null)
    const { user } = renderApp('/folders/1')

    await user.type(await screen.findByLabelText('Username'), 'admin')
    await user.type(screen.getByLabelText('Password'), 'correct-password')
    await user.click(screen.getByRole('button', { name: 'Log in' }))

    expect(await screen.findByRole('navigation', { name: 'Folders' })).toBeInTheDocument()
  })

  it('shows the server message for a wrong password', async () => {
    signInAs(null)
    const { user } = renderApp('/')

    await user.type(await screen.findByLabelText('Username'), 'admin')
    await user.type(screen.getByLabelText('Password'), 'wrong')
    await user.click(screen.getByRole('button', { name: 'Log in' }))

    expect(await screen.findByText('Invalid username or password.')).toBeInTheDocument()
  })

  it('forces a password change before showing the app', async () => {
    signInAs({ ...adminMe, mustChangePassword: true })
    const { user } = renderApp('/folders/1')

    const dialog = await screen.findByRole('dialog', { name: 'Choose a new password' })
    expect(within(dialog).queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Current password'), 'initial-pw')
    await user.type(within(dialog).getByLabelText('New password'), 'brand-new-pw')
    await user.type(within(dialog).getByLabelText('Repeat new password'), 'brand-new-pw')
    await user.click(within(dialog).getByRole('button', { name: 'Change password' }))

    expect(await screen.findByRole('navigation', { name: 'Folders' })).toBeInTheDocument()
  })

  it('rejects mismatched new passwords without calling the API', async () => {
    signInAs({ ...adminMe, mustChangePassword: true })
    const { user } = renderApp('/')

    const dialog = await screen.findByRole('dialog', { name: 'Choose a new password' })
    await user.type(within(dialog).getByLabelText('Current password'), 'initial-pw')
    await user.type(within(dialog).getByLabelText('New password'), 'brand-new-pw')
    await user.type(within(dialog).getByLabelText('Repeat new password'), 'different-pw')
    await user.click(within(dialog).getByRole('button', { name: 'Change password' }))

    expect(await within(dialog).findByText("The new passwords don't match.")).toBeInTheDocument()
  })

  it('a 401 from any API call shows the login page', async () => {
    server.use(http.get('/api/folders/roots', () => HttpResponse.json({ title: 'Unauthorized', status: 401 }, { status: 401 })))
    renderApp('/folders/1')

    expect(await screen.findByRole('button', { name: 'Log in' })).toBeInTheDocument()
  })

  it('login clears cached queries from the previous session', async () => {
    signInAs(null)
    const { user, queryClient } = renderApp('/')
    queryClient.setQueryData(['albums'], [{ id: 99, name: 'Previous user album' }])

    await user.type(await screen.findByLabelText('Username'), 'admin')
    await user.type(screen.getByLabelText('Password'), 'correct-password')
    await user.click(screen.getByRole('button', { name: 'Log in' }))

    await screen.findByRole('navigation', { name: 'Folders' })
    expect(queryClient.getQueryData(['albums'])).toBeUndefined()
  })
})
```

Before writing these assertions, confirm the folder nav label: AppShell renders
`<Box component="nav" aria-label="Folders">`. Also confirm the albums query key in `web/src/api/queries.ts`
(`queryKeys`) and use the real key in the last test.

- [ ] **Step 2: Run the tests and confirm they fail.**

Run: `cd web && npx vitest run src/auth/Auth.test.tsx`

Expected: FAIL. There is no login page yet, and the unhandled `/api/auth/me` errors.

- [ ] **Step 3: Implement.**

`client.ts`: add the handler hook and call it from both fetchers:

```ts
let unauthorizedHandler: (() => void) | null = null

/** AuthGate registers this: any 401 outside /api/auth means the session ended, so show the login page. */
export function setUnauthorizedHandler(handler: (() => void) | null): void {
  unauthorizedHandler = handler
}

function reportUnauthorized(path: string, status: number): void {
  if (status === 401 && !path.startsWith('/api/auth/')) unauthorizedHandler?.()
}
```

In `apiFetch` and `apiFetchText`, inside `if (!response.ok) {`, call
`reportUnauthorized(path, response.status)` before the `throw`.

`types.ts`: add the `Me` type from the Interfaces block.

`web/src/api/auth.ts`:

```ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, apiFetch } from './client'
import type { Me } from './types'

export const meQueryKey = ['me'] as const

const jsonPost = (body: unknown): RequestInit => ({
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
})

/** The signed-in user, or null when there is no session (a 401 is an answer here, not an error). */
export function useCurrentUser() {
  return useQuery({
    queryKey: meQueryKey,
    queryFn: async () => {
      try {
        return await apiFetch<Me>('/api/auth/me')
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) return null
        throw error
      }
    },
    staleTime: Infinity,
  })
}

/** What the UI may offer. The server enforces the same rules; this only hides controls that would be refused. */
export function usePermissions(): { isAdmin: boolean; canRunFolderActions: boolean } {
  const me = useCurrentUser().data
  return { isAdmin: me?.role === 'Admin', canRunFolderActions: me?.canRunFolderActions ?? false }
}

export function useLogin() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (credentials: { username: string; password: string }) => apiFetch<Me>('/api/auth/login', jsonPost(credentials)),
    onSuccess: (me) => {
      // Nothing cached under a previous session may leak into this one.
      queryClient.clear()
      queryClient.setQueryData(meQueryKey, me)
    },
  })
}

export function useLogout() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => apiFetch<void>('/api/auth/logout', { method: 'POST' }),
    onSettled: () => {
      queryClient.clear()
      queryClient.setQueryData(meQueryKey, null)
    },
  })
}

export function useChangePassword() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (body: { currentPassword: string; newPassword: string }) => apiFetch<Me>('/api/auth/password', jsonPost(body)),
    onSuccess: (me) => queryClient.setQueryData(meQueryKey, me),
  })
}
```

`web/src/auth/LoginPage.tsx`:

```tsx
import { Alert, Box, Button, Paper, TextField, Typography } from '@mui/material'
import { Aperture } from 'lucide-react'
import { useActionState } from 'react'
import { ApiError } from '../api/client'
import { useLogin } from '../api/auth'
import { ACCENT, HEADING_SX } from '../design/accent'

function loginErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 429) return 'Too many attempts. Try again in a minute.'
    if (error.status === 401) return error.problem?.title ?? 'Invalid username or password.'
  }
  return "Couldn't reach the server."
}

export function LoginPage() {
  const login = useLogin()
  const [error, submit, pending] = useActionState(async (_previous: string | null, form: FormData) => {
    try {
      await login.mutateAsync({ username: String(form.get('username') ?? ''), password: String(form.get('password') ?? '') })
      return null
    } catch (caught) {
      return loginErrorMessage(caught)
    }
  }, null)

  return (
    <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', bgcolor: 'background.default', p: 2 }}>
      <Paper variant="outlined" sx={{ width: '100%', maxWidth: 360, p: 4, borderRadius: '16px' }}>
        <Box component="form" action={submit} sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25 }}>
            <Box sx={{ display: 'grid', placeItems: 'center', width: 32, height: 32, borderRadius: '8px', bgcolor: ACCENT, color: 'common.white' }}>
              <Aperture size={20} strokeWidth={2.2} />
            </Box>
            <Typography variant="h6" component="h1" sx={HEADING_SX}>PictureManager</Typography>
          </Box>
          {error !== null && <Alert severity="error">{error}</Alert>}
          <TextField name="username" label="Username" autoComplete="username" autoFocus required size="small" />
          <TextField name="password" label="Password" type="password" autoComplete="current-password" required size="small" />
          <Button type="submit" variant="contained" disableElevation disabled={pending}>Log in</Button>
        </Box>
      </Paper>
    </Box>
  )
}
```

`web/src/auth/ChangePasswordDialog.tsx`:

```tsx
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material'
import { useActionState } from 'react'
import { ApiError } from '../api/client'
import { useChangePassword, useLogout } from '../api/auth'

type Props = {
  /** Forced after an admin-set password: no Cancel, and Log out is offered instead. */
  forced?: boolean
  onClose?: () => void
}

function changeErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const fieldError = Object.values(error.problem?.errors ?? {})[0]?.[0]
    if (fieldError) return fieldError
  }
  return "Couldn't change the password."
}

export function ChangePasswordDialog({ forced = false, onClose }: Props) {
  const change = useChangePassword()
  const logout = useLogout()
  const [error, submit, pending] = useActionState(async (_previous: string | null, form: FormData) => {
    const newPassword = String(form.get('newPassword') ?? '')
    if (newPassword !== String(form.get('repeatPassword') ?? '')) return "The new passwords don't match."
    try {
      await change.mutateAsync({ currentPassword: String(form.get('currentPassword') ?? ''), newPassword })
      onClose?.()
      return null
    } catch (caught) {
      return changeErrorMessage(caught)
    }
  }, null)

  return (
    <Dialog open onClose={forced ? undefined : onClose} aria-labelledby="change-password-title" fullWidth maxWidth="xs">
      <form action={submit}>
        <DialogTitle id="change-password-title">{forced ? 'Choose a new password' : 'Change password'}</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '8px !important' }}>
          {forced && <Alert severity="info">Your password was set by an administrator. Choose your own to continue.</Alert>}
          {error !== null && <Alert severity="error">{error}</Alert>}
          <TextField name="currentPassword" label="Current password" type="password" autoComplete="current-password" required size="small" />
          <TextField name="newPassword" label="New password" type="password" autoComplete="new-password" required size="small" helperText="At least 8 characters." />
          <TextField name="repeatPassword" label="Repeat new password" type="password" autoComplete="new-password" required size="small" />
        </DialogContent>
        <DialogActions>
          {forced ? (
            <Button onClick={() => logout.mutate()}>Log out</Button>
          ) : (
            <Button onClick={onClose}>Cancel</Button>
          )}
          <Button type="submit" variant="contained" disableElevation disabled={pending}>Change password</Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
```

`web/src/auth/AuthGate.tsx`:

```tsx
import { Box, CircularProgress } from '@mui/material'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { Outlet } from 'react-router'
import { meQueryKey, useCurrentUser } from '../api/auth'
import { setUnauthorizedHandler } from '../api/client'
import { QueryErrorAlert } from '../shared/QueryErrorAlert'
import { ChangePasswordDialog } from './ChangePasswordDialog'
import { LoginPage } from './LoginPage'

/** Every route sits behind this: no session shows the login page, a forced change shows only that dialog. */
export function AuthGate() {
  const me = useCurrentUser()
  const queryClient = useQueryClient()

  useEffect(() => {
    setUnauthorizedHandler(() => queryClient.setQueryData(meQueryKey, null))
    return () => setUnauthorizedHandler(null)
  }, [queryClient])

  if (me.isPending)
    return (
      <Box sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center' }}>
        <CircularProgress aria-label="Loading" />
      </Box>
    )
  if (me.isError) return <QueryErrorAlert message="Couldn't reach the server." onRetry={() => void me.refetch()} />
  if (me.data === null) return <LoginPage />
  if (me.data.mustChangePassword) return <ChangePasswordDialog forced />
  return <Outlet />
}
```

`routes.tsx`: wrap the two existing top-level routes:

```tsx
export const appRoutes: RouteObject[] = [
  {
    element: <AuthGate />,
    children: [
      { path: '/', element: <AppShell />, children: [/* unchanged */] },
      { path: '/admin', element: <AdminLayout />, children: [/* unchanged */] },
    ],
  },
]
```

`FolderJobsContext.tsx`: the provider sits above the router, so it mounts before login. Re-run the
"restore the active job" effect whenever the signed-in user changes:

```ts
const signedInId = useCurrentUser().data?.id ?? null
useEffect(() => {
  if (signedInId === null) return
  let cancelled = false
  // ... the existing getActiveJob() body, unchanged ...
  return () => {
    cancelled = true
  }
}, [signedInId])
```

Import `useCurrentUser` from `../api/auth`.

- [ ] **Step 4: Run the tests and confirm they pass.**

Run: `cd web && npx vitest run src/auth` and then `npm run test`. All pre-existing tests must still
pass, because the default `me` is the admin.

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src
git commit -m "feat(web): login page, forced password change, and back to login on any 401"
```

---

### Task 7: Frontend permissions (user menu, admin guard, hidden controls)

**Files:**
- Create: `web/src/auth/UserMenu.tsx`, `web/src/auth/RequireAdmin.tsx`
- Modify: `web/src/app/AppShell.tsx`, `web/src/app/routes.tsx`
- Modify: `web/src/tree/FolderTreeNode.tsx`, `web/src/tree/JobStatusBanner.tsx`
- Modify: `web/src/views/FolderView.tsx`, `web/src/views/FolderMenu.tsx`
- Modify: `web/src/viewer/PhotoViewer.tsx`
- Modify: `web/src/people/PeoplePage.tsx`, `web/src/people/PersonView.tsx`
- Test: `web/src/auth/Permissions.test.tsx`

**Interfaces:**
- Consumes: `usePermissions`, `useCurrentUser`, `useLogout`, `ChangePasswordDialog`, and
  `signInAs`/`userMe`/`adminMe` from Task 6.
- Produces: `FolderMenu` gains two props, `canShowHidden: boolean` and `canReanalyse: boolean`. It
  renders nothing when none of its items would show.

- [ ] **Step 1: Write the failing tests.** `web/src/auth/Permissions.test.tsx`:

```tsx
import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { signInAs } from '../test/authHandlers'
import { userMe } from '../test/fixtures'
import { activeJob, scanEvents } from '../test/jobHandlers'
import { renderApp } from '../test/render'
import { server } from '../test/server'

describe('permissions', () => {
  it('a plain user sees no Admin entry, no folder actions and no hide/rotate', async () => {
    signInAs(userMe)
    renderApp('/folders/1')

    const holidays = await screen.findByRole('treeitem', { name: 'Holidays' })
    expect(within(holidays).queryByRole('button', { name: 'Actions for Holidays' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Admin' })).not.toBeInTheDocument()
  })

  it('a user allowed folder actions sees the folder actions menu', async () => {
    signInAs({ ...userMe, canRunFolderActions: true })
    renderApp('/folders/1')

    const holidays = await screen.findByRole('treeitem', { name: 'Holidays' })
    expect(within(holidays).getByRole('button', { name: 'Actions for Holidays' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Admin' })).not.toBeInTheDocument()
  })

  it('a plain user still sees scan progress, but no Cancel', async () => {
    server.use(
      activeJob({ kind: 'FaceRecognition', id: 3, folderId: 2, foldersProcessed: 0, filesFound: 0 }),
      scanEvents(3, []),
    )
    signInAs(userMe)
    renderApp('/folders/1')

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
  })

  it('a plain user is sent away from /admin', async () => {
    signInAs(userMe)
    renderApp('/admin/settings')

    expect(await screen.findByRole('navigation', { name: 'Folders' })).toBeInTheDocument()
  })

  it('the account menu logs out back to the login page', async () => {
    signInAs(userMe)
    const { user } = renderApp('/folders/1')

    await user.click(await screen.findByRole('button', { name: 'Account' }))
    await user.click(screen.getByRole('menuitem', { name: 'Log out' }))

    expect(await screen.findByRole('button', { name: 'Log in' })).toBeInTheDocument()
  })
})
```

Before writing these tests, check the helpers in `web/src/test/jobHandlers.ts`:
- `activeJob` (used in `FolderActions.test.tsx`).
- The face-recognition event helper, if one exists, in place of `scanEvents`. Use whichever helper
  reproduces a running face-recognition job, so the banner would normally render a Cancel button.

Also add one assertion each to the existing tests:
- `PhotoViewer.test.tsx`: with `signInAs(userMe)`, there is no "Rotate right" button and no
  "Review faces" button.
- `PersonView.test.tsx` (or `PeoplePage.test.tsx`): with `signInAs(userMe)`, there is no
  "Edit person" button and no "Recognize faces in all libraries" button.

- [ ] **Step 2: Run the tests and confirm they fail.**

Run: `cd web && npx vitest run src/auth/Permissions.test.tsx`

Expected: FAIL, because the controls are still visible.

- [ ] **Step 3: Implement.**

`web/src/auth/RequireAdmin.tsx`:

```tsx
import type { ReactNode } from 'react'
import { Navigate } from 'react-router'
import { usePermissions } from '../api/auth'

export function RequireAdmin({ children }: { children: ReactNode }) {
  return usePermissions().isAdmin ? children : <Navigate to="/" replace />
}
```

`routes.tsx`: change the admin element to `<RequireAdmin><AdminLayout /></RequireAdmin>`.

`web/src/auth/UserMenu.tsx`:

```tsx
import AccountCircleOutlinedIcon from '@mui/icons-material/AccountCircleOutlined'
import { Divider, IconButton, ListItemText, Menu, MenuItem, Tooltip } from '@mui/material'
import { useState } from 'react'
import { useCurrentUser, useLogout } from '../api/auth'
import { ChangePasswordDialog } from './ChangePasswordDialog'

export function UserMenu() {
  const me = useCurrentUser().data
  const logout = useLogout()
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const [changing, setChanging] = useState(false)
  if (!me) return null

  return (
    <>
      <Tooltip title={me.displayName}>
        <IconButton aria-label="Account" size="small" onClick={(event) => setAnchor(event.currentTarget)} sx={{ color: 'text.secondary' }}>
          <AccountCircleOutlinedIcon fontSize="small" />
        </IconButton>
      </Tooltip>
      <Menu anchorEl={anchor} open={anchor !== null} onClose={() => setAnchor(null)}>
        <MenuItem disabled>
          <ListItemText primary={me.displayName} secondary={me.username} />
        </MenuItem>
        <Divider />
        <MenuItem onClick={() => { setAnchor(null); setChanging(true) }}>Change password</MenuItem>
        <MenuItem onClick={() => { setAnchor(null); logout.mutate() }}>Log out</MenuItem>
      </Menu>
      {changing && <ChangePasswordDialog onClose={() => setChanging(false)} />}
    </>
  )
}
```

`AppShell.tsx`: read `const { isAdmin } = usePermissions()`. Render the existing Admin `IconButton`
only when `isAdmin` is true, and render `<UserMenu />` after it.

`FolderTreeNode.tsx`: `const { canRunFolderActions } = usePermissions()` and wrap the menu:
`{canRunFolderActions && <FolderActionsMenu … />}`. The per-folder progress caption lives inside
`FolderActionsMenu`, so plain users would lose it. To keep "progress stays visible", move the
`activeJob?.folderId === folderId` branch out of `FolderActionsMenu` into a small `FolderJobProgress`
component in the same file. Render it in `FolderTreeNode` for everyone, and render the menu only when
no job runs on that folder. The existing `FolderActions.test.tsx` progress assertions must stay green.

`JobStatusBanner.tsx`: add `const { canRunFolderActions } = usePermissions()` and show the Cancel
button only when `activeJob.kind === 'face-recognitions' && canRunFolderActions`.

`FolderView.tsx`: `const { isAdmin, canRunFolderActions } = usePermissions()`.
- `const showHidden = isAdmin && parseHiddenParam(searchParams)`.
- `hideable={isAdmin}` and `rotatable={isAdmin}`.
- Pass `canShowHidden={isAdmin}` and `canReanalyse={canRunFolderActions}` to `FolderMenu`.

`FolderMenu.tsx`: add the props and gate the items. Show hidden needs `canShowHidden`; Re-analyse needs
`hasPhotos && canReanalyse`; Add to album keeps `hasPhotos`. Show the divider only when an item exists
on both sides of it. Return `null` when no item would render.

`PhotoViewer.tsx`: `const { isAdmin } = usePermissions()`.
- Render the "Rotate right" button only when `canAdd && isAdmin`, and make the `R` shortcut call
  `rotateRight` only when `isAdmin` (add the guard at the top of `rotateRight`).
- Render the "Review faces" toggle only when `openedFor === undefined && isAdmin`. The face-review
  panel is all mutations.

`PeoplePage.tsx`: `const { canRunFolderActions } = usePermissions()`, and render the "Recognize faces in
all libraries" button only when it is true.

`PersonView.tsx`: `const { isAdmin } = usePermissions()`. When `isAdmin` is false:
- `faceReview` is `undefined`.
- `onAssignSelected` is `undefined`.
- `onSetCoverSelected` is `undefined`.
- `banner` is `undefined` (the `SuggestedStrip` is review UI).
- `titleAdornment` is `undefined`.

- [ ] **Step 4: Run the tests and confirm they pass.**

Run: `cd web && npm run test`, then `npm run build` (type check) and `npm run lint`.

Expected: PASS, with no type errors and no lint errors.

- [ ] **Step 5: Commit**

```bash
git add web/src
git commit -m "feat(web): account menu; hide admin-only and folder-action controls from users without the permission"
```

---

### Task 8: Deployment config and docs

**Files:**
- Modify: `docker-compose.yml`, `docker-compose.prod.yml`, `.env.example` (if present),
  `Documents/PictureManager-brief.md`

- [ ] **Step 1:** In both compose files, add this to the `api` service `environment:` block:

```yaml
      # Initial password for the "admin" account; used only while no admin has a password. Changed at first login.
      Auth__InitialAdmin__Password: ${PM_ADMIN_PASSWORD:?Set PM_ADMIN_PASSWORD in .env}
```

In `docker-compose.yml` (dev), use `${PM_ADMIN_PASSWORD:-admin-dev-pw}` instead.

`docker-compose.prod.yml` already has the user's uncommitted local edits (a db `ports` mapping and an
`image:` line). Stage **only** the new env-var hunk with `git add -p docker-compose.prod.yml`, and leave
their edits unstaged. If `.env.example` exists, add `PM_ADMIN_PASSWORD=`.

- [ ] **Step 2:** In `Documents/PictureManager-brief.md`, replace the "Authentication — deferred to v2"
section:

```markdown
## Authentication

Local accounts (no external IdP). An admin creates users; there is no registration.
- Cookie session `pm.auth` (HttpOnly, SameSite=Strict, 14-day sliding); Data Protection keys live in Postgres.
- The initial `admin` account gets its password from `Auth__InitialAdmin__Password` on first start and must change it at first login.
- Surfaces (route groups in Program.cs, enforced server-side):
  - `user`: any signed-in user; browse, favorites, own albums, read-only job status.
  - `folderActions`: scans, discovery, face recognition, folder exclude/remove. Admins, plus users with `CanRunFolderActions`.
  - `admin`: roots, settings, removed folders, hide/rotate, people/face edits.
  - `auth`: login/logout/me/password.
- Design: docs/superpowers/specs/2026-10-09-user-management-design.md
```

Also update the Albums bullet that says "see v1 placeholder owner": albums are owned by the signed-in
user.

- [ ] **Step 3: Full verification.**

Run: `dotnet test` (with `docker compose up -d db`), then `cd web && npm run test && npm run build`.

Expected: everything passes.

- [ ] **Step 4: Manual end-to-end check.**
1. Run `docker compose up -d db`, then `dotnet run --project src/PictureManager.Api` using the dev
   password from `appsettings.Development.json`, and `cd web && npm run dev`.
2. Open the app. The login page appears. Log in as `admin` / `admin-dev-pw` and change the password
   when forced. Existing albums and favorites are still there.
3. Create a second user directly in SQL (the Users page comes in Phase 2) with
   `CanRunFolderActions=false`. Log in as them in a private window:
   - There is no Admin icon and no folder actions menu.
   - Rotate, Review faces and Edit person are not shown.
   - The scan progress banner appears while the admin runs a scan.
   - `curl -X POST` to `/api/scans` with that user's cookie returns 403.
4. Restart the API. Both sessions are still signed in, which confirms the persisted Data Protection keys.

- [ ] **Step 5: Commit**

```bash
git add docker-compose.yml Documents/PictureManager-brief.md   # plus the staged hunk of docker-compose.prod.yml
git commit -m "docs(auth): initial admin password setting in compose files; brief describes the auth surfaces"
```
