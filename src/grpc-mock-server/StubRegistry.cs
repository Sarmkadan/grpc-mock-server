using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Thread-safe registry holding the registered methods and their stubs. Stubs are evaluated
/// by priority (highest first), then by registration order; the first match wins.
/// </summary>
internal sealed class StubRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<string, (MethodType Type, Type RequestType, Type ResponseType)> _methods =
        new(StringComparer.Ordinal);
    private readonly List<StubDefinition> _stubs = new();
    private bool _sealed;

    /// <summary>Status returned when no stub matches an incoming call.</summary>
    public Status UnmatchedStatus { get; set; } = new(StatusCode.Unimplemented, "no stub matched");

    /// <summary>Snapshot of every registered method, keyed by full method name.</summary>
    public IReadOnlyDictionary<string, (MethodType Type, Type RequestType, Type ResponseType)> Methods
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, (MethodType Type, Type RequestType, Type ResponseType)>(_methods);
            }
        }
    }

    /// <summary>Distinct full method names that have been registered.</summary>
    public IReadOnlyCollection<string> RegisteredMethods
    {
        get
        {
            lock (_lock)
            {
                return _methods.Keys.ToList().AsReadOnly();
            }
        }
    }

    /// <summary>
    /// Registers a method. Registering the same method again is a no-op; registering it with a
    /// different <see cref="MethodType"/> throws. After <see cref="Seal"/> only already-known methods are accepted.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The method is already registered with another type, or the registry is sealed and the method is unknown.
    /// </exception>
    public void RegisterMethod(string fullMethodName, MethodType type, Type requestType, Type responseType)
    {
        ArgumentNullException.ThrowIfNull(fullMethodName);
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(responseType);

        lock (_lock)
        {
            if (_methods.TryGetValue(fullMethodName, out var existing))
            {
                if (existing.Type != type)
                {
                    throw new InvalidOperationException(
                        $"Method '{fullMethodName}' is already registered as {existing.Type}; cannot register it as {type}.");
                }

                return;
            }

            if (_sealed)
            {
                var declared = string.Join(", ", _methods.Keys.OrderBy(k => k, StringComparer.Ordinal));
                throw new InvalidOperationException(
                    $"Method '{fullMethodName}' was not declared when the server started, so it cannot be routed. " +
                    $"Declare new methods in StartAsync. Declared methods: [{declared}].");
            }

            _methods[fullMethodName] = (type, requestType, responseType);
        }
    }

    /// <summary>
    /// Freezes the method set. Called once the server has started, because gRPC endpoints are built from
    /// the method set at that point; methods added later could never be routed.
    /// </summary>
    public void Seal()
    {
        lock (_lock)
        {
            _sealed = true;
        }
    }

    /// <summary>Registers a stub, along with its method if it is not yet known.</summary>
    /// <exception cref="InvalidOperationException">The method is already registered with another type.</exception>
    public void Add(StubDefinition stub)
    {
        ArgumentNullException.ThrowIfNull(stub);

        lock (_lock)
        {
            RegisterMethod(stub.FullMethodName, stub.Type, stub.RequestType, stub.ResponseType);
            _stubs.Add(stub);
        }
    }

    /// <summary>Returns the stubs for a method in evaluation order (priority desc, then registration order).</summary>
    public IReadOnlyList<StubDefinition> GetStubs(string fullMethodName)
    {
        lock (_lock)
        {
            return OrderedStubsLocked(fullMethodName).AsReadOnly();
        }
    }

    /// <summary>
    /// Finds the first stub for the method whose <see cref="StubDefinition.Matches"/> accepts the call.
    /// A matched stub with a <see cref="StubDefinition.RemainingTimes"/> count is decremented, and
    /// removed when the count reaches zero.
    /// </summary>
    /// <param name="fullMethodName">Full gRPC method name.</param>
    /// <param name="request">Request message, or null for a streaming call that has not read one yet.</param>
    /// <param name="headers">Request headers.</param>
    /// <returns>The matched stub, or <see langword="null"/> if nothing matched.</returns>
    public StubDefinition? FindMatch(string fullMethodName, object? request, Metadata headers)
    {
        lock (_lock)
        {
            foreach (var stub in OrderedStubsLocked(fullMethodName))
            {
                if (stub.RemainingTimes is <= 0 || !stub.Matches(request, headers))
                {
                    continue;
                }

                if (stub.RemainingTimes is { } remaining)
                {
                    stub.RemainingTimes = remaining - 1;
                    if (stub.RemainingTimes == 0)
                    {
                        _stubs.Remove(stub);
                    }
                }

                return stub;
            }
        }

        return null;
    }

    /// <summary>Removes all stub definitions. Registered methods are kept.</summary>
    public void ClearStubs()
    {
        lock (_lock)
        {
            _stubs.Clear();
        }
    }

    private List<StubDefinition> OrderedStubsLocked(string fullMethodName) =>
        _stubs
            .Where(s => s.FullMethodName == fullMethodName)
            .OrderByDescending(s => s.Priority)
            .ToList();
}
