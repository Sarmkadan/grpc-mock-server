using Google.Protobuf;
using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Root of the fluent stub configuration API. Each <c>For*</c> call declares a gRPC method by its
/// full name and message types and returns a typed builder used to add matchers and responses.
/// </summary>
public interface IGrpcMockBuilder
{
    /// <summary>Declares a unary method stub.</summary>
    /// <typeparam name="TRequest">Protobuf request message type.</typeparam>
    /// <typeparam name="TResponse">Protobuf response message type.</typeparam>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    IUnaryStubBuilder<TRequest, TResponse> ForUnary<TRequest, TResponse>(string fullMethodName)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new();

    /// <summary>Declares a server-streaming method stub.</summary>
    /// <typeparam name="TRequest">Protobuf request message type.</typeparam>
    /// <typeparam name="TResponse">Protobuf response message type.</typeparam>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    IServerStreamingStubBuilder<TRequest, TResponse> ForServerStreaming<TRequest, TResponse>(string fullMethodName)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new();

    /// <summary>Declares a client-streaming method stub.</summary>
    /// <typeparam name="TRequest">Protobuf request message type.</typeparam>
    /// <typeparam name="TResponse">Protobuf response message type.</typeparam>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    IClientStreamingStubBuilder<TRequest, TResponse> ForClientStreaming<TRequest, TResponse>(string fullMethodName)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new();

    /// <summary>Declares a bidirectional-streaming method stub.</summary>
    /// <typeparam name="TRequest">Protobuf request message type.</typeparam>
    /// <typeparam name="TResponse">Protobuf response message type.</typeparam>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    IDuplexStreamingStubBuilder<TRequest, TResponse> ForDuplexStreaming<TRequest, TResponse>(string fullMethodName)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new();

    /// <summary>
    /// Declares a stub for a method whose message types are not available at compile time.
    /// Request and response bodies are handled as raw protobuf bytes.
    /// </summary>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    /// <param name="type">gRPC method type.</param>
    IUnaryStubBuilder<byte[], byte[]> ForRaw(string fullMethodName, MethodType type = MethodType.Unary);

    /// <summary>
    /// Configures the response for any call that does not match a registered stub.
    /// Defaults to <see cref="StatusCode.Unimplemented"/>.
    /// </summary>
    /// <param name="status">Status returned for unmatched calls.</param>
    IGrpcMockBuilder OnUnmatched(Status status);
}

/// <summary>Common request matching and metadata steps shared by all stub kinds.</summary>
/// <typeparam name="TRequest">Request message type.</typeparam>
/// <typeparam name="TSelf">Concrete builder type returned for chaining.</typeparam>
public interface IStubBuilder<TRequest, TSelf>
    where TSelf : IStubBuilder<TRequest, TSelf>
{
    /// <summary>Adds a predicate the request must satisfy for this stub to match.</summary>
    /// <param name="predicate">Predicate evaluated against the deserialized request.</param>
    TSelf When(Func<TRequest, bool> predicate);

    /// <summary>Adds a reusable matcher the request must satisfy.</summary>
    /// <param name="matcher">Matcher instance.</param>
    TSelf When(IRequestMatcher<TRequest> matcher);

    /// <summary>Requires a request header (metadata entry) with the given key and value.</summary>
    /// <param name="key">Header key (case-insensitive).</param>
    /// <param name="value">Expected value.</param>
    TSelf WithHeader(string key, string value);

    /// <summary>Requires a request header to satisfy a predicate.</summary>
    /// <param name="key">Header key (case-insensitive).</param>
    /// <param name="predicate">Predicate evaluated against the header value.</param>
    TSelf WithHeader(string key, Func<string, bool> predicate);

    /// <summary>Sets a priority; higher-priority stubs are evaluated first.</summary>
    /// <param name="priority">Priority value. Default is 0.</param>
    TSelf WithPriority(int priority);

    /// <summary>Artificially delays the response.</summary>
    /// <param name="delay">Delay before the response (or first stream item) is sent.</param>
    TSelf WithDelay(TimeSpan delay);

    /// <summary>Adds response headers (initial metadata).</summary>
    /// <param name="headers">Headers to send.</param>
    TSelf WithResponseHeaders(Metadata headers);

    /// <summary>Adds response trailers.</summary>
    /// <param name="trailers">Trailers to send.</param>
    TSelf WithResponseTrailers(Metadata trailers);

    /// <summary>Limits how many times this stub may match before it is removed.</summary>
    /// <param name="count">Maximum match count.</param>
    TSelf Times(int count);
}

/// <summary>Fluent builder for unary method stubs.</summary>
/// <typeparam name="TRequest">Request message type.</typeparam>
/// <typeparam name="TResponse">Response message type.</typeparam>
public interface IUnaryStubBuilder<TRequest, TResponse> : IStubBuilder<TRequest, IUnaryStubBuilder<TRequest, TResponse>>
{
    /// <summary>Responds with a fixed message.</summary>
    /// <param name="response">Response to return.</param>
    IGrpcMockBuilder Respond(TResponse response);

    /// <summary>Responds with a message computed from the request.</summary>
    /// <param name="factory">Response factory.</param>
    IGrpcMockBuilder Respond(Func<TRequest, TResponse> factory);

    /// <summary>Responds with a message computed asynchronously from the request and call context.</summary>
    /// <param name="factory">Asynchronous response factory.</param>
    IGrpcMockBuilder Respond(Func<TRequest, ServerCallContext, Task<TResponse>> factory);

    /// <summary>Fails the call with the given status.</summary>
    /// <param name="statusCode">gRPC status code.</param>
    /// <param name="detail">Optional status detail message.</param>
    IGrpcMockBuilder Fail(StatusCode statusCode, string? detail = null);

    /// <summary>Fails the call with the given <see cref="RpcException"/>.</summary>
    /// <param name="exception">Exception to throw on match.</param>
    IGrpcMockBuilder Fail(RpcException exception);

    /// <summary>
    /// Returns responses in order for successive matching calls; the last response repeats.
    /// </summary>
    /// <param name="responses">Ordered responses.</param>
    IGrpcMockBuilder RespondInSequence(params TResponse[] responses);
}

/// <summary>Fluent builder for server-streaming method stubs.</summary>
/// <typeparam name="TRequest">Request message type.</typeparam>
/// <typeparam name="TResponse">Response message type.</typeparam>
public interface IServerStreamingStubBuilder<TRequest, TResponse>
    : IStubBuilder<TRequest, IServerStreamingStubBuilder<TRequest, TResponse>>
{
    /// <summary>Streams the given messages then completes.</summary>
    /// <param name="responses">Messages to stream in order.</param>
    IGrpcMockBuilder Stream(params TResponse[] responses);

    /// <summary>Streams messages produced from the request.</summary>
    /// <param name="factory">Factory yielding messages to stream.</param>
    IGrpcMockBuilder Stream(Func<TRequest, IEnumerable<TResponse>> factory);

    /// <summary>Streams messages produced asynchronously; supports delays between items.</summary>
    /// <param name="factory">Asynchronous factory yielding messages to stream.</param>
    IGrpcMockBuilder Stream(Func<TRequest, ServerCallContext, IAsyncEnumerable<TResponse>> factory);

    /// <summary>Streams the given messages then fails with the given status.</summary>
    /// <param name="statusCode">Status to fail with after the messages.</param>
    /// <param name="responses">Messages to stream before failing.</param>
    IGrpcMockBuilder StreamThenFail(StatusCode statusCode, params TResponse[] responses);

    /// <summary>Fails the call immediately with the given status.</summary>
    /// <param name="statusCode">gRPC status code.</param>
    /// <param name="detail">Optional status detail message.</param>
    IGrpcMockBuilder Fail(StatusCode statusCode, string? detail = null);
}

/// <summary>Fluent builder for client-streaming method stubs.</summary>
/// <typeparam name="TRequest">Request message type.</typeparam>
/// <typeparam name="TResponse">Response message type.</typeparam>
public interface IClientStreamingStubBuilder<TRequest, TResponse>
    : IStubBuilder<TRequest, IClientStreamingStubBuilder<TRequest, TResponse>>
{
    /// <summary>Responds with a message computed from all received requests.</summary>
    /// <param name="factory">Factory receiving the full list of streamed requests.</param>
    IGrpcMockBuilder Respond(Func<IReadOnlyList<TRequest>, TResponse> factory);

    /// <summary>Responds with a fixed message regardless of what was streamed.</summary>
    /// <param name="response">Response to return.</param>
    IGrpcMockBuilder Respond(TResponse response);

    /// <summary>Fails the call with the given status.</summary>
    /// <param name="statusCode">gRPC status code.</param>
    /// <param name="detail">Optional status detail message.</param>
    IGrpcMockBuilder Fail(StatusCode statusCode, string? detail = null);
}

/// <summary>Fluent builder for bidirectional-streaming method stubs.</summary>
/// <typeparam name="TRequest">Request message type.</typeparam>
/// <typeparam name="TResponse">Response message type.</typeparam>
public interface IDuplexStreamingStubBuilder<TRequest, TResponse>
    : IStubBuilder<TRequest, IDuplexStreamingStubBuilder<TRequest, TResponse>>
{
    /// <summary>Echoes each request through a mapping function.</summary>
    /// <param name="map">Mapping from request to response.</param>
    IGrpcMockBuilder Echo(Func<TRequest, TResponse> map);

    /// <summary>Gives full control over the duplex stream.</summary>
    /// <param name="handler">Handler receiving the request and response streams.</param>
    IGrpcMockBuilder Handle(Func<IAsyncStreamReader<TRequest>, IServerStreamWriter<TResponse>, ServerCallContext, Task> handler);

    /// <summary>Fails the call with the given status.</summary>
    /// <param name="statusCode">gRPC status code.</param>
    /// <param name="detail">Optional status detail message.</param>
    IGrpcMockBuilder Fail(StatusCode statusCode, string? detail = null);
}
