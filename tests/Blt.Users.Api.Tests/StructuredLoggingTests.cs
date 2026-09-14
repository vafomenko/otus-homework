using System.Text.Json;
using Blt.Users.Api.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Blt.Users.Api.Tests;

public sealed class StructuredLoggingTests
{
    [Fact]
    public void FormatterProducesOneJsonLineWithTypedFieldsAndRequestScope()
    {
        var scopes = new LoggerExternalScopeProvider();
        using var scope = scopes.Push(new Dictionary<string, object?> { ["request_id"] = "req-42" });
        var state = new Dictionary<string, object?>
        {
            ["event"] = "user_created", ["user_id"] = 42L,
            ["{OriginalFormat}"] = "internal template"
        };
        var entry = new LogEntry<Dictionary<string, object?>>(LogLevel.Information, "Users", default,
            state, null, (_, _) => "created\nuser");
        using var writer = new StringWriter();
        new StructuredLogFormatter().Write(entry, scopes, writer);
        var output = writer.ToString();
        Assert.Single(output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.Equal("INFO", root.GetProperty("level").GetString());
        Assert.Equal("user-service", root.GetProperty("service").GetString());
        Assert.Equal(42, root.GetProperty("user_id").GetInt64());
        Assert.Equal("req-42", root.GetProperty("request_id").GetString());
        Assert.True(root.GetProperty("@timestamp").TryGetDateTimeOffset(out _));
        Assert.False(root.TryGetProperty("{OriginalFormat}", out _));
    }

    [Theory]
    [InlineData(LogLevel.Warning, "WARN")]
    [InlineData(LogLevel.Error, "ERROR")]
    public void ErrorFieldsExcludeExceptionMessage(LogLevel level, string expected)
    {
        var entry = new LogEntry<string>(level, "Database", default, "Failed",
            new InvalidOperationException("password=secret"), (state, _) => state);
        using var writer = new StringWriter();
        new StructuredLogFormatter().Write(entry, null, writer);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(expected, document.RootElement.GetProperty("level").GetString());
        Assert.Equal(typeof(InvalidOperationException).FullName,
            document.RootElement.GetProperty("exception_type").GetString());
        Assert.DoesNotContain("password=secret", writer.ToString());
    }
}
