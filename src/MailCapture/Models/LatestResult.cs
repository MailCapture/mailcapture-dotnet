namespace MailCapture.Models;

/// <summary>Internal response from GET /v1/latest/:tag.</summary>
internal record LatestResult(
    IReadOnlyList<Capture> Items,
    int Count,
    DateTimeOffset NextAfter
);

internal record ApiErrorBody(
    string? Status,
    string? Message,
    string? Detail
);
