namespace PictureManager.Application.Roots;

public sealed record RootSummary(int Id, string Name, string? Alias, string MountPath, bool IsActive, string ExportSegment);

/// <summary>PATCH input. Name/IsActive null = unchanged; AliasSpecified distinguishes "clear" (null) from "unchanged".</summary>
public sealed record RootUpdate(string? Name, bool AliasSpecified, string? Alias, bool? IsActive);
