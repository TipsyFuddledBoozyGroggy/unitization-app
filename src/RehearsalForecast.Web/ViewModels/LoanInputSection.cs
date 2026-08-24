using System.ComponentModel.DataAnnotations;
using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Web.ViewModels;

/// <summary>
/// View-model section for loan interest rate and term (Requirement 11).
/// <c>Loan_Proceeds</c> is derived (<c>Max(Total_Capital - Owner_Investment, 0)</c>,
/// Requirement 10.1) and is NOT a user input.
/// </summary>
public sealed class LoanInputSection
{
    /// <summary>
    /// Annual nominal loan interest rate as a nonnegative <see cref="decimal"/>
    /// (Requirement 2.5, Design §10.2). Values greater than <c>1</c> are
    /// accepted at the attribute layer; upstream domain semantics interpret
    /// this as an annualized nominal rate (e.g., <c>0.065</c> = 6.5% per year).
    /// </summary>
    [Display(Name = "Annual Loan Interest Rate")]
    [Range(0.0, double.MaxValue, ErrorMessage = "Annual Loan Interest Rate must be zero or greater.")]
    public decimal AnnualLoanInterestRate { get; set; }

    /// <summary>Loan amortization term in months. Strictly positive (Requirement 2.4).</summary>
    [Display(Name = "Loan Term (Months)")]
    [Range(1, int.MaxValue, ErrorMessage = "Loan Term must be at least 1 month.")]
    public int LoanTermMonths { get; set; } = 1;

    /// <summary>Maps this section to the domain <see cref="LoanInputs"/> record.</summary>
    public LoanInputs ToDomain() =>
        new(AnnualLoanInterestRate, LoanTermMonths);
}
