using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Api.Auth;
using Xunit;

namespace PictureManager.Api.Tests.Smoke;

public partial class ApiSmokeTests
{
    [Fact]
    public async Task Me_SignedInAdmin_ReturnsProfile()
    {
        var me = await _fixture.Client.GetFromJsonAsync<JsonElement>("/api/auth/me");

        me.GetProperty("username").GetString().Should().Be("admin");
        me.GetProperty("role").GetString().Should().Be("Admin");
        me.GetProperty("mustChangePassword").GetBoolean().Should().BeFalse();
        me.GetProperty("canRunFolderActions").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Me_Anonymous_Returns401()
    {
        using var anonymous = _fixture.Factory.CreateClient();
        (await anonymous.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401WithGenericTitle()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "nope-nope" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString()
            .Should().Be("Invalid username or password.");
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        var client = await _fixture.CreateUserClientAsync("logout-user");
        (await client.GetAsync("/api/albums")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsync("/api/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetAsync("/api/albums")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DisabledUser_OpenSession_IsRejectedOnceTheCacheIsEvicted()
    {
        var client = await _fixture.CreateUserClientAsync("soon-disabled");
        await using (var context = _fixture.Database.CreateContext())
        {
            var user = await context.AppUsers.SingleAsync(u => u.NormalizedUsername == "soon-disabled");
            user.IsActive = false;
            await context.SaveChangesAsync();
            _fixture.Factory.Services.GetRequiredService<UserSessionCache>().Evict(user.Id);
        }

        (await client.GetAsync("/api/albums")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MustChangePassword_OnlyAuthEndpointsWork_UntilChanged()
    {
        var client = await _fixture.CreateUserClientAsync("pending-change", mustChangePassword: true);

        (await client.GetAsync("/api/albums")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();

        var changed = await client.PostAsJsonAsync("/api/auth/password",
            new { currentPassword = ApiSmokeFixture.UserPassword, newPassword = "a-fresh-password" });
        changed.StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.GetAsync("/api/albums")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
