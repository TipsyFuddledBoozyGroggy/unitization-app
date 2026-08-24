using RehearsalForecast.Core.Constants;

namespace RehearsalForecast.Core.Schedules;

/// <summary>
/// A value that is either a single constant applied to all 36 months
/// (<see cref="ScheduleMode.Constant"/>) or an explicit 36-element sequence
/// (<see cref="ScheduleMode.Variable"/>).
/// </summary>
/// <typeparam name="T">The value type carried by the schedule. Constrained to
/// <see langword="struct"/> so that monetary schedules can safely use
/// <see cref="decimal"/> without introducing nullability.</typeparam>
/// <remarks>
/// <para>
/// Invariants:
/// </para>
/// <list type="bullet">
///   <item>
///     When <see cref="Mode"/> is <see cref="ScheduleMode.Constant"/>, only
///     <see cref="ConstantValue"/> is meaningful. <see cref="MonthlyValues"/> is still
///     exposed as a fully-populated 36-element list (every entry equal to
///     <see cref="ConstantValue"/>) so that callers may treat it uniformly.
///   </item>
///   <item>
///     When <see cref="Mode"/> is <see cref="ScheduleMode.Variable"/>, only
///     <see cref="MonthlyValues"/> is meaningful. Its length is exactly
///     <see cref="ForecastConstants.ForecastMonths"/> (36) and
///     <see cref="ConstantValue"/> is the type's <see langword="default"/> value.
///   </item>
///   <item>
///     Calculation code MUST use <see cref="At(int)"/> to read values. It MUST NOT branch
///     on <see cref="Mode"/>. That is the entire point of this type.
///   </item>
/// </list>
/// </remarks>
public sealed class MonthlySchedule<T>
    where T : struct
{
    private readonly IReadOnlyList<T> _monthlyValues;

    private MonthlySchedule(ScheduleMode mode, T constantValue, IReadOnlyList<T> monthlyValues)
    {
        Mode = mode;
        ConstantValue = constantValue;
        _monthlyValues = monthlyValues;
    }

    /// <summary>The mode this schedule was constructed in.</summary>
    public ScheduleMode Mode { get; }

    /// <summary>
    /// The single value applied to every month when <see cref="Mode"/> is
    /// <see cref="ScheduleMode.Constant"/>. Equal to <see langword="default"/>(<typeparamref name="T"/>)
    /// when <see cref="Mode"/> is <see cref="ScheduleMode.Variable"/>.
    /// </summary>
    public T ConstantValue { get; }

    /// <summary>
    /// The 36 monthly values. Always exactly
    /// <see cref="ForecastConstants.ForecastMonths"/> elements long, regardless of
    /// <see cref="Mode"/>.
    /// </summary>
    public IReadOnlyList<T> MonthlyValues => _monthlyValues;

    /// <summary>
    /// Creates a <see cref="ScheduleMode.Constant"/>-mode schedule that returns
    /// <paramref name="value"/> for every month.
    /// </summary>
    /// <param name="value">The single value applied to all 36 months.</param>
    public static MonthlySchedule<T> Constant(T value)
    {
        var expanded = new T[ForecastConstants.ForecastMonths];
        for (var i = 0; i < expanded.Length; i++)
        {
            expanded[i] = value;
        }

        return new MonthlySchedule<T>(ScheduleMode.Constant, value, expanded);
    }

    /// <summary>
    /// Creates a <see cref="ScheduleMode.Variable"/>-mode schedule from exactly 36 monthly values.
    /// </summary>
    /// <param name="values">The 36 monthly values, ordered from Month 1 to Month 36.</param>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="values"/> does not contain exactly
    /// <see cref="ForecastConstants.ForecastMonths"/> (36) elements.
    /// </exception>
    public static MonthlySchedule<T> Variable(IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count != ForecastConstants.ForecastMonths)
        {
            throw new ArgumentException(
                $"A variable-mode schedule requires exactly {ForecastConstants.ForecastMonths} monthly values, "
                    + $"but {values.Count} were provided.",
                nameof(values));
        }

        // Defensive copy so the schedule cannot be mutated by callers after construction.
        var copy = new T[ForecastConstants.ForecastMonths];
        for (var i = 0; i < copy.Length; i++)
        {
            copy[i] = values[i];
        }

        return new MonthlySchedule<T>(ScheduleMode.Variable, default, copy);
    }

    /// <summary>
    /// Returns the value for the given 1-based month.
    /// </summary>
    /// <param name="month">Month number in the inclusive range <c>[1, 36]</c>.</param>
    /// <returns>
    /// <see cref="ConstantValue"/> when <see cref="Mode"/> is <see cref="ScheduleMode.Constant"/>;
    /// <c>MonthlyValues[month - 1]</c> when <see cref="Mode"/> is <see cref="ScheduleMode.Variable"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="month"/> is not in <c>[1, <see cref="ForecastConstants.ForecastMonths"/>]</c>.
    /// </exception>
    public T At(int month)
    {
        if (month < 1 || month > ForecastConstants.ForecastMonths)
        {
            throw new ArgumentOutOfRangeException(
                nameof(month),
                month,
                $"Month must be in the inclusive range [1, {ForecastConstants.ForecastMonths}].");
        }

        return Mode == ScheduleMode.Constant ? ConstantValue : _monthlyValues[month - 1];
    }
}
