using System.ComponentModel.DataAnnotations;
using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Web.ViewModels;

/// <summary>
/// View-model section for forecast-wide control values: the opening cash
/// balance and the target cash-positive month (Requirements 13.2, 14.1).
/// </summary>
public sealed class ForecastControlInputSection
{
    /// <summary>
    /// Opening cash balance used as <c>Beginning_Cash[1]</c> in the cash-flow
    /// roll-forward (Requirement 13.2). Nonnegative amount in USD.
    /// </summary>
    [Display(Name = "Beginning Cash (Month 1)")]
    [Range(0.0, double.MaxValue, ErrorMessage = "Beginning Cash (Month 1) must be zero or greater.")]
    public decimal BeginningCashMonth1 { get; set; }

    /// <summary>
    /// Month the user must be cash-positive by; integer in the inclusive
    /// range <c>[1, 36]</c> (Requirements 2.8, 14.1).
    /// </summary>
    [Display(Name = "Target Cash-Positive Month")]
    [Range(1, 36, ErrorMessage = "Target Cash-Positive Month must be an integer between 1 and 36.")]
    public int TargetCashPositiveMonth { get; set; } = 36;

    /// <summary>Maps this section to the domain <see cref="ForecastControlInputs"/> record.</summary>
    public ForecastControlInputs ToDomain() =>
        new(BeginningCashMonth1, TargetCashPositiveMonth);
}
