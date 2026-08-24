using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Core.Forecast;

/// <summary>
/// Pass 6 of the forecast pipeline (design §6.6, Requirement 8): straight-line
/// monthly depreciation of the building over a user-selected number of years.
/// </summary>
/// <remarks>
/// <para>
/// <c>Monthly_Depreciation = Total_Building_Cost / (Depreciation_Period_Years × 12)</c>
/// (Requirement 8.1). The same value is applied identically to every month
/// <c>m ∈ [1, 36]</c> (Requirement 8.2); returning a single scalar
/// <see cref="decimal"/> structurally guarantees that constancy because there
/// is no per-month channel through which the value could vary.
/// </para>
/// <para>
/// The depreciable amount is <see cref="BuildingInputs.TotalBuildingCost"/>
/// alone. Per Design Decision 1 and Requirements 8.3–8.5, this pass explicitly
/// does not read <see cref="BuildingInputs.LandValue"/> and cannot see the
/// non-building capital line items (<c>Equipment</c>,
/// <c>TotalImprovementCost</c>, <c>BuildingPurchaseCost</c>,
/// <c>OtherCapitalCost</c>) — those live on the sibling <c>CapitalInputs</c>
/// record and are therefore not part of this helper's API surface.
/// </para>
/// <para>
/// This helper is deliberately <see langword="internal"/>: it is a per-pass
/// building block for <c>ForecastCalculator</c>, exposed to the test project
/// via <c>InternalsVisibleTo</c>. All arithmetic runs on <see cref="decimal"/>
/// in accordance with Requirement 19.1 (no <see cref="double"/>/<see cref="float"/>).
/// </para>
/// </remarks>
internal static class DepreciationCalculator
{
    /// <summary>
    /// Computes the Pass 6 <c>Monthly_Depreciation</c> scalar from the
    /// supplied <paramref name="building"/> inputs.
    /// </summary>
    /// <param name="building">
    /// Validated building inputs. Only
    /// <see cref="BuildingInputs.TotalBuildingCost"/> and
    /// <see cref="BuildingInputs.DepreciationPeriodYears"/> are consulted;
    /// <see cref="BuildingInputs.LandValue"/> and the occupancy/geometry
    /// fields are ignored (Requirements 8.3, 8.5).
    /// </param>
    /// <returns>
    /// <c>Total_Building_Cost / (Depreciation_Period_Years × 12)</c> as a
    /// <see cref="decimal"/>, or <c>0m</c> when
    /// <see cref="BuildingInputs.TotalBuildingCost"/> is zero.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="building"/> is <see langword="null"/>.</exception>
    internal static decimal Compute(BuildingInputs building)
    {
        ArgumentNullException.ThrowIfNull(building);

        // Requirement 8.1: Monthly_Depreciation = Total_Building_Cost / (Depreciation_Period_Years × 12).
        // Requirement 19.1: all arithmetic in decimal.
        //
        // Zero depreciable amount short-circuits to 0m: this avoids materialising the
        // divisor for the common "no building cost yet" scenario and makes the zero
        // case explicit at the call site (test-visible via DepreciationTests).
        // DepreciationPeriodYears is guaranteed > 0 by Requirement 2.3 (enforced at
        // the view-model / validator boundary).
        if (building.TotalBuildingCost == 0m)
        {
            return 0m;
        }

        return building.TotalBuildingCost / (building.DepreciationPeriodYears * 12m);
    }
}
