// Unit tests for Pass 9 — Pre-tax income, income tax, and net income
// (design §6.9, §15.3 → IncomeTaxTests).
//
// Assumed internal API (matched by the Pass 9 implementation in task 31).
// Design §6.9 documents the per-month formulas but does not spell out the
// C# helper's exact name or signature, so this file assumes:
//
//     namespace RehearsalForecast.Core.Forecast;
//
//     internal sealed record IncomeTaxResult(
//         IReadOnlyList<decimal> ExpensesBeforeIncomeTax,  // length 36
//         IReadOnlyList<decimal> PreTaxIncome,              // length 36
//         IReadOnlyList<decimal> IncomeTax,                 // length 36
//         IReadOnlyList<decimal> TotalExpenses,             // length 36
//         IReadOnlyList<decimal> NetIncome);                // length 36
//
//     internal static class IncomeTaxCalculator
//     {
//         internal static IncomeTaxResult Compute(
//             IReadOnlyList<decimal> grossIncome,           // 36 monthly values from Pass 3
//             IReadOnlyList<decimal> marketingTotal,        // 36 monthly values from Pass 4
//             IReadOnlyList<decimal> operationsTotal,       // 36 monthly values from Pass 5
//             IReadOnlyList<decimal> monthlyLoanInterest,   // 36 monthly values (LoanSchedule.Entries[i].Interest)
//             decimal monthlyDepreciation,                  // scalar from Pass 6
//             decimal incomeTaxRate);                        // scalar in [0, 1] from TaxInputs
//     }
//
// Rationale for the assumption:
//   * Design §6.9 shows five per-month quantities (Expenses_Before_Income_Tax,
//     Pre_Tax_Income, Income_Tax, Total_Expenses, Net_Income) all derived from
//     four per-month input vectors plus two scalars (Monthly_Depreciation,
//     Income_Tax_Rate). Grouping the outputs into a single record keeps the
//     API surface small and makes it obvious that these five quantities move
//     together as a unit into ForecastCalculator's row assembly.
//   * Passing the four contributing vectors and the two scalars (rather than
//     the full ForecastInputs / RevenueResult / OperationsResult / LoanSchedule)
//     keeps this pass narrowly focused on the arithmetic it is responsible for
//     and makes its tests decoupled from earlier-pass details. It also
//     structurally guarantees that no other quantity can leak into the
//     computation (e.g., there is no channel for capital or owner-activity
//     inputs to reach this pass).
//   * The helper is `internal` so it is not part of the Web API surface; the
//     Core csproj's InternalsVisibleTo exposes it to this test project.
//
// If task 31 chooses a different helper name or signature, the test-method
// assertions do not need to change materially — only the Compute(...) call
// sites and the reflection-based structural tests near the bottom.
//
// Validates:
//   * Requirement 12.1 — Expenses_Before_Income_Tax[m] = Marketing_Total[m]
//                        + Operations_Total[m] + Monthly_Loan_Interest[m]
//                        + Monthly_Depreciation.
//   * Requirement 12.2 — Pre_Tax_Income[m] = Gross_Income[m] − Expenses_Before_Income_Tax[m].
//   * Requirement 12.3 — Income_Tax[m] = Max(Pre_Tax_Income[m], 0) × Income_Tax_Rate.
//   * Requirement 12.4 — Pre_Tax_Income[m] ≤ 0  ⇒  Income_Tax[m] = 0.
//   * Requirement 12.5 — Total_Expenses[m] = Expenses_Before_Income_Tax[m] + Income_Tax[m].
//   * Requirement 12.6 — Net_Income[m] = Gross_Income[m] − Total_Expenses[m].
//   * Requirement 12.7 — Losses are not carried forward across months.
//   * Requirement 22.2 — Test names identify the business rule under test.
//   * Requirement 27.4 — Income_Tax_Rate = 0 ⇒ Income_Tax[m] = 0 for every m.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Forecast;
using Xunit;

namespace RehearsalForecast.Core.Tests;

public class IncomeTaxTests
{
    private const int Months = ForecastConstants.ForecastMonths;

    // -----------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------

    /// <summary>Returns a 36-entry list where every element equals <paramref name="value"/>.</summary>
    private static IReadOnlyList<decimal> ConstVec(decimal value)
    {
        var xs = new decimal[Months];
        for (var i = 0; i < Months; i++)
        {
            xs[i] = value;
        }

        return xs;
    }

    /// <summary>Returns a 36-entry list where element at index <c>m-1</c> equals <c>step × m</c>.</summary>
    private static IReadOnlyList<decimal> Ramp(decimal step)
    {
        var xs = new decimal[Months];
        for (var m = 1; m <= Months; m++)
        {
            xs[m - 1] = step * m;
        }

        return xs;
    }

    /// <summary>Zero-filled 36-entry vector (a common baseline for isolating one input at a time).</summary>
    private static IReadOnlyList<decimal> Zeros() => ConstVec(0m);

    // -----------------------------------------------------------------
    // R12.1 & R22.2 — Expenses_Before_Income_Tax composition
    // -----------------------------------------------------------------

    [Fact]
    public void ExpensesBeforeIncomeTax_EqualsSumOfMarketingOperationsInterestAndDepreciation_ForEveryMonth()
    {
        // Distinct values per addend so any omitted or extra term is visible.
        var marketing = ConstVec(100m);
        var operations = ConstVec(200m);
        var interest = ConstVec(50m);
        const decimal monthlyDepreciation = 25m;

        var result = IncomeTaxCalculator.Compute(
            grossIncome: Zeros(),
            marketingTotal: marketing,
            operationsTotal: operations,
            monthlyLoanInterest: interest,
            monthlyDepreciation: monthlyDepreciation,
            incomeTaxRate: 0m);

        Assert.Equal(Months, result.ExpensesBeforeIncomeTax.Count);
        var expected = 100m + 200m + 50m + 25m; // 375
        Assert.All(result.ExpensesBeforeIncomeTax, e => Assert.Equal(expected, e));
    }

    [Fact]
    public void ExpensesBeforeIncomeTax_HonoursPerMonthVariation_InAllFourContributingInputs()
    {
        // Every vector varies month-to-month so no per-month term can be
        // silently substituted with a constant.
        var marketing = Ramp(1m);    //   1,   2, ...,   36
        var operations = Ramp(2m);   //   2,   4, ...,   72
        var interest = Ramp(3m);     //   3,   6, ...,  108
        const decimal depreciation = 10m;

        var result = IncomeTaxCalculator.Compute(
            grossIncome: Zeros(),
            marketingTotal: marketing,
            operationsTotal: operations,
            monthlyLoanInterest: interest,
            monthlyDepreciation: depreciation,
            incomeTaxRate: 0m);

        Assert.Equal(Months, result.ExpensesBeforeIncomeTax.Count);
        for (var m = 1; m <= Months; m++)
        {
            var expected = marketing[m - 1] + operations[m - 1] + interest[m - 1] + depreciation;
            Assert.Equal(expected, result.ExpensesBeforeIncomeTax[m - 1]);
        }
    }

    [Fact]
    public void ExpensesBeforeIncomeTax_IsMonthlyDepreciation_WhenAllOtherInputsAreZero()
    {
        // Depreciation is applied to every month identically (R8.2). With all
        // other contributing inputs zero, Expenses_Before_Income_Tax must
        // equal Monthly_Depreciation on every row.
        const decimal depreciation = 123.45m;

        var result = IncomeTaxCalculator.Compute(
            grossIncome: Zeros(),
            marketingTotal: Zeros(),
            operationsTotal: Zeros(),
            monthlyLoanInterest: Zeros(),
            monthlyDepreciation: depreciation,
            incomeTaxRate: 0m);

        Assert.All(result.ExpensesBeforeIncomeTax, e => Assert.Equal(depreciation, e));
    }

    // -----------------------------------------------------------------
    // R12.2 & R22.2 — Pre_Tax_Income = Gross_Income − Expenses_Before_Income_Tax
    // -----------------------------------------------------------------

    [Fact]
    public void PreTaxIncome_EqualsGrossIncomeMinusExpensesBeforeIncomeTax_ForEveryMonth()
    {
        var grossIncome = ConstVec(1_000m);
        var marketing = ConstVec(100m);
        var operations = ConstVec(200m);
        var interest = ConstVec(50m);
        const decimal depreciation = 25m;

        var result = IncomeTaxCalculator.Compute(
            grossIncome,
            marketing,
            operations,
            interest,
            depreciation,
            incomeTaxRate: 0m);

        var expectedExpenses = 100m + 200m + 50m + 25m; // 375
        var expectedPreTax = 1_000m - expectedExpenses; // 625
        Assert.Equal(Months, result.PreTaxIncome.Count);
        Assert.All(result.PreTaxIncome, p => Assert.Equal(expectedPreTax, p));
    }

    [Fact]
    public void PreTaxIncome_IsNegative_WhenExpensesExceedGrossIncome()
    {
        // R12.4 depends on Pre_Tax_Income legitimately going negative. Verify
        // the sign propagates through this pass (no clamping at zero).
        var result = IncomeTaxCalculator.Compute(
            grossIncome: ConstVec(100m),
            marketingTotal: ConstVec(500m),
            operationsTotal: Zeros(),
            monthlyLoanInterest: Zeros(),
            monthlyDepreciation: 0m,
            incomeTaxRate: 0.30m);

        Assert.All(result.PreTaxIncome, p => Assert.Equal(-400m, p));
    }

    [Fact]
    public void PreTaxIncome_TracksPerMonthGrossIncome_UnderVariableRevenue()
    {
        // Vary Gross_Income only; hold expenses constant across months.
        var grossIncome = Ramp(1_000m);      // 1000, 2000, ..., 36000
        const decimal depreciation = 100m;

        var result = IncomeTaxCalculator.Compute(
            grossIncome,
            marketingTotal: ConstVec(200m),
            operationsTotal: ConstVec(300m),
            monthlyLoanInterest: ConstVec(50m),
            monthlyDepreciation: depreciation,
            incomeTaxRate: 0m);

        var constantExpenses = 200m + 300m + 50m + depreciation; // 650
        for (var m = 1; m <= Months; m++)
        {
            Assert.Equal(grossIncome[m - 1] - constantExpenses, result.PreTaxIncome[m - 1]);
        }
    }

    // -----------------------------------------------------------------
    // R12.3, R12.4 & R22.2 — Income_Tax = Max(Pre_Tax_Income, 0) × Rate
    // -----------------------------------------------------------------

    [Fact]
    public void IncomeTax_EqualsMaxOfPreTaxIncomeAndZeroTimesIncomeTaxRate_WhenIncomeIsPositive()
    {
        const decimal rate = 0.25m;
        // Pre_Tax_Income = 1000 - (100 + 200 + 50 + 25) = 625 everywhere.
        var result = IncomeTaxCalculator.Compute(
            grossIncome: ConstVec(1_000m),
            marketingTotal: ConstVec(100m),
            operationsTotal: ConstVec(200m),
            monthlyLoanInterest: ConstVec(50m),
            monthlyDepreciation: 25m,
            incomeTaxRate: rate);

        var expectedTax = 625m * rate; // 156.25
        Assert.Equal(Months, result.IncomeTax.Count);
        Assert.All(result.IncomeTax, t => Assert.Equal(expectedTax, t));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.05)]
    [InlineData(0.21)]
    [InlineData(0.37)]
    [InlineData(1.0)]
    public void IncomeTax_ScalesLinearlyWithIncomeTaxRate_OnPositivePreTaxIncome(double rateD)
    {
        var rate = (decimal)rateD;
        // Pre_Tax_Income = 1000 - 100 = 900 everywhere.
        var result = IncomeTaxCalculator.Compute(
            grossIncome: ConstVec(1_000m),
            marketingTotal: ConstVec(100m),
            operationsTotal: Zeros(),
            monthlyLoanInterest: Zeros(),
            monthlyDepreciation: 0m,
            incomeTaxRate: rate);

        Assert.All(result.IncomeTax, t => Assert.Equal(900m * rate, t));
    }

    [Fact]
    public void IncomeTax_IsZero_OnLossMonths()
    {
        // Every month is a loss (Gross_Income = 0, expenses > 0). Regardless of
        // the tax rate, Income_Tax must be 0 on every row (R12.4).
        var result = IncomeTaxCalculator.Compute(
            grossIncome: Zeros(),
            marketingTotal: ConstVec(100m),
            operationsTotal: ConstVec(200m),
            monthlyLoanInterest: ConstVec(50m),
            monthlyDepreciation: 25m,
            incomeTaxRate: 0.35m);

        // Pre-tax income is -375 everywhere; tax must be zero.
        Assert.All(result.PreTaxIncome, p => Assert.Equal(-375m, p));
        Assert.All(result.IncomeTax, t => Assert.Equal(0m, t));
    }

    [Fact]
    public void IncomeTax_TreatsPreTaxIncomeOfExactlyZero_AsZeroTax()
    {
        // Boundary case: max(0, 0) × rate = 0. This distinguishes "Max(...,0)"
        // (R12.3) from a naive "if strictly positive" implementation.
        var result = IncomeTaxCalculator.Compute(
            grossIncome: ConstVec(100m),
            marketingTotal: ConstVec(100m),
            operationsTotal: Zeros(),
            monthlyLoanInterest: Zeros(),
            monthlyDepreciation: 0m,
            incomeTaxRate: 0.5m);

        Assert.All(result.PreTaxIncome, p => Assert.Equal(0m, p));
        Assert.All(result.IncomeTax, t => Assert.Equal(0m, t));
    }

    [Fact]
    public void IncomeTax_AppliesRateOnlyToPositivePreTaxIncome_InMixedProfitLossMonths()
    {
        // Construct a scenario where some months are profitable and others are
        // losses, then verify tax is nonzero only on the profitable ones.
        // Gross_Income ramps 1000, 2000, ..., 36000; expenses are a constant
        // 5000 per month, so months 1..4 are losses and months 5..36 are gains.
        var grossIncome = Ramp(1_000m);
        var result = IncomeTaxCalculator.Compute(
            grossIncome,
            marketingTotal: ConstVec(5_000m),
            operationsTotal: Zeros(),
            monthlyLoanInterest: Zeros(),
            monthlyDepreciation: 0m,
            incomeTaxRate: 0.10m);

        for (var m = 1; m <= Months; m++)
        {
            var preTax = grossIncome[m - 1] - 5_000m;
            var expectedTax = preTax > 0m ? preTax * 0.10m : 0m;
            Assert.Equal(preTax, result.PreTaxIncome[m - 1]);
            Assert.Equal(expectedTax, result.IncomeTax[m - 1]);
        }

        // Explicit spot checks around the profit / loss boundary.
        Assert.Equal(0m, result.IncomeTax[0]);   // month 1: pre-tax = -4000
        Assert.Equal(0m, result.IncomeTax[3]);   // month 4: pre-tax = -1000
        Assert.Equal(0m, result.IncomeTax[4]);   // month 5: pre-tax = 0 (boundary)
        Assert.Equal(100m, result.IncomeTax[5]); // month 6: pre-tax = 1000 → tax = 100
    }

    // -----------------------------------------------------------------
    // R12.7 & R22.2 — No cross-month carryforward of losses
    // -----------------------------------------------------------------

    [Fact]
    public void IncomeTax_DoesNotCarryLossesForward_AcrossMonths()
    {
        // Baseline: month 1 is a large loss, months 2..36 are profitable.
        // Mutating the size of the month-1 loss must NOT change the tax in any
        // other month (R12.7). We verify this by running two scenarios that
        // differ ONLY in month-1 expenses and comparing months 2..36.
        var grossIncome = Enumerable.Repeat(10_000m, Months).ToArray();

        var marketingSmallLoss = new decimal[Months];
        var marketingLargeLoss = new decimal[Months];
        for (var i = 0; i < Months; i++)
        {
            // Months 2..36: profitable (expenses < gross income).
            marketingSmallLoss[i] = 1_000m;
            marketingLargeLoss[i] = 1_000m;
        }

        // Month 1 alone differs. Both make the month a loss; the "large" case
        // is a much deeper loss.
        marketingSmallLoss[0] = 12_000m;   // pre-tax = -2,000
        marketingLargeLoss[0] = 100_000m;  // pre-tax = -90,000

        var small = IncomeTaxCalculator.Compute(
            grossIncome,
            marketingTotal: marketingSmallLoss,
            operationsTotal: Zeros(),
            monthlyLoanInterest: Zeros(),
            monthlyDepreciation: 0m,
            incomeTaxRate: 0.30m);

        var large = IncomeTaxCalculator.Compute(
            grossIncome,
            marketingTotal: marketingLargeLoss,
            operationsTotal: Zeros(),
            monthlyLoanInterest: Zeros(),
            monthlyDepreciation: 0m,
            incomeTaxRate: 0.30m);

        // Month 1 is a loss in both scenarios; tax stays 0.
        Assert.Equal(0m, small.IncomeTax[0]);
        Assert.Equal(0m, large.IncomeTax[0]);

        // Months 2..36 must be identical across the two scenarios (no
        // carryforward from month 1's deeper loss).
        for (var m = 2; m <= Months; m++)
        {
            Assert.Equal(small.PreTaxIncome[m - 1], large.PreTaxIncome[m - 1]);
            Assert.Equal(small.IncomeTax[m - 1], large.IncomeTax[m - 1]);
            Assert.Equal(small.TotalExpenses[m - 1], large.TotalExpenses[m - 1]);
            Assert.Equal(small.NetIncome[m - 1], large.NetIncome[m - 1]);
        }

        // Sanity: months 2..36 are actually profitable and taxed at 30%.
        for (var m = 2; m <= Months; m++)
        {
            Assert.Equal(9_000m, small.PreTaxIncome[m - 1]);   // 10,000 − 1,000
            Assert.Equal(2_700m, small.IncomeTax[m - 1]);      // 9,000 × 0.30
        }
    }

    // -----------------------------------------------------------------
    // R27.4 & R22.2 — Zero rate ⇒ zero tax everywhere
    // -----------------------------------------------------------------

    [Fact]
    public void IncomeTax_IsZeroEverywhere_WhenIncomeTaxRateIsZero()
    {
        // R27.4: Income_Tax_Rate = 0 collapses Income_Tax to 0 across all
        // months regardless of Pre_Tax_Income sign or magnitude.
        var result = IncomeTaxCalculator.Compute(
            grossIncome: Ramp(1_000m),        // very profitable
            marketingTotal: ConstVec(50m),
            operationsTotal: ConstVec(100m),
            monthlyLoanInterest: ConstVec(25m),
            monthlyDepreciation: 10m,
            incomeTaxRate: 0m);

        Assert.All(result.IncomeTax, t => Assert.Equal(0m, t));
    }

    [Fact]
    public void TotalExpenses_EqualsExpensesBeforeIncomeTax_WhenIncomeTaxRateIsZero()
    {
        // Corollary of R27.4: with zero tax, Total_Expenses collapses to
        // Expenses_Before_Income_Tax on every row.
        var result = IncomeTaxCalculator.Compute(
            grossIncome: Ramp(500m),
            marketingTotal: ConstVec(50m),
            operationsTotal: ConstVec(100m),
            monthlyLoanInterest: ConstVec(25m),
            monthlyDepreciation: 10m,
            incomeTaxRate: 0m);

        for (var m = 1; m <= Months; m++)
        {
            Assert.Equal(
                result.ExpensesBeforeIncomeTax[m - 1],
                result.TotalExpenses[m - 1]);
        }
    }

    // -----------------------------------------------------------------
    // R12.5 & R22.2 — Total_Expenses composition
    // -----------------------------------------------------------------

    [Fact]
    public void TotalExpenses_EqualsExpensesBeforeIncomeTaxPlusIncomeTax_ForEveryMonth()
    {
        const decimal rate = 0.20m;
        var result = IncomeTaxCalculator.Compute(
            grossIncome: ConstVec(1_000m),
            marketingTotal: ConstVec(100m),
            operationsTotal: ConstVec(200m),
            monthlyLoanInterest: ConstVec(50m),
            monthlyDepreciation: 25m,
            incomeTaxRate: rate);

        Assert.Equal(Months, result.TotalExpenses.Count);
        for (var m = 1; m <= Months; m++)
        {
            var expected = result.ExpensesBeforeIncomeTax[m - 1] + result.IncomeTax[m - 1];
            Assert.Equal(expected, result.TotalExpenses[m - 1]);
        }
    }

    [Fact]
    public void TotalExpenses_HonoursExactHandTranscribedComposition_UnderMixedProfitLossMonths()
    {
        // Concrete, month-by-month check: Total_Expenses[m] is the exact sum
        // of the four expense inputs plus the applicable Income_Tax.
        var grossIncome = Ramp(500m);         // 500, 1000, ..., 18000
        var marketing = ConstVec(400m);
        var operations = ConstVec(300m);
        var interest = ConstVec(100m);
        const decimal depreciation = 50m;
        const decimal rate = 0.25m;

        var result = IncomeTaxCalculator.Compute(
            grossIncome,
            marketing,
            operations,
            interest,
            depreciation,
            rate);

        for (var m = 1; m <= Months; m++)
        {
            var expenses = 400m + 300m + 100m + 50m; // 850 every month
            var preTax = grossIncome[m - 1] - expenses;
            var tax = preTax > 0m ? preTax * rate : 0m;

            Assert.Equal(expenses, result.ExpensesBeforeIncomeTax[m - 1]);
            Assert.Equal(preTax, result.PreTaxIncome[m - 1]);
            Assert.Equal(tax, result.IncomeTax[m - 1]);
            Assert.Equal(expenses + tax, result.TotalExpenses[m - 1]);
        }
    }

    // -----------------------------------------------------------------
    // R12.6 & R22.2 — Net_Income = Gross_Income − Total_Expenses
    // -----------------------------------------------------------------

    [Fact]
    public void NetIncome_EqualsGrossIncomeMinusTotalExpenses_ForEveryMonth()
    {
        const decimal rate = 0.20m;
        var grossIncome = ConstVec(1_000m);
        var result = IncomeTaxCalculator.Compute(
            grossIncome,
            marketingTotal: ConstVec(100m),
            operationsTotal: ConstVec(200m),
            monthlyLoanInterest: ConstVec(50m),
            monthlyDepreciation: 25m,
            incomeTaxRate: rate);

        Assert.Equal(Months, result.NetIncome.Count);
        for (var m = 1; m <= Months; m++)
        {
            var expected = grossIncome[m - 1] - result.TotalExpenses[m - 1];
            Assert.Equal(expected, result.NetIncome[m - 1]);
        }
    }

    [Fact]
    public void NetIncome_IsNegative_OnLossMonthsAndEqualsGrossIncomeMinusExpensesBeforeIncomeTax()
    {
        // On loss months, Income_Tax = 0 (R12.4), so Total_Expenses ==
        // Expenses_Before_Income_Tax and Net_Income == Pre_Tax_Income
        // (both negative).
        var result = IncomeTaxCalculator.Compute(
            grossIncome: ConstVec(100m),
            marketingTotal: ConstVec(1_000m),
            operationsTotal: Zeros(),
            monthlyLoanInterest: Zeros(),
            monthlyDepreciation: 0m,
            incomeTaxRate: 0.35m);

        for (var m = 1; m <= Months; m++)
        {
            Assert.Equal(-900m, result.PreTaxIncome[m - 1]);
            Assert.Equal(0m, result.IncomeTax[m - 1]);
            Assert.Equal(-900m, result.NetIncome[m - 1]);
            Assert.Equal(result.PreTaxIncome[m - 1], result.NetIncome[m - 1]);
        }
    }

    [Fact]
    public void NetIncome_EqualsPreTaxIncomeMinusIncomeTax_UnderAlgebraicRestatement()
    {
        // Net_Income = Gross_Income − (Expenses_Before_Income_Tax + Income_Tax)
        //            = (Gross_Income − Expenses_Before_Income_Tax) − Income_Tax
        //            = Pre_Tax_Income − Income_Tax.
        // Verifies the arithmetic identity across a mix of profitable and
        // loss months.
        var grossIncome = Ramp(300m);
        var result = IncomeTaxCalculator.Compute(
            grossIncome,
            marketingTotal: ConstVec(2_000m),
            operationsTotal: ConstVec(500m),
            monthlyLoanInterest: ConstVec(100m),
            monthlyDepreciation: 50m,
            incomeTaxRate: 0.21m);

        for (var m = 1; m <= Months; m++)
        {
            Assert.Equal(
                result.PreTaxIncome[m - 1] - result.IncomeTax[m - 1],
                result.NetIncome[m - 1]);
        }
    }

    // -----------------------------------------------------------------
    // Structural checks
    // -----------------------------------------------------------------

    [Fact]
    public void IncomeTaxResult_HasExactly36EntriesInEveryVector()
    {
        var result = IncomeTaxCalculator.Compute(
            grossIncome: ConstVec(1_000m),
            marketingTotal: ConstVec(100m),
            operationsTotal: ConstVec(200m),
            monthlyLoanInterest: ConstVec(50m),
            monthlyDepreciation: 25m,
            incomeTaxRate: 0.20m);

        Assert.Equal(Months, result.ExpensesBeforeIncomeTax.Count);
        Assert.Equal(Months, result.PreTaxIncome.Count);
        Assert.Equal(Months, result.IncomeTax.Count);
        Assert.Equal(Months, result.TotalExpenses.Count);
        Assert.Equal(Months, result.NetIncome.Count);
    }

    [Fact]
    public void IncomeTaxCalculator_ComputeSignature_AcceptsOnlyTheDocumentedInputs()
    {
        // Structural guarantee: the helper's parameter list admits no channel
        // for capital, owner-activity, or unrelated inputs to influence the
        // pre-tax / tax / net-income computation. Any future signature
        // widening will trip this test and prompt a design conversation.
        var computeMethods = typeof(IncomeTaxCalculator)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)
            .Where(m => m.Name == "Compute")
            .ToList();

        Assert.Single(computeMethods);

        var parameters = computeMethods[0].GetParameters();
        Assert.Equal(6, parameters.Length);
        Assert.Equal(typeof(IReadOnlyList<decimal>), parameters[0].ParameterType);
        Assert.Equal(typeof(IReadOnlyList<decimal>), parameters[1].ParameterType);
        Assert.Equal(typeof(IReadOnlyList<decimal>), parameters[2].ParameterType);
        Assert.Equal(typeof(IReadOnlyList<decimal>), parameters[3].ParameterType);
        Assert.Equal(typeof(decimal), parameters[4].ParameterType);
        Assert.Equal(typeof(decimal), parameters[5].ParameterType);
    }

    [Fact]
    public void IncomeTaxCalculator_ReturnsSameResultAcrossRepeatedCalls_WithSameInputs()
    {
        // Determinism / referential transparency: identical inputs must
        // produce identical outputs on every invocation.
        var grossIncome = Ramp(500m);
        var marketing = ConstVec(200m);
        var operations = ConstVec(300m);
        var interest = ConstVec(50m);
        const decimal depreciation = 25m;
        const decimal rate = 0.25m;

        var first = IncomeTaxCalculator.Compute(
            grossIncome, marketing, operations, interest, depreciation, rate);
        var second = IncomeTaxCalculator.Compute(
            grossIncome, marketing, operations, interest, depreciation, rate);

        for (var m = 1; m <= Months; m++)
        {
            Assert.Equal(first.ExpensesBeforeIncomeTax[m - 1], second.ExpensesBeforeIncomeTax[m - 1]);
            Assert.Equal(first.PreTaxIncome[m - 1], second.PreTaxIncome[m - 1]);
            Assert.Equal(first.IncomeTax[m - 1], second.IncomeTax[m - 1]);
            Assert.Equal(first.TotalExpenses[m - 1], second.TotalExpenses[m - 1]);
            Assert.Equal(first.NetIncome[m - 1], second.NetIncome[m - 1]);
        }
    }
}
