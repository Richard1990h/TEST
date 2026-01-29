using LittleHelperAI.KingFactory.Models;
using Microsoft.Extensions.Logging;
using System.Text;

namespace LittleHelperAI.KingFactory.Tools.Network;

/// <summary>
/// Tool for making HTTP requests.
/// </summary>
public class FetchTool : ITool
{
    private readonly ILogger<FetchTool> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NetworkConfig _config;

    public string Name => "fetch";
    public string Description => "Make an HTTP request to a URL and return the response.";
    public bool RequiresConfirmation => false;

    public ToolSchema Schema => new()
    {
        Properties = new Dictionary<string, ToolParameter>
        {
            ["url"] = new()
            {
                Type = "string",
                Description = "The URL to fetch"
            },
            ["method"] = new()
            {
                Type = "string",
                Description = "HTTP method (GET, POST, PUT, DELETE)",
                Default = "GET",
                Enum = new List<string> { "GET", "POST", "PUT", "DELETE", "PATCH" }
            },
            ["headers"] = new()
            {
                Type = "object",
                Description = "Request headers as key-value pairs"
            },
            ["body"] = new()
            {
                Type = "string",
                Description = "Request body (for POST, PUT, PATCH)"
            },
            ["timeout"] = new()
            {
                Type = "integer",
                Description = "Timeout in seconds (default: 30)"
            }
        },
        Required = new List<string> { "url" }
    };

    public FetchTool(ILogger<FetchTool> logger, IHttpClientFactory httpClientFactory, NetworkConfig config)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    public ValidationResult ValidateArguments(Dictionary<string, object> arguments)
    {
        if (!arguments.TryGetValue("url", out var urlObj) || urlObj is not string url || string.IsNullOrWhiteSpace(url))
        {
            return ValidationResult.Invalid("'url' is required");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return ValidationResult.Invalid("Invalid URL format");
        }

        if (_config.IsUrlBlocked(uri))
        {
            return ValidationResult.Invalid("This URL is blocked");
        }

        return ValidationResult.Valid();
    }

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        var url = arguments["url"].ToString()!;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = "Invalid URL"
            };
        }

        if (_config.IsUrlBlocked(uri))
        {
            return new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = "URL blocked for security reasons"
            };
        }

        var method = HttpMethod.Get;
        if (arguments.TryGetValue("method", out var methodObj) && methodObj is string methodStr)
        {
            method = methodStr.ToUpperInvariant() switch
            {
                "POST" => HttpMethod.Post,
                "PUT" => HttpMethod.Put,
                "DELETE" => HttpMethod.Delete,
                "PATCH" => HttpMethod.Patch,
                _ => HttpMethod.Get
            };
        }

        var timeout = _config.DefaultTimeoutSeconds;
        if (arguments.TryGetValue("timeout", out var toObj))
        {
            timeout = toObj is int t ? t : int.Parse(toObj.ToString() ?? "30");
            timeout = Math.Min(timeout, _config.MaxTimeoutSeconds);
        }

        _logger.LogInformation("Fetching {Method} {Url}", method, url);

        try
        {
            var client = _httpClientFactory.CreateClient("FactoryFetch");
            client.Timeout = TimeSpan.FromSeconds(timeout);

            var request = new HttpRequestMessage(method, uri);

            // Add headers
            if (arguments.TryGetValue("headers", out var headersObj) && headersObj is Dictionary<string, object> headers)
            {
                foreach (var header in headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToString());
                }
            }

            // Add body
            if (arguments.TryGetValue("body", out var bodyObj) && bodyObj is string body && !string.IsNullOrEmpty(body))
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            var response = await client.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            // Truncate if too large
            if (content.Length > _config.MaxResponseSize)
            {
                content = content.Substring(0, _config.MaxResponseSize) + "\n[truncated]";
            }

            var result = new StringBuilder();
            result.AppendLine($"[Status]: {(int)response.StatusCode} {response.ReasonPhrase}");
            result.AppendLine($"[Content-Type]: {response.Content.Headers.ContentType?.ToString() ?? "unknown"}");
            result.AppendLine();
            result.Append(content);

            _logger.LogInformation("Fetch completed: {StatusCode}", response.StatusCode);

            return new ToolResult
            {
                ToolName = Name,
                Success = response.IsSuccessStatusCode,
                Output = result.ToString(),
                Error = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}"
            };
        }
        catch (TaskCanceledException)
        {
            return new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = $"Request timed out after {timeout} seconds"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fetch failed: {Url}", url);

            return new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = ex.Message
            };
        }
    }
}
