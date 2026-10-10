using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Api.Endpoints;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Api.Tests.Smoke;

public partial class ApiSmokeTests : IClassFixture<ApiSmokeFixture>
{
    private readonly ApiSmokeFixture _fixture;

    public ApiSmokeTests(ApiSmokeFixture fixture)
    {
        _fixture = fixture;
    }

    private RouteEndpoint[] ApiEndpoints() =>
        _fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api/")
                        && e.RoutePattern.RawText is not ("/api/health" or "/api/ping"))
            .ToArray();

    [Fact]
    public void EveryApiEndpoint_BelongsToExactlyOneSurface()
    {
        var endpoints = ApiEndpoints();

        endpoints.Should().NotBeEmpty();
        foreach (var endpoint in endpoints)
            endpoint.Metadata.GetOrderedMetadata<ApiSurfaceMetadata>().Should().HaveCount(1, endpoint.DisplayName);
    }

    [Theory]
    [InlineData("GET", "/api/roots", ApiSurface.Admin)]
    [InlineData("POST", "/api/roots", ApiSurface.Admin)]
    [InlineData("PATCH", "/api/roots/{id:int}", ApiSurface.Admin)]
    [InlineData("DELETE", "/api/roots/{id:int}", ApiSurface.Admin)]
    [InlineData("GET", "/api/settings", ApiSurface.Admin)]
    [InlineData("PUT", "/api/settings", ApiSurface.Admin)]
    [InlineData("POST", "/api/scans", ApiSurface.FolderActions)]
    [InlineData("DELETE", "/api/folders/{id:int}", ApiSurface.FolderActions)]
    [InlineData("GET", "/api/folders/removed", ApiSurface.Admin)]
    [InlineData("POST", "/api/folders/{id:int}/restore", ApiSurface.Admin)]
    [InlineData("GET", "/api/images", ApiSurface.User)]
    [InlineData("PUT", "/api/images/{id:int}/favorite", ApiSurface.User)]
    [InlineData("GET", "/api/images/{id:int}/thumbnail", ApiSurface.User)]
    [InlineData("GET", "/api/albums", ApiSurface.User)]
    [InlineData("GET", "/api/albums/{id:int}/export", ApiSurface.User)]
    [InlineData("GET", "/api/albums/{id:int}/shares", ApiSurface.User)]
    [InlineData("PUT", "/api/albums/{id:int}/shares/{userId:int}", ApiSurface.User)]
    [InlineData("DELETE", "/api/albums/{id:int}/shares/{userId:int}", ApiSurface.User)]
    [InlineData("GET", "/api/duplicates", ApiSurface.User)]
    [InlineData("GET", "/api/duplicates/similar", ApiSurface.User)]
    [InlineData("GET", "/api/folders/roots", ApiSurface.User)]
    [InlineData("POST", "/api/discoveries", ApiSurface.FolderActions)]
    [InlineData("POST", "/api/face-recognitions", ApiSurface.FolderActions)]
    [InlineData("POST", "/api/jobs/{id:int}/cancel", ApiSurface.FolderActions)]
    [InlineData("PUT", "/api/folders/{id:int}/exclusion", ApiSurface.FolderActions)]
    [InlineData("GET", "/api/jobs/active", ApiSurface.User)]
    [InlineData("GET", "/api/scans/{id:int}/events", ApiSurface.User)]
    [InlineData("GET", "/api/face-recognitions/coverage", ApiSurface.User)]
    [InlineData("GET", "/api/face-recognitions/failures", ApiSurface.Admin)]
    [InlineData("PUT", "/api/images/hidden", ApiSurface.Admin)]
    [InlineData("PUT", "/api/images/thumbnail-rotation", ApiSurface.Admin)]
    [InlineData("PATCH", "/api/people/{id:int}", ApiSurface.Admin)]
    [InlineData("POST", "/api/faces/{id:int}/accept", ApiSurface.Admin)]
    [InlineData("GET", "/api/people", ApiSurface.User)]
    [InlineData("GET", "/api/images/{id:int}/faces", ApiSurface.User)]
    [InlineData("POST", "/api/auth/login", ApiSurface.Auth)]
    public void Endpoint_IsOnTheExpectedSurface(string method, string route, ApiSurface expected)
    {
        var endpoint = ApiEndpoints().Single(e =>
            e.RoutePattern.RawText == route
            && e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Contains(method));

        endpoint.Metadata.GetMetadata<ApiSurfaceMetadata>()!.Surface.Should().Be(expected);
    }

    [Fact]
    public async Task BrowseListFavoriteAndAlbum_RoundTripOverHttp()
    {
        int tripFolderId;
        int[] imageIds;
        await using (var context = _fixture.Database.CreateContext())
        {
            var root = TestData.Root("smoke-browse");
            var top = TestData.Folder(root, "");
            var trip = TestData.Folder(root, "Trip", top);
            var images = new[] { "a", "b", "c" }.Select(n => TestData.Image(trip, n)).ToArray();
            context.Images.AddRange(images);
            await context.SaveChangesAsync();
            tripFolderId = trip.Id;
            imageIds = images.Select(i => i.Id).ToArray();
        }

        var client = _fixture.Client;

        var roots = await client.GetFromJsonAsync<JsonElement>("/api/folders/roots");
        var smokeRoot = roots.EnumerateArray().Single(n => n.GetProperty("name").GetString() == "smoke-browse");
        var children = await client.GetFromJsonAsync<JsonElement>($"/api/folders/{smokeRoot.GetProperty("id").GetInt32()}/children");
        children[0].GetProperty("name").GetString().Should().Be("Trip");
        (await client.GetFromJsonAsync<JsonElement>($"/api/folders/{tripFolderId}")).GetProperty("imageCount").GetInt32().Should().Be(3);

        var firstPage = await client.GetFromJsonAsync<JsonElement>($"/api/images?folderId={tripFolderId}&sort=name&limit=2");
        firstPage.GetProperty("items").GetArrayLength().Should().Be(2);
        var cursor = firstPage.GetProperty("nextCursor").GetString();
        var secondPage = await client.GetFromJsonAsync<JsonElement>($"/api/images?folderId={tripFolderId}&sort=name&limit=2&cursor={cursor}");
        secondPage.GetProperty("items").GetArrayLength().Should().Be(1);
        secondPage.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);

        (await client.PutAsync($"/api/images/{imageIds[0]}/favorite", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var favorites = await client.GetFromJsonAsync<JsonElement>($"/api/images?folderId={tripFolderId}&favoritesOnly=true");
        favorites.GetProperty("items").GetArrayLength().Should().Be(1);

        var created = await client.PostAsJsonAsync("/api/albums", new { name = "Smoke album" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var albumId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var added = await client.PostAsJsonAsync($"/api/albums/{albumId}/images", new { folderId = tripFolderId });
        (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("added").GetInt32().Should().Be(3);

        var export = await client.GetStringAsync($"/api/albums/{albumId}/export?prefix=/mnt");
        export.Should().Be("/mnt/smoke-browse/Trip/a.jpg\n/mnt/smoke-browse/Trip/b.jpg\n/mnt/smoke-browse/Trip/c.jpg\n");
    }

    [Fact]
    public async Task SimilarDuplicates_EmptyList_Returns200_AndBadThreshold_Returns400()
    {
        var ok = await _fixture.Client.GetFromJsonAsync<JsonElement>("/api/duplicates/similar");
        ok.GetProperty("items").GetArrayLength().Should().Be(0);

        (await _fixture.Client.GetAsync("/api/duplicates/similar?threshold=99")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Export_NonAsciiAlbumName_SetsRfc5987FileName()
    {
        var client = _fixture.Client;
        var created = await client.PostAsJsonAsync("/api/albums", new { name = "Nyaralás 2025" });
        var albumId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var response = await client.GetAsync($"/api/albums/{albumId}/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.ToString().Should().Be("text/plain; charset=utf-8");
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("Nyaralás 2025.txt");
    }

    [Fact]
    public async Task BadCursor_Returns400ProblemJson_AndUnknownImage_Returns404()
    {
        var bad = await _fixture.Client.GetAsync("/api/images?cursor=not-a-cursor");
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        bad.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        (await _fixture.Client.GetAsync("/api/images/999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DuplicateAlbumName_Returns409()
    {
        await _fixture.Client.PostAsJsonAsync("/api/albums", new { name = "Twice" });

        var second = await _fixture.Client.PostAsJsonAsync("/api/albums", new { name = "TWICE" });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
