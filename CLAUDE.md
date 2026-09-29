# ASP.NET Core & React 19 Project Guide

## Codebase Architecture
- **Backend:** .NET 10, ASP.NET Core Minimal APIs -> Located in `/src`.
- **ORM** Entity Framework Core 10 with PostgreSQL
- **Database:** PostgreSQL
- **Frontend:** React 19 + TypeScript + Vite + MUI + Tailwind CSS v4-> Located in `/web`.
- **Frontend Server state:** TanStack Query.
- **Frontend Grid virtualization:** TanStack Virtual or react-virtuoso.
- **Deployment:** Docker, single container
- **Testing** xUnit, FluentAssertions, NSubstitute
- **Logging:** Serilog
- **Knowledge Base:** Use the local `tokensave` MCP server tools to trace symbol dependencies, callers/callees, and full-stack impact radiuses before writing files.

## Build & Test Commands
- **Backend Build:** `dotnet build`
- **Backend Test:** `dotnet test`
- **Frontend Build:** `npm run build`
- **Frontend Test:** `npm run test`

## Coding Conventions
### C# Backend:
- Use Minimal APIs or explicit asynchronous Controller Actions (`public async Task<IActionResult>`).
- Follow modern C# features (file-scoped namespaces, pattern matching, primary constructors).
- Keep DbContext changes inside scoped transaction boundaries; prioritize clean LINQ execution.
- Do NOT run full project builds just to check errors; let `dotnet build` errors guide single file edits.

### React 19 Frontend:
- Use strict TypeScript components; explicitly type component props.
- Leverage React 19 features natively: use the `use()` hook for async resources/promises and utilize standard Server/Client Action models (`useActionState`).
- Avoid outdated state-management patterns if native React 19 hooks or simple Context API suffice.
- Use explicit semantic path naming for API endpoints fetching from the C# backend.

## Token Optimization Rules
- Never use `grep` or raw `cat` loops across entire directories. Use `tokensave` MCP tools for symbol search and dependency routing.
- Prefer the tokensave MCP tools (search/callers/context) over Bash find/grep or Read for locating code
- Do not let build errors dump more than 20 lines of continuous text into terminal responses. Strip long package traces.
