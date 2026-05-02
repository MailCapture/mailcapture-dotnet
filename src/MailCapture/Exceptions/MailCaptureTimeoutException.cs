namespace MailCapture.Exceptions;

/// <summary>
/// Thrown by <see cref="MailCaptureClient.WaitForAsync"/> when no email
/// arrives before the timeout elapses.
/// </summary>
/// <example>
/// <code>
/// try { var email = await mc.WaitForAsync("signup", new() { Timeout = TimeSpan.FromSeconds(10) }); }
/// catch (MailCaptureTimeoutException e)
/// {
///     output.WriteLine($"Waited {e.WaitedFor.TotalSeconds:0}s for tag: {e.Tag}");
/// }
/// </code>
/// </example>
public class MailCaptureTimeoutException : MailCaptureException
{
    /// <summary>The tag that was being waited on.</summary>
    public string Tag { get; }

    /// <summary>How long the caller waited before giving up.</summary>
    public TimeSpan WaitedFor { get; }

    public MailCaptureTimeoutException(string tag, TimeSpan waitedFor, string? hint = null)
        : base(BuildMessage(tag, waitedFor, hint), "TIMEOUT")
    {
        Tag = tag;
        WaitedFor = waitedFor;
    }

    private static string BuildMessage(string tag, TimeSpan waitedFor, string? hint)
    {
        var msg = $"No email arrived for tag \"{tag}\" within {waitedFor.TotalSeconds:0}s.";
        return hint is not null ? $"{msg} {hint}" : msg;
    }
}
