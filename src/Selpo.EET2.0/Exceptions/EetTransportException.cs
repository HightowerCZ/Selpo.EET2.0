using System;
using System.Net;

namespace Selpo.Eet20;

/// <summary>Thrown when the EET HTTP transport returns a non-success response.</summary>
public sealed class EetTransportException : Exception
{
    /// <summary>Initializes a transport exception with response details.</summary>
    /// <param name="statusCode">HTTP status code returned by the EET endpoint.</param>
    /// <param name="responseBody">Raw response body returned by the endpoint.</param>
    /// <param name="globalTransactionId">Optional authority transaction identifier.</param>
    public EetTransportException(HttpStatusCode statusCode, string responseBody, string? globalTransactionId)
        : this(statusCode, responseBody, globalTransactionId, requestXml: null)
    {
    }

    internal EetTransportException(HttpStatusCode statusCode, string responseBody, string? globalTransactionId, string? requestXml)
        : base($"The EET endpoint returned HTTP {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        GlobalTransactionId = globalTransactionId;
        RequestXml = requestXml;
    }

    /// <summary>Gets the HTTP status code.</summary>
    public HttpStatusCode StatusCode { get; }
    /// <summary>Gets the raw response body.</summary>
    public string ResponseBody { get; }
    /// <summary>Gets the signed SOAP request XML sent before this HTTP failure, when available.</summary>
    public string? RequestXml { get; }
    /// <summary>Gets the raw HTTP response body. This is the same value as <see cref="ResponseBody"/>.</summary>
    public string ResponseXml => ResponseBody;
    /// <summary>Gets the authority transaction identifier, when supplied.</summary>
    public string? GlobalTransactionId { get; }
}
