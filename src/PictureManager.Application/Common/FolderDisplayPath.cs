namespace PictureManager.Application.Common;

public static class FolderDisplayPath
{
    public static string For(string rootName, string relativePath) =>
        string.IsNullOrEmpty(relativePath) ? rootName : $"{rootName}/{relativePath}";
}
