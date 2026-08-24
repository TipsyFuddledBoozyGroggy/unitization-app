namespace RehearsalForecast.Core.Schedules;

/// <summary>
/// Distinguishes the two ways a schedulable input can be supplied.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Constant"/> mode carries a single value that applies to every month
/// from Month 1 through Month 36 (Requirement 1.2).
/// </para>
/// <para>
/// <see cref="Variable"/> mode carries exactly 36 monthly values, one per month in
/// the forecast horizon (Requirement 1.4).
/// </para>
/// </remarks>
public enum ScheduleMode
{
    /// <summary>A single value applied to all 36 months.</summary>
    Constant,

    /// <summary>Exactly 36 monthly values, one per forecast month.</summary>
    Variable,
}
