using System.Net;
using System.Text;
using Blt.Users.Api.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Blt.Users.Api.Tests;

public sealed class ObservabilityTests
{
    [Fact]
    public async Task MetricsCountReal500AndCollapseIdsAndAliases()
    {
        await using var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Observability:EnableFaultInjection", "true"));
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Add("X-Demo-Error", "500");
        foreach (var path in new[] { "/user/123", "/v1/users/999" })
            Assert.Equal(HttpStatusCode.InternalServerError, (await client.GetAsync(path)).StatusCode);

        var metrics = await client.GetStringAsync("/metrics");
        Assert.Contains("blt_http_requests_total{method=\"GET\",route=\"/user/{id}\",status_code=\"500\"} 2", metrics);
        Assert.Contains("blt_http_request_duration_seconds_count{method=\"GET\",route=\"/user/{id}\"} 2", metrics);
        Assert.DoesNotContain("/user/123", metrics);
        Assert.DoesNotContain("/v1/users/999", metrics);
        Assert.DoesNotContain("route=\"/metrics\"", metrics);
        Assert.DoesNotContain("route=\"/health", metrics);
    }

    [Fact]
    public async Task FaultInjectionIsOffByDefaultAndValidation400IsNot500()
    {
        await using var application = new WebApplicationFactory<Program>();
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.Add("X-Demo-Error", "500");
        using var body = new StringContent("{}", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/auth/register", body)).StatusCode);
        var metrics = await client.GetStringAsync("/metrics");
        Assert.Contains("blt_http_requests_total{method=\"POST\",route=\"/auth/register\",status_code=\"400\"} 1", metrics);
        Assert.Contains("blt_http_requests_total{method=\"POST\",route=\"/auth/register\",status_code=\"500\"} 0", metrics);
    }

    [Fact]
    public void SlidingMaximumExpiresOldPeakAndBecomesZeroWhenIdle()
    {
        var time = new ManualTime();
        var maximum = new RollingMaximum(time, TimeSpan.FromMinutes(1));
        maximum.Observe(0.8);
        time.Advance(30);
        maximum.Observe(0.2);
        Assert.Equal(0.8, maximum.Value);
        time.Advance(30);
        Assert.Equal(0.2, maximum.Value);
        time.Advance(30);
        Assert.Equal(0, maximum.Value);
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(int seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
