using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace GrpcMockServer.Tests;

public class UnaryEndToEndTests
{
    [Fact]
    public async Task Respond_FixedMessage_ReturnsIt()
    {
        var expected = new StringValue { Value = "HELLO" };

        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Respond(expected));

        var invoker = server.CreateCallInvoker();
        var request = new StringValue { Value = "hello" };

        var response = await invoker.AsyncUnaryCall(
            TestMethods.EchoUpper, null, new CallOptions(), request);

        Assert.Equal("HELLO", response.Value);
    }

    [Fact]
    public async Task Respond_FactoryFromRequest_UsesRequest()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Respond(req => new StringValue { Value = req.Value.ToUpperInvariant() }));

        var invoker = server.CreateCallInvoker();
        var request = new StringValue { Value = "world" };

        var response = await invoker.AsyncUnaryCall(
            TestMethods.EchoUpper, null, new CallOptions(), request);

        Assert.Equal("WORLD", response.Value);
    }

    [Fact]
    public async Task Respond_AsyncFactory_ReceivesServerCallContext()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Respond(async (req, context) =>
            {
                await Task.CompletedTask;
                return new StringValue { Value = context.Method };
            }));

        var invoker = server.CreateCallInvoker();
        var request = new StringValue { Value = "ignored" };

        var response = await invoker.AsyncUnaryCall(
            TestMethods.EchoUpper, null, new CallOptions(), request);

        Assert.Equal("/e2e.v1.Echo/Upper", response.Value);
    }

    [Fact]
    public async Task When_PredicateFalse_FallsToNextStub()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .When(r => r.Value == "match-me")
            .Respond(new StringValue { Value = "MATCHED" })
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Respond(new StringValue { Value = "FALLBACK" }));

        var invoker = server.CreateCallInvoker();

        var response = await invoker.AsyncUnaryCall(
            TestMethods.EchoUpper, null, new CallOptions(),
            new StringValue { Value = "other" });

        Assert.Equal("FALLBACK", response.Value);
    }

    [Fact]
    public async Task When_NoStubMatches_ReturnsUnimplementedByDefault()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .When(r => r.Value == "never-matches")
            .Respond(new StringValue { Value = "NOPE" }));

        var invoker = server.CreateCallInvoker();

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await invoker.AsyncUnaryCall(
                TestMethods.EchoUpper, null, new CallOptions(),
                new StringValue { Value = "hello" }));

        Assert.Equal(StatusCode.Unimplemented, ex.StatusCode);
    }

    [Fact]
    public async Task OnUnmatched_CustomStatus_IsReturned()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .When(r => r.Value == "never-matches")
            .Respond(new StringValue { Value = "NOPE" })
            .OnUnmatched(new Status(StatusCode.NotFound, "Not found")));

        var invoker = server.CreateCallInvoker();

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await invoker.AsyncUnaryCall(
                TestMethods.EchoUpper, null, new CallOptions(),
                new StringValue { Value = "hello" }));

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Fail_StatusCode_ClientGetsStatusAndDetail()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Fail(StatusCode.InvalidArgument, "bad input"));

        var invoker = server.CreateCallInvoker();

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await invoker.AsyncUnaryCall(
                TestMethods.EchoUpper, null, new CallOptions(),
                new StringValue { Value = "x" }));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.Contains("bad input", ex.Status.Detail);
    }

    [Fact]
    public async Task Fail_RpcExceptionWithTrailers_ClientSeesTrailers()
    {
        var trailers = new Metadata { { "x-error-id", "42" } };
        var rpcEx = new RpcException(
            new Status(StatusCode.PermissionDenied, "denied"),
            trailers);

        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Fail(rpcEx));

        var invoker = server.CreateCallInvoker();

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            await invoker.AsyncUnaryCall(
                TestMethods.EchoUpper, null, new CallOptions(),
                new StringValue { Value = "x" }));

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        var trailer = ex.Trailers.FirstOrDefault(t => t.Key == "x-error-id");
        Assert.NotNull(trailer);
        Assert.Equal("42", trailer.Value);
    }

    [Fact]
    public async Task RespondInSequence_ThreeCalls_ReturnsABB()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .RespondInSequence(
                new StringValue { Value = "A" },
                new StringValue { Value = "B" }));

        var invoker = server.CreateCallInvoker();
        var request = new StringValue { Value = "x" };

        var r1 = await invoker.AsyncUnaryCall(TestMethods.EchoUpper, null, new CallOptions(), request);
        var r2 = await invoker.AsyncUnaryCall(TestMethods.EchoUpper, null, new CallOptions(), request);
        var r3 = await invoker.AsyncUnaryCall(TestMethods.EchoUpper, null, new CallOptions(), request);

        Assert.Equal("A", r1.Value);
        Assert.Equal("B", r2.Value);
        Assert.Equal("B", r3.Value);
    }

    [Fact]
    public async Task Times_One_SecondCallHitsFallback()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Times(1)
            .Respond(new StringValue { Value = "ONCE" })
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Respond(new StringValue { Value = "FALLBACK" }));

        var invoker = server.CreateCallInvoker();
        var request = new StringValue { Value = "x" };

        var r1 = await invoker.AsyncUnaryCall(TestMethods.EchoUpper, null, new CallOptions(), request);
        var r2 = await invoker.AsyncUnaryCall(TestMethods.EchoUpper, null, new CallOptions(), request);

        Assert.Equal("ONCE", r1.Value);
        Assert.Equal("FALLBACK", r2.Value);
    }

    [Fact]
    public async Task WithPriority_Higher_WinsRegardlessOfOrder()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .WithPriority(0)
            .Respond(new StringValue { Value = "LOW" })
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .WithPriority(10)
            .Respond(new StringValue { Value = "HIGH" }));

        var invoker = server.CreateCallInvoker();
        var request = new StringValue { Value = "x" };

        var response = await invoker.AsyncUnaryCall(TestMethods.EchoUpper, null, new CallOptions(), request);

        Assert.Equal("HIGH", response.Value);
    }

    [Fact]
    public async Task Requests_AfterCall_RecordsMatchedCallWithPayload()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Respond(new StringValue { Value = "OK" }));

        var invoker = server.CreateCallInvoker();
        var request = new StringValue { Value = "tracked" };

        await invoker.AsyncUnaryCall(TestMethods.EchoUpper, null, new CallOptions(), request);

        server.Requests.VerifyCalled(TestMethods.EchoUpperFullName, 1);
        var recorded = server.Requests.RequestsFor<StringValue>(TestMethods.EchoUpperFullName);
        Assert.Single(recorded);
        Assert.Equal("tracked", recorded[0].Value);
    }

    [Fact]
    public async Task Requests_UnmatchedCall_RecordedWithMatchedFalse()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .When(r => r.Value == "never-matches")
            .Respond(new StringValue { Value = "NOPE" }));

        var invoker = server.CreateCallInvoker();

        try
        {
            await invoker.AsyncUnaryCall(
                TestMethods.EchoUpper, null, new CallOptions(),
                new StringValue { Value = "unmatched" });
        }
        catch (RpcException)
        {
            // expected - unmatched call
        }

        var all = server.Requests.All;
        Assert.Single(all);
        Assert.False(all[0].Matched);

        Assert.Throws<GrpcMockVerificationException>(() =>
            server.Requests.VerifyNoUnmatchedCalls());
    }
}
