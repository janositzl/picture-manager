namespace PictureManager.Api.Endpoints;

/// <summary>Which authorization surface an endpoint belongs to; each maps to one route group and policy in Program.cs.</summary>
public enum ApiSurface
{
    User,
    Admin,
    FolderActions,
    Auth
}

/// <summary>Endpoint metadata stamped by the route groups in Program.cs.</summary>
public sealed record ApiSurfaceMetadata(ApiSurface Surface);
