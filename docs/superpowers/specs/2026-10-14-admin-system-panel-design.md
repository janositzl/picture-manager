# Admin "System" panel — deployed version + recent logs

## Context
Admins currently have no way to see, from inside the app, (a) which build is deployed on the server or (b) what the app has been logging, without SSHing in and running `docker compose logs` or reading the `api-logs` volume. The app also has no version concept at all: no version in the csproj, no git tags, and `.git` is excluded from the Docker build context.

The outcome is a new **System** tab under `/admin`. It shows the deployed version, build time, start time, and environment, plus a tail of the current Serilog log file with Warning/Error/Fatal entries highlighted and a "warnings & errors only" filter. Developers get a single place to set the version (`VERSION` file) and a small script to bump it.

Decisions already made with the user:
- **Versioning:** SemVer in a repo-root `VERSION` file, bumped by hand or by a script.
- **Commit hash:** not shown.
- **Log viewer:** a tail of the latest file plus a level filter. No picker for older files and no download.

## Versioning (dev side)
- **New `VERSION`** at the repo root containing `1.0.0`. This is the single source of truth.
- **New `Directory.Build.props`** at the repo root, applying to all projects:
  - `<Version>$([System.IO.File]::ReadAllText('$(MSBuildThisFileDirectory)VERSION').Trim())</Version>`
  - `<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>`. This stops the SDK from appending `+<sha>` in local builds, so local and Docker builds report the same string.
- **`Dockerfile` and `Dockerfile.nginx`:** add `COPY VERSION Directory.Build.props ./` before `dotnet restore`. The props file is imported during restore and would fail without `VERSION`.
- **New `tools/bump-version.ps1 [major|minor|patch]`:**
  - Rewrites `VERSION`.
  - Runs `git commit -m "chore: release vX.Y.Z"` and `git tag vX.Y.Z`.
  - Does not push. It prints the push command instead.
- **Build time:** no MSBuild timestamp, which would force a recompile on every build. At runtime it uses `File.GetLastWriteTimeUtc` of the API assembly. In Docker that is the publish time, because `COPY --from` keeps mtimes.
- **Docs:** add a short "Versioning & releases" section to `Documents/PictureManager-Docker-Deployment-Guide.md` and the README: bump → commit/tag → push → on the server `git pull && docker compose ... up -d --build` → check in Admin › System.

## Backend
### Application layer (pure, testable)
`src/PictureManager.Application/Diagnostics/`:
- **`LogEntry`** record: `Timestamp` (string as written), `Level` (`Verbose|Debug|Information|Warning|Error|Fatal`, or null for unparsed lines), `Message`, `Exception` (continuation lines joined, or null).
- **`SerilogFileLineParser`** groups raw lines into entries.
  - It matches the default Serilog file template `{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}` with the regex `^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(VRB|DBG|INF|WRN|ERR|FTL)\] (.*)$`.
  - Lines that don't match (stack traces, multi-line messages) attach to the previous entry's `Exception`/continuation.
  - A leading orphan fragment becomes an entry with a null level.
- **`ILogReader`**: `Task<LogTail> ReadTailAsync(int take, LogLevelFilter minLevel, CancellationToken)`. `LogTail` holds the `FileName` (or null when there's no log file), `Entries`, and `Truncated`.
- **`AppVersionInfo`** record: `Version`, `BuildTimeUtc`, `StartedUtc`, `Environment`, `Runtime`.

### Infrastructure
**`src/PictureManager.Infrastructure/Diagnostics/FileLogReader.cs`** implements `ILogReader`:
- **Configuration:** built from a `LogFileOptions { Directory, FilePattern }`.
- **File choice:** picks the newest `picturemanager-*.log` by `LastWriteTimeUtc`, which also covers `_001` size-rolled files.
- **Locked file:** opens it with `FileShare.ReadWrite | FileShare.Delete`, because Serilog holds the file open.
- **Tail read:**
  - Seeks backwards in 64 KB chunks until it has more than `take` entries' worth of lines, capped at 4 MB.
  - Drops the first partial line when the read didn't start at offset 0.
  - Parses with `SerilogFileLineParser`, filters by level, and returns the last `take` entries.
- **Registration:** register in `InfrastructureServiceCollectionExtensions`.

### Api
- **`LogFileOptions.FromSerilogConfig(IConfiguration)`**, called in `Program.cs`. It finds the `Serilog:WriteTo` entry with `Name == "File"`, reads `Args:path` (`logs/picturemanager-.log`), resolves it against `Directory.GetCurrentDirectory()` exactly as Serilog does, and derives the pattern `picturemanager-*.log`. Without a File sink, the reader reports "no log file configured".
- **New `src/PictureManager.Api/Endpoints/SystemEndpoints.cs`**, following the `SettingsEndpoints` pattern:
  - `GET /api/system/info` returns `AppVersionInfo`. The version comes from `AssemblyInformationalVersionAttribute` on `typeof(Program).Assembly`, the start time from `Process.GetCurrentProcess().StartTime.ToUniversalTime()`, the environment from `IHostEnvironment.EnvironmentName`, and the runtime from `RuntimeInformation.FrameworkDescription`.
  - `GET /api/system/logs?take=500&minLevel=Warning` returns `LogTail`. `take` is clamped to 1..2000 and `minLevel` defaults to `Verbose`.
- **`Program.cs`:** add `admin.MapSystemEndpoints();` next to `admin.MapSettingsEndpoints();`. It goes in the admin group, so `AuthPolicies.AdminOnly` and `ApiSurface.Admin` apply.

## Frontend
- **`web/src/api/types.ts`:** add `AppVersionInfo`, `LogEntry`, and `LogTail` types.
- **`web/src/api/queries.ts`:** add `queryKeys.systemInfo()` and `queryKeys.systemLogs(minLevel)`, plus query options that follow the existing `queryOptions` style.
- **New `web/src/admin/SystemPage.tsx`:**
  - **Version card:** large version string, "Built 2026-10-14 10:32", "Running since … (uptime 3d 4h)", environment, runtime.
  - **Logs section:**
    - Toolbar: file name, a "Warnings & errors only" switch (`minLevel=Warning`), and a Refresh button, which refetches the query.
    - Monospace, virtualized-free list (500 entries is fine).
    - Each entry shows the time, a level chip, and the message.
    - Error/Fatal entries get a red left border and tinted background; Warning entries get amber; Debug/Verbose are muted.
    - An exception or continuation text appears in a collapsible `<pre>` under its entry, expanded by default for errors.
    - The list scrolls to the bottom (newest) on load.
    - Empty and "no log file" states.
- **`AdminLayout.tsx`:** add `{ to: '/admin/system', label: 'System' }` to `TABS`.
- **`routes.tsx`:** add `{ path: 'system', element: <SystemPage /> }`.
- **`web/src/test/handlers.ts` / `fixtures.ts`:** add default msw handlers for both endpoints.

## Tests
- **Application.Tests, `SerilogFileLineParserTests`:** single-line entries; multi-line exception grouping; leading orphan fragment; every level code.
- **Infrastructure tests (or Application.Tests with a temp dir), `FileLogReaderTests`:**
  - Newest file is chosen.
  - Tail returns the last N entries.
  - Read starts mid-file and drops the partial line.
  - Level filter works.
  - Missing directory returns an empty tail.
  - File held open by another `FileStream` with write share still reads.
- **Api.Tests:**
  - Add `[InlineData("GET", "/api/system/info", ApiSurface.Admin)]` and `/api/system/logs` to `ApiSmokeTests`.
  - Smoke test that `/api/system/info` returns the version from `VERSION`.
  - A non-admin gets 403 (follow the `AuthorizationSmokeTests` pattern).
- **Web, `SystemPage.test.tsx`** (pattern: `SettingsPage.test.tsx` with `renderRoutes` + msw):
  - Version is shown.
  - Error entry has the error styling or label.
  - Toggling the switch refetches with `minLevel=Warning`.
  - Empty state.

## Implementation order
0. Save this design to `docs/superpowers/specs/2026-10-14-admin-system-panel-design.md`.
1. `VERSION`, `Directory.Build.props`, Dockerfile COPY lines, `bump-version.ps1`, docs.
2. Parser + tests (TDD).
3. `FileLogReader` + tests.
4. `SystemEndpoints` + `LogFileOptions` + Program.cs wiring + smoke tests.
5. Frontend types/queries → `SystemPage` → tab/route → tests.

## Verification
- `dotnet build`, `dotnet test`, and `npm run build` + `npm run test` in `web/`.
- Run the API locally plus `npm run dev` and log in as admin. Under Admin › System, check:
  - The version reads `1.0.0` and the build time is recent.
  - Logs show today's `logs/picturemanager-YYYYMMDD.log`.
  - Triggering a warning (e.g. a scan of an unavailable root) shows a highlighted entry.
  - The "Warnings & errors only" switch filters.
- Run `tools/bump-version.ps1 patch`, rebuild, and check that the panel shows `1.0.1`. Revert the commit and tag afterwards if it was only a test.
- `docker compose -f docker-compose.prod.yml build api` succeeds, which confirms that `VERSION`/props are copied before restore.
