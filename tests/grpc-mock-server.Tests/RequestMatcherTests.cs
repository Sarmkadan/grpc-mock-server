using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace GrpcMockServer.Tests;

public class RequestMatcherTests
{
    [Fact]
    public void Any_AnyRequest_ReturnsTrue()
    {
        var matcher = RequestMatcher.Any<StringValue>();

        Assert.True(matcher.Matches(new StringValue { Value = "anything" }, new Metadata()));
        Assert.True(matcher.Matches(new StringValue(), new Metadata()));
    }

    [Fact]
    public void Where_PredicateTrue_ReturnsTrue()
    {
        var matcher = RequestMatcher.Where<StringValue>("non-empty", r => r.Value.Length > 0);

        Assert.True(matcher.Matches(new StringValue { Value = "x" }, new Metadata()));
    }

    [Fact]
    public void Where_PredicateFalse_ReturnsFalse()
    {
        var matcher = RequestMatcher.Where<StringValue>("non-empty", r => r.Value.Length > 0);

        Assert.False(matcher.Matches(new StringValue(), new Metadata()));
    }

    [Fact]
    public void Where_Description_IsExposed()
    {
        var matcher = RequestMatcher.Where<StringValue>("non-empty", r => r.Value.Length > 0);

        Assert.Equal("non-empty", matcher.Description);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void Where_EvenPredicate_MatchesOnlyEvenValues(int value, bool expected)
    {
        var matcher = RequestMatcher.Where<Int32Value>("even", v => v.Value % 2 == 0);

        Assert.Equal(expected, matcher.Matches(new Int32Value { Value = value }, new Metadata()));
    }

    [Fact]
    public void Equals_SameContent_ReturnsTrue()
    {
        var matcher = RequestMatcher.Equals(new StringValue { Value = "hello" });

        Assert.True(matcher.Matches(new StringValue { Value = "hello" }, new Metadata()));
    }

    [Fact]
    public void Equals_DifferentContent_ReturnsFalse()
    {
        var matcher = RequestMatcher.Equals(new StringValue { Value = "hello" });

        Assert.False(matcher.Matches(new StringValue { Value = "world" }, new Metadata()));
    }

    [Fact]
    public void Equals_Description_ContainsExpectedValue()
    {
        var matcher = RequestMatcher.Equals(new StringValue { Value = "hello" });

        Assert.Contains("hello", matcher.Description);
    }

    [Fact]
    public void Equals_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RequestMatcher.Equals<StringValue>(null!));
    }

    [Fact]
    public void All_AllMatch_ReturnsTrue()
    {
        var matcher = RequestMatcher.All<StringValue>(
            RequestMatcher.Where<StringValue>("non-empty", r => r.Value.Length > 0),
            RequestMatcher.Where<StringValue>("starts with a", r => r.Value.StartsWith('a')));

        Assert.True(matcher.Matches(new StringValue { Value = "abc" }, new Metadata()));
    }

    [Fact]
    public void All_OneFails_ReturnsFalse()
    {
        var matcher = RequestMatcher.All<StringValue>(
            RequestMatcher.Where<StringValue>("non-empty", r => r.Value.Length > 0),
            RequestMatcher.Where<StringValue>("starts with a", r => r.Value.StartsWith('a')));

        Assert.False(matcher.Matches(new StringValue { Value = "xyz" }, new Metadata()));
    }

    [Fact]
    public void All_Empty_ReturnsTrue()
    {
        var matcher = RequestMatcher.All<StringValue>();

        Assert.True(matcher.Matches(new StringValue(), new Metadata()));
    }

    [Fact]
    public void All_Description_JoinsWithAnd()
    {
        var matcher = RequestMatcher.All<StringValue>(
            RequestMatcher.Where<StringValue>("a", _ => true),
            RequestMatcher.Where<StringValue>("b", _ => true));

        Assert.Equal("a AND b", matcher.Description);
    }

    [Fact]
    public void AnyOf_OneMatches_ReturnsTrue()
    {
        var matcher = RequestMatcher.AnyOf<StringValue>(
            RequestMatcher.Where<StringValue>("short", r => r.Value.Length < 3),
            RequestMatcher.Where<StringValue>("long", r => r.Value.Length > 10));

        Assert.True(matcher.Matches(new StringValue { Value = "hi" }, new Metadata()));
    }

    [Fact]
    public void AnyOf_NoneMatch_ReturnsFalse()
    {
        var matcher = RequestMatcher.AnyOf<StringValue>(
            RequestMatcher.Where<StringValue>("short", r => r.Value.Length < 3),
            RequestMatcher.Where<StringValue>("long", r => r.Value.Length > 10));

        Assert.False(matcher.Matches(new StringValue { Value = "medium" }, new Metadata()));
    }

    [Fact]
    public void AnyOf_Empty_ReturnsFalse()
    {
        var matcher = RequestMatcher.AnyOf<StringValue>();

        Assert.False(matcher.Matches(new StringValue(), new Metadata()));
    }

    [Fact]
    public void AnyOf_Description_JoinsWithOr()
    {
        var matcher = RequestMatcher.AnyOf<StringValue>(
            RequestMatcher.Where<StringValue>("a", _ => true),
            RequestMatcher.Where<StringValue>("b", _ => true));

        Assert.Equal("a OR b", matcher.Description);
    }

    [Fact]
    public void Where_NullPredicate_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RequestMatcher.Where<StringValue>("desc", null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Where_BlankDescription_ThrowsArgumentException(string description)
    {
        Assert.Throws<ArgumentException>(() => RequestMatcher.Where<StringValue>(description, _ => true));
    }
}
