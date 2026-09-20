using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Selpo.Eet20.Security;
using Selpo.Eet20.Serialization;
using Selpo.Eet20.Transport;

namespace Selpo.Eet20;

/// <summary>
/// Entry point for communication with EET 2.0 servers.
/// </summary>
public sealed class EetClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private readonly EetClientOptions? _options;

    /// <summary>
    /// Initializes a new instance using a managed <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="options">Client configuration.</param>
    public EetClient(EetClientOptions options)
        : this(CreateHttpClient(options), disposeHttpClient: true, options)
    {
    }

    /// <summary>
    /// Initializes a new instance using an existing <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">HTTP client configured by the application.</param>
    public EetClient(HttpClient httpClient)
        : this(httpClient, disposeHttpClient: false, options: null)
    {
    }

    /// <summary>Initializes a client using an application HTTP client and EET options.</summary>
    /// <param name="httpClient">HTTP client configured and owned by the application.</param>
    /// <param name="options">Client configuration.</param>
    public EetClient(HttpClient httpClient, EetClientOptions options)
        : this(httpClient, disposeHttpClient: false, options)
    {
    }

    private EetClient(HttpClient httpClient, bool disposeHttpClient, EetClientOptions? options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _disposeHttpClient = disposeHttpClient;
        _options = options;
    }

    /// <summary>
    /// Gets the underlying HTTP client used by the connector.
    /// </summary>
    public HttpClient HttpClient => _httpClient;

    /// <summary>
    /// Signs and submits one registered sale message. When <see cref="EetClientOptions.EnableAutomaticResend"/>
    /// is enabled, temporary technical errors (error code -1) are automatically resent according to
    /// <see cref="EetClientOptions.ResendDelays"/>, and <see cref="EetClientOptions.OnResendAttempt"/> is
    /// invoked after each attempt, including the final one if the resend schedule is exhausted.
    /// <para>
    /// Automatic resend only applies to EET-level temporary errors (error code -1). If a resend attempt
    /// itself fails with a transport, protocol, or signing exception (e.g. a network outage or an expired
    /// certificate), that exception propagates immediately and the resend schedule is abandoned - it is not
    /// treated as a retryable condition, and <see cref="EetClientOptions.OnResendAttempt"/> is not invoked for
    /// that attempt. If <see cref="EetClientOptions.OnDiagnosticEvent"/> is configured, a
    /// <see cref="EetDiagnosticEventKind.Failed"/> event is still raised before the exception propagates, so
    /// callers can observe and handle the failure.
    /// </para>
    /// </summary>
    /// <param name="sale">Sale data to validate, sign, and submit.</param>
    /// <param name="cancellationToken">Token used to cancel the HTTP request or an automatic resend delay.</param>
    /// <returns>The acknowledgement or error response returned by the EET service.</returns>
    public async Task<EetResponse> RegisterSaleAsync(RegisteredSale sale, CancellationToken cancellationToken = default)
    {
        if (sale == null) throw new ArgumentNullException(nameof(sale));
        var options = _options ?? throw new InvalidOperationException("RegisterSaleAsync requires EetClientOptions.");

        if (!options.EnableAutomaticResend)
            return await SendOnceAsync(sale, options, cancellationToken).ConfigureAwait(false);

        if (options.ResendDelays == null || options.ResendDelays.Count == 0)
            throw new InvalidOperationException("EetClientOptions.ResendDelays must contain at least one delay when EnableAutomaticResend is true.");

        var maxAttempts = options.ResendDelays.Count + 1;
        var currentSale = sale;
        var attempt = 0;
        while (true)
        {
            attempt++;
            var response = await SendOnceAsync(currentSale, options, cancellationToken).ConfigureAwait(false);
            var isTemporaryError = response is EetErrorResponse error && error.ErrorCode == -1;

            if (!isTemporaryError)
                return response;

            if (attempt >= maxAttempts)
            {
                options.OnResendAttempt?.Invoke(new EetResendAttempt(attempt, maxAttempts, TimeSpan.Zero, response, isFinalAttempt: true));
                return response;
            }

            var delay = options.ResendDelays[attempt - 1];
            options.OnResendAttempt?.Invoke(new EetResendAttempt(attempt, maxAttempts, delay, response, isFinalAttempt: false));
            options.OnDiagnosticEvent?.Invoke(new EetDiagnosticEvent(EetDiagnosticEventKind.ResendScheduled, $"Attempt {attempt}/{maxAttempts} failed with a temporary error; resending in {delay}.", currentSale.MessageId));
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            currentSale = CreateResendCopy(currentSale);
        }
    }

    /// <summary>
    /// Verifies that the client is fully and correctly configured by sending a signed verification-mode
    /// (<see cref="RegisteredSale.VerificationMode"/> = <c>true</c>) message to the configured EET endpoint.
    /// Unlike <see cref="EetClientOptions.Validate"/>, this performs an actual network round trip, so it
    /// also exercises TLS/certificate trust and endpoint reachability. A verification-mode message is never
    /// registered by the EET service, so this is safe to call against the production endpoint. Both a
    /// successful acknowledgement and an EET-level rejection (e.g. a malformed placeholder value) indicate
    /// that transport, TLS trust, and message signing are working; only transport-level, protocol-level, or
    /// signing failures are reported as an unsuccessful result.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the connection test.</param>
    /// <returns>A result describing whether the signed verification request reached the EET service.</returns>
    public async Task<EetConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var options = _options ?? throw new InvalidOperationException("TestConnectionAsync requires EetClientOptions.");

        var probe = new RegisteredSale
        {
            VerificationMode = true,
            Eic = "CZ00000019",
            UnitId = 1,
            PosId = "connection-test",
            TransactionNumber = "conn-test-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            SubmissionTime = DateTimeOffset.Now,
            TransactionTime = DateTimeOffset.Now,
            TotalAmount = 0.00m,
            FirstSubmission = true
        };

        try
        {
            var response = await SendOnceAsync(probe, options, cancellationToken).ConfigureAwait(false);
            return response switch
            {
                EetAcknowledgementResponse ack => EetConnectionTestResult.Success(ack, $"Connected successfully. Received POK: {ack.Pok}."),
                EetErrorResponse error => EetConnectionTestResult.Success(error, $"Connected successfully. The EET service responded with error {error.ErrorCode}: {error.ErrorMessage}."),
                _ => EetConnectionTestResult.Failure("The EET service returned an unrecognized response.", exception: null!)
            };
        }
        catch (EetTransportException ex)
        {
            return EetConnectionTestResult.Failure($"Could not reach the EET endpoint: {ex.Message}", ex);
        }
        catch (EetProtocolException ex)
        {
            return EetConnectionTestResult.Failure($"The EET service returned a SOAP fault: {ex.Message}", ex);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            return EetConnectionTestResult.Failure($"The signing certificate could not be used: {ex.Message}", ex);
        }
        catch (EetValidationException ex)
        {
            return EetConnectionTestResult.Failure($"The generated verification message was rejected locally: {ex.Message}", ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller requested cancellation; this is not a connection test failure, propagate as usual.
            throw;
        }
        catch (HttpRequestException ex)
        {
            return EetConnectionTestResult.Failure($"Could not connect to the EET endpoint: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            // Guard against any other unexpected failure (e.g. DNS resolution errors, socket exceptions, or
            // an unanticipated timeout) so TestConnectionAsync never throws - it always returns a result.
            return EetConnectionTestResult.Failure($"The connection test failed unexpectedly: {ex.Message}", ex);
        }
    }

    private async Task<EetResponse> SendOnceAsync(RegisteredSale sale, EetClientOptions options, CancellationToken cancellationToken)
    {
        var endpoint = ResolveEndpoint(options);
        var body = EetMessageSerializer.SerializeRegisteredSale(sale);
        var envelope = EetSoapEnvelopeBuilder.Build(body, "id-" + Guid.NewGuid().ToString("N"));
        var certificate = ResolveCertificate(options, out var ownsCertificate);
        options.OnDiagnosticEvent?.Invoke(new EetDiagnosticEvent(EetDiagnosticEventKind.Sending, $"Sending registered sale to {endpoint}.", sale.MessageId));
        try
        {
            var signedEnvelope = EetMessageSigner.Sign(envelope, certificate);
            if (System.Text.Encoding.UTF8.GetByteCount(signedEnvelope) > 12 * 1024)
                throw new EetValidationException("The SOAP message exceeds the EET 12 kB limit.");

            var transport = new EetSoapTransport(_httpClient, endpoint);
            var response = await transport.SendAsync(signedEnvelope, cancellationToken).ConfigureAwait(false);
            var parsed = EetResponseParser.Parse(response.Body, response.GlobalTransactionId, options);
            options.OnDiagnosticEvent?.Invoke(new EetDiagnosticEvent(EetDiagnosticEventKind.ResponseReceived, DescribeResponse(parsed), sale.MessageId));
            return parsed;
        }
        catch (Exception exception)
        {
            options.OnDiagnosticEvent?.Invoke(new EetDiagnosticEvent(EetDiagnosticEventKind.Failed, $"Sending the registered sale failed: {exception.Message}", sale.MessageId, exception));
            throw;
        }
        finally
        {
            if (ownsCertificate) certificate.Dispose();
        }
    }

    private static string DescribeResponse(EetResponse response) => response switch
    {
        EetAcknowledgementResponse ack => $"Received acknowledgement with POK {ack.Pok}.",
        EetErrorResponse error => $"Received EET error {error.ErrorCode}: {error.ErrorMessage}.",
        _ => "Received an unrecognized response."
    };

    private static RegisteredSale CreateResendCopy(RegisteredSale sale) => new()
    {
        MessageId = Guid.NewGuid(),
        SubmissionTime = DateTimeOffset.Now,
        FirstSubmission = false,
        VerificationMode = sale.VerificationMode,
        Eic = sale.Eic,
        AuthorizingEic = sale.AuthorizingEic,
        MultipleTaxpayerAuthorization = sale.MultipleTaxpayerAuthorization,
        UnitId = sale.UnitId,
        PosId = sale.PosId,
        TransactionNumber = sale.TransactionNumber,
        TransactionTime = sale.TransactionTime,
        TotalAmount = sale.TotalAmount,
        IntendedSettlementAmount = sale.IntendedSettlementAmount,
        SettledAmount = sale.SettledAmount
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static HttpClient CreateHttpClient(EetClientOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

#if NET
        // The EET endpoint uses DNS-based high availability and its DNS record can change over time, so
        // pooled connections must be periodically re-established to pick up new addresses.
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = options.HttpConnectionLifetime
        };
        var client = new HttpClient(handler);
#else
        // SocketsHttpHandler is not available on .NET Framework; approximate the same behavior by
        // bounding how long DNS resolutions and connection leases are cached.
        System.Net.ServicePointManager.DnsRefreshTimeout = (int)options.HttpConnectionLifetime.TotalMilliseconds;
        var client = new HttpClient();
        if (!string.IsNullOrWhiteSpace(options.BaseAddress))
        {
            var servicePoint = System.Net.ServicePointManager.FindServicePoint(new Uri(options.BaseAddress, UriKind.Absolute));
            servicePoint.ConnectionLeaseTimeout = (int)options.HttpConnectionLifetime.TotalMilliseconds;
        }
#endif

        if (!string.IsNullOrWhiteSpace(options.BaseAddress))
        {
            client.BaseAddress = new Uri(options.BaseAddress, UriKind.Absolute);
        }

        return client;
    }

    private Uri ResolveEndpoint(EetClientOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseAddress))
            return new Uri(options.BaseAddress, UriKind.Absolute);
        if (_httpClient.BaseAddress != null)
            return _httpClient.BaseAddress;
        throw new InvalidOperationException("An EET HTTPS endpoint must be configured.");
    }

    private static X509Certificate2 ResolveCertificate(EetClientOptions options, out bool ownsCertificate)
    {
        ownsCertificate = false;
        if (options.SigningCertificate != null)
            return options.SigningCertificate;
        if (!string.IsNullOrWhiteSpace(options.SigningCertificatePath))
#if NET10_0_OR_GREATER
        {
            ownsCertificate = true;
            return X509CertificateLoader.LoadPkcs12(System.IO.File.ReadAllBytes(options.SigningCertificatePath), options.SigningCertificatePassword);
        }
#else
        {
            ownsCertificate = true;
            return new X509Certificate2(options.SigningCertificatePath, options.SigningCertificatePassword);
        }
#endif
        throw new InvalidOperationException("A signing certificate must be configured.");
    }
}
