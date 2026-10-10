using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Api.Tests.Smoke;

public partial class ApiSmokeTests
{
    private async Task<int> CreateAlbumAsync(HttpClient client, string name)
    {
        var created = await client.PostAsJsonAsync("/api/albums", new { name });
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Sharing_ViewerReads_EditorChanges_StrangerSeesNothing_AndASharerCanLeave()
    {
        var bob = await _fixture.CreateUserClientAsync("share-bob");
        var stranger = await _fixture.CreateUserClientAsync("share-stranger");
        var bobId = await UserIdAsync("share-bob");
        var albumId = await CreateAlbumAsync(_fixture.Client, "Shared smoke");

        var shared = await _fixture.Client.PutAsJsonAsync($"/api/albums/{albumId}/shares/{bobId}", new { permission = "Viewer" });
        shared.StatusCode.Should().Be(HttpStatusCode.OK);
        (await shared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("permission").GetString().Should().Be("Viewer");

        var listed = (await bob.GetFromJsonAsync<JsonElement>("/api/albums")).EnumerateArray()
            .Single(a => a.GetProperty("id").GetInt32() == albumId);
        listed.GetProperty("access").GetString().Should().Be("Viewer");
        listed.GetProperty("ownerDisplayName").GetString().Should().Be("Administrator");
        (await bob.GetAsync($"/api/albums/{albumId}/images")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await bob.GetAsync($"/api/albums/{albumId}/export")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await bob.PostAsJsonAsync($"/api/albums/{albumId}/sort", new { by = "name" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await bob.PatchAsJsonAsync($"/api/albums/{albumId}", new { name = "Mine now" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await bob.GetAsync($"/api/albums/{albumId}/shares")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.GetAsync($"/api/albums/{albumId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await _fixture.Client.PutAsJsonAsync($"/api/albums/{albumId}/shares/{bobId}", new { permission = "Editor" })).EnsureSuccessStatusCode();
        (await bob.PostAsJsonAsync($"/api/albums/{albumId}/sort", new { by = "name" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await bob.DeleteAsync($"/api/albums/{albumId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var shares = await _fixture.Client.GetFromJsonAsync<JsonElement>($"/api/albums/{albumId}/shares");
        shares.EnumerateArray().Select(s => s.GetProperty("displayName").GetString()).Should().Equal("share-bob");

        (await bob.DeleteAsync($"/api/albums/{albumId}/shares/{bobId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await bob.GetAsync($"/api/albums/{albumId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sharing_WithYourself_AnInactiveOrUnknownUser_OrABadPermission_Is400()
    {
        var me = (await _fixture.Client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetInt32();
        await _fixture.CreateUserClientAsync("share-off");
        var offId = await UserIdAsync("share-off");
        await _fixture.Client.PatchAsJsonAsync($"/api/users/{offId}", new { isActive = false });
        await _fixture.CreateUserClientAsync("share-target");
        var targetId = await UserIdAsync("share-target");
        var albumId = await CreateAlbumAsync(_fixture.Client, "Share validation");

        foreach (var (userId, permission) in new[] { (me, "Viewer"), (offId, "Viewer"), (999_999, "Viewer"), (targetId, "1"), (targetId, "Owner") })
            (await _fixture.Client.PutAsJsonAsync($"/api/albums/{albumId}/shares/{userId}", new { permission }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest, $"user {userId}, permission {permission}");

        (await _fixture.Client.GetFromJsonAsync<JsonElement>($"/api/albums/{albumId}/shares")).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Favorites_ArePerUser()
    {
        int imageId;
        await using (var context = _fixture.Database.CreateContext())
        {
            var image = TestData.Image(TestData.Folder(TestData.Root("smoke-favorites"), ""), "fav");
            context.Images.Add(image);
            await context.SaveChangesAsync();
            imageId = image.Id;
        }
        var bob = await _fixture.CreateUserClientAsync("fav-bob");

        (await _fixture.Client.PutAsync($"/api/images/{imageId}/favorite", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _fixture.Client.GetFromJsonAsync<JsonElement>($"/api/images/{imageId}")).GetProperty("isFavorite").GetBoolean().Should().BeTrue();
        (await bob.GetFromJsonAsync<JsonElement>($"/api/images/{imageId}")).GetProperty("isFavorite").GetBoolean().Should().BeFalse();
        (await bob.GetFromJsonAsync<JsonElement>("/api/images?favoritesOnly=true")).GetProperty("items").GetArrayLength().Should().Be(0);
    }
}
