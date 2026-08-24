using System.ComponentModel.DataAnnotations;
using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Web.ViewModels;

/// <summary>
/// View-model section for the income tax rate (Requirement 12).
/// <c>Payroll_Tax_Rate</c> is a fixed derived constant (<c>0.0765</c>,
/// Requirement 7.2) and is NOT a user input.
/// </summary>
public sealed class TaxInputSection
{
    /// <summary>
    /// Income tax rate applied to positive monthly pre-tax income only, as a
    /// <see cref="decimal"/> in the inclusive range <c>[0, 1]</c>
    /// (Requirements 2.6, 12.3).
    /// </summary>
    [Display(Name = "Income Tax Rate")]
    [Range(0.0, 1.0, ErrorMessage = "Income Tax Rate must be between 0 and 1.")]
    public decimal IncomeTaxRate { get; set; }

    /// <summary>Maps this section to the domain <see cref="TaxInputs"/> record.</summary>
    public TaxInputs ToDomain() =>
        new(IncomeTaxRate);
}
