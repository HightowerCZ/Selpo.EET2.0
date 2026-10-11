using System;
using System.Net;
using Xunit;

namespace Selpo.Eet20.Tests;

public sealed class EetExceptionExtensionsTests
{
    [Fact]
    public void Reads_typed_exception_xml()
    {
        var transport = new EetTransportException(HttpStatusCode.BadRequest, "response", null, "request");
        var protocol = new EetProtocolException("failure", null, "request", "response");
        foreach (Exception exception in new Exception[] { transport, protocol })
        {
            Assert.Equal("request", exception.GetRequestXml());
            Assert.Equal("response", exception.GetResponseXml());
        }
    }

    [Fact]
    public void Returns_null_for_uncaptured_xml_and_rejects_null_exception()
    {
        var exception = new Exception();
        Assert.Null(exception.GetRequestXml());
        Assert.Null(exception.GetResponseXml());
        Assert.Throws<ArgumentNullException>(() => EetExceptionExtensions.GetRequestXml(null!));
        Assert.Throws<ArgumentNullException>(() => EetExceptionExtensions.GetResponseXml(null!));
    }
}
