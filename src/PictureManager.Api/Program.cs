using Microsoft.Extensions.Configuration;
using PictureManager.Api.Endpoints;
using PictureManager.Api.Middleware;
using PictureManager.Application.DependencyInjection;
using PictureManager.Application.Repositories;
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

    var devImageRootOptions = new DevImageRootOptions();
    builder.Configuration.GetSection("DevImageRoot").Bind(devImageRootOptions);
    builder.Services.AddSingleton(devImageRootOptions);

    var thumbnailCacheOptions = new ThumbnailCacheOptions();
    builder.Configuration.GetSection("ThumbnailCache").Bind(thumbnailCacheOptions);
    builder.Services.AddSingleton(thumbnailCacheOptions);

    var connectionString = builder.Configuration.GetConnectionString("PictureManagerDb")
        ?? throw new InvalidOperationException("Connection string 'PictureManagerDb' is not configured.");

    builder.Services.AddHealthChecks()
        .AddNpgSql(connectionString, name: "postgres");

    var app = builder.Build();

    app.MapHealthChecks("/api/health");
    app.MapGet("/api/ping", () => Results.Ok(new { status = "ok" }));

    using (var scope = app.Services.CreateScope())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<IDevImageRootSeeder>();
        await seeder.SeedAsync();
    }

    app.MapScanEndpoints();

    app.UseMiddleware<ImageCacheControlMiddleware>();
    app.MapImageEndpoints();

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
