using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PictureManager.Infrastructure.Persistence;

namespace PictureManager.Tests.Support;

/// <summary>
/// A throwaway, fully migrated Postgres database for one test. The first use in a test process
/// migrates a template database; every test then gets a cheap copy of it via
/// CREATE DATABASE ... TEMPLATE, and disposing drops the copy. Needs the compose `db` service
/// (`docker compose up -d db`); override the server with PICTUREMANAGER_TEST_POSTGRES.
/// </summary>
public sealed class PostgresTestDatabase : IAsyncDisposable
{
    public const string ConnectionStringVariable = "PICTUREMANAGER_TEST_POSTGRES";
    private const string DefaultServerConnectionString =
        "Host=localhost;Port=5432;Username=picturemanager;Password=picturemanager";

    // Each test assembly (Infrastructure.Tests, Api.Tests) links this file, and `dotnet test` runs
    // assemblies in parallel, so the template name is per assembly to keep them from racing.
    private static readonly string AssemblyTag = typeof(PostgresTestDatabase).Assembly.GetName().Name!
        .Replace("PictureManager.", string.Empty).Replace(".", string.Empty).ToLowerInvariant();
    private static readonly string TemplateName = $"pm_tpl_{AssemblyTag}";
    private static readonly SemaphoreSlim TemplateLock = new(1, 1);
    private static bool _templateReady;

    private readonly string _databaseName;

    private PostgresTestDatabase(string databaseName)
    {
        _databaseName = databaseName;
        ConnectionString = BuildConnectionString(databaseName);
        Context = CreateContext();
    }

    public string ConnectionString { get; }

    public PictureManagerDbContext Context { get; }

    public PictureManagerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PictureManagerDbContext>().UseNpgsql(ConnectionString, o => o.UseVector()).Options);

    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        await EnsureTemplateAsync();
        var databaseName = $"pm_t_{AssemblyTag}_{Guid.NewGuid():N}";
        await ExecuteOnServerAsync($"CREATE DATABASE \"{databaseName}\" TEMPLATE \"{TemplateName}\"");
        return new PostgresTestDatabase(databaseName);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));
        await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    private static async Task EnsureTemplateAsync()
    {
        if (_templateReady)
            return;

        await TemplateLock.WaitAsync();
        try
        {
            if (_templateReady)
                return;

            await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{TemplateName}\" WITH (FORCE)");
            await ExecuteOnServerAsync($"CREATE DATABASE \"{TemplateName}\"");

            var templateConnectionString = BuildConnectionString(TemplateName);
            await using (var context = new PictureManagerDbContext(
                new DbContextOptionsBuilder<PictureManagerDbContext>().UseNpgsql(templateConnectionString, o => o.UseVector()).Options))
            {
                await context.Database.MigrateAsync();
            }

            // CREATE DATABASE ... TEMPLATE fails while any connection to the template is open. UseVector() makes EF keep its own
            // NpgsqlDataSource whose pool ClearPool(connection) does not reach, so also terminate whatever sessions remain.
            NpgsqlConnection.ClearPool(new NpgsqlConnection(templateConnectionString));
            await ExecuteOnServerAsync($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{TemplateName}' AND pid <> pg_backend_pid()");
            _templateReady = true;
        }
        finally
        {
            TemplateLock.Release();
        }
    }

    private static async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(BuildConnectionString("postgres"));
        try
        {
            await connection.OpenAsync();
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            throw new InvalidOperationException(
                $"Postgres-backed tests need a reachable server ({ServerConnectionString()}). " +
                $"Start it with `docker compose up -d db`, or set {ConnectionStringVariable}.", ex);
        }

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string ServerConnectionString() =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable) ?? DefaultServerConnectionString;

    private static string BuildConnectionString(string databaseName) =>
        new NpgsqlConnectionStringBuilder(ServerConnectionString()) { Database = databaseName }.ConnectionString;
}
