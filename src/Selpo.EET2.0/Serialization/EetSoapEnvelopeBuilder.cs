using System;
using System.IO;
using System.Text;
using System.Xml;

namespace Selpo.Eet20.Serialization;

internal static class EetSoapEnvelopeBuilder
{
    internal const string SoapNamespace = "http://schemas.xmlsoap.org/soap/envelope/";
    internal const string UtilityNamespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";

    internal static string Build(string bodyXml, string bodyId)
    {
        if (bodyXml == null) throw new ArgumentNullException(nameof(bodyXml));
        if (string.IsNullOrWhiteSpace(bodyId)) throw new ArgumentException("A body identifier is required.", nameof(bodyId));

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false,
            Indent = false
        };

        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(output, settings))
        {
            writer.WriteStartElement("soap", "Envelope", SoapNamespace);
            writer.WriteAttributeString("xmlns", "wsu", null, UtilityNamespace);
            writer.WriteStartElement("soap", "Header", SoapNamespace);
            writer.WriteEndElement();
            writer.WriteStartElement("soap", "Body", SoapNamespace);
            writer.WriteAttributeString("wsu", "Id", UtilityNamespace, bodyId);
            writer.WriteRaw(StripXmlDeclaration(bodyXml));
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static string StripXmlDeclaration(string xml)
    {
        var declarationEnd = xml.IndexOf("?>", StringComparison.Ordinal);
        return declarationEnd >= 0 && xml.TrimStart().StartsWith("<?xml", StringComparison.Ordinal)
            ? xml.Substring(xml.IndexOf("<?xml", StringComparison.Ordinal) + declarationEnd + 2 - xml.IndexOf("<?xml", StringComparison.Ordinal)).TrimStart()
            : xml;
    }
}
