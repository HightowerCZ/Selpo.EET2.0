# Selpo.EET2.0

**🇬🇧 [English](#english) | 🇨🇿 [Čeština](#čeština)**

<a id="english"></a>
## English

`Selpo.EET2.0` is a .NET connector for integrating applications with the Czech Ministry of Finance EET 2.0 service.

The package targets .NET Standard 2.0, .NET Framework 4.8.1, and .NET 10.

### Target framework

- .NET Standard 2.0 (`netstandard2.0`), compatible with .NET Framework 4.6.1+ and .NET Core 2.0+
- .NET Framework 4.8.1 (`net481`)
- .NET 10 (`net10.0`)

### Installation

```bash
dotnet add package Selpo.EET2.0
```

### Example

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

The full WSDL/XSD contract for both environments is available under [`docs/protocol`](https://github.com/HightowerCZ/Selpo.EET2.0/tree/main/docs/protocol) (`EETServiceSOAP.wsdl`, `EETXMLSchema.xsd`).
- See the bilingual [usage and API reference](docs/usage.md) for all public properties, response types, callbacks, and exceptions.
- `SigningCertificate`/`SigningCertificatePath` provide the certificate used to sign the message; `AuthorityRootCertificatePath`/`AuthorityIntermediateCertificatePath` (or their `X509Certificate2` equivalents) can be used to pin the authority's certificates instead of relying on `UseSystemCertificateTrust`.
- `RegisterSaleAsync` throws `EetValidationException` for invalid input, `EetProtocolException` for SOAP fault responses, and `EetTransportException` for network-level failures.

#### Production DNS and long-running connections

The production EET endpoint is served behind a DNS-based high-availability setup, and its DNS record can change over time (e.g. during Ministry of Finance infrastructure maintenance). When `EetClient(EetClientOptions)` creates its own `HttpClient`, it bounds pooled connection lifetime using `EetClientOptions.HttpConnectionLifetime` (defaults to 5 minutes) so long-running services periodically re-resolve DNS instead of pinning a stale address. If you supply your own `HttpClient` via `EetClient(HttpClient, EetClientOptions)`, configure a similar connection lifetime yourself (for example `SocketsHttpHandler.PooledConnectionLifetime` on .NET, or `ServicePoint.ConnectionLeaseTimeout`/`ServicePointManager.DnsRefreshTimeout` on .NET Framework).

### Automatic resend on temporary errors

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

### Diagnostics

`EetClientOptions.OnDiagnosticEvent`

- `Sending` - a message is about to be sent to the EET service.
- `ResponseReceived` - a message was acknowledged or rejected at the EET protocol level.
- `ResendScheduled` - a temporary error (code `-1`) caused an automatic resend to be scheduled.
- `Failed` - the operation failed with a transport, protocol, or signing exception; `EetDiagnosticEvent.Exception` carries the failure.

```csharp
options.OnDiagnosticEvent = e => logger.LogInformation("[{Kind}] {MessageId}: {Message}", e.Kind, e.MessageId, e.Message);
```

### Verifying configuration

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

### Building locally

```bash
dotnet restore src/Selpo.EET2.0/Selpo.EET2.0.csproj
dotnet build src/Selpo.EET2.0/Selpo.EET2.0.csproj --configuration Release
dotnet pack src/Selpo.EET2.0/Selpo.EET2.0.csproj --configuration Release
```

### Testing

The unit test suite (`tests/Selpo.EET2.0.Tests`) covers serialization, XML signing, SOAP envelope construction, response parsing, resend orchestration, transport error handling, and configuration validation (`EetClientOptions.Validate()`, `EetClient.TestConnectionAsync()`) using a simulated HTTP transport - no network access or credentials required.

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

### Known limitations

- Only the EET 2.0 `RegisterSale`/acknowledgement flow described by the [WSDL/XSD contract](https://github.com/HightowerCZ/Selpo.EET2.0/tree/main/docs/protocol) is implemented; no other Ministry of Finance web services are covered.
- The library targets `netstandard2.0`, `net481`, and `net10.0`; other target frameworks are not currently supported (see [CONTRIBUTING.md](https://github.com/HightowerCZ/Selpo.EET2.0/blob/main/CONTRIBUTING.md) if you need to propose adding one).
- Automatic resend only covers EET-level temporary errors (error code `-1`); transport, protocol, and signing failures are never retried automatically (see [Automatic resend on temporary errors](#automatic-resend-on-temporary-errors)).

### Versioning and support

This project follows [Semantic Versioning](https://semver.org/). Releases are published from tagged commits on `main`; see the [GitHub Releases](https://github.com/HightowerCZ/Selpo.EET2.0/releases) page for the changelog of each version. As a `0.x` package, breaking changes may still occur in minor versions until `1.0.0`; check release notes before upgrading.

### Development note

Parts of this project were developed with assistance from AI tools. All generated suggestions were reviewed, adapted, and validated by the project maintainer.

### Contributing

Contributions are welcome - see [CONTRIBUTING.md](https://github.com/HightowerCZ/Selpo.EET2.0/blob/main/CONTRIBUTING.md) for the workflow and coding conventions.

### Reporting issues

Found a bug or have a feature request? Please [open a GitHub issue](https://github.com/HightowerCZ/Selpo.EET2.0/issues). For security-sensitive reports (e.g. certificate handling or signing), do not open a public issue - see [CONTRIBUTING.md](https://github.com/HightowerCZ/Selpo.EET2.0/blob/main/CONTRIBUTING.md#security) for how to contact the maintainer directly.

### License

This project is licensed under the MIT License. See [LICENSE](https://github.com/HightowerCZ/Selpo.EET2.0/blob/main/LICENSE).

---

<a id="čeština"></a>
## Čeština

`Selpo.EET2.0` je .NET konektor pro integraci aplikací se službou EET 2.0 (Elektronická evidence tržeb) Ministerstva financí ČR.

Balíček cílí na .NET Standard 2.0, .NET Framework 4.8.1 a .NET 10.


### Cílová platforma

- .NET Standard 2.0 (`netstandard2.0`), kompatibilní s .NET Framework 4.6.1+ a .NET Core 2.0+
- .NET Framework 4.8.1 (`net481`)
- .NET 10 (`net10.0`)

### Instalace

```bash
dotnet add package Selpo.EET2.0
```

### Příklad použití

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
        Console.WriteLine($"Přijato. FIK/POK: {ack.Pok}, přijato v: {ack.ReceivedAt}");
        break;
    case EetErrorResponse error:
        Console.WriteLine($"Zamítnuto. Chyba {error.ErrorCode}: {error.ErrorMessage}");
        break;
}
```

Kompletní WSDL/XSD kontrakt pro obě prostředí je dostupný ve složce [`docs/protocol`](https://github.com/HightowerCZ/Selpo.EET2.0/tree/main/docs/protocol) (`EETServiceSOAP.wsdl`, `EETXMLSchema.xsd`).
- Podrobný dvojjazyčný [popis použití a API](docs/usage.md) vysvětluje všechny veřejné vlastnosti, typy odpovědí, callbacky a výjimky.
- `SigningCertificate`/`SigningCertificatePath` slouží k zadání certifikátu, kterým se zpráva podepisuje; `AuthorityRootCertificatePath`/`AuthorityIntermediateCertificatePath` (nebo jejich ekvivalenty typu `X509Certificate2`) lze použít k připnutí certifikátů autority místo spoléhání se na `UseSystemCertificateTrust`.
- `RegisterSaleAsync` vyhazuje `EetValidationException` při neplatném vstupu, `EetProtocolException` při SOAP fault odpovědi a `EetTransportException` při chybách na úrovni síťového přenosu.

#### Produkční DNS a dlouhotrvající připojení

Produkční endpoint EET běží za DNS řízenou vysoce dostupnou infrastrukturou a jeho DNS záznam se může v čase měnit (např. při údržbě infrastruktury Ministerstva financí). Když `EetClient(EetClientOptions)` vytváří vlastní `HttpClient`, omezuje životnost poolovaných připojení pomocí `EetClientOptions.HttpConnectionLifetime` (výchozí hodnota 5 minut), aby dlouhoběžící služby pravidelně znovu přeřešily DNS místo toho, aby zůstaly připnuté ke staré adrese. Pokud dodáváte vlastní `HttpClient` přes `EetClient(HttpClient, EetClientOptions)`, nastavte si podobnou životnost připojení sami (např. `SocketsHttpHandler.PooledConnectionLifetime` na .NET, nebo `ServicePoint.ConnectionLeaseTimeout`/`ServicePointManager.DnsRefreshTimeout` na .NET Frameworku).

### Automatické opakované odeslání při dočasných chybách

Služba EET 2.0 může odpovědět chybovým kódem `-1` ("dočasná technická chyba zpracování - odešlete prosím datovou zprávu znovu později"). `EetClient` umí v takovém případě tržbu automaticky znovu odeslat:

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
            // Všechny pokusy o opakované odeslání byly vyčerpány a tržba stále není zaevidována.
            // Zde upozorněte operátora / alerting systém, aby bylo možné tržbu vyřešit manuálně.
            Console.Error.WriteLine($"Tržba stále nezaevidována po {attempt.AttemptNumber} pokusech.");
        }
        else
        {
            Console.WriteLine($"Pokus {attempt.AttemptNumber}/{attempt.MaxAttempts} selhal s dočasnou chybou, opakuji za {attempt.Delay}.");
        }
    }
});
```

- `EnableAutomaticResend` je ve výchozím stavu `false`; knihovna sama od sebe nikdy neodesílá opakovaně, pokud si to výslovně nevyžádáte.
- `ResendDelays` knihovna **neposkytuje** - harmonogram (prodlevu před každým opakovaným pokusem) musíte explicitně dodat vy. Počet položek omezuje maximální počet automatických opakování (např. tři prodlevy umožní až tři opakování po prvotním pokusu). Zapnutí `EnableAutomaticResend` bez `ResendDelays` vyvolá `InvalidOperationException`.
- Každé opakované odeslání používá nově vygenerované UUID zprávy a `FirstSubmission = false`, v souladu se specifikací EET 2.0 pro opakovaná podání.
- `OnResendAttempt` se volá po každém pokusu o opakované odeslání, včetně posledního, pokud je harmonogram vyčerpán a chyba přetrvává (`IsFinalAttempt == true`) - využijte to k logování nebo upozornění, aby trvale selhávající tržba nezůstala bez povšimnutí. `RegisterSaleAsync` v takovém případě volajícímu stále vrátí finální `EetErrorResponse`.
- Automatické opakování pokrývá pouze dočasné chyby na úrovni EET (chybový kód `-1`). Pokud samotný pokus o opakované odeslání selže s výjimkou na úrovni transportu, protokolu nebo podpisu (např. výpadek sítě nebo prošlý certifikát), tato výjimka se okamžitě propaguje z `RegisterSaleAsync` a harmonogram opakování se opouští - neopakuje se a `OnResendAttempt` se pro tento pokus nevolá.

### Diagnostika

`EetClientOptions.OnDiagnosticEvent` je odlehčený hook bez závislostí pro sledování životního cyklu požadavku, aniž byste museli zavádět závislost na logovacím frameworku.

- `Sending` - zpráva se chystá být odeslána (nebo znovu odeslána) službě EET.
- `ResponseReceived` - zpráva byla na úrovni protokolu EET úspěšně potvrzena nebo zamítnuta.
- `ResendScheduled` - dočasná chyba (kód `-1`) vyvolala naplánování automatického opakovaného odeslání.
- `Failed` - operace selhala s výjimkou na úrovni transportu, protokolu nebo podpisu; `EetDiagnosticEvent.Exception` obsahuje danou chybu.

```csharp
options.OnDiagnosticEvent = e => logger.LogInformation("[{Kind}] {MessageId}: {Message}", e.Kind, e.MessageId, e.Message);
```

### Ověření konfigurace

Dvě doplňující se kontroly pomáhají odhalit chyby konfigurace ještě před odesláním skutečných tržeb:

- `EetClientOptions.Validate()` provádí lokální, offline kontroly (bez síťového volání): endpoint je absolutní HTTPS URL, podpisový certifikát je nastaven, načtitelný s privátním klíčem a aktuálně platný, případné připnuté cesty k certifikátům autority existují, nastavení opakovaného odesílání je konzistentní a `HttpConnectionLifetime` je kladné. Vyhazuje `EetValidationException` se seznamem všech nalezených problémů.
- `EetClient.TestConnectionAsync()` provádí skutečný síťový round-trip: odešle podepsanou zprávu v ověřovacím režimu (`RegisteredSale.VerificationMode = true`, kterou služba EET nikdy nezaeviduje) na nakonfigurovaný endpoint a vrátí `EetConnectionTestResult`. Tím se kromě podepisování ověří i důvěryhodnost TLS/certifikátu a dostupnost endpointu. Jak úspěšné potvrzení, tak zamítnutí na úrovni EET se počítají jako úspěch, protože obojí znamená, že požadavek dorazil ke službě a byl zpracován; jako neúspěch se hlásí pouze chyby na úrovni transportu, protokolu nebo podpisu. `TestConnectionAsync` u těchto očekávaných způsobů selhání - včetně neočekávaných HTTP nebo transportních chyb - nikdy nevyhazuje výjimku a vždy vrátí `EetConnectionTestResult` popisující výsledek (pokud není zrušen předaný `CancellationToken`).

`EetClientOptions.RevocationMode` řídí, jak se při ověřování podpisu potvrzení kontroluje odvolání certifikátu v řetězu certifikační autority (výchozí hodnota `X509RevocationMode.Online`, stejně jako výchozí chování .NET). V offline/izolovaných prostředích, kde nejsou dostupné CRL/OCSP endpointy, nastavte `X509RevocationMode.NoCheck`, nebo `X509RevocationMode.Offline` pro spolehnutí se na lokálně uloženou CRL.

```csharp
var options = new EetClientOptions
{
    BaseAddress = "https://pg.trzbyeet.gov.cz:443/eet/services/EETServiceSOAP/v4",
    SigningCertificatePath = @"C:\certs\playground.p12",
    SigningCertificatePassword = "changeit",
    UseSystemCertificateTrust = true
};

// Rychlé selhání při zjevné chybné konfiguraci bez jakéhokoli síťového přístupu.
options.Validate();

using var client = new EetClient(options);

// Ověří skutečný endpoint, důvěryhodnost TLS a podepisování certifikátem.
var testResult = await client.TestConnectionAsync();
if (!testResult.IsSuccess)
{
    Console.Error.WriteLine($"Test připojení k EET selhal: {testResult.Message}");
}
```

### Lokální build

```bash
dotnet restore src/Selpo.EET2.0/Selpo.EET2.0.csproj
dotnet build src/Selpo.EET2.0/Selpo.EET2.0.csproj --configuration Release
dotnet pack src/Selpo.EET2.0/Selpo.EET2.0.csproj --configuration Release
```

### Testování

Sada jednotkových testů (`tests/Selpo.EET2.0.Tests`) pokrývá serializaci, XML podepisování, sestavení SOAP obálky, zpracování odpovědí, orchestraci opakovaného odesílání, zpracování chyb transportu a validaci konfigurace (`EetClientOptions.Validate()`, `EetClient.TestConnectionAsync()`) pomocí simulovaného HTTP transportu - není potřeba žádný síťový přístup ani přihlašovací údaje.

`PlaygroundIntegrationTests` navíc end-to-end ověřují skutečný playground endpoint EET (`pg.trzbyeet.gov.cz`): odeslání tržby v ověřovacím režimu, spuštění `EetClientOptions.Validate()` a spuštění `EetClient.TestConnectionAsync()` proti němu. Tyto testy jsou volitelné (opt-in) a bez nastavení níže uvedených proměnných prostředí se přeskočí, takže se nikdy nespustí neúmyslně v CI nebo na vývojářském stroji bez playground přihlašovacích údajů:

| Proměnná | Povinná | Popis |
| --- | --- | --- |
| `EET_RUN_PLAYGROUND` | Ano | Musí být `true` pro povolení playground testů. |
| `EET_PLAYGROUND_CERTIFICATE_PATH` | Ano | Cesta k `.p12`/`.pfx` playground podpisovému certifikátu. |
| `EET_PLAYGROUND_CERTIFICATE_PASSWORD` | Ano | Heslo k podpisovému certifikátu. |
| `EET_PLAYGROUND_ROOT_CERTIFICATE_PATH` | Ne | Cesta k připnutému kořenovému certifikátu playground; bez zadání se použije důvěryhodné úložiště OS. |
| `EET_PLAYGROUND_INTERMEDIATE_CERTIFICATE_PATH` | Ne | Cesta k připnutému mezilehlému certifikátu playground. |

```bash
$env:EET_RUN_PLAYGROUND = "true"
$env:EET_PLAYGROUND_CERTIFICATE_PATH = "C:\certs\playground.p12"
$env:EET_PLAYGROUND_CERTIFICATE_PASSWORD = "changeit"
dotnet test tests/Selpo.EET2.0.Tests/Selpo.EET2.0.Tests.csproj --filter FullyQualifiedName~PlaygroundIntegrationTests
```

### Známá omezení

- Implementován je pouze tok `RegisterSale`/potvrzení EET 2.0 popsaný [WSDL/XSD kontraktem](https://github.com/HightowerCZ/Selpo.EET2.0/tree/main/docs/protocol); ostatní webové služby Ministerstva financí nejsou pokryty.
- Knihovna cílí na `netstandard2.0`, `net481` a `net10.0`; jiné cílové platformy nejsou aktuálně podporovány (viz [CONTRIBUTING.md](https://github.com/HightowerCZ/Selpo.EET2.0/blob/main/CONTRIBUTING.md), pokud chcete navrhnout přidání další).
- Automatické opakování pokrývá pouze dočasné chyby na úrovni EET (chybový kód `-1`); chyby transportu, protokolu a podepisování se automaticky nikdy neopakují (viz [Automatické opakované odeslání při dočasných chybách](#automatické-opakované-odeslání-při-dočasných-chybách)).

### Verzování a podpora

Tento projekt dodržuje [sémantické verzování](https://semver.org/lang/cs/). Vydání se publikují z tagovaných commitů na `main`; changelog jednotlivých verzí najdete na stránce [GitHub Releases](https://github.com/HightowerCZ/Selpo.EET2.0/releases). Jako balíček `0.x` mohou i minoritní verze obsahovat nekompatibilní změny až do verze `1.0.0` - před aktualizací si vždy přečtěte poznámky k vydání.

### Poznámka k vývoji

Části tohoto projektu vznikly s pomocí AI nástrojů. Všechny vygenerované návrhy byly zkontrolovány, upraveny a ověřeny maintainerem projektu.

### Přispívání

Příspěvky jsou vítány formou forku a pull requestu. Postup, konvence psaní kódu a způsob nahlašování problémů najdete v [CONTRIBUTING.md](https://github.com/HightowerCZ/Selpo.EET2.0/blob/main/CONTRIBUTING.md).

### Nahlašování problémů

Našli jste chybu nebo máte návrh na novou funkci? [Založte prosím issue na GitHubu](https://github.com/HightowerCZ/Selpo.EET2.0/issues). Bezpečnostně citlivé nálezy (např. týkající se práce s certifikáty nebo podepisování) prosím nezveřejňujte jako veřejné issue - postup pro přímý kontakt s maintainerem najdete v [CONTRIBUTING.md](https://github.com/HightowerCZ/Selpo.EET2.0/blob/main/CONTRIBUTING.md#security).

### Licence

Tento projekt je licencován pod MIT licencí. Viz [LICENSE](https://github.com/HightowerCZ/Selpo.EET2.0/blob/main/LICENSE).

