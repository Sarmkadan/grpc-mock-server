# grpc-mock-server

In-process gRPC mock server for .NET integration tests. Fluent API, no proto regeneration.

## Commands

```bash
dotnet build                                   # build everything (warnings are errors)
dotnet test                                    # run all tests
dotnet test --filter "FullyQualifiedName~Unary" # run a subset
dotnet pack src/grpc-mock-server -c Release    # produce the NuGet package
dotnet format --verify-no-changes              # check formatting (CI does this)
```

## Architecture

- `GrpcMockServer` - facade. Owns the ASP.NET Core host (TestHost by default, Kestrel on a real port
  when `GrpcMockOptions.UseTestHost == false`), the stub registry and the request log.
- `IGrpcMockBuilder` + `I*StubBuilder<TReq,TRes>` - fluent configuration surface. Builders are
  immutable-ish accumulators; `Respond`/`Fail`/`Stream` finalize a stub and return to the root builder.
- Stub registry (internal, to be implemented) - maps `package.Service/Method` to an ordered list of
  stubs. Match order: priority desc, then registration order. `Times(n)` decrements and removes.
- Dynamic service definition (internal) - builds `Method<TReq,TRes>` with `Marshaller`s from
  `IMessage<T>` parsers at runtime and registers a `ServerServiceDefinition` via a custom
  `ServiceBinderBase`. This is the piece that removes the need for generated `ServiceBase` classes.
- `IRequestLog` / `RecordedCall` - stores raw payloads (not deserialized) so that verification works
  even for `ForRaw` stubs; deserialization happens lazily in `Request<T>()`.
- `IRequestMatcher<T>` / `RequestMatcher` - composable matchers with a `Description` used in
  `GrpcMockVerificationException` messages.

## Key directories

- `src/grpc-mock-server/` - library (assembly and PackageId `GrpcMockServer`)
- `tests/grpc-mock-server.Tests/` - xUnit tests. Integration tests should spin up a real server and use
  `Grpc.Net.Client` against it; avoid mocking the server internals.
- `.github/workflows/ci.yml` - build + test on push and PR

## Conventions

- `net8.0`, `LangVersion latest`, nullable enabled, `TreatWarningsAsErrors` (see `Directory.Build.props`).
- Public API gets XML docs; `GenerateDocumentationFile` is on, so missing docs fail the build.
- Full method names always use the gRPC wire form `package.Service/Method` (no leading slash).
- Do not add a `ServiceBase` subclass or protoc step to make the mock work - that defeats the purpose.
- Test names: `Method_Scenario_Expectation`.
- Commits: conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`). No `Co-Authored-By` lines.
