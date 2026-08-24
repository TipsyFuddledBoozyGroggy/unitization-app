namespace RehearsalForecast.Core.Validation;

/// <summary>
/// Aggregated outcome of an <c>InputValidator</c> run over a single
/// <c>ForecastInputs</c> instance (design §10.5).
/// </summary>
/// <remarks>
/// When <see cref="IsValid"/> is <c>true</c>, <see cref="Errors"/> is empty. When
/// <see cref="IsValid"/> is <c>false</c>, <see cref="Errors"/> contains at least
/// one entry and may contain many — the validator reports every rule violation it
/// encounters rather than short-circuiting on the first failure.
/// </remarks>
/// <param name="IsValid"><c>true</c> iff every cross-field and structural rule passed.</param>
/// <param name="Errors">Ordered list of individual validation failures; empty when <see cref="IsValid"/> is <c>true</c>.</param>
public sealed record ValidationOutcome(
    bool IsValid,
    IReadOnlyList<ValidationError> Errors);
