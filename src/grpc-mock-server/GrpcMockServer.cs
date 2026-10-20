using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    private readonly IHost _host;
    private readonly TestServer? _testServer;
    private readonly StubRegistry _registry;
    private readonly RequestLog _log;
    private readonly GrpcMockOptions _options;
    private bool _disposed;

    private GrpcMockServer(IHost host, TestServer? testServer, StubRegistry registry, RequestLog log, GrpcMockOptions options)
    {
        _host = host;
        _testServer = testServer;
        _registry = registry;
        _log = log;
        _options = options;
    }

    /// <summary>Base address the server is listening on (e.g. <c>http://127.0.0.1:54321</c>).</summary>
    public Uri BaseAddress
    {
        get
        {
            if (_testServer is not null)
            {
                return _testServer.BaseAddress;
            }

            return new Uri($"http://{_options.Host}:{_options.Port}");
        }
    }

    /// <summary>Recorded requests and verification API for this server instance.</summary>
    public IRequestLog Requests => _log;

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

        var mockBuilder = new GrpcMockBuilder(registry);
        configure(mockBuilder);

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                if (options.UseTestHost)
                {
                    webHost.UseTestServer();
                }
                else
                {
                    webHost.UseKestrel(k => k.ListenLocalhost(options.Port));
                }

                webHost.ConfigureServices(services =>
                {
                    services.AddGrpc(grpcOptions =>
                    {
                        if (options.MaxReceiveMessageSize.HasValue)
                        {
                            grpcOptions.MaxReceiveMessageSize = options.MaxReceiveMessageSize.Value;
                        }
                    });

                    services.AddGrpcMockCore(registry, log, options);

                    if (options.LoggerFactory is not null)
                    {
                        services.AddSingleton(options.LoggerFactory);
                    }
                });

                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGrpcService<MockGrpcService>();
                    });
                });
            });

        var host = hostBuilder.Build();
        await host.StartAsync(cancellationToken);

        TestServer? testServer = null;
        if (options.UseTestHost)
        {
            testServer = host.GetTestServer();
        }

        return new GrpcMockServer(host, testServer, registry, log, options);
    }

    /// <summary>
    /// Adds or replaces stubs on an already running server. Useful for per-test arrangement
    /// when a single server is shared across a test class.
    /// </summary>
    /// <param name="configure">Fluent stub configuration.</param>
    public void Configure(Action<IGrpcMockBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new GrpcMockBuilder(_registry);
        configure(builder);
    }

    /// <summary>Removes all stubs and clears the request log.</summary>
    public void Reset()
    {
        _registry.Clear();
        _log.Clear();
    }

    /// <summary>
    /// Creates a <see cref="GrpcChannel"/> pointed at this server. When the server runs in
    /// <see cref="GrpcMockOptions.UseTestHost"/> mode the channel bypasses the network entirely.
    /// </summary>
    /// <param name="configureOptions">Optional channel customization.</param>
    public GrpcChannel CreateChannel(Action<GrpcChannelOptions>? configureOptions = null)
    {
        var channelOptions = new GrpcChannelOptions();

        if (_testServer is not null)
        {
            channelOptions.HttpHandler = _testServer.CreateHandler();
        }

        configureOptions?.Invoke(channelOptions);

        return GrpcChannel.ForAddress(BaseAddress, channelOptions);
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
        _host.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.Dispose();
    }
}
