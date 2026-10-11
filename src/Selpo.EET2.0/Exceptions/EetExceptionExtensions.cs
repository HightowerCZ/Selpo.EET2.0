using System;

namespace Selpo.Eet20;

/// <summary>Retrieves XML captured by the EET client when an operation fails.</summary>
public static class EetExceptionExtensions
{
    private const string RequestXmlKey = "Selpo.Eet20.RequestXml";
    private const string ResponseXmlKey = "Selpo.Eet20.ResponseXml";

    /// <summary>Gets the signed request XML if it was generated before the failure; otherwise null.</summary>
    public static string? GetRequestXml(this Exception exception)
    {
        if (exception == null) throw new ArgumentNullException(nameof(exception));
        return exception switch
        {
            EetTransportException transport => transport.RequestXml,
            EetProtocolException protocol => protocol.RequestXml,
            _ => exception.Data[RequestXmlKey] as string
        };
    }

    /// <summary>Gets the raw response body if it was received before the failure; otherwise null.</summary>
    public static string? GetResponseXml(this Exception exception)
    {
        if (exception == null) throw new ArgumentNullException(nameof(exception));
        return exception switch
        {
            EetTransportException transport => transport.ResponseXml,
            EetProtocolException protocol => protocol.ResponseXml,
            _ => exception.Data[ResponseXmlKey] as string
        };
    }

    internal static void AttachXml(Exception exception, string? requestXml, string? responseXml)
    {
        if (requestXml != null) exception.Data[RequestXmlKey] = requestXml;
        if (responseXml != null) exception.Data[ResponseXmlKey] = responseXml;
    }
}
