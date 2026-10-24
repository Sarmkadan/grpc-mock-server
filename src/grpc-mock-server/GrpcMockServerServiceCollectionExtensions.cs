using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GrpcMockServer;

/// <summary>
/// Registers an in-process gRPC mock server in an <see cref="IServiceCollection"/>, for hosts that build their
/// dependency graph with <c>Microsoft.Extensions.DependencyInjection</c> (for example <c>WebApplicationFactory</c>).
/// </summary>
public static class GrpcMockServerServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="GrpcMockOptions"/>, <see cref="GrpcMockServer"/> and <see cref="IRequestLog"/> as singletons.
    /// The server starts on first resolution and is disposed together with the service provider.
    /// Invalid options make resolving <c>IOptions&lt;GrpcMockOptions&gt;</c> throw <see cref="OptionsValidationException"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Fluent stub configuration, applied when the server starts.</param>
    /// <param name="configureOptions">Optional server options. Defaults to <see cref="GrpcMockOptions.Default"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddGrpcMockServer(
        this IServiceCollection services,
        Action<IGrpcMockBuilder> configure,
        Action<GrpcMockOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<GrpcMockOptions>()
            .Configure(configureOptions ?? (_ => { }))
            .Validate(IsValid, "GrpcMockOptions failed validation. Call GrpcMockOptions.Validate() to see the invalid property.");

        services.AddSingleton(sp => new GrpcMockServerHost(
            configure,
            sp.GetRequiredService<IOptions<GrpcMockOptions>>().Value));

        services.AddSingleton(sp => sp.GetRequiredService<GrpcMockServerHost>().Server);

        services.AddSingleton<IRequestLog>(sp => sp.GetRequiredService<GrpcMockServer>().Requests);

        return services;
    }

    private static bool IsValid(GrpcMockOptions options)
    {
        try
        {
            options.Validate();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// Owns a <see cref="GrpcMockServer"/> that is started on first access and disposed by the container.
/// </summary>
internal sealed class GrpcMockServerHost : IAsyncDisposable
{
    private readonly Lazy<Task<GrpcMockServer>> _server;
    private bool _disposed;

    public GrpcMockServerHost(Action<IGrpcMockBuilder> configure, GrpcMockOptions options)
    {
        _server = new Lazy<Task<GrpcMockServer>>(() => GrpcMockServer.StartAsync(configure, options));
    }

    /// <summary>The running server. The first access starts it and blocks until startup completes.</summary>
    public GrpcMockServer Server
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _server.Value.GetAwaiter().GetResult();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Only a server that actually started needs disposing; a failed start has nothing to clean up.
        if (_server.IsValueCreated && _server.Value.IsCompletedSuccessfully)
        {
            await _server.Value.Result.DisposeAsync();
        }
    }
}
