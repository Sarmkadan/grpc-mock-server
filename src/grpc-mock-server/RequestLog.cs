using Google.Protobuf;

namespace GrpcMockServer;

/// <summary>
/// Thread-safe implementation of <see cref="IRequestLog"/> that records calls in arrival order
/// and provides verification helpers for test assertions.
/// </summary>
internal sealed class RequestLog : IRequestLog
{
    private readonly object _gate = new();
    private readonly List<RecordedCall> _calls = new();
    private readonly int _maxRecordedCalls;

    /// <summary>Creates a log that keeps at most <paramref name="maxRecordedCalls"/> calls.</summary>
    /// <param name="maxRecordedCalls">Upper bound on retained calls; the oldest are dropped first.</param>
    public RequestLog(int maxRecordedCalls)
    {
        _maxRecordedCalls = maxRecordedCalls;
    }

    /// <summary>Records a call. Drops oldest entries when the cap is exceeded.</summary>
    /// <param name="call">Call to record.</param>
    internal void Record(RecordedCall call)
    {
        lock (_gate)
        {
            _calls.Add(call);
            while (_calls.Count > _maxRecordedCalls)
            {
                _calls.RemoveAt(0);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RecordedCall> All
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RecordedCall> For(string fullMethodName)
    {
        return All.Where(c => string.Equals(c.FullMethodName, fullMethodName, StringComparison.Ordinal)).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<TRequest> RequestsFor<TRequest>(string fullMethodName)
        where TRequest : class, IMessage<TRequest>, new()
    {
        return For(fullMethodName)
            .Where(c => c.RequestPayloads.Count > 0)
            .Select(c => c.Request<TRequest>())
            .ToArray();
    }

    /// <inheritdoc />
    public void VerifyCalled(string fullMethodName, int expectedCount)
    {
        var snapshot = All;
        var actual = snapshot.Count(c => string.Equals(c.FullMethodName, fullMethodName, StringComparison.Ordinal));
        if (actual != expectedCount)
        {
            throw new GrpcMockVerificationException(
                $"Expected {fullMethodName} to be called {expectedCount} time(s) but was called {actual} time(s). " +
                $"Recorded calls:{Environment.NewLine}{FormatCalls(snapshot)}");
        }
    }

    /// <inheritdoc />
    public void VerifyCalledWith<TRequest>(string fullMethodName, Func<TRequest, bool> predicate)
        where TRequest : class, IMessage<TRequest>, new()
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var snapshot = All;
        var found = snapshot
            .Where(c => string.Equals(c.FullMethodName, fullMethodName, StringComparison.Ordinal) && c.RequestPayloads.Count > 0)
            .Any(c => Deserialize<TRequest>(c.RequestPayloads[0]) is { } request && predicate(request));
        if (!found)
        {
            throw new GrpcMockVerificationException(
                $"Expected a call to {fullMethodName} with a request matching the predicate, but none matched. " +
                $"Recorded calls:{Environment.NewLine}{FormatCalls(snapshot)}");
        }
    }

    /// <inheritdoc />
    public void VerifyNotCalled(string fullMethodName)
    {
        var snapshot = All;
        var actual = snapshot.Count(c => string.Equals(c.FullMethodName, fullMethodName, StringComparison.Ordinal));
        if (actual > 0)
        {
            throw new GrpcMockVerificationException(
                $"Expected {fullMethodName} not to be called but was called {actual} time(s). " +
                $"Recorded calls:{Environment.NewLine}{FormatCalls(snapshot)}");
        }
    }

    /// <inheritdoc />
    public void VerifyNoUnmatchedCalls()
    {
        var snapshot = All;
        var unmatched = snapshot.Count(c => !c.Matched);
        if (unmatched > 0)
        {
            throw new GrpcMockVerificationException(
                $"Expected every call to match a stub but {unmatched} call(s) did not. " +
                $"Recorded calls:{Environment.NewLine}{FormatCalls(snapshot)}");
        }
    }

    /// <summary>
    /// Deserializes the first request payload. Returns <c>null</c> when the bytes are not a valid
    /// <typeparamref name="TRequest"/>, so a malformed payload is skipped rather than failing verification.
    /// </summary>
    private static TRequest? Deserialize<TRequest>(ReadOnlyMemory<byte> payload)
        where TRequest : class, IMessage<TRequest>, new()
    {
        var msg = new TRequest();
        try
        {
            msg.MergeFrom(payload.Span);
            return msg;
        }
        catch (InvalidProtocolBufferException)
        {
            return null;
        }
    }

    /// <summary>Renders one line per call for verification failure messages.</summary>
    private static string FormatCalls(IEnumerable<RecordedCall> calls)
    {
        var lines = calls.Select(c =>
            $"  [{c.ReceivedAt:o}] {c.Type} {c.FullMethodName} status={c.Status.StatusCode} " +
            $"matched={c.Matched} payloads={c.RequestPayloads.Count}").ToArray();

        return lines.Length == 0 ? "  (no calls recorded)" : string.Join(Environment.NewLine, lines);
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_gate)
        {
            _calls.Clear();
        }
    }
}
