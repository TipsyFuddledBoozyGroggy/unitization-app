// BuildingGeometryTests — Pass 1 (Requirement 3, Design §6.1)
//
// These tests are written tests-first against the intended internal API that
// task 12 will introduce. Because design §6.1 documents the formulas but does
// not spell out the C# helper's exact name or signature, this file assumes:
//
//     namespace RehearsalForecast.Core.Forecast;
//
//     internal static class BuildingGeometryCalculator
//     {
//         internal static BuildingGeometry Compute(BuildingInputs building);
//     }
//
//     internal sealed record BuildingGeometry(
//         decimal RentableSqft,
//         int TotalRentalUnits);
//
// Rationale for the assumption:
//   * §6.1 requires `Rentable_Sqft` (a decimal number of square feet, from
//     Total_Sqft × Percentage_Available_For_Rent) and `Total_Rental_Units`
//     (a nonnegative integer count, from ceil(Rentable_Sqft / StandardUnitSize)).
//   * Requirement 3.3 forces `Total_Rental_Units = 0` when `Rentable_Sqft = 0`,
//     so an int is the appropriate carrier type for the unit count.
//   * The helper must reference `ForecastConstants.StandardUnitSize` (task 12
//     acceptance) and therefore lives in `RehearsalForecast.Core.Forecast`.
//   * The helper is `internal` so it is not part of the Web API surface;
//     `InternalsVisibleTo` on the Core csproj exposes it to this test project.
//
// If task 12 chooses a different helper name, the test-method assertions do
// not need to change — only the four `Compute(...)` call sites at the bottom
// of each test.

using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;
using Xunit;

namespace RehearsalForecast.Core.Tests;

public class BuildingGeometryTests
{
    // ---------------------------------------------------------------------
    // Requirement 3.1: Rentable_Sqft = Total_Sqft × Percentage_Available_For_Rent
    // ---------------------------------------------------------------------

    [Fact]
    public void Rentable_Sqft_Equals_TotalSqft_Times_PercentageAvailableForRent()
    {
        // 10,000 sqft × 80% = 8,000 sqft rentable.
        var inputs = MakeBuildingInputs(totalSqft: 10_000m, percentage: 0.80m);

        var geometry = BuildingGeometryCalculator.Compute(inputs);

        Assert.Equal(8_000m, geometry.RentableSqft);
    }

    [Fact]
    public void Rentable_Sqft_Preserves_Fractional_Product_Of_TotalSqft_And_Percentage()
    {
        // 12,345.67 × 0.375 = 4,629.62625 — decimal multiplication must not truncate.
        var inputs = MakeBuildingInputs(totalSqft: 12_345.67m, percentage: 0.375m);

        var geometry = BuildingGeometryCalculator.Compute(inputs);

        Assert.Equal(12_345.67m * 0.375m, geometry.RentableSqft);
    }

    [Fact]
    public void Rentable_Sqft_Equals_TotalSqft_When_Percentage_Is_One_Hundred_Percent()
    {
        // 100% availability ⇒ Rentable_Sqft == Total_Sqft.
        var inputs = MakeBuildingInputs(totalSqft: 15_000m, percentage: 1.00m);

        var geometry = BuildingGeometryCalculator.Compute(inputs);

        Assert.Equal(15_000m, geometry.RentableSqft);
    }

    // ---------------------------------------------------------------------
    // Requirement 3.2: Total_Rental_Units = Ceiling(Rentable_Sqft / StandardUnitSize)
    // Requirement 3.4: StandardUnitSize is the single named constant equal to 150.
    // ---------------------------------------------------------------------

    [Fact]
    public void Total_Rental_Units_Equals_Ceiling_Of_RentableSqft_Divided_By_StandardUnitSize()
    {
        // Exact-multiple case: 15,000 sqft / 150 sqft per unit = 100 units, no rounding.
        var inputs = MakeBuildingInputs(
            totalSqft: 15_000m,
            percentage: 1.00m);

        var geometry = BuildingGeometryCalculator.Compute(inputs);

        Assert.Equal(15_000m, geometry.RentableSqft);
        Assert.Equal(100, geometry.TotalRentalUnits);
    }

    [Fact]
    public void Total_Rental_Units_Rounds_Up_When_RentableSqft_Is_Not_A_Multiple_Of_StandardUnitSize()
    {
        // 8,000 sqft / 150 sqft/unit = 53.333… → ceil = 54 units.
        var inputs = MakeBuildingInputs(totalSqft: 10_000m, percentage: 0.80m);

        var geometry = BuildingGeometryCalculator.Compute(inputs);

        // Sanity-check the setup: 10,000 × 0.80 = 8,000 rentable sqft.
        Assert.Equal(8_000m, geometry.RentableSqft);
        Assert.Equal(54, geometry.TotalRentalUnits);
    }

    [Fact]
    public void Total_Rental_Units_Rounds_Up_For_Any_Fractional_Unit_Even_A_Single_Extra_Sqft()
    {
        // Rentable_Sqft = StandardUnitSize + 1 (= 151 by the fixed constant) ⇒ 2 units.
        var oneUnitPlusOneSqft = ForecastConstants.StandardUnitSize + 1m;
        var inputs = MakeBuildingInputs(totalSqft: oneUnitPlusOneSqft, percentage: 1.00m);

        var geometry = BuildingGeometryCalculator.Compute(inputs);

        Assert.Equal(oneUnitPlusOneSqft, geometry.RentableSqft);
        Assert.Equal(2, geometry.TotalRentalUnits);
    }

    [Fact]
    public void Total_Rental_Units_Is_One_When_RentableSqft_Equals_StandardUnitSize()
    {
        // Boundary: exactly one whole unit's worth of rentable area.
        var inputs = MakeBuildingInputs(
            totalSqft: ForecastConstants.StandardUnitSize,
            percentage: 1.00m);

        var geometry = BuildingGeometryCalculator.Compute(inputs);

        Assert.Equal(ForecastConstants.StandardUnitSize, geometry.RentableSqft);
        Assert.Equal(1, geometry.TotalRentalUnits);
    }

    [Fact]
    public void Total_Rental_Units_Uses_StandardUnitSize_Constant_As_The_Divisor()
    {
        // Requirement 3.4: the divisor is the named StandardUnitSize constant,
        // not a magic 150 literal in calculation code. We prove this indirectly
        // by using the constant to construct an input whose expected unit count
        // is well defined regardless of the constant's numeric value.
        var threeUnitsExact = 3m * ForecastConstants.StandardUnitSize;
        var inputs = MakeBuildingInputs(totalSqft: threeUnitsExact, percentage: 1.00m);

        var geometry = BuildingGeometryCalculator.Compute(inputs);

        Assert.Equal(3, geometry.TotalRentalUnits);
    }

    // ---------------------------------------------------------------------
    // Requirement 3.3: Total_Rental_Units = 0 when Rentable_Sqft = 0.
    // ---------------------------------------------------------------------

    [Fact]
    public void Total_Rental_Units_Is_Zero_When_RentableSqft_Is_Zero()
    {
        // Any way of reaching Rentable_Sqft = 0 (here: 0 sqft × 100%) must yield 0 units.
        var inputs = MakeBuildingInputs(totalSqft: 0m, percentage: 1.00m);

        var geometry = BuildingGeometryCalculator.Compute(inputs);

        Assert.Equal(0m, geometry.RentableSqft);
        Assert.Equal(0, geometry.TotalRentalUnits);
    }

    // ---------------------------------------------------------------------
    // Requirement 27.1: Total_Sqft = 0 ⇒ all-zero geometry.
    // ---------------------------------------------------------------------

    [Fact]
    public void Geometry_Is_All_Zero_When_TotalSqft_Is_Zero()
    {
        // Even with 100% availability, 0 sqft of building yields nothing rentable.
        var inputs = MakeBuildingInputs(totalSqft: 0m, percentage: 1.00m);

        var geometry = BuildingGeometryCalculator.Compute(inputs);

        Assert.Equal(0m, geometry.RentableSqft);
        Assert.Equal(0, geometry.TotalRentalUnits);
    }

    // ---------------------------------------------------------------------
    // Requirement 27.2: Percentage_Available_For_Rent = 0 ⇒ all-zero geometry.
    // ---------------------------------------------------------------------

    [Fact]
    public void Geometry_Is_All_Zero_When_PercentageAvailableForRent_Is_Zero()
    {
        // Even with a large building, 0% availability yields no rentable area or units.
        var inputs = MakeBuildingInputs(totalSqft: 50_000m, percentage: 0m);

        var geometry = BuildingGeometryCalculator.Compute(inputs);

        Assert.Equal(0m, geometry.RentableSqft);
        Assert.Equal(0, geometry.TotalRentalUnits);
    }

    // ---------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="BuildingInputs"/> whose only geometry-relevant fields
    /// are <paramref name="totalSqft"/> and <paramref name="percentage"/>. All
    /// other fields are set to values that are legal by contract but that the
    /// building-geometry pass must ignore.
    /// </summary>
    private static BuildingInputs MakeBuildingInputs(decimal totalSqft, decimal percentage) =>
        new(
            TotalSqft: totalSqft,
            PercentageAvailableForRent: percentage,
            TotalBuildingCost: 0m,
            LandValue: 0m,
            DepreciationPeriodYears: 30,
            Occupancy: new OccupancySchedule(UseDefault: true, UserRates: null));
}
