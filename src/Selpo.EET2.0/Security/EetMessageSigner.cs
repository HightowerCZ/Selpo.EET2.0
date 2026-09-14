using System;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using Selpo.Eet20.Serialization;

namespace Selpo.Eet20.Security;

internal static class EetMessageSigner
{
    private const string WsseNamespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    private const string X509TokenProfile = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3";
    private const string Base64Binary = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";

    internal static string Sign(string envelopeXml, X509Certificate2 certificate)
    {
        if (envelopeXml == null) throw new ArgumentNullException(nameof(envelopeXml));
        if (certificate == null) throw new ArgumentNullException(nameof(certificate));
        if (!certificate.HasPrivateKey) throw new EetValidationException("The signing certificate must contain a private key.");

        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(envelopeXml);

        var body = FindRequired(document, "Body", EetSoapEnvelopeBuilder.SoapNamespace);
        var bodyId = body.GetAttribute("Id", EetSoapEnvelopeBuilder.UtilityNamespace);
        if (string.IsNullOrEmpty(bodyId))
            throw new EetValidationException("The SOAP body must have a wsu:Id attribute.");

        var header = FindRequired(document, "Header", EetSoapEnvelopeBuilder.SoapNamespace);
        var security = document.CreateElement("wsse", "Security", WsseNamespace);
        security.SetAttribute("xmlns:wsu", EetSoapEnvelopeBuilder.UtilityNamespace);
        header.AppendChild(security);

        var tokenId = "X509-" + Guid.NewGuid().ToString("N");
        var token = document.CreateElement("wsse", "BinarySecurityToken", WsseNamespace);
        token.SetAttribute("EncodingType", Base64Binary);
        token.SetAttribute("ValueType", X509TokenProfile);
        token.SetAttribute("Id", EetSoapEnvelopeBuilder.UtilityNamespace, tokenId);
        token.InnerText = Convert.ToBase64String(certificate.RawData);
        security.AppendChild(token);

        var signedXml = new WsuSignedXml(document);
        using var signingKey = certificate.GetRSAPrivateKey();
        signedXml.SigningKey = signingKey ?? throw new EetValidationException("The signing certificate must contain an RSA private key.");
        var signedInfo = signedXml.SignedInfo ?? throw new EetValidationException("Unable to create XML signature information.");
        signedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
        signedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;

        var reference = new Reference("#" + bodyId)
        {
            DigestMethod = SignedXml.XmlDsigSHA256Url
        };
        reference.AddTransform(new XmlDsigExcC14NTransform());
        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        var tokenReference = document.CreateElement("wsse", "SecurityTokenReference", WsseNamespace);
        var tokenReferenceValue = document.CreateElement("wsse", "Reference", WsseNamespace);
        tokenReferenceValue.SetAttribute("URI", "#" + tokenId);
        tokenReferenceValue.SetAttribute("ValueType", X509TokenProfile);
        tokenReference.AppendChild(tokenReferenceValue);
        keyInfo.AddClause(new KeyInfoNode(tokenReference));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();
        security.AppendChild(document.ImportNode(signedXml.GetXml(), deep: true));
        return document.OuterXml;
    }

    private static XmlElement FindRequired(XmlDocument document, string localName, string namespaceUri)
    {
        var element = document.GetElementsByTagName(localName, namespaceUri).Item(0) as XmlElement;
        return element ?? throw new EetValidationException($"SOAP message is missing {localName}.");
    }

    private sealed class WsuSignedXml : SignedXml
    {
        internal WsuSignedXml(XmlDocument document) : base(document) { }

        public override XmlElement? GetIdElement(XmlDocument? document, string idValue)
        {
            if (document == null) return null;
            var element = base.GetIdElement(document, idValue);
            if (element != null) return element;
            return document.SelectSingleNode(
                "//*[@wsu:Id=" + EscapeXPathValue(idValue) + "]",
                CreateNamespaceManager(document)) as XmlElement;
        }

        private static XmlNamespaceManager CreateNamespaceManager(XmlDocument document)
        {
            var manager = new XmlNamespaceManager(document.NameTable);
            manager.AddNamespace("wsu", EetSoapEnvelopeBuilder.UtilityNamespace);
            return manager;
        }

        private static string EscapeXPathValue(string value)
        {
            return "'" + value.Replace("'", "&apos;") + "'";
        }
    }
}
