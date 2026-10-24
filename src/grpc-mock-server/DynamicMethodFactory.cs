using System.Reflection;
using Google.Protobuf;
using Grpc.Core;

namespace GrpcMockServer;

/// <summary>
/// Creates <see cref="Method{TRequest, TResponse}"/> instances at runtime for arbitrary
/// protobuf message types, without requiring generated service base classes.
/// </summary>
internal static class DynamicMethodFactory
{
    private static readonly MethodInfo _createTypedMethod =
        typeof(DynamicMethodFactory).GetMethod(nameof(Create), 1, [typeof(MethodType), typeof(string)])!;

    /// <summary>
    /// Creates a gRPC <see cref="Method{TRequest, TResponse}"/> for the given method type and full method name.
    /// </summary>
    /// <typeparam name="TRequest">Request message type.</typeparam>
    /// <typeparam name="TResponse">Response message type.</typeparam>
    /// <param name="type">gRPC method type.</param>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="fullMethodName"/> is not a valid full method name.</exception>
    /// <exception cref="NotSupportedException">A message type is neither <c>byte[]</c> nor an <see cref="IMessage{T}"/>.</exception>
    public static Method<TRequest, TResponse> Create<TRequest, TResponse>(MethodType type, string fullMethodName)
    {
        var (service, method) = MethodNames.Split(fullMethodName);

        return new Method<TRequest, TResponse>(
            type,
            service,
            method,
            CreateMarshaller<TRequest>(),
            CreateMarshaller<TResponse>());
    }

    /// <summary>
    /// Non-generic counterpart of <see cref="Create{TRequest, TResponse}(MethodType, string)"/> for
    /// message types known only at runtime, such as those taken from stub metadata.
    /// </summary>
    /// <param name="type">gRPC method type.</param>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    /// <param name="requestType">Request message CLR type.</param>
    /// <param name="responseType">Response message CLR type.</param>
    /// <returns>A <c>Method&lt;requestType, responseType&gt;</c> boxed as <see cref="object"/>.</returns>
    public static object Create(MethodType type, string fullMethodName, Type requestType, Type responseType)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(responseType);

        var generic = _createTypedMethod.MakeGenericMethod(requestType, responseType);
        try
        {
            return generic.Invoke(null, [type, fullMethodName])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    /// <summary>
    /// Serializes a protobuf message to bytes. Uses the message API directly, because the contextual
    /// marshaller built by this class does not implement the context-free <c>Serializer</c> delegate.
    /// </summary>
    /// <typeparam name="T">Message type.</typeparam>
    /// <param name="message">Message to serialize.</param>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not supported as a message type.</exception>
    public static byte[] Serialize<T>(T message)
    {
        if (typeof(T) == typeof(byte[]))
        {
            return (byte[])(object)message!;
        }

        EnsureMessageType<T>();
        return ((IMessage)message!).ToByteArray();
    }

    /// <summary>
    /// Deserializes a protobuf message from bytes. Uses the message parser directly, for the same reason
    /// as <see cref="Serialize{T}(T)"/>.
    /// </summary>
    /// <typeparam name="T">Message type.</typeparam>
    /// <param name="payload">Serialized message bytes.</param>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not supported as a message type.</exception>
    public static T Deserialize<T>(ReadOnlyMemory<byte> payload)
    {
        if (typeof(T) == typeof(byte[]))
        {
            return (T)(object)payload.ToArray();
        }

        return (T)(object)GetParser<T>().ParseFrom(payload.ToArray());
    }

    private static Marshaller<T> CreateMarshaller<T>()
    {
        if (typeof(T) == typeof(byte[]))
        {
            return Marshallers.Create(b => (byte[])(object)b!, b => (T)(object)b!);
        }

        var parser = GetParser<T>();

        return Marshallers.Create<T>(
            serializer: (msg, ctx) =>
            {
                var message = (IMessage)msg!;
                ctx.SetPayloadLength(message.CalculateSize());
                message.WriteTo(ctx.GetBufferWriter());
                ctx.Complete();
            },
            deserializer: ctx => (T)(object)parser.ParseFrom(ctx.PayloadAsNewBuffer()));
    }

    /// <summary>
    /// Returns the non-generic parser for <typeparamref name="T"/>. The non-generic form is used because
    /// <typeparamref name="T"/> is not constrained to <see cref="IMessage{T}"/> at this level.
    /// </summary>
    private static MessageParser GetParser<T>()
    {
        EnsureMessageType<T>();
        return ((IMessage)Activator.CreateInstance<T>()!).Descriptor.Parser;
    }

    private static void EnsureMessageType<T>()
    {
        if (!typeof(IMessage<>).MakeGenericType(typeof(T)).IsAssignableFrom(typeof(T)))
        {
            throw new NotSupportedException(
                $"Type '{typeof(T)}' is not supported; expected byte[] or a type implementing IMessage<{typeof(T).Name}>.");
        }
    }
}
