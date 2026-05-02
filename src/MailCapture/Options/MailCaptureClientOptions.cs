namespace MailCapture.Options;

/// <summary>
/// Options for configuring <see cref="MailCaptureClient"/>.
/// </summary>
/// <example>
/// <code>
/// var mc = new MailCaptureClient(apiKey, new MailCaptureClientOptions
/// {
///     BaseUrl = "http://localhost:3002",
///     RequestTimeout = TimeSpan.FromSeconds(15),
/// });
/// </code>
/// </example>
public class MailCaptureClientOptions
{
    /// <summary>
    /// API base URL. Override for local development.
    /// Default: <c>https://mailcapture.app</c>
    /// </summary>
    public string BaseUrl { get; init; } = "https://mailcapture.app";

    /// <summary>
    /// Default timeout for non-polling requests.
    /// Default: 10 seconds.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Pre-set your username to skip the <see cref="MailCaptureClient.PingAsync"/> call.
    /// Useful in CI when you already know your username.
    /// </summary>
    public string? Username { get; init; }
}
