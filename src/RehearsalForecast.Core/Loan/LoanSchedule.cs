namespace RehearsalForecast.Core.Loan;

/// <summary>
/// The 36-month loan amortization schedule computed by <c>LoanCalculator</c>.
/// </summary>
/// <remarks>
/// <see cref="Entries"/> is always exactly 36 rows regardless of Loan_Term_Months
/// or Loan_Proceeds (Requirement 11.12, design §7). The producer is responsible for
/// upholding this contract; consumers may treat the length as invariant.
/// </remarks>
/// <param name="MonthlyPayment">Constant loan payment amount. Zero when Loan_Proceeds is zero.</param>
/// <param name="Entries">Exactly 36 amortization rows ordered by <see cref="LoanScheduleEntry.Month"/> ascending.</param>
public sealed record LoanSchedule(
    decimal MonthlyPayment,
    IReadOnlyList<LoanScheduleEntry> Entries);
