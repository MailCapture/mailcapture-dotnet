using MailCapture.Models;
using MailCapture.Options;

namespace MailCapture;

/// <summary>
/// A scoped handle for a single capture inbox (tag).
/// Create one with <see cref="MailCaptureClient.Inbox"/>.
///
/// <para>Keeps test code clean by binding the tag once:</para>
/// </summary>
/// <example>
/// <code>
/// var inbox = mc.Inbox("signup");
/// await inbox.ClearAsync();
/// await yourApp.RegisterAsync(inbox.Address);
/// var email = await inbox.WaitForAsync(new() { Timeout = TimeSpan.FromSeconds(15) });
/// Assert.Equal("123456", email.Otp);
/// </code>
/// </example>
public sealed class Inbox
{
    private readonly MailCaptureClient _client;

    /// <summary>The tag this inbox is scoped to.</summary>
    public string Tag { get; }

    internal Inbox(MailCaptureClient client, string tag)
    {
        _client = client;
        Tag = tag;
    }

    /// <summary>
    /// The full capture email address for this inbox,
    /// e.g. "alice-signup@mailcapture.app".
    /// <para>Requires <see cref="MailCaptureClient.PingAsync"/> to have been called first,
    /// or <see cref="MailCaptureClientOptions.Username"/> to be set.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">If the username is not yet known.</exception>
    public string Address => _client.Address(Tag);

    /// <summary>Wait for an email to arrive. See <see cref="MailCaptureClient.WaitForAsync"/>.</summary>
    public Task<Capture> WaitForAsync(
        WaitOptions? options = null,
        CancellationToken cancellationToken = default)
        => _client.WaitForAsync(Tag, options, cancellationToken);

    /// <summary>List recent captures. See <see cref="MailCaptureClient.ListAsync"/>.</summary>
    public Task<CaptureList> ListAsync(
        ListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options is null
            ? new ListOptions { Tag = Tag }
            : new ListOptions { Tag = Tag, Limit = options.Limit, After = options.After };
        return _client.ListAsync(opts, cancellationToken);
    }

    /// <summary>
    /// Delete all captures in this inbox.
    /// Call before each test to ensure a clean starting state.
    /// </summary>
    /// <example>
    /// <code>
    /// // xUnit: in constructor or via fixture
    /// await inbox.ClearAsync();
    /// </code>
    /// </example>
    public Task ClearAsync(CancellationToken cancellationToken = default)
        => _client.DeleteAsync(Tag, cancellationToken);

    public override string ToString() => $"Inbox(tag=\"{Tag}\")";
}
