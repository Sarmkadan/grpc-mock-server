using Google.Protobuf;
using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Shared implementation of the <see cref="IStubBuilder{TRequest, TSelf}"/> steps. Accumulates
/// request matchers, header matchers, priority, delay, metadata and the remaining-use limit,
/// then composes them into a <see cref="StubDefinition"/> once a response strategy is chosen.
/// </summary>
/// <typeparam name="TRequest">Request message type.</typeparam>
/// <typeparam name="TSelf">Concrete builder type returned for chaining.</typeparam>
internal abstract class StubBuilderBase<TRequest, TSelf> : IStubBuilder<TRequest, TSelf>
    where TSelf : IStubBuilder<TRequest, TSelf>
{
    /// <summary>Root builder that owns the stub registry.</summary>
    protected GrpcMockBuilder Root { get; }

    /// <summary>Registry the finalized stubs are added to.</summary>
    protected StubRegistry Registry { get; }

    /// <summary>Full gRPC method name in the form <c>package.Service/Method</c>.</summary>
    protected string FullMethodName { get; }

    /// <summary>Request matchers that must all match.</summary>
    protected List<IRequestMatcher<TRequest>> Matchers { get; } = new();

    /// <summary>Header key and value predicates that must all match (keys compared case-insensitively).</summary>
    protected List<(string Key, Func<string, bool> Predicate)> HeaderMatchers { get; } = new();

    /// <summary>Evaluation priority; higher-priority stubs are evaluated first.</summary>
    protected int Priority { get; set; }

    /// <summary>Remaining match limit; null means unlimited.</summary>
    protected int? TimesLimit { get; set; }

    /// <summary>Artificial delay before responding.</summary>
    protected TimeSpan Delay { get; set; }

    /// <summary>Response headers (initial metadata); null when none were configured.</summary>
    protected Metadata? ResponseHeaders { get; set; }

    /// <summary>Response trailers; null when none were configured.</summary>
    protected Metadata? ResponseTrailers { get; set; }

    /// <summary>
    /// Creates a builder bound to a method. The concrete builder returns <see cref="Self"/>
    /// from every chaining step.
    /// </summary>
    /// <param name="root">Root builder that owns the stub registry.</param>
    /// <param name="registry">Registry the finalized stubs are added to.</param>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    protected StubBuilderBase(GrpcMockBuilder root, StubRegistry registry, string fullMethodName)
    {
        Root = root;
        Registry = registry;
        FullMethodName = fullMethodName;
    }

    /// <summary>True once a response finalizer has registered the stub; the builder then rejects every further call.</summary>
    private bool _finalized;

    /// <summary>The concrete builder instance, returned from chaining steps.</summary>
    protected abstract TSelf Self { get; }

    /// <summary>
    /// Throws when a response finalizer has already registered this builder's stub.
    /// Every public step calls this first so a finalized builder cannot be modified or registered twice.
    /// </summary>
    /// <exception cref="InvalidOperationException">The stub has already been finalized.</exception>
    protected void ThrowIfFinalized()
    {
        if (_finalized)
        {
            throw new InvalidOperationException("Stub already finalized");
        }
    }

    /// <summary>
    /// Rejects status codes that cannot describe a failed call. Used by the <c>Fail</c> finalizers.
    /// </summary>
    /// <param name="statusCode">Status the stub would fail with.</param>
    /// <exception cref="ArgumentException"><paramref name="statusCode"/> is <see cref="StatusCode.OK"/>.</exception>
    protected static void ValidateFailureStatus(StatusCode statusCode)
    {
        if (statusCode == StatusCode.OK)
        {
            throw new ArgumentException("A failing stub cannot use StatusCode.OK; use a non-OK status code.", nameof(statusCode));
        }
    }

    /// <inheritdoc />
    public TSelf When(Func<TRequest, bool> predicate)
    {
        ThrowIfFinalized();
        ArgumentNullException.ThrowIfNull(predicate);
        OnRequestMatcherAdded();
        Matchers.Add(RequestMatcher.Where("predicate", predicate));
        return Self;
    }

    /// <inheritdoc />
    public TSelf When(IRequestMatcher<TRequest> matcher)
    {
        ThrowIfFinalized();
        ArgumentNullException.ThrowIfNull(matcher);
        OnRequestMatcherAdded();
        Matchers.Add(matcher);
        return Self;
    }

    /// <summary>
    /// Invoked before a request matcher is added by <c>When(...)</c>. Derived builders whose call shape
    /// cannot evaluate request matchers override this to reject them. The default accepts every matcher.
    /// </summary>
    protected virtual void OnRequestMatcherAdded()
    {
    }

    /// <inheritdoc />
    public TSelf WithHeader(string key, string value)
    {
        ThrowIfFinalized();
        ValidateHeaderKey(key);
        ArgumentNullException.ThrowIfNull(value);
        HeaderMatchers.Add((key, actual => string.Equals(actual, value, StringComparison.Ordinal)));
        return Self;
    }

    /// <inheritdoc />
    public TSelf WithHeader(string key, Func<string, bool> predicate)
    {
        ThrowIfFinalized();
        ValidateHeaderKey(key);
        ArgumentNullException.ThrowIfNull(predicate);
        HeaderMatchers.Add((key, predicate));
        return Self;
    }

    /// <inheritdoc />
    public TSelf WithPriority(int priority)
    {
        ThrowIfFinalized();
        Priority = priority;
        return Self;
    }

    /// <inheritdoc />
    public TSelf WithDelay(TimeSpan delay)
    {
        ThrowIfFinalized();
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "Delay must not be negative.");
        }

        Delay = delay;
        return Self;
    }

    /// <inheritdoc />
    public TSelf WithResponseHeaders(Metadata headers)
    {
        ThrowIfFinalized();
        ArgumentNullException.ThrowIfNull(headers);
        ResponseHeaders = headers;
        return Self;
    }

    /// <inheritdoc />
    public TSelf WithResponseTrailers(Metadata trailers)
    {
        ThrowIfFinalized();
        ArgumentNullException.ThrowIfNull(trailers);
        foreach (var entry in trailers)
        {
            if (entry.Key.StartsWith("grpc-", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Trailer key '{entry.Key}' is reserved for gRPC; 'grpc-' keys cannot be set with WithResponseTrailers.",
                    nameof(trailers));
            }
        }

        ResponseTrailers = trailers;
        return Self;
    }

    /// <inheritdoc />
    public TSelf Times(int count)
    {
        ThrowIfFinalized();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        TimesLimit = count;
        return Self;
    }

    /// <summary>
    /// Composes the accumulated stub, adds it to the registry and marks this builder finalized.
    /// The flag is set only after <see cref="StubRegistry.Add"/> succeeds, so a rejected stub leaves the builder usable.
    /// </summary>
    /// <param name="type">gRPC method type.</param>
    /// <param name="responseType">CLR type of the response message.</param>
    /// <param name="handler">Typed response delegate, boxed.</param>
    /// <returns>The root builder, for chaining back to method declarations.</returns>
    protected IGrpcMockBuilder Complete(MethodType type, Type responseType, object handler)
    {
        Registry.Add(Build(type, responseType, handler));
        _finalized = true;
        return Root;
    }

    /// <summary>
    /// Composes the accumulated matchers into a <see cref="StubDefinition"/>. The request matchers
    /// and header matchers are combined with logical AND. Binary header entries never match.
    /// </summary>
    /// <param name="type">gRPC method type.</param>
    /// <param name="responseType">CLR type of the response message.</param>
    /// <param name="handler">Typed response delegate, boxed.</param>
    protected StubDefinition Build(MethodType type, Type responseType, object handler)
    {
        var matchers = Matchers.ToArray();
        var headerMatchers = HeaderMatchers.ToArray();

        var descriptions = matchers
            .Select(m => m.Description)
            .Concat(headerMatchers.Select(h => $"header[{h.Key}]"))
            .ToArray();

        return new StubDefinition
        {
            FullMethodName = FullMethodName,
            Type = type,
            RequestType = typeof(TRequest),
            ResponseType = responseType,
            Priority = Priority,
            RemainingTimes = TimesLimit,
            Delay = Delay,
            ResponseHeaders = ResponseHeaders,
            ResponseTrailers = ResponseTrailers,
            Matches = (request, headers) => MatchesAll(matchers, headerMatchers, request, headers),
            MatcherDescriptions = descriptions,
            Handler = handler,
        };
    }

    private static void ValidateHeaderKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.EndsWith("-bin", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Header '{key}' is a binary ('-bin') header and cannot be matched by string value.",
                nameof(key));
        }
    }

    private static bool MatchesAll(
        IReadOnlyList<IRequestMatcher<TRequest>> matchers,
        IReadOnlyList<(string Key, Func<string, bool> Predicate)> headerMatchers,
        object? request,
        Metadata headers)
    {
        // A null request means there is nothing to evaluate yet (client streaming before the first
        // message, or an empty stream), so request matchers are skipped and only headers decide.
        if (request is not null)
        {
            if (request is not TRequest typed)
            {
                return false;
            }

            foreach (var matcher in matchers)
            {
                if (!matcher.Matches(typed, headers))
                {
                    return false;
                }
            }
        }

        foreach (var (key, valuePredicate) in headerMatchers)
        {
            var entry = headers.FirstOrDefault(e =>
                !e.IsBinary && string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
            if (entry is null || !valuePredicate(entry.Value))
            {
                return false;
            }
        }

        return true;
    }
}
