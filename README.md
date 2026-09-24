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
