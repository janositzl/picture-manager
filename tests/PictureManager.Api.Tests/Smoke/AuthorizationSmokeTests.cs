using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PictureManager.Api.Endpoints;
using Xunit;

namespace PictureManager.Api.Tests.Smoke;

public partial class ApiSmokeTests
{
    /// <summary>(method, concrete path) for every endpoint on the given surfaces. Route values become 999999 (never a real row).</summary>
    private (string Method, string Path)[] Requests(params ApiSurface[] surfaces) =>
        ApiEndpoints()
            .Where(e => surfaces.Contains(e.Metadata.GetMetadata<ApiSurfaceMetadata>()!.Surface))
            .SelectMany(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods
                .Select(m => (m, Regex.Replace(e.RoutePattern.RawText!, @"\{[^}]+\}", "999999"))))
            .ToArray();

    private static HttpRequestMessage Request(string method, string path) =>
        new(new HttpMethod(method), path) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Anonymous_IsRejectedWith401_OnEveryNonAuthEndpoint()
    {
        using var anonymous = _fixture.Factory.CreateClient();
        foreach (var (method, path) in Requests(ApiSurface.User, ApiSurface.Admin, ApiSurface.FolderActions))
            (await anonymous.SendAsync(Request(method, path))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{method} {path}");
    }

    [Fact]
    public async Task PlainUser_IsForbidden_OnAdminAndFolderActionEndpoints()
    {
        var client = await _fixture.CreateUserClientAsync("plain-user");
        foreach (var (method, path) in Requests(ApiSurface.Admin, ApiSurface.FolderActions))
            (await client.SendAsync(Request(method, path))).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {path}");
    }

    [Fact]
    public async Task MustChangePasswordUser_IsForbidden_OnEveryNonAuthEndpoint()
    {
        var client = await _fixture.CreateUserClientAsync("mcp-sweep", mustChangePassword: true);
        foreach (var (method, path) in Requests(ApiSurface.User, ApiSurface.Admin, ApiSurface.FolderActions))
            (await client.SendAsync(Request(method, path))).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{method} {path}");
    }

    [Fact]
    public async Task PlainUser_CanReadJobStatus_AndFaceCoverage()
    {
        var client = await _fixture.CreateUserClientAsync("status-reader");
        (await client.GetAsync("/api/jobs/active")).StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        (await client.GetAsync("/api/face-recognitions/coverage")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UserWithFolderActions_PassesThePolicy()
    {
        var client = await _fixture.CreateUserClientAsync("folder-operator", canRunFolderActions: true);
        // A job that doesn't exist: the handler answers 404, so authorization let the call through. Side-effect free.
        (await client.PostAsync("/api/jobs/999999/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PutAsync("/api/images/hidden", new StringContent("{}", Encoding.UTF8, "application/json")))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
