using System.Collections.Concurrent;
using Google.Protobuf;
using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Thread-safe implementation of <see cref="IRequestLog"/> that records calls in arrival order
/// and provides verification helpers for test assertions.
/// </summary>
internal sealed class RequestLog : IRequestLog
{
    private readonly ConcurrentQueue<RecordedCall> _calls = new();
    private readonly int _maxCalls;

    public RequestLog(int maxCalls = 10_000)
    {
        _maxCalls = maxCalls;
    }

    /// <summary>Records a call. Drops oldest entries when the cap is exceeded.</summary>
    internal void Record(RecordedCall call)
    {
        _calls.Enqueue(call);
        while (_calls.Count > _maxCalls)
        {
            _calls.TryDequeue(out _);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RecordedCall> All => _calls.ToArray();

    /// <inheritdoc />
    public IReadOnlyList<RecordedCall> For(string fullMethodName)
    {
        return _calls.Where(c => c.FullMethodName == fullMethodName).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<TRequest> RequestsFor<TRequest>(string fullMethodName)
        where TRequest : class, IMessage<TRequest>, new()
    {
        return For(fullMethodName)
            .Where(c => c.RequestPayloads.Count > 0)
            .Select(c =>
            {
                var msg = new TRequest();
                msg.MergeFrom(c.RequestPayloads[0].Span);
                return msg;
            })
            .ToArray();
    }

    /// <inheritdoc />
    public void VerifyCalled(string fullMethodName, int expectedCount)
    {
        var actual = For(fullMethodName).Count;
        if (actual != expectedCount)
        {
            throw new GrpcMockVerificationException(
                $"Expected {expectedCount} call(s) to '{fullMethodName}', but got {actual}.");
        }
    }

    /// <inheritdoc />
    public void VerifyCalledWith<TRequest>(string fullMethodName, Func<TRequest, bool> predicate)
        where TRequest : class, IMessage<TRequest>, new()
    {
        var requests = RequestsFor<TRequest>(fullMethodName);
        if (!requests.Any(predicate))
        {
            throw new GrpcMockVerificationException(
                $"No call to '{fullMethodName}' matched the predicate. {requests.Count} call(s) recorded.");
        }
    }

    /// <inheritdoc />
    public void VerifyNotCalled(string fullMethodName)
    {
        var count = For(fullMethodName).Count;
        if (count > 0)
        {
            throw new GrpcMockVerificationException(
                $"Expected no calls to '{fullMethodName}', but got {count}.");
        }
    }

    /// <inheritdoc />
    public void VerifyNoUnmatchedCalls()
    {
        var unmatched = _calls.Where(c => !c.Matched).ToArray();
        if (unmatched.Length > 0)
        {
            throw new GrpcMockVerificationException(
                $"{unmatched.Length} unmatched call(s): {string.Join(", ", unmatched.Select(c => c.FullMethodName))}");
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        while (_calls.TryDequeue(out _))
        {
        }
    }
}
