using Selpo.Eet20.Serialization;
using Xunit;

namespace Selpo.Eet20.Tests;

public sealed class EetSoapEnvelopeBuilderTests
{
    [Fact]
    public void Wraps_registered_sale_in_soap_11_body_with_signing_id()
    {
        var envelope = EetSoapEnvelopeBuilder.Build(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><tns:Trzba xmlns:tns=\"http://fs.gov.cz/eet/schema/v4\" />",
            "body-1");

        Assert.Contains("<soap:Envelope", envelope);
        Assert.Contains("<soap:Header", envelope);
        Assert.Contains("<soap:Body wsu:Id=\"body-1\">", envelope);
        Assert.Contains("<tns:Trzba", envelope);
        Assert.DoesNotContain("<?xml", envelope.Substring(envelope.IndexOf("<soap:Body")));
    }
}
