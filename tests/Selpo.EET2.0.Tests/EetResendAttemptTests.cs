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

/// <summary>
/// Dedicated coverage for the <see cref="EetResendAttempt"/> payload delivered via
/// <see cref="EetClientOptions.OnResendAttempt"/>, separate from the broader <see cref="EetClientTests"/>.
/// </summary>
public sealed class EetResendAttemptTests
{
    [Fact]
    public async Task Intermediate_attempts_report_delay_and_are_not_final()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => CreateErrorResponse(-1)));

        var attempts = new List<EetResendAttempt>();
        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate,
            EnableAutomaticResend = true,
            ResendDelays = new[] { TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2) },
            OnResendAttempt = attempts.Add
        });

        await client.RegisterSaleAsync(CreateSale());

        Assert.Equal(3, attempts.Count);

        Assert.Equal(1, attempts[0].AttemptNumber);
        Assert.Equal(3, attempts[0].MaxAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(1), attempts[0].Delay);
        Assert.False(attempts[0].IsFinalAttempt);
        Assert.IsType<EetErrorResponse>(attempts[0].Response);

        Assert.Equal(2, attempts[1].AttemptNumber);
        Assert.Equal(3, attempts[1].MaxAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(2), attempts[1].Delay);
        Assert.False(attempts[1].IsFinalAttempt);

        Assert.Equal(3, attempts[2].AttemptNumber);
        Assert.Equal(3, attempts[2].MaxAttempts);
        Assert.Equal(TimeSpan.Zero, attempts[2].Delay);
        Assert.True(attempts[2].IsFinalAttempt);
    }

    [Fact]
    public async Task Successful_response_after_resend_stops_the_schedule_without_reporting_final_attempt()
    {
        using var certificate = CreateSelfSignedCertificate();
        var callCount = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            callCount++;
            return callCount == 1 ? CreateErrorResponse(-1) : CreateErrorResponse(2);
        }));

        var attempts = new List<EetResendAttempt>();
        using var client = new EetClient(httpClient, new EetClientOptions
        {
            BaseAddress = "https://eet.example.test/",
            SigningCertificate = certificate,
            EnableAutomaticResend = true,
            ResendDelays = new[] { TimeSpan.Zero, TimeSpan.Zero },
            OnResendAttempt = attempts.Add
        });

        var response = await client.RegisterSaleAsync(CreateSale());

        var singleAttempt = Assert.Single(attempts);
        Assert.False(singleAttempt.IsFinalAttempt);
        Assert.IsType<EetErrorResponse>(response);
        Assert.Equal(2, ((EetErrorResponse)response).ErrorCode);
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
