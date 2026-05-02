namespace MailCapture.Models;

/// <summary>Response from <see cref="MailCaptureClient.PingAsync"/>.</summary>
/// <param name="Status">API status string.</param>
/// <param name="Username">Your unique username — the prefix in all capture addresses.</param>
/// <param name="AddressTemplate">Template string: replace <c>{tag}</c> with your desired tag.</param>
/// <param name="Example">A concrete example address, e.g. "alice-signup@mailcapture.app".</param>
public record PingResult(
    string Status,
    string Username,
    string AddressTemplate,
    string Example
);
