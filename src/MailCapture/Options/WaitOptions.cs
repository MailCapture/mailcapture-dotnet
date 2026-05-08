namespace MailCapture.Options;

/// <summary>
/// Options for <see cref="MailCaptureClient.WaitForAsync"/>.
/// </summary>
/// <example>
/// <code>
/// var email = await mc.WaitForAsync("signup", new WaitOptions
/// {
///     Timeout = TimeSpan.FromSeconds(15),
///     After = DateTimeOffset.UtcNow,
/// });
/// </code>
/// </example>
public class WaitOptions
{
    /// <summary>
    /// Total time to wait for an email.
    /// Default: 60 seconds.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Per-poll server timeout (max 30 seconds).
    /// Lower values mean the loop checks in more frequently.
    /// Default: 10 seconds.
    /// </summary>
    public TimeSpan PollTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Only return captures received strictly after this time.
    /// Defaults to 60 seconds ago, so recent emails are included
    /// but stale ones from previous test runs are ignored.
    /// </summary>
    public DateTimeOffset? After { get; init; }
}
