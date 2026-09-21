# Phase 1 — Scaffold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the PictureManager solution skeleton — all backend projects wired together with DI,
logging, and a Postgres-backed health check; all test projects running; the React/TS/Vite frontend
scaffolded with MUI, Tailwind, ESLint/Prettier, and env var support — so every later phase adds real
features to a project that already builds, tests, and runs end-to-end.

**Architecture:** Five backend class libraries/hosts (Model, Application, Infrastructure, Worker, Api)
wired by constructor DI via one `AddXxx(IServiceCollection)` extension method per layer, composed in
`Api/Program.cs`. No domain entities yet (Model stays empty — phase 2 adds them) and no scanning logic yet
(Worker stays an empty project — phase 3 adds the real `BackgroundService`). This phase proves the
plumbing: config → DI → logging → DB connectivity → health endpoint, and the SPA toolchain.

**Tech Stack:** .NET 10 (SDK 10.0.302, `net10.0` TFM), ASP.NET Core minimal APIs, EF Core 10 + Npgsql,
Serilog (console + rolling file), AspNetCore.HealthChecks.NpgSql, xUnit + FluentAssertions + NSubstitute,
React 19 + TypeScript + Vite, MUI, Tailwind CSS v4, ESLint + Prettier, Docker Compose (Postgres only in
this phase).

**Spec:** [`docs/superpowers/specs/2026-09-21-picturemanager-v1-design.md`](../specs/2026-09-21-picturemanager-v1-design.md)
(and the source brief it implements, [`Documents/PictureManager-brief.md`](../../../Documents/PictureManager-brief.md))

## Global Constraints

- Target framework: `net10.0` everywhere on the backend.
- **FluentAssertions must be pinned below v8** (`[7.0.0,8.0.0)`) — v8+ requires a paid Xceed license for
  commercial use; v7.x is the last free major version.
- Mocking library: **NSubstitute** (MIT-licensed), not Moq.
- Store all relative paths with `/` separators; normalize to NFC and compare case-insensitively — not
  exercised until phase 3, but keep filenames/namespaces ASCII and predictable now.
- The app is strictly read-only against any mounted image folder — nothing in this phase writes to
  `dev-data/images/`.
- Every `AddXxx` DI extension method takes `IServiceCollection` (and `IConfiguration` where it needs
  config) and returns `IServiceCollection` for chaining, per the brief's "interfaces for all Application
  services, repositories for data access" requirement.
- Node package manager: npm (matches the brief's Dockerfile `npm ci`).
- Imaging library for later phases is SkiaSharp, not ImageSharp (per spec decision) — not used yet in this
  phase, but don't add an ImageSharp package reference anywhere.

---

## Task 1: Solution and project scaffolding

**Files:**
- Create: `PictureManager.slnx` (SDK 10.0.302's `dotnet new sln` defaults to the new XML solution format —
  accept `.slnx`, not classic `.sln`; there's no legacy-tooling constraint on a brand-new project)
- Create: `src/PictureManager.Model/PictureManager.Model.csproj`
- Create: `src/PictureManager.Application/PictureManager.Application.csproj`
- Create: `src/PictureManager.Infrastructure/PictureManager.Infrastructure.csproj`
- Create: `src/PictureManager.Worker/PictureManager.Worker.csproj`
- Create: `src/PictureManager.Api/PictureManager.Api.csproj` (+ generated `Program.cs`, `appsettings.json`)
- Create: `tests/PictureManager.Application.Tests/PictureManager.Application.Tests.csproj`
- Create: `tests/PictureManager.Infrastructure.Tests/PictureManager.Infrastructure.Tests.csproj`
- Create: `global.json` (pins the SDK)

**Interfaces:**
- Produces: the project reference graph every later task builds on —
  `Application → Model`, `Infrastructure → Application, Model`, `Worker → Application`,
  `Api → Application, Infrastructure, Worker`, `Application.Tests → Application`,
  `Infrastructure.Tests → Infrastructure`.

- [ ] **Step 1: Pin the SDK**

```bash
cd /c/Work/PictureManager
dotnet new globaljson --sdk-version 10.0.302
```

- [ ] **Step 2: Create the solution file**

```bash
dotnet new sln -n PictureManager
```

- [ ] **Step 3: Create the five backend projects**

```bash
dotnet new classlib -n PictureManager.Model -o src/PictureManager.Model -f net10.0
dotnet new classlib -n PictureManager.Application -o src/PictureManager.Application -f net10.0
dotnet new classlib -n PictureManager.Infrastructure -o src/PictureManager.Infrastructure -f net10.0
dotnet new classlib -n PictureManager.Worker -o src/PictureManager.Worker -f net10.0
dotnet new web -n PictureManager.Api -o src/PictureManager.Api -f net10.0
```

Delete the template placeholder classes (`Class1.cs`) from the four class libraries — they're empty
projects until later phases add real types:

```bash
rm src/PictureManager.Model/Class1.cs
rm src/PictureManager.Application/Class1.cs
rm src/PictureManager.Infrastructure/Class1.cs
rm src/PictureManager.Worker/Class1.cs
```

- [ ] **Step 4: Create the two test projects**

```bash
dotnet new xunit -n PictureManager.Application.Tests -o tests/PictureManager.Application.Tests -f net10.0
dotnet new xunit -n PictureManager.Infrastructure.Tests -o tests/PictureManager.Infrastructure.Tests -f net10.0
rm tests/PictureManager.Application.Tests/UnitTest1.cs
rm tests/PictureManager.Infrastructure.Tests/UnitTest1.cs
```

- [ ] **Step 5: Add project references**

```bash
dotnet add src/PictureManager.Application reference src/PictureManager.Model
dotnet add src/PictureManager.Infrastructure reference src/PictureManager.Application src/PictureManager.Model
dotnet add src/PictureManager.Worker reference src/PictureManager.Application
dotnet add src/PictureManager.Api reference src/PictureManager.Application src/PictureManager.Infrastructure src/PictureManager.Worker
dotnet add tests/PictureManager.Application.Tests reference src/PictureManager.Application
dotnet add tests/PictureManager.Infrastructure.Tests reference src/PictureManager.Infrastructure
```

- [ ] **Step 6: Pin FluentAssertions and add NSubstitute to both test projects**

```bash
dotnet add tests/PictureManager.Application.Tests package FluentAssertions --version "[7.0.0,8.0.0)"
dotnet add tests/PictureManager.Application.Tests package NSubstitute
dotnet add tests/PictureManager.Infrastructure.Tests package FluentAssertions --version "[7.0.0,8.0.0)"
dotnet add tests/PictureManager.Infrastructure.Tests package NSubstitute
```

- [ ] **Step 7: Add every project to the solution**

```bash
dotnet sln add src/PictureManager.Model src/PictureManager.Application src/PictureManager.Infrastructure src/PictureManager.Worker src/PictureManager.Api tests/PictureManager.Application.Tests tests/PictureManager.Infrastructure.Tests
```

- [ ] **Step 8: Build and test to confirm the skeleton compiles**

Run: `dotnet build`
Expected: Build succeeds, 7 projects.

Run: `dotnet test`
Expected: 0 tests run (no test files yet), 0 failures.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "chore: scaffold PictureManager solution and project references"
```

---

## Task 2: `IClock` abstraction in Application

**Files:**
- Create: `src/PictureManager.Application/Common/IClock.cs`
- Create: `src/PictureManager.Application/Common/SystemClock.cs`
- Create: `src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Application.Tests/Common/SystemClockTests.cs`
- Test: `tests/PictureManager.Application.Tests/DependencyInjection/ApplicationServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Produces: `PictureManager.Application.Common.IClock` (property `DateTime UtcNow { get; }`),
  `PictureManager.Application.Common.SystemClock : IClock`,
  `PictureManager.Application.DependencyInjection.ApplicationServiceCollectionExtensions.AddApplication(this IServiceCollection services) : IServiceCollection`.
  Later phases (EXIF timestamps, `FirstSeenUtc`/`MissingSinceUtc`, `CreatedAt`/`UpdatedAt` columns) inject
  `IClock` instead of calling `DateTime.UtcNow` directly, so tests can control time.

- [ ] **Step 1: Add the DI packages needed**

`PictureManager.Application` only needs the `IServiceCollection` abstraction for its extension method.
`PictureManager.Application.Tests` needs the concrete `Microsoft.Extensions.DependencyInjection` package
too — it instantiates `ServiceCollection` and calls `.BuildServiceProvider()`, neither of which is in the
Abstractions package, and nothing installed in Task 1 provides it transitively.

```bash
dotnet add src/PictureManager.Application package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet add tests/PictureManager.Application.Tests package Microsoft.Extensions.DependencyInjection
```

- [ ] **Step 2: Write the failing tests**

`tests/PictureManager.Application.Tests/Common/SystemClockTests.cs`:

```csharp
using System;
using FluentAssertions;
using PictureManager.Application.Common;
using Xunit;

namespace PictureManager.Application.Tests.Common;

public class SystemClockTests
{
    [Fact]
    public void UtcNow_ReturnsCurrentUtcTime()
    {
        var clock = new SystemClock();
        var before = DateTime.UtcNow;

        var result = clock.UtcNow;

        var after = DateTime.UtcNow;
        result.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        result.Kind.Should().Be(DateTimeKind.Utc);
    }
}
```

`tests/PictureManager.Application.Tests/DependencyInjection/ApplicationServiceCollectionExtensionsTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Application.Common;
using PictureManager.Application.DependencyInjection;
using Xunit;

namespace PictureManager.Application.Tests.DependencyInjection;

public class ApplicationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddApplication_RegistersIClockAsSystemClock()
    {
        var services = new ServiceCollection();

        services.AddApplication();
        var provider = services.BuildServiceProvider();

        var clock = provider.GetService<IClock>();

        clock.Should().NotBeNull();
        clock.Should().BeOfType<SystemClock>();
    }
}
```

- [ ] **Step 3: Run tests to verify they fail to compile**

Run: `dotnet test tests/PictureManager.Application.Tests`
Expected: FAIL — `IClock`, `SystemClock`, `AddApplication` do not exist.

- [ ] **Step 4: Implement**

`src/PictureManager.Application/Common/IClock.cs`:

```csharp
using System;

namespace PictureManager.Application.Common;

public interface IClock
{
    DateTime UtcNow { get; }
}
```

`src/PictureManager.Application/Common/SystemClock.cs`:

```csharp
using System;

namespace PictureManager.Application.Common;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
```

`src/PictureManager.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Application.Common;

namespace PictureManager.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/PictureManager.Application.Tests`
Expected: PASS, 2 tests.

- [ ] **Step 6: Commit**

```bash
git add src/PictureManager.Application tests/PictureManager.Application.Tests
git commit -m "feat: add IClock abstraction and AddApplication DI extension"
```

---

## Task 3: EF Core + Npgsql `PictureManagerDbContext`

**Files:**
- Create: `src/PictureManager.Infrastructure/Persistence/PictureManagerDbContext.cs`
- Create: `src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Test: `tests/PictureManager.Infrastructure.Tests/DependencyInjection/InfrastructureServiceCollectionExtensionsTests.cs`
- Create: `.config/dotnet-tools.json` (local tool manifest for `dotnet-ef`, needed starting phase 2)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `PictureManager.Infrastructure.Persistence.PictureManagerDbContext : DbContext` (no `DbSet`s
  yet — phase 2 adds them), `PictureManager.Infrastructure.DependencyInjection.InfrastructureServiceCollectionExtensions.AddInfrastructure(this IServiceCollection services, IConfiguration configuration) : IServiceCollection`
  which reads `ConnectionStrings:PictureManagerDb` and throws `InvalidOperationException` if missing.
  Task 4 (Api host) and Task 5 (docker-compose) both depend on the `ConnectionStrings:PictureManagerDb`
  config key name.

- [ ] **Step 1: Add EF Core / Npgsql packages and the `dotnet-ef` local tool**

`PictureManager.Infrastructure` also needs `Microsoft.Extensions.Configuration.Abstractions` explicitly —
its `AddInfrastructure` method takes an `IConfiguration` parameter, and that type isn't part of the
`Microsoft.EntityFrameworkCore` dependency chain. The test project needs the concrete
`Microsoft.Extensions.Configuration` package too (for `ConfigurationBuilder`/`AddInMemoryCollection`,
neither of which is in the Abstractions package) and the concrete `Microsoft.Extensions.DependencyInjection`
package (for `ServiceCollection`/`BuildServiceProvider()`) — don't rely on either arriving transitively.

```bash
dotnet add src/PictureManager.Infrastructure package Microsoft.EntityFrameworkCore
dotnet add src/PictureManager.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/PictureManager.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet add src/PictureManager.Infrastructure package Microsoft.Extensions.Configuration.Abstractions
dotnet add tests/PictureManager.Infrastructure.Tests package Microsoft.Extensions.Configuration
dotnet add tests/PictureManager.Infrastructure.Tests package Microsoft.Extensions.DependencyInjection
dotnet new tool-manifest
dotnet tool install dotnet-ef
```

- [ ] **Step 2: Write the failing tests**

`tests/PictureManager.Infrastructure.Tests/DependencyInjection/InfrastructureServiceCollectionExtensionsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Infrastructure.DependencyInjection;
using PictureManager.Infrastructure.Persistence;
using Xunit;

namespace PictureManager.Infrastructure.Tests.DependencyInjection;

public class InfrastructureServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInfrastructure_RegistersPictureManagerDbContext()
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

        var dbContext = provider.GetService<PictureManagerDbContext>();

        dbContext.Should().NotBeNull();
    }

    [Fact]
    public void AddInfrastructure_MissingConnectionString_Throws()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        var act = () => services.AddInfrastructure(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PictureManagerDb*");
    }
}
```

- [ ] **Step 3: Run tests to verify they fail to compile**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests`
Expected: FAIL — `PictureManagerDbContext`, `AddInfrastructure` do not exist.

- [ ] **Step 4: Implement**

`src/PictureManager.Infrastructure/Persistence/PictureManagerDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace PictureManager.Infrastructure.Persistence;

public sealed class PictureManagerDbContext : DbContext
{
    public PictureManagerDbContext(DbContextOptions<PictureManagerDbContext> options)
        : base(options)
    {
    }
}
```

`src/PictureManager.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`:

```csharp
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Infrastructure.Persistence;

namespace PictureManager.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PictureManagerDb")
            ?? throw new InvalidOperationException(
                "Connection string 'PictureManagerDb' is not configured.");

        services.AddDbContext<PictureManagerDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/PictureManager.Infrastructure.Tests`
Expected: PASS, 2 tests. (No real Postgres connection is opened — `AddDbContext`/`UseNpgsql` only
validates and stores the connection string.)

- [ ] **Step 6: Commit**

```bash
git add src/PictureManager.Infrastructure tests/PictureManager.Infrastructure.Tests .config
git commit -m "feat: add PictureManagerDbContext and AddInfrastructure DI extension"
```

---

## Task 4: Api host — Serilog, DI composition, health endpoint

**Files:**
- Modify: `src/PictureManager.Api/Program.cs`
- Modify: `src/PictureManager.Api/appsettings.json`
- Modify: `src/PictureManager.Api/appsettings.Development.json`

**Interfaces:**
- Consumes: `AddApplication()` (Task 2), `AddInfrastructure(IConfiguration)` (Task 3),
  `ConnectionStrings:PictureManagerDb` config key (Task 3).
- Produces: `GET /api/health` (200 `Healthy` text when Postgres is reachable, otherwise 503),
  `GET /api/ping` (200 `{"status":"ok"}`) — a trivial liveness route with no DB dependency, useful once
  container health checks are added in phase 7.

- [ ] **Step 1: Add Serilog and health check packages**

```bash
dotnet add src/PictureManager.Api package Serilog.AspNetCore
dotnet add src/PictureManager.Api package Serilog.Sinks.Console
dotnet add src/PictureManager.Api package Serilog.Sinks.File
dotnet add src/PictureManager.Api package AspNetCore.HealthChecks.NpgSql
```

- [ ] **Step 2: Replace `Program.cs`**

```csharp
using Microsoft.Extensions.Configuration;
using PictureManager.Application.DependencyInjection;
using PictureManager.Infrastructure.DependencyInjection;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile(
            $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json",
            optional: true)
        .Build())
    .Enrich.FromLogContext()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting PictureManager.Api");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    var connectionString = builder.Configuration.GetConnectionString("PictureManagerDb")
        ?? throw new InvalidOperationException("Connection string 'PictureManagerDb' is not configured.");

    builder.Services.AddHealthChecks()
        .AddNpgSql(connectionString, name: "postgres");

    var app = builder.Build();

    app.MapHealthChecks("/api/health");
    app.MapGet("/api/ping", () => Results.Ok(new { status = "ok" }));

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "PictureManager.Api terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
```

(Amended post-final-review: the original block here didn't set a non-zero exit code on fatal startup
failure, so a missing connection string would log Fatal and still exit 0 — invisible to Docker
`restart: unless-stopped`, compose healthchecks, and CI, all of which key off exit codes. Fixed with
`Environment.ExitCode = 1;` in the catch.)

- [ ] **Step 3: Replace `appsettings.json`**

```json
{
  "ConnectionStrings": {
    "PictureManagerDb": "Host=localhost;Port=5432;Database=picturemanager;Username=picturemanager;Password=picturemanager"
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/picturemanager-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 14
        }
      }
    ]
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 4: Replace `appsettings.Development.json`**

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug"
    }
  }
}
```

- [ ] **Step 5: Build**

Run: `dotnet build src/PictureManager.Api`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/PictureManager.Api
git commit -m "feat: wire Serilog, DI composition, and health endpoint in Api host"
```

(Manual end-to-end verification of `/api/health` against a real Postgres happens in Task 5, after the
database container exists.)

---

## Task 5: docker-compose Postgres service

**Files:**
- Create: `docker-compose.yml`
- Create: `.env.example`

**Interfaces:**
- Consumes: `ConnectionStrings:PictureManagerDb` in `src/PictureManager.Api/appsettings.json` (Task 4) —
  the compose Postgres service's `POSTGRES_USER`/`POSTGRES_PASSWORD`/`POSTGRES_DB` defaults must match the
  Username/Password/Database in that connection string so `dotnet run` connects to it without extra config.

Note: this task only adds the `db` service. The `dev-data/images` read-only bind mount belongs to the
`app` service, which doesn't exist until phase 7 builds the Dockerfile and containerizes the Api. Until
then, `dotnet run` (phases 2-6) reads `dev-data/images` directly off the host filesystem — no container,
no bind mount needed yet.

- [ ] **Step 1: Create `.env.example`**

```
POSTGRES_DB=picturemanager
POSTGRES_USER=picturemanager
POSTGRES_PASSWORD=picturemanager
```

- [ ] **Step 2: Create `docker-compose.yml`**

```yaml
services:
  db:
    image: postgres:17-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: ${POSTGRES_DB:-picturemanager}
      POSTGRES_USER: ${POSTGRES_USER:-picturemanager}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-picturemanager}
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U $${POSTGRES_USER:-picturemanager}"]
      interval: 5s
      timeout: 5s
      retries: 10

volumes:
  pgdata:
```

- [ ] **Step 3: Start Postgres and verify it's healthy**

```bash
docker compose up -d db
docker compose ps
```

Expected: `db` service shows `healthy` within ~10-15s (poll `docker compose ps` if it's still `starting`).

- [ ] **Step 4: Run the Api against real Postgres and hit the health endpoint**

```bash
dotnet run --project src/PictureManager.Api &
```

Note the `Now listening on: http://localhost:PORT` line, then:

```bash
curl -i http://localhost:PORT/api/health
curl -i http://localhost:PORT/api/ping
```

Expected: `/api/health` → `200 OK`, body `Healthy`. `/api/ping` → `200 OK`, body `{"status":"ok"}`.

Stop the API process, then:

```bash
docker compose down
```

- [ ] **Step 5: Commit**

```bash
git add docker-compose.yml .env.example
git commit -m "chore: add docker-compose Postgres service for local dev"
```

---

## Task 6: Frontend scaffold — Vite/React/TS + MUI + Tailwind v4 + ESLint/Prettier + env vars

**Files:**
- Create: `web/` (via `npm create vite`)
- Modify: `web/vite.config.ts`
- Modify: `web/src/index.css`
- Modify: `web/src/App.tsx`
- Create: `web/src/config/env.ts`
- Modify: `web/src/vite-env.d.ts`
- Modify: `web/eslint.config.js`
- Create: `web/.prettierrc.json`
- Create: `web/.prettierignore`
- Create: `web/.env.example`
- Modify: `web/package.json` (add `format`/`format:check` scripts)

**Interfaces:**
- Produces: `web/src/config/env.ts` exporting `const env: { apiBaseUrl: string }`, read from
  `import.meta.env.VITE_API_BASE_URL`. Phase 6 (frontend API integration) imports `env.apiBaseUrl` as the
  base for TanStack Query fetches instead of hardcoding a URL.

- [ ] **Step 1: Scaffold the Vite React-TS project**

```bash
cd /c/Work/PictureManager
npm create vite@latest web -- --template react-ts
cd web
npm install
```

- [ ] **Step 2: Install MUI**

```bash
npm install @mui/material @emotion/react @emotion/styled @mui/icons-material
```

- [ ] **Step 3: Install Tailwind CSS v4 (Vite plugin, no separate config file)**

```bash
npm install tailwindcss @tailwindcss/vite
```

Edit `web/vite.config.ts`:

```typescript
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
})
```

Replace the contents of `web/src/index.css` — import only Tailwind's theme and utility layers, skipping
`preflight` (Tailwind's base reset) so it doesn't fight MUI's `CssBaseline`:

```css
@import 'tailwindcss/theme.css' layer(theme);
@import 'tailwindcss/utilities.css' layer(utilities);
```

- [ ] **Step 4: Install ESLint/Prettier integration**

```bash
npm install -D prettier eslint-config-prettier
```

Create `web/.prettierrc.json`:

```json
{
  "semi": false,
  "singleQuote": true,
  "trailingComma": "all",
  "printWidth": 100
}
```

Create `web/.prettierignore`:

```
dist
node_modules
```

Edit `web/eslint.config.js` — add the `eslint-config-prettier` import and spread it last in the config
array (it only disables stylistic rules, so it must come after the other configs to win):

```javascript
import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import prettier from 'eslint-config-prettier'

export default tseslint.config(
  { ignores: ['dist'] },
  {
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    files: ['**/*.{ts,tsx}'],
    languageOptions: {
      ecmaVersion: 2020,
      globals: globals.browser,
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
    },
  },
  prettier,
)
```

(If the generated `eslint.config.js` differs from this shape because of the Vite template version
actually scaffolded in Step 1, keep its existing rule blocks and just add the `prettier` import plus
`prettier` as the last entry in the exported config array — the point is that it wins last.)

Add scripts to `web/package.json` (in the `"scripts"` object, alongside the existing `dev`/`build`/`lint`/`preview`):

```json
    "format": "prettier --write .",
    "format:check": "prettier --check ."
```

- [ ] **Step 5: Env var support**

Create `web/.env.example`:

```
VITE_API_BASE_URL=http://localhost:5080
```

Replace `web/src/vite-env.d.ts`:

```typescript
/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
```

Create `web/src/config/env.ts`:

```typescript
export const env = {
  apiBaseUrl: import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080',
}
```

- [ ] **Step 6: Prove MUI + Tailwind + env vars all wire together**

Replace `web/src/App.tsx`:

```tsx
import { ThemeProvider, createTheme, CssBaseline, Button, Typography } from '@mui/material'
import { env } from './config/env'

const theme = createTheme()

function App() {
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <div className="p-8">
        <Typography variant="h4" gutterBottom>
          PictureManager
        </Typography>
        <Typography variant="body2" sx={{ mb: 4 }}>
          API base URL: {env.apiBaseUrl}
        </Typography>
        <Button variant="contained">Scaffold OK</Button>
      </div>
    </ThemeProvider>
  )
}

export default App
```

(Amended post-final-review: the original block used a Tailwind `className="mb-4"` on the MUI
`Typography` — that silently does nothing, because MUI/Emotion injects unlayered CSS, which always wins
the cascade over anything Tailwind puts in a `@layer`, regardless of specificity. The smoke test claimed
to prove "MUI + Tailwind + env vars wire together" while actually demonstrating the opposite for MUI
components. Fixed by adopting the policy: Tailwind utility classes style non-MUI layout (the wrapping
`<div className="p-8">` still works — no MUI style competes with it), MUI's own `sx` prop styles MUI
components. This is now the project convention; see the design spec's frontend section. Revisit with
`<StyledEngineProvider enableCssLayer>` + an explicit `@layer theme, mui, utilities;` order once Playwright
(phase 8) can verify rendered output in a real browser — not attempted here since nothing in this pipeline
can confirm cascade-layer behavior visually.)

- [ ] **Step 7: Verify build and lint**

```bash
npm run build
npm run lint
npm run format:check
```

Expected: all three succeed with no errors. (`format:check` may report the files this task just
hand-edited as unformatted — if so, run `npm run format` once and re-check.)

- [ ] **Step 8: Commit**

```bash
cd /c/Work/PictureManager
git add web
git commit -m "chore: scaffold React/TS/Vite frontend with MUI, Tailwind v4, ESLint/Prettier, env vars"
```

---

## Phase 1 exit criteria

- `dotnet build` and `dotnet test` succeed from the repo root across all 7 backend projects.
- `docker compose up -d db` + `dotnet run --project src/PictureManager.Api` serves a `200 Healthy` at
  `/api/health`.
- `npm run build`, `npm run lint`, and `npm run format:check` succeed in `web/`.
- Everything committed to git on `master`.
