# Selpo.EET2.0

`Selpo.EET2.0` is a .NET connector for integrating applications with the Czech Ministry of Finance EET 2.0 service.

The package targets both .NET Framework 4.8.1 and .NET 10.

## Status

The core EET 2.0 protocol implementation is in place: message serialization, XML signing (XAdES), SOAP envelope construction, transport, and response parsing are implemented and covered by unit tests. The public API may still evolve as more of the official EET 2.0 technical specification is validated against the tax authority playground.

## Target framework

- .NET Framework 4.8.1 (`net481`)
- .NET 10 (`net10.0`)

## Installation

```bash
dotnet add package Selpo.EET2.0
```

## Example

```csharp
using Selpo.Eet20;

using var client = new EetClient(new EetClientOptions
{
    BaseAddress = "https://pg.trzbyeet.gov.cz:443/eet/services/EETServiceSOAP/v4",
    SigningCertificatePath = @"C:\certs\playground.p12",
    SigningCertificatePassword = "changeit",
    UseSystemCertificateTrust = true
});

var response = await client.RegisterSaleAsync(new RegisteredSale
{
    Eic = "CZ8551015704",
    UnitId = 181,
    PosId = "00/2535/CN58",
    TransactionNumber = "2024-0001",
    SubmissionTime = DateTimeOffset.Now,
    TransactionTime = DateTimeOffset.Now,
    TotalAmount = 236.00m,
    VerificationMode = false,
    FirstSubmission = true
});

switch (response)
{
    case EetAcknowledgementResponse ack:
        Console.WriteLine($"Accepted. POK: {ack.Pok}, received at: {ack.ReceivedAt}");
        break;
    case EetErrorResponse error:
        Console.WriteLine($"Rejected. Error {error.ErrorCode}: {error.ErrorMessage}");
        break;
}
```

- `BaseAddress` points at the EET playground endpoint above; use the production endpoint `https://prod.eet.cz:443/eet/services/EETServiceSOAP/v4` for live traffic. The full WSDL/XSD contract for both environments is available under [`docs/protocol`](../../docs/protocol) (`EETServiceSOAP.wsdl`, `EETXMLSchema.xsd`).
- `SigningCertificate`/`SigningCertificatePath` provide the certificate used to sign the message; `AuthorityRootCertificatePath`/`AuthorityIntermediateCertificatePath` (or their `X509Certificate2` equivalents) can be used to pin the authority's certificates instead of relying on `UseSystemCertificateTrust`.
- `RegisterSaleAsync` throws `EetValidationException` for invalid input, `EetProtocolException` for SOAP fault responses, and `EetTransportException` for network-level failures.

### Production DNS and long-running connections

The production EET endpoint is served behind a DNS-based high-availability setup, and its DNS record can change over time (e.g. during Ministry of Finance infrastructure maintenance). When `EetClient(EetClientOptions)` creates its own `HttpClient`, it bounds pooled connection lifetime using `EetClientOptions.HttpConnectionLifetime` (defaults to 5 minutes) so long-running services periodically re-resolve DNS instead of pinning a stale address. If you supply your own `HttpClient` via `EetClient(HttpClient, EetClientOptions)`, configure a similar connection lifetime yourself (for example `SocketsHttpHandler.PooledConnectionLifetime` on .NET, or `ServicePoint.ConnectionLeaseTimeout`/`ServicePointManager.DnsRefreshTimeout` on .NET Framework).

## Automatic resend on temporary errors

The EET 2.0 service can respond with error code `-1` ("temporary technical error in processing - please re-send the data message later"). `EetClient` can automatically resend the sale in that case:

```csharp
using var client = new EetClient(new EetClientOptions
{
    BaseAddress = "https://pg.trzbyeet.gov.cz:443/eet/services/EETServiceSOAP/v4",
    SigningCertificatePath = @"C:\certs\playground.p12",
    SigningCertificatePassword = "changeit",
    EnableAutomaticResend = true,
    ResendDelays = new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) },
    OnResendAttempt = attempt =>
    {
        if (attempt.IsFinalAttempt)
        {
            // All resend attempts have been exhausted and the sale is still not registered.
            // Notify an operator / alerting system here so the sale can be handled manually.
            Console.Error.WriteLine($"Sale still not registered after {attempt.AttemptNumber} attempts.");
        }
        else
        {
            Console.WriteLine($"Attempt {attempt.AttemptNumber}/{attempt.MaxAttempts} failed with a temporary error, resending in {attempt.Delay}.");
        }
    }
});
```

- `EnableAutomaticResend` is `false` by default; the library never resends on its own unless you opt in.
- `ResendDelays` is **not** provided by the library - you must explicitly supply the schedule (the delay before each resend attempt). The number of entries caps the number of automatic resends (e.g. three delays allow up to three resends after the initial attempt). Enabling `EnableAutomaticResend` without `ResendDelays` throws `InvalidOperationException`.
- Each resend uses a newly generated message UUID and `FirstSubmission = false`, per the EET 2.0 specification for repeated submissions.
- `OnResendAttempt` is invoked after every resend attempt, including the last one if the schedule is exhausted and the error persists (`IsFinalAttempt == true`) - use it to log or alert so a persistently failing sale does not go unnoticed. `RegisterSaleAsync` still returns the final `EetErrorResponse` to the caller in that case.
- Automatic resend only covers EET-level temporary errors (error code `-1`). If a resend attempt itself fails with a transport, protocol, or signing exception (e.g. a network outage or an expired certificate), that exception propagates immediately from `RegisterSaleAsync` and the resend schedule is abandoned - it is not retried, and `OnResendAttempt` is not invoked for that attempt.

## Diagnostics

`EetClientOptions.OnDiagnosticEvent` is a lightweight, dependency-free hook for observing request lifecycle without taking a dependency on a logging framework. It is invoked for each `EetDiagnosticEvent` raised while sending, resending, or testing the connection:

- `Sending` - a message is about to be sent to the EET service.
- `ResponseReceived` - a message was acknowledged or rejected at the EET protocol level.
- `ResendScheduled` - a temporary error (code `-1`) caused an automatic resend to be scheduled.
- `Failed` - the operation failed with a transport, protocol, or signing exception; `EetDiagnosticEvent.Exception` carries the failure.

```csharp
options.OnDiagnosticEvent = e => logger.LogInformation("[{Kind}] {MessageId}: {Message}", e.Kind, e.MessageId, e.Message);
```

## Verifying configuration

Two complementary checks help catch configuration problems before you submit real sales:

- `EetClientOptions.Validate()` performs local, offline checks (no network call): the endpoint is an absolute HTTPS URL, a signing certificate is configured and loadable with a private key and is currently valid, any pinned authority certificate paths exist, resend settings are consistent, and `HttpConnectionLifetime` is positive. It throws `EetValidationException` listing every problem found.
- `EetClient.TestConnectionAsync()` performs an actual network round trip: it sends a signed verification-mode message (`RegisteredSale.VerificationMode = true`, which the EET service never registers) to the configured endpoint and returns an `EetConnectionTestResult`. This exercises TLS/certificate trust and endpoint reachability, in addition to signing. Both a successful acknowledgement and an EET-level rejection count as success, since either means the request reached the service and was processed; only transport, protocol, or signing failures are reported as unsuccessful. `TestConnectionAsync` never throws for these expected failure modes - including unexpected HTTP or transport-level errors - it always returns an `EetConnectionTestResult` describing the outcome (unless the supplied `CancellationToken` is cancelled).

`EetClientOptions.RevocationMode` controls how the authority certificate chain is checked for revocation during acknowledgement signature validation (defaults to `X509RevocationMode.Online`, matching the .NET default). Set it to `X509RevocationMode.NoCheck` in offline/air-gapped environments where CRL/OCSP endpoints are unreachable, or `X509RevocationMode.Offline` to rely on a locally cached CRL.

```csharp
var options = new EetClientOptions
{
    BaseAddress = "https://pg.trzbyeet.gov.cz:443/eet/services/EETServiceSOAP/v4",
    SigningCertificatePath = @"C:\certs\playground.p12",
    SigningCertificatePassword = "changeit",
    UseSystemCertificateTrust = true
};

// Fails fast on obvious misconfiguration without any network access.
options.Validate();

using var client = new EetClient(options);

// Exercises the real endpoint, TLS trust, and certificate signing.
var testResult = await client.TestConnectionAsync();
if (!testResult.IsSuccess)
{
    Console.Error.WriteLine($"EET connection test failed: {testResult.Message}");
}
```

## Building locally

```bash
dotnet restore src/Selpo.EET2.0/Selpo.EET2.0.csproj
dotnet build src/Selpo.EET2.0/Selpo.EET2.0.csproj --configuration Release
dotnet pack src/Selpo.EET2.0/Selpo.EET2.0.csproj --configuration Release
```

## Testing

The unit test suite (`tests/Selpo.EET2.0.Tests`) covers serialization, XML signing, SOAP envelope construction, response parsing, resend orchestration, transport error handling, and configuration validation (`EetClientOptions.Validate()`, `EetClient.TestConnectionAsync()`) using stubbed HTTP transport - no network access or credentials are required.

`PlaygroundIntegrationTests` additionally exercises the real EET playground endpoint (`pg.trzbyeet.gov.cz`) end-to-end: submitting a verification-mode sale, running `EetClientOptions.Validate()`, and running `EetClient.TestConnectionAsync()` against it. These tests are opt-in and no-op unless the following environment variables are set, so they never run unintentionally in CI or on a developer machine without playground credentials:

| Variable | Required | Description |
| --- | --- | --- |
| `EET_RUN_PLAYGROUND` | Yes | Must be `true` to enable the playground tests. |
| `EET_PLAYGROUND_CERTIFICATE_PATH` | Yes | Path to a `.p12`/`.pfx` playground signing certificate. |
| `EET_PLAYGROUND_CERTIFICATE_PASSWORD` | Yes | Password for the signing certificate. |
| `EET_PLAYGROUND_ROOT_CERTIFICATE_PATH` | No | Path to a pinned playground root certificate; omit to use the OS trust store. |
| `EET_PLAYGROUND_INTERMEDIATE_CERTIFICATE_PATH` | No | Path to a pinned playground intermediate certificate. |

```bash
$env:EET_RUN_PLAYGROUND = "true"
$env:EET_PLAYGROUND_CERTIFICATE_PATH = "C:\certs\playground.p12"
$env:EET_PLAYGROUND_CERTIFICATE_PASSWORD = "changeit"
dotnet test tests/Selpo.EET2.0.Tests/Selpo.EET2.0.Tests.csproj --filter FullyQualifiedName~PlaygroundIntegrationTests
```

## CI/CD and releasing

GitHub Actions workflows build, test, and publish the package:

- **[`.github/workflows/ci.yml`](../../.github/workflows/ci.yml)** runs on every push and pull request to `main`: restores, builds, and tests the solution across both target frameworks, and packs the NuGet package (without publishing it) to validate that packing succeeds.
- **[`.github/workflows/release.yml`](../../.github/workflows/release.yml)** publishes a new NuGet package to [NuGet.org](https://www.nuget.org/packages/Selpo.EET2.0) whenever a tag matching `v*.*.*` (e.g. `v0.1.0`) is pushed, or when run manually via `workflow_dispatch` with an explicit version. It builds and tests the solution, packs `Selpo.EET2.0.csproj` with the tag's version, uploads the `.nupkg` as a build artifact, and pushes it to NuGet.org.

To publish a release:

1. Update `VersionPrefix` in [`Selpo.EET2.0.csproj`](Selpo.EET2.0.csproj).
2. Tag the commit, e.g. `git tag v0.1.0 && git push origin v0.1.0`.
3. The `release` workflow builds, tests, packs, and pushes the package to NuGet.org automatically.

The release workflow requires a repository secret named `NUGET_API_KEY` containing a NuGet.org API key with push permissions for the `Selpo.EET2.0` package (create one under nuget.org → API Keys, scoped to this package, and add it under the repository's **Settings → Secrets and variables → Actions**).

## Contributing

Contributions are welcome via fork and pull request. See [CONTRIBUTING.md](../../CONTRIBUTING.md) for the workflow, coding conventions, and how to report issues.

## License

This project is licensed under the MIT License. See [LICENSE](https://github.com/HightowerCZ/Selpo.EET2.0/blob/main/LICENSE).
