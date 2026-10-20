using Grpc.Core;

namespace GrpcMockServer;

/// <summary>Result of a successful stub match, carrying the response configuration.</summary>
internal sealed class MatchedStub
{
    /// <summary>Artificial delay before the response is sent.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>Response headers (initial metadata) to send.</summary>
    public Metadata ResponseHeaders { get; init; } = Metadata.Empty;

    /// <summary>Response trailers to attach.</summary>
    public Metadata ResponseTrailers { get; init; } = Metadata.Empty;

    /// <summary>
    /// The handler delegate. Cast to the appropriate <c>UnaryHandler&lt;,&gt;</c>,
    /// <c>ServerStreamingHandler&lt;,&gt;</c>, etc. based on method type.
    /// </summary>
    public Delegate Handler { get; init; } = null!;
}
