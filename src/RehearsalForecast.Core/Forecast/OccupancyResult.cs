namespace RehearsalForecast.Core.Forecast;

/// <summary>
/// Output of Pass 2 of the forecast calculator (design §6.2). Contains the
/// materialised 36-month occupancy schedule and the per-month rented-inventory
/// figures derived from it. Every vector has length
/// <see cref="Constants.ForecastConstants.ForecastMonths"/> (36); index
/// <c>i</c> corresponds to Month <c>i + 1</c>.
/// </summary>
/// <param name="Rates">
/// Materialised occupancy rate per month. Under the default schedule
/// <c>Rates[i] = Min((i + 1) × 0.10, 1.00)</c> (Requirement 4.1); under variable
/// mode <c>Rates[i]</c> is the user-supplied rate for Month <c>i + 1</c>
/// (Requirement 4.2).
/// </param>
/// <param name="RentedUnits">
/// <c>Ceiling(Total_Rental_Units × Rates[i])</c> clamped to
/// <c>[0, Total_Rental_Units]</c> (Requirements 4.3, 4.4).
/// </param>
/// <param name="RentedSqft">
/// <c>Min(RentedUnits[i] × Standard_Unit_Size, Rentable_Sqft)</c>
/// (Requirement 4.5, Design Decision 5).
/// </param>
internal sealed record OccupancyResult(
    IReadOnlyList<decimal> Rates,
    IReadOnlyList<int> RentedUnits,
    IReadOnlyList<decimal> RentedSqft);
