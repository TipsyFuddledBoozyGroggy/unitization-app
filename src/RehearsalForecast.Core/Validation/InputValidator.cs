using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Core.Validation;

/// <summary>
/// Default <see cref="IInputValidator"/> implementation. Enforces the cross-field
/// and structural rules described in design §10.3:
/// <list type="bullet">
///   <item><description>R2.9 — variable-mode <see cref="OccupancySchedule"/> must
///   have exactly 36 entries. (<c>MonthlySchedule&lt;T&gt;.Variable</c> already
///   enforces the same rule at construction time for the marketing and
///   operations schedules, so this validator only inspects
///   <see cref="OccupancySchedule"/>, which is a plain record.)</description></item>
///   <item><description>R2.10 — each user-supplied occupancy rate must lie in
///   the inclusive range <c>[0, 1]</c>; errors identify the offending month
///   via a 0-based index into <see cref="OccupancySchedule.UserRates"/>.</description></item>
///   <item><description>R10.5 — <c>Owner_Investment &gt; Total_Capital</c> is
///   explicitly permitted; no rule in this validator blocks it.</description></item>
/// </list>
/// <para>
/// Single-field range checks (R2.1–R2.8) are the view model's responsibility
/// (task 55); this validator must accept any <see cref="ForecastInputs"/>
/// whose scalar fields already satisfy those contract ranges.
/// </para>
/// </summary>
public sealed class InputValidator : IInputValidator
{
    /// <inheritdoc />
    public ValidationOutcome Validate(ForecastInputs inputs)
    {
        var errors = new List<ValidationError>();

        ValidateOccupancy(inputs, errors);

        return new ValidationOutcome(
            IsValid: errors.Count == 0,
            Errors: errors);
    }

    /// <summary>
    /// Enforces R2.9 and R2.10 on the occupancy schedule. When
    /// <see cref="OccupancySchedule.UseDefault"/> is <see langword="true"/> the
    /// user rates are not inspected — the calculator will use the built-in
    /// ramp (Requirement 4.1). When <see cref="OccupancySchedule.UseDefault"/>
    /// is <see langword="false"/>, <see cref="OccupancySchedule.UserRates"/>
    /// must be non-null, have exactly 36 entries, and every entry must lie in
    /// <c>[0, 1]</c>.
    /// </summary>
    private static void ValidateOccupancy(
        ForecastInputs inputs,
        List<ValidationError> errors)
    {
        var occupancy = inputs.Building.Occupancy;

        // Default ramp: no user rates to validate.
        if (occupancy.UseDefault)
        {
            return;
        }

        // R2.9: variable-mode occupancy requires a supplied user-rate vector.
        if (occupancy.UserRates is null)
        {
            errors.Add(new ValidationError(
                FieldPath: "Building.Occupancy.UserRates",
                Message: "Occupancy user rates must be supplied when Use_Default is false."));
            return;
        }

        // R2.9: user-rate vector length must equal ForecastMonths (36).
        if (occupancy.UserRates.Count != ForecastConstants.ForecastMonths)
        {
            errors.Add(new ValidationError(
                FieldPath: "Building.Occupancy.UserRates",
                Message:
                    $"Occupancy user rates must contain exactly {ForecastConstants.ForecastMonths} entries; " +
                    $"received {occupancy.UserRates.Count}."));
            return;
        }

        // R2.10: each user rate must lie in the inclusive range [0, 1]. Emit
        // one error per offending month so the UI can render a message next to
        // the specific input cell (design §10.4).
        for (var i = 0; i < occupancy.UserRates.Count; i++)
        {
            var rate = occupancy.UserRates[i];
            if (rate < 0m || rate > 1m)
            {
                errors.Add(new ValidationError(
                    FieldPath: $"Building.Occupancy.UserRates[{i}]",
                    Message:
                        $"Occupancy rate for month {i + 1} must be between 0 and 1 inclusive; " +
                        $"received {rate}."));
            }
        }
    }
}
