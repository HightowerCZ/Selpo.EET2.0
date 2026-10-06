using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
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
    public async Task RegisterSaleAsync_times_out_when_timeout_is_reached()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate
        });

        await Assert.ThrowsAsync<TimeoutException>(() => client.RegisterSaleAsync(CreateSale(), TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public async Task RegisterSaleAsync_rejects_non_positive_timeout()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => CreateErrorResponse(2)));

        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate
        });

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.RegisterSaleAsync(CreateSale(), TimeSpan.Zero));
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
    public void Validate_rejects_invalid_signing_certificate_file()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not a certificate");
            var options = new EetClientOptions
            {
                BaseAddress = "https://eet.example.test/",
                SigningCertificatePath = path
            };

            var exception = Assert.Throws<EetValidationException>(() => options.Validate());
            Assert.Contains("could not be loaded", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validate_rejects_certificate_file_without_private_key()
    {
        using var certificate = CreateSelfSignedCertificate();
#if NET10_0_OR_GREATER
        using var publicCertificate = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
#else
        using var publicCertificate = new X509Certificate2(certificate.Export(X509ContentType.Cert));
#endif
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, publicCertificate.Export(X509ContentType.Pkcs12));
            var options = new EetClientOptions
            {
                BaseAddress = "https://eet.example.test/",
                SigningCertificatePath = path
            };

            var exception = Assert.Throws<EetValidationException>(() => options.Validate());
            Assert.Contains("private key", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
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
    public void Validate_rejects_negative_resend_delay()
    {
        using var certificate = CreateSelfSignedCertificate();
        var options = new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate,
            EnableAutomaticResend = true,
            ResendDelays = new[] { TimeSpan.FromSeconds(-1) }
        };

        var exception = Assert.Throws<EetValidationException>(() => options.Validate());
        Assert.Contains("must not be negative", exception.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_uses_configured_identity_endpoint_and_certificate_without_mutating_template()
    {
        using var certificate = CreateSelfSignedCertificate("CN=Configured taxpayer");
        var template = CreateSale();
        template.UnitId = 181;
        template.PosId = "ACTUAL-POS";
        template.AuthorizingEic = "CZ87654321";
        template.MultipleTaxpayerAuthorization = true;
        template.FirstSubmission = false;
        template.SubmissionTime = DateTimeOffset.UtcNow.AddDays(-1);
        var originalMessageId = template.MessageId;
        var originalSubmissionTime = template.SubmissionTime;
        string? requestBody = null;
        Uri? requestUri = null;
        var requestCount = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (request, _) =>
        {
            requestCount++;
            requestUri = request.RequestUri;
            requestBody = await request.Content!.ReadAsStringAsync();
            return CreateErrorResponse(0);
        })) { BaseAddress = new Uri("https://unused.example.test/") };
        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://configured.example.test/eet",
            SigningCertificate = certificate,
            ConnectionTestSale = template,
            EnableAutomaticResend = true,
            ResendDelays = new[] { TimeSpan.Zero }
        });

        var result = await client.TestConnectionAsync();

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(1, requestCount);
        Assert.Equal(new Uri("https://configured.example.test/eet"), requestUri);
        var document = new XmlDocument();
        document.LoadXml(requestBody!);
        var header = (XmlElement)document.GetElementsByTagName("Hlavicka", "http://fs.gov.cz/eet/schema/v4")[0]!;
        var data = (XmlElement)document.GetElementsByTagName("Data", "http://fs.gov.cz/eet/schema/v4")[0]!;
        Assert.Equal("true", header.GetAttribute("overeni"));
        Assert.Equal("true", header.GetAttribute("prvni_zaslani"));
        Assert.NotEqual(originalMessageId.ToString("D"), header.GetAttribute("uuid_zpravy"));
        Assert.NotEqual(originalSubmissionTime.ToString("yyyy-MM-dd'T'HH:mm:sszzz"), header.GetAttribute("dat_odesl"));
        Assert.Equal(template.Eic, data.GetAttribute("eic_popl"));
        Assert.Equal("181", data.GetAttribute("id_jednotky"));
        Assert.Equal(template.PosId, data.GetAttribute("id_pokl"));
        Assert.Equal(template.AuthorizingEic, data.GetAttribute("eic_poverujiciho"));
        Assert.Equal("true", data.GetAttribute("povereni_vice_popl"));
        Assert.Equal(template.TransactionNumber, data.GetAttribute("porad_cis"));
        Assert.Equal("1.00", data.GetAttribute("celk_trzba"));
        var token = document.GetElementsByTagName("BinarySecurityToken", "*")[0]!;
        Assert.Equal(certificate.RawData, Convert.FromBase64String(token.InnerText));
        Assert.False(template.VerificationMode);
        Assert.False(template.FirstSubmission);
        Assert.Equal(originalMessageId, template.MessageId);
        Assert.Equal(originalSubmissionTime, template.SubmissionTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestConnectionAsync_rejects_missing_or_invalid_probe_without_sending(bool invalidProbe)
    {
        var requestCount = 0;
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            requestCount++;
            return CreateErrorResponse(0);
        }));
        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate,
            ConnectionTestSale = invalidProbe ? new RegisteredSale() : null
        });

        var result = await client.TestConnectionAsync();

        Assert.False(result.IsSuccess);
        Assert.IsType<EetValidationException>(result.Exception);
        Assert.Null(result.Response);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task TestConnectionAsync_uses_http_client_endpoint_when_options_endpoint_is_missing()
    {
        using var certificate = CreateSelfSignedCertificate();
        Uri? requestUri = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestUri = request.RequestUri;
            return CreateErrorResponse(0);
        })) { BaseAddress = new Uri("https://fallback.example.test/eet") };
        using var client = new EetClient(httpClient, new EetClientOptions
        {
            SigningCertificate = certificate,
            ConnectionTestSale = CreateSale()
        });

        var result = await client.TestConnectionAsync();

        Assert.True(result.IsSuccess, result.Message);
        Assert.False(result.IsAcknowledgementTrustValidated);
        Assert.Equal(httpClient.BaseAddress, requestUri);
    }

    [Theory]
    [InlineData(0, "ResendDelays")]
    [InlineData(1, "must not be negative")]
    [InlineData(2, "HttpConnectionLifetime")]
    [InlineData(3, "no pinned authority certificate")]
    [InlineData(4, "does not exist")]
    [InlineData(5, "could not be loaded")]
    [InlineData(6, "HTTPS")]
    public async Task TestConnectionAsync_rejects_invalid_configuration_before_sending(int configuration, string expectedError)
    {
        using var certificate = CreateSelfSignedCertificate();
        var filePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(filePath, "not a certificate");
            var requestCount = 0;
            using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            {
                requestCount++;
                return CreateErrorResponse(0);
            }));
            var options = new EetClientOptions
            {
                BaseAddress = "https://eet.example.test/",
                SigningCertificate = certificate,
                ConnectionTestSale = CreateSale()
            };
            switch (configuration)
            {
                case 0: options.EnableAutomaticResend = true; break;
                case 1: options.ResendDelays = new[] { TimeSpan.FromSeconds(-1) }; break;
                case 2: options.HttpConnectionLifetime = TimeSpan.Zero; break;
                case 3: options.UseSystemCertificateTrust = false; break;
                case 4: options.AuthorityRootCertificatePath = filePath + ".missing"; break;
                case 5: options.AuthorityIntermediateCertificatePath = filePath; break;
                case 6: options.BaseAddress = "http://eet.example.test/"; break;
            }
            using var client = new EetClient(httpClient, options);

            var result = await client.TestConnectionAsync();

            Assert.False(result.IsSuccess);
            Assert.False(result.IsAcknowledgementTrustValidated);
            Assert.IsType<EetValidationException>(result.Exception);
            Assert.Contains(expectedError, result.Exception!.Message);
            Assert.Equal(0, requestCount);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task TestConnectionAsync_rejects_expired_signing_certificate_before_sending()
    {
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=Expired taxpayer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1));
        var requestCount = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            requestCount++;
            return CreateErrorResponse(0);
        }));
        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate,
            ConnectionTestSale = CreateSale()
        });

        var result = await client.TestConnectionAsync();

        Assert.False(result.IsSuccess);
        Assert.IsType<EetValidationException>(result.Exception);
        Assert.Contains("not currently valid", result.Exception!.Message);
        Assert.Equal(0, requestCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestConnectionAsync_never_claims_trust_validation_with_a_mismatched_pin(bool signedAcknowledgement)
    {
        using var certificate = CreateSelfSignedCertificate();
        using var actualAuthority = CreateSelfSignedCertificate("CN=Actual authority");
        using var wrongAuthority = CreateSelfSignedCertificate("CN=Wrong authority");
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => signedAcknowledgement
            ? CreateAcknowledgementResponse(actualAuthority)
            : CreateErrorResponse(0)));
        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate,
            ConnectionTestSale = CreateSale(),
            UseSystemCertificateTrust = false,
            PinnedAuthorityCertificate = wrongAuthority
        });

        var result = await client.TestConnectionAsync();

        Assert.False(result.IsAcknowledgementTrustValidated);
        Assert.Equal(!signedAcknowledgement, result.IsSuccess);
        if (signedAcknowledgement)
            Assert.IsType<EetProtocolException>(result.Exception);
        else
            Assert.Contains("trust was not tested", result.Message);
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
            ConnectionTestSale = CreateSale(),
            SigningCertificate = certificate,
            UseSystemCertificateTrust = false,
            PinnedAuthorityCertificate = authorityCertificate
        });

        var result = await client.TestConnectionAsync();

        Assert.True(result.IsSuccess);
        Assert.True(result.IsAcknowledgementTrustValidated);
        Assert.IsType<EetAcknowledgementResponse>(result.Response);
        Assert.Null(result.Exception);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, false)]
    [InlineData(-1, false)]
    public async Task TestConnectionAsync_reports_verification_result(int errorCode, bool expectedSuccess)
    {
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => CreateErrorResponse(errorCode)));

        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            ConnectionTestSale = CreateSale(),
            SigningCertificate = certificate
        });

        var result = await client.TestConnectionAsync();

        Assert.Equal(expectedSuccess, result.IsSuccess);
        Assert.False(result.IsAcknowledgementTrustValidated);
        if (expectedSuccess) Assert.Contains("trust was not tested", result.Message);
        Assert.Equal(errorCode, Assert.IsType<EetErrorResponse>(result.Response).ErrorCode);
        Assert.Null(result.Exception);
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
            ConnectionTestSale = CreateSale(),
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
            ConnectionTestSale = CreateSale(),
            SigningCertificate = certificate
        });

        var result = await client.TestConnectionAsync();

        Assert.False(result.IsSuccess);
        Assert.IsType<InvalidOperationException>(result.Exception);
        Assert.Contains("unexpectedly", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_reports_failure_when_timeout_is_reached()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            ConnectionTestSale = CreateSale(),
            SigningCertificate = certificate
        });

        var result = await client.TestConnectionAsync(TimeSpan.FromMilliseconds(50));

        Assert.False(result.IsSuccess);
        Assert.IsType<TimeoutException>(result.Exception);
    }

    [Fact]
    public async Task TestConnectionAsync_rejects_non_positive_timeout()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => CreateErrorResponse(2)));

        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate
        });

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.TestConnectionAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task TestConnectionAsync_reports_failure_on_http_request_exception()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => throw new HttpRequestException("dns failure")));

        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            ConnectionTestSale = CreateSale(),
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
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : this((request, _) => Task.FromResult(responder(request)))
        {
        }

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _responder(request, cancellationToken);
    }
}
