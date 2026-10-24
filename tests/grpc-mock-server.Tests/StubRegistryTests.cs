using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace GrpcMockServer.Tests;

public class StubRegistryTests
{
    public const string Method = "reg.v1.Svc/Get";

    [Fact]
    public void FindMatch_NoStubs_ReturnsNull()
    {
        var registry = new StubRegistry();

        var result = registry.FindMatch(Method, Req("a"), new Metadata());

        Assert.Null(result);
    }

    [Fact]
    public void FindMatch_PredicateMatches_ReturnsStub()
    {
        var registry = new StubRegistry();
        var mock = new GrpcMockBuilder(registry);
        mock.ForUnary<StringValue, StringValue>(Method)
            .When(r => r.Value == "a")
            .Respond(new StringValue { Value = "A" });

        var result = registry.FindMatch(Method, Req("a"), new Metadata());

        Assert.NotNull(result);
        Assert.Same(registry.GetStubs(Method)[0], result);
    }

    [Fact]
    public void FindMatch_PredicateFails_ReturnsNull()
    {
        var registry = new StubRegistry();
        var mock = new GrpcMockBuilder(registry);
        mock.ForUnary<StringValue, StringValue>(Method)
            .When(r => r.Value == "a")
            .Respond(new StringValue { Value = "A" });

        var result = registry.FindMatch(Method, Req("b"), new Metadata());

        Assert.Null(result);
    }

    [Fact]
    public void FindMatch_TwoStubsSamePriority_ReturnsFirstRegistered()
    {
        var registry = new StubRegistry();
        var mock = new GrpcMockBuilder(registry);
        mock.ForUnary<StringValue, StringValue>(Method)
            .When(RequestMatcher.Where<StringValue>("first", _ => true))
            .Respond(new StringValue { Value = "first" });
        mock.ForUnary<StringValue, StringValue>(Method)
            .When(RequestMatcher.Where<StringValue>("second", _ => true))
            .Respond(new StringValue { Value = "second" });

        var result = registry.FindMatch(Method, Req("a"), new Metadata());

        Assert.Equal("first", Label(result));
    }

    [Fact]
    public void FindMatch_HigherPriorityRegisteredLater_WinsOverEarlier()
    {
        var registry = new StubRegistry();
        var mock = new GrpcMockBuilder(registry);
        mock.ForUnary<StringValue, StringValue>(Method)
            .When(RequestMatcher.Where<StringValue>("low", _ => true))
            .Respond(new StringValue { Value = "low" });
        mock.ForUnary<StringValue, StringValue>(Method)
            .When(RequestMatcher.Where<StringValue>("high", _ => true))
            .WithPriority(10)
            .Respond(new StringValue { Value = "high" });

        var result = registry.FindMatch(Method, Req("a"), new Metadata());

        Assert.Equal("high", Label(result));
    }

    [Fact]
    public void FindMatch_TimesOne_RemovedAfterFirstMatch()
    {
        var registry = new StubRegistry();
        var mock = new GrpcMockBuilder(registry);
        mock.ForUnary<StringValue, StringValue>(Method)
            .When(RequestMatcher.Where<StringValue>("once", _ => true))
            .Times(1)
            .Respond(new StringValue { Value = "once" });
        mock.ForUnary<StringValue, StringValue>(Method)
            .When(RequestMatcher.Where<StringValue>("fallback", _ => true))
            .Respond(new StringValue { Value = "fallback" });

        var first = registry.FindMatch(Method, Req("a"), new Metadata());
        var second = registry.FindMatch(Method, Req("a"), new Metadata());

        Assert.Equal("once", Label(first));
        Assert.Equal("fallback", Label(second));
        Assert.Equal(["fallback"], registry.GetStubs(Method).Select(Label));
    }

    [Fact]
    public void FindMatch_TimesTwo_MatchesTwiceThenFallsThrough()
    {
        var registry = new StubRegistry();
        var mock = new GrpcMockBuilder(registry);
        mock.ForUnary<StringValue, StringValue>(Method)
            .When(RequestMatcher.Where<StringValue>("limited", _ => true))
            .Times(2)
            .Respond(new StringValue { Value = "limited" });
        mock.ForUnary<StringValue, StringValue>(Method)
            .When(RequestMatcher.Where<StringValue>("fallback", _ => true))
            .Respond(new StringValue { Value = "fallback" });

        var labels = Enumerable.Range(0, 3)
            .Select(_ => Label(registry.FindMatch(Method, Req("a"), new Metadata())))
            .ToArray();

        Assert.Equal(["limited", "limited", "fallback"], labels);
    }

    [Fact]
    public void FindMatch_HeaderMatcher_RequiresHeader()
    {
        var registry = new StubRegistry();
        var mock = new GrpcMockBuilder(registry);
        mock.ForUnary<StringValue, StringValue>(Method)
            .WithHeader("X-Tenant", "acme")
            .Respond(new StringValue { Value = "A" });

        var withoutHeader = registry.FindMatch(Method, Req("a"), new Metadata());
        var withHeader = registry.FindMatch(Method, Req("a"), new Metadata { { "x-tenant", "acme" } });

        Assert.Null(withoutHeader);
        Assert.NotNull(withHeader);
    }

    [Fact]
    public void FindMatch_HeaderPredicate_Evaluated()
    {
        var registry = new StubRegistry();
        var mock = new GrpcMockBuilder(registry);
        mock.ForUnary<StringValue, StringValue>(Method)
            .WithHeader("x-tenant", value => value.StartsWith("ac", StringComparison.Ordinal))
            .Respond(new StringValue { Value = "A" });

        var accepted = registry.FindMatch(Method, Req("a"), new Metadata { { "x-tenant", "acme" } });
        var rejected = registry.FindMatch(Method, Req("a"), new Metadata { { "x-tenant", "zeta" } });

        Assert.NotNull(accepted);
        Assert.Null(rejected);
    }

    [Fact]
    public void Add_SameMethodDifferentType_ThrowsInvalidOperation()
    {
        var registry = new StubRegistry();
        var mock = new GrpcMockBuilder(registry);
        mock.ForUnary<StringValue, StringValue>(Method)
            .Respond(new StringValue { Value = "A" });

        Assert.Throws<InvalidOperationException>(() =>
            mock.ForServerStreaming<StringValue, StringValue>(Method));
    }

    [Fact]
    public void Methods_AfterTwoDeclarations_ContainsBoth()
    {
        var registry = new StubRegistry();
        var mock = new GrpcMockBuilder(registry);
        mock.ForUnary<StringValue, StringValue>(Method);
        mock.ForServerStreaming<StringValue, Int32Value>("reg.v1.Svc/List");

        var methods = registry.Methods;

        Assert.Equal(2, methods.Count);
        Assert.Equal(MethodType.Unary, methods[Method].Type);
        Assert.Equal(MethodType.ServerStreaming, methods["reg.v1.Svc/List"].Type);
        Assert.Equal(typeof(Int32Value), methods["reg.v1.Svc/List"].ResponseType);
    }

    [Fact]
    public void UnmatchedStatus_Default_IsUnimplemented()
    {
        var registry = new StubRegistry();

        Assert.Equal(StatusCode.Unimplemented, registry.UnmatchedStatus.StatusCode);
    }

    [Fact]
    public void OnUnmatched_SetsUnmatchedStatus()
    {
        var registry = new StubRegistry();
        var mock = new GrpcMockBuilder(registry);

        mock.OnUnmatched(new Status(StatusCode.NotFound, "no such stub"));

        Assert.Equal(StatusCode.NotFound, registry.UnmatchedStatus.StatusCode);
        Assert.Equal("no such stub", registry.UnmatchedStatus.Detail);
    }

    [Fact]
    public void ClearStubs_KeepsMethods()
    {
        var registry = new StubRegistry();
        var mock = new GrpcMockBuilder(registry);
        mock.ForUnary<StringValue, StringValue>(Method)
            .Respond(new StringValue { Value = "A" });

        registry.ClearStubs();

        Assert.Empty(registry.GetStubs(Method));
        Assert.Null(registry.FindMatch(Method, Req("a"), new Metadata()));
        Assert.Contains(Method, registry.Methods.Keys);
    }

    private static StringValue Req(string value) => new() { Value = value };

    /// <summary>Returns the matcher label of a stub, or a placeholder so failures print something readable.</summary>
    private static string Label(StubDefinition? stub) =>
        stub is null ? "<none>" : string.Join(",", stub.MatcherDescriptions);
}
