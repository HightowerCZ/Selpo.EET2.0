using System;

namespace Selpo.Eet20;

/// <summary>Thrown when an EET response violates the expected protocol.</summary>
public sealed class EetProtocolException : InvalidOperationException
{
    /// <summary>Initializes a protocol exception with the supplied message.</summary>
    public EetProtocolException(string message) : base(message) { }
}
