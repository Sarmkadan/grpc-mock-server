using Microsoft.Extensions.Logging;

namespace GrpcMockServer;

/// <summary>Server configuration.</summary>
public sealed class GrpcMockOptions
{
    /// <summary>Default options: TestHost mode, loopback, no TLS, request recording enabled.</summary>
    public static GrpcMockOptions Default => new();

    /// <summary>
    /// When <see langword="true"/> (default) the server is hosted with
    /// <c>Microsoft.AspNetCore.TestHost</c> and channels created via
    /// <see cref="GrpcMockServer.CreateChannel"/> bypass the network. Set to <see langword="false"/>
    /// to listen on a real socket so out-of-process clients (or other languages) can connect.
    /// </summary>
    public bool UseTestHost { get; set; } = true;

    /// <summary>Port to bind when <see cref="UseTestHost"/> is <see langword="false"/>. 0 picks a free port.</summary>
    public int Port { get; set; }

    /// <summary>Host to bind when <see cref="UseTestHost"/> is <see langword="false"/>.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Whether calls are recorded into <see cref="GrpcMockServer.Requests"/>.</summary>
    public bool RecordRequests { get; set; } = true;

    /// <summary>Maximum number of recorded calls kept in memory; oldest are dropped first.</summary>
    public int MaxRecordedCalls { get; set; } = 10_000;

    /// <summary>Optional logger factory for server diagnostics (matched/unmatched calls).</summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>Maximum inbound message size in bytes. <see langword="null"/> uses the gRPC default (4 MB).</summary>
    public int? MaxReceiveMessageSize { get; set; }

    /// <summary>Enables gRPC server reflection so tools like <c>grpcurl</c> can list stubbed methods.</summary>
    public bool EnableReflection { get; set; }
}
