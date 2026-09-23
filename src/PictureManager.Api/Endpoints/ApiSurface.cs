namespace PictureManager.Api.Endpoints;

/// <summary>Which authorization surface an endpoint belongs to (the v2 auth seam).</summary>
public enum ApiSurface
{
    User,
    Admin
}

/// <summary>Endpoint metadata stamped by the user/admin route groups in Program.cs.</summary>
public sealed record ApiSurfaceMetadata(ApiSurface Surface);
