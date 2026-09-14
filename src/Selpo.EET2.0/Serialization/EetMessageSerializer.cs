using System;
using System.IO;
using System.Text;
using System.Xml;

namespace Selpo.Eet20.Serialization;

internal static class EetMessageSerializer
{
    internal const string Namespace = "http://fs.gov.cz/eet/schema/v4";

    internal static string SerializeRegisteredSale(RegisteredSale sale)
    {
        if (sale == null) throw new ArgumentNullException(nameof(sale));

        var validationErrors = sale.Validate();
        if (validationErrors.Count > 0)
            throw new EetValidationException(string.Join(" ", validationErrors));

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false,
            Indent = false
        };

        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(output, settings))
        {
            writer.WriteStartElement("tns", "Trzba", Namespace);
            writer.WriteStartElement("tns", "Hlavicka", Namespace);
            writer.WriteAttributeString("uuid_zpravy", sale.MessageId.ToString("D"));
            writer.WriteAttributeString("dat_odesl", FormatDateTime(sale.SubmissionTime));
            writer.WriteAttributeString("prvni_zaslani", FormatBoolean(sale.FirstSubmission));
            if (sale.VerificationMode)
                writer.WriteAttributeString("overeni", "true");
            writer.WriteEndElement();

            writer.WriteStartElement("tns", "Data", Namespace);
            writer.WriteAttributeString("eic_popl", sale.Eic);
            WriteOptionalAttribute(writer, "eic_poverujiciho", sale.AuthorizingEic);
            WriteOptionalBoolean(writer, "povereni_vice_popl", sale.MultipleTaxpayerAuthorization);
            writer.WriteAttributeString("id_jednotky", sale.UnitId.ToString());
            writer.WriteAttributeString("id_pokl", sale.PosId);
            writer.WriteAttributeString("porad_cis", sale.TransactionNumber);
            writer.WriteAttributeString("dat_trzby", FormatDateTime(sale.TransactionTime));
            writer.WriteAttributeString("celk_trzba", RegisteredSale.FormatAmount(sale.TotalAmount, nameof(sale.TotalAmount)));
            WriteOptionalAmount(writer, "urceno_cerp_zuct", sale.IntendedSettlementAmount, nameof(sale.IntendedSettlementAmount));
            WriteOptionalAmount(writer, "cerp_zuct", sale.SettledAmount, nameof(sale.SettledAmount));
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static string FormatDateTime(DateTimeOffset value)
    {
        return value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatBoolean(bool value) => value ? "true" : "false";

    private static void WriteOptionalAttribute(XmlWriter writer, string name, string? value)
    {
        if (value != null) writer.WriteAttributeString(name, value);
    }

    private static void WriteOptionalBoolean(XmlWriter writer, string name, bool? value)
    {
        if (value.HasValue) writer.WriteAttributeString(name, FormatBoolean(value.Value));
    }

    private static void WriteOptionalAmount(XmlWriter writer, string name, decimal? value, string valueName)
    {
        if (value.HasValue) writer.WriteAttributeString(name, RegisteredSale.FormatAmount(value.Value, valueName));
    }
}
