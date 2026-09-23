using System.Collections.Concurrent;
using Prometheus;

namespace Blt.Users.Api.Observability;

public sealed record ApiMetricRoute(string Template);

public sealed class ApiMetrics
{
    private readonly Counter _requests;
    private readonly Histogram _duration;
    private readonly Gauge _maximum;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<(string Method, string Route), RollingMaximum> _windows = new();

    public CollectorRegistry Registry { get; } = Metrics.NewCustomRegistry();

    public ApiMetrics(TimeProvider time)
    {
        _time = time;
        var factory = Metrics.WithCustomRegistry(Registry);
        _requests = factory.CreateCounter("blt_http_requests_total", "Completed API requests.",
            new CounterConfiguration { LabelNames = ["method", "route", "status_code"] });
        _duration = factory.CreateHistogram("blt_http_request_duration_seconds", "API response time in seconds.",
            new HistogramConfiguration
            {
                LabelNames = ["method", "route"],
                Buckets = [.001, .0025, .005, .01, .025, .05, .1, .2, .3, .5, .75, 1, 2, 3, 5, 10, 20, 30]
            });
        _maximum = factory.CreateGauge("blt_http_request_duration_max_seconds",
            "Exact maximum completed API response time over the preceding 60 seconds; zero when idle.",
            new GaugeConfiguration { LabelNames = ["method", "route"] });

        foreach (var (method, route) in new[]
        {
            ("POST", "/auth/register"), ("POST", "/auth/login"), ("POST", "/auth/logout"),
            ("GET", "/profile"), ("PUT", "/profile"),
            ("GET", "/user"), ("GET", "/user/{id}"),
            ("PUT", "/user/{id}"), ("DELETE", "/user/{id}")
        })
        {
            _windows[(method, route)] = new RollingMaximum(time, TimeSpan.FromMinutes(1));
            _duration.WithLabels(method, route);
            // An unused 500 series must be zero, not missing from the dashboard.
            _requests.WithLabels(method, route, "500");
            _maximum.WithLabels(method, route).Set(0);
        }

        Registry.AddBeforeCollectCallback(() =>
        {
            foreach (var (labels, window) in _windows)
                _maximum.WithLabels(labels.Method, labels.Route).Set(window.Value);
        });
    }

    public void Observe(string method, string route, int status, double seconds)
    {
        _requests.WithLabels(method, route, status.ToString(System.Globalization.CultureInfo.InvariantCulture)).Inc();
        _duration.WithLabels(method, route).Observe(seconds);
        _windows.GetOrAdd((method, route), _ => new RollingMaximum(_time, TimeSpan.FromMinutes(1))).Observe(seconds);
    }
}

// Monotonic deque: retain only candidates for the next maximum, expiring by
// monotonic clock. A scrape after an idle minute cannot report an old peak.
public sealed class RollingMaximum(TimeProvider time, TimeSpan window)
{
    private readonly object _gate = new();
    private readonly LinkedList<(long Timestamp, double Value)> _samples = new();

    public void Observe(double value)
    {
        lock (_gate)
        {
            var now = time.GetTimestamp();
            Expire(now);
            while (_samples.Last is { } last && last.Value.Value <= value)
                _samples.RemoveLast();
            _samples.AddLast((now, value));
        }
    }

    public double Value
    {
        get
        {
            lock (_gate)
            {
                Expire(time.GetTimestamp());
                return _samples.First?.Value.Value ?? 0;
            }
        }
    }

    private void Expire(long now)
    {
        while (_samples.First is { } first && time.GetElapsedTime(first.Value.Timestamp, now) >= window)
            _samples.RemoveFirst();
    }
}
