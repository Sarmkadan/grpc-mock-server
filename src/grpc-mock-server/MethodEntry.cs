using Grpc.Core;

namespace GrpcMockServer;

/// <summary>Describes a single registered gRPC method in the stub registry.</summary>
internal sealed class MethodEntry
{
    /// <summary>Full method name in the form <c>package.Service/Method</c>.</summary>
    public required string FullMethodName { get; init; }

    /// <summary>gRPC method type (Unary, ServerStreaming, etc.).</summary>
    public required MethodType Type { get; init; }

    /// <summary>CLR type of the request message.</summary>
    public required Type RequestType { get; init; }

    /// <summary>CLR type of the response message.</summary>
    public required Type ResponseType { get; init; }
}
