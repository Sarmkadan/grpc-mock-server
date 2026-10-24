using Google.Protobuf;
using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Fluent builder for client-streaming stubs. Matching and metadata steps come from <see cref="StubBuilderBase{TRequest, TSelf}"/>;
/// each response finalizer composes a <see cref="StubDefinition"/> and registers it.
/// </summary>
/// <remarks>
/// Request matchers (<c>When(...)</c>) cannot be evaluated before the stream is read, so for client streaming
/// they are evaluated against the <b>first</b> received message only. If the client sends an empty stream there
/// is no message to evaluate and the request matchers are skipped; header matchers still apply.
/// The handler receives the complete list of streamed messages once the stub has matched.
/// </remarks>
/// <typeparam name="TRequest">Request message type.</typeparam>
/// <typeparam name="TResponse">Response message type.</typeparam>
internal sealed class ClientStreamingStubBuilder<TRequest, TResponse>
    : StubBuilderBase<TRequest, IClientStreamingStubBuilder<TRequest, TResponse>>, IClientStreamingStubBuilder<TRequest, TResponse>
    where TRequest : class, IMessage<TRequest>, new()
    where TResponse : class, IMessage<TResponse>, new()
{
    /// <summary>
    /// Creates a client-streaming stub builder bound to a method.
    /// </summary>
    /// <param name="root">Root builder that owns the stub registry.</param>
    /// <param name="registry">Registry the finalized stub is added to.</param>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    public ClientStreamingStubBuilder(GrpcMockBuilder root, StubRegistry registry, string fullMethodName)
        : base(root, registry, fullMethodName)
    {
    }

    /// <inheritdoc />
    protected override IClientStreamingStubBuilder<TRequest, TResponse> Self => this;

    /// <inheritdoc />
    public IGrpcMockBuilder Respond(Func<IReadOnlyList<TRequest>, TResponse> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ClientStreamingHandler<TRequest, TResponse> handler = (requests, _) => Task.FromResult(factory(requests));
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder Respond(TResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        ClientStreamingHandler<TRequest, TResponse> handler = (_, _) => Task.FromResult(response);
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder Fail(StatusCode statusCode, string? detail = null)
    {
        ClientStreamingHandler<TRequest, TResponse> handler = (_, _) =>
            throw new RpcException(new Status(statusCode, detail ?? string.Empty));
        return Register(handler);
    }

    private IGrpcMockBuilder Register(ClientStreamingHandler<TRequest, TResponse> handler)
    {
        Registry.Add(Build(MethodType.ClientStreaming, typeof(TResponse), handler));
        return Root;
    }
}
