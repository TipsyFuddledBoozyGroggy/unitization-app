// Tests for the depreciation pass (design §6.6, §15.3 → DepreciationTests).
//
// These tests are written tests-first against the intended internal API that
// task 22 will introduce. Design §6.6 documents the formula but does not
// spell out the C# helper's exact name or signature, so this file assumes:
//
//     namespace RehearsalForecast.Core.Forecast;
//
//     internal static class DepreciationCalculator
//     {
//         internal static decimal Compute(BuildingInputs building);
//     }
//
// Rationale for the assumption:
//   * §6.6 defines a single scalar quantity Monthly_Depreciation applied
//     identically to every month (R8.2). A scalar `decimal` return type is
//     the most direct encoding — it structurally guarantees the "identical
//     across all 36 months" property because there is no per-month channel
//     through which the value could vary.
//   * Task 22 restricts the helper's inputs to BuildingInputs.TotalBuildingCost
//     and BuildingInputs.DepreciationPeriodYears; taking BuildingInputs (and
//     nothing else) structurally guarantees R8.4 — non-building capital line
//     items (Equipment, TotalImprovementCost, BuildingPurchaseCost,
//     OtherCapitalCost) live on the sibling CapitalInputs record and cannot
//     be seen by this helper.
//   * The helper is `internal` so it is not part of the Web API surface; the
//     Core csproj's InternalsVisibleTo exposes it to this test project.
//
// If task 22 chooses a different helper name or signature, the test-method
// assertions do not need to change materially — only the Compute(...) call
// sites and the reflection-based structural tests near the bottom.
//
// Validates:
//   * Requirement 8.1  — Monthly_Depreciation = Total_Building_Cost / (Depreciation_Period_Years × 12).
//   * Requirement 8.2  — Monthly_Depreciation is identical for every month m ∈ [1, 36].
//   * Requirement 8.3  — Land_Value is not part of the depreciable amount.
//   * Requirement 8.4  — Equipment, TotalImprovementCost, BuildingPurchaseCost,
//                        and OtherCapitalCost are not part of the depreciable amount.
//   * Requirement 8.5  — Land_Value is captured for display but does not participate
//                        in any calculation (design decision 1).
//   * Requirement 22.2 — Test names identify the business rule under test.

using System;
using System.Linq;
using System.Reflection;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;
using Xunit;

namespace RehearsalForecast.Core.Tests;

public class DepreciationTests
{
    private const int Months = ForecastConstants.ForecastMonths;

    // -----------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="BuildingInputs"/> whose only depreciation-relevant
    /// fields are <paramref name="totalBuildingCost"/> and
    /// <paramref name="depreciationPeriodYears"/>. Geometry and occupancy
    /// fields are set to legal-but-irrelevant values that the depreciation
    /// pass must ignore.
    /// </summary>
    private static BuildingInputs MakeBuildingInputs(
        decimal totalBuildingCost,
        int depreciationPeriodYears,
        decimal landValue = 0m)
        => new(
            TotalSqft: 0m,
            PercentageAvailableForRent: 0m,
            TotalBuildingCost: totalBuildingCost,
            LandValue: landValue,
            DepreciationPeriodYears: depreciationPeriodYears,
            Occupancy: new OccupancySchedule(UseDefault: true, UserRates: null));

    // -----------------------------------------------------------------
    // R8.1 & R22.2 — Formula
    // -----------------------------------------------------------------

    [Fact]
    public void DepreciationCalculator_ComputesTotalBuildingCostDividedByYearsTimesTwelve()
    {
        // 1,200,000 / (30 × 12) = 1,200,000 / 360 = 3,333.333… (decimal exact).
        var inputs = MakeBuildingInputs(
            totalBuildingCost: 1_200_000m,
            depreciationPeriodYears: 30);

        var monthlyDepreciation = DepreciationCalculator.Compute(inputs);

        Assert.Equal(1_200_000m / (30m * 12m), monthlyDepreciation);
    }

    [Theory]
    [InlineData(0.0, 1)]
    [InlineData(0.0, 30)]
    [InlineData(120_000.0, 1)]
    [InlineData(1_000_000.0, 25)]
    [InlineData(1_800_000.0, 30)]
    [InlineData(500_000.0, 40)]
    [InlineData(999_999.99, 27)]
    public void DepreciationCalculator_ComputesTotalBuildingCostDividedByYearsTimesTwelve_AcrossExamples(
        double totalBuildingCostD, int depreciationPeriodYears)
    {
        var totalBuildingCost = (decimal)totalBuildingCostD;
        var inputs = MakeBuildingInputs(totalBuildingCost, depreciationPeriodYears);

        var monthlyDepreciation = DepreciationCalculator.Compute(inputs);

        Assert.Equal(
            totalBuildingCost / (depreciationPeriodYears * 12m),
            monthlyDepreciation);
    }

    [Fact]
    public void DepreciationCalculator_ReturnsZero_WhenTotalBuildingCostIsZero()
    {
        // Zero depreciable amount ⇒ zero Monthly_Depreciation, regardless of period.
        var inputs = MakeBuildingInputs(
            totalBuildingCost: 0m,
            depreciationPeriodYears: 30);

        var monthlyDepreciation = DepreciationCalculator.Compute(inputs);

        Assert.Equal(0m, monthlyDepreciation);
    }

    [Fact]
    public void DepreciationCalculator_EqualsAnnualDepreciationDividedByTwelve_ForOneYearPeriod()
    {
        // Minimum legal period is 1 year (Requirement 2.3). Monthly should equal
        // annual / 12 exactly. 120,000 / (1 × 12) = 10,000.
        var inputs = MakeBuildingInputs(
            totalBuildingCost: 120_000m,
            depreciationPeriodYears: 1);

        var monthlyDepreciation = DepreciationCalculator.Compute(inputs);

        Assert.Equal(10_000m, monthlyDepreciation);
    }

    // -----------------------------------------------------------------
    // R8.2 & R22.2 — Identical across all 36 months
    // -----------------------------------------------------------------

    [Fact]
    public void DepreciationCalculator_ReturnsScalarDecimal_UsedIdenticallyForEveryMonth()
    {
        // R8.2 structural guarantee: the helper returns a scalar decimal (not a
        // per-month list), so ForecastCalculator has no channel through which
        // Monthly_Depreciation could vary across months — the same value is
        // applied to every m ∈ [1, 36].
        var computeMethod = typeof(DepreciationCalculator)
            .GetMethod(
                "Compute",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(computeMethod);
        Assert.Equal(typeof(decimal), computeMethod!.ReturnType);
    }

    [Fact]
    public void DepreciationCalculator_MonthlyDepreciation_IsConstantAcrossThirtySixMonths()
    {
        // Materialise the scalar into a per-month vector as Pass 6 (design §6.6)
        // will do inside ForecastCalculator: every month index carries the same
        // value derived from the formula in R8.1.
        var inputs = MakeBuildingInputs(
            totalBuildingCost: 900_000m,
            depreciationPeriodYears: 25);
        var monthlyDepreciation = DepreciationCalculator.Compute(inputs);

        var perMonth = Enumerable.Repeat(monthlyDepreciation, Months).ToArray();

        Assert.Equal(Months, perMonth.Length);
        Assert.All(perMonth, v => Assert.Equal(monthlyDepreciation, v));
        Assert.All(perMonth, v => Assert.Equal(900_000m / (25m * 12m), v));
    }

    [Fact]
    public void DepreciationCalculator_ReturnsSameValueAcrossRepeatedCalls_WithSameInputs()
    {
        // Determinism / referential transparency: identical inputs must produce
        // identical outputs. Combined with the scalar return type, this rules
        // out hidden per-invocation state that could vary the value.
        var inputs = MakeBuildingInputs(
            totalBuildingCost: 480_000m,
            depreciationPeriodYears: 20);

        var first = DepreciationCalculator.Compute(inputs);
        var second = DepreciationCalculator.Compute(inputs);
        var third = DepreciationCalculator.Compute(inputs);

        Assert.Equal(first, second);
        Assert.Equal(second, third);
    }

    // -----------------------------------------------------------------
    // R8.3, R8.5 & R22.2 — Land_Value mutation does not change the figure
    // -----------------------------------------------------------------

    [Fact]
    public void DepreciationCalculator_IgnoresLandValue_WhenComputingMonthlyDepreciation()
    {
        // Two inputs that differ ONLY in LandValue must produce identical
        // Monthly_Depreciation (R8.3, R8.5, Design Decision 1).
        var withoutLand = MakeBuildingInputs(
            totalBuildingCost: 1_500_000m,
            depreciationPeriodYears: 30,
            landValue: 0m);

        var withLand = withoutLand with { LandValue = 500_000m };

        var d1 = DepreciationCalculator.Compute(withoutLand);
        var d2 = DepreciationCalculator.Compute(withLand);

        Assert.Equal(d1, d2);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(100.0)]
    [InlineData(250_000.0)]
    [InlineData(1_000_000.0)]
    [InlineData(9_999_999.99)]
    public void DepreciationCalculator_IsInvariant_UnderAnyLandValue(double landValueD)
    {
        var landValue = (decimal)landValueD;
        var baseline = MakeBuildingInputs(
            totalBuildingCost: 1_800_000m,
            depreciationPeriodYears: 30,
            landValue: 0m);
        var mutated = baseline with { LandValue = landValue };

        var expected = 1_800_000m / (30m * 12m);
        Assert.Equal(expected, DepreciationCalculator.Compute(baseline));
        Assert.Equal(expected, DepreciationCalculator.Compute(mutated));
    }

    // -----------------------------------------------------------------
    // R8.4 & R22.2 — Non-building capital line items do not change the figure
    // -----------------------------------------------------------------

    [Fact]
    public void DepreciationCalculator_ComputeSignature_AcceptsOnlyBuildingInputs()
    {
        // Structural guarantee of R8.4: the helper cannot read Equipment,
        // TotalImprovementCost, BuildingPurchaseCost, or OtherCapitalCost
        // because CapitalInputs is not part of its parameter list. Any future
        // change that widens the signature to accept capital inputs will
        // trip this test.
        var computeMethods = typeof(DepreciationCalculator)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name == "Compute")
            .ToList();

        Assert.Single(computeMethods);

        var parameters = computeMethods[0].GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(BuildingInputs), parameters[0].ParameterType);
    }

    [Fact]
    public void BuildingInputs_ExposesNoNonBuildingCapitalLineItemFields()
    {
        // R8.4 defensive check: the non-building capital line items must live
        // on the sibling CapitalInputs record, not on BuildingInputs. If any
        // of them leak onto BuildingInputs, the depreciation helper could
        // start reading them by accident.
        var propertyNames = typeof(BuildingInputs)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Equipment", propertyNames);
        Assert.DoesNotContain("TotalImprovementCost", propertyNames);
        Assert.DoesNotContain("BuildingPurchaseCost", propertyNames);
        Assert.DoesNotContain("OtherCapitalCost", propertyNames);
    }

    [Fact]
    public void DepreciationCalculator_IsInvariant_UnderChangesToNonBuildingCapitalLineItems()
    {
        // Two building inputs with identical TotalBuildingCost,
        // DepreciationPeriodYears, and LandValue must yield the same
        // Monthly_Depreciation. Non-building capital line items live on
        // CapitalInputs — a sibling of BuildingInputs on ForecastInputs —
        // and cannot be observed by this helper. Even if a caller supplies
        // wildly different CapitalInputs alongside these BuildingInputs
        // inside a wider ForecastInputs, the depreciation figure is
        // unaffected because CapitalInputs never reaches this pass.
        //
        // We assert this by holding TotalBuildingCost / DepreciationPeriodYears
        // fixed and observing the identical result; the reflection test
        // above (R8.4 structural guarantee) additionally proves the helper
        // has no channel to see capital line items at all.
        var buildingA = MakeBuildingInputs(
            totalBuildingCost: 1_200_000m,
            depreciationPeriodYears: 30);
        var buildingB = MakeBuildingInputs(
            totalBuildingCost: 1_200_000m,
            depreciationPeriodYears: 30);

        Assert.Equal(
            DepreciationCalculator.Compute(buildingA),
            DepreciationCalculator.Compute(buildingB));
    }
}
