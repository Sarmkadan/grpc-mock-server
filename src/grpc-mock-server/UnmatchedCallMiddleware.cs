using System.Globalization;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GrpcMockServer;

/// <summary>
/// Fallback handler for gRPC calls to methods that were never declared. Such calls have no endpoint and would
/// otherwise fall through ASP.NET Core routing as HTTP 404. This handler records the call as unmatched and answers
/// with a trailers-only gRPC response carrying <see cref="StubRegistry.UnmatchedStatus"/>.
/// </summary>
internal sealed class UnmatchedCallMiddleware
{
    private const string _grpcContentType = "application/grpc";

    private readonly StubRegistry _registry;
    private readonly RequestLog _log;
    private readonly bool _recordRequests;
    private readonly ILogger? _logger;

    /// <summary>Creates the fallback handler.</summary>
    /// <param name="registry">Registry supplying the unmatched status.</param>
    /// <param name="log">Log that receives the unmatched call.</param>
    /// <param name="options">Server options; <see cref="GrpcMockOptions.LoggerFactory"/> enables warnings.</param>
    public UnmatchedCallMiddleware(StubRegistry registry, RequestLog log, GrpcMockOptions options)
    {
        _registry = registry;
        _log = log;
        _recordRequests = options.RecordRequests;
        _logger = options.LoggerFactory?.CreateLogger<UnmatchedCallMiddleware>();
    }

    /// <summary>
    /// Handles a request matched by the <c>/{service}/{method}</c> fallback route. Requests without a gRPC
    /// content type are not gRPC calls and keep the HTTP 404 response.
    /// </summary>
    /// <param name="context">The HTTP context of the request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;

        if (request.ContentType is null || !request.ContentType.StartsWith(_grpcContentType, StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var fullMethodName = request.Path.Value![1..];
        var status = _registry.UnmatchedStatus;

        RecordCall(fullMethodName, MetadataConversions.ToMetadata(request.Headers), status);

        _logger?.LogWarning("Unmatched gRPC call {Method}", fullMethodName);

        // Trailers-only response: grpc-status and grpc-message travel in the headers, with no body.
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = _grpcContentType;
        response.Headers["grpc-status"] = ((int)status.StatusCode).ToString(CultureInfo.InvariantCulture);
        response.Headers["grpc-message"] = Uri.EscapeDataString(status.Detail);

        await response.CompleteAsync();
    }

    private void RecordCall(string fullMethodName, Metadata headers, Status status)
    {
        if (!_recordRequests)
        {
            return;
        }

        // The method's type is unknown because it was never declared, so Unary is a placeholder.
        _log.Record(new RecordedCall(
            fullMethodName,
            MethodType.Unary,
            headers,
            Array.Empty<ReadOnlyMemory<byte>>(),
            status,
            Matched: false,
            DateTimeOffset.UtcNow));
    }
}
