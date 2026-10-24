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

    /// <summary>Client-streaming method <c>e2e.v1.Aggregate/Concat</c> with <see cref="StringValue"/> request and response.</summary>
    public static readonly Method<StringValue, StringValue> Concat =
        ClientStreaming<StringValue, StringValue>("e2e.v1.Aggregate", "Concat");

    /// <summary>Full wire-form name for <see cref="Concat"/>.</summary>
    public const string ConcatFullName = "e2e.v1.Aggregate/Concat";

    /// <summary>Duplex-streaming method <c>e2e.v1.Echo/Stream</c> with <see cref="StringValue"/> request and response.</summary>
    public static readonly Method<StringValue, StringValue> EchoStream =
        DuplexStreaming<StringValue, StringValue>("e2e.v1.Echo", "Stream");

    /// <summary>Full wire-form name for <see cref="EchoStream"/>.</summary>
    public const string EchoStreamFullName = "e2e.v1.Echo/Stream";

    /// <summary>
    /// Builds a unary <see cref="Method{TRequest,TResponse}"/> for the given service and method name.
    /// </summary>
    /// <typeparam name="TRequest">Request message type.</typeparam>
    /// <typeparam name="TResponse">Response message type.</typeparam>
    /// <param name="service">Fully qualified service name, e.g. <c>hdr.v1.Svc</c>.</param>
    /// <param name="method">Method name within the service, e.g. <c>Get</c>.</param>
    public static Method<TRequest, TResponse> Unary<TRequest, TResponse>(string service, string method)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        return Create<TRequest, TResponse>(MethodType.Unary, service, method);
    }

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
        return Create<TRequest, TResponse>(MethodType.ServerStreaming, service, method);
    }

    /// <summary>
    /// Builds a client-streaming <see cref="Method{TRequest,TResponse}"/> for the given service and method name.
    /// </summary>
    /// <typeparam name="TRequest">Request message type.</typeparam>
    /// <typeparam name="TResponse">Response message type.</typeparam>
    /// <param name="service">Fully qualified service name, e.g. <c>e2e.v1.Aggregate</c>.</param>
    /// <param name="method">Method name within the service, e.g. <c>Concat</c>.</param>
    public static Method<TRequest, TResponse> ClientStreaming<TRequest, TResponse>(string service, string method)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        return Create<TRequest, TResponse>(MethodType.ClientStreaming, service, method);
    }

    /// <summary>
    /// Builds a duplex-streaming <see cref="Method{TRequest,TResponse}"/> for the given service and method name.
    /// </summary>
    /// <typeparam name="TRequest">Request message type.</typeparam>
    /// <typeparam name="TResponse">Response message type.</typeparam>
    /// <param name="service">Fully qualified service name, e.g. <c>e2e.v1.Echo</c>.</param>
    /// <param name="method">Method name within the service, e.g. <c>Stream</c>.</param>
    public static Method<TRequest, TResponse> DuplexStreaming<TRequest, TResponse>(string service, string method)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        return Create<TRequest, TResponse>(MethodType.DuplexStreaming, service, method);
    }

    private static Method<TRequest, TResponse> Create<TRequest, TResponse>(MethodType type, string service, string method)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        return new Method<TRequest, TResponse>(
            type,
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
