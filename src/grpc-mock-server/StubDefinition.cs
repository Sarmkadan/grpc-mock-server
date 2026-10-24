using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Internal representation of a single stub registered via the fluent builder.
/// Holds the method signature, the matching predicate, the response configuration and
/// the remaining-use counter.
/// </summary>
internal sealed class StubDefinition
{
    /// <summary>Full gRPC method name in the form <c>package.Service/Method</c>.</summary>
    public required string FullMethodName { get; init; }

    /// <summary>gRPC method type (Unary, ServerStreaming, etc.).</summary>
    public required MethodType Type { get; init; }

    /// <summary>CLR type of the request message.</summary>
    public required Type RequestType { get; init; }

    /// <summary>CLR type of the response message.</summary>
    public required Type ResponseType { get; init; }

    /// <summary>Priority for evaluation order; higher wins.</summary>
    public int Priority { get; init; }

    /// <summary>Remaining matches before the stub is removed; null means unlimited.</summary>
    public int? RemainingTimes { get; set; }

    /// <summary>Artificial delay before responding.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>Response headers (initial metadata); null when none were configured.</summary>
    public Metadata? ResponseHeaders { get; init; }

    /// <summary>Response trailers; null when none were configured.</summary>
    public Metadata? ResponseTrailers { get; init; }

    /// <summary>
    /// Evaluates the request object and request headers. Returns true when the stub applies.
    /// The request is null for client/duplex streaming calls that have not yet read a message.
    /// </summary>
    public required Func<object?, Metadata, bool> Matches { get; init; }

    /// <summary>Descriptions of each matcher, used in verification diagnostics.</summary>
    public IReadOnlyList<string> MatcherDescriptions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The typed response delegate, boxed. The service binder casts it to the handler delegate
    /// that matches <see cref="Type"/>.
    /// </summary>
    public required object Handler { get; init; }
}
