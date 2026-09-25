using Microsoft.Extensions.Configuration;
using PictureManager.Api.Endpoints;
using PictureManager.Api.Middleware;
using PictureManager.Application.DependencyInjection;
using PictureManager.Application.Discovery;
using PictureManager.Application.Repositories;
using PictureManager.Application.Roots;
using PictureManager.Application.Scanning;
using PictureManager.Application.Thumbnails;
using PictureManager.Infrastructure.DependencyInjection;
using PictureManager.Worker.DependencyInjection;
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
    builder.Services.AddWorker();
    builder.Services.AddProblemDetails();

    var imageRootsOptions = new ImageRootsOptions
    {
        Entries = builder.Configuration.GetSection("ImageRoots").Get<List<ImageRootConfigEntry>>() ?? new List<ImageRootConfigEntry>()
    };
    builder.Services.AddSingleton(imageRootsOptions);

    var thumbnailCacheOptions = new ThumbnailCacheOptions();
    builder.Configuration.GetSection("ThumbnailCache").Bind(thumbnailCacheOptions);
    builder.Services.AddSingleton(thumbnailCacheOptions);

    builder.Services.AddSingleton(ScanningOptions.FromConfig(
        builder.Configuration.GetSection("Scanning:SupportedExtensions").Get<List<string?>>()));

    var connectionString = builder.Configuration.GetConnectionString("PictureManagerDb")
        ?? throw new InvalidOperationException("Connection string 'PictureManagerDb' is not configured.");

    builder.Services.AddHealthChecks()
        .AddNpgSql(connectionString, name: "postgres");

    var app = builder.Build();

    // Outside Development, unhandled exceptions become a 500 ProblemDetails with no stack trace.
    if (!app.Environment.IsDevelopment())
        app.UseExceptionHandler();

    app.MapHealthChecks("/api/health");
    app.MapGet("/api/ping", () => Results.Ok(new { status = "ok" }));

    using (var scope = app.Services.CreateScope())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<IImageRootSeeder>();
        await seeder.SeedAsync();

        // Before the server accepts requests, so it can only ever fail jobs a previous process left behind.
        var interrupted = await scope.ServiceProvider.GetRequiredService<IScanService>().FailInterruptedJobsAsync();
        if (interrupted > 0)
            Log.Warning("Marked {Count} scan job(s) interrupted by a restart as failed", interrupted);

        // Discovery is cheap and takes priority over any queued scan (HasActiveJobAsync refuses a second job,
        // so this only fires when nothing else is running): a brand-new root, or one a previous discovery never
        // finished walking, gets queued here rather than waiting for a manual "Refresh structure".
        var folderRepository = scope.ServiceProvider.GetRequiredService<IFolderRepository>();
        var discoveryService = scope.ServiceProvider.GetRequiredService<IDiscoveryService>();
        foreach (var root in await scope.ServiceProvider.GetRequiredService<IImageRootRepository>().GetAllAsync())
        {
            if (!root.IsActive || !await folderRepository.HasUndiscoveredFoldersAsync(root.Id))
                continue;

            try
            {
                await discoveryService.QueueDiscoveryAsync(root.Id, null);
            }
            catch (DiscoveryAlreadyInProgressException)
            {
                // Another root's discovery (or a resumed scan) is already queued; the next restart or a manual
                // "Refresh structure" picks this root up.
                break;
            }
        }
    }

    app.UseMiddleware<ImageCacheControlMiddleware>();

    // Every /api endpoint lives in exactly one of these two groups (ApiSurfaceMetadata). v2 auth seam:
    // user.RequireAuthorization(); admin.RequireAuthorization("AdminOnly"); with no route changes.
    var user = app.MapGroup("/api").WithMetadata(new ApiSurfaceMetadata(ApiSurface.User));
    var admin = app.MapGroup("/api").WithMetadata(new ApiSurfaceMetadata(ApiSurface.Admin));

    user.MapImageEndpoints();
    user.MapImageQueryEndpoints();
    user.MapFolderEndpoints(admin);
    user.MapAlbumEndpoints();
    user.MapDuplicateEndpoints();
    admin.MapScanEndpoints();
    admin.MapDiscoveryEndpoints();
    admin.MapRootEndpoints();
    admin.MapSettingsEndpoints();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "PictureManager.Api terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

// Exposes the top-level-statement entry point to WebApplicationFactory<Program> in the tests.
public partial class Program;
