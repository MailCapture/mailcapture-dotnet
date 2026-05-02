namespace MailCapture.Models;

/// <summary>Response from <see cref="MailCaptureClient.ListAsync"/>.</summary>
public record CaptureList(
    IReadOnlyList<Capture> Items,
    int Count
);
