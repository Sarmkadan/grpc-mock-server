using Grpc.AspNetCore.Server.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GrpcMockServer;

/// <summary>
/// DI registration helpers for the gRPC mock server runtime.
/// </summary>
internal static class GrpcMockServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core mock gRPC services: the <see cref="DynamicServiceMethodProvider"/>
    /// that binds stub-backed handlers into the ASP.NET Core gRPC pipeline.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="registry">The stub registry holding configured stubs.</param>
    /// <param name="log">The request log for recording calls.</param>
    /// <param name="options">Server options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGrpcMockCore(
        this IServiceCollection services,
        StubRegistry registry,
        RequestLog log,
        GrpcMockOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(options);

        var provider = new DynamicServiceMethodProvider(registry, log, options);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IServiceMethodProvider<MockGrpcService>>(provider));

        return services;
    }
}
