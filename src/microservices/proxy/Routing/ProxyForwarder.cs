using System.Net;

namespace CinemaAbyss.ProxyService.Routing;

/// <summary>
/// Forwards the incoming HTTP request as-is (method, headers, query string, body) to a backend
/// and streams the backend's response back to the client. This is the mechanism that makes the
/// Strangler Fig switch invisible to callers — they only ever talk to the gateway.
/// </summary>
public class ProxyForwarder
{
    private static readonly HashSet<string> ExcludedRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Transfer-Encoding"
    };

    private static readonly HashSet<string> ExcludedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transfer-Encoding"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProxyForwarder> _logger;

    public ProxyForwarder(IHttpClientFactory httpClientFactory, ILogger<ProxyForwarder> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task ForwardAsync(HttpContext context, string targetBaseUrl, string targetName)
    {
        var client = _httpClientFactory.CreateClient(nameof(ProxyForwarder));

        var targetUri = new Uri(new Uri(targetBaseUrl), context.Request.Path + context.Request.QueryString);

        using var requestMessage = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

        if (HttpMethods.IsPost(context.Request.Method) ||
            HttpMethods.IsPut(context.Request.Method) ||
            HttpMethods.IsPatch(context.Request.Method))
        {
            requestMessage.Content = new StreamContent(context.Request.Body);
            if (context.Request.ContentType is not null)
            {
                requestMessage.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
            }
        }

        foreach (var header in context.Request.Headers)
        {
            if (!ExcludedRequestHeaders.Contains(header.Key))
            {
                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        _logger.LogInformation("Routing {Method} {Path} -> {Target} ({Url})",
            context.Request.Method, context.Request.Path, targetName, targetUri);

        context.Response.Headers["X-Routed-To"] = targetName;

        try
        {
            using var responseMessage = await client.SendAsync(
                requestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

            context.Response.StatusCode = (int)responseMessage.StatusCode;

            foreach (var header in responseMessage.Headers)
            {
                if (!ExcludedResponseHeaders.Contains(header.Key))
                {
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                }
            }

            foreach (var header in responseMessage.Content.Headers)
            {
                if (!ExcludedResponseHeaders.Contains(header.Key) && header.Key != "Content-Length")
                {
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                }
            }

            await responseMessage.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach {Target} at {Url}", targetName, targetUri);
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
            await context.Response.WriteAsJsonAsync(new { error = $"Upstream '{targetName}' is unavailable: {ex.Message}" });
        }
    }
}
