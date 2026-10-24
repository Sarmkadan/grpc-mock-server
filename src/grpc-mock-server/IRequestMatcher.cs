using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Reusable request matcher. Implement this to share matching logic across stubs
/// (e.g. "request has a non-empty tenant id") or to get a readable description in
/// verification failure messages.
/// </summary>
/// <typeparam name="TRequest">Request message type.</typeparam>
public interface IRequestMatcher<in TRequest>
{
    /// <summary>Human-readable description used in diagnostics, e.g. <c>Id == 42</c>.</summary>
    string Description { get; }

    /// <summary>Returns <see langword="true"/> if the request satisfies this matcher.</summary>
    /// <param name="request">Deserialized request message.</param>
    /// <param name="headers">Request headers (initial metadata).</param>
    bool Matches(TRequest request, Metadata headers);
}

/// <summary>Factory helpers for common matchers.</summary>
public static class RequestMatcher
{
    /// <summary>Matches every request.</summary>
    /// <typeparam name="TRequest">Request message type.</typeparam>
    public static IRequestMatcher<TRequest> Any<TRequest>() => new AnyMatcher<TRequest>();

    /// <summary>Wraps a predicate with a description.</summary>
    /// <typeparam name="TRequest">Request message type.</typeparam>
    /// <param name="description">Description shown in diagnostics.</param>
    /// <param name="predicate">Predicate evaluated against the request.</param>
    public static IRequestMatcher<TRequest> Where<TRequest>(string description, Func<TRequest, bool> predicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(predicate);
        return new PredicateMatcher<TRequest>(description, predicate);
    }

    /// <summary>
    /// Matches when the request is protobuf-equal to <paramref name="expected"/>
    /// (uses <see cref="Google.Protobuf.IMessage"/> equality semantics).
    /// </summary>
    /// <typeparam name="TRequest">Request message type.</typeparam>
    /// <param name="expected">Expected request.</param>
    public static IRequestMatcher<TRequest> Equals<TRequest>(TRequest expected)
        where TRequest : Google.Protobuf.IMessage<TRequest>
    {
        ArgumentNullException.ThrowIfNull(expected);
        return new EqualsMatcher<TRequest>(expected);
    }

    /// <summary>Combines matchers with logical AND.</summary>
    /// <typeparam name="TRequest">Request message type.</typeparam>
    /// <param name="matchers">Matchers that must all match.</param>
    public static IRequestMatcher<TRequest> All<TRequest>(params IRequestMatcher<TRequest>[] matchers)
    {
        ArgumentNullException.ThrowIfNull(matchers);
        return new AllMatcher<TRequest>(matchers.ToArray());
    }

    /// <summary>Combines matchers with logical OR.</summary>
    /// <typeparam name="TRequest">Request message type.</typeparam>
    /// <param name="matchers">Matchers of which at least one must match.</param>
    public static IRequestMatcher<TRequest> AnyOf<TRequest>(params IRequestMatcher<TRequest>[] matchers)
    {
        ArgumentNullException.ThrowIfNull(matchers);
        return new AnyOfMatcher<TRequest>(matchers.ToArray());
    }
}
