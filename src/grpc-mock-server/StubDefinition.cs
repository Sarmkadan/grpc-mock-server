using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Internal representation of a single stub registered via the fluent builder.
/// Holds matching predicates, response configuration and lifetime tracking.
/// </summary>
internal sealed class StubDefinition
{
    private readonly object _lock = new();
    private int _remaining;

    /// <summary>Full gRPC method name in the form <c>package.Service/Method</c>.</summary>
    public required string FullMethodName { get; init; }

    /// <summary>Priority for evaluation order; higher wins.</summary>
    public int Priority { get; init; }

    /// <summary>Artificial delay before responding.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>Response headers (initial metadata).</summary>
    public Metadata ResponseHeaders { get; init; } = Metadata.Empty;

    /// <summary>Response trailers.</summary>
    public Metadata ResponseTrailers { get; init; } = Metadata.Empty;

    /// <summary>Max number of times this stub may match; null means unlimited.</summary>
    public int? TimesLimit { get; init; }

    /// <summary>Order this stub was registered; used as tiebreaker after priority.</summary>
    public int RegistrationOrder { get; init; }

    /// <summary>The handler delegate invoked when the stub matches.</summary>
    public required Delegate Handler { get; init; }

    /// <summary>Request predicates; all must match for the stub to fire.</summary>
    public IReadOnlyList<Func<object, bool>> Predicates { get; init; } = Array.Empty<Func<object, bool>>();

    /// <summary>Header matchers; all must match.</summary>
    public IReadOnlyList<(string Key, Func<string, bool> Predicate)> HeaderMatchers { get; init; } =
        Array.Empty<(string, Func<string, bool>)>();

    /// <summary>Initializes the remaining counter from the limit.</summary>
    public void Initialize()
    {
        _remaining = TimesLimit ?? int.MaxValue;
    }

    /// <summary>Returns true if the stub is still active (has remaining invocations).</summary>
    public bool IsActive
    {
        get
        {
            lock (_lock)
            {
                return _remaining > 0;
            }
        }
    }

    /// <summary>Attempts to consume one invocation. Returns false if exhausted.</summary>
    public bool TryConsume()
    {
        lock (_lock)
        {
            if (_remaining <= 0)
            {
                return false;
            }

            if (TimesLimit.HasValue)
            {
                _remaining--;
            }

            return true;
        }
    }

    /// <summary>Checks whether the request and headers satisfy all predicates.</summary>
    public bool Matches(object? request, Metadata headers)
    {
        foreach (var predicate in Predicates)
        {
            if (request is null || !predicate(request))
            {
                return false;
            }
        }

        foreach (var (key, valuePredicate) in HeaderMatchers)
        {
            var entry = headers.FirstOrDefault(e =>
                string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
            if (entry is null || !valuePredicate(entry.Value))
            {
                return false;
            }
        }

        return true;
    }
}
