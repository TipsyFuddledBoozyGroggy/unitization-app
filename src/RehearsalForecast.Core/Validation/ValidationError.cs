namespace RehearsalForecast.Core.Validation;

/// <summary>
/// A single cross-field or structural validation failure produced by
/// <c>InputValidator</c> (Requirement 2, design §10).
/// </summary>
/// <remarks>
/// Field-shape validation (range checks, required-value checks) is enforced by
/// data annotations on the view model; <see cref="ValidationError"/> covers the
/// rules that cannot be expressed as a single-field attribute.
/// </remarks>
/// <param name="FieldPath">Dotted path identifying the offending field, e.g. <c>"Building.PercentageAvailableForRent"</c> or <c>"Building.Occupancy.UserRates[7]"</c>.</param>
/// <param name="Message">Human-readable explanation of the validation failure suitable for display next to the input field.</param>
public sealed record ValidationError(
    string FieldPath,
    string Message);
