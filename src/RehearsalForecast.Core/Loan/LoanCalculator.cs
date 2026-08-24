using RehearsalForecast.Core.Constants;

namespace RehearsalForecast.Core.Loan;

/// <summary>
/// Produces the 36-month loan amortization schedule for the rehearsal-forecast
/// engine (design §7, Requirement 11).
/// </summary>
/// <remarks>
/// <para>
/// Handles four regimes uniformly and always emits exactly
/// <see cref="ForecastConstants.ForecastMonths"/> rows:
/// </para>
/// <list type="bullet">
///   <item><description><b>Zero-proceeds</b> — every row is <c>(m, 0, 0, 0, 0, 0)</c> and <c>Monthly_Loan_Payment = 0</c> (design §7.1, Requirement 11.1).</description></item>
///   <item><description><b>Zero-interest</b> — <c>Monthly_Loan_Payment = Loan_Proceeds / Loan_Term_Months</c>; linear amortization with <c>Monthly_Loan_Interest[m] = 0</c> (design §7.2, Requirement 11.2).</description></item>
///   <item><description><b>Positive-interest</b> — standard fully-amortizing fixed-payment formula (design §7.3, Requirement 11.3).</description></item>
///   <item><description><b>Term boundaries</b> — rows past <c>Loan_Term_Months</c> are all-zero when the term is ≤ 36; <c>Loan_Ending_Balance[36]</c> remains positive when the term exceeds 36 (design §7.4, Requirements 11.10, 11.11).</description></item>
/// </list>
/// <para>
/// All arithmetic uses <see cref="decimal"/> per Requirement 19.1; the integer
/// power <c>(1 + i)^n</c> is computed via a loop of decimal multiplications
/// (see <see cref="DecimalPow(decimal, int)"/>) so no <see cref="double"/> or
/// <see cref="float"/> conversion occurs anywhere in the pipeline
/// (Requirement 19.2).
/// </para>
/// <para>
/// The class is stateless and thread-safe; it is registered <c>Scoped</c> in
/// <c>Program.cs</c> only for DI-lifetime uniformity across core services.
/// </para>
/// </remarks>
public sealed class LoanCalculator : ILoanCalculator
{
    /// <inheritdoc />
    public LoanSchedule Compute(
        decimal loanProceeds,
        decimal annualInterestRate,
        int loanTermMonths)
    {
        // ------------------------------------------------------------------
        // Regime 1 — zero-proceeds (design §7.1, Requirement 11.1).
        // Short-circuits before any interest math so the schedule is
        // unconditionally all-zero regardless of the interest rate and term.
        // ------------------------------------------------------------------
        if (loanProceeds == 0m)
        {
            return BuildZeroSchedule();
        }

        // ------------------------------------------------------------------
        // Regime 2 — zero-interest linear amortization (design §7.2,
        // Requirement 11.2). Interest is identically zero; the fixed payment
        // degenerates to Loan_Proceeds / Loan_Term_Months (evaluated with
        // decimal division — no rounding).
        // ------------------------------------------------------------------
        if (annualInterestRate == 0m)
        {
            decimal zeroInterestPayment = loanProceeds / loanTermMonths;
            return BuildAmortizedSchedule(
                loanProceeds: loanProceeds,
                monthlyPayment: zeroInterestPayment,
                monthlyRate: 0m,
                loanTermMonths: loanTermMonths);
        }

        // ------------------------------------------------------------------
        // Regime 3 — positive-interest, standard fully-amortizing formula
        // (design §7.3, Requirement 11.3):
        //     Monthly_Loan_Payment = Loan_Proceeds · i · (1 + i)^n
        //                            / ((1 + i)^n − 1)
        // where i = Annual_Loan_Interest_Rate / 12 and n = Loan_Term_Months.
        //
        // The expression is evaluated left-to-right so callers reproducing
        // the same product/quotient ordering obtain byte-identical decimal
        // results (see LoanAmortizationPositiveTests.ExpectedMonthlyPayment).
        // ------------------------------------------------------------------
        decimal monthlyRate = annualInterestRate / 12m;
        decimal factor = DecimalPow(1m + monthlyRate, loanTermMonths);
        decimal monthlyPayment = loanProceeds * monthlyRate * factor / (factor - 1m);

        return BuildAmortizedSchedule(
            loanProceeds: loanProceeds,
            monthlyPayment: monthlyPayment,
            monthlyRate: monthlyRate,
            loanTermMonths: loanTermMonths);
    }

    // ======================================================================
    // Schedule assembly
    // ======================================================================

    /// <summary>
    /// Emits the all-zero 36-row schedule used when <c>Loan_Proceeds = 0</c>
    /// (design §7.1, Requirement 11.1). <c>Month</c> remains 1-based so
    /// downstream indexing does not have to branch on this regime.
    /// </summary>
    private static LoanSchedule BuildZeroSchedule()
    {
        var entries = new LoanScheduleEntry[ForecastConstants.ForecastMonths];
        for (int m = 1; m <= ForecastConstants.ForecastMonths; m++)
        {
            entries[m - 1] = new LoanScheduleEntry(
                Month: m,
                BeginningBalance: 0m,
                Payment: 0m,
                Interest: 0m,
                Principal: 0m,
                EndingBalance: 0m);
        }
        return new LoanSchedule(MonthlyPayment: 0m, Entries: entries);
    }

    /// <summary>
    /// Rolls the amortization state forward for either regime that has a
    /// nonzero monthly payment (design §7.2, §7.3, §7.4).
    /// </summary>
    /// <remarks>
    /// <para>Per-month logic mirrors the requirements verbatim:</para>
    /// <list type="bullet">
    ///   <item><description><c>Interest[m] = Balance[m] × monthlyRate</c> (Requirement 11.5).</description></item>
    ///   <item><description><c>Principal[m] = Min(Payment − Interest[m], Balance[m])</c> (Requirement 11.6).</description></item>
    ///   <item><description><c>EndingBalance[m] = Max(Balance[m] − Principal[m], 0)</c> (Requirement 11.7).</description></item>
    ///   <item><description><c>BeginningBalance[m + 1] = EndingBalance[m]</c> (Requirement 11.8).</description></item>
    /// </list>
    /// <para>
    /// When <paramref name="loanTermMonths"/> is ≤ 36 the loop stops emitting
    /// active rows at month <c>Loan_Term_Months</c>; the remaining slots are
    /// filled with all-zero rows (Requirement 11.10). At the final active
    /// month the principal absorbs the entire remaining balance so
    /// <c>Loan_Ending_Balance[Loan_Term_Months] = 0</c> exactly, regardless
    /// of any decimal-arithmetic residual left by the standard formula
    /// (Requirement 11.12).
    /// </para>
    /// <para>
    /// When <paramref name="loanTermMonths"/> is &gt; 36 the loop runs through
    /// all 36 months without any final-month absorption, so
    /// <c>Loan_Ending_Balance[36]</c> reflects the true outstanding residual
    /// (Requirement 11.11 — "no forced early payoff").
    /// </para>
    /// </remarks>
    private static LoanSchedule BuildAmortizedSchedule(
        decimal loanProceeds,
        decimal monthlyPayment,
        decimal monthlyRate,
        int loanTermMonths)
    {
        var entries = new LoanScheduleEntry[ForecastConstants.ForecastMonths];
        decimal balance = loanProceeds;

        // Requirement 11.11: when Loan_Term_Months > 36 we amortize through
        // month 36 without forcing early payoff, so the "final-month" residual
        // absorption at month = Loan_Term_Months never triggers in this run.
        int activeMonths = Math.Min(loanTermMonths, ForecastConstants.ForecastMonths);

        for (int m = 1; m <= ForecastConstants.ForecastMonths; m++)
        {
            if (m > activeMonths)
            {
                // Requirement 11.10: rows past Loan_Term_Months are all-zero
                // (only reachable when Loan_Term_Months < 36).
                entries[m - 1] = new LoanScheduleEntry(
                    Month: m,
                    BeginningBalance: 0m,
                    Payment: 0m,
                    Interest: 0m,
                    Principal: 0m,
                    EndingBalance: 0m);
                continue;
            }

            decimal beginningBalance = balance;

            // Requirement 11.5: Monthly_Loan_Interest[m] = Balance × monthlyRate.
            // Yields exactly 0 for the zero-interest regime because monthlyRate = 0.
            decimal interest = beginningBalance * monthlyRate;

            // Requirement 11.6: principal is clamped at the outstanding balance
            // so the loan can never over-amortize on the standard step. Using
            // Math.Min on decimals keeps the entire computation on the decimal
            // rail.
            decimal principal = Math.Min(monthlyPayment - interest, beginningBalance);

            // Requirement 11.12: at the final active month, absorb any decimal
            // residual so Loan_Ending_Balance[Loan_Term_Months] = 0. This runs
            // only when Loan_Term_Months ≤ 36 because `activeMonths` clamps to
            // 36 when the term is longer (Requirement 11.11).
            if (m == loanTermMonths)
            {
                principal = beginningBalance;
            }

            // Requirement 11.7: EndingBalance = Max(Balance − Principal, 0).
            // The R11.6 clamp above already guarantees non-negativity, but the
            // Max floor is preserved so the identity in the spec is applied
            // verbatim.
            decimal endingBalance = beginningBalance - principal;
            if (endingBalance < 0m) endingBalance = 0m;

            entries[m - 1] = new LoanScheduleEntry(
                Month: m,
                BeginningBalance: beginningBalance,
                Payment: monthlyPayment,
                Interest: interest,
                Principal: principal,
                EndingBalance: endingBalance);

            // Requirement 11.8: roll the ending balance into the next month's
            // beginning balance.
            balance = endingBalance;
        }

        return new LoanSchedule(MonthlyPayment: monthlyPayment, Entries: entries);
    }

    // ======================================================================
    // Decimal-safe integer power
    // ======================================================================

    /// <summary>
    /// Integer-exponent power for <see cref="decimal"/> values, computed as a
    /// loop of decimal multiplications.
    /// </summary>
    /// <param name="x">The base.</param>
    /// <param name="n">The non-negative integer exponent.</param>
    /// <returns><paramref name="x"/> raised to the <paramref name="n"/>-th power.</returns>
    /// <remarks>
    /// <para>
    /// The .NET base class library has no native decimal <c>Pow</c>; the usual
    /// workaround of routing through <see cref="Math.Pow(double, double)"/> is
    /// prohibited by Requirements 19.1 and 19.2 because it introduces a
    /// binary-float conversion. This helper keeps the calculation on the
    /// decimal rail end-to-end.
    /// </para>
    /// <para>
    /// The evaluation order (left-to-right multiplication starting from
    /// <c>1m</c>) matches the reference computation in
    /// <c>LoanAmortizationPositiveTests.DecimalPow</c>, so callers computing
    /// <c>(1 + i)^n</c> obtain byte-identical decimal results in both the
    /// production code and the test oracle.
    /// </para>
    /// </remarks>
    private static decimal DecimalPow(decimal x, int n)
    {
        decimal result = 1m;
        for (int k = 0; k < n; k++)
        {
            result *= x;
        }
        return result;
    }
}
