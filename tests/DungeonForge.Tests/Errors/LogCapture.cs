using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace DungeonForge.Tests.Errors;

internal sealed record LogEntry(
    LogLevel Level, EventId EventId, string Message, IReadOnlyDictionary<string, object?> Scope, Exception? Exception);

// Captures every log call with the event id and the flattened values of the scopes open at that moment.
// Register with `services.AddLogging(l => l.AddProvider(capture))`.
internal sealed class LogCapture : ILoggerProvider, ILogger
{
    private static readonly AsyncLocal<ImmutableStack<object>> OpenScopes = new();

    public List<LogEntry> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        var before = OpenScopes.Value ?? ImmutableStack<object>.Empty;
        OpenScopes.Value = before.Push(state);
        return new Restore(before);
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var scope = new Dictionary<string, object?>();
        foreach (var open in (OpenScopes.Value ?? ImmutableStack<object>.Empty).Reverse())
        {
            if (open is IEnumerable<KeyValuePair<string, object>> pairs)
            {
                foreach (var (key, value) in pairs)
                {
                    scope[key] = value;
                }
            }
        }

        lock (Entries)
        {
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), scope, exception));
        }
    }

    public void Dispose()
    {
    }

    private sealed class Restore(ImmutableStack<object> before) : IDisposable
    {
        public void Dispose() => OpenScopes.Value = before;
    }
}
