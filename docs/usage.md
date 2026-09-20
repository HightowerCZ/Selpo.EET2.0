# Selpo.EET2.0 Usage and API Reference

**[English](#english) | [Cestina](#cestina)**

<a id="english"></a>
## English

This guide explains the public API of `Selpo.EET2.0`. The EET interface sends SOAP 1.1 messages over HTTPS and signs registered-sale messages with WS-Security. The production endpoint records sales; the playground endpoint is for development only.

### Basic usage

```csharp
using Selpo.Eet20;

var options = new EetClientOptions
{
    BaseAddress = "https://pg.trzbyeet.gov.cz:443/eet/services/EETServiceSOAP/v4",
    SigningCertificatePath = @"C:\certs\playground.p12",
    SigningCertificatePassword = "changeit",
    UseSystemCertificateTrust = true
};

options.Validate();

using var client = new EetClient(options);
var response = await client.RegisterSaleAsync(new RegisteredSale
{
    Eic = "CZ8551015704",
    UnitId = 181,
    PosId = "00/2535/CN58",
    TransactionNumber = "2024-0001",
    SubmissionTime = DateTimeOffset.Now,
    TransactionTime = DateTimeOffset.Now,
    TotalAmount = 236.00m,
    FirstSubmission = true,
    VerificationMode = false
});

switch (response)
{
    case EetAcknowledgementResponse acknowledgement:
        Console.WriteLine($"Accepted: {acknowledgement.Pok}");
        break;
    case EetErrorResponse error:
        Console.WriteLine($"EET error {error.ErrorCode}: {error.ErrorMessage}");
        break;
}
```

`RegisterSaleAsync` returns either `EetAcknowledgementResponse` or `EetErrorResponse`. A successful production acknowledgement contains a valid POK. A playground acknowledgement contains a test POK ending in `-ff` and is not a legal production acknowledgement.

### EetClient

`EetClient(EetClientOptions)` creates and owns an `HttpClient`. `EetClient(HttpClient)` uses an application-supplied client but cannot register sales because no EET options are available. Use `EetClient(HttpClient, EetClientOptions)` when the application manages the HTTP client and the connector still needs EET options. The public `HttpClient` property exposes the client used by the connector. The client disposes an HTTP client only when it created that client itself.

- `RegisterSaleAsync(sale, cancellationToken)` signs and sends one sale. It may automatically resend error code `-1` when enabled.
- `TestConnectionAsync(cancellationToken)` sends a signed verification-mode probe. It never records a sale and returns an `EetConnectionTestResult` for expected connection, protocol, signing, and validation failures.
- `Dispose()` disposes the internally created HTTP client.

### EetClientOptions properties

| Property | Meaning and usage |
| --- | --- |
| `BaseAddress` | Absolute HTTPS URL of the EET service. Use the production URL for real reporting and the playground URL for development. |
| `SigningCertificate` | An `X509Certificate2` containing the private key used to sign the SOAP message. |
| `SigningCertificatePath` | Path to a `.p12` or `.pfx` signing certificate. Use this or `SigningCertificate`. |
| `SigningCertificatePassword` | Password used when loading `SigningCertificatePath`. |
| `UseSystemCertificateTrust` | When `true` (default), validate the authority certificate chain through the operating-system trust store. |
| `PinnedAuthorityCertificate` | Optional authority certificate pinned directly in memory. Use when trust must be restricted to a known certificate. |
| `AuthorityRootCertificate` | Optional authority root certificate supplied in memory for chain validation. |
| `AuthorityRootCertificatePath` | Path to the authority root certificate. |
| `AuthorityIntermediateCertificate` | Optional authority intermediate/subordinate certificate supplied in memory. |
| `AuthorityIntermediateCertificatePath` | Path to the authority intermediate certificate. |
| `RevocationMode` | Certificate revocation behavior during acknowledgement-signature validation. Default is `X509RevocationMode.Online`; use `Offline` for cached revocation data or `NoCheck` only when the environment cannot perform revocation checks. |
| `EnableAutomaticResend` | Enables resending only for EET error code `-1`, the temporary technical-processing error. Default is `false`. |
| `ResendDelays` | Explicit delay before each resend. Three entries allow up to three resends after the initial attempt. Required and non-negative when automatic resend is enabled. |
| `OnResendAttempt` | Callback invoked after each temporary-error attempt, including the final exhausted attempt. Use it for logging or alerting. |
| `HttpConnectionLifetime` | Lifetime of pooled connections when the client creates its own HTTP client. Default is five minutes, allowing DNS-based endpoint changes to be picked up. Must be positive. It does not configure an application-supplied `HttpClient`. |
| `OnDiagnosticEvent` | Optional callback for request lifecycle events. It is independent of exceptions and resend callbacks. |

Call `Validate()` before network communication. It checks the HTTPS endpoint, signing certificate, certificate paths, trust configuration, resend settings, and connection lifetime. It throws `EetValidationException` containing all detected problems.

Certificate selection is exclusive in practice: configure `SigningCertificate` or `SigningCertificatePath`. When system trust is disabled, configure `PinnedAuthorityCertificate`, `AuthorityRootCertificate`, or `AuthorityRootCertificatePath`.

### RegisteredSale properties

`RegisteredSale` represents one EET data message. `MessageId` identifies the message submission, not the sale. A resend receives a new UUID while the sale identity remains the same.

| Property | Meaning and constraints |
| --- | --- |
| `MessageId` | UUID of this submission attempt. Defaults to a new UUID and must not be empty. |
| `SubmissionTime` | Time when this message is sent, including its UTC offset. Defaults to the current local time. |
| `FirstSubmission` | `true` for the first submission of a sale; `false` for a repeat. Defaults to `true`. |
| `VerificationMode` | `true` validates connectivity and message processing without registering the sale. Defaults to `false`. |
| `Eic` | Mandatory taxpayer identification number. Format: `CZ` followed by 8 to 10 digits. |
| `AuthorizingEic` | Optional identification number of the taxpayer on whose behalf the sale is reported. |
| `MultipleTaxpayerAuthorization` | Optional flag indicating that the sale is reported for multiple taxpayers. |
| `UnitId` | Mandatory registering-unit identifier assigned through DIS+. Range: 1 to 999,999,999. |
| `PosId` | Mandatory taxpayer point-of-sale identifier. Allowed characters are ASCII letters, digits, spaces, and `. , : ; / # - _`; maximum 20 characters. |
| `TransactionNumber` | Mandatory taxpayer-created receipt or transaction sequence number. Same allowed character set as `PosId`; maximum 25 characters. |
| `TransactionTime` | Mandatory time of the sale, including its UTC offset. |
| `TotalAmount` | Mandatory total sale amount in CZK, with exactly two decimal places and absolute value below 100,000,000. |
| `IntendedSettlementAmount` | Optional CZK amount intended for later drawing or settlement, with exactly two decimals. |
| `SettledAmount` | Optional CZK amount subsequently drawn or settled, with exactly two decimals. |

The EET service identifies a sale using `Eic`, `UnitId`, `PosId`, `TransactionNumber`, `TransactionTime`, and `TotalAmount`. `MessageId` is not part of that sale identity.

### Responses and warnings

`EetResponse.GlobalTransactionId` is the HTTP tracing identifier supplied by the authority. Store it with failures and support diagnostics.

`EetAcknowledgementResponse` properties:

- `MessageId`: UUID of the submitted message, when returned.
- `ReceivedAt`: authority receipt time, when returned.
- `Pok`: acknowledgement code. In production it is the valid POK; in the playground it is a test value ending in `-ff`.
- `IsTestEnvironment`: `true` when the response came from the playground.
- `Warnings`: non-critical checks that did not prevent acceptance.

`EetErrorResponse` properties:

- `MessageId`: UUID of the rejected or verified message, when returned.
- `RejectedAt`: authority processing/rejection time, when returned.
- `ErrorCode`: EET error code. Code `0` means a verification-mode message was processed successfully; code `-1` means retry later.
- `ErrorMessage`: authority error text.
- `IsTestEnvironment`: `true` for a playground response.
- `Warnings`: non-critical warnings, mainly on verification responses with code `0`.

Each `EetWarning` has a numeric `Code` and authority-provided `Message`. Warnings do not by themselves mean that a production sale was rejected.

### Verification and connection testing

Use `VerificationMode = true` for an explicit probe. The EET specification says that verification mode does not fulfill the reporting obligation and does not register the sale. `TestConnectionAsync()` creates such a probe automatically.

`EetConnectionTestResult` contains:

- `IsSuccess`: `true` when a signed request reached the service and produced an EET response, including an EET-level rejection.
- `Message`: human-readable result summary.
- `Response`: received `EetResponse`, when the request reached the service.
- `Exception`: underlying failure, when the test could not complete successfully.

Cancellation requested through the `CancellationToken` is propagated rather than converted into a failed result.

### Automatic resend

Automatic resend applies only to EET error code `-1`. The configured delay list controls the number of resends. Each resend creates a new `MessageId`, sets `FirstSubmission` to `false`, and preserves the sale fields. Transport, protocol, signing, and validation exceptions are not retried.

`EetResendAttempt` contains:

- `AttemptNumber`: one-based attempt that produced `Response`.
- `MaxAttempts`: initial submission plus configured resend entries.
- `Delay`: delay used before the attempt; zero on the initial/final exhausted notification.
- `Response`: response that triggered the callback.
- `IsFinalAttempt`: no further automatic resend will occur when `true`.

### Diagnostics

`OnDiagnosticEvent` can observe:

- `Sending`: a message is about to be sent.
- `ResponseReceived`: an EET response was parsed.
- `ResendScheduled`: error `-1` scheduled another attempt.
- `Failed`: transport, protocol, signing, or another send failure occurred.

`EetDiagnosticEvent` provides `Kind`, a human-readable `Message`, the related `MessageId`, and an `Exception` for failure events.

### Exceptions

- `EetValidationException`: local input or configuration is invalid.
- `EetProtocolException`: the response is not a valid EET/SOAP protocol response or contains a SOAP fault.
- `EetTransportException`: the HTTP request returned a non-success status. Its `StatusCode`, raw `ResponseBody`, and optional `GlobalTransactionId` are available for troubleshooting.

### Protocol reference

The authoritative contract is in [`docs/protocol`](protocol), especially `EETServiceSOAP.wsdl` and `EETXMLSchema.xsd`. The Czech specification is legally authoritative; the attached English text is for orientation.

---

<a id="cestina"></a>
## Cestina

Tato prirucka vysvetluje verejne API balicku `Selpo.EET2.0`. Rozhrani EET posila zpravy SOAP 1.1 pres HTTPS a zpravy o evidovane trzbe podepisuje pomoci WS-Security. Produkcni endpoint trzby eviduje; playground slouzi pouze pro vyvoj.

### Zakladni pouziti

```csharp
using Selpo.Eet20;

var options = new EetClientOptions
{
    BaseAddress = "https://pg.trzbyeet.gov.cz:443/eet/services/EETServiceSOAP/v4",
    SigningCertificatePath = @"C:\certs\playground.p12",
    SigningCertificatePassword = "changeit",
    UseSystemCertificateTrust = true
};

options.Validate();

using var client = new EetClient(options);
var response = await client.RegisterSaleAsync(new RegisteredSale
{
    Eic = "CZ8551015704",
    UnitId = 181,
    PosId = "00/2535/CN58",
    TransactionNumber = "2024-0001",
    SubmissionTime = DateTimeOffset.Now,
    TransactionTime = DateTimeOffset.Now,
    TotalAmount = 236.00m,
    FirstSubmission = true,
    VerificationMode = false
});

switch (response)
{
    case EetAcknowledgementResponse acknowledgement:
        Console.WriteLine($"Prijato: {acknowledgement.Pok}");
        break;
    case EetErrorResponse error:
        Console.WriteLine($"Chyba EET {error.ErrorCode}: {error.ErrorMessage}");
        break;
}
```

`RegisterSaleAsync` vraci `EetAcknowledgementResponse` nebo `EetErrorResponse`. Uspezne potvrzeni z produkce obsahuje platny POK. Potvrzeni z playgroundu obsahuje testovaci POK koncici na `-ff` a neni platnym produkcnim potvrzenim.

### EetClient

`EetClient(EetClientOptions)` vytvari a vlastni `HttpClient`. `EetClient(HttpClient)` pouziva HTTP klienta aplikace, ale bez nastaveni EET neumozni registraci trzby. `EetClient(HttpClient, EetClientOptions)` pouzijte, kdyz HTTP klienta spravuje aplikace a konektor stale potrebuje nastaveni EET. Verejna vlastnost `HttpClient` zpristupnuje klienta pouzivaneho konektorem. Klient uvolni HTTP klienta pouze tehdy, kdyz ho vytvoril sam.

- `RegisterSaleAsync(sale, cancellationToken)` podepise a odesle jednu trzbu. Pri povoleni muze automaticky opakovat chybu `-1`.
- `TestConnectionAsync(cancellationToken)` odesle podepsany dotaz v overovacim modu. Trzbu nezaeviduje a vraci `EetConnectionTestResult` pro ocekavane chyby spojeni, protokolu, podpisu a validace.
- `Dispose()` uvolni interne vytvoreneho HTTP klienta.

### Vlastnosti EetClientOptions

| Vlastnost | Vyznam a pouziti |
| --- | --- |
| `BaseAddress` | Absolutni HTTPS adresa sluzby EET. Pro skutecne vykazovani pouzijte produkci, pro vyvoj playground. |
| `SigningCertificate` | `X509Certificate2` s privatnim klicem pro podpis SOAP zpravy. |
| `SigningCertificatePath` | Cesta k podpisovemu certifikatu `.p12` nebo `.pfx`. Pouzijte tuto vlastnost nebo `SigningCertificate`. |
| `SigningCertificatePassword` | Heslo pro nacteni certifikatu z `SigningCertificatePath`. |
| `UseSystemCertificateTrust` | Pri `true` (vychozi) se retezec certifikatu autority overuje v systemovem ulozisti duveryhodnych certifikatu. |
| `PinnedAuthorityCertificate` | Volitelny certifikat autority pripnuty primo v pameti. |
| `AuthorityRootCertificate` | Volitelny korenovy certifikat autority dodany v pameti. |
| `AuthorityRootCertificatePath` | Cesta ke korenovemu certifikatu autority. |
| `AuthorityIntermediateCertificate` | Volitelny mezilehly certifikat autority dodany v pameti. |
| `AuthorityIntermediateCertificatePath` | Cesta k mezilehlemu certifikatu autority. |
| `RevocationMode` | Zpusob kontroly odvolani certifikatu pri overovani podpisu potvrzeni. Vychozi je `X509RevocationMode.Online`; `Offline` pouziva lokalni data a `NoCheck` kontroly vypne. |
| `EnableAutomaticResend` | Povoli opakovani pouze pri chybe EET `-1`, tedy docasne technicke chybe. Vychozi je `false`. |
| `ResendDelays` | Explicitni prodlevy pred opakovanim. Tri polozky umozni az tri opakovani po prvnim pokusu. Pri zapnutem opakovani jsou povinne a nesmi byt zaporne. |
| `OnResendAttempt` | Callback volany po kazdem pokusu pri docasne chybe, vcetne posledniho vycerpaneho pokusu. Slouzi k logovani a upozorneni. |
| `HttpConnectionLifetime` | Doba zivota poolovanych spojeni, pokud klient vytvari vlastni HTTP klient. Vychozi je pet minut. Musi byt kladna. U vlastniho `HttpClient` se nenastavuje touto vlastnosti. |
| `OnDiagnosticEvent` | Volitelny callback pro udalosti zivotniho cyklu pozadavku. Je nezavisly na vyjimkach a callbacku opakovani. |

Pred sitovou komunikaci zavolejte `Validate()`. Kontroluje HTTPS endpoint, podpisovy certifikat, cesty k certifikatum, nastaveni duvery, opakovani a zivotnost spojeni. Pri chybach vyhodi `EetValidationException` se seznamem problemu.

V praxi nastavte `SigningCertificate` nebo `SigningCertificatePath`. Pokud vypnete systemovou duveru, nastavte `PinnedAuthorityCertificate`, `AuthorityRootCertificate` nebo `AuthorityRootCertificatePath`.

### Vlastnosti RegisteredSale

`RegisteredSale` predstavuje jednu datovou zpravu EET. `MessageId` identifikuje zpravu, nikoli trzbu. Pri opakovani se vytvari nove UUID, ale identita trzby zustava stejna.

| Vlastnost | Vyklad a omezeni |
| --- | --- |
| `MessageId` | UUID tohoto pokusu o odeslani. Vychozi je nove UUID a nesmi byt prazdne. |
| `SubmissionTime` | Cas odeslani zpravy vcetne casoveho posunu. Vychozi je aktualni lokalni cas. |
| `FirstSubmission` | `true` pri prvnim odeslani trzby, `false` pri opakovani. Vychozi je `true`. |
| `VerificationMode` | `true` overi spojeni a zpracovatelnost bez evidence trzby. Vychozi je `false`. |
| `Eic` | Povinne identifikacni cislo poplatnika. Format `CZ` a 8 az 10 cislic. |
| `AuthorizingEic` | Volitelne identifikacni cislo poplatnika, za ktereho je trzba evidovana. |
| `MultipleTaxpayerAuthorization` | Volitelny priznak evidence za vice poplatniku. |
| `UnitId` | Povinne oznaceni evidencni jednotky z DIS+. Rozsah 1 az 999 999 999. |
| `PosId` | Povinne oznaceni pokladniho zarizeni. ASCII pismena, cislice, mezera a znaky `. , : ; / # - _`, nejvyse 20 znaku. |
| `TransactionNumber` | Povinne poradove cislo trzby nebo cislo uctenky. Stejna sada znaku jako u `PosId`, nejvyse 25 znaku. |
| `TransactionTime` | Povinny cas uskutecneni trzby vcetne casoveho posunu. |
| `TotalAmount` | Povinna celkova castka v CZK, presne dve desetinna mista, absolutni hodnota mensi nez 100 000 000. |
| `IntendedSettlementAmount` | Volitelna castka v CZK urcena k pozdejsimu cerpani nebo zuctovani, presne dve desetinna mista. |
| `SettledAmount` | Volitelna castka v CZK nasledne cerpana nebo zuctovana, presne dve desetinna mista. |

Sluzba EET urcuje identitu trzby pomoci `Eic`, `UnitId`, `PosId`, `TransactionNumber`, `TransactionTime` a `TotalAmount`. `MessageId` do identity trzby nevstupuje.

### Odpovedi a varovani

`EetResponse.GlobalTransactionId` je identifikator pro trasovani, ktery muze byt dodan financni spravou. Ukladejte ho spolu s chybami a diagnostikou.

Vlastnosti `EetAcknowledgementResponse`:

- `MessageId`: UUID odeslane zpravy, pokud ho odpoved obsahuje.
- `ReceivedAt`: cas prijeti na strane autority, pokud ho odpoved obsahuje.
- `Pok`: potvrzovaci kod. V produkci platny POK, v playgroundu testovaci hodnota koncici na `-ff`.
- `IsTestEnvironment`: `true`, pokud odpoved prisla z playgroundu.
- `Warnings`: propustna varovani, ktera nezabranila prijeti.

Vlastnosti `EetErrorResponse`:

- `MessageId`: UUID odmitnute nebo overovane zpravy, pokud ho odpoved obsahuje.
- `RejectedAt`: cas zpracovani nebo odmitnuti, pokud ho odpoved obsahuje.
- `ErrorCode`: kod chyby EET. Kod `0` znamena uspesne zpracovani overovaciho modu; kod `-1` znamena opakovat pozdeji.
- `ErrorMessage`: text chyby autority.
- `IsTestEnvironment`: `true` pro odpoved z playgroundu.
- `Warnings`: propustna varovani, zejmena u overovaci odpovedi s kodem `0`.

Kazde `EetWarning` obsahuje ciselny `Code` a text autority `Message`. Samotne varovani neznamena, ze produkcni trzba byla odmitnuta.

### Overeni a test spojeni

Pro vlastni overovaci pozadavek nastavte `VerificationMode = true`. Podle specifikace overovaci rezim neplni evidencni povinnost a trzbu neeviduje. `TestConnectionAsync()` takovy pozadavek vytvori automaticky.

`EetConnectionTestResult` obsahuje:

- `IsSuccess`: `true`, pokud podepsany pozadavek dorazil ke sluzbe a dostal odpoved EET, vcetne odmitnuti na urovni EET.
- `Message`: strucne lidske vysvetleni vysledku.
- `Response`: prijata odpoved `EetResponse`, pokud pozadavek dorazil ke sluzbe.
- `Exception`: puvodni vyjimka pri neuspesnem testu.

Zruseni pomoci `CancellationToken` se propaguje jako zruseni, ne jako neuspesny vysledek.

### Automaticke opakovani

Automaticke opakovani se tyka pouze chyby EET `-1`. Seznam prodlev urcuje pocet opakovani. Kazde opakovani vytvori nove `MessageId`, nastavi `FirstSubmission` na `false` a zachova udaje trzby. Transportni, protokolove, podpisove a validacni vyjimky se neopakuji.

`EetResendAttempt` obsahuje:

- `AttemptNumber`: poradi pokusu, ktery vytvoril `Response`, od jedne.
- `MaxAttempts`: prvni odeslani plus pocet nastavenych opakovani.
- `Delay`: prodleva pouzita pred pokusem; nula pri prvnim a poslednim vycerpanem hlaseni.
- `Response`: odpoved, ktera callback vyvolala.
- `IsFinalAttempt`: pri `true` uz dalsi automaticke opakovani neprobehne.

### Diagnostika

`OnDiagnosticEvent` muze sledovat:

- `Sending`: zprava se chysta k odeslani.
- `ResponseReceived`: odpoved EET byla zpracovana.
- `ResendScheduled`: chyba `-1` naplanovala dalsi pokus.
- `Failed`: nastala transportni, protokolova, podpisova nebo jina chyba odeslani.

`EetDiagnosticEvent` poskytuje `Kind`, lidsky citelny `Message`, souvisejici `MessageId` a u udalosti selhani `Exception`.

### Vyjimky

- `EetValidationException`: lokalni vstup nebo konfigurace nejsou platne.
- `EetProtocolException`: odpoved neni platnou odpovedi EET/SOAP nebo obsahuje SOAP fault.
- `EetTransportException`: HTTP pozadavek vratil neuspesny stav. K dispozici jsou `StatusCode`, surove telo `ResponseBody` a pripadny `GlobalTransactionId`.

### Odkaz na protokol

Autoritativni kontrakt je ve slozce [`docs/protocol`](protocol), zejmena v souborech `EETServiceSOAP.wsdl` a `EETXMLSchema.xsd`. Pravni zavazna je ceska specifikace; prilozeny anglicky text slouzi pouze k orientaci.
