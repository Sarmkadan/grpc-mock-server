using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace GrpcMockServer.Tests;

/// <summary>
/// Helper marshallers and <see cref="Method{TRequest,TResponse}"/> definitions for end-to-end tests
/// that exercise the mock server without protoc-generated stubs.
/// </summary>
internal static class TestMethods
{
    private static readonly Marshaller<StringValue> StringValueMarshaller =
        Marshallers.Create(
            m => m.ToByteArray(),
            StringValue.Parser.ParseFrom);

    private static readonly Marshaller<Int32Value> Int32ValueMarshaller =
        Marshallers.Create(
            m => m.ToByteArray(),
            Int32Value.Parser.ParseFrom);

    /// <summary>Unary method <c>e2e.v1.Echo/Upper</c> with <see cref="StringValue"/> request and response.</summary>
    public static readonly Method<StringValue, StringValue> EchoUpper =
        new(
            MethodType.Unary,
            "e2e.v1.Echo",
            "Upper",
            StringValueMarshaller,
            StringValueMarshaller);

    /// <summary>Full wire-form name for <see cref="EchoUpper"/>.</summary>
    public const string EchoUpperFullName = "e2e.v1.Echo/Upper";
}
