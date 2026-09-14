using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using Selpo.Eet20.Security;

namespace Selpo.Eet20.Serialization;

internal static class EetResponseParser
{
    internal static EetResponse Parse(string envelopeXml, string? globalTransactionId, EetClientOptions options)
    {
        if (envelopeXml == null) throw new ArgumentNullException(nameof(envelopeXml));
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(envelopeXml);
        var body = document.GetElementsByTagName("Body", EetSoapEnvelopeBuilder.SoapNamespace).Item(0) as XmlElement
            ?? throw new EetProtocolException("The response does not contain a SOAP body.");
        var fault = FindChild(body, "Fault");
        if (fault != null)
            throw new EetProtocolException("The EET service returned a SOAP fault: " + fault.InnerText.Trim());
        var response = FindChild(body, "Odpoved") ?? throw new EetProtocolException("The response does not contain an EET Odpoved element.");
        var header = FindChild(response, "Hlavicka") ?? throw new EetProtocolException("The response does not contain an EET header.");
        var messageId = ParseGuid(GetAttribute(header, "uuid_zpravy"));
        var warnings = ParseWarnings(response);
        var acknowledgement = FindChild(response, "Potvrzeni");
        if (acknowledgement != null)
        {
            EetMessageSignatureValidator.ValidateAcknowledgement(envelopeXml, options);
            var pok = GetRequiredAttribute(acknowledgement, "pok");
            return new EetAcknowledgementResponse(messageId, ParseDateTime(GetAttribute(header, "dat_prij")), pok, ParseBoolean(acknowledgement, "test"), warnings, globalTransactionId);
        }

        var error = FindChild(response, "Chyba") ?? throw new EetProtocolException("The response contains neither acknowledgement nor error data.");
        var code = int.Parse(GetRequiredAttribute(error, "kod"), CultureInfo.InvariantCulture);
        return new EetErrorResponse(messageId, ParseDateTime(GetAttribute(header, "dat_odmit")), code, error.InnerText.Trim(), ParseBoolean(error, "test"), warnings, globalTransactionId);
    }

    private static IReadOnlyList<EetWarning> ParseWarnings(XmlElement response)
    {
        var warnings = new List<EetWarning>();
        foreach (XmlNode node in response.ChildNodes)
        {
            if (node is XmlElement warning && warning.LocalName == "Varovani")
            {
                var code = int.Parse(GetRequiredAttribute(warning, "kod_varov"), CultureInfo.InvariantCulture);
                warnings.Add(new EetWarning(code, warning.InnerText.Trim()));
            }
        }
        return warnings;
    }

    private static XmlElement? FindChild(XmlElement parent, string localName)
    {
        foreach (XmlNode node in parent.ChildNodes)
            if (node is XmlElement element && element.LocalName == localName) return element;
        return null;
    }

    private static string? GetAttribute(XmlElement element, string name) => element.HasAttribute(name) ? element.GetAttribute(name) : null;
    private static string GetRequiredAttribute(XmlElement element, string name) => GetAttribute(element, name) ?? throw new EetProtocolException($"The response is missing {name}.");
    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var result) ? result : null;
    private static DateTimeOffset? ParseDateTime(string? value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result) ? result : null;
    private static bool ParseBoolean(XmlElement element, string name) => bool.TryParse(GetAttribute(element, name), out var result) && result;
}
