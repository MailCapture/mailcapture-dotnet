using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using MailCapture.Exceptions;
using MailCapture.Models;
using MailCapture.Options;

namespace MailCapture;

/// <summary>
/// MailCapture API client. Create one instance and reuse it across your test suite.
/// </summary>
/// <example>
/// <code>
/// // Minimal
/// var mc = new MailCaptureClient(Environment.GetEnvironmentVariable("MAILCAPTURE_API_KEY")!);
///
/// // With options
/// var mc = new MailCaptureClient(apiKey, new MailCaptureClientOptions
/// {
///     BaseUrl = "http://localhost:3002",
///     RequestTimeout = TimeSpan.FromSeconds(15),
/// });
///
/// // In your test:
/// await mc.PingAsync();
/// await mc.DeleteAsync("signup");
/// await yourApp.RegisterAsync(mc.Address("signup"));
/// var email = await mc.WaitForAsync("signup", new() { Timeout = TimeSpan.FromSeconds(15) });
/// Assert.Equal("123456", email.Otp);
/// </code>
/// </example>
public sealed class MailCaptureClient : IDisposable
{
    private const int MaxPollSeconds = 30;
    private static readonly TimeSpan ServerPollBuffer = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly TimeSpan _requestTimeout;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private string? _username;

    private static readonly string[] s_adjectives =
    [
        "angry","bold","brave","calm","cold","cool","dark","dizzy",
        "dusty","eager","fierce","fluffy","funky","fuzzy","glad",
        "gloomy","grumpy","hasty","hungry","icy","itchy","jolly",
        "jumpy","keen","lazy","lucky","mad","mean","moody","muddy",
        "noisy","odd","pale","peppy","proud","quick","quiet","rowdy",
        "rusty","silly","sleepy","sneaky","spooky","swift","tiny",
        "tough","vivid","weird","wild","young",
    ];

    private static readonly string[] s_animals =
    [
        "ant","bear","boar","cat","crab","crow","deer","dove",
        "duck","eel","elk","finch","fox","frog","goat","hawk",
        "hare","ibis","jay","kiwi","lamb","lark","lion","lynx",
        "mink","mole","moth","mule","newt","owl","panda","pig",
        "puma","ram","rat","rook","seal","slug","snail","swan",
        "toad","vole","wasp","wolf","wren","yak","zebra","bat",
        "bee","carp",
    ];

    /// <summary>
    /// Create a new client with the given API key.
    /// </summary>
    /// <param name="apiKey">Your MailCapture API key (<c>mc_...</c>).</param>
    /// <param name="options">Optional configuration.</param>
    /// <param name="httpClient">Optional custom <see cref="HttpClient"/> for testing.</param>
    public MailCaptureClient(
        string apiKey,
        MailCaptureClientOptions? options = null,
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException(
                "MailCapture: API key is required.\n" +
                "  new MailCaptureClient(Environment.GetEnvironmentVariable(\"MAILCAPTURE_API_KEY\")!)",
                nameof(apiKey));

        if (!apiKey.StartsWith("mc_", StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                "[mailcapture] Warning: API key does not start with \"mc_\". Are you sure you copied the full key? " +
                "Make sure you copied the full key from https://mailcapture.app/admin/api-keys");
        }

        var opts = options ?? new MailCaptureClientOptions();
        _apiKey = apiKey;
        _baseUrl = opts.BaseUrl.TrimEnd('/');
        _requestTimeout = opts.RequestTimeout;
        _username = opts.Username;

        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }
    }

    // -------------------------------------------------------------------------
    // Public API

    /// <summary>
    /// Validate your API key and retrieve your capture address template.
    /// Also caches your username so <see cref="Address"/> works without a network call.
    /// </summary>
    /// <exception cref="MailCaptureAuthException">If the API key is invalid.</exception>
    public async Task<PingResult> PingAsync(CancellationToken cancellationToken = default)
    {
        var result = await GetJsonAsync<PingResult>("/v1/ping", cancellationToken);
        _username = result.Username;
        return result;
    }

    /// <summary>
    /// Wait for an email to arrive at the given tag and return it.
    ///
    /// <para>Long-polls the API — the server holds the connection open and responds
    /// the instant an email arrives. No client-side busy-waiting.</para>
    ///
    /// <para>The <see cref="WaitOptions.After"/> cursor defaults to 60 seconds ago so recent
    /// emails are included but stale ones from previous runs are ignored.
    /// For maximum isolation, call <see cref="DeleteAsync"/> before triggering the email.</para>
    /// </summary>
    /// <param name="tag">The capture tag to wait on, e.g. "signup".</param>
    /// <param name="options">Optional timeout and cursor settings.</param>
    /// <param name="cancellationToken">
    /// Cancels the wait immediately. Use a linked token or <c>xUnit.CancellationToken</c>
    /// to enforce test-level deadlines.
    /// </param>
    /// <exception cref="MailCaptureTimeoutException">If no email arrives before <see cref="WaitOptions.Timeout"/>.</exception>
    /// <example>
    /// <code>
    /// // 30-second default
    /// var email = await mc.WaitForAsync("signup");
    ///
    /// // Custom timeout
    /// var email = await mc.WaitForAsync("signup", new() { Timeout = TimeSpan.FromSeconds(15) });
    ///
    /// // Only emails received after now
    /// var email = await mc.WaitForAsync("signup", new()
    /// {
    ///     Timeout = TimeSpan.FromSeconds(15),
    ///     After = DateTimeOffset.UtcNow,
    /// });
    /// </code>
    /// </example>
    public async Task<Capture> WaitForAsync(
        string tag,
        WaitOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? new WaitOptions();
        var pollTimeout = TimeSpan.FromSeconds(
            Math.Clamp(opts.PollTimeout.TotalSeconds, 1, MaxPollSeconds));
        var deadline = DateTimeOffset.UtcNow + opts.Timeout;
        var after = opts.After ?? DateTimeOffset.UtcNow.AddSeconds(-60);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            var effectivePoll = TimeSpan.FromSeconds(
                Math.Min(pollTimeout.TotalSeconds,
                         Math.Max(1, Math.Ceiling(remaining.TotalSeconds))));

            var result = await PollLatestAsync(tag, effectivePoll, after, cancellationToken);
            if (result is not null)
            {
                if (result.Items.Count > 0)
                    return result.Items[0];
                after = result.NextAfter;
            }
            // result null => server-side 408, loop again
        }

        var hint = _username is not null
            ? $"Make sure you're sending to {_username}-{tag}@mailcapture.app."
            : "Check that you're sending to the right address (call PingAsync() first to get your username).";

        throw new MailCaptureTimeoutException(tag, opts.Timeout, hint);
    }

    /// <summary>
    /// List recent captures (newest first).
    /// </summary>
    /// <example>
    /// <code>
    /// var list = await mc.ListAsync(new() { Tag = "signup", Limit = 10 });
    /// foreach (var email in list.Items)
    ///     output.WriteLine(email.Subject);
    /// </code>
    /// </example>
    public async Task<CaptureList> ListAsync(
        ListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        if (options?.Tag is not null)   query["tag"]   = options.Tag;
        if (options?.Limit is not null) query["limit"] = options.Limit.ToString();
        if (options?.After is not null) query["after"] = options.After.Value.ToString("O");

        var path = "/v1/captures" + (query.Count > 0 ? "?" + query : string.Empty);
        return await GetJsonAsync<CaptureList>(path, cancellationToken);
    }

    /// <summary>
    /// Get a single capture by ID.
    /// </summary>
    /// <exception cref="MailCaptureNotFoundException">If the capture does not exist.</exception>
    public async Task<Capture> GetAsync(
        string captureId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(captureId))
            throw new ArgumentException("captureId is required", nameof(captureId));

        return await GetJsonAsync<Capture>(
            $"/v1/captures/{Uri.EscapeDataString(captureId)}", cancellationToken);
    }

    /// <summary>
    /// Delete all captures for a tag.
    /// Call this before each test to start with a clean inbox.
    /// </summary>
    /// <example>
    /// <code>
    /// // xUnit — in constructor or IAsyncLifetime.InitializeAsync
    /// await mc.DeleteAsync("signup");
    /// </code>
    /// </example>
    public async Task DeleteAsync(string tag, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tag))
            throw new ArgumentException("tag is required", nameof(tag));

        using var response = await SendAsync(
            HttpMethod.Delete, $"/v1/captures/{Uri.EscapeDataString(tag)}",
            _requestTimeout, cancellationToken);

        if (response.StatusCode != HttpStatusCode.NoContent &&
            !response.IsSuccessStatusCode)
        {
            await ThrowApiErrorAsync(response);
        }
    }

    /// <summary>
    /// Get a scoped <see cref="Inbox"/> for a specific tag.
    /// </summary>
    /// <example>
    /// <code>
    /// var inbox = mc.Inbox("password-reset");
    /// await inbox.ClearAsync();
    /// await yourApp.RequestPasswordResetAsync(inbox.Address);
    /// var email = await inbox.WaitForAsync(new() { Timeout = TimeSpan.FromSeconds(10) });
    /// Assert.NotNull(email.Otp);
    /// </code>
    /// </example>
    public Inbox Inbox(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            throw new ArgumentException("tag is required", nameof(tag));
        return new Inbox(this, tag);
    }

    /// <summary>
    /// Get the full capture email address for a tag.
    /// Requires <see cref="PingAsync"/> to have been called first,
    /// or <see cref="MailCaptureClientOptions.Username"/> to be set.
    /// </summary>
    /// <exception cref="InvalidOperationException">If the username is not yet known.</exception>
    public string Address(string tag)
    {
        if (_username is null)
            throw new InvalidOperationException(
                "MailCapture: username is not known. " +
                "Call await PingAsync() first, or set Username in MailCaptureClientOptions.");
        return $"{_username}-{tag}@mailcapture.app";
    }

    /// <summary>
    /// Generate a unique, human-readable tag such as <c>"funky-otter-a3f2b8"</c>.
    /// No client or network call needed — safe to call before <see cref="PingAsync"/>.
    /// </summary>
    /// <remarks>
    /// Format: <c>{adjective}-{animal}-{6 hex digits}</c>.
    /// ~42 billion combinations — collision probability &lt; 0.1% across 10 000 tags.
    /// </remarks>
    public static string GenerateTag()
    {
        var adj    = s_adjectives[Random.Shared.Next(s_adjectives.Length)];
        var animal = s_animals[Random.Shared.Next(s_animals.Length)];
        var suffix = Random.Shared.Next(0x1000000).ToString("x6");
        return $"{adj}-{animal}-{suffix}";
    }

    /// <summary>
    /// Generate a unique tag and its corresponding capture email address.
    /// Requires <see cref="PingAsync"/> to have been called first (same contract
    /// as <see cref="Address"/>).
    /// </summary>
    /// <example>
    /// <code>
    /// await mc.PingAsync();
    /// var (tag, email) = mc.Generate();
    /// // tag:   "funky-otter-a3f2b8"
    /// // email: "alice-funky-otter-a3f2b8@mailcapture.app"
    /// await yourApp.RegisterAsync(email);
    /// var capture = await mc.WaitForAsync(tag, new() { Timeout = TimeSpan.FromSeconds(15) });
    /// </code>
    /// </example>
    public (string Tag, string Email) Generate()
    {
        var tag = GenerateTag();
        return (tag, Address(tag));
    }

    /// <summary>
    /// Your cached username, set after <see cref="PingAsync"/> or via
    /// <see cref="MailCaptureClientOptions.Username"/>.
    /// </summary>
    public string? Username => _username;

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    // -------------------------------------------------------------------------
    // Internals

    /// <summary>
    /// Long-poll /v1/latest/:tag once.
    /// Returns <c>null</c> on a server-side 408 — caller loops again.
    /// </summary>
    private async Task<LatestResult?> PollLatestAsync(
        string tag, TimeSpan pollTimeout, DateTimeOffset after,
        CancellationToken cancellationToken)
    {
        var clientTimeout = pollTimeout + ServerPollBuffer;
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["timeout"] = ((int)pollTimeout.TotalSeconds).ToString();
        query["after"] = after.UtcDateTime.ToString("O");

        var path = $"/v1/latest/{Uri.EscapeDataString(tag)}?{query}";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(clientTimeout);

        HttpResponseMessage response;
        try
        {
            response = await SendAsync(HttpMethod.Get, path, clientTimeout, cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our per-poll CTS fired (not the parent) — treat as server timeout, loop again.
            return null;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.RequestTimeout)
                return null; // server-side 408

            if (!response.IsSuccessStatusCode)
                await ThrowApiErrorAsync(response);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<LatestResult>(body, JsonOptions)
                ?? throw new MailCaptureApiException(200, "INVALID_RESPONSE", "Empty response body.");
        }
    }

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, _requestTimeout, cancellationToken);

        if (!response.IsSuccessStatusCode)
            await ThrowApiErrorAsync(response);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new MailCaptureApiException(200, "INVALID_RESPONSE", "Empty response body.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, _baseUrl + path);
        request.Headers.Add("X-API-Key", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MailCaptureNetworkException(_baseUrl, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MailCaptureNetworkException(_baseUrl, ex);
        }
    }

    private static async Task ThrowApiErrorAsync(HttpResponseMessage response)
    {
        ApiErrorBody? body = null;
        try
        {
            var json = await response.Content.ReadAsStringAsync();
            body = JsonSerializer.Deserialize<ApiErrorBody>(json, JsonOptions);
        }
        catch { /* best-effort */ }

        var code = body?.Message ?? "UNKNOWN_ERROR";
        var detail = body?.Detail;

        throw (int)response.StatusCode switch
        {
            401 => new MailCaptureAuthException(detail) as MailCaptureException,
            404 => new MailCaptureNotFoundException(detail),
            _   => new MailCaptureApiException((int)response.StatusCode, code, detail),
        };
    }
}
