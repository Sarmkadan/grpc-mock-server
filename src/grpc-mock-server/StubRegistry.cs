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
    private readonly List<StubDefinition> _stubs = new();
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

    /// <summary>Returns true if a method with the given name has been registered.</summary>
    internal bool HasMethod(string fullMethodName)
    {
        lock (_lock)
        {
            return _methods.Any(m => m.FullMethodName == fullMethodName);
        }
    }

    /// <summary>Registers a stub definition.</summary>
    internal void AddStub(StubDefinition stub)
    {
        ArgumentNullException.ThrowIfNull(stub);
        lock (_lock)
        {
            _stubs.Add(stub);
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
        lock (_lock)
        {
            // Evaluate stubs in priority desc, then registration order asc.
            var candidates = _stubs
                .Where(s => s.FullMethodName == fullMethodName && s.IsActive)
                .OrderByDescending(s => s.Priority)
                .ThenBy(s => s.RegistrationOrder);

            foreach (var stub in candidates)
            {
                if (!stub.Matches(request, headers))
                {
                    continue;
                }

                if (!stub.TryConsume())
                {
                    continue;
                }

                return new MatchedStub
                {
                    Delay = stub.Delay,
                    ResponseHeaders = stub.ResponseHeaders,
                    ResponseTrailers = stub.ResponseTrailers,
                    Handler = stub.Handler
                };
            }
        }

        return null;
    }

    /// <summary>Clears all registered methods and stubs.</summary>
    internal void Clear()
    {
        lock (_lock)
        {
            _methods.Clear();
            _stubs.Clear();
        }
    }
}
