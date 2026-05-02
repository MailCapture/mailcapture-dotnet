namespace MailCapture.Exceptions;

/// <summary>
/// Base class for all MailCapture SDK exceptions.
/// Check <see cref="ErrorCode"/> for a machine-readable error type.
/// </summary>
public class MailCaptureException : Exception
{
    /// <summary>Machine-readable error code, e.g. "UNAUTHORIZED", "TIMEOUT".</summary>
    public string ErrorCode { get; }

    public MailCaptureException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public MailCaptureException(string message, string errorCode, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
