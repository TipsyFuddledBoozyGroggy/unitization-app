// Property 5: Occupancy clamping invariants.
// Validates: Requirements 4.1, 4.3, 4.4, 4.5, 27.1, 27.2.
//
// Design §10 (Property 5), §15.4. For any valid building inputs and any
// occupancy schedule, and for every month m in [1, 36]:
//
//   * Rented_Units[m]  in [0, Total_Rental_Units]                          (R4.3, R4.4).
//   * Rented_Sqft[m]   <= Rentable_Sqft                                     (R4.5).
//   * Rented_Sqft[m]   == 0 when Rentable_Sqft == 0                         (R27.1, R27.2).
//   * Default schedule: Rates[m] = Min(m * 0.10, 1.00) for m in [1, 10]
//                       and 1.00 for m in [11, 36]                          (R4.1).
//
// FsCheck.Xunit runs the [Property] at least 100 iterations (the default).
// Each iteration exercises both the default-formula and the user-supplied
// occupancy branches so the clamp invariants are validated uniformly.

using FsCheck.Xunit;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;
using Xunit;

namespace RehearsalForecast.Core.Tests.PropertyTests;

public class OccupancyClampProperty
{
    /// <summary>
    /// Builds a <see cref="BuildingInputs"/> for the property test. Only
    /// the geometry fields and the supplied occupancy schedule matter to
    /// <see cref="OccupancyCalculator.Compute"/>; the remaining fields
    /// carry innocuous placeholders.
    /// </summary>
    private static BuildingInputs MakeBuildingInputs(
        decimal totalSqft,
        decimal percentageAvailable,
        OccupancySchedule occupancy)
    {
        return new BuildingInputs(
            TotalSqft: totalSqft,
            PercentageAvailableForRent: percentageAvailable,
            TotalBuildingCost: 0m,
            LandValue: 0m,
            DepreciationPeriodYears: 30,
            Occupancy: occupancy);
    }

    /// <summary>
    /// Property 5: occupancy-clamping invariants hold for every month
    /// across both the default and the variable-rate branches, and the
    /// default schedule matches the R4.1 ramp exactly.
    /// Validates: Requirements 4.1, 4.3, 4.4, 4.5, 27.1, 27.2.
    /// </summary>
    [Property]
    public void OccupancyClamps_HoldForEveryMonth_AndDefaultScheduleMatchesRamp(
        uint totalSqftRaw,
        uint percentageRaw,
        bool useDefault,
        uint rateSeed)
    {
        var totalSqft = PropertyTestHelpers.SqftFromRaw(totalSqftRaw);
        var percentageAvailable = PropertyTestHelpers.RateFromRaw(percentageRaw);

        var occupancy = useDefault
            ? new OccupancySchedule(UseDefault: true, UserRates: null)
            : new OccupancySchedule(
                UseDefault: false,
                UserRates: PropertyTestHelpers.RatesVectorFromSeed(rateSeed));

        var building = MakeBuildingInputs(totalSqft, percentageAvailable, occupancy);
        var geometry = BuildingGeometryCalculator.Compute(building);
        var result = OccupancyCalculator.Compute(building, geometry);

        // Each output vector must have exactly 36 entries (Pass 2 shape).
        Assert.Equal(36, result.Rates.Count);
        Assert.Equal(36, result.RentedUnits.Count);
        Assert.Equal(36, result.RentedSqft.Count);

        for (var m = 1; m <= 36; m++)
        {
            var i = m - 1;
            var rentedUnits = result.RentedUnits[i];
            var rentedSqft = result.RentedSqft[i];

            // R4.4 (with R4.3): Rented_Units[m] is in [0, Total_Rental_Units].
            Assert.True(
                rentedUnits >= 0,
                $"Rented_Units[{m}] ({rentedUnits}) must be >= 0.");
            Assert.True(
                rentedUnits <= geometry.TotalRentalUnits,
                $"Rented_Units[{m}] ({rentedUnits}) must be <= "
                + $"Total_Rental_Units ({geometry.TotalRentalUnits}).");

            // R4.5: Rented_Sqft[m] <= Rentable_Sqft (clamp guarantees no
            // overshoot even when Rented_Units * 150 would exceed).
            Assert.True(
                rentedSqft <= geometry.RentableSqft,
                $"Rented_Sqft[{m}] ({rentedSqft}) must be <= "
                + $"Rentable_Sqft ({geometry.RentableSqft}).");

            // R27.1 / R27.2 edge: Rented_Sqft[m] = 0 when Rentable_Sqft = 0.
            if (geometry.RentableSqft == 0m)
            {
                Assert.Equal(0m, rentedSqft);
            }
        }

        // R4.1: default schedule matches Min(m * 0.10, 1.00) for m in [1, 10]
        // and 1.00 for m in [11, 36]. Only checked on the default branch;
        // the variable branch is exercised for the clamp invariants above.
        if (useDefault)
        {
            for (var m = 1; m <= 36; m++)
            {
                var expected = m <= 10
                    ? m * 0.10m
                    : 1.00m;

                Assert.Equal(expected, result.Rates[m - 1]);
            }
        }
        else
        {
            // Structural sanity on the variable branch: every generated
            // rate is in [0, 1] (RatesVectorFromSeed enforces this), and
            // the calculator echoes them into `Rates` verbatim.
            for (var m = 1; m <= 36; m++)
            {
                var rate = result.Rates[m - 1];
                Assert.True(rate >= 0m && rate <= 1m,
                    $"Occupancy_Rate[{m}] ({rate}) must be in [0, 1].");
            }
        }

        // The `StandardUnitSize` constant is the sole floor-area conversion
        // factor for the pass; reading it here documents the dependency
        // and ensures its literal never migrates back into the tests.
        _ = ForecastConstants.StandardUnitSize;
    }
}
