using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Core.Forecast;

/// <summary>
/// Pass 4 — computes the per-month <c>Marketing_Total</c> vector from
/// <see cref="MarketingInputs"/> (design §6.4).
/// </summary>
/// <remarks>
/// <para>
/// For each month <c>m ∈ [1, 36]</c>:
/// </para>
/// <code>
/// Marketing_Total[m] = Print.At(m) + Search.At(m) + Social.At(m) + OtherMarketing.At(m)
/// </code>
/// <para>
/// The helper reads each line item through
/// <see cref="Schedules.MonthlySchedule{T}.At(int)"/> uniformly (Requirement 1.5)
/// so the calculation is agnostic to whether any individual line item was supplied
/// in constant or variable mode (Requirement 6.2).
/// </para>
/// <para>
/// The returned list is exactly <see cref="ForecastConstants.ForecastMonths"/> (36)
/// entries long, ordered from Month 1 through Month 36; index <c>m - 1</c> holds
/// the value for Month <c>m</c>.
/// </para>
/// </remarks>
internal static class MarketingCalculator
{
    /// <summary>
    /// Computes the 36-entry <c>Marketing_Total</c> vector for the supplied
    /// marketing line items (Requirement 6.3).
    /// </summary>
    /// <param name="marketing">
    /// The four marketing line items (Requirement 6.1). Must be non-null.
    /// </param>
    /// <returns>
    /// A read-only list of length <see cref="ForecastConstants.ForecastMonths"/>
    /// where element at index <c>m - 1</c> equals
    /// <c>Print.At(m) + Search.At(m) + Social.At(m) + OtherMarketing.At(m)</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="marketing"/> is <see langword="null"/>.
    /// </exception>
    internal static IReadOnlyList<decimal> Compute(MarketingInputs marketing)
    {
        ArgumentNullException.ThrowIfNull(marketing);

        var totals = new decimal[ForecastConstants.ForecastMonths];
        for (var m = 1; m <= ForecastConstants.ForecastMonths; m++)
        {
            totals[m - 1] =
                marketing.Print.At(m)
                + marketing.Search.At(m)
                + marketing.Social.At(m)
                + marketing.OtherMarketing.At(m);
        }

        return totals;
    }
}
