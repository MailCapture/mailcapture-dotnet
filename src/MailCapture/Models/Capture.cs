namespace MailCapture.Models;

/// <summary>
/// A captured email.
/// </summary>
/// <param name="Id">Unique ID for this capture.</param>
/// <param name="Tag">Tag portion of the address, e.g. "signup" from "alice-signup@mailcapture.app".</param>
/// <param name="Subject">Email subject line.</param>
/// <param name="Otp">Extracted OTP / verification code, or <c>null</c> if none detected. The service parses codes automatically.</param>
/// <param name="BodyText">Plain-text body of the email, or <c>null</c> if not present.</param>
/// <param name="BodyHtml">HTML body of the email, or <c>null</c> if not present.</param>
/// <param name="LatencyMs">Time from email send to capture receipt, in milliseconds.</param>
/// <param name="Status">Email delivery status, e.g. "captured".</param>
/// <param name="ReceivedAt">When the email was received.</param>
/// <example>
/// <code>
/// var email = await mc.WaitForAsync("signup");
/// Console.WriteLine(email.Otp);        // "123456"
/// Console.WriteLine(email.Subject);    // "Verify your account"
/// Console.WriteLine(email.LatencyMs);  // 145
/// </code>
/// </example>
public record Capture(
    string Id,
    string Tag,
    string Subject,
    string? Otp,
    string? BodyText,
    string? BodyHtml,
    int LatencyMs,
    string Status,
    DateTimeOffset ReceivedAt
);
