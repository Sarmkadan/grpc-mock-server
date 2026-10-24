using System.Reflection;
using Google.Protobuf;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Registers gRPC service methods at runtime by implementing
/// <see cref="IServiceMethodProvider{MockGrpcService}"/>. Each method registered in the
/// <see cref="StubRegistry"/> is bound to a handler that evaluates stubs and records calls.
/// No generated <c>ServiceBase</c> class is required.
/// </summary>
internal sealed class DynamicServiceMethodProvider : IServiceMethodProvider<MockGrpcService>
{
    private readonly StubRegistry _registry;
    private readonly RequestLog _log;
    private readonly GrpcMockOptions _options;

    private static readonly MethodInfo BindMethod =
        typeof(DynamicServiceMethodProvider)
            .GetMethod(nameof(Bind), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public DynamicServiceMethodProvider(StubRegistry registry, RequestLog log, GrpcMockOptions options)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public void OnServiceMethodDiscovery(ServiceMethodProviderContext<MockGrpcService> context)
    {
        foreach (var (fullMethodName, (type, requestType, responseType)) in _registry.Methods)
        {
            var generic = BindMethod.MakeGenericMethod(requestType, responseType);
            generic.Invoke(this, new object[] { context, fullMethodName, type });
        }
    }

    private void Bind<TRequest, TResponse>(
        ServiceMethodProviderContext<MockGrpcService> context,
        string fullMethodName, MethodType methodType)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        var method = DynamicMethodFactory.CreateMethod<TRequest, TResponse>(
            fullMethodName, methodType);

        var metadata = new List<object>();

        switch (methodType)
        {
            case MethodType.Unary:
                context.AddUnaryMethod(method, metadata, CreateUnaryHandler<TRequest, TResponse>(fullMethodName, methodType));
                break;

            case MethodType.ServerStreaming:
                context.AddServerStreamingMethod(method, metadata, CreateServerStreamingHandler<TRequest, TResponse>(fullMethodName, methodType));
                break;

            case MethodType.ClientStreaming:
                context.AddClientStreamingMethod(method, metadata, CreateClientStreamingHandler<TRequest, TResponse>(fullMethodName, methodType));
                break;

            case MethodType.DuplexStreaming:
                context.AddDuplexStreamingMethod(method, metadata, CreateDuplexStreamingHandler<TRequest, TResponse>(fullMethodName, methodType));
                break;
        }
    }

    private UnaryServerMethod<MockGrpcService, TRequest, TResponse> CreateUnaryHandler<TRequest, TResponse>(
        string fullMethodName, MethodType methodType)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        return async (_, request, context) =>
        {
            var headers = context.RequestHeaders;
            var stub = _registry.FindMatch(fullMethodName, request, headers);

            if (stub is null)
            {
                RecordCall<TRequest>(fullMethodName, methodType, request, _registry.UnmatchedStatus, matched: false);
                throw new RpcException(_registry.UnmatchedStatus);
            }

            if (stub.Delay > TimeSpan.Zero)
            {
                await Task.Delay(stub.Delay, context.CancellationToken);
            }

            if (stub.ResponseHeaders is { Count: > 0 } responseHeaders)
            {
                await context.WriteResponseHeadersAsync(responseHeaders);
            }

            foreach (var trailer in stub.ResponseTrailers ?? Metadata.Empty)
            {
                context.ResponseTrailers.Add(trailer);
            }

            var handler = (UnaryHandler<TRequest, TResponse>)stub.Handler;
            var response = await handler(request, context);

            RecordCall<TRequest>(fullMethodName, methodType, request, new Status(StatusCode.OK, string.Empty), matched: true);

            return response;
        };
    }

    private ServerStreamingServerMethod<MockGrpcService, TRequest, TResponse> CreateServerStreamingHandler<TRequest, TResponse>(
        string fullMethodName, MethodType methodType)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        return async (_, request, responseStream, context) =>
        {
            var headers = context.RequestHeaders;
            var stub = _registry.FindMatch(fullMethodName, request, headers);

            if (stub is null)
            {
                RecordCall<TRequest>(fullMethodName, methodType, request, _registry.UnmatchedStatus, matched: false);
                throw new RpcException(_registry.UnmatchedStatus);
            }

            if (stub.Delay > TimeSpan.Zero)
            {
                await Task.Delay(stub.Delay, context.CancellationToken);
            }

            if (stub.ResponseHeaders is { Count: > 0 } responseHeaders)
            {
                await context.WriteResponseHeadersAsync(responseHeaders);
            }

            foreach (var trailer in stub.ResponseTrailers ?? Metadata.Empty)
            {
                context.ResponseTrailers.Add(trailer);
            }

            var handler = (ServerStreamingHandler<TRequest, TResponse>)stub.Handler;
            await handler(request, responseStream, context);

            RecordCall<TRequest>(fullMethodName, methodType, request, new Status(StatusCode.OK, string.Empty), matched: true);
        };
    }

    private ClientStreamingServerMethod<MockGrpcService, TRequest, TResponse> CreateClientStreamingHandler<TRequest, TResponse>(
        string fullMethodName, MethodType methodType)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        return async (_, requestStream, context) =>
        {
            var headers = context.RequestHeaders;

            // Stubs are matched against the first message only (null for an empty stream); the rest of
            // the stream is drained after a stub has matched, so the handler receives every message.
            var requests = new List<TRequest>();
            if (await requestStream.MoveNext(context.CancellationToken))
            {
                requests.Add(requestStream.Current);
            }

            var firstMessage = requests.Count > 0 ? requests[0] : null;
            var stub = _registry.FindMatch(fullMethodName, firstMessage, headers);

            if (stub is null)
            {
                RecordCall<TRequest>(fullMethodName, methodType, firstMessage, _registry.UnmatchedStatus, matched: false);
                throw new RpcException(_registry.UnmatchedStatus);
            }

            if (stub.Delay > TimeSpan.Zero)
            {
                await Task.Delay(stub.Delay, context.CancellationToken);
            }

            if (stub.ResponseHeaders is { Count: > 0 } responseHeaders)
            {
                await context.WriteResponseHeadersAsync(responseHeaders);
            }

            foreach (var trailer in stub.ResponseTrailers ?? Metadata.Empty)
            {
                context.ResponseTrailers.Add(trailer);
            }

            while (await requestStream.MoveNext(context.CancellationToken))
            {
                requests.Add(requestStream.Current);
            }

            var handler = (ClientStreamingHandler<TRequest, TResponse>)stub.Handler;
            var response = await handler(requests, context);

            RecordCall<TRequest>(fullMethodName, methodType, firstMessage, new Status(StatusCode.OK, string.Empty), matched: true);

            return response;
        };
    }

    private DuplexStreamingServerMethod<MockGrpcService, TRequest, TResponse> CreateDuplexStreamingHandler<TRequest, TResponse>(
        string fullMethodName, MethodType methodType)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        return async (_, requestStream, responseStream, context) =>
        {
            var headers = context.RequestHeaders;

            // For duplex streaming, read the first message or pass null for initial match.
            TRequest? firstMessage = null;
            if (await requestStream.MoveNext(context.CancellationToken))
            {
                firstMessage = requestStream.Current;
            }

            var stub = _registry.FindMatch(fullMethodName, firstMessage, headers);

            if (stub is null)
            {
                RecordCall<TRequest>(fullMethodName, methodType, firstMessage, _registry.UnmatchedStatus, matched: false);
                throw new RpcException(_registry.UnmatchedStatus);
            }

            if (stub.Delay > TimeSpan.Zero)
            {
                await Task.Delay(stub.Delay, context.CancellationToken);
            }

            if (stub.ResponseHeaders is { Count: > 0 } responseHeaders)
            {
                await context.WriteResponseHeadersAsync(responseHeaders);
            }

            foreach (var trailer in stub.ResponseTrailers ?? Metadata.Empty)
            {
                context.ResponseTrailers.Add(trailer);
            }

            var handler = (DuplexStreamingHandler<TRequest, TResponse>)stub.Handler;

            // Wrap the stream so the handler sees the first message we already consumed.
            var wrappedStream = new PrependedStreamReader<TRequest>(firstMessage, requestStream);
            await handler(wrappedStream, responseStream, context);

            RecordCall<TRequest>(fullMethodName, methodType, firstMessage, new Status(StatusCode.OK, string.Empty), matched: true);
        };
    }

    private void RecordCall<TRequest>(string fullMethodName, MethodType methodType, TRequest? request, Status status, bool matched)
        where TRequest : class, IMessage<TRequest>, new()
    {
        if (!_options.RecordRequests)
        {
            return;
        }

        var payloads = new List<ReadOnlyMemory<byte>>();
        if (request is not null)
        {
            payloads.Add(DynamicMethodFactory.Serialize(request));
        }

        _log.Record(new RecordedCall(
            fullMethodName,
            methodType,
            new Metadata(),
            payloads.AsReadOnly(),
            status,
            matched,
            DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Wraps an <see cref="IAsyncStreamReader{T}"/> to prepend a message that was already
    /// consumed for stub matching in client/duplex streaming scenarios.
    /// </summary>
    private sealed class PrependedStreamReader<T> : IAsyncStreamReader<T>
    {
        private readonly IAsyncStreamReader<T> _inner;
        private T? _prepended;
        private bool _prependConsumed;

        public PrependedStreamReader(T? prepended, IAsyncStreamReader<T> inner)
        {
            _prepended = prepended;
            _prependConsumed = prepended is null;
            _inner = inner;
        }

        public T Current { get; private set; } = default!;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (!_prependConsumed)
            {
                _prependConsumed = true;
                Current = _prepended!;
                _prepended = default;
                return true;
            }

            if (await _inner.MoveNext(cancellationToken))
            {
                Current = _inner.Current;
                return true;
            }

            return false;
        }
    }
}
