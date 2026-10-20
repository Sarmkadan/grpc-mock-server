namespace GrpcMockServer;

/// <summary>
/// Empty marker type used as the TService parameter for
/// <see cref="Grpc.AspNetCore.Server.Model.IServiceMethodProvider{TService}"/>.
/// The mock server does not need a real service base class; this type satisfies the
/// generic constraint so that dynamically registered methods route through ASP.NET Core gRPC.
/// </summary>
internal sealed class MockGrpcService { }
