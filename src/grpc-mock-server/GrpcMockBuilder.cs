using Google.Protobuf;
using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Default implementation of <see cref="IGrpcMockBuilder"/> that accumulates stub definitions
/// and method entries into a <see cref="StubRegistry"/>.
/// </summary>
internal sealed class GrpcMockBuilder : IGrpcMockBuilder
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
        _registry.RegisterMethod(fullMethodName, MethodType.Unary, typeof(TRequest), typeof(TResponse));
        return new UnaryStubBuilder<TRequest, TResponse>(this, fullMethodName);
    }

    /// <inheritdoc />
    public IServerStreamingStubBuilder<TRequest, TResponse> ForServerStreaming<TRequest, TResponse>(string fullMethodName)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public IClientStreamingStubBuilder<TRequest, TResponse> ForClientStreaming<TRequest, TResponse>(string fullMethodName)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public IDuplexStreamingStubBuilder<TRequest, TResponse> ForDuplexStreaming<TRequest, TResponse>(string fullMethodName)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public IUnaryStubBuilder<byte[], byte[]> ForRaw(string fullMethodName, MethodType type = MethodType.Unary)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public IGrpcMockBuilder OnUnmatched(Status status)
    {
        _registry.UnmatchedStatus = status;
        return this;
    }

    internal void AddStub(StubDefinition stub) => _registry.Add(stub);
}

/// <summary>
/// Fluent builder for unary stubs that accumulates predicates, metadata and finally
/// a response strategy before registering the stub in the registry.
/// </summary>
internal sealed class UnaryStubBuilder<TRequest, TResponse>
    : IUnaryStubBuilder<TRequest, TResponse>
    where TRequest : class, IMessage<TRequest>, new()
    where TResponse : class, IMessage<TResponse>, new()
{
    private readonly GrpcMockBuilder _root;
    private readonly string _fullMethodName;
    private readonly List<Func<object, bool>> _predicates = new();
    private readonly List<(string Key, Func<string, bool> Predicate)> _headerMatchers = new();
    private readonly List<string> _descriptions = new();
    private int _priority;
    private TimeSpan _delay;
    private Metadata _responseHeaders = Metadata.Empty;
    private Metadata _responseTrailers = Metadata.Empty;
    private int? _timesLimit;

    public UnaryStubBuilder(GrpcMockBuilder root, string fullMethodName)
    {
        _root = root;
        _fullMethodName = fullMethodName;
    }

    /// <inheritdoc />
    public IUnaryStubBuilder<TRequest, TResponse> When(Func<TRequest, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _predicates.Add(obj => predicate((TRequest)obj));
        _descriptions.Add("custom predicate");
        return this;
    }

    /// <inheritdoc />
    public IUnaryStubBuilder<TRequest, TResponse> When(IRequestMatcher<TRequest> matcher)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        _predicates.Add(obj => matcher.Matches((TRequest)obj, Metadata.Empty));
        _descriptions.Add(matcher.Description);
        return this;
    }

    /// <inheritdoc />
    public IUnaryStubBuilder<TRequest, TResponse> WithHeader(string key, string value)
    {
        _headerMatchers.Add((key, v => v == value));
        _descriptions.Add($"header '{key}' == '{value}'");
        return this;
    }

    /// <inheritdoc />
    public IUnaryStubBuilder<TRequest, TResponse> WithHeader(string key, Func<string, bool> predicate)
    {
        _headerMatchers.Add((key, predicate));
        _descriptions.Add($"header '{key}' (custom predicate)");
        return this;
    }

    /// <inheritdoc />
    public IUnaryStubBuilder<TRequest, TResponse> WithPriority(int priority)
    {
        _priority = priority;
        return this;
    }

    /// <inheritdoc />
    public IUnaryStubBuilder<TRequest, TResponse> WithDelay(TimeSpan delay)
    {
        _delay = delay;
        return this;
    }

    /// <inheritdoc />
    public IUnaryStubBuilder<TRequest, TResponse> WithResponseHeaders(Metadata headers)
    {
        _responseHeaders = headers;
        return this;
    }

    /// <inheritdoc />
    public IUnaryStubBuilder<TRequest, TResponse> WithResponseTrailers(Metadata trailers)
    {
        _responseTrailers = trailers;
        return this;
    }

    /// <inheritdoc />
    public IUnaryStubBuilder<TRequest, TResponse> Times(int count)
    {
        _timesLimit = count;
        return this;
    }

    /// <inheritdoc />
    public IGrpcMockBuilder Respond(TResponse response)
    {
        UnaryHandler<TRequest, TResponse> handler = (_, _) => Task.FromResult(response);
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder Respond(Func<TRequest, TResponse> factory)
    {
        UnaryHandler<TRequest, TResponse> handler = (req, _) => Task.FromResult(factory(req));
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder Respond(Func<TRequest, ServerCallContext, Task<TResponse>> factory)
    {
        UnaryHandler<TRequest, TResponse> handler = (req, ctx) => factory(req, ctx);
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder Fail(StatusCode statusCode, string? detail = null)
    {
        UnaryHandler<TRequest, TResponse> handler = (_, _) =>
            throw new RpcException(new Status(statusCode, detail ?? string.Empty));
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder Fail(RpcException exception)
    {
        UnaryHandler<TRequest, TResponse> handler = (_, _) => throw exception;
        return Register(handler);
    }

    /// <inheritdoc />
    public IGrpcMockBuilder RespondInSequence(params TResponse[] responses)
    {
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

    private IGrpcMockBuilder Register(Delegate handler)
    {
        var predicates = _predicates.ToList();
        var headerMatchers = _headerMatchers.ToList();

        _root.AddStub(new StubDefinition
        {
            FullMethodName = _fullMethodName,
            Type = MethodType.Unary,
            RequestType = typeof(TRequest),
            ResponseType = typeof(TResponse),
            Priority = _priority,
            RemainingTimes = _timesLimit,
            Delay = _delay,
            ResponseHeaders = _responseHeaders,
            ResponseTrailers = _responseTrailers,
            Matches = (request, headers) => Matches(predicates, headerMatchers, request, headers),
            MatcherDescriptions = _descriptions.ToList().AsReadOnly(),
            Handler = handler
        });
        return _root;
    }

    private static bool Matches(
        IReadOnlyList<Func<object, bool>> predicates,
        IReadOnlyList<(string Key, Func<string, bool> Predicate)> headerMatchers,
        object? request,
        Metadata headers)
    {
        foreach (var predicate in predicates)
        {
            if (request is null || !predicate(request))
            {
                return false;
            }
        }

        foreach (var (key, valuePredicate) in headerMatchers)
        {
            var entry = headers.FirstOrDefault(e =>
                string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
            if (entry is null || !valuePredicate(entry.Value))
            {
                return false;
            }
        }

        return true;
    }
}
