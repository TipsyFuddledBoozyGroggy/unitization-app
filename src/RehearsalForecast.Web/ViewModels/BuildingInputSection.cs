using System.ComponentModel.DataAnnotations;
using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Web.ViewModels;

/// <summary>
/// View-model section for building geometry, depreciable cost, and the
/// occupancy schedule (Requirements 3, 4, 8). <see cref="LandValue"/> is
/// captured for display only and is never referenced by any calculation in this
/// phase (Requirement 8.5, Design Decision 1).
/// </summary>
public sealed class BuildingInputSection
{
    /// <summary>Total warehouse floor area in square feet (Requirement 3.1).</summary>
    [Display(Name = "Total Sqft")]
    [Range(0.0, double.MaxValue, ErrorMessage = "Total Sqft must be zero or greater.")]
    public decimal TotalSqft { get; set; }

    /// <summary>
    /// Share of <see cref="TotalSqft"/> that can be rented, as a
    /// <see cref="decimal"/> in the inclusive range <c>[0, 1]</c>
    /// (Requirement 2.2).
    /// </summary>
    [Display(Name = "Percentage Available For Rent")]
    [Range(0.0, 1.0, ErrorMessage = "Percentage Available For Rent must be between 0 and 1.")]
    public decimal PercentageAvailableForRent { get; set; }

    /// <summary>
    /// Depreciable building cost in USD (Requirement 8.1). Drives
    /// <c>Monthly_Depreciation = Total_Building_Cost / (Depreciation_Period_Years * 12)</c>.
    /// </summary>
    [Display(Name = "Total Building Cost")]
    [Range(0.0, double.MaxValue, ErrorMessage = "Total Building Cost must be zero or greater.")]
    public decimal TotalBuildingCost { get; set; }

    /// <summary>
    /// Land value reported for display only. Never used in any calculation in
    /// this phase (Requirement 8.5, Design Decision 1).
    /// </summary>
    [Display(Name = "Land Value")]
    [Range(0.0, double.MaxValue, ErrorMessage = "Land Value must be zero or greater.")]
    public decimal LandValue { get; set; }

    /// <summary>
    /// Number of years over which the building is depreciated. Strictly
    /// positive integer (Requirement 2.3).
    /// </summary>
    [Display(Name = "Depreciation Period (Years)")]
    [Range(1, int.MaxValue, ErrorMessage = "Depreciation Period must be at least 1 year.")]
    public int DepreciationPeriodYears { get; set; } = 1;

    /// <summary>The occupancy schedule (default ramp or 36 user rates; Requirement 4).</summary>
    public OccupancyScheduleViewModel Occupancy { get; set; } = new();

    /// <summary>Maps this section to the domain <see cref="BuildingInputs"/> record.</summary>
    public BuildingInputs ToDomain() =>
        new(
            TotalSqft,
            PercentageAvailableForRent,
            TotalBuildingCost,
            LandValue,
            DepreciationPeriodYears,
            Occupancy.ToDomain());
}
