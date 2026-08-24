using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Core.Forecast;

/// <summary>
/// Contract for the top-level 36-month rehearsal-forecast engine
/// (design §4.1, §6.12, Requirement 16.5).
/// </summary>
/// <remarks>
/// <para>
/// The interface exists so that <c>PriceSolver</c> (design §8) and the web
/// layer's <c>Calculate</c> action (design §11.1) can call the calculator
/// through a stable seam and be substituted for testing (design §2, §4).
/// The candidate <c>Flat_Price_Per_Sqft</c> is intentionally a parameter of
/// <see cref="Compute"/> rather than a member of <see cref="ForecastInputs"/>
/// because it is the solver's iteration variable, not a user input
/// (design §4.1 API contract, Requirement 15.1).
/// </para>
/// <para>
/// Every implementation must guarantee the invariants encoded in
/// <see cref="ForecastResult"/>: exactly 36 <see cref="MonthlyForecastRow"/>
/// entries ordered by <see cref="MonthlyForecastRow.Month"/> ascending, and
/// summary metrics consistent with the values embedded in the rows
/// (Requirement 16.4, 16.5). All monetary arithmetic must be performed on
/// <see cref="decimal"/> (Requirement 19.1); no <see cref="double"/> or
/// <see cref="float"/> is permitted anywhere in the pipeline.
/// </para>
/// </remarks>
public interface IForecastCalculator
{
    /// <summary>
    /// Produces a complete 36-month forecast for the given inputs and the
    /// supplied candidate <c>Flat_Price_Per_Sqft</c>.
    /// </summary>
    /// <param name="inputs">
    /// The fully-validated aggregate of every user-supplied input required by
    /// the calculator (Requirement 17). Must be non-null.
    /// </param>
    /// <param name="flatPricePerSqft">
    /// The candidate 36-month flat price per square foot; the solver iterates
    /// this value, so callers who already know the final answer pass it once.
    /// Zero is a legal value (Design Decision 12, Requirement 15.4).
    /// </param>
    /// <returns>
    /// A <see cref="ForecastResult"/> whose
    /// <see cref="ForecastResult.Rows"/> collection contains exactly
    /// <see cref="Constants.ForecastConstants.ForecastMonths"/> (36) entries.
    /// </returns>
    ForecastResult Compute(ForecastInputs inputs, decimal flatPricePerSqft);
}
