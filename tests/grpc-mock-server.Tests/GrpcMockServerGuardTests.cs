using Google.Protobuf.WellKnownTypes;

namespace GrpcMockServer.Tests;

public class GrpcMockServerGuardTests
{
    // ---- Disposed server ----

    [Fact]
    public async Task CreateChannel_AfterDispose_ThrowsObjectDisposedException()
    {
        var server = await StartServerAsync();
        await server.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => server.CreateChannel());
    }

    [Fact]
    public async Task Requests_AfterDispose_ThrowsObjectDisposedException()
    {
        var server = await StartServerAsync();
        await server.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => server.Requests);
    }

    [Fact]
    public async Task BaseAddress_AfterDispose_ThrowsObjectDisposedException()
    {
        var server = await StartServerAsync();
        await server.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => server.BaseAddress);
    }

    [Fact]
    public async Task Configure_AfterDispose_ThrowsObjectDisposedException()
    {
        var server = await StartServerAsync();
        await server.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => server.Configure(mock => mock
            .ForUnary<StringValue, StringValue>("guard.v1.Svc/Echo")
            .Respond(new StringValue())));
    }

    [Fact]
    public async Task Reset_AfterDispose_ThrowsObjectDisposedException()
    {
        var server = await StartServerAsync();
        await server.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => server.Reset());
    }

    [Fact]
    public async Task CreateCallInvoker_AfterDispose_ThrowsObjectDisposedException()
    {
        var server = await StartServerAsync();
        await server.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => server.CreateCallInvoker());
    }

    // ---- StartAsync option validation ----

    [Fact]
    public async Task StartAsync_PortOutOfRange_ThrowsArgumentExceptionNamingPort()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => GrpcMockServer.StartAsync(
            mock => mock.ForUnary<StringValue, StringValue>("guard.v1.Svc/Echo").Respond(new StringValue()),
            new GrpcMockOptions { Port = 70000 }));

        Assert.Equal("Port", ex.ParamName);
    }

    [Fact]
    public async Task StartAsync_MaxRecordedCallsZero_ThrowsArgumentExceptionNamingMaxRecordedCalls()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => GrpcMockServer.StartAsync(
            mock => mock.ForUnary<StringValue, StringValue>("guard.v1.Svc/Echo").Respond(new StringValue()),
            new GrpcMockOptions { MaxRecordedCalls = 0 }));

        Assert.Equal("MaxRecordedCalls", ex.ParamName);
    }

    // ---- RequestLog argument guards ----

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RequestLog_NonPositiveCapacity_ThrowsArgumentOutOfRangeException(int maxRecordedCalls)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new RequestLog(maxRecordedCalls));

        Assert.Equal("maxRecordedCalls", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequestLog_For_NullOrWhitespaceName_ThrowsArgumentException(string? name)
    {
        var log = new RequestLog(10);

        var ex = Assert.ThrowsAny<ArgumentException>(() => log.For(name!));

        Assert.Equal("fullMethodName", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequestLog_RequestsFor_NullOrWhitespaceName_ThrowsArgumentException(string? name)
    {
        var log = new RequestLog(10);

        var ex = Assert.ThrowsAny<ArgumentException>(() => log.RequestsFor<StringValue>(name!));

        Assert.Equal("fullMethodName", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequestLog_VerifyCalled_NullOrWhitespaceName_ThrowsArgumentException(string? name)
    {
        var log = new RequestLog(10);

        var ex = Assert.ThrowsAny<ArgumentException>(() => log.VerifyCalled(name!, 0));

        Assert.Equal("fullMethodName", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequestLog_VerifyCalledWith_NullOrWhitespaceName_ThrowsArgumentException(string? name)
    {
        var log = new RequestLog(10);

        var ex = Assert.ThrowsAny<ArgumentException>(() =>
            log.VerifyCalledWith<StringValue>(name!, _ => true));

        Assert.Equal("fullMethodName", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequestLog_VerifyNotCalled_NullOrWhitespaceName_ThrowsArgumentException(string? name)
    {
        var log = new RequestLog(10);

        var ex = Assert.ThrowsAny<ArgumentException>(() => log.VerifyNotCalled(name!));

        Assert.Equal("fullMethodName", ex.ParamName);
    }

    [Fact]
    public void RequestLog_VerifyCalled_NegativeExpectedCount_ThrowsArgumentOutOfRangeException()
    {
        var log = new RequestLog(10);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => log.VerifyCalled("guard.v1.Svc/Echo", -1));

        Assert.Equal("expectedCount", ex.ParamName);
    }

    [Fact]
    public void RequestLog_VerifyCalledWith_NullPredicate_ThrowsArgumentNullException()
    {
        var log = new RequestLog(10);

        var ex = Assert.Throws<ArgumentNullException>(() =>
            log.VerifyCalledWith<StringValue>("guard.v1.Svc/Echo", null!));

        Assert.Equal("predicate", ex.ParamName);
    }

    // ---- RecordedCall.Request<T>() ----

    [Fact]
    public void RecordedCall_RequestWithoutPayload_ThrowsInvalidOperationExceptionNamingMethod()
    {
        var call = new RecordedCall(
            "guard.v1.Svc/Echo",
            Grpc.Core.MethodType.Unary,
            new Grpc.Core.Metadata(),
            Array.Empty<ReadOnlyMemory<byte>>(),
            Grpc.Core.Status.DefaultSuccess,
            Matched: false,
            ReceivedAt: DateTimeOffset.UtcNow);

        var ex = Assert.Throws<InvalidOperationException>(() => call.Request<StringValue>());

        Assert.Contains("guard.v1.Svc/Echo", ex.Message);
    }

    private static Task<GrpcMockServer> StartServerAsync() => GrpcMockServer.StartAsync(mock => mock
        .ForUnary<StringValue, StringValue>("guard.v1.Svc/Echo")
        .Respond(new StringValue()));
}
