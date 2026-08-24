namespace RehearsalForecast.Core.Loan;

/// <summary>
/// Contract for the loan-amortization component (design §4.2, Requirement 11).
/// Implementations produce a fixed-length 36-month <see cref="LoanSchedule"/>
/// for the supplied <c>Loan_Proceeds</c>, <c>Annual_Loan_Interest_Rate</c>,
/// and <c>Loan_Term_Months</c>.
/// </summary>
/// <remarks>
/// <para>
/// The interface exists solely to allow <c>ForecastCalculator</c> to be tested
/// against a stub schedule (design §4.2, §DI-and-interfaces policy). Consumers
/// should treat the returned schedule's shape as invariant:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="LoanSchedule.Entries"/> is always exactly 36 rows (Requirement 11.12 wording).</description></item>
///   <item><description>Rows past <c>Loan_Term_Months</c> are all-zero when the term is ≤ 36 (Requirement 11.10).</description></item>
///   <item><description><see cref="LoanScheduleEntry.EndingBalance"/> at month 36 is positive when the term exceeds 36 (Requirement 11.11).</description></item>
///   <item><description>All monetary values are <see cref="decimal"/>; no <see cref="double"/>/<see cref="float"/> conversion is permitted anywhere in the pipeline (Requirement 19).</description></item>
/// </list>
/// </remarks>
public interface ILoanCalculator
{
    /// <summary>
    /// Produces a 36-month amortization schedule.
    /// </summary>
    /// <param name="loanProceeds">
    /// The <c>Loan_Proceeds</c> amount in USD; must be ≥ 0. When zero, every row
    /// is <c>(m, 0, 0, 0, 0, 0)</c> and <see cref="LoanSchedule.MonthlyPayment"/>
    /// is <c>0</c> (Requirement 11.1).
    /// </param>
    /// <param name="annualInterestRate">
    /// The <c>Annual_Loan_Interest_Rate</c>; must be ≥ 0. When zero and
    /// <paramref name="loanProceeds"/> is positive the schedule uses linear
    /// amortization <c>Loan_Proceeds / Loan_Term_Months</c> (Requirement 11.2);
    /// otherwise the standard fully-amortizing fixed-payment formula applies
    /// (Requirement 11.3).
    /// </param>
    /// <param name="loanTermMonths">
    /// The <c>Loan_Term_Months</c>; must be &gt; 0. Rows past
    /// <paramref name="loanTermMonths"/> are all-zero when the term is ≤ 36
    /// (Requirement 11.10); the schedule shows the outstanding residual at
    /// month 36 when the term exceeds 36 (Requirement 11.11).
    /// </param>
    /// <returns>The 36-month <see cref="LoanSchedule"/>.</returns>
    LoanSchedule Compute(
        decimal loanProceeds,
        decimal annualInterestRate,
        int loanTermMonths);
}
