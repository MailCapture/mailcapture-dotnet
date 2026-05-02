namespace MailCapture.Exceptions;

/// <summary>
/// Thrown when the SDK cannot reach the MailCapture API.
/// Check your network connection and the <c>BaseUrl</c> option.
/// The original exception is available via <see cref="Exception.InnerException"/>.
/// </summary>
public class MailCaptureNetworkException : MailCaptureException
{
    public MailCaptureNetworkException(string baseUrl, Exception innerException)
        : base(
            $"Could not reach the MailCapture API at {baseUrl}. " +
            "Check your network connection and firewall settings.",
            "NETWORK_ERROR",
            innerException) { }
}
