using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Core.Forecast;

/// <summary>
/// Result of Pass 1 of the forecast pipeline (design §6.1): the derived
/// building-geometry quantities that every downstream pass depends on.
/// </summary>
/// <remarks>
/// Held internally and consumed by <c>ForecastCalculator</c> when assembling
/// <see cref="ForecastResult"/>; not exposed on the public API surface of
/// <c>RehearsalForecast.Core</c>. Both fields are always nonnegative for any
/// valid <see cref="BuildingInputs"/> (Requirements 3.1 &amp; 3.2).
/// </remarks>
/// <param name="RentableSqft">
/// <c>Total_Sqft × Percentage_Available_For_Rent</c> (Requirement 3.1). A
/// <see cref="decimal"/> per Requirement 19.1; no truncation of the product.
/// </param>
/// <param name="TotalRentalUnits">
/// <c>ceil(Rentable_Sqft / StandardUnitSize)</c> (Requirement 3.2), or
/// <c>0</c> when <see cref="RentableSqft"/> is zero (Requirement 3.3). Carried
/// as <see cref="int"/> because the unit count is a pure count.
/// </param>
internal sealed record BuildingGeometry(
    decimal RentableSqft,
    int TotalRentalUnits);

/// <summary>
/// Pass 1 of the forecast pipeline: derives rentable square footage and the
/// unit-capacity count from <see cref="BuildingInputs"/> (design §6.1,
/// Requirement 3).
/// </summary>
/// <remarks>
/// This helper is deliberately <see langword="internal"/>: it is a per-pass
/// building block used by <c>ForecastCalculator</c> and exposed to the test
/// project via <c>InternalsVisibleTo</c>. All arithmetic is performed on
/// <see cref="decimal"/> in accordance with Requirement 19.1; the ceiling
/// step uses <see cref="Math.Ceiling(decimal)"/> so no <see cref="double"/>
/// conversion occurs at any point (design §20.4).
/// </remarks>
internal static class BuildingGeometryCalculator
{
    /// <summary>
    /// Computes the Pass 1 building geometry for the supplied
    /// <paramref name="building"/> inputs.
    /// </summary>
    /// <param name="building">
    /// Validated building inputs. <c>Total_Sqft</c> and
    /// <c>Percentage_Available_For_Rent</c> are the only fields consulted;
    /// capital, depreciation, and occupancy fields are ignored.
    /// </param>
    /// <returns>
    /// A <see cref="BuildingGeometry"/> containing
    /// <c>Rentable_Sqft = Total_Sqft × Percentage_Available_For_Rent</c> and
    /// <c>Total_Rental_Units = ceil(Rentable_Sqft / ForecastConstants.StandardUnitSize)</c>
    /// (or <c>0</c> when <c>Rentable_Sqft</c> is zero — Requirement 3.3).
    /// </returns>
    internal static BuildingGeometry Compute(BuildingInputs building)
    {
        ArgumentNullException.ThrowIfNull(building);

        // Requirement 3.1: Rentable_Sqft = Total_Sqft × Percentage_Available_For_Rent.
        // decimal × decimal preserves the exact product without truncation.
        decimal rentableSqft = building.TotalSqft * building.PercentageAvailableForRent;

        // Requirement 3.2: Total_Rental_Units = Ceiling(Rentable_Sqft / StandardUnitSize).
        // Math.Ceiling(decimal) → decimal keeps the entire computation on the decimal
        // rail (design §20.4); the final cast to int is safe because the value is a
        // nonnegative whole number by construction.
        //
        // Requirement 3.3: Rentable_Sqft == 0 short-circuits to zero units so that
        // the divisor never has to be inspected and the result is unambiguously zero.
        int totalRentalUnits = rentableSqft == 0m
            ? 0
            : (int)Math.Ceiling(rentableSqft / ForecastConstants.StandardUnitSize);

        return new BuildingGeometry(rentableSqft, totalRentalUnits);
    }
}
