using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Core.Forecast;

/// <summary>
/// Pass 2 of the forecast calculator (design §6.2). Materialises the 36-month
/// occupancy schedule and derives the per-month rented-inventory figures used by
/// later passes. All arithmetic is performed with <see cref="decimal"/>; no
/// binary floating-point is introduced.
/// </summary>
internal static class OccupancyCalculator
{
    /// <summary>
    /// Produces a 36-element <see cref="OccupancyResult"/> for the supplied inputs
    /// and precomputed <paramref name="geometry"/>.
    /// </summary>
    /// <param name="inputs">
    /// Full building inputs. Only <see cref="BuildingInputs.Occupancy"/> is read
    /// by this pass; other fields are consumed by other passes.
    /// </param>
    /// <param name="geometry">
    /// Pass 1 output. <see cref="BuildingGeometry.TotalRentalUnits"/> drives
    /// <see cref="OccupancyResult.RentedUnits"/> and
    /// <see cref="BuildingGeometry.RentableSqft"/> is the upper clamp for
    /// <see cref="OccupancyResult.RentedSqft"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// When <see cref="OccupancySchedule.UseDefault"/> is <see langword="true"/>,
    /// <c>Rates[m − 1] = Min(m × 0.10, 1.00)</c> for every <c>m ∈ [1, 36]</c>
    /// (Requirement 4.1). Otherwise <c>Rates[m − 1]</c> is taken directly from
    /// <see cref="OccupancySchedule.UserRates"/> (Requirement 4.2). The validator
    /// guarantees the user-supplied schedule has exactly 36 entries, each in
    /// <c>[0, 1]</c>; this pass performs no defensive re-validation.
    /// </para>
    /// <para>
    /// For every month:
    /// <c>RentedUnits[m − 1] = clamp(ceil(TotalRentalUnits × Rates[m − 1]), 0, TotalRentalUnits)</c>
    /// (Requirements 4.3, 4.4) and
    /// <c>RentedSqft[m − 1] = Min(RentedUnits[m − 1] × StandardUnitSize, RentableSqft)</c>
    /// (Requirement 4.5, Design Decision 5).
    /// </para>
    /// </remarks>
    /// <returns>
    /// An <see cref="OccupancyResult"/> whose vectors each contain
    /// <see cref="ForecastConstants.ForecastMonths"/> entries.
    /// </returns>
    internal static OccupancyResult Compute(BuildingInputs inputs, BuildingGeometry geometry)
    {
        var months = ForecastConstants.ForecastMonths;
        var rates = new decimal[months];
        var rentedUnits = new int[months];
        var rentedSqft = new decimal[months];

        if (inputs.Occupancy.UseDefault)
        {
            // Requirement 4.1: default ramp Min(m × 0.10, 1.00). Saturates at
            // 1.00 from month 10 onward.
            for (var i = 0; i < months; i++)
            {
                var m = i + 1;
                rates[i] = Math.Min(m * 0.10m, 1.00m);
            }
        }
        else
        {
            // Requirement 4.2: variable mode uses the 36 user-supplied rates
            // verbatim. Length and range are enforced by InputValidator.
            var userRates = inputs.Occupancy.UserRates!;
            for (var i = 0; i < months; i++)
            {
                rates[i] = userRates[i];
            }
        }

        var totalRentalUnits = geometry.TotalRentalUnits;
        var rentableSqft = geometry.RentableSqft;

        for (var i = 0; i < months; i++)
        {
            // Requirement 4.3: Rented_Units = Ceiling(Total_Rental_Units × Occupancy_Rate).
            // Math.Ceiling on decimal preserves decimal precision; cast to int is
            // safe because the product cannot exceed Total_Rental_Units × 1.00.
            var product = totalRentalUnits * rates[i];
            var ceiled = (int)Math.Ceiling(product);

            // Requirement 4.4: clamp to [0, Total_Rental_Units]. The upper clamp
            // guards against rates > 1.00 that may have slipped past validation;
            // the lower clamp guards against negative rates for the same reason.
            var clamped = Math.Clamp(ceiled, 0, totalRentalUnits);
            rentedUnits[i] = clamped;

            // Requirement 4.5 / Design Decision 5: Rented_Sqft is clamped to
            // Rentable_Sqft when Rented_Units × Standard_Unit_Size would overshoot.
            // Rented_Units itself retains its ceiling-based value (Design Decision 5).
            var uncappedSqft = clamped * ForecastConstants.StandardUnitSize;
            rentedSqft[i] = Math.Min(uncappedSqft, rentableSqft);
        }

        return new OccupancyResult(rates, rentedUnits, rentedSqft);
    }
}
