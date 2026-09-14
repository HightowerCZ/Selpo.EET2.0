using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Selpo.Eet20;
using Xunit;

namespace Selpo.Eet20.Tests;

public sealed class EetClientTests
{
    [Fact]
    public void Creates_http_client_from_options()
    {
        using var client = new EetClient(new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/"
        });

        Assert.Equal(new Uri("https://eet.example.test/"), client.HttpClient.BaseAddress);
    }

    [Fact]
    public void Uses_the_http_client_provided_by_the_application()
    {
        using var httpClient = new HttpClient();
        using var client = new EetClient(httpClient);

        Assert.Same(httpClient, client.HttpClient);
    }

    [Fact]
    public void Rejects_null_options()
    {
        Assert.Throws<ArgumentNullException>(() => new EetClient((EetClientOptions)null!));
    }

    [Fact]
    public void Rejects_null_http_client()
    {
        Assert.Throws<ArgumentNullException>(() => new EetClient((HttpClient)null!));
    }

    [Fact]
    public async Task RegisterSaleAsync_requires_resend_delays_when_automatic_resend_is_enabled()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => CreateErrorResponse(-1)));
        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate,
            EnableAutomaticResend = true
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RegisterSaleAsync(CreateSale()));
    }

    [Fact]
    public async Task RegisterSaleAsync_resends_on_temporary_error_until_schedule_is_exhausted()
    {
        using var certificate = CreateSelfSignedCertificate();
        var attemptsMade = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref attemptsMade);
            return CreateErrorResponse(-1);
        }));

        var notifications = new List<EetResendAttempt>();
        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate,
            EnableAutomaticResend = true,
            ResendDelays = new[] { TimeSpan.Zero, TimeSpan.Zero },
            OnResendAttempt = notifications.Add
        });

        var response = await client.RegisterSaleAsync(CreateSale());

        var error = Assert.IsType<EetErrorResponse>(response);
        Assert.Equal(-1, error.ErrorCode);
        Assert.Equal(3, attemptsMade);
        Assert.Equal(3, notifications.Count);
        Assert.False(notifications[0].IsFinalAttempt);
        Assert.False(notifications[1].IsFinalAttempt);
        Assert.True(notifications[2].IsFinalAttempt);
        Assert.Equal(3, notifications[2].MaxAttempts);
    }

    [Fact]
    public async Task RegisterSaleAsync_does_not_resend_non_temporary_errors()
    {
        using var certificate = CreateSelfSignedCertificate();
        var attemptsMade = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref attemptsMade);
            return CreateErrorResponse(3);
        }));

        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate,
            EnableAutomaticResend = true,
            ResendDelays = new[] { TimeSpan.Zero }
        });

        var response = await client.RegisterSaleAsync(CreateSale());

        var error = Assert.IsType<EetErrorResponse>(response);
        Assert.Equal(3, error.ErrorCode);
        Assert.Equal(1, attemptsMade);
    }

    [Fact]
    public async Task RegisterSaleAsync_includes_global_transaction_id_in_transport_exception()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Internal error", System.Text.Encoding.UTF8, "text/plain")
            };
            response.Headers.Add("X-Global-Transaction-Id", "452ba7806a71df6100036b61");
            return response;
        }));

        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate
        });

        var exception = await Assert.ThrowsAsync<EetTransportException>(() => client.RegisterSaleAsync(CreateSale()));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Equal("452ba7806a71df6100036b61", exception.GlobalTransactionId);
        Assert.Equal("Internal error", exception.ResponseBody);
    }

    [Fact]
    public void Validate_passes_for_a_well_formed_configuration()
    {
        using var certificate = CreateSelfSignedCertificate();
        var options = new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate
        };

        var exception = Record.Exception(() => options.Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_rejects_missing_base_address()
    {
        using var certificate = CreateSelfSignedCertificate();
        var options = new EetClientOptions
        {
            SigningCertificate = certificate
        };

        var exception = Assert.Throws<EetValidationException>(() => options.Validate());
        Assert.Contains("BaseAddress", exception.Message);
    }

    [Fact]
    public void Validate_rejects_non_https_base_address()
    {
        using var certificate = CreateSelfSignedCertificate();
        var options = new EetClientOptions
        {
            BaseAddress = "http://eet.example.test/",
            SigningCertificate = certificate
        };

        var exception = Assert.Throws<EetValidationException>(() => options.Validate());
        Assert.Contains("HTTPS", exception.Message);
    }

    [Fact]
    public void Validate_rejects_missing_signing_certificate()
    {
        var options = new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/"
        };

        var exception = Assert.Throws<EetValidationException>(() => options.Validate());
        Assert.Contains("signing certificate", exception.Message);
    }

    [Fact]
    public void Validate_rejects_enabled_resend_without_delays()
    {
        using var certificate = CreateSelfSignedCertificate();
        var options = new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate,
            EnableAutomaticResend = true
        };

        var exception = Assert.Throws<EetValidationException>(() => options.Validate());
        Assert.Contains("ResendDelays", exception.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_reports_success_on_acknowledgement()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var authorityCertificate = CreateSelfSignedCertificate("CN=EET authority test");
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => CreateAcknowledgementResponse(authorityCertificate)));

        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate,
            UseSystemCertificateTrust = false,
            PinnedAuthorityCertificate = authorityCertificate
        });

        var result = await client.TestConnectionAsync();

        Assert.True(result.IsSuccess);
        Assert.IsType<EetAcknowledgementResponse>(result.Response);
        Assert.Null(result.Exception);
    }

    [Fact]
    public async Task TestConnectionAsync_reports_success_on_eet_error_response()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => CreateErrorResponse(2)));

        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate
        });

        var result = await client.TestConnectionAsync();

        Assert.True(result.IsSuccess);
        Assert.IsType<EetErrorResponse>(result.Response);
    }

    [Fact]
    public async Task TestConnectionAsync_reports_failure_on_transport_exception()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Unavailable", System.Text.Encoding.UTF8, "text/plain")
            }));

        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate
        });

        var result = await client.TestConnectionAsync();

        Assert.False(result.IsSuccess);
        Assert.IsType<EetTransportException>(result.Exception);
    }

    [Fact]
    public async Task TestConnectionAsync_reports_failure_on_unexpected_exception()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("boom")));

        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate
        });

        var result = await client.TestConnectionAsync();

        Assert.False(result.IsSuccess);
        Assert.IsType<InvalidOperationException>(result.Exception);
        Assert.Contains("unexpectedly", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_reports_failure_on_http_request_exception()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => throw new HttpRequestException("dns failure")));

        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate
        });

        var result = await client.TestConnectionAsync();

        Assert.False(result.IsSuccess);
        Assert.IsType<HttpRequestException>(result.Exception);
    }

    [Fact]
    public async Task RegisterSaleAsync_raises_diagnostic_events_for_send_and_response()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => CreateErrorResponse(2)));

        var events = new List<EetDiagnosticEvent>();
        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate,
            OnDiagnosticEvent = events.Add
        });

        await client.RegisterSaleAsync(CreateSale());

        Assert.Contains(events, e => e.Kind == EetDiagnosticEventKind.Sending);
        Assert.Contains(events, e => e.Kind == EetDiagnosticEventKind.ResponseReceived);
    }

    [Fact]
    public async Task RegisterSaleAsync_raises_failed_diagnostic_event_on_transport_exception()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Unavailable", System.Text.Encoding.UTF8, "text/plain")
            }));

        var events = new List<EetDiagnosticEvent>();
        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate,
            OnDiagnosticEvent = events.Add
        });

        await Assert.ThrowsAsync<EetTransportException>(() => client.RegisterSaleAsync(CreateSale()));

        Assert.Contains(events, e => e.Kind == EetDiagnosticEventKind.Failed && e.Exception is EetTransportException);
    }

    [Fact]
    public async Task RegisterSaleAsync_raises_resend_scheduled_diagnostic_event()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => CreateErrorResponse(-1)));

        var events = new List<EetDiagnosticEvent>();
        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate,
            EnableAutomaticResend = true,
            ResendDelays = new[] { TimeSpan.Zero },
            OnDiagnosticEvent = events.Add
        });

        await client.RegisterSaleAsync(CreateSale());

        Assert.Contains(events, e => e.Kind == EetDiagnosticEventKind.ResendScheduled);
    }

    #if NET
    [Fact]
    public void Creates_http_client_that_applies_connection_lifetime()
    {
        using var client = new EetClient(new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            HttpConnectionLifetime = TimeSpan.FromMinutes(2)
        });

        var handler = GetPrimaryHandler(client.HttpClient);
        Assert.IsType<SocketsHttpHandler>(handler);
        Assert.Equal(TimeSpan.FromMinutes(2), ((SocketsHttpHandler)handler).PooledConnectionLifetime);
    }

    private static object GetPrimaryHandler(HttpClient httpClient)
    {
        var field = typeof(HttpMessageInvoker).GetField("_handler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("Could not locate the internal HttpClient handler field.");
        return field.GetValue(httpClient) ?? throw new InvalidOperationException("HttpClient handler was null.");
    }
#else
    [Fact]
    public void Creates_http_client_that_applies_connection_lifetime_via_service_point_manager()
    {
        using var client = new EetClient(new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            HttpConnectionLifetime = TimeSpan.FromMinutes(2)
        });

        var servicePoint = System.Net.ServicePointManager.FindServicePoint(new Uri("https://eet.example.test/"));
        Assert.Equal((int)TimeSpan.FromMinutes(2).TotalMilliseconds, servicePoint.ConnectionLeaseTimeout);
        Assert.Equal((int)TimeSpan.FromMinutes(2).TotalMilliseconds, System.Net.ServicePointManager.DnsRefreshTimeout);
    }
#endif

    private static HttpResponseMessage CreateAcknowledgementResponse(X509Certificate2 authorityCertificate)
    {
        var body = "<tns:Odpoved xmlns:tns=\"http://fs.gov.cz/eet/schema/v4\"><tns:Hlavicka uuid_zpravy=\"" + Guid.NewGuid() +
            "\" dat_prij=\"2027-03-04T18:25:21+01:00\" /><tns:Potvrzeni pok=\"1234abcd-1234-abcd-1234-abcd1234abcd-02\" test=\"true\" /></tns:Odpoved>";
        var envelope = Selpo.Eet20.Serialization.EetSoapEnvelopeBuilder.Build(body, "body-" + Guid.NewGuid().ToString("N"));
        var signed = Selpo.Eet20.Security.EetMessageSigner.Sign(envelope, authorityCertificate);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(signed, System.Text.Encoding.UTF8, "text/xml")
        };
    }

    private static RegisteredSale CreateSale() => new()
    {
        Eic = "CZ12345678",
        UnitId = 1,
        PosId = "POS1",
        TransactionNumber = "1",
        TransactionTime = DateTimeOffset.UtcNow,
        TotalAmount = 1m
    };

    private static X509Certificate2 CreateSelfSignedCertificate(string subjectName = "CN=EET test")
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
    }

    private static HttpResponseMessage CreateErrorResponse(int errorCode)
    {
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:tns=\"http://fs.gov.cz/eet/schema/v4\">" +
            "<soap:Body><tns:Odpoved><tns:Hlavicka dat_odmit=\"2027-03-04T18:25:21+01:00\" />" +
            $"<tns:Chyba kod=\"{errorCode}\">Chyba zpracovani</tns:Chyba></tns:Odpoved></soap:Body></soap:Envelope>";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(xml, System.Text.Encoding.UTF8, "text/xml")
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
