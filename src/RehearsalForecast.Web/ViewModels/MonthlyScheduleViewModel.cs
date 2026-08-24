using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Schedules;

namespace RehearsalForecast.Web.ViewModels;

/// <summary>
/// View-model carrier for a schedulable <see cref="decimal"/> input. Mirrors
/// <see cref="MonthlySchedule{T}"/> in shape and converts to the domain type via
/// <see cref="ToDomain"/> (Design §11.2).
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Mode"/> field decides which of <see cref="ConstantValue"/> or
/// <see cref="MonthlyValues"/> is authoritative. <see cref="MonthlyValues"/> is
/// always sized to exactly <see cref="ForecastConstants.ForecastMonths"/> (36)
/// so that model binding and the Razor form editor can address every slot by
/// index (Requirement 1.4).
/// </para>
/// <para>
/// Single-field range checks for each monthly value are performed at the
/// structural level by <c>InputValidator</c> (Requirement 2.9); this view model
/// carries no <c>[Range]</c> annotations on its list elements because data
/// annotations do not apply element-wise.
/// </para>
/// </remarks>
public sealed class MonthlyScheduleViewModel
{
    /// <summary>
    /// The mode in which the user supplied this schedule. Defaults to
    /// <see cref="ScheduleMode.Constant"/> for new forms (Requirement 1.1).
    /// </summary>
    public ScheduleMode Mode { get; set; } = ScheduleMode.Constant;

    /// <summary>
    /// The single value applied to every month when <see cref="Mode"/> is
    /// <see cref="ScheduleMode.Constant"/> (Requirement 1.2). Ignored when
    /// <see cref="Mode"/> is <see cref="ScheduleMode.Variable"/>.
    /// </summary>
    public decimal ConstantValue { get; set; }

    /// <summary>
    /// The 36 monthly values used when <see cref="Mode"/> is
    /// <see cref="ScheduleMode.Variable"/> (Requirement 1.4). Prepopulated with
    /// 36 zero entries so form binders can address every month by index.
    /// </summary>
    public List<decimal> MonthlyValues { get; set; } = new(new decimal[ForecastConstants.ForecastMonths]);

    /// <summary>
    /// Converts this view model to a domain <see cref="MonthlySchedule{T}"/>.
    /// Uses <see cref="MonthlySchedule{T}.Constant(T)"/> or
    /// <see cref="MonthlySchedule{T}.Variable(System.Collections.Generic.IReadOnlyList{T})"/>
    /// according to <see cref="Mode"/>.
    /// </summary>
    /// <returns>The domain schedule.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown by <see cref="MonthlySchedule{T}.Variable(System.Collections.Generic.IReadOnlyList{T})"/>
    /// when <see cref="Mode"/> is <see cref="ScheduleMode.Variable"/> and
    /// <see cref="MonthlyValues"/> does not contain exactly 36 entries. In
    /// production the <c>InputValidator</c> (Requirement 2.9) rejects
    /// short/long submissions before reaching this method.
    /// </exception>
    public MonthlySchedule<decimal> ToDomain() =>
        Mode == ScheduleMode.Constant
            ? MonthlySchedule<decimal>.Constant(ConstantValue)
            : MonthlySchedule<decimal>.Variable(MonthlyValues);
}
