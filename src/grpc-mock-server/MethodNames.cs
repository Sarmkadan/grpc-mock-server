using System.Text.RegularExpressions;

namespace GrpcMockServer;

/// <summary>
/// Validation and parsing helpers for full gRPC method names in the wire form <c>package.Service/Method</c>.
/// </summary>
internal static class MethodNames
{
    private static readonly Regex _pattern = new(
        @"^[A-Za-z_][A-Za-z0-9_.]*/[A-Za-z_][A-Za-z0-9_]*\z",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Throws unless <paramref name="fullMethodName"/> is in the form <c>package.Service/Method</c>
    /// (no leading slash, exactly one slash).
    /// </summary>
    /// <param name="fullMethodName">Full method name to check.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fullMethodName"/> is null.</exception>
    /// <exception cref="ArgumentException">The name is not a valid full method name.</exception>
    public static void Validate(string fullMethodName)
    {
        ArgumentNullException.ThrowIfNull(fullMethodName);

        if (!_pattern.IsMatch(fullMethodName))
        {
            throw new ArgumentException(
                $"'{fullMethodName}' is not a valid full method name; expected 'package.Service/Method' without a leading slash.",
                nameof(fullMethodName));
        }
    }

    /// <summary>Splits a full method name into its service and method parts.</summary>
    /// <param name="fullMethodName">Full method name in the form <c>package.Service/Method</c>.</param>
    /// <returns>The service name (including package) and the method name.</returns>
    /// <exception cref="ArgumentException">The name is not a valid full method name.</exception>
    public static (string Service, string Method) Split(string fullMethodName)
    {
        Validate(fullMethodName);

        var slash = fullMethodName.IndexOf('/');
        return (fullMethodName[..slash], fullMethodName[(slash + 1)..]);
    }
}
