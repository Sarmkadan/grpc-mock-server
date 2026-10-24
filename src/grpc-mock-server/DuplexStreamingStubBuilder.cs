using Google.Protobuf;
using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Fluent builder for duplex-streaming stubs. Matching and metadata steps come from <see cref="StubBuilderBase{TRequest, TSelf}"/>;
/// each finalizer composes a <see cref="StubDefinition"/> and registers it.
/// </summary>
/// <remarks>
/// Duplex handlers receive the request stream reader directly, so request matchers (<c>When(...)</c>) cannot be
/// evaluated against the stream without consuming it. The binder peeks only the first message to decide whether
/// the stub matches, which makes <c>When(...)</c> on duplex stubs unsupported: calling it throws
/// <see cref="NotSupportedException"/>. Use <c>WithHeader(...)</c> to route duplex calls instead.
/// </remarks>
/// <typeparam name="TRequest">Request message type.</typeparam>
/// <typeparam name="TResponse">Response message type.</typeparam>
internal sealed class DuplexStreamingStubBuilder<TRequest, TResponse>
    : StubBuilderBase<TRequest, IDuplexStreamingStubBuilder<TRequest, TResponse>>, IDuplexStreamingStubBuilder<TRequest, TResponse>
    where TRequest : class, IMessage<TRequest>, new()
    where TResponse : class, IMessage<TResponse>, new()
{
    /// <summary>
    /// Creates a duplex-streaming stub builder bound to a method.
    /// </summary>
    /// <param name="root">Root builder that owns the stub registry.</param>
    /// <param name="registry">Registry the finalized stub is added to.</param>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    public DuplexStreamingStubBuilder(GrpcMockBuilder root, StubRegistry registry, string fullMethodName)
        : base(root, registry, fullMethodName)
    {
    }

    /// <inheritdoc />
    protected override IDuplexStreamingStubBuilder<TRequest, TResponse> Self => this;

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always. Request matchers cannot be evaluated on a duplex stream; use <c>WithHeader(...)</c>.</exception>
    protected override void OnRequestMatcherAdded() =>
        throw new NotSupportedException(
            "When(...) is not supported on duplex-streaming stubs; only WithHeader(...) matchers can be used.");

    /// <inheritdoc />
    public IGrpcMockBuilder Echo(Func<TRequest, TResponse> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        DuplexStreamingHandler<TRequest, TResponse> handler = async (requestStream, responseStream, context) =>
        {
            await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
            {
                await responseStream.WriteAsync(map(request));
            }
        };
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder Handle(Func<IAsyncStreamReader<TRequest>, IServerStreamWriter<TResponse>, ServerCallContext, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        DuplexStreamingHandler<TRequest, TResponse> duplexHandler = (requestStream, responseStream, context) =>
            handler(requestStream, responseStream, context);
        return Register(duplexHandler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder Fail(StatusCode statusCode, string? detail = null)
    {
        DuplexStreamingHandler<TRequest, TResponse> handler = (_, _, _) =>
            throw new RpcException(new Status(statusCode, detail ?? string.Empty));
        return Register(handler);
    }

    private IGrpcMockBuilder Register(DuplexStreamingHandler<TRequest, TResponse> handler)
    {
        Registry.Add(Build(MethodType.DuplexStreaming, typeof(TResponse), handler));
        return Root;
    }
}
