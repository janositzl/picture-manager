using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace PictureManager.Api.Tests.Smoke;

// The fixture runs the host with PathBase=/pm, as a sub-path deployment behind a reverse proxy would.
public partial class ApiSmokeTests
{
    [Theory]
    [InlineData("/pm/api/ping")]
    // Direct access to the published port, without the proxy's prefix, keeps working.
    [InlineData("/api/ping")]
    public async Task Ping_AnsweredWithAndWithoutPathBase(string path)
    {
        var response = await _fixture.Client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuthenticatedEndpoint_RoutesUnderPathBase()
    {
        var response = await _fixture.Client.GetAsync("/pm/api/roots");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
