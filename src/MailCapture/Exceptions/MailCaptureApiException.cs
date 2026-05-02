namespace MailCapture.Exceptions;

/// <summary>
/// Thrown when the API returns an unexpected error response.
/// </summary>
public class MailCaptureApiException : MailCaptureException
{
    /// <summary>HTTP status code returned by the server.</summary>
    public int StatusCode { get; }

    /// <summary>Human-readable detail from the server, if any.</summary>
    public string? Detail { get; }

    public MailCaptureApiException(int statusCode, string code, string? detail = null)
        : base(
            detail is not null
                ? $"API error ({statusCode}): {detail}"
                : $"API error ({statusCode}): {code}",
            code)
    {
        StatusCode = statusCode;
        Detail = detail;
    }
}
