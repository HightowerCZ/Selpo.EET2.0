using System;
using System.Collections.Generic;

namespace Selpo.Eet20;

/// <summary>Base type for an EET service response.</summary>
public abstract class EetResponse
{
    internal EetResponse(string? globalTransactionId) => GlobalTransactionId = globalTransactionId;

    /// <summary>Gets the authority transaction identifier, when supplied.</summary>
    public string? GlobalTransactionId { get; }
}

/// <summary>Successful acknowledgement returned by the EET service.</summary>
public sealed class EetAcknowledgementResponse : EetResponse
{
    internal EetAcknowledgementResponse(Guid? messageId, DateTimeOffset? receivedAt, string pok, bool isTest, IReadOnlyList<EetWarning> warnings, string? globalTransactionId)
        : base(globalTransactionId)
    {
        MessageId = messageId;
        ReceivedAt = receivedAt;
        Pok = pok;
        IsTestEnvironment = isTest;
        Warnings = warnings;
    }

    /// <summary>Gets the submitted message identifier.</summary>
    public Guid? MessageId { get; }
    /// <summary>Gets the authority receipt timestamp.</summary>
    public DateTimeOffset? ReceivedAt { get; }
    /// <summary>Gets the acknowledgement code.</summary>
    public string Pok { get; }
    /// <summary>Gets whether the response came from the playground.</summary>
    public bool IsTestEnvironment { get; }
    /// <summary>Gets non-critical warnings.</summary>
    public IReadOnlyList<EetWarning> Warnings { get; }
}

/// <summary>Error or verification result returned by the EET service.</summary>
public sealed class EetErrorResponse : EetResponse
{
    internal EetErrorResponse(Guid? messageId, DateTimeOffset? rejectedAt, int errorCode, string errorMessage, bool isTest, IReadOnlyList<EetWarning> warnings, string? globalTransactionId)
        : base(globalTransactionId)
    {
        MessageId = messageId;
        RejectedAt = rejectedAt;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        IsTestEnvironment = isTest;
        Warnings = warnings;
    }

    /// <summary>Gets the original message identifier when supplied.</summary>
    public Guid? MessageId { get; }
    /// <summary>Gets the rejection timestamp when supplied.</summary>
    public DateTimeOffset? RejectedAt { get; }
    /// <summary>Gets the EET error code.</summary>
    public int ErrorCode { get; }
    /// <summary>Gets the EET error text.</summary>
    public string ErrorMessage { get; }
    /// <summary>Gets whether the response came from the playground.</summary>
    public bool IsTestEnvironment { get; }
    /// <summary>Gets non-critical warnings.</summary>
    public IReadOnlyList<EetWarning> Warnings { get; }
}

/// <summary>A non-critical EET warning.</summary>
public sealed class EetWarning
{
    internal EetWarning(int code, string message) { Code = code; Message = message; }
    /// <summary>Gets the warning code.</summary>
    public int Code { get; }
    /// <summary>Gets the warning message.</summary>
    public string Message { get; }
}
