using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using Selpo.Eet20;
using Selpo.Eet20.Security;
using Selpo.Eet20.Serialization;
using Xunit;

namespace Selpo.Eet20.Tests;

public sealed class EetMessageSignerTests
{
    [Fact]
    public void Signs_only_the_soap_body_with_required_algorithms()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=EET test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
        var body = EetMessageSerializer.SerializeRegisteredSale(CreateSale());
        var envelope = EetSoapEnvelopeBuilder.Build(body, "body-1");

        var signed = EetMessageSigner.Sign(envelope, certificate);
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(signed);

        var signature = document.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#").Item(0) as XmlElement;
        Assert.NotNull(signature);
        Assert.Equal("http://www.w3.org/2001/10/xml-exc-c14n#", signature!["SignedInfo", "http://www.w3.org/2000/09/xmldsig#"]!["CanonicalizationMethod", "http://www.w3.org/2000/09/xmldsig#"]!.GetAttribute("Algorithm"));
        Assert.Equal("http://www.w3.org/2001/04/xmldsig-more#rsa-sha256", signature["SignedInfo", "http://www.w3.org/2000/09/xmldsig#"]!["SignatureMethod", "http://www.w3.org/2000/09/xmldsig#"]!.GetAttribute("Algorithm"));
        var references = signature.GetElementsByTagName("Reference", "http://www.w3.org/2000/09/xmldsig#").Cast<XmlElement>().ToArray();
        Assert.Single(references);
        Assert.Equal("#body-1", references[0].GetAttribute("URI"));
        Assert.Single(document.GetElementsByTagName("BinarySecurityToken", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"));
    }

    private static RegisteredSale CreateSale() => new RegisteredSale
    {
        Eic = "CZ12345678",
        UnitId = 1,
        PosId = "POS1",
        TransactionNumber = "1",
        TransactionTime = DateTimeOffset.UtcNow,
        TotalAmount = 1m
    };
}
