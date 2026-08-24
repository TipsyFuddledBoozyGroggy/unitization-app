using RehearsalForecast.Core.Constants;

namespace RehearsalForecast.Core.Forecast;

/// <summary>
/// Pass 9 of the forecast pipeline (design §6.9, Requirement 12): per-month
/// pre-tax income, income tax, total expenses, and net income.
/// </summary>
/// <remarks>
/// <para>
/// For each month <c>m ∈ [1, 36]</c>:
/// </para>
/// <code>
/// Expenses_Before_Income_Tax[m] =
///       Marketing_Total[m] + Operations_Total[m]
///     + Monthly_Loan_Interest[m] + Monthly_Depreciation      // R12.1
///
/// Pre_Tax_Income[m] = Gross_Income[m] − Expenses_Before_Income_Tax[m]   // R12.2
/// Income_Tax[m]     = max(Pre_Tax_Income[m], 0) × Income_Tax_Rate       // R12.3, R12.4
/// Total_Expenses[m] = Expenses_Before_Income_Tax[m] + Income_Tax[m]     // R12.5
/// Net_Income[m]     = Gross_Income[m] − Total_Expenses[m]               // R12.6
/// </code>
/// <para>
/// Losses are <b>not</b> carried forward across months (Requirement 12.7):
/// the tax formula operates on each month independently and the
/// <c>max(·, 0)</c> clamp is applied per-month with no state accumulated
/// between iterations.
/// </para>
/// <para>
/// This pass takes the four contributing per-month vectors plus the two
/// scalars (<c>Monthly_Depreciation</c> from Pass 6 and <c>Income_Tax_Rate</c>
/// from <c>TaxInputs</c>) as its inputs. It intentionally does not accept the
/// full <c>ForecastInputs</c>, <c>OperationsResult</c>, or <c>LoanSchedule</c>
/// so that no other quantity — capital, owner-activity, geometry — can leak
/// into the computation (Requirement 7.5's exclusion is preserved
/// structurally at this pass as well).
/// </para>
/// <para>
/// All arithmetic is <see cref="decimal"/> per Requirement 19.1 (no
/// <see cref="double"/> or <see cref="float"/>). The helper is
/// <see langword="internal"/> and reachable to the test project through
/// <c>InternalsVisibleTo</c>.
/// </para>
/// </remarks>
internal static class IncomeTaxCalculator
{
    /// <summary>
    /// Computes the five per-month vectors produced by Pass 9 from the
    /// supplied inputs (design §6.9).
    /// </summary>
    /// <param name="grossIncome">
    /// <c>Gross_Income[m]</c> for <c>m ∈ [1, 36]</c>, produced by Pass 3
    /// (design §6.3). Must have length exactly
    /// <see cref="ForecastConstants.ForecastMonths"/>.
    /// </param>
    /// <param name="marketingTotal">
    /// <c>Marketing_Total[m]</c> for <c>m ∈ [1, 36]</c>, produced by Pass 4
    /// (design §6.4). Must have length exactly
    /// <see cref="ForecastConstants.ForecastMonths"/>.
    /// </param>
    /// <param name="operationsTotal">
    /// <c>Operations_Total[m]</c> for <c>m ∈ [1, 36]</c>, produced by Pass 5
    /// (design §6.5). Must have length exactly
    /// <see cref="ForecastConstants.ForecastMonths"/>.
    /// </param>
    /// <param name="monthlyLoanInterest">
    /// <c>Monthly_Loan_Interest[m]</c> for <c>m ∈ [1, 36]</c>, drawn from
    /// <c>LoanSchedule.Entries[m − 1].Interest</c> (Pass 8, design §6.8).
    /// Must have length exactly
    /// <see cref="ForecastConstants.ForecastMonths"/>.
    /// </param>
    /// <param name="monthlyDepreciation">
    /// <c>Monthly_Depreciation</c> scalar produced by Pass 6 (design §6.6).
    /// The same value is applied identically to every month (Requirement 8.2).
    /// </param>
    /// <param name="incomeTaxRate">
    /// <c>Income_Tax_Rate</c> from <see cref="Domain.TaxInputs"/>. Contract:
    /// value in <c>[0, 1]</c> (enforced upstream by validation, Requirement 2.6).
    /// </param>
    /// <returns>
    /// An <see cref="IncomeTaxResult"/> whose five vectors each contain
    /// exactly <see cref="ForecastConstants.ForecastMonths"/> (36) values,
    /// ordered from Month 1 to Month 36 (i.e. <c>list[m - 1]</c>).
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Any of the vector arguments is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Any vector argument does not have length exactly
    /// <see cref="ForecastConstants.ForecastMonths"/>.
    /// </exception>
    internal static IncomeTaxResult Compute(
        IReadOnlyList<decimal> grossIncome,
        IReadOnlyList<decimal> marketingTotal,
        IReadOnlyList<decimal> operationsTotal,
        IReadOnlyList<decimal> monthlyLoanInterest,
        decimal monthlyDepreciation,
        decimal incomeTaxRate)
    {
        ArgumentNullException.ThrowIfNull(grossIncome);
        ArgumentNullException.ThrowIfNull(marketingTotal);
        ArgumentNullException.ThrowIfNull(operationsTotal);
        ArgumentNullException.ThrowIfNull(monthlyLoanInterest);

        RequireLength36(grossIncome, nameof(grossIncome));
        RequireLength36(marketingTotal, nameof(marketingTotal));
        RequireLength36(operationsTotal, nameof(operationsTotal));
        RequireLength36(monthlyLoanInterest, nameof(monthlyLoanInterest));

        var months = ForecastConstants.ForecastMonths;
        var expensesBeforeTax = new decimal[months];
        var preTaxIncome = new decimal[months];
        var incomeTax = new decimal[months];
        var totalExpenses = new decimal[months];
        var netIncome = new decimal[months];

        for (var m = 1; m <= months; m++)
        {
            var i = m - 1;

            // R12.1: Expenses_Before_Income_Tax[m] composition.
            var expenses =
                marketingTotal[i]
                + operationsTotal[i]
                + monthlyLoanInterest[i]
                + monthlyDepreciation;

            // R12.2: Pre_Tax_Income[m] = Gross_Income[m] − Expenses_Before_Income_Tax[m].
            var preTax = grossIncome[i] - expenses;

            // R12.3, R12.4, R12.7: per-month max(·, 0) clamp; no cross-month state.
            var tax = Math.Max(preTax, 0m) * incomeTaxRate;

            // R12.5: Total_Expenses[m] = Expenses_Before_Income_Tax[m] + Income_Tax[m].
            var total = expenses + tax;

            // R12.6: Net_Income[m] = Gross_Income[m] − Total_Expenses[m].
            var net = grossIncome[i] - total;

            expensesBeforeTax[i] = expenses;
            preTaxIncome[i] = preTax;
            incomeTax[i] = tax;
            totalExpenses[i] = total;
            netIncome[i] = net;
        }

        return new IncomeTaxResult(
            expensesBeforeTax,
            preTaxIncome,
            incomeTax,
            totalExpenses,
            netIncome);
    }

    private static void RequireLength36(IReadOnlyList<decimal> vector, string paramName)
    {
        if (vector.Count != ForecastConstants.ForecastMonths)
        {
            throw new ArgumentException(
                $"Expected {ForecastConstants.ForecastMonths} monthly values but received {vector.Count}.",
                paramName);
        }
    }
}

/// <summary>
/// The five per-month vectors produced by <see cref="IncomeTaxCalculator.Compute"/>
/// (design §6.9). Each list contains exactly
/// <see cref="ForecastConstants.ForecastMonths"/> (36) values indexed from
/// Month 1 to Month 36 (i.e. <c>list[m - 1]</c>).
/// </summary>
/// <param name="ExpensesBeforeIncomeTax">
/// Per-month <c>Marketing_Total + Operations_Total + Monthly_Loan_Interest
/// + Monthly_Depreciation</c> (Requirement 12.1).
/// </param>
/// <param name="PreTaxIncome">
/// Per-month <c>Gross_Income − Expenses_Before_Income_Tax</c>
/// (Requirement 12.2). May be negative on loss months.
/// </param>
/// <param name="IncomeTax">
/// Per-month <c>max(Pre_Tax_Income, 0) × Income_Tax_Rate</c>
/// (Requirements 12.3, 12.4). Zero on loss months; zero everywhere when
/// <c>Income_Tax_Rate == 0</c> (Requirement 27.4).
/// </param>
/// <param name="TotalExpenses">
/// Per-month <c>Expenses_Before_Income_Tax + Income_Tax</c>
/// (Requirement 12.5).
/// </param>
/// <param name="NetIncome">
/// Per-month <c>Gross_Income − Total_Expenses</c> (Requirement 12.6). Equal
/// to <c>Pre_Tax_Income − Income_Tax</c> by algebraic restatement.
/// </param>
internal sealed record IncomeTaxResult(
    IReadOnlyList<decimal> ExpensesBeforeIncomeTax,
    IReadOnlyList<decimal> PreTaxIncome,
    IReadOnlyList<decimal> IncomeTax,
    IReadOnlyList<decimal> TotalExpenses,
    IReadOnlyList<decimal> NetIncome);
