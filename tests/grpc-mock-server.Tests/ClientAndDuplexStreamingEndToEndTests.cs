using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace GrpcMockServer.Tests;

public class ClientAndDuplexStreamingEndToEndTests
{
    [Fact]
    public async Task Respond_FactoryOverAllRequests_ConcatenatesValues()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForClientStreaming<StringValue, StringValue>(TestMethods.ConcatFullName)
            .Respond(requests => new StringValue { Value = string.Concat(requests.Select(r => r.Value)) }));

        using var cts = Deadline();
        using var call = server.CreateCallInvoker().AsyncClientStreamingCall(
            TestMethods.Concat, null, new CallOptions(cancellationToken: cts.Token));

        await call.RequestStream.WriteAsync(new StringValue { Value = "a" });
        await call.RequestStream.WriteAsync(new StringValue { Value = "b" });
        await call.RequestStream.WriteAsync(new StringValue { Value = "c" });
        await call.RequestStream.CompleteAsync();

        var response = await call.ResponseAsync;

        Assert.Equal("abc", response.Value);
    }

    [Fact]
    public async Task Respond_Fixed_IgnoresStreamedRequests()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForClientStreaming<StringValue, StringValue>(TestMethods.ConcatFullName)
            .Respond(new StringValue { Value = "fixed" }));

        using var cts = Deadline();
        using var call = server.CreateCallInvoker().AsyncClientStreamingCall(
            TestMethods.Concat, null, new CallOptions(cancellationToken: cts.Token));

        await call.RequestStream.WriteAsync(new StringValue { Value = "a" });
        await call.RequestStream.WriteAsync(new StringValue { Value = "b" });
        await call.RequestStream.CompleteAsync();

        var response = await call.ResponseAsync;

        Assert.Equal("fixed", response.Value);
    }

    [Fact]
    public async Task Respond_EmptyStream_FactoryReceivesEmptyList()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForClientStreaming<StringValue, StringValue>(TestMethods.ConcatFullName)
            .Respond(requests => new StringValue { Value = requests.Count.ToString() }));

        using var cts = Deadline();
        using var call = server.CreateCallInvoker().AsyncClientStreamingCall(
            TestMethods.Concat, null, new CallOptions(cancellationToken: cts.Token));

        await call.RequestStream.CompleteAsync();

        var response = await call.ResponseAsync;

        Assert.Equal("0", response.Value);
    }

    [Fact]
    public async Task Fail_Status_ClientGetsStatus()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForClientStreaming<StringValue, StringValue>(TestMethods.ConcatFullName)
            .Fail(StatusCode.InvalidArgument, "bad input"));

        using var cts = Deadline();
        using var call = server.CreateCallInvoker().AsyncClientStreamingCall(
            TestMethods.Concat, null, new CallOptions(cancellationToken: cts.Token));

        await call.RequestStream.WriteAsync(new StringValue { Value = "a" });
        await call.RequestStream.CompleteAsync();

        var ex = await Assert.ThrowsAsync<RpcException>(() => call.ResponseAsync);

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.Equal("bad input", ex.Status.Detail);
    }

    [Fact]
    public async Task Requests_AfterClientStream_RecordsAllPayloads()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForClientStreaming<StringValue, StringValue>(TestMethods.ConcatFullName)
            .Respond(new StringValue { Value = "ok" }));

        using var cts = Deadline();
        using var call = server.CreateCallInvoker().AsyncClientStreamingCall(
            TestMethods.Concat, null, new CallOptions(cancellationToken: cts.Token));

        await call.RequestStream.WriteAsync(new StringValue { Value = "first" });
        await call.RequestStream.WriteAsync(new StringValue { Value = "second" });
        await call.RequestStream.WriteAsync(new StringValue { Value = "third" });
        await call.RequestStream.CompleteAsync();
        await call.ResponseAsync;

        server.Requests.VerifyCalled(TestMethods.ConcatFullName, 1);
        var recorded = Assert.Single(server.Requests.For(TestMethods.ConcatFullName));
        Assert.Equal(MethodType.ClientStreaming, recorded.Type);
        Assert.True(recorded.Matched);
        Assert.Equal(3, recorded.RequestPayloads.Count);

        var requests = server.Requests.RequestsFor<StringValue>(TestMethods.ConcatFullName);
        Assert.Equal("first", requests[0].Value);
    }

    [Fact]
    public async Task Echo_Map_EachRequestGetsMappedResponse()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForDuplexStreaming<StringValue, StringValue>(TestMethods.EchoStreamFullName)
            .Echo(r => new StringValue { Value = r.Value.ToUpperInvariant() }));

        using var cts = Deadline();
        using var call = server.CreateCallInvoker().AsyncDuplexStreamingCall(
            TestMethods.EchoStream, null, new CallOptions(cancellationToken: cts.Token));

        var received = new List<string>();
        foreach (var input in new[] { "a", "b", "c" })
        {
            await call.RequestStream.WriteAsync(new StringValue { Value = input });
            Assert.True(await call.ResponseStream.MoveNext(cts.Token));
            received.Add(call.ResponseStream.Current.Value);
        }

        await call.RequestStream.CompleteAsync();

        Assert.Equal(new[] { "A", "B", "C" }, received);
        Assert.False(await call.ResponseStream.MoveNext(cts.Token));
    }

    [Fact]
    public async Task Echo_ClientCompletes_ServerCompletes()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForDuplexStreaming<StringValue, StringValue>(TestMethods.EchoStreamFullName)
            .Echo(r => r));

        using var cts = Deadline();
        using var call = server.CreateCallInvoker().AsyncDuplexStreamingCall(
            TestMethods.EchoStream, null, new CallOptions(cancellationToken: cts.Token));

        await call.RequestStream.WriteAsync(new StringValue { Value = "x" });
        await call.RequestStream.WriteAsync(new StringValue { Value = "y" });
        await call.RequestStream.CompleteAsync();

        var received = new List<string>();
        await foreach (var item in call.ResponseStream.ReadAllAsync(cts.Token))
        {
            received.Add(item.Value);
        }

        Assert.Equal(new[] { "x", "y" }, received);
        Assert.Equal(StatusCode.OK, call.GetStatus().StatusCode);
    }

    [Fact]
    public async Task Handle_CustomHandler_HasFullControl()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForDuplexStreaming<StringValue, StringValue>(TestMethods.EchoStreamFullName)
            .Handle(async (requests, responses, context) =>
            {
                var values = new List<string>();
                await foreach (var request in requests.ReadAllAsync(context.CancellationToken))
                {
                    values.Add(request.Value);
                }

                await responses.WriteAsync(new StringValue { Value = $"{values.Count}:{string.Join(",", values)}" });
            }));

        using var cts = Deadline();
        using var call = server.CreateCallInvoker().AsyncDuplexStreamingCall(
            TestMethods.EchoStream, null, new CallOptions(cancellationToken: cts.Token));

        await call.RequestStream.WriteAsync(new StringValue { Value = "a" });
        await call.RequestStream.WriteAsync(new StringValue { Value = "b" });
        await call.RequestStream.WriteAsync(new StringValue { Value = "c" });
        await call.RequestStream.CompleteAsync();

        var responses = new List<string>();
        await foreach (var item in call.ResponseStream.ReadAllAsync(cts.Token))
        {
            responses.Add(item.Value);
        }

        Assert.Equal(new[] { "3:a,b,c" }, responses);
    }

    [Fact]
    public async Task Fail_Status_ClientGetsStatusOnFirstRead()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForDuplexStreaming<StringValue, StringValue>(TestMethods.EchoStreamFullName)
            .Fail(StatusCode.Unavailable, "backend down"));

        using var cts = Deadline();
        using var call = server.CreateCallInvoker().AsyncDuplexStreamingCall(
            TestMethods.EchoStream, null, new CallOptions(cancellationToken: cts.Token));

        await call.RequestStream.WriteAsync(new StringValue { Value = "a" });

        var ex = await Assert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(cts.Token));

        Assert.Equal(StatusCode.Unavailable, ex.StatusCode);
        Assert.Equal("backend down", ex.Status.Detail);
    }

    [Fact]
    public async Task WithHeader_MatchingHeader_RoutesToStub()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForDuplexStreaming<StringValue, StringValue>(TestMethods.EchoStreamFullName)
            .WithHeader("x-key", "v")
            .Echo(r => new StringValue { Value = "routed:" + r.Value }));

        using var cts = Deadline();
        var headers = new Metadata { { "x-key", "v" } };
        using var call = server.CreateCallInvoker().AsyncDuplexStreamingCall(
            TestMethods.EchoStream, null, new CallOptions(headers: headers, cancellationToken: cts.Token));

        await call.RequestStream.WriteAsync(new StringValue { Value = "a" });
        await call.RequestStream.CompleteAsync();

        Assert.True(await call.ResponseStream.MoveNext(cts.Token));
        Assert.Equal("routed:a", call.ResponseStream.Current.Value);
    }

    [Fact]
    public async Task WithHeader_MissingHeader_FallsToUnmatched()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForDuplexStreaming<StringValue, StringValue>(TestMethods.EchoStreamFullName)
            .WithHeader("x-key", "v")
            .Echo(r => r));

        using var cts = Deadline();
        using var call = server.CreateCallInvoker().AsyncDuplexStreamingCall(
            TestMethods.EchoStream, null, new CallOptions(cancellationToken: cts.Token));

        await call.RequestStream.CompleteAsync();

        var ex = await Assert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(cts.Token));

        Assert.Equal(StatusCode.Unimplemented, ex.StatusCode);
    }

    [Fact]
    public void When_OnDuplexBuilder_ThrowsNotSupported()
    {
        var builder = new GrpcMockBuilder(new StubRegistry());
        var duplex = builder.ForDuplexStreaming<StringValue, StringValue>(TestMethods.EchoStreamFullName);

        Assert.Throws<NotSupportedException>(() => duplex.When(r => r.Value == "x"));
    }

    /// <summary>Upper bound on each call so a broken stub fails the test instead of hanging the run.</summary>
    private static CancellationTokenSource Deadline() => new(TimeSpan.FromSeconds(10));
}
