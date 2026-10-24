using System.Diagnostics;
using System.Runtime.CompilerServices;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace GrpcMockServer.Tests;

public class ServerStreamingEndToEndTests
{
    [Fact]
    public async Task Stream_ThreeMessages_ClientReceivesAllInOrder()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForServerStreaming<Int32Value, StringValue>(TestMethods.FeedItemsFullName)
            .Stream(
                new StringValue { Value = "a" },
                new StringValue { Value = "b" },
                new StringValue { Value = "c" }));

        var invoker = server.CreateCallInvoker();
        var call = invoker.AsyncServerStreamingCall(
            TestMethods.FeedItems, null, new CallOptions(), new Int32Value { Value = 0 });

        var received = await ReadAllAsync(call);

        Assert.Equal(new[] { "a", "b", "c" }, received);
    }

    [Fact]
    public async Task Stream_EmptyArray_CompletesWithNoItems()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForServerStreaming<Int32Value, StringValue>(TestMethods.FeedItemsFullName)
            .Stream());

        var invoker = server.CreateCallInvoker();
        var call = invoker.AsyncServerStreamingCall(
            TestMethods.FeedItems, null, new CallOptions(), new Int32Value { Value = 0 });

        var received = await ReadAllAsync(call);

        Assert.Empty(received);
    }

    [Fact]
    public async Task Stream_FactoryFromRequest_ProducesNItems()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForServerStreaming<Int32Value, StringValue>(TestMethods.FeedItemsFullName)
            .Stream(req => Enumerable.Range(0, req.Value)
                .Select(i => new StringValue { Value = i.ToString() })));

        var invoker = server.CreateCallInvoker();
        var call = invoker.AsyncServerStreamingCall(
            TestMethods.FeedItems, null, new CallOptions(), new Int32Value { Value = 5 });

        var received = await ReadAllAsync(call);

        Assert.Equal(new[] { "0", "1", "2", "3", "4" }, received);
    }

    [Fact]
    public async Task Stream_AsyncFactory_WithDelayBetweenItems_AllArrive()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForServerStreaming<Int32Value, StringValue>(TestMethods.FeedItemsFullName)
            .Stream((req, _) => DelayedItems(req.Value)));

        var invoker = server.CreateCallInvoker();
        var call = invoker.AsyncServerStreamingCall(
            TestMethods.FeedItems, null, new CallOptions(), new Int32Value { Value = 3 });

        var received = await ReadAllAsync(call);

        Assert.Equal(new[] { "0", "1", "2" }, received);
    }

    [Fact]
    public async Task Stream_AsyncFactory_ClientCancels_ServerStops()
    {
        var produced = 0;

        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForServerStreaming<Int32Value, StringValue>(TestMethods.FeedItemsFullName)
            .Stream((_, context) => EndlessItems(() => Interlocked.Increment(ref produced), context.CancellationToken)));

        var invoker = server.CreateCallInvoker();
        using var cts = new CancellationTokenSource();
        var call = invoker.AsyncServerStreamingCall(
            TestMethods.FeedItems, null, new CallOptions(cancellationToken: cts.Token), new Int32Value());

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (var _ in call.ResponseStream.ReadAllAsync())
            {
                cts.Cancel();
            }
        });

        Assert.Equal(StatusCode.Cancelled, ex.StatusCode);

        // The server must stop producing: allow 500 ms to wind down, then the counter must hold still.
        await Task.Delay(500);
        var settled = Volatile.Read(ref produced);
        await Task.Delay(100);
        Assert.Equal(settled, Volatile.Read(ref produced));
    }

    [Fact]
    public async Task StreamThenFail_TwoItemsThenNotFound_ClientGetsItemsThenStatus()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForServerStreaming<Int32Value, StringValue>(TestMethods.FeedItemsFullName)
            .StreamThenFail(
                StatusCode.NotFound,
                new StringValue { Value = "one" },
                new StringValue { Value = "two" }));

        var invoker = server.CreateCallInvoker();
        var call = invoker.AsyncServerStreamingCall(
            TestMethods.FeedItems, null, new CallOptions(), new Int32Value { Value = 0 });

        var received = new List<string>();
        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (var item in call.ResponseStream.ReadAllAsync())
            {
                received.Add(item.Value);
            }
        });

        Assert.Equal(new[] { "one", "two" }, received);
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Fail_Immediately_NoItemsAndStatus()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForServerStreaming<Int32Value, StringValue>(TestMethods.FeedItemsFullName)
            .Fail(StatusCode.NotFound, "no such feed"));

        var invoker = server.CreateCallInvoker();
        var call = invoker.AsyncServerStreamingCall(
            TestMethods.FeedItems, null, new CallOptions(), new Int32Value { Value = 0 });

        var received = new List<string>();
        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (var item in call.ResponseStream.ReadAllAsync())
            {
                received.Add(item.Value);
            }
        });

        Assert.Empty(received);
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
        Assert.Equal("no such feed", ex.Status.Detail);
    }

    [Fact]
    public async Task WithResponseHeaders_Set_ClientReadsInitialMetadata()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForServerStreaming<Int32Value, StringValue>(TestMethods.FeedItemsFullName)
            .WithResponseHeaders(new Metadata { { "x-feed-version", "7" } })
            .Stream(new StringValue { Value = "a" }));

        var invoker = server.CreateCallInvoker();
        var call = invoker.AsyncServerStreamingCall(
            TestMethods.FeedItems, null, new CallOptions(), new Int32Value { Value = 0 });

        var headers = await call.ResponseHeadersAsync;
        var received = await ReadAllAsync(call);

        Assert.Equal("7", headers.GetValue("x-feed-version"));
        Assert.Equal(new[] { "a" }, received);
    }

    [Fact]
    public async Task WithResponseTrailers_Set_ClientReadsTrailers()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForServerStreaming<Int32Value, StringValue>(TestMethods.FeedItemsFullName)
            .WithResponseTrailers(new Metadata { { "x-items-written", "2" } })
            .Stream(
                new StringValue { Value = "a" },
                new StringValue { Value = "b" }));

        var invoker = server.CreateCallInvoker();
        var call = invoker.AsyncServerStreamingCall(
            TestMethods.FeedItems, null, new CallOptions(), new Int32Value { Value = 0 });

        await ReadAllAsync(call);
        var trailers = call.GetTrailers();

        Assert.Equal("2", trailers.GetValue("x-items-written"));
    }

    [Fact]
    public async Task WithDelay_200ms_FirstItemArrivesAfterDelay()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForServerStreaming<Int32Value, StringValue>(TestMethods.FeedItemsFullName)
            .WithDelay(TimeSpan.FromMilliseconds(200))
            .Stream(new StringValue { Value = "late" }));

        var invoker = server.CreateCallInvoker();
        var call = invoker.AsyncServerStreamingCall(
            TestMethods.FeedItems, null, new CallOptions(), new Int32Value { Value = 0 });

        var stopwatch = Stopwatch.StartNew();
        var hasItem = await call.ResponseStream.MoveNext(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(hasItem);
        Assert.Equal("late", call.ResponseStream.Current.Value);
        Assert.True(
            stopwatch.ElapsedMilliseconds >= 150,
            $"First item arrived after {stopwatch.ElapsedMilliseconds} ms; expected at least 150 ms.");
    }

    [Fact]
    public async Task Requests_AfterStream_RecordsSingleCallWithServerStreamingType()
    {
        await using var server = await GrpcMockServer.StartAsync(mock => mock
            .ForServerStreaming<Int32Value, StringValue>(TestMethods.FeedItemsFullName)
            .Stream(
                new StringValue { Value = "x" },
                new StringValue { Value = "y" }));

        var invoker = server.CreateCallInvoker();
        var call = invoker.AsyncServerStreamingCall(
            TestMethods.FeedItems, null, new CallOptions(), new Int32Value { Value = 3 });

        await ReadAllAsync(call);

        server.Requests.VerifyCalled(TestMethods.FeedItemsFullName, 1);
        var recorded = Assert.Single(server.Requests.For(TestMethods.FeedItemsFullName));
        Assert.Equal(MethodType.ServerStreaming, recorded.Type);
        Assert.True(recorded.Matched);
        Assert.Equal(3, recorded.Request<Int32Value>().Value);
    }

    private static async Task<List<string>> ReadAllAsync(AsyncServerStreamingCall<StringValue> call)
    {
        var items = new List<string>();
        await foreach (var item in call.ResponseStream.ReadAllAsync())
        {
            items.Add(item.Value);
        }

        return items;
    }

    private static async IAsyncEnumerable<StringValue> DelayedItems(
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                await Task.Delay(10, cancellationToken);
            }

            yield return new StringValue { Value = i.ToString() };
        }
    }

    private static async IAsyncEnumerable<StringValue> EndlessItems(
        Action onProduced,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; ; i++)
        {
            onProduced();
            yield return new StringValue { Value = i.ToString() };
            await Task.Delay(10, cancellationToken);
        }
    }
}
