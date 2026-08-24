namespace RehearsalForecast.Core.Domain;

/// <summary>
/// Building geometry, capital-cost accounting for depreciation, and the occupancy
/// schedule. <see cref="LandValue"/> is captured for display purposes only and is
/// never referenced by any calculation in this phase (Requirement 8.5, Design
/// Decision 1).
/// </summary>
/// <param name="TotalSqft">Total warehouse floor area in square feet. Nonnegative (Requirement 3.1).</param>
/// <param name="PercentageAvailableForRent">
/// Share of <see cref="TotalSqft"/> that can be rented, as a <see cref="decimal"/> in
/// the inclusive range <c>[0, 1]</c> (Requirement 2.2). Contract only, not enforced
/// by the type.
/// </param>
/// <param name="TotalBuildingCost">
/// Depreciable building cost in USD (Requirement 8.1). Divided by
/// <c>Depreciation_Period_Years * 12</c> to produce <c>Monthly_Depreciation</c>.
/// </param>
/// <param name="LandValue">
/// Reported for display only. Never used in any calculation in this phase
/// (Requirement 8.5, Design Decision 1).
/// </param>
/// <param name="DepreciationPeriodYears">
/// Number of years over which the building is depreciated. Strictly positive
/// (Requirement 2.3).
/// </param>
/// <param name="Occupancy">
/// Discriminated occupancy input (default ramp versus 36 user-supplied rates,
/// Requirement 4).
/// </param>
public sealed record BuildingInputs(
    decimal TotalSqft,
    decimal PercentageAvailableForRent,
    decimal TotalBuildingCost,
    decimal LandValue,
    int DepreciationPeriodYears,
    OccupancySchedule Occupancy);
