using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Selpo.Eet20;

/// <summary>Data required to submit one registered sale.</summary>
public sealed class RegisteredSale
{
    /// <summary>Gets or sets the unique identifier for this submission attempt.</summary>
    public Guid MessageId { get; set; } = Guid.NewGuid();
    /// <summary>Gets or sets the submission timestamp including its UTC offset.</summary>
    public DateTimeOffset SubmissionTime { get; set; } = DateTimeOffset.Now;
    /// <summary>Gets or sets whether this is the first submission of the sale.</summary>
    public bool FirstSubmission { get; set; } = true;
    /// <summary>Gets or sets whether the message uses verification mode.</summary>
    public bool VerificationMode { get; set; }
    /// <summary>Gets or sets the taxpayer EIC.</summary>
    public string Eic { get; set; } = string.Empty;
    /// <summary>Gets or sets the optional authorizing taxpayer EIC.</summary>
    public string? AuthorizingEic { get; set; }
    /// <summary>Gets or sets the optional multiple-taxpayer authorization flag.</summary>
    public bool? MultipleTaxpayerAuthorization { get; set; }
    /// <summary>Gets or sets the registering unit identifier.</summary>
    public int UnitId { get; set; }
    /// <summary>Gets or sets the point-of-sale identifier.</summary>
    public string PosId { get; set; } = string.Empty;
    /// <summary>Gets or sets the sale transaction sequence number.</summary>
    public string TransactionNumber { get; set; } = string.Empty;
    /// <summary>Gets or sets the transaction timestamp including its UTC offset.</summary>
    public DateTimeOffset TransactionTime { get; set; }
    /// <summary>Gets or sets the total sale amount in CZK.</summary>
    public decimal TotalAmount { get; set; }
    /// <summary>Gets or sets the optional amount intended for later settlement.</summary>
    public decimal? IntendedSettlementAmount { get; set; }
    /// <summary>Gets or sets the optional amount subsequently settled.</summary>
    public decimal? SettledAmount { get; set; }

    internal IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        ValidatePattern(errors, Eic, "Eic", @"^CZ[0-9]{8,10}$");
        ValidateOptionalPattern(errors, AuthorizingEic, "AuthorizingEic", @"^CZ[0-9]{8,10}$");
        ValidatePattern(errors, PosId, "PosId", @"^[0-9a-zA-Z\.,:;/#\-_ ]{1,20}$");
        ValidatePattern(errors, TransactionNumber, "TransactionNumber", @"^[0-9a-zA-Z\.,:;/#\-_ ]{1,25}$");

        if (UnitId < 1 || UnitId > 999999999)
            errors.Add("UnitId must be between 1 and 999999999.");
        if (MessageId == Guid.Empty)
            errors.Add("MessageId must not be empty.");

        ValidateAmount(errors, TotalAmount, "TotalAmount", required: true);
        ValidateAmount(errors, IntendedSettlementAmount, "IntendedSettlementAmount", required: false);
        ValidateAmount(errors, SettledAmount, "SettledAmount", required: false);
        return errors;
    }

    internal static string FormatAmount(decimal value, string name)
    {
        if (decimal.Round(value, 2) != value || value <= -100000000m || value >= 100000000m)
            throw new EetValidationException($"{name} must have exactly two decimals and be between -100000000 and 100000000.");
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static void ValidateAmount(List<string> errors, decimal? value, string name, bool required)
    {
        if (!value.HasValue)
        {
            if (required) errors.Add($"{name} is required.");
            return;
        }
        if (decimal.Round(value.Value, 2) != value.Value || value.Value <= -100000000m || value.Value >= 100000000m)
            errors.Add($"{name} must have exactly two decimals and be between -100000000 and 100000000.");
    }

    private static void ValidatePattern(List<string> errors, string value, string name, string pattern)
    {
        if (string.IsNullOrEmpty(value) || !Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant))
            errors.Add($"{name} has an invalid format.");
    }

    private static void ValidateOptionalPattern(List<string> errors, string? value, string name, string pattern)
    {
        if (value != null) ValidatePattern(errors, value, name, pattern);
    }
}
