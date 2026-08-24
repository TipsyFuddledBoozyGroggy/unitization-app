// Property 4: Building geometry.
// Validates: Requirements 3.1, 3.2, 3.3, 3.4, 27.1, 27.2.
//
// Design §10 (Property 4), §15.4. For any nonnegative Total_Sqft and any
// Percentage_Available_For_Rent in [0, 1]:
//
//   * Rentable_Sqft = Total_Sqft * Percentage_Available_For_Rent           (R3.1).
//   * Total_Rental_Units = Ceiling(Rentable_Sqft / 150)                    (R3.2, R3.4).
//   * Total_Rental_Units = 0 iff Rentable_Sqft = 0                         (R3.3).
//   * Total_Sqft = 0 => all-zero geometry                                  (R27.1).
//   * Percentage_Available_For_Rent = 0 => all-zero geometry               (R27.2).
//
// FsCheck.Xunit runs the [Property] at least 100 iterations (the default).
// The two derived scalars (Rentable_Sqft, Total_Rental_Units) are the only
// quantities Pass 1 emits, so this property fully covers the pass's output
// space.

using FsCheck.Xunit;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;
using Xunit;

namespace RehearsalForecast.Core.Tests.PropertyTests;

public class BuildingGeometryProperty
{
    /// <summary>
    /// Builds a <see cref="BuildingInputs"/> record whose non-geometry
    /// fields carry innocuous placeholder values. The property only
    /// consults <c>TotalSqft</c> and <c>PercentageAvailableForRent</c>; the
    /// remaining fields (TotalBuildingCost, LandValue, DepreciationPeriodYears,
    /// Occupancy) are not read by <see cref="BuildingGeometryCalculator.Compute"/>.
    /// </summary>
    private static BuildingInputs MakeBuildingInputs(decimal totalSqft, decimal percentageAvailable)
    {
        return new BuildingInputs(
            TotalSqft: totalSqft,
            PercentageAvailableForRent: percentageAvailable,
            TotalBuildingCost: 0m,
            LandValue: 0m,
            DepreciationPeriodYears: 30,
            Occupancy: new OccupancySchedule(UseDefault: true, UserRates: null));
    }

    /// <summary>
    /// Property 4: building geometry identities hold across all valid
    /// (Total_Sqft, Percentage_Available_For_Rent) pairs.
    /// Validates: Requirements 3.1, 3.2, 3.3, 3.4, 27.1, 27.2.
    /// </summary>
    [Property]
    public void BuildingGeometry_MatchesRequirementIdentities(
        uint totalSqftRaw,
        uint percentageRaw)
    {
        var totalSqft = PropertyTestHelpers.SqftFromRaw(totalSqftRaw);
        var percentageAvailable = PropertyTestHelpers.RateFromRaw(percentageRaw);

        var building = MakeBuildingInputs(totalSqft, percentageAvailable);
        var geometry = BuildingGeometryCalculator.Compute(building);

        // R3.1: Rentable_Sqft = Total_Sqft * Percentage_Available_For_Rent
        // (exact decimal multiplication).
        var expectedRentableSqft = totalSqft * percentageAvailable;
        Assert.Equal(expectedRentableSqft, geometry.RentableSqft);

        if (geometry.RentableSqft == 0m)
        {
            // R3.3: Total_Rental_Units = 0 when Rentable_Sqft = 0.
            Assert.Equal(0, geometry.TotalRentalUnits);
        }
        else
        {
            // R3.2: Total_Rental_Units = Ceiling(Rentable_Sqft / StandardUnitSize)
            // (R3.4 requires exactly the single named constant `150m`).
            var expectedUnits = (int)System.Math.Ceiling(
                geometry.RentableSqft / ForecastConstants.StandardUnitSize);
            Assert.Equal(expectedUnits, geometry.TotalRentalUnits);
        }

        // R3.3 (biconditional): Total_Rental_Units = 0 iff Rentable_Sqft = 0.
        Assert.Equal(geometry.RentableSqft == 0m, geometry.TotalRentalUnits == 0);

        // R27.1 / R27.2 edge cases: either Total_Sqft = 0 or Percentage = 0
        // yields all-zero geometry. Structural checks (independent of the
        // R3.1 identity above) that pin the "zero" behaviour explicitly.
        if (totalSqft == 0m)
        {
            Assert.Equal(0m, geometry.RentableSqft);
            Assert.Equal(0, geometry.TotalRentalUnits);
        }

        if (percentageAvailable == 0m)
        {
            Assert.Equal(0m, geometry.RentableSqft);
            Assert.Equal(0, geometry.TotalRentalUnits);
        }
    }
}
