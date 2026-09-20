using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Selpo.Eet20;
using Selpo.Eet20.Security;
using Selpo.Eet20.Serialization;
using Xunit;

namespace Selpo.Eet20.Tests;

public sealed class EetResponseParserTests
{
    [Fact]
    public void Parses_unsigned_error_response()
    {
        var response = EetResponseParser.Parse(
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:tns=\"http://fs.gov.cz/eet/schema/v4\"><soap:Body><tns:Odpoved><tns:Hlavicka dat_odmit=\"2027-01-08T21:19:40+01:00\" /><tns:Chyba kod=\"7\">Datova zprava je prilis velka</tns:Chyba></tns:Odpoved></soap:Body></soap:Envelope>",
            "transaction-1",
            new EetClientOptions());

        var error = Assert.IsType<EetErrorResponse>(response);
        Assert.Equal(7, error.ErrorCode);
        Assert.Equal("transaction-1", error.GlobalTransactionId);
    }

    [Fact]
    public void Requires_and_validates_acknowledgement_signature()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=EET authority test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
        var body = "<tns:Odpoved xmlns:tns=\"http://fs.gov.cz/eet/schema/v4\"><tns:Hlavicka uuid_zpravy=\"e23e5a5a-08d7-4a08-844d-2b6c6b60621d\" dat_prij=\"2027-01-08T21:19:40+01:00\" /><tns:Potvrzeni pok=\"987a6be5-6af5-44f3-b4fc-987654321000-02\" /></tns:Odpoved>";
        var envelope = EetSoapEnvelopeBuilder.Build(body, "body-1");
        var signed = EetMessageSigner.Sign(envelope, certificate);

        var response = EetResponseParser.Parse(signed, null, new EetClientOptions
        {
            UseSystemCertificateTrust = false,
            PinnedAuthorityCertificate = certificate
        });

        var acknowledgement = Assert.IsType<EetAcknowledgementResponse>(response);
        Assert.Equal("987a6be5-6af5-44f3-b4fc-987654321000-02", acknowledgement.Pok);
    }

    [Fact]
    public void Rejects_soap_fault_response()
    {
        var exception = Assert.Throws<EetProtocolException>(() => EetResponseParser.Parse(
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body><soap:Fault><faultstring>Service unavailable</faultstring></soap:Fault></soap:Body></soap:Envelope>",
            null,
            new EetClientOptions()));

        Assert.Contains("SOAP fault", exception.Message);
        Assert.Contains("Service unavailable", exception.Message);
    }

    [Fact]
    public void Rejects_response_without_soap_body()
    {
        var exception = Assert.Throws<EetProtocolException>(() => EetResponseParser.Parse(
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"></soap:Envelope>",
            null,
            new EetClientOptions()));

        Assert.Contains("SOAP body", exception.Message);
    }
}
