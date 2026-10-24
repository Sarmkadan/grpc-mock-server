using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace GrpcMockServer.Tests;

public class LifecycleTests
{
    [Fact]
    public async Task Configure_NewStubForDeclaredMethod_IsRoutable()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Respond(new StringValue { Value = "FIRST" }));

        server.Configure(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .When(r => r.Value == "special")
            .WithPriority(1)
            .Respond(new StringValue { Value = "SPECIAL" }));

        var invoker = server.CreateCallInvoker();

        var response = await invoker.AsyncUnaryCall(
            TestMethods.EchoUpper, null, new CallOptions(), new StringValue { Value = "special" });

        Assert.Equal("SPECIAL", response.Value);
    }

    [Fact]
    public async Task Configure_UndeclaredMethod_ThrowsListingDeclaredMethods()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Respond(new StringValue { Value = "FIRST" }));

        var ex = Assert.Throws<InvalidOperationException>(() => server.Configure(mock => mock
            .ForUnary<StringValue, StringValue>("e2e.v1.Echo/Lower")
            .Respond(new StringValue { Value = "LATE" })));

        Assert.Contains("e2e.v1.Echo/Lower", ex.Message);
        Assert.Contains(TestMethods.EchoUpperFullName, ex.Message);
    }

    [Fact]
    public async Task Reset_RemovesStubs_KeepsDeclaredMethods()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Respond(new StringValue { Value = "FIRST" }));

        var invoker = server.CreateCallInvoker();
        await invoker.AsyncUnaryCall(TestMethods.EchoUpper, null, new CallOptions(), new StringValue { Value = "a" });

        server.Reset();

        // Stub is gone, so the call is unmatched; the method is still routed (Unimplemented, not NotFound).
        var ex = await Assert.ThrowsAsync<RpcException>(() => invoker.AsyncUnaryCall(
            TestMethods.EchoUpper, null, new CallOptions(), new StringValue { Value = "b" }).ResponseAsync);

        Assert.Equal(StatusCode.Unimplemented, ex.StatusCode);
        // The log was cleared, so only the call made after Reset is recorded, and it was unmatched.
        var recorded = Assert.Single(server.Requests.All);
        Assert.False(recorded.Matched);

        // Declared methods remain usable: a new stub can be added after Reset.
        server.Configure(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Respond(new StringValue { Value = "AGAIN" }));

        var response = await invoker.AsyncUnaryCall(
            TestMethods.EchoUpper, null, new CallOptions(), new StringValue { Value = "c" });

        Assert.Equal("AGAIN", response.Value);
    }

    [Fact]
    public async Task CreateChannel_AfterDispose_Throws()
    {
        var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Respond(new StringValue { Value = "X" }));

        await server.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => server.CreateChannel());
    }
}
