using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Core.Validation;

/// <summary>
/// Runs the cross-field and structural validation rules that cannot be
/// expressed as single-field data annotations on the view model
/// (design §4.4, §10.3).
/// </summary>
/// <remarks>
/// <para>
/// Single-field range checks (R2.1–R2.8) are enforced by data annotations on
/// <c>ForecastInputViewModel</c> at model-binding time; this validator is not
/// responsible for those.
/// </para>
/// <para>
/// The controller must observe the returned <see cref="ValidationOutcome"/>
/// before invoking <c>ISolver.Solve</c> or <c>IForecastCalculator.Compute</c>
/// (R2.13, R27.9, design §10.5).
/// </para>
/// </remarks>
public interface IInputValidator
{
    /// <summary>
    /// Validates the supplied <see cref="ForecastInputs"/> against every
    /// cross-field and structural rule owned by this validator.
    /// </summary>
    /// <param name="inputs">The aggregate user inputs to validate.</param>
    /// <returns>
    /// A <see cref="ValidationOutcome"/> whose <see cref="ValidationOutcome.IsValid"/>
    /// is <see langword="true"/> iff every rule passed. When
    /// <see cref="ValidationOutcome.IsValid"/> is <see langword="false"/>,
    /// <see cref="ValidationOutcome.Errors"/> contains one entry per rule
    /// violation encountered; the validator does not short-circuit on the
    /// first failure.
    /// </returns>
    ValidationOutcome Validate(ForecastInputs inputs);
}
