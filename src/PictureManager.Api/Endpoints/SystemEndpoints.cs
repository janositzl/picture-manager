using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using PictureManager.Application.Diagnostics;

namespace PictureManager.Api.Endpoints;

public static class SystemEndpoints
{
    private const int DefaultTake = 500;
    private const int MaxTake = 2000;

    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapGet("/system/info", GetInfo);
        admin.MapGet("/system/logs", GetLogsAsync);
        return admin;
    }

    public static Ok<AppVersionInfo> GetInfo(IHostEnvironment environment)
    {
        var assembly = typeof(Program).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        // A SemVer "+metadata" suffix is not part of the version the operator sets.
        var plus = version.IndexOf('+');
        if (plus >= 0)
            version = version[..plus];

        // The publish step stamps the assembly; in Docker the copied file keeps that time.
        var buildTime = new DateTimeOffset(File.GetLastWriteTimeUtc(assembly.Location), TimeSpan.Zero);
        var started = new DateTimeOffset(Process.GetCurrentProcess().StartTime.ToUniversalTime(), TimeSpan.Zero);

        return TypedResults.Ok(new AppVersionInfo(
            version, buildTime, started, environment.EnvironmentName, RuntimeInformation.FrameworkDescription));
    }

    public static async Task<Results<Ok<LogTail>, ValidationProblem>> GetLogsAsync(
        ILogReader reader, CancellationToken cancellationToken, int? take = null, string? minLevel = null)
    {
        var level = LogSeverity.Verbose;
        if (!string.IsNullOrEmpty(minLevel) && !Enum.TryParse(minLevel, ignoreCase: true, out level))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["minLevel"] = [$"Must be one of: {string.Join(", ", Enum.GetNames<LogSeverity>())}."]
            });

        var count = Math.Clamp(take ?? DefaultTake, 1, MaxTake);
        return TypedResults.Ok(await reader.ReadTailAsync(count, level, cancellationToken));
    }
}
