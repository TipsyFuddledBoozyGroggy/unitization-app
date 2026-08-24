using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Loan;

namespace RehearsalForecast.Core.Forecast;

/// <summary>
/// Pass 12 of the forecast pipeline (design §4.1, §6.12): orchestrates Passes
/// 1–11 in order and assembles a <see cref="ForecastResult"/> populated with
/// summary metrics, all 36 <see cref="MonthlyForecastRow"/> records, and the
/// two outputs of the Cash-Positive Rule (Requirements 14.1, 14.5).
/// </summary>
/// <remarks>
/// <para>
/// The class is the single top-level entry point for the calculation engine
/// (design §4.1). It has one collaborator — <see cref="ILoanCalculator"/> —
/// injected through the constructor so that <c>PriceSolver</c> and the web
/// layer share the same amortization implementation, and so tests can
/// substitute a stub schedule (design §4.2). All other passes are pure
/// static helpers inside <c>RehearsalForecast.Core.Forecast</c> and are
/// invoked in the fixed order described by design §6.12.
/// </para>
/// <para>
/// Pipeline (design §6.12; the numbered comments in <see cref="Compute"/>
/// mirror this list one-for-one):
/// </para>
/// <list type="number">
///   <item><description>Pass 1 — <see cref="BuildingGeometryCalculator"/></description></item>
///   <item><description>Pass 2 — <see cref="OccupancyCalculator"/></description></item>
///   <item><description>Pass 3 — <see cref="RevenueCalculator"/></description></item>
///   <item><description>Pass 4 — <see cref="MarketingCalculator"/></description></item>
///   <item><description>Pass 5 — <see cref="OperationsCalculator"/></description></item>
///   <item><description>Pass 6 — <see cref="DepreciationCalculator"/></description></item>
///   <item><description>Pass 7 — <see cref="CapitalCalculator"/></description></item>
///   <item><description>Pass 8 — <see cref="ILoanCalculator.Compute"/></description></item>
///   <item><description>Pass 9 — <see cref="IncomeTaxCalculator"/></description></item>
///   <item><description>Pass 10 — <see cref="CashFlowCalculator"/></description></item>
///   <item><description>Pass 11 — <see cref="CashPositiveRuleEvaluator"/></description></item>
/// </list>
/// <para>
/// The class is stateless and thread-safe; every call to
/// <see cref="Compute"/> is independent. It is registered <c>Scoped</c> in
/// <c>Program.cs</c> only for DI-lifetime uniformity with the other core
/// services. All arithmetic runs on <see cref="decimal"/> per Requirement
/// 19.1; no <see cref="double"/> or <see cref="float"/> is introduced at
/// any step.
/// </para>
/// </remarks>
public sealed class ForecastCalculator : IForecastCalculator
{
    private readonly ILoanCalculator _loanCalculator;

    /// <summary>
    /// Constructs a <see cref="ForecastCalculator"/> that will delegate loan
    /// amortization to the supplied <paramref name="loanCalculator"/>
    /// (design §4.2).
    /// </summary>
    /// <param name="loanCalculator">
    /// The loan-amortization component (Requirement 11). Must be non-null;
    /// resolved by the DI container from the <c>Scoped</c> registration in
    /// <c>Program.cs</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="loanCalculator"/> is <see langword="null"/>.
    /// </exception>
    public ForecastCalculator(ILoanCalculator loanCalculator)
    {
        ArgumentNullException.ThrowIfNull(loanCalculator);
        _loanCalculator = loanCalculator;
    }

    /// <inheritdoc />
    public ForecastResult Compute(ForecastInputs inputs, decimal flatPricePerSqft)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        // ------------------------------------------------------------------
        // Pass 1 — Building geometry (design §6.1, Requirement 3).
        //   Rentable_Sqft, Total_Rental_Units.
        // ------------------------------------------------------------------
        var geometry = BuildingGeometryCalculator.Compute(inputs.Building);

        // ------------------------------------------------------------------
        // Pass 2 — Occupancy schedule (design §6.2, Requirement 4).
        //   Rates[m], Rented_Units[m], Rented_Sqft[m] for m ∈ [1, 36].
        // ------------------------------------------------------------------
        var occupancy = OccupancyCalculator.Compute(inputs.Building, geometry);

        // ------------------------------------------------------------------
        // Pass 3 — Revenue (design §6.3, Requirement 5).
        //   Monthly_Price_Per_Sqft, Gross_Revenue[m], Gross_Income[m].
        // ------------------------------------------------------------------
        var revenue = RevenueCalculator.Compute(occupancy.RentedSqft, flatPricePerSqft);

        // ------------------------------------------------------------------
        // Pass 4 — Marketing (design §6.4, Requirement 6).
        //   Marketing_Total[m].
        // ------------------------------------------------------------------
        var marketingTotal = MarketingCalculator.Compute(inputs.Marketing);

        // ------------------------------------------------------------------
        // Pass 5 — Operations (design §6.5, Requirement 7).
        //   Wages[m], Payroll_Tax[m], Operations_Total[m].
        // ------------------------------------------------------------------
        var operations = OperationsCalculator.Compute(inputs.Operations);

        // ------------------------------------------------------------------
        // Pass 6 — Depreciation (design §6.6, Requirement 8).
        //   Monthly_Depreciation scalar; identical every month by construction.
        // ------------------------------------------------------------------
        var monthlyDepreciation = DepreciationCalculator.Compute(inputs.Building);

        // ------------------------------------------------------------------
        // Pass 7 — Capital and financing (design §6.7, Requirements 9, 10).
        //   Total_Capital, Loan_Proceeds, and the three Month-1 timing vectors.
        // ------------------------------------------------------------------
        var capital = CapitalCalculator.Compute(
            inputs.Capital,
            inputs.OwnerActivity.OwnerInvestment);

        // ------------------------------------------------------------------
        // Pass 8 — Loan amortization (design §6.8, §7, Requirement 11).
        //   36-row LoanSchedule. Delegated to the injected ILoanCalculator
        //   so the solver and the web layer share the same implementation.
        // ------------------------------------------------------------------
        var loanSchedule = _loanCalculator.Compute(
            capital.LoanProceeds,
            inputs.Loan.AnnualLoanInterestRate,
            inputs.Loan.LoanTermMonths);

        // Materialise the per-month Interest and Principal columns of the
        // schedule as flat decimal[] vectors so that Passes 9 and 10 can be
        // fed on the same shape (IReadOnlyList<decimal>) they use in their
        // unit tests. This avoids allocating throwaway LINQ enumerables
        // inside each pass while keeping the composition explicit here.
        var monthlyLoanInterest = new decimal[ForecastConstants.ForecastMonths];
        var monthlyLoanPrincipal = new decimal[ForecastConstants.ForecastMonths];
        for (var i = 0; i < ForecastConstants.ForecastMonths; i++)
        {
            monthlyLoanInterest[i] = loanSchedule.Entries[i].Interest;
            monthlyLoanPrincipal[i] = loanSchedule.Entries[i].Principal;
        }

        // ------------------------------------------------------------------
        // Pass 9 — Income tax (design §6.9, Requirement 12).
        //   Expenses_Before_Income_Tax[m], Pre_Tax_Income[m],
        //   Income_Tax[m], Total_Expenses[m], Net_Income[m].
        // ------------------------------------------------------------------
        var tax = IncomeTaxCalculator.Compute(
            revenue.GrossIncome,
            marketingTotal,
            operations.OperationsTotal,
            monthlyLoanInterest,
            monthlyDepreciation,
            inputs.Taxes.IncomeTaxRate);

        // ------------------------------------------------------------------
        // Pass 10 — Cash-flow roll-forward (design §6.10, Requirement 13).
        //   Beginning_Cash[m], Ending_Cash[m].
        // ------------------------------------------------------------------
        var cashFlow = CashFlowCalculator.Compute(
            inputs.ForecastControls.BeginningCashMonth1,
            tax.NetIncome,
            monthlyDepreciation,
            monthlyLoanPrincipal,
            capital.CapitalExpendituresInMonth,
            capital.OwnerInvestmentInMonth,
            capital.LoanProceedsInMonth,
            inputs.OwnerActivity.OwnerWithdrawals);

        // ------------------------------------------------------------------
        // Pass 11 — Cash-Positive Rule (design §6.11, Requirement 14).
        //   Cash_Positive_Rule_Satisfied,
        //   First_Sustained_Nonnegative_Month (nullable = "None").
        // ------------------------------------------------------------------
        var cashRule = CashPositiveRuleEvaluator.Evaluate(
            cashFlow.EndingCash,
            inputs.ForecastControls.TargetCashPositiveMonth);

        // ------------------------------------------------------------------
        // Pass 12 — Assemble ForecastResult (design §6.12, §5.4, §5.5).
        //
        // Every row draws each field from the pass that produced it. Scalar
        // pass outputs (Monthly_Price_Per_Sqft, Total_Rental_Units,
        // Monthly_Depreciation, Owner_Withdrawals) are echoed on every row
        // for Requirement 16.5's results-table shape.
        //
        // MonthlyLoanPayment uses the per-row Payment from the schedule
        // rather than LoanSchedule.MonthlyPayment: this way rows past the
        // loan term (when term < 36) display 0 rather than the still-live
        // constant payment (Requirement 11.10 semantics carried through
        // to the results table).
        //
        // CashPositiveStatus is the per-row flag EndingCash >= 0 (design
        // §5.4 note); the fleet-wide Cash_Positive_Rule sits on the
        // ForecastResult summary instead.
        // ------------------------------------------------------------------
        var rows = new MonthlyForecastRow[ForecastConstants.ForecastMonths];
        for (var i = 0; i < ForecastConstants.ForecastMonths; i++)
        {
            var entry = loanSchedule.Entries[i];
            rows[i] = new MonthlyForecastRow(
                Month: i + 1,
                OccupancyRate: occupancy.Rates[i],
                TotalRentalUnits: geometry.TotalRentalUnits,
                RentedUnits: occupancy.RentedUnits[i],
                RentedSqft: occupancy.RentedSqft[i],
                MonthlyPricePerSqft: revenue.MonthlyPricePerSqft,
                GrossRevenue: revenue.GrossRevenue[i],
                GrossIncome: revenue.GrossIncome[i],
                MarketingTotal: marketingTotal[i],
                OperationsTotal: operations.OperationsTotal[i],
                Wages: operations.Wages[i],
                PayrollTax: operations.PayrollTax[i],
                LoanBeginningBalance: entry.BeginningBalance,
                MonthlyLoanPayment: entry.Payment,
                MonthlyLoanInterest: entry.Interest,
                MonthlyLoanPrincipal: entry.Principal,
                LoanEndingBalance: entry.EndingBalance,
                MonthlyDepreciation: monthlyDepreciation,
                PreTaxIncome: tax.PreTaxIncome[i],
                IncomeTax: tax.IncomeTax[i],
                TotalExpenses: tax.TotalExpenses[i],
                NetIncome: tax.NetIncome[i],
                BeginningCash: cashFlow.BeginningCash[i],
                OwnerInvestmentInMonth: capital.OwnerInvestmentInMonth[i],
                LoanProceedsInMonth: capital.LoanProceedsInMonth[i],
                CapitalExpendituresInMonth: capital.CapitalExpendituresInMonth[i],
                OwnerWithdrawals: inputs.OwnerActivity.OwnerWithdrawals,
                EndingCash: cashFlow.EndingCash[i],
                CashPositiveStatus: cashFlow.EndingCash[i] >= 0m);
        }

        return new ForecastResult(
            TotalCapital: capital.TotalCapital,
            OwnerInvestment: inputs.OwnerActivity.OwnerInvestment,
            LoanProceeds: capital.LoanProceeds,
            RentableSqft: geometry.RentableSqft,
            TotalRentalUnits: geometry.TotalRentalUnits,
            FlatPricePerSqft: flatPricePerSqft,
            MonthlyPricePerSqft: revenue.MonthlyPricePerSqft,
            TargetCashPositiveMonth: inputs.ForecastControls.TargetCashPositiveMonth,
            CashPositiveRuleSatisfied: cashRule.CashPositiveRuleSatisfied,
            FirstSustainedNonnegativeMonth: cashRule.FirstSustainedNonnegativeMonth,
            Rows: rows);
    }
}
