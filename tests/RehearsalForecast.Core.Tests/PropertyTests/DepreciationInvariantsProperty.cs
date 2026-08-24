// Property-based test for Property 7 — Depreciation invariants
// (design §10, Property 7; §15.4).
//
// Property 7 states that for any valid ForecastInputs with
// Total_Building_Cost ≥ 0 and Depreciation_Period_Years ≥ 1:
//
//   1. Monthly_Depreciation = Total_Building_Cost / (Depreciation_Period_Years × 12)
//   2. Monthly_Depreciation is identical for every month m ∈ [1, 36]
//   3. Mutating Land_Value while holding every other input fixed produces a
//      byte-identical Monthly_Depreciation
//   4. Mutating any non-building capital line item (Equipment,
//      TotalImprovementCost, BuildingPurchaseCost, OtherCapitalCost) while
//      holding Total_Building_Cost fixed does NOT change Monthly_Depreciation
//
// The test drives both the internal DepreciationCalculator scalar helper
// (design §6.6) AND the full ForecastCalculator pipeline (design §6.12) so
// the invariants are verified at both API surfaces: the pass-6 unit boundary
// and the assembled 36-row result. This structurally rules out a regression
// that would compute the scalar correctly but then leak Land_Value or a
// capital line item into a MonthlyForecastRow's MonthlyDepreciation field.
//
// Bounding strategy: raw FsCheck-generated System.Int32 values are Math.Abs +
// modulo folded into finite, non-negative decimal amounts and into a valid
// Depreciation_Period_Years in [1, 40]. This avoids arithmetic overflow,
// pathological division, and negative amounts (which are rejected by
// InputValidator in production but not by DepreciationCalculator directly —
// see the reflection-based structural test in DepreciationTests for the
// property that non-building capital line items cannot even be seen by the
// pass). All arithmetic remains in decimal per Requirement 19.1.
//
// Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5

using FsCheck.Xunit;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;
using RehearsalForecast.Core.Loan;
using RehearsalForecast.Core.Schedules;
using Xunit;

namespace RehearsalForecast.Core.Tests.PropertyTests;

public class DepreciationInvariantsProperty
{
    private const int Months = ForecastConstants.ForecastMonths;

    // ------------------------------------------------------------------
    // Bounded generators
    //
    // FsCheck generates full-range Int32 by default; we fold into a small
    // finite, non-negative decimal range so the tests remain focused on
    // the arithmetic identity rather than on OverflowException edge cases.
    // ------------------------------------------------------------------

    /// <summary>
    /// Folds a raw Int32 into a nonnegative USD amount in [0, ~1,000,000)
    /// with cent precision. The mask via <c>Math.Abs((long)raw)</c> avoids
    /// the well-known "abs(int.MinValue)" overflow.
    /// </summary>
    private static decimal BoundMoney(int raw) =>
        (decimal)(Math.Abs((long)raw) % 100_000_000L) / 100m;

    /// <summary>
    /// Folds a raw Int32 into a valid <c>Depreciation_Period_Years</c> in
    /// <c>[1, 40]</c> (Requirement 2.3 — strictly positive).
    /// </summary>
    private static int BoundYears(int raw) =>
        (int)(Math.Abs((long)raw) % 40L) + 1;

    private static MonthlySchedule<decimal> Zero() =>
        MonthlySchedule<decimal>.Constant(0m);

    /// <summary>
    /// Assembles a full <see cref="ForecastInputs"/> whose only non-zero
    /// fields are the ones the depreciation-related tests care about.
    /// Marketing/operations/loan/tax/owner-activity all zero so the
    /// forecast pipeline runs cleanly and Monthly_Depreciation surfaces
    /// unmodified on every row.
    /// </summary>
    private static ForecastInputs MakeInputs(
        decimal totalBuildingCost,
        int depreciationYears,
        decimal landValue,
        decimal equipment,
        decimal totalImprovement,
        decimal buildingPurchase,
        decimal otherCapital)
    {
        return new ForecastInputs(
            Capital: new CapitalInputs(
                Equipment: equipment,
                TotalImprovementCost: totalImprovement,
                BuildingPurchaseCost: buildingPurchase,
                OtherCapitalCost: otherCapital),
            Marketing: new MarketingInputs(
                Print: Zero(),
                Search: Zero(),
                Social: Zero(),
                OtherMarketing: Zero()),
            Operations: new OperationsInputs(
                Accounting: Zero(),
                Custodial: Zero(),
                Gas: Zero(),
                Insurance: Zero(),
                It: Zero(),
                OfficeSupplies: Zero(),
                ProfessionalServices: Zero(),
                RentExpense: Zero(),
                Repairs: Zero(),
                Shipping: Zero(),
                PropertyTax: Zero(),
                Utilities: Zero(),
                Wages: Zero(),
                OtherOperations: Zero()),
            Building: new BuildingInputs(
                TotalSqft: 0m,
                PercentageAvailableForRent: 0m,
                TotalBuildingCost: totalBuildingCost,
                LandValue: landValue,
                DepreciationPeriodYears: depreciationYears,
                Occupancy: new OccupancySchedule(UseDefault: true, UserRates: null)),
            Loan: new LoanInputs(
                AnnualLoanInterestRate: 0m,
                LoanTermMonths: 36),
            Taxes: new TaxInputs(IncomeTaxRate: 0m),
            OwnerActivity: new OwnerActivityInputs(
                OwnerInvestment: 0m,
                OwnerWithdrawals: 0m),
            ForecastControls: new ForecastControlInputs(
                BeginningCashMonth1: 0m,
                TargetCashPositiveMonth: 1));
    }

    // ------------------------------------------------------------------
    // Property 7 — full universal statement
    //
    // Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5
    // ------------------------------------------------------------------

    [Property]
    public void Property_7_Depreciation_Invariants(
        int rawTotalBuildingCost,
        int rawDepreciationYears,
        int rawLandValue,
        int rawLandValueAlt,
        int rawEquipment,
        int rawImprovement,
        int rawBuildingPurchase,
        int rawOtherCapital,
        int rawEquipmentAlt,
        int rawImprovementAlt,
        int rawBuildingPurchaseAlt,
        int rawOtherCapitalAlt)
    {
        var totalBuildingCost = BoundMoney(rawTotalBuildingCost);
        var depYears = BoundYears(rawDepreciationYears);
        var landValue = BoundMoney(rawLandValue);
        var landValueAlt = BoundMoney(rawLandValueAlt);
        var equipment = BoundMoney(rawEquipment);
        var improvement = BoundMoney(rawImprovement);
        var buildingPurchase = BoundMoney(rawBuildingPurchase);
        var otherCapital = BoundMoney(rawOtherCapital);
        var equipmentAlt = BoundMoney(rawEquipmentAlt);
        var improvementAlt = BoundMoney(rawImprovementAlt);
        var buildingPurchaseAlt = BoundMoney(rawBuildingPurchaseAlt);
        var otherCapitalAlt = BoundMoney(rawOtherCapitalAlt);

        // ---- Pass-6 scalar helper (design §6.6) ---------------------

        var building = new BuildingInputs(
            TotalSqft: 0m,
            PercentageAvailableForRent: 0m,
            TotalBuildingCost: totalBuildingCost,
            LandValue: landValue,
            DepreciationPeriodYears: depYears,
            Occupancy: new OccupancySchedule(UseDefault: true, UserRates: null));

        var monthlyDepreciation = DepreciationCalculator.Compute(building);

        // R8.1: Monthly_Depreciation = Total_Building_Cost / (Depreciation_Period_Years × 12).
        var expected = totalBuildingCost == 0m
            ? 0m
            : totalBuildingCost / (depYears * 12m);
        Assert.Equal(expected, monthlyDepreciation);

        // R8.3 / R8.5 at the pass boundary: mutating LandValue produces a
        // byte-identical scalar result — the pass never reads LandValue.
        var buildingAltLand = building with { LandValue = landValueAlt };
        Assert.Equal(monthlyDepreciation, DepreciationCalculator.Compute(buildingAltLand));

        // ---- End-to-end via ForecastCalculator (design §6.12) --------

        var calc = new ForecastCalculator(new LoanCalculator());
        var inputs = MakeInputs(
            totalBuildingCost, depYears, landValue,
            equipment, improvement, buildingPurchase, otherCapital);
        var forecast = calc.Compute(inputs, 0m);

        // R8.2: Monthly_Depreciation is identical across all 36 rows.
        Assert.Equal(Months, forecast.Rows.Count);
        Assert.All(forecast.Rows,
            row => Assert.Equal(monthlyDepreciation, row.MonthlyDepreciation));

        // R8.3 / R8.5 end-to-end: mutating LandValue yields byte-identical
        // Monthly_Depreciation on every row. (The rest of the row may
        // change if other passes happen to read LandValue in a future
        // regression — Requirement 8.5 forbids exactly that. Here we
        // specifically pin the Monthly_Depreciation column.)
        var inputsAltLand = MakeInputs(
            totalBuildingCost, depYears, landValueAlt,
            equipment, improvement, buildingPurchase, otherCapital);
        var forecastAltLand = calc.Compute(inputsAltLand, 0m);
        Assert.All(forecastAltLand.Rows,
            row => Assert.Equal(monthlyDepreciation, row.MonthlyDepreciation));

        // R8.4: mutating any non-building capital line item leaves
        // Monthly_Depreciation unchanged on every row. We mutate all four
        // simultaneously to give the property maximum discriminating power
        // — any leak from CapitalInputs into the depreciation pass would
        // surface here as a mismatch.
        var inputsAltCapital = MakeInputs(
            totalBuildingCost, depYears, landValue,
            equipmentAlt, improvementAlt, buildingPurchaseAlt, otherCapitalAlt);
        var forecastAltCapital = calc.Compute(inputsAltCapital, 0m);
        Assert.All(forecastAltCapital.Rows,
            row => Assert.Equal(monthlyDepreciation, row.MonthlyDepreciation));
    }
}
