using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Application.Users;
using PictureManager.Model;
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
    private const string InitialAdminPasswordVariable = "Auth__InitialAdmin__Password";
    private const string LoginLimitVariable = "Auth__LoginAttemptsPerMinute";
    public const string InitialAdminPassword = "initial-admin-pw";
    public const string AdminPassword = "smoke-admin-pw";
    public const string UserPassword = "smoke-user-pw";

    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), "pm-smoke-cache-" + Guid.NewGuid().ToString("N"));

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

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        Environment.SetEnvironmentVariable(ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(CacheRootVariable, null);
        Environment.SetEnvironmentVariable(InitialAdminPasswordVariable, null);
        Environment.SetEnvironmentVariable(LoginLimitVariable, null);
        await Database.DisposeAsync();

        // The cache directory is only created on demand, so it may not exist.
        if (Directory.Exists(_cacheRoot))
            Directory.Delete(_cacheRoot, recursive: true);
    }
}
