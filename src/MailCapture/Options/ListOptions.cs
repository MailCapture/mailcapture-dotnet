namespace MailCapture.Options;

/// <summary>Options for <see cref="MailCaptureClient.ListAsync"/>.</summary>
public class ListOptions
{
    /// <summary>Filter captures by tag.</summary>
    public string? Tag { get; init; }

    /// <summary>Maximum results to return (1–100). Default: 25.</summary>
    public int? Limit { get; init; }

    /// <summary>Only return captures received after this time.</summary>
    public DateTimeOffset? After { get; init; }
}
