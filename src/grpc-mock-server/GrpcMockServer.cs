using System.Net;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    private readonly StubRegistry _registry;
    private readonly RequestLog _log;
    private readonly GrpcMockOptions _options;
    private readonly WebApplication _app;
    private readonly Uri _baseAddress;
    private readonly List<GrpcChannel> _channels = new();
    private bool _disposed;

    private GrpcMockServer(WebApplication app, Uri baseAddress, StubRegistry registry, RequestLog log, GrpcMockOptions options)
    {
        _app = app;
        _baseAddress = baseAddress;
        _registry = registry;
        _log = log;
        _options = options;
    }

    /// <summary>Base address the server is listening on (e.g. <c>http://127.0.0.1:54321</c>).</summary>
    /// <exception cref="ObjectDisposedException">The server has been disposed.</exception>
    public Uri BaseAddress
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _baseAddress;
        }
    }

    /// <summary>Recorded requests and verification API for this server instance.</summary>
    /// <exception cref="ObjectDisposedException">The server has been disposed.</exception>
    public IRequestLog Requests
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _log;
        }
    }

    /// <summary>
    /// Starts a mock server on a free loopback port and registers the stubs configured by <paramref name="configure"/>.
    /// </summary>
    /// <param name="configure">Fluent stub configuration.</param>
    /// <param name="options">Optional server options. Defaults to <see cref="GrpcMockOptions.Default"/>.</param>
    /// <param name="cancellationToken">Token used to cancel startup.</param>
    /// <returns>A running server that must be disposed at the end of the test.</returns>
    public static async Task<GrpcMockServer> StartAsync(
        Action<IGrpcMockBuilder> configure,
        GrpcMockOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);

        options ??= GrpcMockOptions.Default;
        options.Validate();
        options = options.Clone();

        var registry = new StubRegistry();
        var log = new RequestLog(options.MaxRecordedCalls);

        configure(new GrpcMockBuilder(registry));

        var builder = WebApplication.CreateSlimBuilder();

        if (options.UseTestHost)
        {
            builder.WebHost.UseTestServer();
        }
        else
        {
            // "localhost" is accepted by Validate() but is not an IP literal, so map it explicitly.
            var address = string.Equals(options.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                ? IPAddress.Loopback
                : IPAddress.Parse(options.Host);

            builder.WebHost.ConfigureKestrel(k => k.Listen(address, options.Port, l => l.Protocols = HttpProtocols.Http2));
        }

        builder.Services.AddGrpc(o =>
        {
            // Without this, gRPC answers undeclared methods itself (HTTP 200, grpc-status UNIMPLEMENTED) and
            // the call never reaches UnmatchedCallMiddleware, so it would not be recorded.
            o.IgnoreUnknownServices = true;

            if (options.MaxReceiveMessageSize.HasValue)
            {
                o.MaxReceiveMessageSize = options.MaxReceiveMessageSize.Value;
            }
        });

        if (options.LoggerFactory is not null)
        {
            builder.Services.AddSingleton(options.LoggerFactory);
        }
        else
        {
            builder.Logging.ClearProviders();
        }

        builder.Services.AddGrpcMockCore(registry, log, options);

        var app = builder.Build();
        app.MapGrpcService<MockGrpcService>();

        // Calls to undeclared methods match no endpoint; answer them with a gRPC status instead of a 404.
        var unmatched = new UnmatchedCallMiddleware(registry, log, options);
        app.MapFallback("/{service}/{method}", unmatched.InvokeAsync);

        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        var baseAddress = options.UseTestHost
            ? new Uri("http://localhost")
            : new Uri(app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First());

        // Endpoints are built from the declared method set when the host starts, so freeze it here.
        registry.Seal();

        return new GrpcMockServer(app, baseAddress, registry, log, options);
    }

    /// <summary>
    /// Adds stubs on an already running server. Useful for per-test arrangement when a single
    /// server is shared across a test class.
    /// </summary>
    /// <remarks>
    /// Only methods declared during <see cref="StartAsync"/> can be routed. Configuring a stub for a method
    /// that was not declared at startup throws <see cref="InvalidOperationException"/>; add that method in
    /// the <c>configure</c> callback passed to <see cref="StartAsync"/> instead.
    /// </remarks>
    /// <param name="configure">Fluent stub configuration.</param>
    /// <exception cref="InvalidOperationException">The configuration declares a method that was not declared at startup.</exception>
    public void Configure(Action<IGrpcMockBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var builder = new GrpcMockBuilder(_registry);
        configure(builder);
    }

    /// <summary>Removes all stub definitions and clears the request log. Declared methods are kept.</summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _registry.ClearStubs();
        _log.Clear();
    }

    /// <summary>
    /// Creates a <see cref="GrpcChannel"/> pointed at this server. When the server runs in
    /// <see cref="GrpcMockOptions.UseTestHost"/> mode the channel bypasses the network entirely.
    /// Channels created here are disposed together with the server.
    /// </summary>
    /// <param name="configureOptions">Optional channel customization.</param>
    public GrpcChannel CreateChannel(Action<GrpcChannelOptions>? configureOptions = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var channelOptions = new GrpcChannelOptions();

        if (_options.UseTestHost)
        {
            channelOptions.HttpHandler = _app.GetTestServer().CreateHandler();
        }

        configureOptions?.Invoke(channelOptions);

        var channel = GrpcChannel.ForAddress(BaseAddress, channelOptions);
        _channels.Add(channel);
        return channel;
    }

    /// <summary>
    /// Creates a raw <see cref="CallInvoker"/> for scenarios where a generated client is not available.
    /// </summary>
    public CallInvoker CreateCallInvoker() => CreateChannel().CreateCallInvoker();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var channel in _channels)
        {
            channel.Dispose();
        }

        _channels.Clear();

        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <inheritdoc />
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
