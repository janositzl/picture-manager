using System.IO;

namespace PictureManager.Application.Scanning;

public static class ImagePathResolver
{
    public static string ResolvePhysicalPath(string mountPath, string relativeFolderPath, string fileName, string extension)
    {
        var fullFileName = fileName + extension;
        return string.IsNullOrEmpty(relativeFolderPath)
            ? Path.Combine(mountPath, fullFileName)
            : Path.Combine(mountPath, relativeFolderPath.Replace('/', Path.DirectorySeparatorChar), fullFileName);
    }
}
