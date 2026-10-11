using System;

namespace Selpo.Eet20;

/// <summary>Thrown when an EET response violates the expected protocol.</summary>
public sealed class EetProtocolException : InvalidOperationException
{
    /// <summary>Initializes a protocol exception with the supplied message.</summary>
    /// <param name="message">Description of the protocol failure.</param>
    public EetProtocolException(string message) : this(message, innerException: null, requestXml: null, responseXml: null) { }

    internal EetProtocolException(string message, Exception? innerException, string? requestXml, string? responseXml)
        : base(message, innerException)
    {
        RequestXml = requestXml;
        ResponseXml = responseXml;
    }

    /// <summary>Gets the signed SOAP request XML sent before the protocol failure, when available.</summary>
    public string? RequestXml { get; }

    /// <summary>Gets the raw SOAP response XML that caused the protocol failure, when available.</summary>
    public string? ResponseXml { get; }
}
