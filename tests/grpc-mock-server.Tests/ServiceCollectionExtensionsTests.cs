using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GrpcMockServer.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddGrpcMockServer_Resolve_ReturnsRunningServer()
    {
        await using var provider = new ServiceCollection()
            .AddGrpcMockServer(mock => mock
                .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
                .Respond(req => new StringValue { Value = req.Value.ToUpperInvariant() }))
            .BuildServiceProvider();

        var server = provider.GetRequiredService<GrpcMockServer>();
        var invoker = server.CreateCallInvoker();

        var response = await invoker.AsyncUnaryCall(
            TestMethods.EchoUpper, null, new CallOptions(), new StringValue { Value = "hello" });

        Assert.Equal("HELLO", response.Value);
    }

    [Fact]
    public void AddGrpcMockServer_InvalidOptions_ThrowsOnResolve()
    {
        using var provider = new ServiceCollection()
            .AddGrpcMockServer(
                _ => { },
                options => options.Port = -1)
            .BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<GrpcMockServer>());
    }

    [Fact]
    public async Task AddGrpcMockServer_IRequestLog_IsSameInstanceAsServerRequests()
    {
        await using var provider = new ServiceCollection()
            .AddGrpcMockServer(mock => mock
                .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
                .Respond(new StringValue { Value = "OK" }))
            .BuildServiceProvider();

        var server = provider.GetRequiredService<GrpcMockServer>();
        var log = provider.GetRequiredService<IRequestLog>();

        Assert.Same(server.Requests, log);

        await server.CreateCallInvoker().AsyncUnaryCall(
            TestMethods.EchoUpper, null, new CallOptions(), new StringValue { Value = "x" });

        log.VerifyCalled(TestMethods.EchoUpperFullName, 1);
    }

    [Fact]
    public async Task ServiceProvider_DisposeAsync_DisposesServer()
    {
        var provider = new ServiceCollection()
            .AddGrpcMockServer(mock => mock
                .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
                .Respond(new StringValue { Value = "OK" }))
            .BuildServiceProvider();

        var server = provider.GetRequiredService<GrpcMockServer>();

        await provider.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => server.BaseAddress);
    }
}
