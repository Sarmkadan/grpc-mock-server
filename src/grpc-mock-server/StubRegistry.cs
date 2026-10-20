using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Thread-safe registry holding all configured stubs. Methods are evaluated in priority order
/// and the first match wins.
/// </summary>
internal sealed class StubRegistry
{
    private readonly object _lock = new();
    private readonly List<MethodEntry> _methods = new();
    private Status _unmatchedStatus = new(StatusCode.Unimplemented, "No stub matched.");

    /// <summary>All registered method entries.</summary>
    public IReadOnlyList<MethodEntry> Methods
    {
        get
        {
            lock (_lock)
            {
                return _methods.ToList().AsReadOnly();
            }
        }
    }

    /// <summary>Status returned when no stub matches an incoming call.</summary>
    public Status UnmatchedStatus
    {
        get
        {
            lock (_lock)
            {
                return _unmatchedStatus;
            }
        }
        set
        {
            lock (_lock)
            {
                _unmatchedStatus = value;
            }
        }
    }

    /// <summary>Registers a method entry.</summary>
    internal void AddMethod(MethodEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            _methods.Add(entry);
        }
    }

    /// <summary>
    /// Attempts to find a stub matching the incoming call.
    /// </summary>
    /// <param name="fullMethodName">Full gRPC method name.</param>
    /// <param name="request">Deserialized request message (null for client/duplex streaming initial match).</param>
    /// <param name="headers">Request headers.</param>
    /// <returns>The matched stub configuration, or <see langword="null"/> if nothing matched.</returns>
    public MatchedStub? FindMatch(string fullMethodName, object? request, Metadata headers)
    {
        // Stub - will be implemented in a later task with the fluent builder.
        return null;
    }

    /// <summary>Clears all registered methods.</summary>
    internal void Clear()
    {
        lock (_lock)
        {
            _methods.Clear();
        }
    }
}
