using Google.Protobuf;
using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Fluent builder for server-streaming stubs. Matching and metadata steps come from <see cref="StubBuilderBase{TRequest, TSelf}"/>;
/// each stream finalizer composes a <see cref="StubDefinition"/> and registers it.
/// </summary>
/// <typeparam name="TRequest">Request message type.</typeparam>
/// <typeparam name="TResponse">Response message type.</typeparam>
internal sealed class ServerStreamingStubBuilder<TRequest, TResponse>
    : StubBuilderBase<TRequest, IServerStreamingStubBuilder<TRequest, TResponse>>, IServerStreamingStubBuilder<TRequest, TResponse>
    where TRequest : class, IMessage<TRequest>, new()
    where TResponse : class, IMessage<TResponse>, new()
{
    /// <summary>
    /// Creates a server-streaming stub builder bound to a method.
    /// </summary>
    /// <param name="root">Root builder that owns the stub registry.</param>
    /// <param name="registry">Registry the finalized stub is added to.</param>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    public ServerStreamingStubBuilder(GrpcMockBuilder root, StubRegistry registry, string fullMethodName)
        : base(root, registry, fullMethodName)
    {
    }

    /// <inheritdoc />
    protected override IServerStreamingStubBuilder<TRequest, TResponse> Self => this;

    /// <inheritdoc />
    public IGrpcMockBuilder Stream(params TResponse[] responses)
    {
        ThrowIfFinalized();
        ArgumentNullException.ThrowIfNull(responses);
        ServerStreamingHandler<TRequest, TResponse> handler = async (_, responseStream, _) =>
        {
            foreach (var response in responses)
            {
                await responseStream.WriteAsync(response);
            }
        };
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder Stream(Func<TRequest, IEnumerable<TResponse>> factory)
    {
        ThrowIfFinalized();
        ArgumentNullException.ThrowIfNull(factory);
        ServerStreamingHandler<TRequest, TResponse> handler = async (request, responseStream, _) =>
        {
            foreach (var response in factory(request))
            {
                await responseStream.WriteAsync(response);
            }
        };
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder Stream(Func<TRequest, ServerCallContext, IAsyncEnumerable<TResponse>> factory)
    {
        ThrowIfFinalized();
        ArgumentNullException.ThrowIfNull(factory);
        ServerStreamingHandler<TRequest, TResponse> handler = async (request, responseStream, context) =>
        {
            await foreach (var response in factory(request, context).WithCancellation(context.CancellationToken))
            {
                await responseStream.WriteAsync(response);
            }
        };
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder StreamThenFail(StatusCode statusCode, params TResponse[] responses)
    {
        ThrowIfFinalized();
        ValidateFailureStatus(statusCode);
        ArgumentNullException.ThrowIfNull(responses);
        ServerStreamingHandler<TRequest, TResponse> handler = async (_, responseStream, _) =>
        {
            foreach (var response in responses)
            {
                await responseStream.WriteAsync(response);
            }

            throw new RpcException(new Status(statusCode, string.Empty));
        };
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder Fail(StatusCode statusCode, string? detail = null)
    {
        ThrowIfFinalized();
        ValidateFailureStatus(statusCode);
        ServerStreamingHandler<TRequest, TResponse> handler = (_, _, _) =>
            throw new RpcException(new Status(statusCode, detail ?? string.Empty));
        return Register(handler);
    }

    private IGrpcMockBuilder Register(ServerStreamingHandler<TRequest, TResponse> handler)
    {
        return Complete(MethodType.ServerStreaming, typeof(TResponse), handler);
    }
}
