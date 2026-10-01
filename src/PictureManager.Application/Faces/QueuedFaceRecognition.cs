namespace PictureManager.Application.Faces;

/// <summary>A face job whose row exists (Enumerating). FolderId null = every active root.</summary>
public sealed record QueuedFaceRecognition(int JobId, int? FolderId, bool IsRecursive);
