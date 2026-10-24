using Google.Protobuf;
using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Default implementation of <see cref="IGrpcMockBuilder"/> that accumulates stub definitions
/// and method entries into a <see cref="StubRegistry"/>.
/// </summary>
internal sealed partial class GrpcMockBuilder : IGrpcMockBuilder
{
    private readonly StubRegistry _registry;

    public GrpcMockBuilder(StubRegistry registry)
    {
        _registry = registry;
    }

    /// <inheritdoc />
    public IUnaryStubBuilder<TRequest, TResponse> ForUnary<TRequest, TResponse>(string fullMethodName)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullMethodName);
        MethodNames.Validate(fullMethodName);
        _registry.RegisterMethod(fullMethodName, MethodType.Unary, typeof(TRequest), typeof(TResponse));
        return new UnaryStubBuilder<TRequest, TResponse>(this, _registry, fullMethodName);
    }

    /// <inheritdoc />
    public IServerStreamingStubBuilder<TRequest, TResponse> ForServerStreaming<TRequest, TResponse>(string fullMethodName)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullMethodName);
        MethodNames.Validate(fullMethodName);
        _registry.RegisterMethod(fullMethodName, MethodType.ServerStreaming, typeof(TRequest), typeof(TResponse));
        return new ServerStreamingStubBuilder<TRequest, TResponse>(this, _registry, fullMethodName);
    }

    /// <inheritdoc />
    public IClientStreamingStubBuilder<TRequest, TResponse> ForClientStreaming<TRequest, TResponse>(string fullMethodName)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullMethodName);
        MethodNames.Validate(fullMethodName);
        _registry.RegisterMethod(fullMethodName, MethodType.ClientStreaming, typeof(TRequest), typeof(TResponse));
        return new ClientStreamingStubBuilder<TRequest, TResponse>(this, _registry, fullMethodName);
    }

    /// <inheritdoc />
    public IDuplexStreamingStubBuilder<TRequest, TResponse> ForDuplexStreaming<TRequest, TResponse>(string fullMethodName)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullMethodName);
        MethodNames.Validate(fullMethodName);
        _registry.RegisterMethod(fullMethodName, MethodType.DuplexStreaming, typeof(TRequest), typeof(TResponse));
        return new DuplexStreamingStubBuilder<TRequest, TResponse>(this, _registry, fullMethodName);
    }

    /// <inheritdoc />
    public IUnaryStubBuilder<byte[], byte[]> ForRaw(string fullMethodName, MethodType type = MethodType.Unary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullMethodName);
        MethodNames.Validate(fullMethodName);
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Must be a defined MethodType value.");
        }

        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public IGrpcMockBuilder OnUnmatched(Status status)
    {
        if (status.StatusCode == StatusCode.OK)
        {
            throw new ArgumentException("An unmatched call cannot succeed; use a non-OK status code.", nameof(status));
        }

        _registry.UnmatchedStatus = status;
        return this;
    }
}
