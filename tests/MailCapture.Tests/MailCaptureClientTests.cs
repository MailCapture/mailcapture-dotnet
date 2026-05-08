using System.Net;
using MailCapture.Exceptions;
using MailCapture.Options;
using Xunit;
using static MailCapture.Tests.MockHttpHandler;
using static MailCapture.Tests.Payloads;

namespace MailCapture.Tests;

public class MailCaptureClientTests
{
    private static MailCaptureClient MakeClient(MockHttpHandler handler) =>
        new("mc_testkey", httpClient: new HttpClient(handler));

    // ─── Constructor ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ThrowsOnEmptyKey()
    {
        Assert.Throws<ArgumentException>(() => new MailCaptureClient(""));
    }

    [Fact]
    public void Constructor_WritesWarningOnBadKeyPrefix()
    {
        var originalErr = Console.Error;
        var sw = new System.IO.StringWriter();
        Console.SetError(sw);
        try
        {
            _ = new MailCaptureClient("bad_key_format");
            Assert.Contains("mc_", sw.ToString());
        }
        finally { Console.SetError(originalErr); }
    }

    [Fact]
    public void Constructor_NoWarningForLiveKey()
    {
        var originalErr = Console.Error;
        var sw = new System.IO.StringWriter();
        Console.SetError(sw);
        try
        {
            _ = new MailCaptureClient("mc_testkey");
            Assert.Empty(sw.ToString());
        }
        finally { Console.SetError(originalErr); }
    }

    [Fact]
    public void Constructor_UsernamePreset()
    {
        using var mc = new MailCaptureClient("mc_testkey",
            new MailCaptureClientOptions { Username = "alice" });
        Assert.Equal("alice", mc.Username);
        Assert.Equal("alice-signup@mailcapture.app", mc.Address("signup"));
    }

    // ─── Ping ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ping_ReturnsResult()
    {
        var handler = new MockHttpHandler().Enqueue(Json(HttpStatusCode.OK, Ping("alice")));
        using var mc = MakeClient(handler);

        var result = await mc.PingAsync();

        Assert.Equal("alice", result.Username);
        Assert.Equal("alice-{tag}@mailcapture.app", result.AddressTemplate);
    }

    [Fact]
    public async Task Ping_CachesUsername()
    {
        var handler = new MockHttpHandler().Enqueue(Json(HttpStatusCode.OK, Ping("bob")));
        using var mc = MakeClient(handler);

        Assert.Null(mc.Username);
        await mc.PingAsync();
        Assert.Equal("bob", mc.Username);
    }

    [Fact]
    public async Task Ping_SendsApiKeyHeader()
    {
        var handler = new MockHttpHandler().Enqueue(Json(HttpStatusCode.OK, Ping()));
        using var mc = MakeClient(handler);

        await mc.PingAsync();

        var req = handler.Requests[0];
        Assert.Equal("mc_testkey", req.Headers.GetValues("X-API-Key").First());
    }

    [Fact]
    public async Task Ping_ThrowsAuthExceptionOn401()
    {
        var handler = new MockHttpHandler()
            .Enqueue(Json(HttpStatusCode.Unauthorized, Error("UNAUTHORIZED", "Invalid API key")));
        using var mc = MakeClient(handler);

        var ex = await Assert.ThrowsAsync<MailCaptureAuthException>(() => mc.PingAsync());
        Assert.Equal("UNAUTHORIZED", ex.ErrorCode);
        Assert.Contains("Authentication failed", ex.Message);
        Assert.Contains("mc_", ex.Message);
    }

    // ─── Address ──────────────────────────────────────────────────────────────

    [Fact]
    public void Address_ThrowsBeforePing()
    {
        using var mc = new MailCaptureClient("mc_testkey");
        Assert.Throws<InvalidOperationException>(() => mc.Address("signup"));
    }

    [Fact]
    public async Task Address_ReturnsCorrectEmailAfterPing()
    {
        var handler = new MockHttpHandler().Enqueue(Json(HttpStatusCode.OK, Ping("carol")));
        using var mc = MakeClient(handler);
        await mc.PingAsync();

        Assert.Equal("carol-signup@mailcapture.app", mc.Address("signup"));
        Assert.Equal("carol-password-reset@mailcapture.app", mc.Address("password-reset"));
    }

    // ─── WaitFor ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task WaitFor_ReturnsFirstCapture()
    {
        var cap = Capture(id: "cap-1", otp: "999999");
        var handler = new MockHttpHandler().Enqueue(Json(HttpStatusCode.OK, Latest(cap)));
        using var mc = MakeClient(handler);

        var email = await mc.WaitForAsync("signup", new() { Timeout = TimeSpan.FromSeconds(5) });

        Assert.Equal("cap-1", email.Id);
        Assert.Equal("999999", email.Otp);
    }

    [Fact]
    public async Task WaitFor_LoopsOn408ThenReturnsCapture()
    {
        var cap = Capture(id: "cap-2", otp: "654321");
        var handler = new MockHttpHandler()
            .Enqueue(Json(HttpStatusCode.RequestTimeout, TimeoutBody()))
            .Enqueue(Json(HttpStatusCode.OK, Latest(cap)));
        using var mc = MakeClient(handler);

        var email = await mc.WaitForAsync("signup", new()
        {
            Timeout = TimeSpan.FromSeconds(30),
            PollTimeout = TimeSpan.FromSeconds(1),
        });

        Assert.Equal("cap-2", email.Id);
        Assert.Equal("654321", email.Otp);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task WaitFor_ThrowsTimeoutException()
    {
        var handler = new MockHttpHandler()
            .Always(Json(HttpStatusCode.RequestTimeout, TimeoutBody()));
        using var mc = MakeClient(handler);

        var ex = await Assert.ThrowsAsync<MailCaptureTimeoutException>(() =>
            mc.WaitForAsync("signup", new()
            {
                Timeout = TimeSpan.FromMilliseconds(200),
                PollTimeout = TimeSpan.FromSeconds(1),
            }));

        Assert.Equal("signup", ex.Tag);
        Assert.Equal("TIMEOUT", ex.ErrorCode);
        Assert.Contains("\"signup\"", ex.Message);
    }

    [Fact]
    public async Task WaitFor_TimeoutHintIncludesAddress()
    {
        var handler = new MockHttpHandler()
            .Enqueue(Json(HttpStatusCode.OK, Ping("alice")))
            .Always(Json(HttpStatusCode.RequestTimeout, TimeoutBody()));
        using var mc = MakeClient(handler);
        await mc.PingAsync();

        var ex = await Assert.ThrowsAsync<MailCaptureTimeoutException>(() =>
            mc.WaitForAsync("signup", new()
            {
                Timeout = TimeSpan.FromMilliseconds(200),
                PollTimeout = TimeSpan.FromSeconds(1),
            }));

        Assert.Contains("alice-signup@mailcapture.app", ex.Message);
    }

    [Fact]
    public async Task WaitFor_RespectsParentCancellation()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Handler that blocks until the cancellation token fires.
        var blockingHandler = new BlockingHttpHandler(cts.Token);
        using var mc = new MailCaptureClient("mc_testkey",
            httpClient: new HttpClient(blockingHandler));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            mc.WaitForAsync("signup",
                new() { Timeout = TimeSpan.FromSeconds(30) },
                cts.Token));
    }

    [Fact]
    public async Task WaitFor_NullOtpAndBodyFields()
    {
        var cap = new
        {
            id = "cap-null", tag = "signup", subject = "Test",
            otp = (string?)null, body_text = (string?)null, body_html = (string?)null,
            latency_ms = 50, status = "captured", received_at = "2024-01-01T00:00:00Z",
        };
        var handler = new MockHttpHandler().Enqueue(Json(HttpStatusCode.OK, Latest(cap)));
        using var mc = MakeClient(handler);

        var email = await mc.WaitForAsync("signup", new() { Timeout = TimeSpan.FromSeconds(5) });

        Assert.Null(email.Otp);
        Assert.Null(email.BodyText);
        Assert.Null(email.BodyHtml);
    }

    // ─── List ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsCaptures()
    {
        var body = new { items = new[] { Capture() }, count = 1 };
        var handler = new MockHttpHandler().Enqueue(Json(HttpStatusCode.OK, body));
        using var mc = MakeClient(handler);

        var result = await mc.ListAsync();

        Assert.Equal(1, result.Count);
        Assert.Single(result.Items);
        Assert.Equal("cap-1", result.Items[0].Id);
    }

    [Fact]
    public async Task List_SendsQueryParams()
    {
        var body = new { items = Array.Empty<object>(), count = 0 };
        var handler = new MockHttpHandler().Enqueue(Json(HttpStatusCode.OK, body));
        using var mc = MakeClient(handler);

        await mc.ListAsync(new() { Tag = "signup", Limit = 10 });

        var uri = handler.Requests[0].RequestUri!.ToString();
        Assert.Contains("tag=signup", uri);
        Assert.Contains("limit=10", uri);
    }

    // ─── Get ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ThrowsOnEmptyId()
    {
        using var mc = new MailCaptureClient("mc_testkey");
        await Assert.ThrowsAsync<ArgumentException>(() => mc.GetAsync(""));
    }

    [Fact]
    public async Task Get_ReturnsCapture()
    {
        var handler = new MockHttpHandler().Enqueue(Json(HttpStatusCode.OK, Capture(id: "cap-xyz")));
        using var mc = MakeClient(handler);

        var result = await mc.GetAsync("cap-xyz");
        Assert.Equal("cap-xyz", result.Id);
    }

    [Fact]
    public async Task Get_ThrowsNotFoundOn404()
    {
        var handler = new MockHttpHandler()
            .Enqueue(Json(HttpStatusCode.NotFound, Error("NOT_FOUND", "Resource not found")));
        using var mc = MakeClient(handler);

        var ex = await Assert.ThrowsAsync<MailCaptureNotFoundException>(() => mc.GetAsync("missing"));
        Assert.Equal("NOT_FOUND", ex.ErrorCode);
    }

    // ─── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ThrowsOnEmptyTag()
    {
        using var mc = new MailCaptureClient("mc_testkey");
        await Assert.ThrowsAsync<ArgumentException>(() => mc.DeleteAsync(""));
    }

    [Fact]
    public async Task Delete_SucceedsOn204()
    {
        var handler = new MockHttpHandler().Enqueue(NoContent());
        using var mc = MakeClient(handler);

        await mc.DeleteAsync("signup"); // no exception

        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
        Assert.Contains("/v1/captures/signup", handler.Requests[0].RequestUri!.ToString());
    }

    // ─── Inbox ────────────────────────────────────────────────────────────────

    [Fact]
    public void Inbox_ThrowsOnEmptyTag()
    {
        using var mc = new MailCaptureClient("mc_testkey");
        Assert.Throws<ArgumentException>(() => mc.Inbox(""));
    }

    [Fact]
    public void Inbox_ReturnsInboxWithCorrectTag()
    {
        using var mc = new MailCaptureClient("mc_testkey");
        var inbox = mc.Inbox("signup");
        Assert.Equal("signup", inbox.Tag);
    }

    [Fact]
    public void Inbox_AddressThrowsBeforePing()
    {
        using var mc = new MailCaptureClient("mc_testkey");
        var inbox = mc.Inbox("signup");
        Assert.Throws<InvalidOperationException>(() => inbox.Address);
    }

    [Fact]
    public async Task Inbox_ClearCallsDelete()
    {
        var handler = new MockHttpHandler().Enqueue(NoContent());
        using var mc = MakeClient(handler);

        await mc.Inbox("signup").ClearAsync();

        Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
        Assert.Contains("/v1/captures/signup", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Inbox_WaitForDelegatesWithTag()
    {
        var cap = Capture(id: "cap-inbox", tag: "invite");
        var handler = new MockHttpHandler().Enqueue(Json(HttpStatusCode.OK, Latest(cap)));
        using var mc = MakeClient(handler);

        var email = await mc.Inbox("invite").WaitForAsync(new() { Timeout = TimeSpan.FromSeconds(5) });

        Assert.Equal("cap-inbox", email.Id);
        Assert.Contains("/v1/latest/invite", handler.Requests[0].RequestUri!.ToString());
    }

    // ─── Network errors ───────────────────────────────────────────────────────

    [Fact]
    public async Task Request_ThrowsNetworkExceptionOnConnectionRefused()
    {
        using var mc = new MailCaptureClient("mc_testkey",
            new MailCaptureClientOptions { BaseUrl = "http://localhost:1" });

        var ex = await Assert.ThrowsAsync<MailCaptureNetworkException>(() => mc.PingAsync());
        Assert.Equal("NETWORK_ERROR", ex.ErrorCode);
        Assert.NotNull(ex.InnerException);
    }
}

/// <summary>Handler that blocks until the cancellation token fires, then throws.</summary>
internal sealed class BlockingHttpHandler : HttpMessageHandler
{
    private readonly CancellationToken _token;
    public BlockingHttpHandler(CancellationToken token) => _token = token;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _token);
        await Task.Delay(Timeout.Infinite, linked.Token);
        return new HttpResponseMessage(); // never reached
    }
}
