using Grpc.Core;
using Grpc.Net.Client;

namespace GrpcMockServer;

/// <summary>
/// In-process gRPC mock server. Hosts an ASP.NET Core Kestrel/TestHost pipeline that routes
/// incoming gRPC calls to fluent stubs registered via <see cref="IGrpcMockBuilder"/>.
/// No generated service base class is required - methods are declared by their full name
/// (<c>package.Service/Method</c>) and the request/response message types.
/// </summary>
/// <example>
/// <code>
/// await using var server = await GrpcMockServer.StartAsync(mock => mock
///     .ForUnary&lt;GetUserRequest, GetUserResponse&gt;("users.v1.UserService/GetUser")
///     .When(r => r.Id == 42)
///     .Respond(new GetUserResponse { Name = "Ada" }));
///
/// var client = new UserService.UserServiceClient(server.CreateChannel());
/// </code>
/// </example>
public sealed class GrpcMockServer : IAsyncDisposable, IDisposable
{
    private GrpcMockServer()
    {
    }

    /// <summary>Base address the server is listening on (e.g. <c>http://127.0.0.1:54321</c>).</summary>
    public Uri BaseAddress => throw new NotImplementedException();

    /// <summary>Recorded requests and verification API for this server instance.</summary>
    public IRequestLog Requests => throw new NotImplementedException();

    /// <summary>
    /// Starts a mock server on a free loopback port and registers the stubs configured by <paramref name="configure"/>.
    /// </summary>
    /// <param name="configure">Fluent stub configuration.</param>
    /// <param name="options">Optional server options. Defaults to <see cref="GrpcMockOptions.Default"/>.</param>
    /// <param name="cancellationToken">Token used to cancel startup.</param>
    /// <returns>A running server that must be disposed at the end of the test.</returns>
    public static Task<GrpcMockServer> StartAsync(
        Action<IGrpcMockBuilder> configure,
        GrpcMockOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        throw new NotImplementedException();
    }

    /// <summary>
    /// Adds or replaces stubs on an already running server. Useful for per-test arrangement
    /// when a single server is shared across a test class.
    /// </summary>
    /// <param name="configure">Fluent stub configuration.</param>
    public void Configure(Action<IGrpcMockBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        throw new NotImplementedException();
    }

    /// <summary>Removes all stubs and clears the request log.</summary>
    public void Reset() => throw new NotImplementedException();

    /// <summary>
    /// Creates a <see cref="GrpcChannel"/> pointed at this server. When the server runs in
    /// <see cref="GrpcMockOptions.UseTestHost"/> mode the channel bypasses the network entirely.
    /// </summary>
    /// <param name="configureOptions">Optional channel customization.</param>
    public GrpcChannel CreateChannel(Action<GrpcChannelOptions>? configureOptions = null)
        => throw new NotImplementedException();

    /// <summary>
    /// Creates a raw <see cref="CallInvoker"/> for scenarios where a generated client is not available.
    /// </summary>
    public CallInvoker CreateCallInvoker() => throw new NotImplementedException();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => throw new NotImplementedException();

    /// <inheritdoc />
    public void Dispose() => throw new NotImplementedException();
}
