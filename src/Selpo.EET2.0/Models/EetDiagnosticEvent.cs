using System;

namespace Selpo.Eet20;

/// <summary>Categorizes a diagnostic event raised by <see cref="EetClient"/>.</summary>
public enum EetDiagnosticEventKind
{
    /// <summary>A message is about to be sent (or resent) to the EET service.</summary>
    Sending,
    /// <summary>A message was successfully acknowledged or rejected at the EET protocol level.</summary>
    ResponseReceived,
    /// <summary>A temporary error caused an automatic resend to be scheduled.</summary>
    ResendScheduled,
    /// <summary>The operation failed with a transport, protocol, or signing exception.</summary>
    Failed
}

/// <summary>
/// A single diagnostic event describing progress of a <see cref="EetClient.RegisterSaleAsync"/> or
/// <see cref="EetClient.TestConnectionAsync"/> call. Subscribe via <see cref="EetClientOptions.OnDiagnosticEvent"/>
/// to observe request lifecycle without taking a dependency on a logging framework.
/// </summary>
public sealed class EetDiagnosticEvent
{
    internal EetDiagnosticEvent(EetDiagnosticEventKind kind, string message, Guid messageId, Exception? exception = null)
    {
        Kind = kind;
        Message = message;
        MessageId = messageId;
        Exception = exception;
    }

    /// <summary>Gets the category of the event.</summary>
    public EetDiagnosticEventKind Kind { get; }

    /// <summary>Gets a human-readable description of the event.</summary>
    public string Message { get; }

    /// <summary>Gets the message identifier (<see cref="RegisteredSale.MessageId"/>) the event relates to.</summary>
    public Guid MessageId { get; }

    /// <summary>Gets the exception associated with the event, when <see cref="Kind"/> is <see cref="EetDiagnosticEventKind.Failed"/>.</summary>
    public Exception? Exception { get; }
}
