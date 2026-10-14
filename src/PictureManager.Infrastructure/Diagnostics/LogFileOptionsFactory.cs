using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace PictureManager.Infrastructure.Diagnostics;

public static class LogFileOptionsFactory
{
    /// <summary>
    /// Locates the Serilog "File" sink in configuration and derives its directory and file glob
    /// ("logs/picturemanager-.log" with a rolling interval writes "picturemanager-20261014.log").
    /// Relative paths resolve against the working directory, as Serilog does. No File sink yields empty options.
    /// </summary>
    public static LogFileOptions FromConfiguration(IConfiguration configuration)
    {
        var path = configuration.GetSection("Serilog:WriteTo").GetChildren()
            .Where(s => s["Name"] == "File")
            .Select(s => s["Args:path"])
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

        if (path is null)
            return new LogFileOptions(null, null);

        var full = Path.GetFullPath(path);
        var name = Path.GetFileNameWithoutExtension(full);
        var extension = Path.GetExtension(full);
        return new LogFileOptions(Path.GetDirectoryName(full), name + "*" + extension);
    }
}
