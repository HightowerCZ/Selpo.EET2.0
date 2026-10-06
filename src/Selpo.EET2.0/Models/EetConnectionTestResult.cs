using System;

namespace Selpo.Eet20;

/// <summary>
/// Outcome of <see cref="EetClient.TestConnectionAsync(System.Threading.CancellationToken)"/>, describing whether the configured endpoint,
/// TLS connection, and signing certificate are usable. Authority acknowledgement trust is only
/// tested when a signed acknowledgement is received.
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
    /// Gets whether the configured verification-mode message was accepted by the EET service
    /// with an acknowledgement or verification result code 0. Other EET rejections are failures.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets whether a signed acknowledgement passed the configured authority certificate policy.
    /// False for verification code 0, which does not exercise acknowledgement signature or trust validation.
    /// </summary>
    public bool IsAcknowledgementTrustValidated => IsSuccess && Response is EetAcknowledgementResponse;

    /// <summary>Gets a human-readable summary of the outcome.</summary>
    public string Message { get; }

    /// <summary>Gets the EET response received, when the request reached the service.</summary>
    public EetResponse? Response { get; }

    /// <summary>Gets the exception that caused the test to fail, when applicable.</summary>
    public Exception? Exception { get; }

    internal static EetConnectionTestResult Success(EetResponse response, string message) => new(true, message, response, exception: null);

    internal static EetConnectionTestResult Failure(string message, Exception exception) => new(false, message, response: null, exception);
}
