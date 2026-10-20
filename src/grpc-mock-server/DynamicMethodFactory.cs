using Google.Protobuf;
using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Creates <see cref="Method{TRequest, TResponse}"/> instances at runtime for arbitrary
/// protobuf message types, without requiring generated service base classes.
/// </summary>
internal static class DynamicMethodFactory
{
    /// <summary>
    /// Creates a gRPC <see cref="Method{TRequest, TResponse}"/> for the given full method name and type.
    /// </summary>
    /// <typeparam name="TRequest">Request message type.</typeparam>
    /// <typeparam name="TResponse">Response message type.</typeparam>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    /// <param name="type">gRPC method type.</param>
    public static Method<TRequest, TResponse> CreateMethod<TRequest, TResponse>(
        string fullMethodName, MethodType type)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        var slashIndex = fullMethodName.LastIndexOf('/');
        var serviceName = slashIndex > 0 ? fullMethodName[..slashIndex] : string.Empty;
        var methodName = slashIndex >= 0 ? fullMethodName[(slashIndex + 1)..] : fullMethodName;

        return new Method<TRequest, TResponse>(
            type,
            serviceName,
            methodName,
            CreateMarshaller<TRequest>(),
            CreateMarshaller<TResponse>());
    }

    /// <summary>Serializes a protobuf message to bytes.</summary>
    /// <typeparam name="T">Message type.</typeparam>
    /// <param name="message">Message to serialize.</param>
    public static byte[] Serialize<T>(T message) where T : class, IMessage<T>
    {
        return message.ToByteArray();
    }

    private static Marshaller<T> CreateMarshaller<T>() where T : class, IMessage<T>, new()
    {
        return Marshallers.Create(
            serializer: msg => msg.ToByteArray(),
            deserializer: bytes =>
            {
                var instance = new T();
                instance.MergeFrom(bytes);
                return instance;
            });
    }
}
