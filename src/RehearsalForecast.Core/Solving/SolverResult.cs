using RehearsalForecast.Core.Forecast;

namespace RehearsalForecast.Core.Solving;

/// <summary>
/// Result of the target-price solver: either a converged <see cref="Success"/>
/// carrying the minimum <c>Flat_Price_Per_Sqft</c> that satisfies the
/// Cash_Positive_Rule, or a <see cref="Failure"/> when the Solver_Safety_Limit is
/// exceeded (Requirement 15.11).
/// </summary>
/// <remarks>
/// This is a closed discriminated union: only <see cref="Success"/> and
/// <see cref="Failure"/> may derive from <see cref="SolverResult"/>, enforced by the
/// private constructor being visible only to nested types.
/// </remarks>
public abstract record SolverResult
{
    /// <summary>
    /// Private constructor closes the type hierarchy so only the nested
    /// <see cref="Success"/> and <see cref="Failure"/> variants may extend
    /// <see cref="SolverResult"/>.
    /// </summary>
    private SolverResult() { }

    /// <summary>
    /// A successful solve: the solver converged on <see cref="FlatPricePerSqft"/>
    /// as the minimum nonnegative price (rounded up to Currency_Precision) for
    /// which the Cash_Positive_Rule holds (Requirement 15.8, 15.10).
    /// </summary>
    /// <param name="FlatPricePerSqft">Minimum Flat_Price_Per_Sqft satisfying the rule, rounded up to two decimals.</param>
    /// <param name="Forecast">The full forecast produced at <paramref name="FlatPricePerSqft"/>.</param>
    /// <param name="Iterations">Total number of solver iterations used (spanning upper-bound expansion, bisection, and post-rounding re-verification).</param>
    public sealed record Success(
        decimal FlatPricePerSqft,
        ForecastResult Forecast,
        int Iterations) : SolverResult;

    /// <summary>
    /// A failed solve: the Solver_Safety_Limit was exceeded before the solver could
    /// converge on a valid price (Requirement 15.11). The solver never throws for
    /// this condition; it always returns a <see cref="Failure"/>.
    /// </summary>
    /// <param name="Reason">Human-readable description of the failure (e.g., which loop exceeded the safety limit).</param>
    /// <param name="Iterations">Total number of solver iterations attempted before failure.</param>
    public sealed record Failure(
        string Reason,
        int Iterations) : SolverResult;
}
