using System;

namespace Selpo.Eet20;

/// <summary>Thrown when a registered sale cannot satisfy the EET wire contract.</summary>
public sealed class EetValidationException : ArgumentException
{
    /// <summary>Initializes a validation exception with the supplied message.</summary>
    public EetValidationException(string message)
        : base(message)
    {
    }
}
