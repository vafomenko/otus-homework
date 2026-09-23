using System.Net;
using System.Net.Http.Json;
using Blt.Users.Api.Data;
using Blt.Users.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Blt.Users.Api.Tests;

public sealed class SecurityBoundaryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public SecurityBoundaryTests(WebApplicationFactory<Program> app) => _client = app.CreateClient();

    public static readonly string[] Prefixes = ["/user", "/users", "/v1/user", "/v1/users",
        "/api/v1/user", "/api/v1/users", "/otusapp/vyacheslav/user", "/otusapp/vyacheslav/v1/user"];

    [Fact]
    public async Task AllAliasesRejectAnonymousReadWriteDeleteAndListWithoutDatabase()
    {
        foreach (var prefix in Prefixes)
        {
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete })
            {
                using var request = new HttpRequestMessage(method, $"{prefix}/42")
                    { Content = JsonContent.Create(new { firstName = "Stolen" }) };
                using var response = await _client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
                Assert.True(response.Headers.CacheControl?.NoStore);
            }
            Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync(prefix)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PutAsJsonAsync("/profile", new { firstName = "Stolen" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsync("/auth/logout", null)).StatusCode);
    }

    [Theory]
    [InlineData("Bearer forged")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer")]
    [InlineData("Bearer a.b.c")]
    public async Task MalformedCredentialsCannotAuthenticate(string header)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/profile");
        request.Headers.TryAddWithoutValidation("Authorization", header);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task UserHeadersCookiesAndQueryParametersAreNotCredentials()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/user/42?userId=42&access_token=forged");
        request.Headers.Add("X-User-Id", "42");
        request.Headers.Add("X-Forwarded-User", "42");
        request.Headers.Add("Cookie", "session=42; userId=42");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task DatabaseFailureDoesNotBypassSessionAuthentication()
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DatabaseOptions>();
                services.AddSingleton(new DatabaseOptions("Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Password=test;Timeout=1"));
            }));
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SessionToken.Create());
        using var response = await client.GetAsync("/profile");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("firstName", body);
    }

    [Fact]
    public async Task PublicCredentialEndpointsHaveABoundedRateAndReturnRetryAfter()
    {
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        for (var attempt = 0; attempt < 30; attempt++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/auth/login", new { })).StatusCode);
        using var blocked = await client.PostAsJsonAsync("/auth/register", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), blocked.Headers.RetryAfter?.Delta);
    }
}
