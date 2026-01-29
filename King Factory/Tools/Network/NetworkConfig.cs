namespace LittleHelperAI.KingFactory.Tools.Network;

/// <summary>
/// Configuration for network tools.
/// </summary>
public class NetworkConfig
{
    /// <summary>
    /// Default timeout for requests in seconds.
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum timeout allowed in seconds.
    /// </summary>
    public int MaxTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Maximum response size in bytes.
    /// </summary>
    public int MaxResponseSize { get; set; } = 1024 * 1024; // 1 MB

    /// <summary>
    /// Blocked URL patterns.
    /// </summary>
    public HashSet<string> BlockedHosts { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "127.0.0.1",
        "0.0.0.0",
        "::1",
        "metadata.google.internal",
        "169.254.169.254" // AWS metadata
    };

    /// <summary>
    /// Blocked URL schemes.
    /// </summary>
    public HashSet<string> BlockedSchemes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "file",
        "ftp"
    };

    /// <summary>
    /// Check if a URL is blocked.
    /// </summary>
    public bool IsUrlBlocked(Uri uri)
    {
        // Check scheme
        if (BlockedSchemes.Contains(uri.Scheme))
            return true;

        // Check host
        if (BlockedHosts.Contains(uri.Host))
            return true;

        // Check for private IP ranges
        if (System.Net.IPAddress.TryParse(uri.Host, out var ip))
        {
            var bytes = ip.GetAddressBytes();

            // 10.x.x.x
            if (bytes[0] == 10)
                return true;

            // 172.16.x.x - 172.31.x.x
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;

            // 192.168.x.x
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;
        }

        return false;
    }
}
