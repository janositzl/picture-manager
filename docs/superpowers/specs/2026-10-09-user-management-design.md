# User Management — Design

## Context

PictureManager v1 has no login. Every request runs as the seeded `AppUser` #1 ("System") via the
`ICurrentUser` seam (`src/PictureManager.Application/Common/SystemCurrentUser.cs`), and every album
belongs to that user. The brief had deferred auth to v2 with Zitadel OIDC. We are now building v2 with
**local accounts** instead, because the requirement is that only the admin adds users and there is no
self-registration.

**Decisions so far (from the user):**
- Local username/password accounts, cookie session, no external IdP.
- Only the admin creates users. No registration.
- Albums belong to an owner and can be shared with other users as **Viewer** or **Editor**.
- In scope: **per-user favorites**, **disable instead of delete** (plus hard delete with album
  transfer/delete), **password self-service** (change own password; admin reset forces a change).
- **Admin-only:** every action that changes the shared library for everyone: hide/unhide, thumbnail
  rotation, all people/face mutations.
- **Folder actions are a per-user permission:** `CanRunFolderActions`, default **false**. This covers
  everything in the folder tree's actions menu: Refresh structure, Scan, Recognize/Reanalyse faces,
  Exclude/Include, Remove folder, and cancelling a running job. Users without it don't see the actions.
  Admins always have it.
- Out of scope: per-user root access restrictions.

**Assumptions to confirm on review:**
- An admin has **no** special access to other users' private albums. Admins see their own albums and
  albums shared with them, the same as any user.
- Existing data is preserved: user #1 becomes the initial admin (username `admin`) and keeps all
  existing albums and favorites.

**Already in place to reuse:**
- `AppUser`, `UserRole {User, Admin}`, `Album.OwnerUserId`.
- `ICurrentUser`, which `AlbumService` and `ImageQueryService` already use.
- The `user` and `admin` MapGroups in `src/PictureManager.Api/Program.cs:141-157`, which carry
  `ApiSurfaceMetadata` and a comment marking where `RequireAuthorization` goes.
- `Result`/`ResultHttpExtensions` for endpoint results.
- `apiFetch`/`ApiError` in `web/src/api/client.ts`.
- `AdminLayout` and the admin routes in `web/src/app/routes.tsx`.

## Delivery: 3 phases, each shippable

### Phase 1: Authentication foundation

**Model + migration** (`src/PictureManager.Model/AppUser.cs`, `AppUserConfiguration.cs`):
- `AppUser` gains these fields:
  - `Username` (≤64 chars, unique on `lower(Username)`)
  - `PasswordHash?`
  - `IsActive = true`
  - `MustChangePassword`
  - `CanRunFolderActions = false` (ignored for admins, who always have it)
  - `SecurityStamp` (Guid, rotated on every password, role, active or folder-actions change)
  - `CreatedAt`, `LastLoginAt?`
- Drop `ZitadelSubjectId` and its index.
- Migration updates row #1 to `Username="admin"`, `DisplayName="Administrator"`, `Role=Admin`,
  `PasswordHash=null`. Remove `HasData` (or update it to match).
- Rename `AppUser.SystemUserId` → `AppUser.InitialAdminId`, and update its references.

**Initial admin bootstrap** (new `IAdminBootstrapper`, called at startup in `Program.cs` next to
`IImageRootSeeder`):
- If no active admin has a password, read `Auth:InitialAdmin:Password` (env
  `Auth__InitialAdmin__Password`), hash it, and set `MustChangePassword=true`.
- If that setting is missing, log an error. Nobody can log in until it is set.

**Password hashing:**
- New `IPasswordHasher` in Application, implemented in Infrastructure by wrapping
  `Microsoft.AspNetCore.Identity.PasswordHasher<AppUser>` (package `Microsoft.Extensions.Identity.Core`).
- Minimum 8 characters, no other complexity rules.

**Cookie auth** (`Program.cs`):
- `AddAuthentication().AddCookie("pm.auth")` with `HttpOnly`, `SameSite=Strict`,
  `SecurePolicy=SameAsRequest` (LAN may be http), and a 14-day sliding expiry.
- `OnRedirectToLogin` → 401 and `OnRedirectToAccessDenied` → 403, so the API never redirects.
- `OnValidatePrincipal` checks the `stamp` claim and `IsActive` against an `IMemoryCache` entry
  (≤60 s TTL, evicted explicitly on user changes). A disabled user or a password reset logs out
  existing sessions without a DB hit on every thumbnail.
- **Data Protection keys persisted to Postgres** (`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`,
  `DataProtectionKeys` table). Without this, every container restart logs everyone out.
- Claims: `NameIdentifier`=Id, `Name`=Username, `Role`, `stamp`, `mcp` (must-change-password),
  `fa` (can run folder actions; issued for admins too).

**Authorization:**
- `user.RequireAuthorization("Active")` and `admin.RequireAuthorization("AdminOnly")`, exactly the seam
  that is already planned.
- A third group, `folderActions`, uses `RequireAuthorization("FolderActions")`: Active plus the `fa`
  claim. Add `ApiSurface.FolderActions` to `ApiSurface.cs`.
- All three policies also require that `mcp` is absent. A user who must change their password can only
  reach the `auth` group.
- **Endpoint regrouping for folder actions:**
  - **`folderActions`** (mutations, moved from `admin`): `POST /discoveries`, `POST /scans`,
    `POST /face-recognitions`, `POST /jobs/{id}/cancel`, `PUT /folders/{id}/exclusion`,
    `DELETE /folders/{id}`.
  - **`user`** (read-only status, moved from `admin`, which the tree polls for every user): `GET /jobs/active`,
    `GET /discoveries/{id}/events`, `GET /scans/{id}/events`, `GET /face-recognitions/{id}/events`,
    `GET /face-recognitions/coverage`. Everyone sees scan progress and face-status icons.
  - **`admin`** stays as is: removed-folders list, restore and delete, `face-recognitions/failures`,
    roots, settings.
  - `MapFolderEndpoints`, `MapScanEndpoints`, `MapDiscoveryEndpoints`, `MapFaceRecognitionEndpoints` and
    `MapJobEndpoints` take the groups they need, following the existing `MapFolderEndpoints(user, admin)`
    pattern.
- A new anonymous `auth` group at `/api/auth`, with `ApiSurface.Auth` added to `ApiSurface.cs`.
- `/api/health` and `/api/ping` stay anonymous. Static files and the SPA fallback stay anonymous.
- Login rate limit: `AddRateLimiter` fixed window, 10 per minute per remote IP on `POST /api/auth/login`.

**`ICurrentUser`:**
- Becomes `{ int UserId; bool IsAdmin; bool CanRunFolderActions; }`.
- New scoped `HttpCurrentUser` in Api reads the claims. It replaces the singleton `SystemCurrentUser`
  registration in `ApplicationServiceCollectionExtensions.cs:20`.
- `SystemCurrentUser` is deleted.

**Auth endpoints** (new `src/PictureManager.Api/Endpoints/AuthEndpoints.cs` and
`Application/Users/AuthService.cs`):
- `POST /api/auth/login {username,password}` → 200 `Me` and sets the cookie, or 401 with a generic
  message. Updates `LastLoginAt`.
- `POST /api/auth/logout` → 204.
- `GET /api/auth/me` → `Me {id, username, displayName, role, mustChangePassword, canRunFolderActions}`
  (always true for admins), or 401.
- `POST /api/auth/password {currentPassword,newPassword}`: requires a signed-in user and works even
  with `mcp` set. Rotates the stamp, clears `mcp` and re-issues the cookie.

**Move shared-library mutations to the admin group:**
- `ImageQueryEndpoints`: `PUT /images/hidden` and `PUT /images/thumbnail-rotation`.
- `PeopleEndpoints`: every POST/PATCH/PUT/DELETE. Change the signature to
  `MapPeopleEndpoints(user, admin)`, mirroring `MapFolderEndpoints(user, admin)`. The GETs stay on `user`.
- The "show hidden" query option is honoured only when `ICurrentUser.IsAdmin`.
- `ImageCacheControlMiddleware`: change `public` to `private` caching, because responses now require auth.

**Frontend:**
- `web/src/auth/`:
  - `AuthProvider` + `useCurrentUser()`, a TanStack query on `['me']` that treats 401 as `null`.
  - `LoginPage` (form uses `useActionState`).
  - `ChangePasswordPage`, forced when `mustChangePassword` is set.
  - `RequireAdmin` route guard.
- `client.ts`: a 401 from any call other than login triggers a registered `onUnauthorized` handler
  that sets `['me']` to null, which shows the login page.
- On login and logout, `queryClient.clear()` so no data leaks between users.
- `AppShell`: user menu with display name, Change password, Admin (admins only), and Log out.
- Hide admin-only controls for non-admins using `useCurrentUser().isAdmin`:
  - Hide/Unhide in `SelectionBar`/`FolderMenu`
  - Rotate in `PhotoViewer`
  - Face review in `FaceReview`/`PeopleInPhoto`/`PersonAssign`
  - `PersonEditDialog`, `SuggestedStrip` actions, `AssignGroupDialog`
  - "Show hidden" toggle
- Hide folder actions unless `useCurrentUser().canRunFolderActions`:
  - Don't render `FolderActionsMenu` in `FolderTreeNode`.
  - Hide the scan and face-recognition buttons in `FolderView`/`FolderMenu`.
  - Hide the Cancel button in `JobStatusBanner`. The progress display stays visible to everyone.

### Phase 2: User administration

**API** (admin group; new `UserEndpoints.cs`, `Application/Users/UserService.cs`, and extend
`IAppUserRepository`):
- `GET /api/users`: list with id, username, displayName, role, isActive, canRunFolderActions,
  lastLoginAt and album count.
- `POST /api/users {username, displayName, password, role, canRunFolderActions = false}`: the new user
  has `MustChangePassword=true`. Returns 409 for a duplicate username.
- `PATCH /api/users/{id} {displayName?, role?, isActive?, canRunFolderActions?}`: role, active or
  folder-actions changes rotate the stamp, so the change takes effect within about 60 s.
- `POST /api/users/{id}/reset-password {newPassword}`: sets `MustChangePassword=true` and rotates the stamp.
- `DELETE /api/users/{id}?albums=transfer|delete`:
  - `transfer` reassigns owned albums to the calling admin. On a name clash, append " (from {username})".
  - Shares and favorites cascade.
- **Guards:**
  - An admin cannot disable, demote or delete themselves.
  - The last active admin can never be removed.
  - Both return a 409 with a message.

**User directory** (user group, used by the share picker):
- `GET /api/users/directory` → `[{id, displayName}]` for active users, excluding the caller.

**Frontend:**
- `web/src/admin/UsersPage.tsx` plus dialogs: create, edit (name/role/active), reset password, and
  delete (with a transfer/delete albums choice).
- Create and edit dialogs include a "Can run folder actions (scan, refresh, face recognition)"
  checkbox, unchecked by default. When the role is Admin it is shown checked and disabled. The list
  has a matching column.
- Add the route under `/admin` and a nav entry in `AdminLayout`.
- `web/src/api/users.ts` holds the query and mutation hooks.

### Phase 3: Album sharing + per-user favorites

**Sharing model:**
- New `AlbumShare {AlbumId, UserId, Permission (Viewer|Editor), CreatedAt}`.
- PK `(AlbumId, UserId)`. Cascade on album delete and on user delete.

**Access resolution:**
- Replace `IAlbumRepository.GetOwnedAsync(id, userId)` with `GetAccessibleAsync(id, userId)`, which
  returns `(Album, AlbumAccess Owner|Editor|Viewer)` or null.
- `AlbumService` checks a required level per operation. Inaccessible albums still behave as 404, and a
  user with insufficient access gets 403.

| Operation | Viewer | Editor | Owner |
|---|---|---|---|
| get, list images, export | ✓ | ✓ | ✓ |
| add/remove/move/sort images, set cover | | ✓ | ✓ |
| rename/describe, delete, manage shares | | | ✓ |
| leave (remove own share) | ✓ | ✓ | |

**Album list and summaries:**
- `GET /albums` returns owned albums plus shared ones.
- `AlbumSummary`/`AlbumDetail` gain `access` and `ownerDisplayName`.
- Name uniqueness stays per owner.

**Share endpoints** (user group):
- `GET /albums/{id}/shares` (owner only).
- `PUT /albums/{id}/shares/{userId} {permission}` (owner only).
- `DELETE /albums/{id}/shares/{userId}`: the owner can remove anyone; any user can remove themselves.

**Other album changes:**
- The image detail's album list (used by `ViewerAlbums`) shows only albums the caller can access.

**Per-user favorites:**
- New `UserFavorite {UserId, ImageId, CreatedAt}`, PK `(UserId, ImageId)`, cascade on both sides.
- Migration copies `Images.IsFavorite = true` rows to user #1, then drops `Image.IsFavorite` and its index.
- Every projection and filter takes the current user:
  - `ImageProjections.cs` becomes a factory taking `userId`.
  - `ImageQueryRepository` (favoritesOnly filter, rows, set favorite).
  - `AlbumRepository.ListImagesAsync`.
  - `DuplicateService`/`ImageQueryService` row mapping.
- `IsFavorite` is computed as `UserFavorites.Any(f => f.UserId == userId && f.ImageId == i.Id)`.
- The API contract for `PUT/DELETE /images/{id}/favorite` is unchanged.

**Frontend:**
- `AlbumsPage`: "My albums" and "Shared with me" sections; shared cards show the owner name.
- `AlbumView`: gate edit controls on `access`. Owners get a Share button opening a new `ShareDialog`
  (directory picker, Viewer/Editor select, current shares with remove). Non-owners get a "Leave album"
  action.
- `AlbumPicker`/`useAlbumAdder`: offer only albums where `access` is Editor or Owner.

## Error handling

- Login failure is always the generic 401 "Invalid username or password", including for disabled users.
- A 429 from the rate limiter shows "Too many attempts, try again in a minute".
- Sharing with yourself, with an inactive user or with an unknown user → 400 validation problem.
- Guard violations (last admin, self-demotion) → 409 ProblemDetails, shown through the existing
  `notify` toasts.

## Testing

- **Unit (xUnit + FluentAssertions + NSubstitute):**
  - `AuthService`: login success/failure/disabled, change password.
  - `UserService`: duplicate username, last-admin guard, self guards, delete with transfer and name clash.
  - `AlbumService`: the access matrix above, including 404 vs 403.
  - `AdminBootstrapper`.
- **Repository tests:** follow the existing infrastructure test pattern. Cover:
  - `GetAccessibleAsync` and share cascade.
  - Per-user favorite isolation between two users.
  - The favorites data migration.
- **API smoke tests** (`tests/PictureManager.Api.Tests/Smoke/ApiSmokeFixture.cs`):
  - Add a logged-in client helper.
  - Every `/api` endpoint except auth/health/ping returns 401 anonymous.
  - Every admin-surface endpoint returns 403 for a `User`.
  - Every folder-actions endpoint returns 403 for a `User` without `CanRunFolderActions`, and is allowed
    for one with it and for an admin. The moved read-only status endpoints are allowed for any `User`.
  - A must-change-password user gets 403 everywhere except `/api/auth/*`.
  - The login cookie round-trip works.
- **Frontend (Vitest + MSW, existing `web/src/test/` handlers):**
  - Login flow, and a 401 bouncing to login.
  - Forced password change.
  - Admin menu and controls hidden for a `User`.
  - Folder actions menu hidden without `canRunFolderActions`, shown with it, while scan progress stays visible.
  - `UsersPage` CRUD.
  - `ShareDialog`, and a Viewer seeing a read-only `AlbumView`.

## Verification (end to end)

1. `dotnet test` and `npm run test` pass after each phase.
2. Set `Auth__InitialAdmin__Password` in `docker-compose.prod.yml` (that file already has local
   uncommitted changes, so edit with care) and start the app:
   - Log in as `admin`. You are forced to change the password.
   - Existing albums and favorites are still there.
3. As admin, create user `bob` and log in as bob in a private window. Check that bob:
   - Must change his password.
   - Sees no Admin menu, no hide/rotate/face controls, and gets 403 when calling those endpoints directly.
   - Has empty favorites.
   - Sees no folder actions menu, but does see the progress banner while the admin runs a scan.
   - After the admin enables "Can run folder actions" for bob, he sees the folder actions within
     about 60 s and can start a scan.
4. Share an admin album with bob as Viewer: he can see and export it but not edit. Change the share to
   Editor: he can add images, but cannot rename or delete the album.
5. Disable bob: his open session gets 401 within about 60 s. Delete bob with `transfer`: his albums
   move to the admin.
6. Restart the container: the session survives, which confirms the persisted Data Protection keys.

## Next steps once this plan is approved

1. Save this design as `docs/superpowers/specs/2026-10-09-user-management-design.md` and commit it.
2. Write the phase-1 implementation plan with the writing-plans skill.
3. Implementation then proceeds one phase at a time.
