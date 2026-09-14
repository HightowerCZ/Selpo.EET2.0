using System;
using System.IO;
using System.Threading.Tasks;
using Selpo.Eet20;
using Xunit;

namespace Selpo.Eet20.Tests;

public sealed class PlaygroundIntegrationTests
{
    [Fact]
    public async Task Verification_message_is_accepted_by_playground()
    {
        if (!TryCreatePlaygroundOptions(out var options))
            return;

        using var client = new EetClient(options);

        var response = await client.RegisterSaleAsync(new RegisteredSale
        {
            Eic = "CZ8551015704",
            UnitId = 181,
            PosId = "00/2535/CN58",
            TransactionNumber = "integration-test-1",
            SubmissionTime = DateTimeOffset.Now,
            TransactionTime = DateTimeOffset.Now,
            TotalAmount = 1.00m,
            VerificationMode = true,
            FirstSubmission = true
        });

        var error = Assert.IsType<EetErrorResponse>(response);
        Assert.Equal(0, error.ErrorCode);
    }

    [Fact]
    public void Configuration_passes_local_validation_against_playground_options()
    {
        if (!TryCreatePlaygroundOptions(out var options))
            return;

        var exception = Record.Exception(() => options.Validate());

        Assert.Null(exception);
    }

    [Fact]
    public async Task TestConnectionAsync_succeeds_against_playground()
    {
        if (!TryCreatePlaygroundOptions(out var options))
            return;

        using var client = new EetClient(options);

        var result = await client.TestConnectionAsync();

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(result.Response);
        Assert.Null(result.Exception);
    }

    private static bool TryCreatePlaygroundOptions(out EetClientOptions options)
    {
        options = null!;
        var certificatePath = Environment.GetEnvironmentVariable("EET_PLAYGROUND_CERTIFICATE_PATH");
        if (!string.Equals(Environment.GetEnvironmentVariable("EET_RUN_PLAYGROUND"), "true", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(certificatePath))
            return false;

        var password = Environment.GetEnvironmentVariable("EET_PLAYGROUND_CERTIFICATE_PASSWORD")
            ?? throw new InvalidOperationException("EET_PLAYGROUND_CERTIFICATE_PASSWORD is required when EET_RUN_PLAYGROUND=true.");
        var rootCertificatePath = Environment.GetEnvironmentVariable("EET_PLAYGROUND_ROOT_CERTIFICATE_PATH");
        var intermediateCertificatePath = Environment.GetEnvironmentVariable("EET_PLAYGROUND_INTERMEDIATE_CERTIFICATE_PATH");
        if (!File.Exists(certificatePath))
            throw new FileNotFoundException("The configured playground certificate was not found.", certificatePath);

        options = new EetClientOptions
        {
            BaseAddress = "https://pg.trzbyeet.gov.cz:443/eet/services/EETServiceSOAP/v4",
            SigningCertificatePath = certificatePath,
            SigningCertificatePassword = password,
            UseSystemCertificateTrust = string.IsNullOrWhiteSpace(rootCertificatePath),
            AuthorityRootCertificatePath = rootCertificatePath,
            AuthorityIntermediateCertificatePath = intermediateCertificatePath
        };
        return true;
    }
}

