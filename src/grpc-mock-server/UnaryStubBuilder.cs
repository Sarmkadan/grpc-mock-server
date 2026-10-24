using Google.Protobuf;
using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Fluent builder for unary stubs. Matching and metadata steps come from <see cref="StubBuilderBase{TRequest, TSelf}"/>;
/// each response finalizer composes a <see cref="StubDefinition"/> and registers it.
/// </summary>
/// <typeparam name="TRequest">Request message type.</typeparam>
/// <typeparam name="TResponse">Response message type.</typeparam>
internal sealed class UnaryStubBuilder<TRequest, TResponse>
    : StubBuilderBase<TRequest, IUnaryStubBuilder<TRequest, TResponse>>, IUnaryStubBuilder<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    /// <summary>
    /// Creates a unary stub builder bound to a method.
    /// </summary>
    /// <param name="root">Root builder that owns the stub registry.</param>
    /// <param name="registry">Registry the finalized stub is added to.</param>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    public UnaryStubBuilder(GrpcMockBuilder root, StubRegistry registry, string fullMethodName)
        : base(root, registry, fullMethodName)
    {
    }

    /// <inheritdoc />
    protected override IUnaryStubBuilder<TRequest, TResponse> Self => this;

    /// <inheritdoc />
    public IGrpcMockBuilder Respond(TResponse response)
    {
        ThrowIfFinalized();
        ArgumentNullException.ThrowIfNull(response);
        UnaryHandler<TRequest, TResponse> handler = (_, _) => Task.FromResult(response);
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder Respond(Func<TRequest, TResponse> factory)
    {
        ThrowIfFinalized();
        ArgumentNullException.ThrowIfNull(factory);
        UnaryHandler<TRequest, TResponse> handler = (request, _) => Task.FromResult(factory(request));
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder Respond(Func<TRequest, ServerCallContext, Task<TResponse>> factory)
    {
        ThrowIfFinalized();
        ArgumentNullException.ThrowIfNull(factory);
        UnaryHandler<TRequest, TResponse> handler = (request, context) => factory(request, context);
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder Fail(StatusCode statusCode, string? detail = null)
    {
        ThrowIfFinalized();
        ValidateFailureStatus(statusCode);
        UnaryHandler<TRequest, TResponse> handler = (_, _) =>
            throw new RpcException(new Status(statusCode, detail ?? string.Empty));
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder Fail(RpcException exception)
    {
        ThrowIfFinalized();
        ArgumentNullException.ThrowIfNull(exception);
        UnaryHandler<TRequest, TResponse> handler = (_, _) => throw exception;
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder RespondInSequence(params TResponse[] responses)
    {
        ThrowIfFinalized();
        ArgumentNullException.ThrowIfNull(responses);
        if (responses.Length == 0)
        {
            throw new ArgumentException("At least one response is required.", nameof(responses));
        }

        var index = -1;
        var last = responses.Length - 1;
        UnaryHandler<TRequest, TResponse> handler = (_, _) =>
        {
            var i = Interlocked.Increment(ref index);
            return Task.FromResult(responses[Math.Min(i, last)]);
        };
        return Register(handler);
    }

    private IGrpcMockBuilder Register(UnaryHandler<TRequest, TResponse> handler)
    {
        return Complete(MethodType.Unary, typeof(TResponse), handler);
    }
}
