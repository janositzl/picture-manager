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
}
finally
{
    Log.CloseAndFlush();
}
