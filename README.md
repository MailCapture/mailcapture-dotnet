# MailCapture C# SDK

Official C# / .NET SDK for [MailCapture](https://mailcapture.app) — a real email capture API for integration testing OTP codes, verification links, and other transactional emails.

Zero runtime dependencies. Framework agnostic — works with ASP.NET Core, Blazor, console apps, or any other .NET project.

## Requirements

- .NET 8+ (targets `net8.0`)

## Installation

```bash
dotnet add package MailCapture
```

Or in your `.csproj`:

```xml
<PackageReference Include="MailCapture" Version="0.1.0" />
```

## Quick start

```csharp
using MailCapture;
using MailCapture.Options;

var mc = new MailCaptureClient(Environment.GetEnvironmentVariable("MAILCAPTURE_API_KEY")!);
await mc.PingAsync();  // validates key, caches username

// In your test:
await mc.DeleteAsync("signup");
await yourApp.RegisterAsync(mc.Address("signup"));   // "alice-signup@mailcapture.app"

var email = await mc.WaitForAsync("signup", new() { Timeout = TimeSpan.FromSeconds(15) });
Console.WriteLine(email.Otp);     // "123456"
Console.WriteLine(email.Subject); // "Verify your account"
```

## Integration test pattern (xUnit)

```csharp
using MailCapture;
using MailCapture.Options;
using Xunit;

public class UserRegistrationTests : IAsyncLifetime
{
    private readonly MailCaptureClient _mc;
    private readonly Inbox _inbox;

    public UserRegistrationTests()
    {
        _mc    = new MailCaptureClient(Environment.GetEnvironmentVariable("MAILCAPTURE_API_KEY")!);
        _inbox = _mc.Inbox("signup");
    }

    public async Task InitializeAsync()
    {
        await _mc.PingAsync();     // validates key, caches username
        await _inbox.ClearAsync(); // clean inbox before each test
    }

    public Task DisposeAsync() => _inbox.ClearAsync();

    [Fact]
    public async Task SendsVerificationEmail()
    {
        await yourApp.RegisterAsync(_inbox.Address);

        var email = await _inbox.WaitForAsync(new() { Timeout = TimeSpan.FromSeconds(10) });

        Assert.Equal("Verify your account", email.Subject);
        Assert.Matches(@"^\d{6}$", email.Otp!);
        Assert.True(email.LatencyMs < 5000);
    }
}
```

## ASP.NET Core dependency injection

```csharp
// Program.cs
builder.Services.AddSingleton(sp =>
    new MailCaptureClient(
        builder.Configuration["MailCapture:ApiKey"]!,
        new MailCaptureClientOptions
        {
            Username = builder.Configuration["MailCapture:Username"],
        }));
```

```csharp
// In your test class
public class EmailTests(MailCaptureClient mc) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task PasswordResetSendsOtp()
    {
        var inbox = mc.Inbox("password-reset");
        await inbox.ClearAsync();

        await yourApp.RequestPasswordResetAsync(inbox.Address);

        var email = await inbox.WaitForAsync(new() { Timeout = TimeSpan.FromSeconds(10) });
        Assert.NotNull(email.Otp);
    }
}
```

## API reference

### `new MailCaptureClient(apiKey, options?, httpClient?)`

```csharp
// Minimal
var mc = new MailCaptureClient(apiKey);

// With options
var mc = new MailCaptureClient(apiKey, new MailCaptureClientOptions
{
    BaseUrl        = "http://localhost:3002",  // local dev
    RequestTimeout = TimeSpan.FromSeconds(15),
    Username       = "alice",                  // skip PingAsync()
});

// With injected HttpClient (for testing or custom transport)
var mc = new MailCaptureClient(apiKey, httpClient: myHttpClient);
```

`MailCaptureClient` implements `IDisposable`. Dispose it or register it as a singleton.

---

### `PingAsync(ct)` → `PingResult`

Validates your API key and returns your address template. Caches the username so `Address()` works without a network call.

```csharp
var result = await mc.PingAsync();
result.Username         // "alice"
result.AddressTemplate  // "alice-{tag}@mailcapture.app"
result.Example          // "alice-signup@mailcapture.app"
```

---

### `WaitForAsync(tag, options?, ct)` → `Capture`

Long-polls the API and returns the first email for the given tag. The server holds the connection open — no client-side busy-waiting.

Pass a `CancellationToken` to enforce test-level deadlines (xUnit's `TestContext.Current.CancellationToken`, or your own via `CancellationTokenSource`).

```csharp
// 30-second default
var email = await mc.WaitForAsync("signup");

// Custom timeout
var email = await mc.WaitForAsync("signup", new() { Timeout = TimeSpan.FromSeconds(15) });

// Full options
var email = await mc.WaitForAsync("signup", new WaitOptions
{
    Timeout     = TimeSpan.FromSeconds(15),
    PollTimeout = TimeSpan.FromSeconds(5),   // per-poll server timeout, max 30s
    After       = DateTimeOffset.UtcNow,     // only captures received after now
});
```

Throws `MailCaptureTimeoutException` if no email arrives in time.

---

### `Inbox(tag)` → `Inbox`

Returns a scoped `Inbox` for a tag. Recommended for all test code.

```csharp
var inbox = mc.Inbox("password-reset");

inbox.Address                         // "alice-password-reset@mailcapture.app"
await inbox.WaitForAsync(options, ct)
await inbox.ListAsync(options, ct)
await inbox.ClearAsync(ct)            // deletes all captures for this tag
```

---

### `Address(tag)` → `string`

Returns the capture email address synchronously. Requires `PingAsync()` first or `Username` in options.

```csharp
await mc.PingAsync();
mc.Address("signup") // "alice-signup@mailcapture.app"
```

---

### `ListAsync(options?, ct)` → `CaptureList`

```csharp
var list = await mc.ListAsync(new() { Tag = "signup", Limit = 10 });
foreach (var email in list.Items)
    output.WriteLine(email.Subject);
```

---

### `GetAsync(captureId, ct)` → `Capture`

Throws `MailCaptureNotFoundException` if the capture doesn't exist.

---

### `DeleteAsync(tag, ct)`

Deletes all captures for a tag. Use in `IAsyncLifetime.InitializeAsync` or `t.Cleanup`.

---

## The `Capture` record

```csharp
record Capture(
    string          Id,         // UUID
    string          Tag,        // e.g. "signup"
    string          Subject,    // email subject line
    string?         Otp,        // extracted code — null if none detected
    string?         BodyText,   // plain-text body
    string?         BodyHtml,   // HTML body
    int             LatencyMs,  // send-to-capture time in ms
    string          Status,     // e.g. "captured"
    DateTimeOffset  ReceivedAt  // when received
);
```

`Otp` is nullable — always null-check or use `email.Otp!` only when you've asserted it's set.

---

## Exception handling

All exceptions inherit from `MailCaptureException` which has an `ErrorCode` property.

```csharp
try
{
    var email = await mc.WaitForAsync("signup", new() { Timeout = TimeSpan.FromSeconds(10) });
}
catch (MailCaptureTimeoutException e)
{
    output.WriteLine($"Waited {e.WaitedFor.TotalSeconds:0}s for tag \"{e.Tag}\"");
    output.WriteLine("Did the email send? Check your email service logs.");
}
catch (MailCaptureAuthException)
{
    output.WriteLine("Check your MAILCAPTURE_API_KEY environment variable.");
}
catch (MailCaptureNetworkException e)
{
    output.WriteLine($"Network error: {e.InnerException?.Message}");
}
```

| Exception | `ErrorCode` | When |
|---|---|---|
| `MailCaptureAuthException` | `UNAUTHORIZED` | Invalid or revoked API key |
| `MailCaptureTimeoutException` | `TIMEOUT` | `WaitForAsync` exceeded timeout |
| `MailCaptureNotFoundException` | `NOT_FOUND` | `GetAsync` — capture not found |
| `MailCaptureNetworkException` | `NETWORK_ERROR` | Could not reach the API |
| `MailCaptureApiException` | varies | Unexpected API error |

---

## Testing with a mock `HttpClient`

Inject a custom `HttpMessageHandler` to unit-test code that uses `MailCaptureClient` without hitting the real API:

```csharp
var handler = new MockHttpMessageHandler();
handler.When("/v1/ping")
    .Respond("application/json", JsonSerializer.Serialize(new
    {
        status = "ok", username = "alice",
        address_template = "alice-{tag}@mailcapture.app",
        example = "alice-signup@mailcapture.app",
    }));

var mc = new MailCaptureClient(apiKey, httpClient: new HttpClient(handler)
{
    BaseAddress = new Uri("https://mailcapture.app")
});
```

Or with the SDK's own `MockHttpHandler` from the tests project (copy it into your test project).

---

## CI configuration

```yaml
# .github/workflows/integration.yml
- name: Run integration tests
  env:
    MAILCAPTURE_API_KEY: ${{ secrets.MAILCAPTURE_API_KEY }}
  run: dotnet test --filter Category=Integration --timeout 120
```

---

## Local development

```csharp
var mc = new MailCaptureClient(apiKey, new MailCaptureClientOptions
{
    BaseUrl = "http://localhost:3002",
});
```
