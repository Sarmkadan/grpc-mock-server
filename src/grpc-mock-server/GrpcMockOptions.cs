using System.Net;
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
    [Obsolete("Reflection is not supported; dynamic methods have no descriptors.")]
    public bool EnableReflection { get; set; }

    /// <summary>
    /// Validates all property values and throws if any are out of range.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="Port"/>, <see cref="Host"/>, <see cref="MaxRecordedCalls"/>
    /// or <see cref="MaxReceiveMessageSize"/> have invalid values.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Thrown when <see cref="EnableReflection"/> is <see langword="true"/>.
    /// </exception>
    public void Validate()
    {
        if (Port < 0 || Port > 65535)
        {
            throw new ArgumentException($"Port must be between 0 and 65535, but was {Port}.", nameof(Port));
        }

        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new ArgumentException("Host must not be null or whitespace.", nameof(Host));
        }

        if (!string.Equals(Host, "localhost", StringComparison.OrdinalIgnoreCase) && !IPAddress.TryParse(Host, out _))
        {
            throw new ArgumentException($"Host must be \"localhost\" or a valid IP address, but was \"{Host}\".", nameof(Host));
        }

        if (MaxRecordedCalls <= 0)
        {
            throw new ArgumentException($"MaxRecordedCalls must be greater than 0, but was {MaxRecordedCalls}.", nameof(MaxRecordedCalls));
        }

        if (MaxReceiveMessageSize.HasValue && MaxReceiveMessageSize.Value <= 0)
        {
            throw new ArgumentException($"MaxReceiveMessageSize must be greater than 0 when set, but was {MaxReceiveMessageSize.Value}.", nameof(MaxReceiveMessageSize));
        }

#pragma warning disable CS0618 // Obsolete member access is intentional in validation
        if (EnableReflection)
        {
            throw new NotSupportedException(
                "Reflection is not supported; dynamic methods have no proto descriptors. " +
                "The EnableReflection property is obsolete.");
        }
#pragma warning restore CS0618
    }

    /// <summary>
    /// Returns a shallow copy of these options so the caller can snapshot the configuration
    /// at a point in time without sharing mutable state.
    /// </summary>
    /// <returns>A new <see cref="GrpcMockOptions"/> instance with the same property values.</returns>
    public GrpcMockOptions Clone() => (GrpcMockOptions)MemberwiseClone();

    /// <summary>
    /// Creates options configured for a real socket (non-TestHost) on the specified port and host.
    /// </summary>
    /// <param name="port">Port to bind. 0 (default) picks a free port.</param>
    /// <param name="host">Host to bind. Defaults to <c>127.0.0.1</c>.</param>
    /// <returns>A new <see cref="GrpcMockOptions"/> with <see cref="UseTestHost"/> set to <see langword="false"/>.</returns>
    public static GrpcMockOptions RealSocket(int port = 0, string host = "127.0.0.1") => new()
    {
        UseTestHost = false,
        Port = port,
        Host = host,
    };
}
