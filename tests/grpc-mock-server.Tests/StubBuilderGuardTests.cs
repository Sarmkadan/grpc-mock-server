using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace GrpcMockServer.Tests;

public class StubBuilderGuardTests
{
    private static IUnaryStubBuilder<StringValue, StringValue> Unary() =>
        new GrpcMockBuilder(new StubRegistry()).ForUnary<StringValue, StringValue>("Service/Method");

    private static IServerStreamingStubBuilder<StringValue, StringValue> ServerStreaming() =>
        new GrpcMockBuilder(new StubRegistry()).ForServerStreaming<StringValue, StringValue>("Service/Method");

    private static IClientStreamingStubBuilder<StringValue, StringValue> ClientStreaming() =>
        new GrpcMockBuilder(new StubRegistry()).ForClientStreaming<StringValue, StringValue>("Service/Method");

    private static IDuplexStreamingStubBuilder<StringValue, StringValue> Duplex() =>
        new GrpcMockBuilder(new StubRegistry()).ForDuplexStreaming<StringValue, StringValue>("Service/Method");

    // ---- When(...) ----

    [Fact]
    public void When_NullPredicate_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            Unary().When((Func<StringValue, bool>)null!));

        Assert.Equal("predicate", ex.ParamName);
    }

    [Fact]
    public void When_NullMatcher_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            Unary().When((IRequestMatcher<StringValue>)null!));

        Assert.Equal("matcher", ex.ParamName);
    }

    // ---- WithHeader(...) ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithHeader_NullOrWhitespaceKey_ThrowsArgumentException(string? key)
    {
        var builder = Unary();

        var valueEx = Assert.ThrowsAny<ArgumentException>(() => builder.WithHeader(key!, "v"));
        var predicateEx = Assert.ThrowsAny<ArgumentException>(() => builder.WithHeader(key!, _ => true));

        Assert.Equal("key", valueEx.ParamName);
        Assert.Equal("key", predicateEx.ParamName);
    }

    [Fact]
    public void WithHeader_NullValue_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            Unary().WithHeader("x-tenant", (string)null!));

        Assert.Equal("value", ex.ParamName);
    }

    [Fact]
    public void WithHeader_NullPredicate_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            Unary().WithHeader("x-tenant", (Func<string, bool>)null!));

        Assert.Equal("predicate", ex.ParamName);
    }

    [Theory]
    [InlineData("x-trace-bin")]
    [InlineData("X-Trace-BIN")]
    public void WithHeader_BinaryKey_ThrowsArgumentException(string key)
    {
        var builder = Unary();

        var valueEx = Assert.Throws<ArgumentException>(() => builder.WithHeader(key, "v"));
        var predicateEx = Assert.Throws<ArgumentException>(() => builder.WithHeader(key, _ => true));

        Assert.Equal("key", valueEx.ParamName);
        Assert.Contains("binary", valueEx.Message);
        Assert.Equal("key", predicateEx.ParamName);
    }

    [Fact]
    public void WithHeader_TextKey_IsAccepted()
    {
        var builder = Unary();

        builder.WithHeader("x-tenant", "acme").WithHeader("x-region", _ => true);
    }

    // ---- WithDelay / Times / Priority ----

    [Fact]
    public void WithDelay_Negative_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Unary().WithDelay(TimeSpan.FromMilliseconds(-1)));

        Assert.Equal("delay", ex.ParamName);
    }

    [Fact]
    public void WithDelay_Zero_IsAccepted()
    {
        Unary().WithDelay(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Times_ZeroOrNegative_ThrowsArgumentOutOfRangeException(int count)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Unary().Times(count));

        Assert.Equal("count", ex.ParamName);
    }

    [Fact]
    public void Times_Positive_IsAccepted()
    {
        Unary().Times(1);
    }

    // ---- WithResponseHeaders / WithResponseTrailers ----

    [Fact]
    public void WithResponseHeaders_Null_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            Unary().WithResponseHeaders(null!));

        Assert.Equal("headers", ex.ParamName);
    }

    [Fact]
    public void WithResponseTrailers_Null_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            Unary().WithResponseTrailers(null!));

        Assert.Equal("trailers", ex.ParamName);
    }

    [Theory]
    [InlineData("grpc-status")]
    [InlineData("grpc-message")]
    [InlineData("grpc-custom-thing")]
    public void WithResponseTrailers_ReservedGrpcKey_ThrowsArgumentException(string key)
    {
        var trailers = new Metadata { { key, "x" } };

        var ex = Assert.Throws<ArgumentException>(() => Unary().WithResponseTrailers(trailers));

        Assert.Equal("trailers", ex.ParamName);
        Assert.Contains(key, ex.Message);
    }

    [Fact]
    public void WithResponseTrailers_ApplicationKey_IsAccepted()
    {
        var trailers = new Metadata { { "x-checksum", "abc" } };

        Unary().WithResponseTrailers(trailers);
    }

    // ---- Unary finalizers ----

    [Fact]
    public void Respond_NullResponse_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            Unary().Respond((StringValue)null!));

        Assert.Equal("response", ex.ParamName);
    }

    [Fact]
    public void Respond_NullFactory_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            Unary().Respond((Func<StringValue, StringValue>)null!));

        Assert.Equal("factory", ex.ParamName);
    }

    [Fact]
    public void Respond_NullAsyncFactory_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            Unary().Respond((Func<StringValue, ServerCallContext, Task<StringValue>>)null!));

        Assert.Equal("factory", ex.ParamName);
    }

    [Fact]
    public void Fail_NullRpcException_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => Unary().Fail((RpcException)null!));

        Assert.Equal("exception", ex.ParamName);
    }

    [Fact]
    public void RespondInSequence_NoResponses_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => Unary().RespondInSequence());

        Assert.Equal("responses", ex.ParamName);
    }

    [Fact]
    public void RespondInSequence_NullArray_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            Unary().RespondInSequence((StringValue[])null!));

        Assert.Equal("responses", ex.ParamName);
    }

    [Fact]
    public void Fail_OkStatus_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => Unary().Fail(StatusCode.OK));

        Assert.Equal("statusCode", ex.ParamName);
    }

    // ---- Server-streaming finalizers ----

    [Fact]
    public void Stream_NullArray_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            ServerStreaming().Stream((StringValue[])null!));

        Assert.Equal("responses", ex.ParamName);
    }

    [Fact]
    public void ServerStreaming_Fail_OkStatus_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => ServerStreaming().Fail(StatusCode.OK));

        Assert.Equal("statusCode", ex.ParamName);
    }

    [Fact]
    public void StreamThenFail_OkStatus_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            ServerStreaming().StreamThenFail(StatusCode.OK, new StringValue { Value = "a" }));

        Assert.Equal("statusCode", ex.ParamName);
    }

    // ---- Client-streaming finalizers ----

    [Fact]
    public void ClientStreaming_Fail_OkStatus_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => ClientStreaming().Fail(StatusCode.OK));

        Assert.Equal("statusCode", ex.ParamName);
    }

    // ---- Duplex finalizers ----

    [Fact]
    public void Duplex_Fail_OkStatus_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => Duplex().Fail(StatusCode.OK));

        Assert.Equal("statusCode", ex.ParamName);
    }

    // ---- Finalization ----

    [Fact]
    public void Rejected_Finalizer_LeavesBuilderUsable()
    {
        var registry = new StubRegistry();
        var builder = new GrpcMockBuilder(registry).ForUnary<StringValue, StringValue>("Service/Method");

        Assert.Throws<ArgumentException>(() => builder.Fail(StatusCode.OK));
        Assert.Throws<ArgumentNullException>(() => builder.Respond((StringValue)null!));
        Assert.Empty(registry.GetStubs("Service/Method"));

        builder.Respond(new StringValue { Value = "ok" });

        Assert.Single(registry.GetStubs("Service/Method"));
    }

    [Fact]
    public void Unary_SecondFinalizer_ThrowsInvalidOperationException()
    {
        var registry = new StubRegistry();
        var builder = new GrpcMockBuilder(registry).ForUnary<StringValue, StringValue>("Service/Method");
        builder.Respond(new StringValue { Value = "first" });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.Respond(new StringValue { Value = "second" }));

        Assert.Equal("Stub already finalized", ex.Message);
        Assert.Single(registry.GetStubs("Service/Method"));
    }

    [Fact]
    public void Unary_FinalizerAfterFail_ThrowsInvalidOperationException()
    {
        var builder = Unary();
        builder.Fail(StatusCode.NotFound);

        Assert.Throws<InvalidOperationException>(() => builder.Fail(StatusCode.Internal));
    }

    [Fact]
    public void Unary_StepAfterFinalize_ThrowsInvalidOperationException()
    {
        var builder = Unary();
        builder.Respond(new StringValue { Value = "ok" });

        Assert.Throws<InvalidOperationException>(() => builder.When(_ => true));
        Assert.Throws<InvalidOperationException>(() => builder.WithHeader("x-tenant", "acme"));
        Assert.Throws<InvalidOperationException>(() => builder.WithPriority(5));
        Assert.Throws<InvalidOperationException>(() => builder.WithDelay(TimeSpan.Zero));
        Assert.Throws<InvalidOperationException>(() => builder.Times(1));
        Assert.Throws<InvalidOperationException>(() => builder.WithResponseHeaders(new Metadata()));
        Assert.Throws<InvalidOperationException>(() => builder.WithResponseTrailers(new Metadata()));
    }

    [Fact]
    public void ServerStreaming_SecondFinalizer_ThrowsInvalidOperationException()
    {
        var builder = ServerStreaming();
        builder.Stream(new StringValue { Value = "a" });

        Assert.Throws<InvalidOperationException>(() => builder.Stream(new StringValue { Value = "b" }));
        Assert.Throws<InvalidOperationException>(() => builder.Fail(StatusCode.Internal));
    }

    [Fact]
    public void ClientStreaming_SecondFinalizer_ThrowsInvalidOperationException()
    {
        var builder = ClientStreaming();
        builder.Respond(new StringValue { Value = "a" });

        Assert.Throws<InvalidOperationException>(() => builder.Fail(StatusCode.Internal));
    }

    [Fact]
    public void Duplex_SecondFinalizer_ThrowsInvalidOperationException()
    {
        var builder = Duplex();
        builder.Echo(r => r);

        Assert.Throws<InvalidOperationException>(() => builder.Echo(r => r));
        Assert.Throws<InvalidOperationException>(() => builder.Fail(StatusCode.Internal));
    }
}
