using System.Net;
using System.Text;
using System.Text.Json;

namespace MailCapture.Tests;

/// <summary>
/// A queue-based HttpMessageHandler for sequential test responses.
/// </summary>
internal sealed class MockHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _handlers = new();
    public List<HttpRequestMessage> Requests { get; } = [];

    public MockHttpHandler Enqueue(HttpResponseMessage response)
    {
        _handlers.Enqueue(_ => response);
        return this;
    }

    /// <summary>Add a handler that returns the same response for every call (infinite).</summary>
    public MockHttpHandler Always(HttpResponseMessage response)
    {
        // Wrap in a factory that clones so the same response can be returned many times.
        var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var status = response.StatusCode;
        _handlers.Enqueue(_ => Json(status, json));
        // Replace the queue with a single always-handler tracked separately.
        _alwaysHandler = _ => Json(status, json);
        return this;
    }

    private Func<HttpRequestMessage, HttpResponseMessage>? _alwaysHandler;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_handlers.TryDequeue(out var factory))
            return Task.FromResult(factory(request));

        if (_alwaysHandler is not null)
            return Task.FromResult(_alwaysHandler(request));

        throw new InvalidOperationException("MockHttpHandler: no more responses queued.");
    }

    // ─── Factory helpers ──────────────────────────────────────────────────

    public static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        Json(status, JsonSerializer.Serialize(body));

    public static HttpResponseMessage Json(HttpStatusCode status, string json)
    {
        var response = new HttpResponseMessage(status);
        response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return response;
    }

    public static HttpResponseMessage NoContent() =>
        new(HttpStatusCode.NoContent);
}

/// <summary>Shared JSON payloads used across test classes.</summary>
internal static class Payloads
{
    public static object Ping(string username = "alice") => new
    {
        status = "ok",
        username,
        address_template = $"{username}-{{tag}}@mailcapture.app",
        example = $"{username}-signup@mailcapture.app",
    };

    public static object Capture(
        string id = "cap-1", string tag = "signup", string? otp = "123456") => new
    {
        id, tag, subject = "Test Email",
        otp, body_text = "Hello", body_html = "<p>Hello</p>",
        latency_ms = 100, status = "captured",
        received_at = "2024-01-01T00:00:00Z",
    };

    public static object Latest(object capture) => new
    {
        items = new[] { capture },
        count = 1,
        next_after = "2024-01-01T00:00:01Z",
    };

    public static object TimeoutBody() => new
    {
        status = "error", message = "REQUEST_TIMEOUT", detail = "Timed out",
    };

    public static object Error(string message, string? detail = null) => new
    {
        status = "fail", message, detail,
    };
}
