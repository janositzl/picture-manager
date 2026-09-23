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
