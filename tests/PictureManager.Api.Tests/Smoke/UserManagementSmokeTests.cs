using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace PictureManager.Api.Tests.Smoke;

public partial class ApiSmokeTests
{
    private static object NewUserBody(string username, string password = "long-enough-pw") =>
        new { username, displayName = username + " D", password, role = "User", canRunFolderActions = false };

    private async Task<int> UserIdAsync(string username)
    {
        var users = await _fixture.Client.GetFromJsonAsync<JsonElement>("/api/users");
        return users.EnumerateArray().Single(u => u.GetProperty("username").GetString() == username).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Admin_CreatesUser_WhoMustChangeThePasswordAtFirstLogin()
    {
        var created = await _fixture.Client.PostAsJsonAsync("/api/users", NewUserBody("created-user"));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("role").GetString().Should().Be("User");
        body.GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();

        var client = await _fixture.LoginAsync("created-user", "long-enough-pw");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        me.GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("dup-user-a", "dup-user-a")]
    [InlineData("dup-user-b", "  DUP-USER-B ")]
    public async Task Admin_CreatingADuplicateUsername_IgnoringCaseAndWhitespace_Is409(string first, string second)
    {
        (await _fixture.Client.PostAsJsonAsync("/api/users", NewUserBody(first))).EnsureSuccessStatusCode();

        var response = await _fixture.Client.PostAsJsonAsync("/api/users", NewUserBody(second));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Admin_CreatingAUserWithAShortPassword_Is400()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/users", NewUserBody("short-pw-user", "short"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("password");
    }

    [Fact]
    public async Task Admin_DisablingAUser_EndsTheirOpenSessionImmediately()
    {
        var client = await _fixture.CreateUserClientAsync("to-disable");
        (await client.GetAsync("/api/users/directory")).StatusCode.Should().Be(HttpStatusCode.OK);
        var id = await UserIdAsync("to-disable");

        var patched = await _fixture.Client.PatchAsJsonAsync($"/api/users/{id}", new { isActive = false });

        patched.StatusCode.Should().Be(HttpStatusCode.OK);
        (await patched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeFalse();
        (await client.GetAsync("/api/users/directory")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_ResettingAPassword_EndsTheirSession_AndForcesAChangeAtNextLogin()
    {
        var client = await _fixture.CreateUserClientAsync("to-reset");
        var id = await UserIdAsync("to-reset");

        var reset = await _fixture.Client.PostAsJsonAsync($"/api/users/{id}/reset-password", new { newPassword = "reset-by-admin" });

        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/users/directory")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var fresh = await _fixture.LoginAsync("to-reset", "reset-by-admin");
        (await fresh.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Admin_CannotDisableDemoteOrDeleteThemselves()
    {
        var me = await _fixture.Client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var id = me.GetProperty("id").GetInt32();

        (await _fixture.Client.PatchAsJsonAsync($"/api/users/{id}", new { isActive = false })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _fixture.Client.PatchAsJsonAsync($"/api/users/{id}", new { role = "User" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _fixture.Client.DeleteAsync($"/api/users/{id}?albums=transfer")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _fixture.Client.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_DeletingAUserWithTransfer_MovesTheirAlbumsToTheAdmin()
    {
        var client = await _fixture.CreateUserClientAsync("to-transfer");
        (await client.PostAsJsonAsync("/api/albums", new { name = "Transferred trip" })).EnsureSuccessStatusCode();
        var id = await UserIdAsync("to-transfer");

        var deleted = await _fixture.Client.DeleteAsync($"/api/users/{id}?albums=transfer");

        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var albums = await _fixture.Client.GetFromJsonAsync<JsonElement>("/api/albums");
        albums.EnumerateArray().Select(a => a.GetProperty("name").GetString()).Should().Contain("Transferred trip");
        (await client.GetAsync("/api/albums")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_DeletingAUserWithDeleteAlbums_RemovesTheirAlbums()
    {
        var client = await _fixture.CreateUserClientAsync("to-purge");
        (await client.PostAsJsonAsync("/api/albums", new { name = "Purged trip" })).EnsureSuccessStatusCode();
        var id = await UserIdAsync("to-purge");

        (await _fixture.Client.DeleteAsync($"/api/users/{id}?albums=delete")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var albums = await _fixture.Client.GetFromJsonAsync<JsonElement>("/api/albums");
        albums.EnumerateArray().Select(a => a.GetProperty("name").GetString()).Should().NotContain("Purged trip");
    }

    [Theory]
    [InlineData("")]
    [InlineData("?albums=nope")]
    public async Task Admin_DeletingWithoutAValidAlbumsChoice_Is400_AndDeletesNothing(string query)
    {
        await _fixture.CreateUserClientAsync("keep-me" + query.Length);
        var id = await UserIdAsync("keep-me" + query.Length);

        var response = await _fixture.Client.DeleteAsync($"/api/users/{id}{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await UserIdAsync("keep-me" + query.Length)).Should().Be(id);
    }

    [Fact]
    public async Task Admin_UpdatingOrDeletingAnUnknownUser_Is404()
    {
        (await _fixture.Client.PatchAsJsonAsync("/api/users/999999", new { displayName = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _fixture.Client.DeleteAsync("/api/users/999999?albums=transfer")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Directory_ListsActiveUsersExceptTheCaller_ForAPlainUser()
    {
        var client = await _fixture.CreateUserClientAsync("directory-reader");
        await _fixture.CreateUserClientAsync("directory-other");
        var offId = await UserIdAsync("directory-other");
        (await _fixture.Client.PostAsJsonAsync("/api/users", NewUserBody("directory-off"))).EnsureSuccessStatusCode();
        await _fixture.Client.PatchAsJsonAsync($"/api/users/{await UserIdAsync("directory-off")}", new { isActive = false });

        var entries = await client.GetFromJsonAsync<JsonElement>("/api/users/directory");
        var names = entries.EnumerateArray().Select(e => e.GetProperty("displayName").GetString()).ToList();

        names.Should().Contain("directory-other").And.NotContain("directory-reader").And.NotContain("directory-off D");
        entries.EnumerateArray().First().TryGetProperty("username", out _).Should().BeFalse("the directory exposes only id and display name");
        offId.Should().BeGreaterThan(0);
    }
}
