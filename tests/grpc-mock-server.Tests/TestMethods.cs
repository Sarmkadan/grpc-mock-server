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

    /// <summary>Server-streaming method <c>e2e.v1.Feed/Items</c> with <see cref="Int32Value"/> request and <see cref="StringValue"/> response.</summary>
    public static readonly Method<Int32Value, StringValue> FeedItems =
        ServerStreaming<Int32Value, StringValue>("e2e.v1.Feed", "Items");

    /// <summary>Full wire-form name for <see cref="FeedItems"/>.</summary>
    public const string FeedItemsFullName = "e2e.v1.Feed/Items";

    /// <summary>
    /// Builds a server-streaming <see cref="Method{TRequest,TResponse}"/> for the given service and method name.
    /// Marshallers are derived from each message type's protobuf parser, so no generated code is needed.
    /// </summary>
    /// <typeparam name="TRequest">Request message type.</typeparam>
    /// <typeparam name="TResponse">Response message type.</typeparam>
    /// <param name="service">Fully qualified service name, e.g. <c>e2e.v1.Feed</c>.</param>
    /// <param name="method">Method name within the service, e.g. <c>Items</c>.</param>
    public static Method<TRequest, TResponse> ServerStreaming<TRequest, TResponse>(string service, string method)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        return new Method<TRequest, TResponse>(
            MethodType.ServerStreaming,
            service,
            method,
            MarshallerFor<TRequest>(),
            MarshallerFor<TResponse>());
    }

    private static Marshaller<T> MarshallerFor<T>()
        where T : class, IMessage<T>, new()
    {
        var parser = (MessageParser<T>)new T().Descriptor.Parser;
        return Marshallers.Create(
            m => m.ToByteArray(),
            parser.ParseFrom);
    }
}
