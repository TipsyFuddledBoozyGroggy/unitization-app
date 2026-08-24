namespace RehearsalForecast.Core.Domain;

/// <summary>
/// User-supplied loan parameters. <c>Loan_Proceeds</c> is derived
/// (<c>Max(Total_Capital - Owner_Investment, 0)</c>, Requirement 10.1) and is NOT
/// a member of this record.
/// </summary>
/// <param name="AnnualLoanInterestRate">
/// Annual nominal loan interest rate as a <see cref="decimal"/> in the inclusive
/// range <c>[0, 1]</c> (Requirements 2.5, 11.3). Contract only, not enforced by
/// the type.
/// </param>
/// <param name="LoanTermMonths">Loan amortization term in months. Strictly positive (Requirement 2.4).</param>
public sealed record LoanInputs(
    decimal AnnualLoanInterestRate,
    int LoanTermMonths);
