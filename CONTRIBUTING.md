# Contributing

Thanks for taking the time to contribute.

## Setup

```bash
git clone https://github.com/Sarmkadan/grpc-mock-server.git
cd grpc-mock-server
dotnet build
dotnet test
```

Requires the .NET 8 SDK or newer.

## Workflow

1. Open an issue first for anything beyond a small fix, so the design can be discussed.
2. Fork, branch from `main` (`feat/...`, `fix/...`).
3. Keep the build warning-free - `TreatWarningsAsErrors` is on and XML docs are required for public API.
4. Add tests. Integration tests should start a real `GrpcMockServer` and call it through `Grpc.Net.Client`.
5. Run `dotnet format` before committing.
6. Use conventional commit messages: `feat:`, `fix:`, `docs:`, `test:`, `chore:`.
7. Open a PR against `main`. CI must be green.

## Scope

The library exists to remove the need for generated `ServiceBase` classes in tests. Changes that reintroduce
a protoc step or a hand-written service per test are out of scope.

## License

By contributing you agree that your contributions are licensed under the MIT License.
