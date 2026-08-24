using RehearsalForecast.Core.Constants;

namespace RehearsalForecast.Core.Forecast;

/// <summary>
/// Pass 10 of the forecast pipeline (design §6.10, Requirement 13): rolls
/// cash forward month by month from a user-supplied opening balance and
/// applies the master accounting identity to produce <c>Beginning_Cash</c>
/// and <c>Ending_Cash</c> vectors of length <see cref="ForecastConstants.ForecastMonths"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sign convention (Requirement 13.7).</b> Additions increase
/// <c>Ending_Cash</c>; subtractions decrease it. The identity implemented
/// below matches Requirement 13.4 verbatim:
/// </para>
/// <code>
/// Beginning_Cash[1] = beginningCashMonth1                                   // R13.2
/// Beginning_Cash[m] = Ending_Cash[m - 1]                    for m ∈ [2, 36] // R13.3
///
/// Ending_Cash[m] =
///       Beginning_Cash[m]
///     + Owner_Investment_In_Month[m]                      // addition
///     + Loan_Proceeds_In_Month[m]                         // addition
///     + Net_Income[m]                                     // addition
///     + Monthly_Depreciation                              // addition (non-cash add-back, R13.5)
///     - Capital_Expenditures_In_Month[m]                  // subtraction
///     - Monthly_Loan_Principal[m]                         // subtraction (principal only, R11.14)
///     - Owner_Withdrawals                                 // subtraction (every month, R13.6)
/// </code>
/// <para>
/// Two explicit correctness anchors from design §6.10:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Depreciation add-back (R13.5).</b> <c>Monthly_Depreciation</c> was
///       already subtracted inside <c>Net_Income[m]</c> via
///       <c>Expenses_Before_Income_Tax</c> in Pass 9. Because it is non-cash,
///       it is added back here so the cash line reflects real cash movement.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Principal-only servicing (R11.14).</b> <c>Monthly_Loan_Interest[m]</c>
///       was already treated as an expense inside <c>Net_Income[m]</c> in
///       Pass 9. Only <c>Monthly_Loan_Principal[m]</c> reduces cash — the
///       parameter list of <see cref="Compute"/> deliberately does not expose
///       an interest channel, making the "interest never reaches cash flow"
///       property structural rather than something the caller must remember.
///     </description>
///   </item>
/// </list>
/// <para>
/// This helper is deliberately <see langword="internal"/>: it is a per-pass
/// building block for <c>ForecastCalculator</c>, exposed to the test project
/// via <c>InternalsVisibleTo</c>. All arithmetic runs on <see cref="decimal"/>
/// in accordance with Requirement 19.1 (no <see cref="double"/>/<see cref="float"/>).
/// </para>
/// </remarks>
internal static class CashFlowCalculator
{
    /// <summary>
    /// Computes the Pass 10 <c>Beginning_Cash</c> and <c>Ending_Cash</c>
    /// vectors from the outputs of Passes 3–9 and the loan schedule.
    /// </summary>
    /// <param name="beginningCashMonth1">
    /// The user-supplied opening cash balance for Month 1 (Requirement 13.2).
    /// May be negative (pre-financing shortfalls are legitimate); passed
    /// through to <c>Beginning_Cash[0]</c> without transformation.
    /// </param>
    /// <param name="netIncome">
    /// The 36-entry <c>Net_Income</c> vector from Pass 9 (design §6.9). Index
    /// <c>m - 1</c> holds the value for Month <c>m</c>. Contributes as an
    /// addition to <c>Ending_Cash[m]</c> per Requirement 13.4.
    /// </param>
    /// <param name="monthlyDepreciation">
    /// The scalar <c>Monthly_Depreciation</c> from Pass 6 (design §6.6,
    /// Requirement 8.2 — identical across all 36 months). Added back as a
    /// non-cash expense (Requirement 13.5); modelled as a scalar because the
    /// design guarantees monthly constancy — a per-month channel would allow
    /// the add-back to vary and is therefore deliberately excluded.
    /// </param>
    /// <param name="monthlyLoanPrincipal">
    /// The 36-entry principal-only column of the <c>LoanSchedule</c> from
    /// Pass 8 (design §6.8, Requirement 11.14). Contributes as a subtraction
    /// to <c>Ending_Cash[m]</c>. <c>Monthly_Loan_Interest</c> is deliberately
    /// NOT accepted by this helper: interest is already an expense inside
    /// <c>Net_Income[m]</c>; subtracting it again would double-count it.
    /// </param>
    /// <param name="capitalExpendituresInMonth">
    /// The 36-entry <c>Capital_Expenditures_In_Month</c> vector from Pass 7
    /// (design §6.7). Month-1 timing is imposed upstream (index 0 carries
    /// <c>Total_Capital</c>, indices 1..35 are zero); Pass 10 respects that
    /// timing by subtracting whatever this vector supplies per month, without
    /// adding month-specific behaviour of its own.
    /// </param>
    /// <param name="ownerInvestmentInMonth">
    /// The 36-entry <c>Owner_Investment_In_Month</c> vector from Pass 7. Same
    /// Month-1 timing as above. Contributes as an addition to
    /// <c>Ending_Cash[m]</c> per Requirement 13.4.
    /// </param>
    /// <param name="loanProceedsInMonth">
    /// The 36-entry <c>Loan_Proceeds_In_Month</c> vector from Pass 7. Same
    /// Month-1 timing as above. Contributes as an addition to
    /// <c>Ending_Cash[m]</c> per Requirement 13.4.
    /// </param>
    /// <param name="ownerWithdrawals">
    /// The scalar <c>Owner_Withdrawals</c> from <c>OwnerActivityInputs</c>
    /// (Requirement 1.6, DD8). Subtracted uniformly from every month
    /// <c>m ∈ [1, 36]</c> (Requirement 13.6); modelled as a scalar because
    /// Variable_Mode is deliberately not supported for withdrawals in this
    /// phase — the parameter type itself enforces monthly uniformity.
    /// </param>
    /// <returns>
    /// A <see cref="CashFlowResult"/> whose two vectors each hold exactly
    /// <see cref="ForecastConstants.ForecastMonths"/> (36) decimals, indexed
    /// <c>m - 1</c> for month <c>m</c>. <c>Beginning_Cash[0]</c> equals
    /// <paramref name="beginningCashMonth1"/> and
    /// <c>Beginning_Cash[i]</c> equals <c>Ending_Cash[i - 1]</c> for <c>i ≥ 1</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Any of <paramref name="netIncome"/>, <paramref name="monthlyLoanPrincipal"/>,
    /// <paramref name="capitalExpendituresInMonth"/>,
    /// <paramref name="ownerInvestmentInMonth"/>, or
    /// <paramref name="loanProceedsInMonth"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Any input vector does not have exactly
    /// <see cref="ForecastConstants.ForecastMonths"/> (36) entries. The length
    /// invariant is structural — the identity in Requirement 13.4 is defined
    /// per month across the fixed 36-month horizon and cannot be evaluated
    /// otherwise.
    /// </exception>
    internal static CashFlowResult Compute(
        decimal beginningCashMonth1,
        IReadOnlyList<decimal> netIncome,
        decimal monthlyDepreciation,
        IReadOnlyList<decimal> monthlyLoanPrincipal,
        IReadOnlyList<decimal> capitalExpendituresInMonth,
        IReadOnlyList<decimal> ownerInvestmentInMonth,
        IReadOnlyList<decimal> loanProceedsInMonth,
        decimal ownerWithdrawals)
    {
        ArgumentNullException.ThrowIfNull(netIncome);
        ArgumentNullException.ThrowIfNull(monthlyLoanPrincipal);
        ArgumentNullException.ThrowIfNull(capitalExpendituresInMonth);
        ArgumentNullException.ThrowIfNull(ownerInvestmentInMonth);
        ArgumentNullException.ThrowIfNull(loanProceedsInMonth);

        RequireLength(netIncome, nameof(netIncome));
        RequireLength(monthlyLoanPrincipal, nameof(monthlyLoanPrincipal));
        RequireLength(capitalExpendituresInMonth, nameof(capitalExpendituresInMonth));
        RequireLength(ownerInvestmentInMonth, nameof(ownerInvestmentInMonth));
        RequireLength(loanProceedsInMonth, nameof(loanProceedsInMonth));

        var beginning = new decimal[ForecastConstants.ForecastMonths];
        var ending = new decimal[ForecastConstants.ForecastMonths];

        for (var i = 0; i < ForecastConstants.ForecastMonths; i++)
        {
            // Requirement 13.2 / 13.3: Beginning_Cash[1] is the supplied
            // opening balance; subsequent months roll forward from the prior
            // month's Ending_Cash.
            beginning[i] = i == 0 ? beginningCashMonth1 : ending[i - 1];

            // Requirement 13.4 verbatim: additions first, then subtractions
            // (Requirement 13.7 sign convention). Monthly_Depreciation is
            // added back per R13.5; only Monthly_Loan_Principal reduces cash
            // per R11.14; Owner_Withdrawals is subtracted every month per R13.6.
            ending[i] =
                beginning[i]
                + ownerInvestmentInMonth[i]
                + loanProceedsInMonth[i]
                + netIncome[i]
                + monthlyDepreciation
                - capitalExpendituresInMonth[i]
                - monthlyLoanPrincipal[i]
                - ownerWithdrawals;
        }

        return new CashFlowResult(BeginningCash: beginning, EndingCash: ending);
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when a supplied per-month vector
    /// is not exactly <see cref="ForecastConstants.ForecastMonths"/> entries
    /// long. Requirement 13.1 fixes the horizon at 36 monthly records, so a
    /// mismatched length is a caller programming error rather than a
    /// recoverable condition.
    /// </summary>
    private static void RequireLength(IReadOnlyList<decimal> vector, string parameterName)
    {
        if (vector.Count != ForecastConstants.ForecastMonths)
        {
            throw new ArgumentException(
                $"Expected exactly {ForecastConstants.ForecastMonths} monthly entries, but received {vector.Count}.",
                parameterName);
        }
    }
}

/// <summary>
/// The outputs of <see cref="CashFlowCalculator.Compute"/> (design §6.10):
/// the two per-month cash vectors produced by the roll-forward pass.
/// </summary>
/// <param name="BeginningCash">
/// 36-entry vector. Index 0 equals the user-supplied opening cash
/// (<c>beginningCashMonth1</c>, Requirement 13.2). For every subsequent
/// index <c>i ∈ [1, 35]</c>, the value equals <see cref="EndingCash"/> at
/// index <c>i - 1</c> (Requirement 13.3).
/// </param>
/// <param name="EndingCash">
/// 36-entry vector. Element at index <c>m - 1</c> is <c>Ending_Cash</c> for
/// month <c>m</c>, computed by the accounting identity in Requirement 13.4.
/// Consumed downstream by Pass 11 (§6.11) to evaluate the
/// <c>Cash_Positive_Rule</c> and <c>First_Sustained_Nonnegative_Month</c>.
/// </param>
internal sealed record CashFlowResult(
    IReadOnlyList<decimal> BeginningCash,
    IReadOnlyList<decimal> EndingCash);
