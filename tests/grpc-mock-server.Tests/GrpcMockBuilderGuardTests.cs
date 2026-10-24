using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace GrpcMockServer.Tests;

public class GrpcMockBuilderGuardTests
{
    [Theory]
    [InlineData("users.v1.UserService/GetUser")]
    [InlineData("Service/Method")]
    [InlineData("a.b.c.D_E/f_1")]
    public void ForUnary_ValidName_RegistersMethod(string fullMethodName)
    {
        var registry = new StubRegistry();
        var builder = new GrpcMockBuilder(registry);

        builder.ForUnary<StringValue, StringValue>(fullMethodName);

        Assert.Contains(fullMethodName, registry.RegisteredMethods);
    }

    [Theory]
    [InlineData("/users.v1.UserService/GetUser", "must not start with '/'")]
    [InlineData("/Service/Method", "must not start with '/'")]
    [InlineData("users.v1.UserService", "is missing the '/'")]
    [InlineData("Service", "is missing the '/'")]
    [InlineData("a/b/c", "contains 2 '/' characters")]
    [InlineData("users.v1.UserService//GetUser", "contains 2 '/' characters")]
    [InlineData("Service/", "has an empty method name")]
    [InlineData("/", "must not start with '/'")]
    [InlineData(".Service/Method", "has an empty package or service segment")]
    [InlineData("users..UserService/GetUser", "has an empty package or service segment")]
    [InlineData("Serv ice/Method", "has an invalid service segment 'Serv ice'")]
    [InlineData("Service/Met-hod", "has an invalid method name 'Met-hod'")]
    [InlineData("Service/Method.Name", "has an invalid method name 'Method.Name'")]
    [InlineData("1Service/Method", "has an invalid service segment '1Service'")]
    public void ForUnary_InvalidName_ThrowsArgumentExceptionNamingProblem(string fullMethodName, string expected)
    {
        var builder = new GrpcMockBuilder(new StubRegistry());

        var ex = Assert.Throws<ArgumentException>(() =>
            builder.ForUnary<StringValue, StringValue>(fullMethodName));

        Assert.Equal("fullMethodName", ex.ParamName);
        Assert.Contains(expected, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForUnary_NullOrWhitespaceName_ThrowsArgumentException(string? fullMethodName)
    {
        var builder = new GrpcMockBuilder(new StubRegistry());

        var ex = Assert.ThrowsAny<ArgumentException>(() =>
            builder.ForUnary<StringValue, StringValue>(fullMethodName!));

        Assert.Equal("fullMethodName", ex.ParamName);
    }

    [Theory]
    [InlineData("ForServerStreaming")]
    [InlineData("ForClientStreaming")]
    [InlineData("ForDuplexStreaming")]
    [InlineData("ForRaw")]
    public void OtherEntryPoints_InvalidName_ThrowsArgumentException(string entryPoint)
    {
        var builder = new GrpcMockBuilder(new StubRegistry());
        const string bad = "/users.v1.UserService/GetUser";

        var ex = Assert.Throws<ArgumentException>(() => Invoke(builder, entryPoint, bad));

        Assert.Equal("fullMethodName", ex.ParamName);
        Assert.Contains("must not start with '/'", ex.Message);
    }

    [Fact]
    public void ForRaw_UndefinedMethodType_ThrowsArgumentOutOfRangeException()
    {
        var builder = new GrpcMockBuilder(new StubRegistry());

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            builder.ForRaw("Service/Method", (MethodType)99));

        Assert.Equal("type", ex.ParamName);
    }

    [Fact]
    public void OnUnmatched_OkStatus_ThrowsArgumentException()
    {
        var builder = new GrpcMockBuilder(new StubRegistry());

        var ex = Assert.Throws<ArgumentException>(() =>
            builder.OnUnmatched(new Status(StatusCode.OK, "")));

        Assert.Equal("status", ex.ParamName);
    }

    [Fact]
    public void OnUnmatched_NonOkStatus_IsAccepted()
    {
        var registry = new StubRegistry();
        var builder = new GrpcMockBuilder(registry);

        builder.OnUnmatched(new Status(StatusCode.NotFound, "no such method"));

        Assert.Equal(StatusCode.NotFound, registry.UnmatchedStatus.StatusCode);
    }

    [Fact]
    public void Add_SameMethodWithDifferentTypePair_ThrowsInvalidOperationExceptionWithBothPairs()
    {
        var registry = new StubRegistry();
        registry.Add(CreateStub(typeof(StringValue), typeof(StringValue)));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.Add(CreateStub(typeof(Int32Value), typeof(StringValue))));

        Assert.Contains(typeof(StringValue).FullName!, ex.Message);
        Assert.Contains(typeof(Int32Value).FullName!, ex.Message);
    }

    [Fact]
    public void Add_SameMethodWithSameTypes_IsAccepted()
    {
        var registry = new StubRegistry();
        registry.Add(CreateStub(typeof(StringValue), typeof(StringValue)));

        registry.Add(CreateStub(typeof(StringValue), typeof(StringValue)));

        Assert.Equal(2, registry.GetStubs("Service/Method").Count);
    }

    private static StubDefinition CreateStub(System.Type requestType, System.Type responseType) => new()
    {
        FullMethodName = "Service/Method",
        Type = MethodType.Unary,
        RequestType = requestType,
        ResponseType = responseType,
        Matches = (_, _) => true,
        Handler = new object(),
    };

    private static void Invoke(GrpcMockBuilder builder, string entryPoint, string fullMethodName)
    {
        switch (entryPoint)
        {
            case "ForServerStreaming":
                builder.ForServerStreaming<StringValue, StringValue>(fullMethodName);
                break;
            case "ForClientStreaming":
                builder.ForClientStreaming<StringValue, StringValue>(fullMethodName);
                break;
            case "ForDuplexStreaming":
                builder.ForDuplexStreaming<StringValue, StringValue>(fullMethodName);
                break;
            case "ForRaw":
                builder.ForRaw(fullMethodName);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entryPoint), entryPoint, null);
        }
    }
}
