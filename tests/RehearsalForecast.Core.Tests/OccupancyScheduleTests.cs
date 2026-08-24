// OccupancyScheduleTests — unit tests for Pass 2 of the forecast calculator
// (design §6.2 "Occupancy schedule"). Covers Requirements 4.1–4.5.
//
// TDD-first: this file is written before the implementation in task 14. It
// references an internal helper API that does not yet exist, so this file
// will NOT compile until task 14 lands. That is intentional (see tasks.md
// task 13 status "in progress" and task 14 status "queued").
//
// Assumed internal API surface (design §6.2 does not name a concrete type;
// this shape is the working assumption documented in the task prompt and
// will be implemented by task 14):
//
//   namespace RehearsalForecast.Core.Forecast;
//
//   internal sealed record BuildingGeometry(
//       decimal RentableSqft,
//       int TotalRentalUnits);
//
//   internal sealed record OccupancyResult(
//       IReadOnlyList<decimal> Rates,        // length 36, index i = month i+1
//       IReadOnlyList<int> RentedUnits,      // length 36
//       IReadOnlyList<decimal> RentedSqft);  // length 36
//
//   internal static class OccupancyCalculator
//   {
//       internal static OccupancyResult Compute(
//           BuildingInputs inputs,
//           BuildingGeometry geometry);
//   }
//
// If task 14 chooses a different shape, this file will need to be updated
// to match. InternalsVisibleTo is added by task 11's subagent, not here.

using System.Collections.Generic;
using System.Linq;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;
using Xunit;

namespace RehearsalForecast.Core.Tests;

public class OccupancyScheduleTests
{
    // ---------------------------------------------------------------------
    // Default schedule (Requirement 4.1)
    // ---------------------------------------------------------------------

    [Fact]
    public void DefaultSchedule_YieldsTenPercentRamp_ThenSaturatesAtOneHundredPercent()
    {
        var inputs = MakeInputs(occupancy: new OccupancySchedule(UseDefault: true, UserRates: null));
        var geometry = new BuildingGeometry(RentableSqft: 1500m, TotalRentalUnits: 10);

        var result = OccupancyCalculator.Compute(inputs, geometry);

        var expected = new decimal[ForecastConstants.ForecastMonths];
        for (var i = 0; i < expected.Length; i++)
        {
            var month = i + 1;
            expected[i] = System.Math.Min(month * 0.10m, 1.00m);
        }

        Assert.Equal(expected, result.Rates);
    }

    [Fact]
    public void DefaultSchedule_MatchesExactValuesFromRequirement4Point1()
    {
        var inputs = MakeInputs(occupancy: new OccupancySchedule(UseDefault: true, UserRates: null));
        var geometry = new BuildingGeometry(RentableSqft: 1500m, TotalRentalUnits: 10);

        var result = OccupancyCalculator.Compute(inputs, geometry);

        // Months 1..10: 0.10, 0.20, ..., 1.00
        Assert.Equal(0.10m, result.Rates[0]);
        Assert.Equal(0.20m, result.Rates[1]);
        Assert.Equal(0.30m, result.Rates[2]);
        Assert.Equal(0.40m, result.Rates[3]);
        Assert.Equal(0.50m, result.Rates[4]);
        Assert.Equal(0.60m, result.Rates[5]);
        Assert.Equal(0.70m, result.Rates[6]);
        Assert.Equal(0.80m, result.Rates[7]);
        Assert.Equal(0.90m, result.Rates[8]);
        Assert.Equal(1.00m, result.Rates[9]);

        // Months 11..36: 1.00 for every month.
        for (var i = 10; i < ForecastConstants.ForecastMonths; i++)
        {
            Assert.Equal(1.00m, result.Rates[i]);
        }
    }

    // ---------------------------------------------------------------------
    // Variable-mode schedule (Requirement 4.2)
    // ---------------------------------------------------------------------

    [Fact]
    public void VariableMode_UsesExactlyThe36UserSuppliedRates()
    {
        // Distinct value per month so a mis-ordering or off-by-one is caught.
        var userRates = Enumerable.Range(1, ForecastConstants.ForecastMonths)
            .Select(m => m / 100m) // 0.01, 0.02, ..., 0.36
            .ToList();

        var inputs = MakeInputs(occupancy: new OccupancySchedule(UseDefault: false, UserRates: userRates));
        var geometry = new BuildingGeometry(RentableSqft: 1500m, TotalRentalUnits: 10);

        var result = OccupancyCalculator.Compute(inputs, geometry);

        Assert.Equal(userRates, result.Rates);
    }

    [Fact]
    public void VariableMode_IgnoresDefaultRampFormula_EvenAtEarlyMonths()
    {
        // Every user rate is 0.05 — well below the default month-1 rate of 0.10.
        var userRates = Enumerable.Repeat(0.05m, ForecastConstants.ForecastMonths).ToList();

        var inputs = MakeInputs(occupancy: new OccupancySchedule(UseDefault: false, UserRates: userRates));
        var geometry = new BuildingGeometry(RentableSqft: 1500m, TotalRentalUnits: 10);

        var result = OccupancyCalculator.Compute(inputs, geometry);

        Assert.All(result.Rates, r => Assert.Equal(0.05m, r));
    }

    // ---------------------------------------------------------------------
    // Rented_Units[m] = Ceiling(Total_Rental_Units × Occupancy_Rate[m]),
    // clamped to [0, Total_Rental_Units] (Requirements 4.3, 4.4)
    // ---------------------------------------------------------------------

    [Fact]
    public void RentedUnits_IsCeilingOfTotalRentalUnitsTimesRate()
    {
        // Total_Rental_Units = 10 and rate = 0.25 ⇒ ceil(10 × 0.25) = ceil(2.5) = 3.
        var rates = Enumerable.Repeat(0.25m, ForecastConstants.ForecastMonths).ToList();
        var inputs = MakeInputs(occupancy: new OccupancySchedule(UseDefault: false, UserRates: rates));
        var geometry = new BuildingGeometry(RentableSqft: 1500m, TotalRentalUnits: 10);

        var result = OccupancyCalculator.Compute(inputs, geometry);

        Assert.All(result.RentedUnits, u => Assert.Equal(3, u));
    }

    [Fact]
    public void RentedUnits_IsZero_WhenOccupancyRateIsZero()
    {
        var rates = Enumerable.Repeat(0m, ForecastConstants.ForecastMonths).ToList();
        var inputs = MakeInputs(occupancy: new OccupancySchedule(UseDefault: false, UserRates: rates));
        var geometry = new BuildingGeometry(RentableSqft: 1500m, TotalRentalUnits: 10);

        var result = OccupancyCalculator.Compute(inputs, geometry);

        Assert.All(result.RentedUnits, u => Assert.Equal(0, u));
    }

    [Fact]
    public void RentedUnits_EqualsTotalRentalUnits_WhenOccupancyRateIsOne()
    {
        var rates = Enumerable.Repeat(1.00m, ForecastConstants.ForecastMonths).ToList();
        var inputs = MakeInputs(occupancy: new OccupancySchedule(UseDefault: false, UserRates: rates));
        var geometry = new BuildingGeometry(RentableSqft: 1500m, TotalRentalUnits: 10);

        var result = OccupancyCalculator.Compute(inputs, geometry);

        Assert.All(result.RentedUnits, u => Assert.Equal(10, u));
    }

    [Fact]
    public void RentedUnits_AreClampedToClosedRangeZeroThroughTotalRentalUnits()
    {
        // A mix of extremes across the 36 months. Each entry ∈ [0, 1] as required
        // by Acceptance Criterion 4.2, but the assertion is that the *result* is
        // always in [0, Total_Rental_Units] per Acceptance Criterion 4.4.
        var rates = new List<decimal>();
        for (var m = 1; m <= ForecastConstants.ForecastMonths; m++)
        {
            rates.Add(m % 3 == 0 ? 0m : (m % 3 == 1 ? 0.5m : 1.00m));
        }

        var inputs = MakeInputs(occupancy: new OccupancySchedule(UseDefault: false, UserRates: rates));
        var geometry = new BuildingGeometry(RentableSqft: 1500m, TotalRentalUnits: 10);

        var result = OccupancyCalculator.Compute(inputs, geometry);

        Assert.All(result.RentedUnits, u =>
        {
            Assert.InRange(u, 0, geometry.TotalRentalUnits);
        });
    }

    [Fact]
    public void RentedUnits_AreZero_WhenTotalRentalUnitsIsZero()
    {
        // Empty building: Rentable_Sqft = 0 ⇒ Total_Rental_Units = 0 ⇒ every
        // Rented_Units[m] = ceil(0 × r) = 0 regardless of the schedule.
        var rates = Enumerable.Repeat(1.00m, ForecastConstants.ForecastMonths).ToList();
        var inputs = MakeInputs(occupancy: new OccupancySchedule(UseDefault: false, UserRates: rates));
        var geometry = new BuildingGeometry(RentableSqft: 0m, TotalRentalUnits: 0);

        var result = OccupancyCalculator.Compute(inputs, geometry);

        Assert.All(result.RentedUnits, u => Assert.Equal(0, u));
        Assert.All(result.RentedSqft, s => Assert.Equal(0m, s));
    }

    // ---------------------------------------------------------------------
    // Rented_Sqft[m] = Min(Rented_Units[m] × 150, Rentable_Sqft) (Requirement 4.5)
    // ---------------------------------------------------------------------

    [Fact]
    public void RentedSqft_EqualsRentedUnitsTimesStandardUnitSize_WhenBelowRentableSqft()
    {
        // Total_Rental_Units = 10, Rentable_Sqft = 1500 (exactly 10 × 150).
        // Rate = 0.50 ⇒ Rented_Units = ceil(5.0) = 5 ⇒ Rented_Sqft = 5 × 150 = 750 ≤ 1500.
        var rates = Enumerable.Repeat(0.50m, ForecastConstants.ForecastMonths).ToList();
        var inputs = MakeInputs(occupancy: new OccupancySchedule(UseDefault: false, UserRates: rates));
        var geometry = new BuildingGeometry(RentableSqft: 1500m, TotalRentalUnits: 10);

        var result = OccupancyCalculator.Compute(inputs, geometry);

        Assert.All(result.RentedSqft, s => Assert.Equal(750m, s));
    }

    [Fact]
    public void RentedSqft_ClampsToRentableSqft_WhenRentedUnitsTimes150Overshoots()
    {
        // Rentable_Sqft = 1000 ⇒ Total_Rental_Units = ceil(1000 / 150) = 7.
        // At 100% occupancy Rented_Units = 7 and Rented_Units × 150 = 1050 > 1000,
        // so Rented_Sqft is clamped to Rentable_Sqft (Design Decision 5).
        var rates = Enumerable.Repeat(1.00m, ForecastConstants.ForecastMonths).ToList();
        var inputs = MakeInputs(occupancy: new OccupancySchedule(UseDefault: false, UserRates: rates));
        var geometry = new BuildingGeometry(RentableSqft: 1000m, TotalRentalUnits: 7);

        var result = OccupancyCalculator.Compute(inputs, geometry);

        Assert.All(result.RentedUnits, u => Assert.Equal(7, u));
        Assert.All(result.RentedSqft, s => Assert.Equal(1000m, s));
    }

    [Fact]
    public void RentedSqft_NeverExceedsRentableSqft_ForAnyMonth()
    {
        // A pathological geometry where 7 × 150 = 1050 exceeds Rentable_Sqft = 1000
        // for any nonzero month. Mix rates so several months trigger the clamp
        // and several do not.
        var rates = new List<decimal>();
        for (var m = 1; m <= ForecastConstants.ForecastMonths; m++)
        {
            rates.Add(m % 2 == 0 ? 1.00m : 0.30m);
        }

        var inputs = MakeInputs(occupancy: new OccupancySchedule(UseDefault: false, UserRates: rates));
        var geometry = new BuildingGeometry(RentableSqft: 1000m, TotalRentalUnits: 7);

        var result = OccupancyCalculator.Compute(inputs, geometry);

        Assert.All(result.RentedSqft, s => Assert.True(s <= geometry.RentableSqft,
            $"Rented_Sqft ({s}) exceeded Rentable_Sqft ({geometry.RentableSqft})"));
    }

    [Fact]
    public void RentedSqft_IsZero_WhenOccupancyRateIsZero()
    {
        var rates = Enumerable.Repeat(0m, ForecastConstants.ForecastMonths).ToList();
        var inputs = MakeInputs(occupancy: new OccupancySchedule(UseDefault: false, UserRates: rates));
        var geometry = new BuildingGeometry(RentableSqft: 1500m, TotalRentalUnits: 10);

        var result = OccupancyCalculator.Compute(inputs, geometry);

        Assert.All(result.RentedSqft, s => Assert.Equal(0m, s));
    }

    // ---------------------------------------------------------------------
    // Shape invariants
    // ---------------------------------------------------------------------

    [Fact]
    public void Result_HasExactly36EntriesForEveryVector()
    {
        var inputs = MakeInputs(occupancy: new OccupancySchedule(UseDefault: true, UserRates: null));
        var geometry = new BuildingGeometry(RentableSqft: 1500m, TotalRentalUnits: 10);

        var result = OccupancyCalculator.Compute(inputs, geometry);

        Assert.Equal(ForecastConstants.ForecastMonths, result.Rates.Count);
        Assert.Equal(ForecastConstants.ForecastMonths, result.RentedUnits.Count);
        Assert.Equal(ForecastConstants.ForecastMonths, result.RentedSqft.Count);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="BuildingInputs"/> record whose only field that affects
    /// occupancy computation is <see cref="BuildingInputs.Occupancy"/>. All other
    /// fields are populated with harmless placeholder values so the record is
    /// well-formed.
    /// </summary>
    private static BuildingInputs MakeInputs(OccupancySchedule occupancy)
    {
        return new BuildingInputs(
            TotalSqft: 1000m,
            PercentageAvailableForRent: 1m,
            TotalBuildingCost: 500_000m,
            LandValue: 100_000m,
            DepreciationPeriodYears: 30,
            Occupancy: occupancy);
    }
}
