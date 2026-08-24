// Property-based test for Property 9 — Income tax and net income composition
// (design §10, Property 9; §15.4).
//
// Property 9 states that for any valid ForecastInputs, any Flat_Price_Per_Sqft,
// and every month m ∈ [1, 36]:
//
//   1. Expenses_Before_Income_Tax[m] = Marketing_Total[m] + Operations_Total[m]
//                                    + Monthly_Loan_Interest[m] + Monthly_Depreciation  (R12.1)
//   2. Pre_Tax_Income[m] = Gross_Income[m] − Expenses_Before_Income_Tax[m]              (R12.2)
//   3. Income_Tax[m] = max(Pre_Tax_Income[m], 0) × Income_Tax_Rate                       (R12.3)
//   4. Pre_Tax_Income[m] ≤ 0 ⇒ Income_Tax[m] = 0                                          (R12.4)
//   5. Income_Tax_Rate == 0 ⇒ Income_Tax[m] = 0 for every m                              (R27.4)
//   6. Total_Expenses[m] = Expenses_Before_Income_Tax[m] + Income_Tax[m]                 (R12.5)
//   7. Net_Income[m] = Gross_Income[m] − Total_Expenses[m]                               (R12.6)
//
// The test drives the pass-9 internal helper (design §6.9,
// IncomeTaxCalculator) directly with hand-generated 36-entry per-month
// vectors and two scalars (monthly_depreciation, income_tax_rate). Taking
// the four contributing vectors as inputs — rather than assembling a full
// ForecastInputs and running the whole pipeline — pins the property to the
// arithmetic identity in R12 and avoids coupling the test to unrelated
// passes.
//
// Bounding strategy: every raw Int32 folds into a modest nonnegative decimal
// (Marketing/Operations/Interest ≥ 0 by construction) or into a signed
// bounded decimal (Gross_Income can be small so pre-tax naturally straddles
// zero across the 100-iteration run, giving both branches of the max(·, 0)
// clamp real coverage). Income_Tax_Rate folds into a decimal in [0, 1] per
// Requirement 2.6. All arithmetic remains in decimal per Requirement 19.1.
//
// Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7, 27.4

using System.Collections.Generic;
using FsCheck.Xunit;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Forecast;
using Xunit;

namespace RehearsalForecast.Core.Tests.PropertyTests;

public class IncomeTaxCompositionProperty
{
    private const int Months = ForecastConstants.ForecastMonths;

    // ------------------------------------------------------------------
    // Bounded generators
    //
    // A single per-vector seed generates 36 element-wise-distinct decimals
    // via an offset scheme. Using distinct values per month gives the
    // property maximum discriminating power against "off-by-one" or
    // "wrong month reached" regressions.
    // ------------------------------------------------------------------

    private static decimal BoundNonNegative(int raw) =>
        (decimal)(Math.Abs((long)raw) % 1_000_000L) / 100m; // 0..10,000 with cent precision

    private static decimal BoundSigned(int raw) =>
        (decimal)(((long)raw) % 1_000_000L) / 100m; // −10,000..+10,000 with cent precision

    private static decimal BoundRate(int raw) =>
        (decimal)(Math.Abs((long)raw) % 101L) / 100m; // 0..1.00 in 0.01 steps

    /// <summary>
    /// Materialises a 36-entry nonnegative vector where each month carries
    /// <c>|seed + 7·(m − 1)| mod 1,000,000 / 100</c>. The linear shift per
    /// month yields month-wise distinct values under most seeds and lets a
    /// bug that reads the wrong month index surface as a mismatch.
    /// </summary>
    private static IReadOnlyList<decimal> MakeNonNegativeVector(int seed)
    {
        var xs = new decimal[Months];
        for (var i = 0; i < Months; i++)
        {
            xs[i] = BoundNonNegative(seed + (7 * i));
        }
        return xs;
    }

    /// <summary>
    /// Materialises a 36-entry signed vector using the same per-month shift
    /// scheme as <see cref="MakeNonNegativeVector(int)"/>. Values can be
    /// negative so that <c>Pre_Tax_Income</c> naturally spans loss and
    /// profit months across the 100-iteration run.
    /// </summary>
    private static IReadOnlyList<decimal> MakeSignedVector(int seed)
    {
        var xs = new decimal[Months];
        for (var i = 0; i < Months; i++)
        {
            xs[i] = BoundSigned(seed + (11 * i));
        }
        return xs;
    }

    // ------------------------------------------------------------------
    // Property 9 — full universal statement
    //
    // Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 12.7, 27.4
    // ------------------------------------------------------------------

    [Property]
    public void Property_9_Income_Tax_And_Net_Income_Composition(
        int seedGrossIncome,
        int seedMarketing,
        int seedOperations,
        int seedInterest,
        int rawMonthlyDepreciation,
        int rawIncomeTaxRate)
    {
        // Per-month vectors (36 entries each, seeded from distinct raw ints).
        var grossIncome = MakeSignedVector(seedGrossIncome);
        var marketingTotal = MakeNonNegativeVector(seedMarketing);
        var operationsTotal = MakeNonNegativeVector(seedOperations);
        var monthlyLoanInterest = MakeNonNegativeVector(seedInterest);

        // Scalars.
        var monthlyDepreciation = BoundNonNegative(rawMonthlyDepreciation);
        var incomeTaxRate = BoundRate(rawIncomeTaxRate);

        var result = IncomeTaxCalculator.Compute(
            grossIncome,
            marketingTotal,
            operationsTotal,
            monthlyLoanInterest,
            monthlyDepreciation,
            incomeTaxRate);

        // Length invariants (design §6.9 return-type contract).
        Assert.Equal(Months, result.ExpensesBeforeIncomeTax.Count);
        Assert.Equal(Months, result.PreTaxIncome.Count);
        Assert.Equal(Months, result.IncomeTax.Count);
        Assert.Equal(Months, result.TotalExpenses.Count);
        Assert.Equal(Months, result.NetIncome.Count);

        for (var m = 1; m <= Months; m++)
        {
            var i = m - 1;

            // R12.1: Expenses_Before_Income_Tax[m] composition.
            var expectedExpenses =
                marketingTotal[i]
                + operationsTotal[i]
                + monthlyLoanInterest[i]
                + monthlyDepreciation;
            Assert.Equal(expectedExpenses, result.ExpensesBeforeIncomeTax[i]);

            // R12.2: Pre_Tax_Income[m] = Gross_Income[m] − Expenses_Before_Income_Tax[m].
            var expectedPreTax = grossIncome[i] - expectedExpenses;
            Assert.Equal(expectedPreTax, result.PreTaxIncome[i]);

            // R12.3: Income_Tax[m] = max(Pre_Tax_Income[m], 0) × Income_Tax_Rate.
            var expectedTax = Math.Max(expectedPreTax, 0m) * incomeTaxRate;
            Assert.Equal(expectedTax, result.IncomeTax[i]);

            // R12.4: Pre_Tax_Income[m] ≤ 0 ⇒ Income_Tax[m] = 0.
            if (expectedPreTax <= 0m)
            {
                Assert.Equal(0m, result.IncomeTax[i]);
            }

            // R12.5: Total_Expenses[m] = Expenses_Before_Income_Tax[m] + Income_Tax[m].
            Assert.Equal(expectedExpenses + expectedTax, result.TotalExpenses[i]);

            // R12.6: Net_Income[m] = Gross_Income[m] − Total_Expenses[m].
            var expectedNet = grossIncome[i] - (expectedExpenses + expectedTax);
            Assert.Equal(expectedNet, result.NetIncome[i]);
        }

        // R27.4: Income_Tax_Rate == 0 ⇒ Income_Tax[m] = 0 for every month.
        //
        // Rather than asking FsCheck to hit rate == 0 by chance, we compute
        // a second forecast at rate = 0 with the same underlying vectors
        // and depreciation, then assert every element of Income_Tax is zero.
        // This structural check makes the R27.4 corollary universal rather
        // than probabilistic.
        var zeroRateResult = IncomeTaxCalculator.Compute(
            grossIncome,
            marketingTotal,
            operationsTotal,
            monthlyLoanInterest,
            monthlyDepreciation,
            incomeTaxRate: 0m);
        Assert.All(zeroRateResult.IncomeTax, tax => Assert.Equal(0m, tax));
    }
}
