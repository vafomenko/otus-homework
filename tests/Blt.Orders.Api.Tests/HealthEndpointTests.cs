using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Blt.Orders.Api.Tests;

public sealed class HealthEndpointTests(
    WebApplicationFactory<Program> application)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = application.CreateClient();

    [Fact]
    public async Task GetHealthReturnsOkWithRequiredPayload()
    {
        using var response = await _client.GetAsync("/health/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            """{"status":"OK"}""",
            await response.Content.ReadAsStringAsync());
    }
}
