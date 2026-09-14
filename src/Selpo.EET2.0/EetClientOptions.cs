using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace Selpo.Eet20;

/// <summary>
/// Configuration options for <see cref="EetClient"/>.
/// </summary>
public sealed class EetClientOptions
{
    /// <summary>
    /// Gets or sets the base address of the EET 2.0 server.
    /// </summary>
    public string? BaseAddress { get; set; }

    /// <summary>
    /// Gets or sets the certificate used to sign registered sale messages.
    /// </summary>
    public X509Certificate2? SigningCertificate { get; set; }

    /// <summary>
    /// Gets or sets the path to a certificate file used to sign messages.
    /// </summary>
    public string? SigningCertificatePath { get; set; }

    /// <summary>
    /// Gets or sets the password for <see cref="SigningCertificatePath"/> when required.
    /// </summary>
    public string? SigningCertificatePassword { get; set; }

    /// <summary>
    /// Gets or sets whether authority acknowledgement certificates use the OS trust store.
    /// </summary>
    public bool UseSystemCertificateTrust { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional pinned authority certificate or root certificate.
    /// </summary>
    public X509Certificate2? PinnedAuthorityCertificate { get; set; }

    /// <summary>Gets or sets the authority root certificate.</summary>
    public X509Certificate2? AuthorityRootCertificate { get; set; }

    /// <summary>Gets or sets the path to the authority root certificate.</summary>
    public string? AuthorityRootCertificatePath { get; set; }

    /// <summary>Gets or sets the authority subordinate certificate.</summary>
    public X509Certificate2? AuthorityIntermediateCertificate { get; set; }

    /// <summary>Gets or sets the path to the authority subordinate certificate.</summary>
    public string? AuthorityIntermediateCertificatePath { get; set; }

    /// <summary>
    /// Gets or sets the revocation checking mode used when building the authority certificate chain during
    /// acknowledgement signature validation. Defaults to <see cref="X509RevocationMode.Online"/>, matching
    /// the framework default for <see cref="X509Chain"/>. Set to <see cref="X509RevocationMode.NoCheck"/> to
    /// skip revocation checks entirely (for example in offline/air-gapped environments), or
    /// <see cref="X509RevocationMode.Offline"/> to use a locally cached CRL.
    /// </summary>
    public X509RevocationMode RevocationMode { get; set; } = X509RevocationMode.Online;

    /// <summary>
    /// Gets or sets whether <see cref="EetClient.RegisterSaleAsync"/> should automatically resend a
    /// registered sale data message when the EET service returns error code -1 ("temporary technical
    /// error in processing - please resend the data message later", as per the EET 2.0 data interface
    /// specification). When enabled, resends follow the schedule configured in <see cref="ResendDelays"/>.
    /// Each resend uses a newly generated message UUID and <c>FirstSubmission</c> set to <c>false</c>,
    /// as required by the specification. Disabled by default.
    /// </summary>
    public bool EnableAutomaticResend { get; set; }

    /// <summary>
    /// Gets or sets the delay before each automatic resend attempt when <see cref="EnableAutomaticResend"/>
    /// is enabled. The number of entries determines the maximum number of resend attempts (e.g. three
    /// entries allow up to three resends after the initial submission). This must be supplied explicitly
    /// by the developer - there is no built-in default resend schedule - and must contain at least one
    /// entry when <see cref="EnableAutomaticResend"/> is <c>true</c>.
    /// </summary>
    public IReadOnlyList<TimeSpan>? ResendDelays { get; set; }

    /// <summary>
    /// Gets or sets a callback invoked after every automatic resend attempt made because of a temporary
    /// technical error (error code -1), including the last one if the resend schedule is exhausted and
    /// the failure persists. Use this to log, alert, or otherwise notify the application when a sale is
    /// still not registered after all configured resend attempts.
    /// </summary>
    public Action<EetResendAttempt>? OnResendAttempt { get; set; }

    /// <summary>
    /// Gets or sets how long pooled HTTP connections are kept alive before being re-established. The EET
    /// endpoint uses DNS-based high availability, and its DNS record may change over time (see the EET 2.0
    /// operational documentation); a bounded connection lifetime ensures new connections re-resolve DNS
    /// instead of reusing a stale IP address indefinitely. This setting only applies when <see cref="EetClient"/>
    /// creates its own <see cref="System.Net.Http.HttpClient"/> (i.e. via the <see cref="EetClient(EetClientOptions)"/>
    /// constructor); it has no effect when an application-supplied <see cref="System.Net.Http.HttpClient"/> is used
    /// - configure connection lifetime on that client's handler instead. Defaults to 5 minutes.
    /// </summary>
    public TimeSpan HttpConnectionLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets a callback invoked for diagnostic events raised while sending, resending, or testing the
    /// connection (see <see cref="EetDiagnosticEventKind"/>). This is a lightweight, dependency-free hook for
    /// observing request lifecycle (e.g. for logging) without requiring a logging framework reference. It does
    /// not replace exceptions or <see cref="OnResendAttempt"/>; both continue to fire as documented.
    /// </summary>
    public Action<EetDiagnosticEvent>? OnDiagnosticEvent { get; set; }

    /// <summary>
    /// Validates that these options are internally consistent and usable, without making any network call.
    /// This checks that an endpoint is configured (as an absolute HTTPS URL), that a signing certificate is
    /// configured and loadable (and, when supplied via <see cref="SigningCertificatePath"/>, that the file
    /// exists and the certificate has a private key), that pinned authority certificate files exist when
    /// configured, and that the automatic resend settings are consistent. Use
    /// <see cref="EetClient.TestConnectionAsync"/> to additionally verify that the endpoint is reachable and
    /// the certificate/trust chain is accepted by the EET service.
    /// </summary>
    /// <exception cref="EetValidationException">One or more configuration problems were found. The
    /// exception message lists every problem found.</exception>
    public void Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(BaseAddress))
        {
            errors.Add("BaseAddress must be set to the EET service endpoint.");
        }
        else if (!Uri.TryCreate(BaseAddress, UriKind.Absolute, out var endpointUri))
        {
            errors.Add($"BaseAddress '{BaseAddress}' is not a valid absolute URL.");
        }
        else if (!string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"BaseAddress '{BaseAddress}' must use HTTPS.");
        }

        if (SigningCertificate == null && string.IsNullOrWhiteSpace(SigningCertificatePath))
        {
            errors.Add("A signing certificate must be configured via SigningCertificate or SigningCertificatePath.");
        }
        else if (SigningCertificate != null)
        {
            if (!SigningCertificate.HasPrivateKey)
                errors.Add("SigningCertificate does not have a private key required to sign messages.");
            if (DateTime.Now < SigningCertificate.NotBefore || DateTime.Now > SigningCertificate.NotAfter)
                errors.Add($"SigningCertificate is not currently valid (valid from {SigningCertificate.NotBefore:u} to {SigningCertificate.NotAfter:u}).");
        }
        else if (!string.IsNullOrWhiteSpace(SigningCertificatePath) && !System.IO.File.Exists(SigningCertificatePath))
        {
            errors.Add($"SigningCertificatePath '{SigningCertificatePath}' does not exist.");
        }

        ValidatePinnedCertificatePath(errors, AuthorityRootCertificate, AuthorityRootCertificatePath, nameof(AuthorityRootCertificatePath));
        ValidatePinnedCertificatePath(errors, AuthorityIntermediateCertificate, AuthorityIntermediateCertificatePath, nameof(AuthorityIntermediateCertificatePath));

        if (!UseSystemCertificateTrust && PinnedAuthorityCertificate == null && AuthorityRootCertificate == null
            && string.IsNullOrWhiteSpace(AuthorityRootCertificatePath))
        {
            errors.Add("UseSystemCertificateTrust is false, but no pinned authority certificate was configured (PinnedAuthorityCertificate, AuthorityRootCertificate, or AuthorityRootCertificatePath).");
        }

        if (EnableAutomaticResend && (ResendDelays == null || ResendDelays.Count == 0))
        {
            errors.Add("ResendDelays must contain at least one delay when EnableAutomaticResend is true.");
        }

        if (HttpConnectionLifetime <= TimeSpan.Zero)
        {
            errors.Add("HttpConnectionLifetime must be a positive duration.");
        }

        if (errors.Count > 0)
        {
            throw new EetValidationException("Invalid EetClientOptions configuration: " + string.Join(" ", errors));
        }
    }

    private static void ValidatePinnedCertificatePath(List<string> errors, X509Certificate2? certificate, string? path, string pathPropertyName)
    {
        if (certificate == null && !string.IsNullOrWhiteSpace(path) && !System.IO.File.Exists(path))
        {
            errors.Add($"{pathPropertyName} '{path}' does not exist.");
        }
    }
}
