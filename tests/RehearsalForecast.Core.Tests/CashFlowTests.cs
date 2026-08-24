// Tests for the cash-flow roll-forward pass
// (design §6.10, §15.3 → CashFlowTests).
//
// These tests are written tests-first against the intended internal API that
// task 33 (Pass 10) will introduce. Design §6.10 fixes the arithmetic — the
// full Requirement 13.4 accounting identity — but does not spell out the C#
// helper's exact name or signature, so this file assumes the surface given
// by task 32's prompt:
//
//     namespace RehearsalForecast.Core.Forecast;
//
//     internal static class CashFlowCalculator
//     {
//         internal static CashFlowResult Compute(
//             decimal beginningCashMonth1,
//             IReadOnlyList<decimal> netIncome,                     // length 36
//             decimal monthlyDepreciation,                          // scalar (R8.2)
//             IReadOnlyList<decimal> monthlyLoanPrincipal,          // length 36
//             IReadOnlyList<decimal> capitalExpendituresInMonth,    // length 36
//             IReadOnlyList<decimal> ownerInvestmentInMonth,        // length 36
//             IReadOnlyList<decimal> loanProceedsInMonth,           // length 36
//             decimal ownerWithdrawals);                            // scalar (R1.6, DD8)
//     }
//
//     internal sealed record CashFlowResult(
//         IReadOnlyList<decimal> BeginningCash,                     // length 36
//         IReadOnlyList<decimal> EndingCash);                       // length 36
//
// Rationale for the assumption:
//   * §6.10 evaluates the roll-forward from the outputs of earlier passes:
//     Net_Income (§6.9), Monthly_Depreciation (§6.6, scalar), Monthly_Loan_Principal
//     (from LoanSchedule.Entries[m − 1].Principal, §6.8), the three "in-month"
//     vectors from Pass 7 (§6.7), and the scalar Owner_Withdrawals (R1.6, DD8).
//     Passing these as bare per-month vectors and scalars keeps the helper
//     independent of the full ForecastInputs / partial-forecast records, so it
//     can be unit-tested with concise, hand-crafted inputs.
//   * Monthly_Depreciation is a single decimal, not a 36-vector: R8.2 guarantees
//     "identical across all 36 months", and Pass 6 (§6.6) already returns a scalar.
//     Modelling it as a scalar here preserves that structural invariant end-to-end.
//   * Owner_Withdrawals is a single decimal for the same reason (R1.6, DD8) —
//     Variable_Mode is deliberately not supported for withdrawals in this phase.
//   * Monthly_Loan_Principal is a length-36 vector rather than a LoanSchedule
//     because Pass 10 needs only the Principal column of the schedule
//     (R11.14 — "SHALL subtract only Monthly_Loan_Principal[m]"). Threading the
//     entire schedule would give the helper a channel through which
//     Monthly_Loan_Interest could accidentally reach the cash-flow line, which
//     R11.14 explicitly forbids. Taking only the principal vector makes the
//     "interest never reaches cash flow" property structural rather than an
//     assertion we would have to make repeatedly.
//   * CashFlowResult is a minimal record: only the two per-month vectors the
//     cash-flow pass produces. Later passes (Cash_Positive_Rule / FSNM,
//     Pass 11, §6.11) consume Ending_Cash directly.
//   * The helper is `internal` so it is not part of the Web API surface;
//     `InternalsVisibleTo` on the Core csproj exposes it to this test project.
//
// If task 33 chooses a different helper name, a different container record,
// or reorders/renames the parameters, only the `CashFlowCalculator.Compute(...)`
// call sites and the reflection-based structural test near the bottom of this
// file need to change; the arithmetic assertions themselves remain the
// specification's arithmetic.
//
// Validates:
//   * Requirement 13.1 — Exactly 36 monthly cash-flow records.
//   * Requirement 13.2 — Beginning_Cash[1] = user-supplied opening cash.
//   * Requirement 13.3 — Beginning_Cash[m] = Ending_Cash[m − 1] for m ∈ [2, 36].
//   * Requirement 13.4 — Ending_Cash[m] = Beginning_Cash[m]
//                        + Owner_Investment_In_Month[m] + Loan_Proceeds_In_Month[m]
//                        + Net_Income[m] + Monthly_Depreciation
//                        − Capital_Expenditures_In_Month[m]
//                        − Monthly_Loan_Principal[m] − Owner_Withdrawals.
//   * Requirement 13.5 — Monthly_Depreciation is added back as a non-cash expense.
//   * Requirement 13.6 — Owner_Withdrawals is applied uniformly to every month.
//   * Requirement 13.7 — Sign convention: additions increase Ending_Cash,
//                        subtractions decrease it (matches R13.4 verbatim).
//   * Requirement 11.14 — Only Monthly_Loan_Principal[m] is subtracted for
//                         loan servicing; Monthly_Loan_Interest is NOT subtracted.
//   * Requirement 22.2 — Test names identify the business rule under test.
//   * Requirement 27.5 — When Owner_Withdrawals = 0, no withdrawal is subtracted
//                        in any month.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Forecast;
using Xunit;

namespace RehearsalForecast.Core.Tests;

public class CashFlowTests
{
    private const int Months = ForecastConstants.ForecastMonths;

    // ==================================================================
    // Helpers
    // ==================================================================

    /// <summary>Returns a 36-entry vector of zeros.</summary>
    private static IReadOnlyList<decimal> Zeros() => new decimal[Months];

    /// <summary>
    /// Returns a 36-entry vector whose only nonzero slot is Month 1
    /// (index 0). Models the Month-1 timing of Capital_Expenditures_In_Month,
    /// Owner_Investment_In_Month, and Loan_Proceeds_In_Month per §6.7.
    /// </summary>
    private static IReadOnlyList<decimal> Month1Only(decimal value)
    {
        var xs = new decimal[Months];
        xs[0] = value;
        return xs;
    }

    /// <summary>Returns a 36-entry vector with the same value in every month.</summary>
    private static IReadOnlyList<decimal> Constant(decimal value)
    {
        var xs = new decimal[Months];
        for (var i = 0; i < Months; i++) xs[i] = value;
        return xs;
    }

    /// <summary>
    /// Returns a distinct-per-month ramp: <c>step, 2·step, 3·step, …, 36·step</c>.
    /// Used to make per-month errors visible (a dropped or duplicated month
    /// would break the identity at a specific index).
    /// </summary>
    private static IReadOnlyList<decimal> Ramp(decimal step)
    {
        var xs = new decimal[Months];
        for (var i = 0; i < Months; i++) xs[i] = step * (i + 1);
        return xs;
    }

    /// <summary>
    /// Pre-computes the Requirement 13.4 accounting identity for every month
    /// from the same primitive inputs the SUT receives, so tests can assert
    /// against a specification-derived value rather than a hand-picked number.
    /// This mirrors the roll-forward exactly: Beginning_Cash[1] equals the
    /// supplied opening cash (R13.2) and Beginning_Cash[m] equals the previous
    /// month's Ending_Cash for m ≥ 2 (R13.3).
    /// </summary>
    private static (decimal[] Beginning, decimal[] Ending) ExpectedIdentity(
        decimal beginningCashMonth1,
        IReadOnlyList<decimal> netIncome,
        decimal monthlyDepreciation,
        IReadOnlyList<decimal> monthlyLoanPrincipal,
        IReadOnlyList<decimal> capitalExpendituresInMonth,
        IReadOnlyList<decimal> ownerInvestmentInMonth,
        IReadOnlyList<decimal> loanProceedsInMonth,
        decimal ownerWithdrawals)
    {
        var beginning = new decimal[Months];
        var ending = new decimal[Months];

        for (var i = 0; i < Months; i++)
        {
            beginning[i] = i == 0 ? beginningCashMonth1 : ending[i - 1];

            // Requirement 13.4 verbatim.
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

        return (beginning, ending);
    }

    /// <summary>
    /// A rich baseline scenario used across identity-preserving tests. Every
    /// input is nonzero to exercise every term of the R13.4 identity, and the
    /// per-month vectors use ramps so a per-index mismatch would show up in
    /// the failure message rather than being masked by uniform values.
    /// </summary>
    private static CashFlowResult ComputeBaseline(
        decimal beginningCashMonth1 = 50_000m,
        decimal monthlyDepreciation = 5_000m,
        decimal ownerWithdrawals = 2_000m)
    {
        return CashFlowCalculator.Compute(
            beginningCashMonth1: beginningCashMonth1,
            netIncome: Ramp(100m),                            // 100, 200, …, 3600
            monthlyDepreciation: monthlyDepreciation,
            monthlyLoanPrincipal: Constant(1_500m),           // level loan principal
            capitalExpendituresInMonth: Month1Only(100_000m),
            ownerInvestmentInMonth: Month1Only(30_000m),
            loanProceedsInMonth: Month1Only(70_000m),
            ownerWithdrawals: ownerWithdrawals);
    }

    // ==================================================================
    // R13.1 — Exactly 36 monthly cash-flow records.
    // ==================================================================

    [Fact]
    public void CashFlowResult_BeginningCash_HasExactlyThirtySixEntries()
    {
        var result = ComputeBaseline();

        Assert.Equal(Months, result.BeginningCash.Count);
    }

    [Fact]
    public void CashFlowResult_EndingCash_HasExactlyThirtySixEntries()
    {
        var result = ComputeBaseline();

        Assert.Equal(Months, result.EndingCash.Count);
    }

    // ==================================================================
    // R13.2 — Beginning_Cash[1] = user-supplied opening cash.
    // ==================================================================

    [Fact]
    public void BeginningCash_Month1_EqualsBeginningCashMonth1_Argument()
    {
        // The opening cash is passed straight through to Beginning_Cash[1]
        // without any transformation (no add-back, no clamp).
        var result = CashFlowCalculator.Compute(
            beginningCashMonth1: 12_345.67m,
            netIncome: Zeros(),
            monthlyDepreciation: 0m,
            monthlyLoanPrincipal: Zeros(),
            capitalExpendituresInMonth: Zeros(),
            ownerInvestmentInMonth: Zeros(),
            loanProceedsInMonth: Zeros(),
            ownerWithdrawals: 0m);

        Assert.Equal(12_345.67m, result.BeginningCash[0]);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(100_000.0)]
    [InlineData(-25_000.0)] // Beginning_Cash may legitimately be negative pre-financing.
    public void BeginningCash_Month1_EqualsBeginningCashMonth1_Argument_AcrossValues(double openingCashD)
    {
        var openingCash = (decimal)openingCashD;

        var result = CashFlowCalculator.Compute(
            beginningCashMonth1: openingCash,
            netIncome: Zeros(),
            monthlyDepreciation: 0m,
            monthlyLoanPrincipal: Zeros(),
            capitalExpendituresInMonth: Zeros(),
            ownerInvestmentInMonth: Zeros(),
            loanProceedsInMonth: Zeros(),
            ownerWithdrawals: 0m);

        Assert.Equal(openingCash, result.BeginningCash[0]);
    }

    // ==================================================================
    // R13.3 — Beginning_Cash[m] = Ending_Cash[m − 1] for m ∈ [2, 36].
    // ==================================================================

    [Fact]
    public void BeginningCash_ForMonthsTwoThroughThirtySix_EqualsPreviousMonthEndingCash()
    {
        // Rich baseline: every term of the identity is nonzero, so the
        // roll-forward chain is exercised at every step (a stale
        // Beginning_Cash[m] would diverge from Ending_Cash[m − 1] on any month).
        var result = ComputeBaseline();

        for (var i = 1; i < Months; i++)
        {
            Assert.Equal(result.EndingCash[i - 1], result.BeginningCash[i]);
        }
    }

    // ==================================================================
    // R13.4 & R13.7 — Full accounting identity for every month.
    // ==================================================================

    [Fact]
    public void EndingCash_EachMonth_MatchesRequirement13_4_Identity_ForRichMixedInputs()
    {
        // Every input contributes: opening cash, ramped net income (per-month
        // variation), non-zero depreciation add-back, level loan principal
        // subtraction, Month-1 capex/owner-investment/loan-proceeds timing,
        // and non-zero owner withdrawals. Any dropped or double-counted term
        // would fail the assertion at the specific offending month.
        var beginningCashMonth1 = 50_000m;
        var netIncome = Ramp(100m);
        var monthlyDepreciation = 5_000m;
        var monthlyLoanPrincipal = Constant(1_500m);
        var capitalExpendituresInMonth = Month1Only(100_000m);
        var ownerInvestmentInMonth = Month1Only(30_000m);
        var loanProceedsInMonth = Month1Only(70_000m);
        var ownerWithdrawals = 2_000m;

        var result = CashFlowCalculator.Compute(
            beginningCashMonth1,
            netIncome,
            monthlyDepreciation,
            monthlyLoanPrincipal,
            capitalExpendituresInMonth,
            ownerInvestmentInMonth,
            loanProceedsInMonth,
            ownerWithdrawals);

        var (expectedBeginning, expectedEnding) = ExpectedIdentity(
            beginningCashMonth1,
            netIncome,
            monthlyDepreciation,
            monthlyLoanPrincipal,
            capitalExpendituresInMonth,
            ownerInvestmentInMonth,
            loanProceedsInMonth,
            ownerWithdrawals);

        for (var i = 0; i < Months; i++)
        {
            Assert.Equal(expectedBeginning[i], result.BeginningCash[i]);
            Assert.Equal(expectedEnding[i], result.EndingCash[i]);
        }
    }

    [Fact]
    public void EndingCash_Month1_MatchesRequirement13_4_Identity_HandComputed()
    {
        // Human-checkable Month-1 case: all values are round numbers so the
        // expected result can be verified on paper against the R13.4 formula.
        //
        //   Beginning_Cash[1]                 =  50,000
        // + Owner_Investment_In_Month[1]      = +30,000
        // + Loan_Proceeds_In_Month[1]         = +70,000
        // + Net_Income[1]                     = +10,000
        // + Monthly_Depreciation              =  +5,000
        // − Capital_Expenditures_In_Month[1]  = −100,000
        // − Monthly_Loan_Principal[1]         =  −1,500
        // − Owner_Withdrawals                 =  −2,000
        //                                       ────────
        //   Ending_Cash[1]                    =  61,500
        var netIncome = new decimal[Months];
        netIncome[0] = 10_000m;

        var monthlyLoanPrincipal = new decimal[Months];
        monthlyLoanPrincipal[0] = 1_500m;

        var result = CashFlowCalculator.Compute(
            beginningCashMonth1: 50_000m,
            netIncome: netIncome,
            monthlyDepreciation: 5_000m,
            monthlyLoanPrincipal: monthlyLoanPrincipal,
            capitalExpendituresInMonth: Month1Only(100_000m),
            ownerInvestmentInMonth: Month1Only(30_000m),
            loanProceedsInMonth: Month1Only(70_000m),
            ownerWithdrawals: 2_000m);

        Assert.Equal(61_500m, result.EndingCash[0]);
    }

    // ==================================================================
    // R13.5 — Monthly_Depreciation is added back explicitly.
    //
    // Structural test strategy: hold all other inputs constant, run two
    // scenarios with monthlyDepreciation = 0 and monthlyDepreciation = D,
    // and assert that Ending_Cash[m] rises by exactly m · D. The cumulative
    // m · D reflects the roll-forward: each month's add-back also flows
    // forward into every subsequent Beginning_Cash.
    // ==================================================================

    [Fact]
    public void EndingCash_AddsBack_MonthlyDepreciation_ExplicitlyEveryMonth()
    {
        // Scenario chosen so Net_Income[m] can be strongly negative — this
        // is exactly the case where the depreciation add-back matters most:
        // depreciation was subtracted inside Net_Income (via
        // Expenses_Before_Income_Tax, §6.9) but is non-cash and must be
        // added back to Ending_Cash (§6.10, R13.5).
        const decimal depreciation = 5_000m;

        var withoutAddBack = CashFlowCalculator.Compute(
            beginningCashMonth1: 0m,
            netIncome: Constant(-1_000m),
            monthlyDepreciation: 0m,
            monthlyLoanPrincipal: Zeros(),
            capitalExpendituresInMonth: Zeros(),
            ownerInvestmentInMonth: Zeros(),
            loanProceedsInMonth: Zeros(),
            ownerWithdrawals: 0m);

        var withAddBack = CashFlowCalculator.Compute(
            beginningCashMonth1: 0m,
            netIncome: Constant(-1_000m),
            monthlyDepreciation: depreciation,
            monthlyLoanPrincipal: Zeros(),
            capitalExpendituresInMonth: Zeros(),
            ownerInvestmentInMonth: Zeros(),
            loanProceedsInMonth: Zeros(),
            ownerWithdrawals: 0m);

        // Delta at month m = m · depreciation (cumulative roll-forward).
        for (var i = 0; i < Months; i++)
        {
            var delta = withAddBack.EndingCash[i] - withoutAddBack.EndingCash[i];
            Assert.Equal((i + 1) * depreciation, delta);
        }
    }

    [Fact]
    public void EndingCash_AddsBack_MonthlyDepreciation_UsingTheSameScalarValueForEveryMonth()
    {
        // R8.2 / R13.5 structural: the helper takes a single decimal for
        // Monthly_Depreciation, so there is no channel through which the
        // add-back could vary across months. The parameter type itself
        // enforces "identical add-back for every m ∈ [1, 36]".
        var compute = typeof(CashFlowCalculator)
            .GetMethod(
                "Compute",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(compute);

        var depreciationParameter = compute!
            .GetParameters()
            .Single(p => p.Name == "monthlyDepreciation");

        Assert.Equal(typeof(decimal), depreciationParameter.ParameterType);
    }

    // ==================================================================
    // R11.14 — Only Monthly_Loan_Principal[m] is subtracted;
    //          Monthly_Loan_Interest is NOT subtracted in cash flow.
    // ==================================================================

    [Fact]
    public void CashFlowCalculator_ComputeSignature_ExposesLoanPrincipal_ButNoLoanInterest()
    {
        // R11.14 structural guarantee: the helper's parameter list must
        // include monthlyLoanPrincipal (so R13.4 can subtract it) but MUST
        // NOT include any interest parameter — Monthly_Loan_Interest was
        // already counted as an expense inside Net_Income (§6.9); a second
        // subtraction in the cash-flow line would double-count it.
        var compute = typeof(CashFlowCalculator)
            .GetMethod(
                "Compute",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(compute);

        var parameterNames = compute!
            .GetParameters()
            .Select(p => p.Name!)
            .ToArray();

        Assert.Contains("monthlyLoanPrincipal", parameterNames);
        Assert.DoesNotContain(parameterNames, n =>
            n.Contains("interest", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EndingCash_Subtracts_MonthlyLoanPrincipal_ExactlyOncePerMonth()
    {
        // Arithmetic corollary of R11.14: increasing loan principal by P per
        // month decreases Ending_Cash[m] by exactly m · P (cumulative through
        // roll-forward). A double-subtraction would move the delta to 2·m·P
        // and this test would fail.
        const decimal extraPrincipal = 500m;

        var baseline = CashFlowCalculator.Compute(
            beginningCashMonth1: 0m,
            netIncome: Zeros(),
            monthlyDepreciation: 0m,
            monthlyLoanPrincipal: Zeros(),
            capitalExpendituresInMonth: Zeros(),
            ownerInvestmentInMonth: Zeros(),
            loanProceedsInMonth: Zeros(),
            ownerWithdrawals: 0m);

        var withPrincipal = CashFlowCalculator.Compute(
            beginningCashMonth1: 0m,
            netIncome: Zeros(),
            monthlyDepreciation: 0m,
            monthlyLoanPrincipal: Constant(extraPrincipal),
            capitalExpendituresInMonth: Zeros(),
            ownerInvestmentInMonth: Zeros(),
            loanProceedsInMonth: Zeros(),
            ownerWithdrawals: 0m);

        for (var i = 0; i < Months; i++)
        {
            var delta = withPrincipal.EndingCash[i] - baseline.EndingCash[i];
            Assert.Equal(-(i + 1) * extraPrincipal, delta);
        }
    }

    // ==================================================================
    // Capital_Expenditures_In_Month is subtracted only in Month 1.
    // (Design §6.7: Capital_Expenditures_In_Month[1] = Total_Capital;
    //  Capital_Expenditures_In_Month[m > 1] = 0. Pass 10 must respect that
    //  timing without adding month-specific behaviour of its own.)
    // ==================================================================

    [Fact]
    public void EndingCash_Subtracts_CapitalExpenditures_Only_When_VectorHasNonZeroInMonth1()
    {
        // With capex vector [X, 0, 0, ..., 0] (the shape Pass 7 emits), the
        // Ending_Cash impact must appear in Month 1 and then persist through
        // every subsequent month via the roll-forward (Beginning_Cash[m] =
        // Ending_Cash[m − 1]). It must NOT be re-subtracted in months 2..36.
        const decimal totalCapital = 100_000m;

        var withoutCapex = CashFlowCalculator.Compute(
            beginningCashMonth1: 0m,
            netIncome: Zeros(),
            monthlyDepreciation: 0m,
            monthlyLoanPrincipal: Zeros(),
            capitalExpendituresInMonth: Zeros(),
            ownerInvestmentInMonth: Zeros(),
            loanProceedsInMonth: Zeros(),
            ownerWithdrawals: 0m);

        var withCapex = CashFlowCalculator.Compute(
            beginningCashMonth1: 0m,
            netIncome: Zeros(),
            monthlyDepreciation: 0m,
            monthlyLoanPrincipal: Zeros(),
            capitalExpendituresInMonth: Month1Only(totalCapital),
            ownerInvestmentInMonth: Zeros(),
            loanProceedsInMonth: Zeros(),
            ownerWithdrawals: 0m);

        // Every month should be reduced by exactly totalCapital (not m ·
        // totalCapital), because the capex is subtracted only once — in
        // Month 1 — and then rolls forward unchanged.
        for (var i = 0; i < Months; i++)
        {
            var delta = withCapex.EndingCash[i] - withoutCapex.EndingCash[i];
            Assert.Equal(-totalCapital, delta);
        }
    }

    [Fact]
    public void EndingCash_Ignores_Zero_CapitalExpenditure_Entries_ForMonthsTwoThroughThirtySix()
    {
        // Complementary shape check: given a capex vector whose Month-1 slot
        // is zero, no capex reduction should occur in any month. Together
        // with the previous test this pins down "capex is subtracted only in
        // Month 1" to precisely the vector's Month-1 slot.
        var result = CashFlowCalculator.Compute(
            beginningCashMonth1: 0m,
            netIncome: Zeros(),
            monthlyDepreciation: 0m,
            monthlyLoanPrincipal: Zeros(),
            capitalExpendituresInMonth: Zeros(),
            ownerInvestmentInMonth: Zeros(),
            loanProceedsInMonth: Zeros(),
            ownerWithdrawals: 0m);

        Assert.All(result.EndingCash, v => Assert.Equal(0m, v));
    }

    // ==================================================================
    // R13.6 — Owner_Withdrawals is applied uniformly to every month.
    // ==================================================================

    [Fact]
    public void EndingCash_Reduces_By_OwnerWithdrawals_Every_Month_Uniformly()
    {
        // Same inputs, only ownerWithdrawals differs. Because the same W is
        // subtracted every month and rolls forward, Ending_Cash[m] drops by
        // exactly m · W. That "· m" is the uniformity signature: any month
        // that skipped the subtraction would show a smaller drop; any month
        // that double-subtracted would show a larger drop.
        const decimal withdrawal = 2_000m;

        var without = ComputeBaseline(ownerWithdrawals: 0m);
        var with = ComputeBaseline(ownerWithdrawals: withdrawal);

        for (var i = 0; i < Months; i++)
        {
            var delta = with.EndingCash[i] - without.EndingCash[i];
            Assert.Equal(-(i + 1) * withdrawal, delta);
        }
    }

    [Fact]
    public void OwnerWithdrawals_IsScalar_NotAMonthlySchedule_InTheComputeSignature()
    {
        // DD8 & R1.6 structural guarantee: Owner_Withdrawals has no
        // Variable_Mode toggle in this phase. The helper models this by
        // taking a bare decimal — there is no per-month channel through
        // which the withdrawal could vary.
        var compute = typeof(CashFlowCalculator)
            .GetMethod(
                "Compute",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(compute);

        var withdrawalParameter = compute!
            .GetParameters()
            .Single(p => p.Name == "ownerWithdrawals");

        Assert.Equal(typeof(decimal), withdrawalParameter.ParameterType);
    }

    // ==================================================================
    // R27.5 — Owner_Withdrawals = 0 has no effect.
    // ==================================================================

    [Fact]
    public void EndingCash_UnchangedWhen_OwnerWithdrawals_IsZero()
    {
        // Two runs identical except that one omits withdrawals and the
        // other explicitly passes 0 must produce byte-identical results
        // (R27.5): "shall not subtract any withdrawal in any month".
        var beginningCashMonth1 = 50_000m;
        var netIncome = Ramp(100m);
        var monthlyDepreciation = 5_000m;
        var monthlyLoanPrincipal = Constant(1_500m);
        var capitalExpendituresInMonth = Month1Only(100_000m);
        var ownerInvestmentInMonth = Month1Only(30_000m);
        var loanProceedsInMonth = Month1Only(70_000m);

        var result = CashFlowCalculator.Compute(
            beginningCashMonth1,
            netIncome,
            monthlyDepreciation,
            monthlyLoanPrincipal,
            capitalExpendituresInMonth,
            ownerInvestmentInMonth,
            loanProceedsInMonth,
            ownerWithdrawals: 0m);

        // Reference: recompute the identity with W = 0 and confirm the
        // withdrawal term contributes nothing.
        var (_, expectedEnding) = ExpectedIdentity(
            beginningCashMonth1,
            netIncome,
            monthlyDepreciation,
            monthlyLoanPrincipal,
            capitalExpendituresInMonth,
            ownerInvestmentInMonth,
            loanProceedsInMonth,
            ownerWithdrawals: 0m);

        for (var i = 0; i < Months; i++)
        {
            Assert.Equal(expectedEnding[i], result.EndingCash[i]);
        }
    }

    [Fact]
    public void EndingCash_WithZeroWithdrawals_MatchesIdentity_WithoutWithdrawalTerm()
    {
        // A second angle on R27.5: compare two runs of the calculator itself,
        // one with W = 0 and one with W = 2000. Their difference at month m
        // must be exactly m · 2000 (from R13.6); at W = 0 the difference from
        // "no withdrawal at all" must be zero. This closes the loop: passing
        // W = 0 is indistinguishable from omitting the withdrawal term.
        var zero = ComputeBaseline(ownerWithdrawals: 0m);
        var nonZero = ComputeBaseline(ownerWithdrawals: 2_000m);

        for (var i = 0; i < Months; i++)
        {
            // W = 0 leg matches the identity with the withdrawal term dropped
            // (this is what R27.5 codifies).
            Assert.Equal(
                nonZero.EndingCash[i] + (i + 1) * 2_000m,
                zero.EndingCash[i]);
        }
    }
}
