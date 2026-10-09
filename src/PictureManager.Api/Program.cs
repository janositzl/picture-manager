using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using PictureManager.Api.Auth;
using PictureManager.Api.Endpoints;
using PictureManager.Api.Middleware;
using PictureManager.Application.DependencyInjection;
using PictureManager.Application.Discovery;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Application.Roots;
using PictureManager.Application.Scanning;
using PictureManager.Application.Thumbnails;
using PictureManager.Application.Users;
using PictureManager.Infrastructure.DependencyInjection;
using PictureManager.Worker.DependencyInjection;
using Scalar.AspNetCore;
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
    var authOptions = new AuthOptions
    {
        InitialAdminPassword = builder.Configuration["Auth:InitialAdmin:Password"],
        LoginAttemptsPerMinute = builder.Configuration.GetValue("Auth:LoginAttemptsPerMinute", 10)
    };
    builder.Services.AddPictureManagerAuth(authOptions);
    // Behind a reverse proxy the real client address and scheme arrive in X-Forwarded-*; without this every client
    // looks like the proxy (one shared login rate-limit bucket) and SameAsRequest cookies never see https.
    // Only loopback proxies are trusted by default; an external proxy needs KnownNetworks/KnownProxies configured.
    builder.Services.Configure<ForwardedHeadersOptions>(o => o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
    builder.Services.AddProblemDetails();
    builder.Services.AddOpenApi();

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

    var faceRecognitionOptions = new FaceRecognitionOptions();
    builder.Configuration.GetSection("FaceRecognition").Bind(faceRecognitionOptions);
    faceRecognitionOptions.ModelDirectory = Path.GetFullPath(faceRecognitionOptions.ModelDirectory, builder.Environment.ContentRootPath);
    builder.Services.AddSingleton(faceRecognitionOptions);

    var connectionString = builder.Configuration.GetConnectionString("PictureManagerDb")
        ?? throw new InvalidOperationException("Connection string 'PictureManagerDb' is not configured.");

    builder.Services.AddHealthChecks()
        .AddNpgSql(connectionString, name: "postgres");

    var app = builder.Build();

    app.UseForwardedHeaders();

    // Outside Development, unhandled exceptions become a 500 ProblemDetails with no stack trace.
    if (!app.Environment.IsDevelopment())
        app.UseExceptionHandler();

    app.MapHealthChecks("/api/health");
    app.MapGet("/api/ping", () => Results.Ok(new { status = "ok" }));

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapOpenApi("/openapi/{documentName}.yaml");
        app.MapScalarApiReference();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "PictureManager.Api v1");
            options.RoutePrefix = "swagger";
        });
    }

    using (var scope = app.Services.CreateScope())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<IImageRootSeeder>();
        await seeder.SeedAsync();
        await scope.ServiceProvider.GetRequiredService<IAdminBootstrapper>().EnsureInitialAdminPasswordAsync();

        // Before the server accepts requests, so it can only ever fail jobs a previous process left behind.
        var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
        var interrupted = await scanService.FailInterruptedJobsAsync();
        if (interrupted > 0)
            Log.Warning("Marked {Count} scan job(s) interrupted by a restart as failed", interrupted);

        // Images left Pending by a scan whose in-memory enrichment queue was lost in that same restart:
        // nothing else ever retries them (a rescan sees the file as unchanged and skips re-enqueuing).
        var requeued = await scanService.RequeueStalledEnrichmentAsync();
        if (requeued > 0)
            Log.Information("Re-queued {Count} image(s) left unenriched by an interrupted scan", requeued);

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

        // Backfill perceptual hashes for images indexed before hashing existed. Runs after discovery so discovery
        // keeps priority for the single "no active job" slot (RequeueAsync does nothing while a job is active);
        // skipped when the catch-up above queued something, and it resumes next start.
        if (requeued == 0)
        {
            var backfilled = await scanService.RequeueMissingPerceptualHashAsync();
            if (backfilled > 0)
                Log.Information("Queued {Count} indexed image(s) for perceptual-hash backfill", backfilled);
        }
    }

    app.UseMiddleware<ImageCacheControlMiddleware>();

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    // Every /api endpoint lives in exactly one of these groups (ApiSurfaceMetadata); each carries its policy,
    // so nothing under /api is reachable anonymously except /api/auth, /api/health and /api/ping.
    var user = app.MapGroup("/api").WithMetadata(new ApiSurfaceMetadata(ApiSurface.User)).RequireAuthorization(AuthPolicies.Active);
    var folderActions = app.MapGroup("/api").WithMetadata(new ApiSurfaceMetadata(ApiSurface.FolderActions)).RequireAuthorization(AuthPolicies.FolderActions);
    var admin = app.MapGroup("/api").WithMetadata(new ApiSurfaceMetadata(ApiSurface.Admin)).RequireAuthorization(AuthPolicies.AdminOnly);
    var auth = app.MapGroup("/api/auth").WithMetadata(new ApiSurfaceMetadata(ApiSurface.Auth));

    auth.MapAuthEndpoints();

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

    // The built web frontend, when present (the Docker image copies web/dist into
    // wwwroot). Not present in local dev/test, where the frontend runs via its own
    // Vite dev server instead — WebRootPath is null then, so this is a no-op.
    if (!string.IsNullOrEmpty(app.Environment.WebRootPath))
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");
    }

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
