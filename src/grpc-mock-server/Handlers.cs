using Grpc.Core;

namespace GrpcMockServer;

/// <summary>Handler delegates invoked when a stub matches an incoming gRPC call.</summary>
internal delegate Task<TResponse> UnaryHandler<TRequest, TResponse>(
    TRequest request, ServerCallContext context)
    where TRequest : class where TResponse : class;

/// <summary>Server-streaming handler delegate.</summary>
internal delegate Task ServerStreamingHandler<TRequest, TResponse>(
    TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context)
    where TRequest : class where TResponse : class;

/// <summary>Client-streaming handler delegate. Receives every request message the client streamed.</summary>
internal delegate Task<TResponse> ClientStreamingHandler<TRequest, TResponse>(
    IReadOnlyList<TRequest> requests, ServerCallContext context)
    where TRequest : class where TResponse : class;

/// <summary>Duplex-streaming handler delegate.</summary>
internal delegate Task DuplexStreamingHandler<TRequest, TResponse>(
    IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream,
    ServerCallContext context)
    where TRequest : class where TResponse : class;
