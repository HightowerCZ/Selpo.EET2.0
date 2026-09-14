using System;
using Selpo.Eet20;
using Selpo.Eet20.Serialization;
using Xunit;

namespace Selpo.Eet20.Tests;

public sealed class EetMessageSerializerTests
{
    [Fact]
    public void Serializes_registered_sale_using_protocol_names_and_formats()
    {
        var sale = new RegisteredSale
        {
            MessageId = Guid.Parse("e23e5a5a-08d7-4a08-844d-2b6c6b60621d"),
            SubmissionTime = new DateTimeOffset(2027, 1, 8, 21, 19, 40, TimeSpan.FromHours(1)),
            FirstSubmission = true,
            Eic = "CZ8551015704",
            UnitId = 181,
            PosId = "00/2535/CN58",
            TransactionNumber = "0/2482/IE25",
            TransactionTime = new DateTimeOffset(2027, 1, 7, 22, 1, 0, TimeSpan.FromHours(1)),
            TotalAmount = 87988m
        };

        var xml = EetMessageSerializer.SerializeRegisteredSale(sale);

        Assert.Contains("<tns:Trzba xmlns:tns=\"http://fs.gov.cz/eet/schema/v4\">", xml);
        Assert.Contains("uuid_zpravy=\"e23e5a5a-08d7-4a08-844d-2b6c6b60621d\"", xml);
        Assert.Contains("celk_trzba=\"87988.00\"", xml);
        Assert.DoesNotContain("eic_poverujiciho", xml);
    }

    [Fact]
    public void Rejects_invalid_sale_before_serialization()
    {
        var sale = new RegisteredSale { Eic = "invalid", TotalAmount = 1.234m };

        Assert.Throws<EetValidationException>(() => EetMessageSerializer.SerializeRegisteredSale(sale));
    }
}
