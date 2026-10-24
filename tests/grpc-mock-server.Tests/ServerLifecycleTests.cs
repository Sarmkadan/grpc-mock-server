using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;

namespace GrpcMockServer.Tests;

public class ServerLifecycleTests
{
    private const string _getFullName = "life.v1.Svc/Get";

    private static readonly Method<StringValue, StringValue> _get =
        TestMethods.Unary<StringValue, StringValue>("life.v1.Svc", "Get");

    [Fact]
    public async Task StartAsync_TestHostMode_BaseAddressIsLocalhost()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(_getFullName)
            .Respond(new StringValue { Value = "OK" }));

        Assert.Equal("http", server.BaseAddress.Scheme);
        Assert.Equal("localhost", server.BaseAddress.Host);
    }

    [Fact]
    public async Task StartAsync_RealSocketMode_BindsFreePort()
    {
        await using var server = await GrpcMockServer.StartAsync(
            mock => mock
                .ForUnary<StringValue, StringValue>(_getFullName)
                .Respond(new StringValue { Value = "OK" }),
            new GrpcMockOptions { UseTestHost = false, Port = 0 });

        Assert.Equal("http", server.BaseAddress.Scheme);
        Assert.True(server.BaseAddress.Port > 0);
    }

    [Fact]
    public async Task RealSocketMode_ExternalGrpcChannel_CanCall()
    {
        await using var server = await GrpcMockServer.StartAsync(
            mock => mock
                .ForUnary<StringValue, StringValue>(_getFullName)
                .Respond(new StringValue { Value = "EXTERNAL" }),
            new GrpcMockOptions { UseTestHost = false, Port = 0 });

        // A client that is not created through CreateChannel, i.e. one that behaves like an out-of-process caller.
        using var channel = GrpcChannel.ForAddress(server.BaseAddress);
        var invoker = channel.CreateCallInvoker();

        var response = await invoker.AsyncUnaryCall(
            _get, null, new CallOptions(), new StringValue { Value = "hello" });

        Assert.Equal("EXTERNAL", response.Value);
    }

    [Fact]
    public async Task Configure_AddsStubOnDeclaredMethod_NewStubMatches()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(_getFullName)
            .Respond(new StringValue { Value = "FALLBACK" }));

        var invoker = server.CreateCallInvoker();

        var before = await invoker.AsyncUnaryCall(_get, null, new CallOptions(), new StringValue { Value = "x" });
        Assert.Equal("FALLBACK", before.Value);

        server.Configure(mock => mock
            .ForUnary<StringValue, StringValue>(_getFullName)
            .WithPriority(1)
            .Respond(new StringValue { Value = "CONFIGURED" }));

        var after = await invoker.AsyncUnaryCall(_get, null, new CallOptions(), new StringValue { Value = "x" });
        Assert.Equal("CONFIGURED", after.Value);
    }

    [Fact]
    public async Task Configure_NewMethodAfterStart_ThrowsInvalidOperation()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(_getFullName)
            .Respond(new StringValue { Value = "OK" }));

        // Only methods declared in StartAsync can be routed; a late, undeclared method is rejected up front.
        var ex = Assert.Throws<InvalidOperationException>(() => server.Configure(mock => mock
            .ForUnary<StringValue, StringValue>("life.v1.Svc/Late")
            .Respond(new StringValue { Value = "LATE" })));

        Assert.Contains("life.v1.Svc/Late", ex.Message);
    }

    [Fact]
    public async Task Reset_ClearsStubsAndLog()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(_getFullName)
            .Respond(new StringValue { Value = "OK" }));

        var invoker = server.CreateCallInvoker();
        await invoker.AsyncUnaryCall(_get, null, new CallOptions(), new StringValue { Value = "before" });
        Assert.Single(server.Requests.All);

        server.Reset();

        Assert.Empty(server.Requests.All);

        var ex = await Assert.ThrowsAsync<RpcException>(() => invoker.AsyncUnaryCall(
            _get, null, new CallOptions(), new StringValue { Value = "after" }).ResponseAsync);

        Assert.Equal(StatusCode.Unimplemented, ex.StatusCode);
    }

    [Fact]
    public async Task DisposeAsync_Twice_DoesNotThrow()
    {
        var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(_getFullName)
            .Respond(new StringValue { Value = "OK" }));

        await server.DisposeAsync();
        var second = await Record.ExceptionAsync(() => server.DisposeAsync().AsTask());

        Assert.Null(second);
    }

    [Fact]
    public async Task Dispose_ThenDisposeAsync_DoesNotThrow()
    {
        var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(_getFullName)
            .Respond(new StringValue { Value = "OK" }));

        server.Dispose();
        var ex = await Record.ExceptionAsync(() => server.DisposeAsync().AsTask());

        Assert.Null(ex);
    }

    [Fact]
    public async Task DisposeAsync_DisposesCreatedChannels()
    {
        var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(_getFullName)
            .Respond(new StringValue { Value = "OK" }));

        var invoker = server.CreateChannel().CreateCallInvoker();

        await server.DisposeAsync();

        // The channel created by the server was disposed along with it, so calls through it must fail.
        var ex = await Record.ExceptionAsync(() => invoker.AsyncUnaryCall(
            _get, null, new CallOptions(), new StringValue { Value = "x" }).ResponseAsync);

        Assert.True(ex is ObjectDisposedException or RpcException, $"Unexpected exception: {ex?.GetType()}");
    }

    [Fact]
    public async Task StartAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GrpcMockServer.StartAsync(
            mock => mock
                .ForUnary<StringValue, StringValue>(_getFullName)
                .Respond(new StringValue { Value = "OK" }),
            cancellationToken: cts.Token));
    }

    [Fact]
    public async Task RecordRequests_False_LogStaysEmpty()
    {
        await using var server = await GrpcMockServer.StartAsync(
            mock => mock
                .ForUnary<StringValue, StringValue>(_getFullName)
                .Respond(new StringValue { Value = "OK" }),
            new GrpcMockOptions { RecordRequests = false });

        var invoker = server.CreateCallInvoker();
        var response = await invoker.AsyncUnaryCall(_get, null, new CallOptions(), new StringValue { Value = "x" });

        Assert.Equal("OK", response.Value);
        Assert.Empty(server.Requests.All);
    }

    [Fact]
    public async Task MaxRecordedCalls_2_KeepsOnlyLastTwo()
    {
        await using var server = await GrpcMockServer.StartAsync(
            mock => mock
                .ForUnary<StringValue, StringValue>(_getFullName)
                .Respond(new StringValue { Value = "OK" }),
            new GrpcMockOptions { MaxRecordedCalls = 2 });

        var invoker = server.CreateCallInvoker();
        foreach (var value in new[] { "a", "b", "c" })
        {
            await invoker.AsyncUnaryCall(_get, null, new CallOptions(), new StringValue { Value = value });
        }

        var recorded = server.Requests.RequestsFor<StringValue>(_getFullName);
        Assert.Equal(2, server.Requests.All.Count);
        Assert.Equal(new[] { "b", "c" }, recorded.Select(r => r.Value));
    }
}
