using System;
using System.Net;

namespace Selpo.Eet20;

/// <summary>Thrown when the EET HTTP transport returns a non-success response.</summary>
public sealed class EetTransportException : Exception
{
    /// <summary>Initializes a transport exception with response details.</summary>
    public EetTransportException(HttpStatusCode statusCode, string responseBody, string? globalTransactionId)
        : base($"The EET endpoint returned HTTP {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        GlobalTransactionId = globalTransactionId;
    }

    /// <summary>Gets the HTTP status code.</summary>
    public HttpStatusCode StatusCode { get; }
    /// <summary>Gets the raw response body.</summary>
    public string ResponseBody { get; }
    /// <summary>Gets the authority transaction identifier, when supplied.</summary>
    public string? GlobalTransactionId { get; }
}
