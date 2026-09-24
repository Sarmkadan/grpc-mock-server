using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Records every call received by the server and exposes a verification API in the spirit of
/// WireMock's <c>verify()</c>: assert how many times a method was invoked and with what payload.
/// </summary>
public interface IRequestLog
{
    /// <summary>All recorded calls, in arrival order.</summary>
    IReadOnlyList<RecordedCall> All { get; }

    /// <summary>Recorded calls for a specific method.</summary>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    IReadOnlyList<RecordedCall> For(string fullMethodName);

    /// <summary>
    /// Deserialized request messages received by a specific unary or server-streaming method.
    /// </summary>
    /// <typeparam name="TRequest">Request message type.</typeparam>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    IReadOnlyList<TRequest> RequestsFor<TRequest>(string fullMethodName)
        where TRequest : class, Google.Protobuf.IMessage<TRequest>, new();

    /// <summary>
    /// Asserts the method was called exactly <paramref name="expectedCount"/> times.
    /// Throws <see cref="GrpcMockVerificationException"/> with a diff of recorded calls on failure.
    /// </summary>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    /// <param name="expectedCount">Expected number of calls.</param>
    void VerifyCalled(string fullMethodName, int expectedCount);

    /// <summary>Asserts the method was called at least once with a request satisfying the predicate.</summary>
    /// <typeparam name="TRequest">Request message type.</typeparam>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    /// <param name="predicate">Predicate the request must satisfy.</param>
    void VerifyCalledWith<TRequest>(string fullMethodName, Func<TRequest, bool> predicate)
        where TRequest : class, Google.Protobuf.IMessage<TRequest>, new();

    /// <summary>Asserts the method was never called.</summary>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    void VerifyNotCalled(string fullMethodName);

    /// <summary>Asserts that no call reached the server without matching a stub.</summary>
    void VerifyNoUnmatchedCalls();

    /// <summary>Clears the log.</summary>
    void Clear();
}

/// <summary>A single call recorded by the mock server.</summary>
/// <param name="FullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
/// <param name="Type">gRPC method type.</param>
/// <param name="Headers">Request headers as received.</param>
/// <param name="RequestPayloads">Raw serialized request messages (one per streamed item).</param>
/// <param name="Status">Status returned to the client.</param>
/// <param name="Matched">Whether a stub matched this call.</param>
/// <param name="ReceivedAt">UTC timestamp of arrival.</param>
public sealed record RecordedCall(
    string FullMethodName,
    MethodType Type,
    Metadata Headers,
    IReadOnlyList<ReadOnlyMemory<byte>> RequestPayloads,
    Status Status,
    bool Matched,
    DateTimeOffset ReceivedAt)
{
    /// <summary>Deserializes the first request payload as <typeparamref name="TRequest"/>.</summary>
    /// <typeparam name="TRequest">Request message type.</typeparam>
    public TRequest Request<TRequest>() where TRequest : class, Google.Protobuf.IMessage<TRequest>, new()
        => throw new NotImplementedException();
}

/// <summary>Thrown when a request log verification fails.</summary>
public sealed class GrpcMockVerificationException : Exception
{
    /// <summary>Creates a verification exception.</summary>
    /// <param name="message">Failure description including recorded calls.</param>
    public GrpcMockVerificationException(string message) : base(message)
    {
    }
}
