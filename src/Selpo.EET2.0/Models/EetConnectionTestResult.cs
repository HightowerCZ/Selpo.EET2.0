using System;

namespace Selpo.Eet20;

/// <summary>
/// Outcome of <see cref="EetClient.TestConnectionAsync"/>, describing whether the configured endpoint,
/// TLS/certificate trust, and signing certificate are usable end-to-end.
/// </summary>
public sealed class EetConnectionTestResult
{
    internal EetConnectionTestResult(bool isSuccess, string message, EetResponse? response, Exception? exception)
    {
        IsSuccess = isSuccess;
        Message = message;
        Response = response;
        Exception = exception;
    }

    /// <summary>
    /// Gets whether the connection test succeeded, i.e. a signed verification-mode message reached the
    /// EET service and produced a valid protocol response (either an acknowledgement or an EET-level
    /// rejection - both indicate that transport, TLS trust, and signing were successful).
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a human-readable summary of the outcome.</summary>
    public string Message { get; }

    /// <summary>Gets the EET response received, when the request reached the service.</summary>
    public EetResponse? Response { get; }

    /// <summary>Gets the exception that caused the test to fail, when applicable.</summary>
    public Exception? Exception { get; }

    internal static EetConnectionTestResult Success(EetResponse response, string message) => new(true, message, response, exception: null);

    internal static EetConnectionTestResult Failure(string message, Exception exception) => new(false, message, response: null, exception);
}
