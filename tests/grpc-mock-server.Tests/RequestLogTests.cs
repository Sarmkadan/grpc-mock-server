using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace GrpcMockServer.Tests;

public class RequestLogTests
{
    [Fact]
    public void Record_TwoCalls_AllReturnsInOrder()
    {
        var log = new RequestLog(10);
        var first = Call("test.Svc/Alpha");
        var second = Call("test.Svc/Beta");

        log.Record(first);
        log.Record(second);

        Assert.Equal(2, log.All.Count);
        Assert.Same(first, log.All[0]);
        Assert.Same(second, log.All[1]);
    }

    [Fact]
    public void Record_ExceedsMax_DropsOldest()
    {
        var log = new RequestLog(3);
        for (var i = 0; i < 5; i++)
        {
            log.Record(Call($"test.Svc/M{i}"));
        }

        Assert.Equal(new[] { "test.Svc/M2", "test.Svc/M3", "test.Svc/M4" }, log.All.Select(c => c.FullMethodName));
    }

    [Fact]
    public void For_MixedMethods_FiltersByName()
    {
        var log = new RequestLog(10);
        var a1 = Call("test.Svc/Alpha");
        var b1 = Call("test.Svc/Beta");
        var a2 = Call("test.Svc/Alpha");
        log.Record(a1);
        log.Record(b1);
        log.Record(a2);

        var alphaCalls = log.For("test.Svc/Alpha");

        Assert.Equal(2, alphaCalls.Count);
        Assert.Same(a1, alphaCalls[0]);
        Assert.Same(a2, alphaCalls[1]);
    }

    [Fact]
    public void RequestsFor_StringValues_DeserializesEach()
    {
        var log = new RequestLog(10);
        log.Record(Call("test.Svc/Alpha", true, new StringValue { Value = "one" }));
        log.Record(Call("test.Svc/Beta", true, new StringValue { Value = "other" }));
        log.Record(Call("test.Svc/Alpha", true, new StringValue { Value = "two" }));

        var requests = log.RequestsFor<StringValue>("test.Svc/Alpha");

        Assert.Equal(new[] { "one", "two" }, requests.Select(r => r.Value));
    }

    [Fact]
    public void Request_EmptyPayloads_ThrowsInvalidOperation()
    {
        var call = Call("test.Svc/Alpha");

        Assert.Throws<InvalidOperationException>(() => call.Request<StringValue>());
    }

    [Fact]
    public void VerifyCalled_ExactCount_DoesNotThrow()
    {
        var log = new RequestLog(10);
        log.Record(Call("test.Svc/Alpha"));
        log.Record(Call("test.Svc/Alpha"));
        log.Record(Call("test.Svc/Beta"));

        log.VerifyCalled("test.Svc/Alpha", 2);
    }

    [Fact]
    public void VerifyCalled_WrongCount_ThrowsWithRecordedCallsInMessage()
    {
        var log = new RequestLog(10);
        log.Record(Call("test.Svc/Alpha"));

        var ex = Assert.Throws<GrpcMockVerificationException>(() => log.VerifyCalled("test.Svc/Alpha", 3));

        Assert.Contains("test.Svc/Alpha", ex.Message);
        Assert.Contains("3 time(s)", ex.Message);
        Assert.Contains("1 time(s)", ex.Message);
        Assert.Contains("Recorded calls:", ex.Message);
    }

    [Fact]
    public void VerifyCalledWith_MatchingPredicate_DoesNotThrow()
    {
        var log = new RequestLog(10);
        log.Record(Call("test.Svc/Alpha", true, new StringValue { Value = "hello" }));

        log.VerifyCalledWith<StringValue>("test.Svc/Alpha", r => r.Value == "hello");
    }

    [Fact]
    public void VerifyCalledWith_NoMatch_Throws()
    {
        var log = new RequestLog(10);
        log.Record(Call("test.Svc/Alpha", true, new StringValue { Value = "hello" }));

        Assert.Throws<GrpcMockVerificationException>(() =>
            log.VerifyCalledWith<StringValue>("test.Svc/Alpha", r => r.Value == "goodbye"));
    }

    [Fact]
    public void VerifyCalledWith_UndeserializablePayload_SkipsAndThrows()
    {
        var log = new RequestLog(10);
        log.Record(Call("test.Svc/Alpha") with { RequestPayloads = new ReadOnlyMemory<byte>[] { new byte[] { 0xFF, 0xFF } } });

        // The predicate always accepts, so the only way this throws is if the garbage payload was skipped.
        Assert.Throws<GrpcMockVerificationException>(() =>
            log.VerifyCalledWith<StringValue>("test.Svc/Alpha", _ => true));
    }

    [Fact]
    public void VerifyNotCalled_NeverCalled_DoesNotThrow()
    {
        var log = new RequestLog(10);
        log.Record(Call("test.Svc/Beta"));

        log.VerifyNotCalled("test.Svc/Alpha");
    }

    [Fact]
    public void VerifyNotCalled_Called_Throws()
    {
        var log = new RequestLog(10);
        log.Record(Call("test.Svc/Alpha"));

        Assert.Throws<GrpcMockVerificationException>(() => log.VerifyNotCalled("test.Svc/Alpha"));
    }

    [Fact]
    public void VerifyNoUnmatchedCalls_AllMatched_DoesNotThrow()
    {
        var log = new RequestLog(10);
        log.Record(Call("test.Svc/Alpha", matched: true));
        log.Record(Call("test.Svc/Beta", matched: true));

        log.VerifyNoUnmatchedCalls();
    }

    [Fact]
    public void VerifyNoUnmatchedCalls_OneUnmatched_ThrowsAndNamesMethod()
    {
        var log = new RequestLog(10);
        log.Record(Call("test.Svc/Alpha", matched: true));
        log.Record(Call("test.Svc/Beta", matched: false));

        var ex = Assert.Throws<GrpcMockVerificationException>(() => log.VerifyNoUnmatchedCalls());

        Assert.Contains("test.Svc/Beta", ex.Message);
    }

    [Fact]
    public void Clear_AfterRecords_AllEmpty()
    {
        var log = new RequestLog(10);
        log.Record(Call("test.Svc/Alpha"));
        log.Record(Call("test.Svc/Beta"));

        log.Clear();

        Assert.Empty(log.All);
        Assert.Empty(log.For("test.Svc/Alpha"));
    }

    [Fact]
    public void Record_ConcurrentWriters_NoLossUnderMax()
    {
        var log = new RequestLog(10_000);

        Parallel.For(0, 1000, i => log.Record(Call("test.Svc/Alpha", true, new StringValue { Value = i.ToString() })));

        Assert.Equal(1000, log.All.Count);
    }

    /// <summary>Builds a recorded call directly, serializing each payload the way the server would receive it.</summary>
    private static RecordedCall Call(string method, bool matched = true, params IMessage[] payloads)
    {
        var bytes = payloads.Select(p => (ReadOnlyMemory<byte>)MessageExtensions.ToByteArray(p)).ToArray();
        return new RecordedCall(
            method,
            MethodType.Unary,
            new Metadata(),
            bytes,
            Status.DefaultSuccess,
            matched,
            DateTimeOffset.UtcNow);
    }
}
