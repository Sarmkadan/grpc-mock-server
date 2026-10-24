using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace GrpcMockServer.Tests;

public class UnmatchedCallTests
{
    // Same wire shape as TestMethods.EchoUpper, but never declared on the server.
    private static readonly Method<StringValue, StringValue> _undeclaredLower =
        new(
            MethodType.Unary,
            "e2e.v1.Echo",
            "Lower",
            Marshallers.Create(m => m.ToByteArray(), StringValue.Parser.ParseFrom),
            Marshallers.Create(m => m.ToByteArray(), StringValue.Parser.ParseFrom));

    [Fact]
    public async Task Call_UndeclaredMethod_ReturnsConfiguredGrpcStatus()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Respond(new StringValue { Value = "X" }));

        var invoker = server.CreateCallInvoker();

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            invoker.AsyncUnaryCall(_undeclaredLower, null, new CallOptions(), new StringValue { Value = "A" }).ResponseAsync);

        Assert.Equal(StatusCode.Unimplemented, ex.StatusCode);
    }

    [Fact]
    public async Task Call_UndeclaredMethod_IsRecordedAsUnmatched()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Respond(new StringValue { Value = "X" }));

        var invoker = server.CreateCallInvoker();

        await Assert.ThrowsAsync<RpcException>(() =>
            invoker.AsyncUnaryCall(_undeclaredLower, null, new CallOptions(), new StringValue { Value = "A" }).ResponseAsync);

        var recorded = server.Requests.For("e2e.v1.Echo/Lower");
        Assert.Single(recorded);
        Assert.False(recorded[0].Matched);
        Assert.Equal(StatusCode.Unimplemented, recorded[0].Status.StatusCode);
        Assert.Empty(recorded[0].RequestPayloads);
        Assert.Equal("application/grpc", recorded[0].Headers.GetValue("content-type"));

        Assert.Throws<GrpcMockVerificationException>(() => server.Requests.VerifyNoUnmatchedCalls());
    }

    [Fact]
    public async Task Call_UndeclaredMethod_UsesConfiguredUnmatchedStatus()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(TestMethods.EchoUpperFullName)
            .Respond(new StringValue { Value = "X" })
            .OnUnmatched(new Status(StatusCode.NotFound, "no such method")));

        var invoker = server.CreateCallInvoker();

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            invoker.AsyncUnaryCall(_undeclaredLower, null, new CallOptions(), new StringValue { Value = "A" }).ResponseAsync);

        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
        Assert.Equal("no such method", ex.Status.Detail);
    }
}
