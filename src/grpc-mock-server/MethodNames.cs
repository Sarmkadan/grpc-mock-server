using System.Text.RegularExpressions;

namespace GrpcMockServer;

/// <summary>
/// Validation and parsing helpers for full gRPC method names in the wire form <c>package.Service/Method</c>.
/// </summary>
internal static class MethodNames
{
    private static readonly Regex _identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// Throws unless <paramref name="fullMethodName"/> is in the form <c>package.Service/Method</c>:
    /// no leading slash, exactly one slash, and non-empty dot-separated service segments and a method segment
    /// made of letters, digits and underscores.
    /// </summary>
    /// <param name="fullMethodName">Full method name to check.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fullMethodName"/> is null.</exception>
    /// <exception cref="ArgumentException">The name is not a valid full method name.</exception>
    public static void Validate(string fullMethodName)
    {
        ArgumentNullException.ThrowIfNull(fullMethodName);

        if (fullMethodName.Length == 0)
        {
            throw Invalid(fullMethodName, "must not be empty");
        }

        if (fullMethodName[0] == '/')
        {
            throw Invalid(fullMethodName, "must not start with '/'; use 'package.Service/Method'");
        }

        var slashes = fullMethodName.Count(c => c == '/');
        if (slashes == 0)
        {
            throw Invalid(fullMethodName, "is missing the '/' between the service and the method; use 'package.Service/Method'");
        }

        if (slashes > 1)
        {
            throw Invalid(fullMethodName, $"contains {slashes} '/' characters; exactly one is allowed");
        }

        var slash = fullMethodName.IndexOf('/');
        var service = fullMethodName[..slash];
        var method = fullMethodName[(slash + 1)..];

        if (service.Length == 0)
        {
            throw Invalid(fullMethodName, "has an empty service name before '/'");
        }

        if (method.Length == 0)
        {
            throw Invalid(fullMethodName, "has an empty method name after '/'");
        }

        foreach (var segment in service.Split('.'))
        {
            if (segment.Length == 0)
            {
                throw Invalid(fullMethodName, "has an empty package or service segment (consecutive or leading/trailing '.')");
            }

            if (!_identifier.IsMatch(segment))
            {
                throw Invalid(fullMethodName, $"has an invalid service segment '{segment}'; use letters, digits and '_', not starting with a digit");
            }
        }

        if (!_identifier.IsMatch(method))
        {
            throw Invalid(fullMethodName, $"has an invalid method name '{method}'; use letters, digits and '_', not starting with a digit");
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

    private static ArgumentException Invalid(string fullMethodName, string problem) =>
        new($"'{fullMethodName}' is not a valid full method name: it {problem}.", "fullMethodName");
}
