using Grpc.Core;
using Microsoft.AspNetCore.Http;

namespace GrpcMockServer;

/// <summary>
/// Conversions between ASP.NET Core HTTP headers and gRPC <see cref="Metadata"/>.
/// </summary>
internal static class MetadataConversions
{
    /// <summary>
    /// Copies HTTP request headers into gRPC metadata. Keys are lowercased, as gRPC requires. Keys ending in
    /// <c>-bin</c> carry base64 on the wire and are decoded to binary entries; malformed ones are skipped.
    /// </summary>
    /// <param name="headers">Request headers as received by the server.</param>
    /// <returns>A new metadata collection holding every header value.</returns>
    public static Metadata ToMetadata(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var metadata = new Metadata();

        foreach (var header in headers)
        {
            var key = header.Key.ToLowerInvariant();

            foreach (var value in header.Value)
            {
                if (value is null)
                {
                    continue;
                }

                if (key.EndsWith(Metadata.BinaryHeaderSuffix, StringComparison.Ordinal))
                {
                    if (TryDecodeBase64(value, out var bytes))
                    {
                        metadata.Add(new Metadata.Entry(key, bytes));
                    }
                }
                else
                {
                    metadata.Add(key, value);
                }
            }
        }

        return metadata;
    }

    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        // gRPC allows the base64 padding to be omitted, which Convert.FromBase64String rejects.
        var padded = value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=');

        try
        {
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }
}
