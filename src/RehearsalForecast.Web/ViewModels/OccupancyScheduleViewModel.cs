using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Web.ViewModels;

/// <summary>
/// View-model carrier for the occupancy input. Mirrors the domain
/// <see cref="OccupancySchedule"/> discriminated shape (Requirement 4.1, 4.2).
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="UseDefault"/> is <see langword="true"/> the calculator will
/// use the built-in ramp <c>Occupancy_Rate[m] = Min(m * 0.10, 1.00)</c>
/// (Requirement 4.1) and <see cref="UserRates"/> is ignored.
/// </para>
/// <para>
/// When <see cref="UseDefault"/> is <see langword="false"/>, the 36 user-supplied
/// rates in <see cref="UserRates"/> are passed through. Each entry must be a
/// <see cref="decimal"/> in the inclusive range <c>[0, 1]</c>; that structural
/// range check lives in <c>InputValidator</c> (Requirement 2.10) rather than on
/// this view model because data annotations do not apply element-wise.
/// </para>
/// </remarks>
public sealed class OccupancyScheduleViewModel
{
    /// <summary>
    /// <see langword="true"/> to use the built-in ramp
    /// <c>Min(m * 0.10, 1.00)</c> (Requirement 4.1); <see langword="false"/> to
    /// use <see cref="UserRates"/> (Requirement 4.2).
    /// </summary>
    public bool UseDefault { get; set; } = true;

    /// <summary>
    /// The 36 user-supplied monthly occupancy rates used when
    /// <see cref="UseDefault"/> is <see langword="false"/>. Prepopulated with 36
    /// zero entries so the form editor can address every month by index; the
    /// UI seeds this with the default ramp when the user toggles to variable
    /// mode (Requirement 4.7).
    /// </summary>
    public List<decimal> UserRates { get; set; } = new(new decimal[ForecastConstants.ForecastMonths]);

    /// <summary>
    /// Converts this view model to a domain <see cref="OccupancySchedule"/>.
    /// When <see cref="UseDefault"/> is <see langword="true"/> a
    /// <see cref="OccupancySchedule"/> with <c>UserRates = null</c> is returned
    /// so the calculator will apply the built-in ramp.
    /// </summary>
    /// <returns>The domain occupancy schedule.</returns>
    public OccupancySchedule ToDomain() =>
        UseDefault
            ? new OccupancySchedule(true, UserRates: null)
            : new OccupancySchedule(false, UserRates.ToArray());
}
