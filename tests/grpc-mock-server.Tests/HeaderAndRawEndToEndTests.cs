using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace GrpcMockServer.Tests;

/// <summary>
/// End-to-end tests for header-based routing, raw <c>byte[]</c> stubs and <see cref="RequestMatcher.Equals{TRequest}"/>,
/// driven through a real <see cref="GrpcMockServer"/> in TestHost mode.
/// </summary>
public class HeaderAndRawEndToEndTests
{
    private const string _getFullName = "hdr.v1.Svc/Get";
    private const string _rawPingFullName = "raw.v1.Svc/Ping";

    private static readonly Method<StringValue, StringValue> _get =
        TestMethods.Unary<StringValue, StringValue>("hdr.v1.Svc", "Get");

    private static readonly Marshaller<byte[]> _identityBytes =
        Marshallers.Create<byte[]>(b => b, b => b);

    private static readonly Method<byte[], byte[]> _ping =
        new(MethodType.Unary, "raw.v1.Svc", "Ping", _identityBytes, _identityBytes);

    [Fact]
    public async Task WithHeader_ExactValue_RoutesToStub()
    {
        await using var server = await StartTenantRoutingAsync();
        var invoker = server.CreateCallInvoker();

        var response = await invoker.AsyncUnaryCall(
            _get, null, WithTenant("acme"), new StringValue { Value = "q" });

        Assert.Equal("exact", response.Value);
    }

    [Fact]
    public async Task WithHeader_KeyCaseInsensitive_Matches()
    {
        await using var server = await StartTenantRoutingAsync();
        var invoker = server.CreateCallInvoker();

        // Metadata lowercases keys on the client, so the wire carries "x-tenant"; the server-side comparison
        // is still the case-insensitive path under test. The key is written in mixed case to show intent.
        var headers = new Metadata { { "X-Tenant", "acme" } };

        var response = await invoker.AsyncUnaryCall(
            _get, null, new CallOptions(headers: headers), new StringValue { Value = "q" });

        Assert.Equal("exact", response.Value);
    }

    [Fact]
    public async Task WithHeader_Predicate_RoutesToStub()
    {
        await using var server = await StartTenantRoutingAsync();
        var invoker = server.CreateCallInvoker();

        var response = await invoker.AsyncUnaryCall(
            _get, null, WithTenant("beta-eu"), new StringValue { Value = "q" });

        Assert.Equal("predicate", response.Value);
    }

    [Fact]
    public async Task WithHeader_Missing_FallsThroughToUnmatched()
    {
        await using var server = await StartTenantRoutingAsync();
        var invoker = server.CreateCallInvoker();

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            invoker.AsyncUnaryCall(_get, null, new CallOptions(), new StringValue { Value = "q" }).ResponseAsync);

        Assert.Equal(StatusCode.Unimplemented, ex.StatusCode);
        var call = Assert.Single(server.Requests.For(_getFullName));
        Assert.False(call.Matched);
    }

    [Fact]
    public async Task WithHeader_CombinedWithWhen_BothRequired()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(_getFullName)
            .When(r => r.Value == "vip")
            .WithHeader("x-tenant", "acme")
            .Respond(new StringValue { Value = "vip-acme" }));

        var invoker = server.CreateCallInvoker();

        var matched = await invoker.AsyncUnaryCall(
            _get, null, WithTenant("acme"), new StringValue { Value = "vip" });
        Assert.Equal("vip-acme", matched.Value);

        // Header present but request body fails the When predicate.
        await Assert.ThrowsAsync<RpcException>(() =>
            invoker.AsyncUnaryCall(_get, null, WithTenant("acme"), new StringValue { Value = "regular" }).ResponseAsync);

        // Request body matches but the header is absent.
        await Assert.ThrowsAsync<RpcException>(() =>
            invoker.AsyncUnaryCall(_get, null, new CallOptions(), new StringValue { Value = "vip" }).ResponseAsync);

        Assert.Equal(2, server.Requests.For(_getFullName).Count(c => !c.Matched));
        Assert.Single(server.Requests.For(_getFullName), c => c.Matched);
    }

    [Fact]
    public async Task RequestMatcher_Equals_UsedInWhen_MatchesProtobufEquality()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForUnary<StringValue, StringValue>(_getFullName)
            .When(RequestMatcher.Equals(new StringValue { Value = "x" }))
            .Respond(new StringValue { Value = "equal" }));

        var invoker = server.CreateCallInvoker();

        // A separately constructed message with the same content is protobuf-equal to the expected one.
        var response = await invoker.AsyncUnaryCall(
            _get, null, new CallOptions(), new StringValue { Value = "x" });
        Assert.Equal("equal", response.Value);

        await Assert.ThrowsAsync<RpcException>(() =>
            invoker.AsyncUnaryCall(_get, null, new CallOptions(), new StringValue { Value = "y" }).ResponseAsync);
    }

    [Fact(Skip = "ForRaw is not implemented yet: GrpcMockBuilder.ForRaw throws NotImplementedException.")]
    public async Task ForRaw_UnaryBytes_RoundTrips()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForRaw(_rawPingFullName)
            .When(b => b.Length > 0)
            .Respond(b => b));

        var invoker = server.CreateCallInvoker();
        var requestBytes = new StringValue { Value = "ping" }.ToByteArray();

        var responseBytes = await invoker.AsyncUnaryCall(
            _ping, null, new CallOptions(), requestBytes);

        Assert.Equal("ping", StringValue.Parser.ParseFrom(responseBytes).Value);
    }

    [Fact(Skip = "ForRaw is not implemented yet: GrpcMockBuilder.ForRaw throws NotImplementedException.")]
    public async Task ForRaw_StatusFailure_Propagates()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForRaw(_rawPingFullName)
            .Fail(StatusCode.PermissionDenied, "nope"));

        var invoker = server.CreateCallInvoker();
        var requestBytes = new StringValue { Value = "ping" }.ToByteArray();

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            invoker.AsyncUnaryCall(_ping, null, new CallOptions(), requestBytes).ResponseAsync);

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        Assert.Equal("nope", ex.Status.Detail);
    }

    [Fact(Skip = "ForRaw is not implemented yet: GrpcMockBuilder.ForRaw throws NotImplementedException.")]
    public async Task ForRaw_RecordedPayload_IsRawBytes()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForRaw(_rawPingFullName)
            .Respond(b => b));

        var invoker = server.CreateCallInvoker();
        var requestBytes = new StringValue { Value = "ping" }.ToByteArray();

        await invoker.AsyncUnaryCall(_ping, null, new CallOptions(), requestBytes);

        var call = Assert.Single(server.Requests.For(_rawPingFullName));
        Assert.Equal(requestBytes, call.RequestPayloads[0].ToArray());

        // The log keeps raw bytes, so typed access still deserializes them on demand.
        var typed = Assert.Single(server.Requests.RequestsFor<StringValue>(_rawPingFullName));
        Assert.Equal("ping", typed.Value);
    }

    [Fact(Skip = "ForRaw is not implemented yet: GrpcMockBuilder.ForRaw throws NotImplementedException.")]
    public void ForRaw_StreamingType_ThrowsNotSupported()
    {
        var builder = new GrpcMockBuilder(new StubRegistry());

        Assert.Throws<NotSupportedException>(() =>
            builder.ForRaw("raw.v1.Svc/Stream", MethodType.ServerStreaming));
    }

    /// <summary>
    /// Two unary stubs on the same method that differ only by their <c>x-tenant</c> header matcher:
    /// an exact value and a prefix predicate.
    /// </summary>
    private static Task<GrpcMockServer> StartTenantRoutingAsync() => GrpcMockServer.StartAsync(mock =>
    {
        mock.ForUnary<StringValue, StringValue>(_getFullName)
            .WithHeader("x-tenant", "acme")
            .Respond(new StringValue { Value = "exact" });

        mock.ForUnary<StringValue, StringValue>(_getFullName)
            .WithHeader("x-tenant", t => t.StartsWith("beta", StringComparison.Ordinal))
            .Respond(new StringValue { Value = "predicate" });
    });

    private static CallOptions WithTenant(string tenant) =>
        new(headers: new Metadata { { "x-tenant", tenant } });
}
