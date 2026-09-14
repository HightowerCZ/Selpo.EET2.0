using System;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using Selpo.Eet20.Serialization;

namespace Selpo.Eet20.Security;

internal static class EetMessageSignatureValidator
{
    private const string DsNamespace = "http://www.w3.org/2000/09/xmldsig#";
    private const string WsseNamespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";

    internal static void ValidateAcknowledgement(string envelopeXml, EetClientOptions options)
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(envelopeXml);
        var signature = document.GetElementsByTagName("Signature", DsNamespace).Item(0) as XmlElement
            ?? throw new EetProtocolException("The acknowledgement does not contain an XML signature.");
        var token = document.GetElementsByTagName("BinarySecurityToken", WsseNamespace).Item(0) as XmlElement
            ?? throw new EetProtocolException("The acknowledgement does not contain a signing certificate.");

        X509Certificate2 certificate;
        try
        {
    #if NET10_0_OR_GREATER
            certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(token.InnerText));
    #else
            certificate = new X509Certificate2(Convert.FromBase64String(token.InnerText));
    #endif
        }
        catch (Exception exception) when (exception is FormatException || exception is CryptographicException)
        {
            throw new EetProtocolException("The acknowledgement contains an invalid signing certificate.");
        }

        using (certificate)
        {
            var body = document.GetElementsByTagName("Body", EetSoapEnvelopeBuilder.SoapNamespace).Item(0) as XmlElement
                ?? throw new EetProtocolException("The acknowledgement does not contain a SOAP body.");
            var bodyId = body.GetAttribute("Id", EetSoapEnvelopeBuilder.UtilityNamespace);
            if (string.IsNullOrEmpty(bodyId))
                throw new EetProtocolException("The acknowledgement SOAP body has no signing identifier.");

            var signedXml = new WsuSignedXml(document);
            signedXml.LoadXml(signature);
            var references = signedXml.SignedInfo?.References;
            var reference = references != null && references.Count == 1 ? references[0] as Reference : null;
            if (reference == null || reference.Uri != "#" + bodyId)
                throw new EetProtocolException("The acknowledgement signature must reference only the SOAP body.");
            if (!signedXml.CheckSignature(certificate, verifySignatureOnly: true))
                throw new EetProtocolException("The acknowledgement XML signature is invalid.");

            var pinned = options.PinnedAuthorityCertificate;
            if (pinned != null && !string.Equals(certificate.Thumbprint, pinned.Thumbprint, StringComparison.OrdinalIgnoreCase))
                throw new EetProtocolException("The acknowledgement certificate does not match the pinned certificate.");

            if ((options.UseSystemCertificateTrust || HasAuthorityChain(options)) && !BuildTrustedChain(certificate, options))
                throw new EetProtocolException("The acknowledgement certificate is not trusted by the configured certificate policy.");
            if (!options.UseSystemCertificateTrust && pinned == null && !HasAuthorityChain(options))
                throw new EetProtocolException("A pinned authority certificate or authority chain is required when system trust is disabled.");
        }
    }

    private static bool BuildTrustedChain(X509Certificate2 certificate, EetClientOptions options)
    {
        using var chain = new X509Chain();
#if NET10_0_OR_GREATER
        var root = ResolveCertificate(options.AuthorityRootCertificate, options.AuthorityRootCertificatePath);
        if (root != null)
        {
            using (root)
            {
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(root);
                chain.ChainPolicy.RevocationMode = options.RevocationMode;
                AddIntermediate(chain, options);
                return chain.Build(certificate);
            }
        }
#endif
    var hasConfiguredRoot = !string.IsNullOrWhiteSpace(options.AuthorityRootCertificatePath) || options.AuthorityRootCertificate != null;
    chain.ChainPolicy.RevocationMode = options.RevocationMode;
    if (hasConfiguredRoot)
    {
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
    }
        AddIntermediate(chain, options);
        var trusted = chain.Build(certificate);
        var configuredRoot = ResolveCertificate(options.AuthorityRootCertificate, options.AuthorityRootCertificatePath);
        if (configuredRoot == null) return trusted;
        using (configuredRoot)
        {
            var chainRoot = chain.ChainElements.Count == 0 ? null : chain.ChainElements[chain.ChainElements.Count - 1].Certificate;
            var allowedUnknownRoot = chain.ChainStatus.Length == 1 && chain.ChainStatus[0].Status == X509ChainStatusFlags.UntrustedRoot;
            return (trusted || allowedUnknownRoot) && chainRoot != null && string.Equals(chainRoot.Thumbprint, configuredRoot.Thumbprint, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool HasAuthorityChain(EetClientOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.AuthorityRootCertificatePath) || options.AuthorityRootCertificate != null;
    }

    private static void AddIntermediate(X509Chain chain, EetClientOptions options)
    {
        var intermediate = ResolveCertificate(options.AuthorityIntermediateCertificate, options.AuthorityIntermediateCertificatePath);
        if (intermediate != null) chain.ChainPolicy.ExtraStore.Add(intermediate);
    }

    private static X509Certificate2? ResolveCertificate(X509Certificate2? certificate, string? path)
    {
        if (certificate != null) return certificate;
        if (string.IsNullOrWhiteSpace(path)) return null;
#if NET10_0_OR_GREATER
        return X509CertificateLoader.LoadCertificate(System.IO.File.ReadAllBytes(path));
#else
        return new X509Certificate2(path);
#endif
    }

    private sealed class WsuSignedXml : SignedXml
    {
        internal WsuSignedXml(XmlDocument document) : base(document) { }

        public override XmlElement? GetIdElement(XmlDocument? document, string idValue)
        {
            if (document == null) return null;
            var element = base.GetIdElement(document, idValue);
            if (element != null) return element;
            var manager = new XmlNamespaceManager(document.NameTable);
            manager.AddNamespace("wsu", EetSoapEnvelopeBuilder.UtilityNamespace);
            return document.SelectSingleNode("//*[@wsu:Id='" + idValue + "']", manager) as XmlElement;
        }
    }
}
