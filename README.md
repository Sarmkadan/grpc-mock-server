# grpc-mock-server

[![CI](https://github.com/Sarmkadan/grpc-mock-server/actions/workflows/ci.yml/badge.svg)](https://github.com/Sarmkadan/grpc-mock-server/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/GrpcMockServer.svg)](https://www.nuget.org/packages/GrpcMockServer)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

In-process gRPC mock server for integration tests with a fluent C# API - no proto regeneration needed.

Think WireMock, but for gRPC and living inside your xUnit/NUnit process. You declare a method by its
full name and message types, attach matchers and a response, and point your real generated client at
the server. No `ServiceBase` subclass, no hand-written fake service per test, no protoc step for the mock.

## Features

- **Fluent stubs** - `ForUnary<Req, Res>("pkg.Service/Method").When(...).Respond(...)`
- **All four call kinds** - unary, server-streaming, client-streaming, duplex
- **Failure injection** - any `StatusCode`, custom `RpcException`, delays, trailers
- **Sequenced responses** - first call returns A, second returns B, then B repeats
- **Header matching** - route on auth tokens, tenant ids, correlation ids
- **Request verification** - `Requests.VerifyCalled("...", times: 1)`, `VerifyCalledWith<Req>(...)`
- **Raw mode** - stub methods whose message types you do not have compiled (`byte[]` in/out)
- **Zero network by default** - runs on `Microsoft.AspNetCore.TestHost`; flip a flag to bind a real port for cross-process or cross-language clients

## Quick start

```csharp
using GrpcMockServer;

await using var server = await GrpcMockServer.StartAsync(mock => mock
    .ForUnary<GetUserRequest, GetUserResponse>("users.v1.UserService/GetUser")
        .When(r => r.Id == 42)
        .Respond(new GetUserResponse { Id = 42, Name = "Ada Lovelace" })
    .ForUnary<GetUserRequest, GetUserResponse>("users.v1.UserService/GetUser")
        .Fail(StatusCode.NotFound, "user not found")
    .ForServerStreaming<ListEventsRequest, Event>("events.v1.EventService/List")
        .Stream(new Event { Id = 1 }, new Event { Id = 2 }));

// Your real generated client - nothing changes in production code.
var client = new UserService.UserServiceClient(server.CreateChannel());

var user = await client.GetUserAsync(new GetUserRequest { Id = 42 });
Assert.Equal("Ada Lovelace", user.Name);

server.Requests.VerifyCalled("users.v1.UserService/GetUser", expectedCount: 1);
server.Requests.VerifyCalledWith<GetUserRequest>("users.v1.UserService/GetUser", r => r.Id == 42);
```

## Usage

The examples assume generated message types and service clients from your own `.proto` files:
`GetUserRequest`, `GetUserResponse`, `ListEventsRequest`, `Event`, `CreateUserRequest`,
`CreateUsersResponse`, `ChatMessage`, plus `UserService`, `EventService` and `ChatService` clients. Method names use the
gRPC wire form `package.Service/Method`. Snippets are shown as test bodies and assume `using Grpc.Core;` and
the following alias. The namespace and the class share the name `GrpcMockServer`, so `GrpcMockServer.StartAsync`
does not resolve in a file that imports the namespace; the alias avoids that:

```csharp
using MockServer = GrpcMockServer.GrpcMockServer;
```

### Failure injection

Fail a call with a status code and detail. The detail ends up in `RpcException.Status.Detail`:

```csharp
await using var server = await MockServer.StartAsync(mock => mock
    .ForUnary<GetUserRequest, GetUserResponse>("users.v1.UserService/GetUser")
        .Fail(StatusCode.PermissionDenied, "token expired"));

var client = new UserService.UserServiceClient(server.CreateChannel());

var ex = await Assert.ThrowsAsync<RpcException>(
    () => client.GetUserAsync(new GetUserRequest { Id = 42 }).ResponseAsync);

Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
Assert.Equal("token expired", ex.Status.Detail);
```

Fail with a prebuilt `RpcException` when the client needs trailers, for example a retry hint:

```csharp
var quotaTrailers = new Metadata { { "retry-after-seconds", "30" } };
var quotaExceeded = new RpcException(
    new Status(StatusCode.ResourceExhausted, "quota exceeded"), quotaTrailers);

await using var server = await MockServer.StartAsync(mock => mock
    .ForUnary<GetUserRequest, GetUserResponse>("users.v1.UserService/GetUser")
        .Fail(quotaExceeded));

var client = new UserService.UserServiceClient(server.CreateChannel());

var ex = await Assert.ThrowsAsync<RpcException>(
    () => client.GetUserAsync(new GetUserRequest { Id = 42 }).ResponseAsync);

Assert.Equal(StatusCode.ResourceExhausted, ex.StatusCode);
Assert.Equal("30", ex.Trailers.GetValue("retry-after-seconds"));
```

Simulate a slow backend with `WithDelay`. The delay is cancelled with the call, so a client deadline shorter
than the delay makes the call fail at the deadline instead of waiting out the full delay:

```csharp
await using var server = await MockServer.StartAsync(mock => mock
    .ForUnary<GetUserRequest, GetUserResponse>("users.v1.UserService/GetUser")
        .WithDelay(TimeSpan.FromSeconds(2))
        .Respond(new GetUserResponse { Id = 42, Name = "Ada Lovelace" }));

var client = new UserService.UserServiceClient(server.CreateChannel());

var started = DateTime.UtcNow;
await Assert.ThrowsAsync<RpcException>(() => client.GetUserAsync(
    new GetUserRequest { Id = 42 },
    deadline: DateTime.UtcNow.AddMilliseconds(200)).ResponseAsync);

Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1));
```

### Sequenced responses

`RespondInSequence` returns its responses in order to successive matching calls, and the last one repeats.
Use it for a value that changes over time, such as a job that is pending before it is ready:

```csharp
await using var server = await MockServer.StartAsync(mock => mock
    .ForUnary<GetUserRequest, GetUserResponse>("users.v1.UserService/GetUser")
        .RespondInSequence(
            new GetUserResponse { Id = 42, Name = "pending" },
            new GetUserResponse { Id = 42, Name = "Ada Lovelace" }));

var client = new UserService.UserServiceClient(server.CreateChannel());

var first = await client.GetUserAsync(new GetUserRequest { Id = 42 });
var second = await client.GetUserAsync(new GetUserRequest { Id = 42 });
var third = await client.GetUserAsync(new GetUserRequest { Id = 42 });

Assert.Equal("pending", first.Name);
Assert.Equal("Ada Lovelace", second.Name);
Assert.Equal("Ada Lovelace", third.Name);
```

For retry tests, pair `Times(1)` with a fallback stub. `Times` decrements the count on every match and removes
the stub at zero, so the first call hits the transient failure and every later call falls through to the
fallback. Stubs with the same priority are evaluated in registration order, so the one-shot stub goes first:

```csharp
await using var server = await MockServer.StartAsync(mock => mock
    // One transient failure. The stub is removed after its single match.
    .ForUnary<GetUserRequest, GetUserResponse>("users.v1.UserService/GetUser")
        .Times(1)
        .Fail(StatusCode.Unavailable, "connection reset")
    // Fallback for every later call.
    .ForUnary<GetUserRequest, GetUserResponse>("users.v1.UserService/GetUser")
        .Respond(new GetUserResponse { Id = 42, Name = "Ada Lovelace" }));

var client = new UserService.UserServiceClient(server.CreateChannel());

GetUserResponse? user = null;
var attempts = 0;
while (user is null && attempts < 3)
{
    attempts++;
    try
    {
        user = await client.GetUserAsync(new GetUserRequest { Id = 42 });
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
    {
        // Transient: try again.
    }
}

Assert.Equal("Ada Lovelace", user?.Name);
Assert.Equal(2, attempts);
```

### Header-based routing

`WithHeader` makes a stub match only when a request header is present with a given value, or when a predicate
accepts it. Header keys are case-insensitive. Keys ending in `-bin` are rejected because binary headers cannot be
matched by string:

```csharp
await using var server = await MockServer.StartAsync(mock => mock
    // Exact match: tenant-scoped data.
    .ForUnary<GetUserRequest, GetUserResponse>("users.v1.UserService/GetUser")
        .WithHeader("x-tenant-id", "acme")
        .Respond(new GetUserResponse { Id = 42, Name = "Ada (acme)" })
    .ForUnary<GetUserRequest, GetUserResponse>("users.v1.UserService/GetUser")
        .WithHeader("x-tenant-id", "globex")
        .Respond(new GetUserResponse { Id = 42, Name = "Ada (globex)" })
    // Predicate: any non-empty bearer token is accepted.
    .ForUnary<GetUserRequest, GetUserResponse>("users.v1.UserService/GetUser")
        .WithHeader("authorization", value => value.StartsWith("Bearer ", StringComparison.Ordinal)
            && value.Length > "Bearer ".Length)
        .Respond(new GetUserResponse { Id = 42, Name = "Ada Lovelace" })
    // Anything else, including calls with no authorization header.
    .ForUnary<GetUserRequest, GetUserResponse>("users.v1.UserService/GetUser")
        .Fail(StatusCode.Unauthenticated, "missing or malformed bearer token"));

var client = new UserService.UserServiceClient(server.CreateChannel());

var acme = await client.GetUserAsync(
    new GetUserRequest { Id = 42 },
    headers: new Metadata { { "x-tenant-id", "acme" } });

var authorized = await client.GetUserAsync(
    new GetUserRequest { Id = 42 },
    headers: new Metadata { { "authorization", "Bearer eyJhbGciOi" } });

Assert.Equal("Ada (acme)", acme.Name);
Assert.Equal("Ada Lovelace", authorized.Name);

var rejected = await Assert.ThrowsAsync<RpcException>(
    () => client.GetUserAsync(new GetUserRequest { Id = 42 }).ResponseAsync);
Assert.Equal(StatusCode.Unauthenticated, rejected.StatusCode);
```

### Server streaming

`Stream` has three forms. The `params` form streams fixed messages. The factory form derives messages from the
request. The `IAsyncEnumerable` form produces messages asynchronously, so it can pause between items. Pass the
call's `ServerCallContext.CancellationToken` to the delay so the stream stops when the client cancels:

```csharp
static async IAsyncEnumerable<Event> SlowEvents(ListEventsRequest request, ServerCallContext context)
{
    for (var id = 1; id <= request.Limit; id++)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(100), context.CancellationToken);
        yield return new Event { Id = id, Kind = "tick" };
    }
}

await using var server = await MockServer.StartAsync(mock => mock
    // Fixed messages for one topic.
    .ForServerStreaming<ListEventsRequest, Event>("events.v1.EventService/List")
        .When(r => r.Topic == "orders")
        .Stream(
            new Event { Id = 1, Kind = "created" },
            new Event { Id = 2, Kind = "shipped" })
    // Messages derived from the request: Limit decides how many come back.
    .ForServerStreaming<ListEventsRequest, Event>("events.v1.EventService/List")
        .Stream(r => Enumerable.Range(1, r.Limit).Select(id => new Event { Id = id, Kind = "tick" }))
    // Asynchronous producer with a delay between items.
    .ForServerStreaming<ListEventsRequest, Event>("events.v1.EventService/Watch")
        .Stream(SlowEvents)
    // Some messages, then a failure. The client reads the items before the RpcException.
    .ForServerStreaming<ListEventsRequest, Event>("events.v1.EventService/Tail")
        .StreamThenFail(StatusCode.Unavailable, new Event { Id = 1, Kind = "created" }));

var client = new EventService.EventServiceClient(server.CreateChannel());

using var orders = client.List(new ListEventsRequest { Topic = "orders" });
var received = new List<Event>();
while (await orders.ResponseStream.MoveNext())
{
    received.Add(orders.ResponseStream.Current);
}

Assert.Equal(new[] { 1, 2 }, received.Select(e => e.Id));

using var tail = client.Tail(new ListEventsRequest { Topic = "orders" });
var ex = await Assert.ThrowsAsync<RpcException>(async () =>
{
    while (await tail.ResponseStream.MoveNext())
    {
    }
});
Assert.Equal(StatusCode.Unavailable, ex.StatusCode);
```

### Client and duplex streaming

Client-streaming factories receive every message the client wrote, in order. Duplex stubs either map each
message with `Echo`, or take over the whole exchange with `Handle`:

```csharp
await using var server = await MockServer.StartAsync(mock => mock
    // Client streaming: count what the client sent.
    .ForClientStreaming<CreateUserRequest, CreateUsersResponse>("users.v1.UserService/CreateUsers")
        .Respond(requests => new CreateUsersResponse { Created = requests.Count })
    // Duplex: map each message to a reply.
    .ForDuplexStreaming<ChatMessage, ChatMessage>("chat.v1.ChatService/Session")
        .Echo(message => new ChatMessage { Text = "echo: " + message.Text })
    // Duplex with full control: reply to each message, then send a summary when the client completes.
    .ForDuplexStreaming<ChatMessage, ChatMessage>("chat.v1.ChatService/Transcribe")
        .Handle(async (requests, responses, context) =>
        {
            var count = 0;
            while (await requests.MoveNext(context.CancellationToken))
            {
                count++;
                await responses.WriteAsync(new ChatMessage { Text = $"ack {count}: {requests.Current.Text}" });
            }

            await responses.WriteAsync(new ChatMessage { Text = $"done after {count} message(s)" });
        }));

var users = new UserService.UserServiceClient(server.CreateChannel());

using var upload = users.CreateUsers();
foreach (var name in new[] { "Ada", "Grace" })
{
    await upload.RequestStream.WriteAsync(new CreateUserRequest { Name = name });
}

await upload.RequestStream.CompleteAsync();
var created = await upload.ResponseAsync;

Assert.Equal(2, created.Created);

var chat = new ChatService.ChatServiceClient(server.CreateChannel());

using var session = chat.Session();
await session.RequestStream.WriteAsync(new ChatMessage { Text = "hello" });
await session.RequestStream.CompleteAsync();
Assert.True(await session.ResponseStream.MoveNext());
Assert.Equal("echo: hello", session.ResponseStream.Current.Text);
```

### Verifying calls

The request log records calls that return a response from a stub, and calls that match no stub, in arrival
order. The verification methods throw `GrpcMockVerificationException` when an assertion fails:

```csharp
await using var server = await MockServer.StartAsync(mock => mock
    .ForUnary<GetUserRequest, GetUserResponse>("users.v1.UserService/GetUser")
        .Respond(r => new GetUserResponse { Id = r.Id, Name = "user " + r.Id }));

var client = new UserService.UserServiceClient(server.CreateChannel());

await client.GetUserAsync(new GetUserRequest { Id = 42 }, headers: new Metadata { { "x-tenant-id", "acme" } });
await client.GetUserAsync(new GetUserRequest { Id = 7 });

// Exact number of calls to a method.
server.Requests.VerifyCalled("users.v1.UserService/GetUser", expectedCount: 2);

// At least one call whose deserialized request satisfies the predicate.
server.Requests.VerifyCalledWith<GetUserRequest>("users.v1.UserService/GetUser", r => r.Id == 42);

// A method that the code under test must not call.
server.Requests.VerifyNotCalled("users.v1.UserService/DeleteUser");

// Every call matched a stub.
server.Requests.VerifyNoUnmatchedCalls();

// Inspect the recorded calls directly.
var calls = server.Requests.For("users.v1.UserService/GetUser");
Assert.Equal(StatusCode.OK, calls[0].Status.StatusCode);
Assert.True(calls[0].Matched);

GetUserRequest firstRequest = calls[0].Request<GetUserRequest>();
Assert.Equal(42, firstRequest.Id);

IReadOnlyList<GetUserRequest> allRequests =
    server.Requests.RequestsFor<GetUserRequest>("users.v1.UserService/GetUser");
Assert.Equal(new[] { 42, 7 }, allRequests.Select(r => r.Id));
```

When a verification fails, the message states the expected and actual counts, then lists every recorded call
with its timestamp, method type, status, match result and payload count. If the first assertion above had asked
for `expectedCount: 3`, the exception message would look like this:

```
Expected users.v1.UserService/GetUser to be called 3 time(s) but was called 2 time(s). Recorded calls:
  [2026-10-24T09:12:44.0123456+00:00] Unary users.v1.UserService/GetUser status=OK matched=True payloads=1
  [2026-10-24T09:12:44.0198765+00:00] Unary users.v1.UserService/GetUser status=OK matched=True payloads=1
```

`RecordedCall.Request<T>()` parses the first payload only. For client-streaming calls, read
`RecordedCall.RequestPayloads` directly to get every message, in order.

### Sharing one server across a test class

Starting a server per test is the simplest option. When a class has many tests, start one server in an xUnit
fixture and give each test its own stubs with `Configure`, then clear them with `Reset` when the test finishes.

Methods are routed only if they were declared in the `configure` callback passed to `StartAsync`. `Configure`
throws `InvalidOperationException` for a method that was not declared at startup, so declare every method the
class needs in the fixture. Stubs added by `Configure` are appended after the fixture's stubs, so the placeholder
would win at the same priority. Higher priority is evaluated first, so the per-test stubs use `WithPriority(1)`
and take precedence over the placeholder at the default priority of 0:

```csharp
public sealed class UserServiceFixture : IAsyncLifetime
{
    public MockServer Server { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Declares the method. Without a stub, the method would not be routable.
        Server = await MockServer.StartAsync(mock => mock
            .ForUnary<GetUserRequest, GetUserResponse>("users.v1.UserService/GetUser")
                .Fail(StatusCode.Unimplemented, "not configured for this test"));
    }

    public async Task DisposeAsync() => await Server.DisposeAsync();
}

public sealed class UserLookupTests : IClassFixture<UserServiceFixture>, IDisposable
{
    private const string GetUser = "users.v1.UserService/GetUser";

    private readonly UserServiceFixture _fixture;
    private readonly UserService.UserServiceClient _client;

    public UserLookupTests(UserServiceFixture fixture)
    {
        _fixture = fixture;
        _client = new UserService.UserServiceClient(fixture.Server.CreateChannel());
    }

    [Fact]
    public async Task GetUser_KnownId_ReturnsConfiguredUser()
    {
        _fixture.Server.Configure(mock => mock
            .ForUnary<GetUserRequest, GetUserResponse>(GetUser)
                .When(r => r.Id == 42)
                .WithPriority(1)
                .Respond(new GetUserResponse { Id = 42, Name = "Ada Lovelace" }));

        var user = await _client.GetUserAsync(new GetUserRequest { Id = 42 });

        Assert.Equal("Ada Lovelace", user.Name);
        _fixture.Server.Requests.VerifyCalled(GetUser, expectedCount: 1);
    }

    public void Dispose() => _fixture.Server.Reset();
}
```

`Reset` removes every stub and clears the request log, but keeps the declared methods. Each test therefore starts
with an empty log, and the next `Configure` call can route the same methods again.

## Installation

```bash
dotnet add package GrpcMockServer
```

Targets `net8.0`. Depends on `Grpc.AspNetCore.Server`, `Grpc.Net.Client` and `Google.Protobuf`.

## API overview

| Type | Purpose |
|------|---------|
| `GrpcMockServer` | Lifecycle: `StartAsync`, `Configure`, `Reset`, `CreateChannel`, `CreateCallInvoker`, dispose |
| `IGrpcMockBuilder` | Entry point of the fluent API: `ForUnary`, `ForServerStreaming`, `ForClientStreaming`, `ForDuplexStreaming`, `ForRaw`, `OnUnmatched` |
| `IUnaryStubBuilder<TReq, TRes>` and friends | Per-method stubs: `When`, `WithHeader`, `WithDelay`, `WithPriority`, `Times`, `Respond`, `Fail`, `Stream`, `Echo`, `Handle` |
| `IRequestMatcher<TReq>` / `RequestMatcher` | Reusable, self-describing matchers: `Any`, `Where`, `Equals`, `All`, `AnyOf` |
| `IRequestLog` / `RecordedCall` | Recorded calls and verification: `VerifyCalled`, `VerifyCalledWith`, `VerifyNotCalled`, `VerifyNoUnmatchedCalls` |
| `GrpcMockOptions` | TestHost vs real socket, port, recording limits, logging, reflection |

### How "no proto regeneration" works

gRPC only needs a `Method<TRequest, TResponse>` descriptor with marshallers to route a call. The server
builds those descriptors at runtime from the full method name and the `IMessage<T>` types you already have
in your client assembly, then registers a dynamic `ServerServiceDefinition`. Your protos stay untouched.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE) - Copyright 2026 Vladyslav Zaiets
