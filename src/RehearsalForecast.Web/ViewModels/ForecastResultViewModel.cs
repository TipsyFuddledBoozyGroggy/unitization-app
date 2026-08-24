using RehearsalForecast.Core.Forecast;

namespace RehearsalForecast.Web.ViewModels;

/// <summary>
/// View model rendered by the results page. Carries the round-tripped user
/// inputs (for the "Export CSV" form and the solver re-run on export;
/// Design §11.6), the calculator output when the solver succeeded, and the
/// human-readable failure message when it did not.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="SolverFailureMessage"/> is non-<see langword="null"/>, the
/// view suppresses <c>Flat_Price_Per_Sqft</c>, the 36-row detail table, and
/// the CSV export form (Requirement 27.7); <see cref="Result"/> is
/// <see langword="null"/> in that case.
/// </para>
/// </remarks>
public sealed class ForecastResultViewModel
{
    /// <summary>
    /// The exact input view model the user submitted, kept so the results page
    /// can round-trip it to the CSV export action (Design §11.6). Never
    /// <see langword="null"/>.
    /// </summary>
    public ForecastInputViewModel Inputs { get; init; } = new();

    /// <summary>
    /// The solver's forecast result. <see langword="null"/> when the solver
    /// failed (see <see cref="SolverFailureMessage"/>, Requirement 27.7).
    /// </summary>
    public ForecastResult? Result { get; init; }

    /// <summary>
    /// A human-readable solver-failure message when the solver breached the
    /// safety limit (Requirement 15.12). <see langword="null"/> on success.
    /// </summary>
    public string? SolverFailureMessage { get; init; }
}
