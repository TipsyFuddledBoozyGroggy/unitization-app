// Tests for capital sizing and Month-1 financing timing
// (design §6.7, §15.3 → CapitalAndFinancingTests).
//
// These tests are written tests-first against the intended internal API that
// task 24 (Pass 7) will introduce. Design §6.7 documents the formulas but
// does not spell out the C# helper's exact name or signature, so this file
// assumes the following surface (per task 23's guidance):
//
//     namespace RehearsalForecast.Core.Forecast;
//
//     internal static class CapitalCalculator
//     {
//         internal static CapitalResult Compute(
//             CapitalInputs capital,
//             decimal ownerInvestment);
//     }
//
//     internal sealed record CapitalResult(
//         decimal TotalCapital,
//         decimal LoanProceeds,
//         IReadOnlyList<decimal> CapitalExpendituresInMonth, // length 36, month 1 = TotalCapital, else 0
//         IReadOnlyList<decimal> OwnerInvestmentInMonth,     // length 36, month 1 = ownerInvestment, else 0
//         IReadOnlyList<decimal> LoanProceedsInMonth);       // length 36, month 1 = LoanProceeds, else 0
//
// Rationale for the assumption:
//   * §6.7 defines Total_Capital, Loan_Proceeds, and the three "In_Month"
//     vectors together as a single pass; grouping them in one call keeps
//     the sizing arithmetic and Month-1 timing coupled by construction.
//   * `Owner_Investment` is a scalar (Requirement 10.3) rather than a
//     schedule, so passing it as a bare `decimal` alongside `CapitalInputs`
//     matches the pass's inputs exactly and avoids introducing an
//     `OwnerActivityInputs` dependency in a helper that has no use for
//     `Owner_Withdrawals`.
//   * The per-month vectors are `IReadOnlyList<decimal>` of length 36 with
//     1-based semantics realised as `list[m - 1]`, matching the convention
//     used by `OperationsResult` (§6.5) and `OccupancyResult` (§6.2).
//   * The helper is `internal` so it is not part of the Web API surface;
//     `InternalsVisibleTo` on the Core csproj exposes it to this test project.
//
// If task 24 chooses a different helper name or shape, only the `Compute(...)`
// call sites and the `CapitalResult` field-name accessors need to change; the
// arithmetic assertions themselves remain the specification's arithmetic.
//
// Validates:
//   * Requirement 9.1   — Total_Capital = Equipment + TotalImprovementCost
//                         + BuildingPurchaseCost + OtherCapitalCost.
//   * Requirement 9.2   — Capital_Expenditures_In_Month[1] = Total_Capital.
//   * Requirement 9.3   — Capital_Expenditures_In_Month[m] = 0 for m in [2, 36].
//   * Requirement 10.1  — Loan_Proceeds = Max(Total_Capital − Owner_Investment, 0).
//   * Requirement 10.2  — Owner_Investment > Total_Capital ⇒ Loan_Proceeds = 0
//                         AND Capital_Expenditures_In_Month[1] = Total_Capital.
//   * Requirement 10.3  — Owner_Investment_In_Month[1] = Owner_Investment,
//                         zero elsewhere.
//   * Requirement 10.4  — Loan_Proceeds_In_Month[1] = Loan_Proceeds,
//                         zero elsewhere.
//   * Requirement 22.2  — Test names identify the business rule under test.
//   * Requirement 27.3  — Edge case: Total_Capital = 0 AND Owner_Investment = 0
//                         ⇒ Loan_Proceeds = 0 (no negative or spurious loan).

using System.Collections.Generic;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;
using Xunit;

namespace RehearsalForecast.Core.Tests;

public class CapitalAndFinancingTests
{
    private const int Months = ForecastConstants.ForecastMonths;

    // ---------------------------------------------------------------
    // R9.1: Total_Capital = Equipment + TotalImprovementCost
    //                       + BuildingPurchaseCost + OtherCapitalCost
    // ---------------------------------------------------------------

    [Fact]
    public void CapitalCalculator_TotalCapital_SumsAllFourCapitalLineItems()
    {
        // Distinct prime-ish values so a dropped or duplicated addend is visible.
        var capital = new CapitalInputs(
            Equipment: 1_000m,
            TotalImprovementCost: 2_500m,
            BuildingPurchaseCost: 750_000m,
            OtherCapitalCost: 137m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 0m);

        Assert.Equal(1_000m + 2_500m + 750_000m + 137m, result.TotalCapital);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 0, 0, 0)]
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(12.34, 56.78, 90.12, 34.56)]
    [InlineData(500_000, 250_000, 1_250_000, 75_000)]
    public void CapitalCalculator_TotalCapital_EqualsExactSumOfFourInputs(
        double equipment,
        double improvement,
        double purchase,
        double other)
    {
        var capital = new CapitalInputs(
            Equipment: (decimal)equipment,
            TotalImprovementCost: (decimal)improvement,
            BuildingPurchaseCost: (decimal)purchase,
            OtherCapitalCost: (decimal)other);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 0m);

        Assert.Equal(
            (decimal)equipment + (decimal)improvement + (decimal)purchase + (decimal)other,
            result.TotalCapital);
    }

    [Fact]
    public void CapitalCalculator_TotalCapital_PreservesFractionalCents()
    {
        // decimal must not truncate cent-level fractions.
        var capital = new CapitalInputs(
            Equipment: 0.01m,
            TotalImprovementCost: 0.02m,
            BuildingPurchaseCost: 0.03m,
            OtherCapitalCost: 0.04m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 0m);

        Assert.Equal(0.10m, result.TotalCapital);
    }

    // ---------------------------------------------------------------
    // R10.1: Loan_Proceeds = Max(Total_Capital − Owner_Investment, 0)
    // ---------------------------------------------------------------

    [Fact]
    public void CapitalCalculator_LoanProceeds_EqualsTotalCapitalMinusOwnerInvestment_WhenOwnerUnderInvests()
    {
        // Total_Capital = 100,000; Owner_Investment = 40,000 ⇒ Loan_Proceeds = 60,000.
        var capital = MakeCapital(totalCapital: 100_000m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 40_000m);

        Assert.Equal(100_000m, result.TotalCapital);
        Assert.Equal(60_000m, result.LoanProceeds);
    }

    [Fact]
    public void CapitalCalculator_LoanProceeds_EqualsTotalCapital_WhenOwnerInvestmentIsZero()
    {
        // No owner contribution ⇒ full amount financed.
        var capital = MakeCapital(totalCapital: 250_000m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 0m);

        Assert.Equal(250_000m, result.LoanProceeds);
    }

    [Fact]
    public void CapitalCalculator_LoanProceeds_IsZero_WhenOwnerInvestmentEqualsTotalCapital()
    {
        // Boundary: owner exactly funds capital ⇒ no loan needed.
        var capital = MakeCapital(totalCapital: 500_000m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 500_000m);

        Assert.Equal(0m, result.LoanProceeds);
    }

    // ---------------------------------------------------------------
    // R10.2 & R27.3: Owner-over-investment ⇒ Loan_Proceeds = 0 yet
    //                Capital_Expenditures_In_Month[1] still equals Total_Capital.
    // ---------------------------------------------------------------

    [Fact]
    public void CapitalCalculator_LoanProceeds_ClampsToZero_WhenOwnerInvestmentExceedsTotalCapital()
    {
        // Owner over-invests: Total_Capital = 100,000; Owner_Investment = 175,000.
        var capital = MakeCapital(totalCapital: 100_000m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 175_000m);

        // Loan_Proceeds is clamped to 0 — never negative (R10.1, R10.2).
        Assert.Equal(0m, result.LoanProceeds);
    }

    [Fact]
    public void CapitalCalculator_CapitalExpendituresInMonth1_EqualsTotalCapital_EvenWhenOwnerOverInvests()
    {
        // R10.2: owner over-investment does NOT net against capex — the entire
        // Total_Capital remains as the Month-1 capital expenditure. (The excess
        // owner cash reaches Ending_Cash via Owner_Investment_In_Month[1] and
        // the cash-flow roll-forward — not by shrinking capex.)
        var capital = MakeCapital(totalCapital: 100_000m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 175_000m);

        Assert.Equal(100_000m, result.TotalCapital);
        Assert.Equal(100_000m, result.CapitalExpendituresInMonth[0]);
        Assert.Equal(0m, result.LoanProceeds);
    }

    [Fact]
    public void CapitalCalculator_OwnerInvestmentInMonth1_EqualsOwnerInvestment_EvenWhenItExceedsTotalCapital()
    {
        // R10.3: Owner_Investment_In_Month[1] carries the raw owner amount,
        // regardless of whether it exceeds Total_Capital. The excess is what
        // makes owner-over-investment observable downstream.
        var capital = MakeCapital(totalCapital: 100_000m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 175_000m);

        Assert.Equal(175_000m, result.OwnerInvestmentInMonth[0]);
    }

    // ---------------------------------------------------------------
    // R27.3: Total_Capital = 0 AND Owner_Investment = 0 ⇒ Loan_Proceeds = 0.
    // ---------------------------------------------------------------

    [Fact]
    public void CapitalCalculator_LoanProceeds_IsZero_WhenTotalCapitalAndOwnerInvestmentAreBothZero()
    {
        // Degenerate business case: nothing to finance, nothing invested.
        // The clamp must yield 0, never a negative or spurious value.
        var capital = new CapitalInputs(
            Equipment: 0m,
            TotalImprovementCost: 0m,
            BuildingPurchaseCost: 0m,
            OtherCapitalCost: 0m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 0m);

        Assert.Equal(0m, result.TotalCapital);
        Assert.Equal(0m, result.LoanProceeds);
    }

    [Fact]
    public void CapitalCalculator_AllMonthlyVectorsAreZero_WhenTotalCapitalAndOwnerInvestmentAreBothZero()
    {
        // The Month-1 timing convention must still hold in the degenerate case:
        // every entry of every vector is 0, not just Month 1.
        var capital = new CapitalInputs(
            Equipment: 0m,
            TotalImprovementCost: 0m,
            BuildingPurchaseCost: 0m,
            OtherCapitalCost: 0m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 0m);

        Assert.Equal(Months, result.CapitalExpendituresInMonth.Count);
        Assert.Equal(Months, result.OwnerInvestmentInMonth.Count);
        Assert.Equal(Months, result.LoanProceedsInMonth.Count);
        Assert.All(result.CapitalExpendituresInMonth, v => Assert.Equal(0m, v));
        Assert.All(result.OwnerInvestmentInMonth, v => Assert.Equal(0m, v));
        Assert.All(result.LoanProceedsInMonth, v => Assert.Equal(0m, v));
    }

    // ---------------------------------------------------------------
    // R9.2 & R9.3: Month-1 timing for Capital_Expenditures_In_Month.
    // ---------------------------------------------------------------

    [Fact]
    public void CapitalCalculator_CapitalExpendituresInMonth1_EqualsTotalCapital()
    {
        var capital = MakeCapital(totalCapital: 425_000m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 100_000m);

        Assert.Equal(425_000m, result.CapitalExpendituresInMonth[0]);
    }

    [Fact]
    public void CapitalCalculator_CapitalExpendituresInMonth_IsZero_ForMonthsTwoThroughThirtySix()
    {
        var capital = MakeCapital(totalCapital: 425_000m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 100_000m);

        Assert.Equal(Months, result.CapitalExpendituresInMonth.Count);
        for (var i = 1; i < Months; i++)
        {
            Assert.Equal(0m, result.CapitalExpendituresInMonth[i]);
        }
    }

    // ---------------------------------------------------------------
    // R10.3: Month-1 timing for Owner_Investment_In_Month.
    // ---------------------------------------------------------------

    [Fact]
    public void CapitalCalculator_OwnerInvestmentInMonth1_EqualsOwnerInvestment()
    {
        var capital = MakeCapital(totalCapital: 300_000m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 90_000m);

        Assert.Equal(90_000m, result.OwnerInvestmentInMonth[0]);
    }

    [Fact]
    public void CapitalCalculator_OwnerInvestmentInMonth_IsZero_ForMonthsTwoThroughThirtySix()
    {
        var capital = MakeCapital(totalCapital: 300_000m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 90_000m);

        Assert.Equal(Months, result.OwnerInvestmentInMonth.Count);
        for (var i = 1; i < Months; i++)
        {
            Assert.Equal(0m, result.OwnerInvestmentInMonth[i]);
        }
    }

    // ---------------------------------------------------------------
    // R10.4: Month-1 timing for Loan_Proceeds_In_Month.
    // ---------------------------------------------------------------

    [Fact]
    public void CapitalCalculator_LoanProceedsInMonth1_EqualsLoanProceeds()
    {
        // Total_Capital = 300,000; Owner_Investment = 90,000 ⇒ Loan_Proceeds = 210,000.
        var capital = MakeCapital(totalCapital: 300_000m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 90_000m);

        Assert.Equal(210_000m, result.LoanProceeds);
        Assert.Equal(210_000m, result.LoanProceedsInMonth[0]);
    }

    [Fact]
    public void CapitalCalculator_LoanProceedsInMonth_IsZero_ForMonthsTwoThroughThirtySix()
    {
        var capital = MakeCapital(totalCapital: 300_000m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 90_000m);

        Assert.Equal(Months, result.LoanProceedsInMonth.Count);
        for (var i = 1; i < Months; i++)
        {
            Assert.Equal(0m, result.LoanProceedsInMonth[i]);
        }
    }

    [Fact]
    public void CapitalCalculator_LoanProceedsInMonth1_IsZero_WhenOwnerOverInvests()
    {
        // R10.2 timing corollary: if Loan_Proceeds is 0, the Month-1 slot must
        // also be 0. (No spurious cash inflow just because Month 1 is "special".)
        var capital = MakeCapital(totalCapital: 100_000m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 500_000m);

        Assert.Equal(0m, result.LoanProceedsInMonth[0]);
        for (var i = 1; i < Months; i++)
        {
            Assert.Equal(0m, result.LoanProceedsInMonth[i]);
        }
    }

    // ---------------------------------------------------------------
    // Structural check: every monthly vector has exactly 36 entries.
    // (Enforces the ForecastMonths contract across the whole pass.)
    // ---------------------------------------------------------------

    [Fact]
    public void CapitalCalculator_AllMonthlyVectors_HaveExactlyThirtySixEntries()
    {
        var capital = MakeCapital(totalCapital: 1_000m);

        var result = CapitalCalculator.Compute(capital, ownerInvestment: 500m);

        Assert.Equal(36, result.CapitalExpendituresInMonth.Count);
        Assert.Equal(36, result.OwnerInvestmentInMonth.Count);
        Assert.Equal(36, result.LoanProceedsInMonth.Count);
    }

    // ---------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="CapitalInputs"/> whose four line items sum exactly to
    /// <paramref name="totalCapital"/>. The distribution across line items is
    /// irrelevant to the capital-and-financing pass; only the sum matters
    /// (Requirement 9.1). We spread the amount unevenly so any accidental
    /// dependence on a single field would show up in a follow-up test.
    /// </summary>
    private static CapitalInputs MakeCapital(decimal totalCapital)
    {
        // Split so the four addends are distinct and sum to totalCapital.
        // For totalCapital = 0 all four are 0.
        var quarter = totalCapital / 4m;
        var equipment = quarter;
        var improvement = quarter;
        var purchase = quarter;
        var other = totalCapital - equipment - improvement - purchase;
        return new CapitalInputs(
            Equipment: equipment,
            TotalImprovementCost: improvement,
            BuildingPurchaseCost: purchase,
            OtherCapitalCost: other);
    }
}
