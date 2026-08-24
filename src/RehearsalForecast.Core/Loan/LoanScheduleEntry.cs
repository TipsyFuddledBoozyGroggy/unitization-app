namespace RehearsalForecast.Core.Loan;

/// <summary>
/// A single month's row in the 36-month loan amortization schedule.
/// </summary>
/// <remarks>
/// <para>
/// When the loan term is shorter than 36 months, rows past payoff are all-zero
/// (Requirement 11.10). When the loan term is longer than 36 months,
/// <see cref="EndingBalance"/> at month 36 is positive (Requirement 11.11).
/// </para>
/// <para>Every monetary field is <see cref="decimal"/>; only <see cref="Month"/> is <see cref="int"/>.</para>
/// </remarks>
/// <param name="Month">1-based month index in <c>[1, 36]</c>.</param>
/// <param name="BeginningBalance">Outstanding loan principal at the start of the month.</param>
/// <param name="Payment">Constant monthly payment amount for the month; zero after payoff or when Loan_Proceeds is zero.</param>
/// <param name="Interest">Interest portion of the payment: <c>BeginningBalance × (annualRate / 12)</c>.</param>
/// <param name="Principal">Principal portion of the payment, never exceeding <see cref="BeginningBalance"/> (Requirement 11.9).</param>
/// <param name="EndingBalance">Outstanding loan principal at the end of the month: <c>BeginningBalance − Principal</c>.</param>
public sealed record LoanScheduleEntry(
    int Month,
    decimal BeginningBalance,
    decimal Payment,
    decimal Interest,
    decimal Principal,
    decimal EndingBalance);
