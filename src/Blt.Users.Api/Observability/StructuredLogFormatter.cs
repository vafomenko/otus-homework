using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Blt.Users.Api.Observability;

public sealed class StructuredLogFormatter() : ConsoleFormatter("structured")
{
    public override void Write<TState>(in LogEntry<TState> entry,
        IExternalScopeProvider? scopes, TextWriter writer)
    {
        var fields = new Dictionary<string, object?>();
        scopes?.ForEachScope((scope, target) => AddFields(scope, target), fields);
        AddFields(entry.State, fields);
        fields["@timestamp"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            System.Globalization.CultureInfo.InvariantCulture);
        fields["service"] = "user-service";
        fields["level"] = entry.LogLevel switch
        {
            LogLevel.Information => "INFO", LogLevel.Warning => "WARN",
            LogLevel.Error or LogLevel.Critical => "ERROR",
            _ => entry.LogLevel.ToString().ToUpperInvariant()
        };
        fields["category"] = entry.Category;
        fields["message"] = entry.Formatter(entry.State, entry.Exception);
        // Exception messages can contain database values; keep type and stack only.
        if (entry.Exception is { } exception)
        {
            fields["exception_type"] = exception.GetType().FullName;
            fields["stack_trace"] = exception.StackTrace;
        }
        writer.WriteLine(JsonSerializer.Serialize(fields));
    }

    private static void AddFields(object? state, Dictionary<string, object?> fields)
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> values)
            foreach (var pair in values)
                if (pair.Key != "{OriginalFormat}") fields[pair.Key] = pair.Value;
    }
}
