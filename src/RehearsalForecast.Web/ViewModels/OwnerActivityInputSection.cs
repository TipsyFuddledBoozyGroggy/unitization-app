using System.ComponentModel.DataAnnotations;
using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Web.ViewModels;

/// <summary>
/// View-model section for owner-initiated capital movements (Requirement 10).
/// <see cref="OwnerWithdrawals"/> is a single scalar applied uniformly to every
/// month (Requirement 1.6, Design Decision 8) and does NOT expose a
/// variable-mode toggle.
/// </summary>
public sealed class OwnerActivityInputSection
{
    /// <summary>
    /// Owner's cash contribution received in Month 1 only (Requirement 10.3).
    /// Nonnegative amount in USD. May exceed <c>Total_Capital</c> without
    /// invalidating the submission (Requirement 10.5).
    /// </summary>
    [Display(Name = "Owner Investment")]
    [Range(0.0, double.MaxValue, ErrorMessage = "Owner Investment must be zero or greater.")]
    public decimal OwnerInvestment { get; set; }

    /// <summary>
    /// Constant per-month owner withdrawal, applied uniformly Month 1 through
    /// Month 36 (Requirements 1.6, 13.6). Nonnegative amount in USD.
    /// </summary>
    [Display(Name = "Owner Withdrawals")]
    [Range(0.0, double.MaxValue, ErrorMessage = "Owner Withdrawals must be zero or greater.")]
    public decimal OwnerWithdrawals { get; set; }

    /// <summary>Maps this section to the domain <see cref="OwnerActivityInputs"/> record.</summary>
    public OwnerActivityInputs ToDomain() =>
        new(OwnerInvestment, OwnerWithdrawals);
}
