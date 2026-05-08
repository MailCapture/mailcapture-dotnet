namespace MailCapture.Exceptions;

/// <summary>
/// Thrown when authentication fails — the API key is invalid, expired, or revoked.
/// </summary>
/// <example>
/// <code>
/// try { await mc.PingAsync(); }
/// catch (MailCaptureAuthException)
/// {
///     // Check your MAILCAPTURE_API_KEY environment variable.
/// }
/// </code>
/// </example>
public class MailCaptureAuthException : MailCaptureException
{
    public MailCaptureAuthException(string? detail = null)
        : base(BuildMessage(detail), "UNAUTHORIZED") { }

    private static string BuildMessage(string? detail)
    {
        var hint = detail is not null ? $"Server said: \"{detail}\"." : "Your API key was rejected.";
        return $"Authentication failed. {hint} " +
               "Make sure your key is valid and has not been revoked. " +
               "Keys look like: mc_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx. " +
               "Find your keys at https://mailcapture.app/admin/api-keys";
    }
}
