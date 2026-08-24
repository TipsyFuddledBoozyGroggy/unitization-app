using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Core.Solving;

/// <summary>
/// Contract for the target-price solver (design §4.3, Requirement 15).
/// </summary>
/// <remarks>
/// <para>
/// The interface exists so that the web layer's <c>Calculate</c> action
/// (design §11.1) can be tested without running the full search, and so
/// that alternative solver implementations can be substituted for testing
/// (design §2, §4).
/// </para>
/// <para>
/// Every implementation must guarantee the invariants encoded in
/// <see cref="SolverResult"/>: either a <see cref="SolverResult.Success"/>
/// whose <c>FlatPricePerSqft</c> is the minimum nonnegative price (rounded
/// up to <see cref="ForecastConstants.CurrencyPrecision"/>) for which the
/// Cash_Positive_Rule holds (Requirement 15.1, 15.8), or a
/// <see cref="SolverResult.Failure"/> when
/// <see cref="ForecastConstants.SolverSafetyLimit"/> is exceeded
/// (Requirement 15.11). Implementations must be deterministic (Requirement
/// 15.2) and must use <see cref="decimal"/> throughout (Requirement 15.12).
/// </para>
/// </remarks>
public interface ISolver
{
    /// <summary>
    /// Returns the minimum nonnegative <c>Flat_Price_Per_Sqft</c> (rounded
    /// up to <see cref="ForecastConstants.CurrencyPrecision"/>) that
    /// satisfies the Cash_Positive_Rule, or a
    /// <see cref="SolverResult.Failure"/> when the
    /// <see cref="ForecastConstants.SolverSafetyLimit"/> is exceeded before
    /// convergence (Requirement 15.11).
    /// </summary>
    /// <param name="inputs">
    /// The fully-validated aggregate of every user-supplied input required
    /// by the calculator (Requirement 17). Must be non-null.
    /// </param>
    /// <returns>
    /// A <see cref="SolverResult"/>: either <see cref="SolverResult.Success"/>
    /// carrying the minimum <c>Flat_Price_Per_Sqft</c> and the fresh
    /// <see cref="Forecast.ForecastResult"/> produced at that price, or
    /// <see cref="SolverResult.Failure"/> carrying a human-readable reason
    /// and the terminal iteration count.
    /// </returns>
    SolverResult Solve(ForecastInputs inputs);
}
