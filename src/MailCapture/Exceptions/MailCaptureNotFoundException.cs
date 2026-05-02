namespace MailCapture.Exceptions;

/// <summary>
/// Thrown when a capture is not found by ID.
/// The capture may have expired, been deleted, or the ID may be wrong.
/// </summary>
public class MailCaptureNotFoundException : MailCaptureException
{
    public MailCaptureNotFoundException(string? detail = null)
        : base(
            detail ?? "Capture not found. It may have expired, been deleted, or the ID is incorrect.",
            "NOT_FOUND") { }
}
