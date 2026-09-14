using System;

namespace Selpo.Eet20;

/// <summary>
/// Describes one automatic resend attempt performed by <see cref="EetClient.RegisterSaleAsync"/>
/// when <see cref="EetClientOptions.EnableAutomaticResend"/> is enabled and the EET service returns
/// a temporary technical error (error code -1).
/// </summary>
public sealed class EetResendAttempt
{
    internal EetResendAttempt(int attemptNumber, int maxAttempts, TimeSpan delay, EetResponse response, bool isFinalAttempt)
    {
        AttemptNumber = attemptNumber;
        MaxAttempts = maxAttempts;
        Delay = delay;
        Response = response;
        IsFinalAttempt = isFinalAttempt;
    }

    /// <summary>Gets the 1-based number of the submission attempt that produced <see cref="Response"/>.</summary>
    public int AttemptNumber { get; }

    /// <summary>Gets the maximum number of attempts allowed by the configured resend schedule (initial submission plus resends).</summary>
    public int MaxAttempts { get; }

    /// <summary>Gets the delay that was awaited before this attempt, or <see cref="TimeSpan.Zero"/> for the initial submission or the final exhausted attempt.</summary>
    public TimeSpan Delay { get; }

    /// <summary>Gets the response received for this attempt.</summary>
    public EetResponse Response { get; }

    /// <summary>
    /// Gets whether this was the last attempt allowed by the resend schedule and the sale is still not
    /// registered. When <c>true</c>, no further automatic resends will be made and the caller must decide
    /// how to handle the persistent failure.
    /// </summary>
    public bool IsFinalAttempt { get; }
}
