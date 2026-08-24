using RehearsalForecast.Core.Constants;

namespace RehearsalForecast.Core.Forecast;

/// <summary>
/// Output of Pass 3 (Revenue). Carries the single-scalar
/// <see cref="MonthlyPricePerSqft"/> plus the 36-month <see cref="GrossRevenue"/>
/// and <see cref="GrossIncome"/> vectors consumed by later passes.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GrossRevenue"/> and <see cref="GrossIncome"/> always have exactly
/// <see cref="ForecastConstants.ForecastMonths"/> entries.
/// <see cref="GrossIncome"/> equals <see cref="GrossRevenue"/> element-wise
/// because Cost of Goods Sold is out of scope for this phase (Design Decision 6,
/// Requirement 5.3).
/// </para>
/// <para>Every field is <see cref="decimal"/> (Requirement 19.1).</para>
/// </remarks>
/// <param name="MonthlyPricePerSqft">
/// Derived rate <c>Flat_Price_Per_Sqft / 36</c> applied identically to every
/// month in <c>[1, 36]</c> (Requirement 5.1, 5.4).
/// </param>
/// <param name="GrossRevenue">
/// Per-month gross revenue: <c>Rented_Sqft[m] × MonthlyPricePerSqft</c>
/// (Requirement 5.2). Length is exactly 36.
/// </param>
/// <param name="GrossIncome">
/// Per-month gross income; equals <see cref="GrossRevenue"/> element-wise in
/// this phase (Requirement 5.3, Design Decision 6). Length is exactly 36.
/// </param>
internal sealed record RevenueResult(
    decimal MonthlyPricePerSqft,
    IReadOnlyList<decimal> GrossRevenue,
    IReadOnlyList<decimal> GrossIncome);

/// <summary>
/// Pass 3 of the forecast pipeline — Revenue (design §6.3, Requirement 5).
/// </summary>
/// <remarks>
/// Given the per-month <c>Rented_Sqft</c> vector produced by Pass 2
/// (<see cref="OccupancyResult"/>-style output) and the candidate 36-month flat
/// price per square foot, this pass derives <c>Monthly_Price_Per_Sqft</c> and the
/// per-month <c>Gross_Revenue</c> and <c>Gross_Income</c> vectors. It has no
/// dependencies on other passes and performs no I/O.
/// </remarks>
internal static class RevenueCalculator
{
    /// <summary>
    /// Computes the Pass 3 outputs from the Pass 2 rented-sqft vector and the
    /// candidate flat price per square foot.
    /// </summary>
    /// <param name="rentedSqft">
    /// Per-month rented square footage from Pass 2, indexed by <c>m-1</c>. Must
    /// contain exactly <see cref="ForecastConstants.ForecastMonths"/> entries.
    /// </param>
    /// <param name="flatPricePerSqft">
    /// Candidate 36-month flat price per square foot. Zero is permitted
    /// (Design Decision 12, Requirement 15.4).
    /// </param>
    /// <returns>
    /// A <see cref="RevenueResult"/> whose <see cref="RevenueResult.GrossRevenue"/>
    /// and <see cref="RevenueResult.GrossIncome"/> each have exactly
    /// <see cref="ForecastConstants.ForecastMonths"/> entries.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="rentedSqft"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="rentedSqft"/> does not contain exactly 36 entries.
    /// </exception>
    internal static RevenueResult Compute(
        IReadOnlyList<decimal> rentedSqft,
        decimal flatPricePerSqft)
    {
        ArgumentNullException.ThrowIfNull(rentedSqft);
        if (rentedSqft.Count != ForecastConstants.ForecastMonths)
        {
            throw new ArgumentException(
                $"Expected exactly {ForecastConstants.ForecastMonths} rented-sqft entries, got {rentedSqft.Count}.",
                nameof(rentedSqft));
        }

        // Requirement 5.1: Monthly_Price_Per_Sqft = Flat_Price_Per_Sqft / 36.
        // Requirement 5.4: the same rate applies to every month, so it is computed
        // once here and reused for every m ∈ [1, 36].
        var monthlyPricePerSqft = flatPricePerSqft / ForecastConstants.ForecastMonths;

        var grossRevenue = new decimal[ForecastConstants.ForecastMonths];
        var grossIncome = new decimal[ForecastConstants.ForecastMonths];

        for (var i = 0; i < ForecastConstants.ForecastMonths; i++)
        {
            // Requirement 5.2: Gross_Revenue[m] = Rented_Sqft[m] × Monthly_Price_Per_Sqft.
            var revenue = rentedSqft[i] * monthlyPricePerSqft;
            grossRevenue[i] = revenue;

            // Requirement 5.3 (Design Decision 6): Gross_Income[m] = Gross_Revenue[m]
            // because COGS is out of scope for this phase.
            grossIncome[i] = revenue;
        }

        return new RevenueResult(monthlyPricePerSqft, grossRevenue, grossIncome);
    }
}
