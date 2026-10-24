using Google.Protobuf;
using Grpc.Core;

namespace GrpcMockServer;

/// <summary>Matches every request. Backs <see cref="RequestMatcher.Any{TRequest}"/>.</summary>
/// <typeparam name="T">Request message type.</typeparam>
internal sealed class AnyMatcher<T> : IRequestMatcher<T>
{
    /// <inheritdoc />
    public string Description => "any";

    /// <inheritdoc />
    public bool Matches(T request, Metadata headers) => true;
}

/// <summary>Matches requests accepted by a predicate. Backs <see cref="RequestMatcher.Where{TRequest}"/>.</summary>
/// <typeparam name="T">Request message type.</typeparam>
internal sealed class PredicateMatcher<T> : IRequestMatcher<T>
{
    private readonly Func<T, bool> _predicate;

    /// <summary>Creates a matcher that evaluates <paramref name="predicate"/> against each request.</summary>
    /// <param name="description">Description shown in diagnostics.</param>
    /// <param name="predicate">Predicate evaluated against the request.</param>
    public PredicateMatcher(string description, Func<T, bool> predicate)
    {
        Description = description;
        _predicate = predicate;
    }

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public bool Matches(T request, Metadata headers) => _predicate(request);
}

/// <summary>Matches protobuf-equal requests. Backs <see cref="RequestMatcher.Equals{TRequest}"/>.</summary>
/// <typeparam name="T">Request message type.</typeparam>
internal sealed class EqualsMatcher<T> : IRequestMatcher<T>
    where T : IMessage<T>
{
    private readonly T _expected;

    /// <summary>Creates a matcher that compares requests to <paramref name="expected"/> by protobuf equality.</summary>
    /// <param name="expected">Expected request.</param>
    public EqualsMatcher(T expected)
    {
        _expected = expected;
    }

    /// <inheritdoc />
    public string Description => "== " + _expected.ToString();

    /// <inheritdoc />
    public bool Matches(T request, Metadata headers) => _expected.Equals(request);
}

/// <summary>Matches when all child matchers match. Backs <see cref="RequestMatcher.All{TRequest}"/>.</summary>
/// <typeparam name="T">Request message type.</typeparam>
internal sealed class AllMatcher<T> : IRequestMatcher<T>
{
    private readonly IRequestMatcher<T>[] _matchers;

    /// <summary>Creates a matcher that requires every child matcher to match.</summary>
    /// <param name="matchers">Child matchers. An empty array matches every request.</param>
    public AllMatcher(IRequestMatcher<T>[] matchers)
    {
        _matchers = matchers;
    }

    /// <inheritdoc />
    public string Description => string.Join(" AND ", _matchers.Select(m => m.Description));

    /// <inheritdoc />
    public bool Matches(T request, Metadata headers)
    {
        foreach (var matcher in _matchers)
        {
            if (!matcher.Matches(request, headers))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Matches when at least one child matcher matches. Backs <see cref="RequestMatcher.AnyOf{TRequest}"/>.</summary>
/// <typeparam name="T">Request message type.</typeparam>
internal sealed class AnyOfMatcher<T> : IRequestMatcher<T>
{
    private readonly IRequestMatcher<T>[] _matchers;

    /// <summary>Creates a matcher that requires at least one child matcher to match.</summary>
    /// <param name="matchers">Child matchers. An empty array matches no request.</param>
    public AnyOfMatcher(IRequestMatcher<T>[] matchers)
    {
        _matchers = matchers;
    }

    /// <inheritdoc />
    public string Description => string.Join(" OR ", _matchers.Select(m => m.Description));

    /// <inheritdoc />
    public bool Matches(T request, Metadata headers)
    {
        foreach (var matcher in _matchers)
        {
            if (matcher.Matches(request, headers))
            {
                return true;
            }
        }

        return false;
    }
}
