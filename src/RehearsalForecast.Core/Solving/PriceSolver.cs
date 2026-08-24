using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;

namespace RehearsalForecast.Core.Solving;

/// <summary>
/// Deterministic bounded binary-search solver for the minimum nonnegative
/// <c>Flat_Price_Per_Sqft</c> that satisfies the Cash_Positive_Rule
/// (design §4.3, §8; Requirement 15).
/// </summary>
/// <remarks>
/// <para>
/// The algorithm is composed of six phases, each guarded by
/// <see cref="ForecastConstants.SolverSafetyLimit"/> against runaway
/// iteration (design §8.8, Requirement 15.11):
/// </para>
/// <list type="number">
///   <item>
///     <description>
///       <b>Fast path (§8.2, R15.3, R15.4).</b> Compute at <c>p = 0</c>.
///       If the Cash_Positive_Rule already holds, return
///       <see cref="SolverResult.Success"/> with
///       <c>FlatPricePerSqft = 0</c> and <c>Iterations = 1</c>.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Geometric upper-bound expansion (§8.3, R15.5).</b> Starting at
///       <c>high = 1</c>, compute at <c>high</c>. If the rule holds, break
///       and record the last known infeasible value as <c>low</c>.
///       Otherwise, retain <c>high</c> as the new <c>low</c> and double
///       <c>high</c>, saturating at <see cref="decimal.MaxValue"/> to
///       preclude overflow (Requirement 15.11: no unhandled exceptions).
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Bisection to <see cref="ForecastConstants.SolverTolerance"/>
///       (§8.4, R15.6).</b> While <c>high − low</c> exceeds the tolerance,
///       compute at <c>mid = (low + high) / 2</c> and shrink the bracket
///       accordingly. Because the Cash_Positive_Rule is monotone
///       non-decreasing in <c>Flat_Price_Per_Sqft</c> (design §8.9),
///       <c>high</c> converges downward to the smallest tolerated value
///       that satisfies the rule.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Ceiling to <see cref="ForecastConstants.CurrencyPrecision"/>
///       (§8.5, R15.8).</b> Round <c>high</c> UP to two decimals via
///       <c>Math.Ceiling(high * 100m) / 100m</c>.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Post-rounding re-verification (§8.6–§8.7, R15.9, R27.6).</b>
///       Compute at the rounded value. Monotonicity normally makes this a
///       one-call check, but if the rounded value still fails the rule,
///       raise by <see cref="ForecastConstants.CurrencyPrecision"/> and
///       retry until the rule holds or the safety limit is breached.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Cent-level minimality walk-down (R15.10).</b> Bisection's
///       <see cref="ForecastConstants.SolverTolerance"/> (0.0001) is 100×
///       finer than <see cref="ForecastConstants.CurrencyPrecision"/>
///       (0.01), but ceil-to-cents can still overshoot by one cent when
///       the real-valued threshold sits within a tolerance of a cent
///       boundary from below: bisection returns <c>high</c> just above
///       the boundary, and ceil rounds up to the NEXT cent even though
///       the boundary cent already satisfies the rule. Walk down by
///       <see cref="ForecastConstants.CurrencyPrecision"/> while the
///       previous cent still satisfies the rule; this converts real-valued
///       bracketing into cent-level minimality per R15.10.
///     </description>
///   </item>
/// </list>
/// <para>
/// A single shared iteration counter spans all three loops. Each
/// <see cref="IForecastCalculator.Compute"/> call increments the counter,
/// so <c>Iterations</c> on the returned envelope is the total number of
/// forecast evaluations performed. On a safety-limit breach the counter
/// terminates at exactly
/// <c><see cref="ForecastConstants.SolverSafetyLimit"/> + 1</c> before the
/// solver returns <see cref="SolverResult.Failure"/> (Requirement 15.11).
/// </para>
/// <para>
/// Every intermediate value is <see cref="decimal"/> (Requirement 15.12);
/// no <see cref="double"/> or <see cref="float"/> is introduced at any
/// step. The class has no dependency on ASP.NET Core, Razor, Terraform, or
/// any UI abstraction (Requirement 15.13) — its sole collaborator is
/// <see cref="IForecastCalculator"/>.
/// </para>
/// <para>
/// The class is stateless and thread-safe; every call to
/// <see cref="Solve"/> is independent of every other call. It is
/// registered <c>Scoped</c> in <c>Program.cs</c> only for DI-lifetime
/// uniformity with the other core services (Requirement 15.2:
/// determinism).
/// </para>
/// </remarks>
public sealed class PriceSolver : ISolver
{
    private readonly IForecastCalculator _forecastCalculator;

    /// <summary>
    /// Constructs a <see cref="PriceSolver"/> that will delegate every
    /// candidate forecast to the supplied <paramref name="forecastCalculator"/>
    /// (design §4.3, Requirement 15.7).
    /// </summary>
    /// <param name="forecastCalculator">
    /// The forecast engine used to evaluate each candidate
    /// <c>Flat_Price_Per_Sqft</c>. Must be non-null; resolved by the DI
    /// container from the <c>Scoped</c> registration in <c>Program.cs</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="forecastCalculator"/> is <see langword="null"/>.
    /// </exception>
    public PriceSolver(IForecastCalculator forecastCalculator)
    {
        ArgumentNullException.ThrowIfNull(forecastCalculator);
        _forecastCalculator = forecastCalculator;
    }

    /// <inheritdoc />
    public SolverResult Solve(ForecastInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        // A single shared iteration counter spans every phase. Each
        // Compute call increments it exactly once; on a safety-limit
        // breach the counter is exactly SolverSafetyLimit + 1.
        var iterations = 0;

        // ------------------------------------------------------------------
        // Phase 1 — Fast path at p = 0 (design §8.2, R15.3, R15.4, DD12).
        //
        // If the Cash_Positive_Rule already holds at zero, the search is
        // over: 0 is the trivially minimum nonnegative price. The fast
        // path is worth its dedicated branch because the counter must
        // report exactly Iterations = 1 in this case (design §8.2).
        // ------------------------------------------------------------------
        iterations++;
        var forecastAtZero = _forecastCalculator.Compute(inputs, 0m);
        if (forecastAtZero.CashPositiveRuleSatisfied)
        {
            return new SolverResult.Success(0m, forecastAtZero, iterations);
        }

        // ------------------------------------------------------------------
        // Phase 2 — Geometric upper-bound expansion (design §8.3, R15.5).
        //
        // p = 0 is now a known infeasible value from Phase 1, so we seed
        // low = 0 rather than the pseudocode's high/2 shorthand. This
        // preserves the "low = last known infeasible" invariant even when
        // the very first high = 1m already satisfies the rule, which
        // would otherwise leave low pointing at an untested 0.5.
        //
        // Doubling gives O(log(price)) iterations to bracket the answer
        // regardless of scale. On the never-satisfies scenario the loop
        // runs until iterations exceeds SolverSafetyLimit; to prevent
        // decimal overflow after ~96 doublings we saturate high at
        // decimal.MaxValue (Requirement 15.11: no unhandled exceptions).
        // Once saturated, subsequent iterations continue to consume the
        // safety-limit budget so that the terminal Iterations count is
        // exactly SolverSafetyLimit + 1.
        // ------------------------------------------------------------------
        var low = 0m;
        var high = 1m;
        ForecastResult forecastAtHigh;
        while (true)
        {
            iterations++;
            if (iterations > ForecastConstants.SolverSafetyLimit)
            {
                return new SolverResult.Failure(
                    "Solver could not find an upper bound for Flat_Price_Per_Sqft within the safety limit.",
                    iterations);
            }

            forecastAtHigh = _forecastCalculator.Compute(inputs, high);
            if (forecastAtHigh.CashPositiveRuleSatisfied)
            {
                break;
            }

            // high was infeasible; hold it as the new lower bound and
            // double high (saturating at decimal.MaxValue to preclude
            // OverflowException on the never-satisfies path).
            //
            // NOTE ON SATURATION: try/catch is used here rather than a
            // pre-multiplication guard because <c>decimal.MaxValue / 2m</c>
            // rounds upward under bankers-rounding at decimal's 29-digit
            // precision limit — the exact half of MaxValue is not
            // representable — so a naive <c>high &lt;= decimal.MaxValue / 2m</c>
            // guard admits a boundary value whose double still overflows.
            // Catching the overflow is bounded to at most one occurrence
            // per solver call and is only reached on the never-satisfies
            // failure path, so has no measurable cost.
            low = high;
            try
            {
                high *= 2m;
            }
            catch (OverflowException)
            {
                high = decimal.MaxValue;
            }
        }

        // ------------------------------------------------------------------
        // Phase 3 — Bisection to SolverTolerance (design §8.4, R15.6).
        //
        // Loop invariant: low is a known infeasible price and high is a
        // known feasible price. Each iteration halves the bracket by
        // testing the midpoint. Monotonicity of the Cash_Positive_Rule in
        // Flat_Price_Per_Sqft (design §8.9) is what makes bisection valid.
        // The loop terminates when the bracket width shrinks to at most
        // SolverTolerance; at that point high is the smallest tolerated
        // value that satisfies the rule.
        // ------------------------------------------------------------------
        while (high - low > ForecastConstants.SolverTolerance)
        {
            iterations++;
            if (iterations > ForecastConstants.SolverSafetyLimit)
            {
                return new SolverResult.Failure(
                    "Solver bisection did not converge within the safety limit.",
                    iterations);
            }

            var mid = (low + high) / 2m;
            var midForecast = _forecastCalculator.Compute(inputs, mid);
            if (midForecast.CashPositiveRuleSatisfied)
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        // ------------------------------------------------------------------
        // Phase 4 — Round UP to CurrencyPrecision (design §8.5, R15.8).
        //
        // Currency_Precision is 0.01 USD (two decimals). Scaling by 100,
        // ceiling to an integer, and dividing back is the exact §8.5
        // recipe and preserves decimal precision throughout.
        // ------------------------------------------------------------------
        var rounded = Math.Ceiling(high * 100m) / 100m;

        // ------------------------------------------------------------------
        // Phase 5 — Post-rounding re-verification (design §8.6–§8.7,
        // R15.9, R15.10, R27.6).
        //
        // Because ceil moves the price upward, monotonicity normally
        // means the rounded value still satisfies the rule and the loop
        // runs exactly once. R15.9–R15.10 require re-verification anyway,
        // and R27.6 requires an incremental raise if the rounded value
        // fails. The safety-limit guard ensures the loop cannot run
        // indefinitely.
        // ------------------------------------------------------------------
        ForecastResult finalForecast;
        while (true)
        {
            iterations++;
            if (iterations > ForecastConstants.SolverSafetyLimit)
            {
                return new SolverResult.Failure(
                    "Solver post-rounding re-verification did not converge within the safety limit.",
                    iterations);
            }

            finalForecast = _forecastCalculator.Compute(inputs, rounded);
            if (finalForecast.CashPositiveRuleSatisfied)
            {
                break;
            }

            rounded += ForecastConstants.CurrencyPrecision;
        }

        // ------------------------------------------------------------------
        // Phase 6 — Cent-level minimality walk-down (R15.10).
        //
        // Bisection's SolverTolerance (0.0001) is 100× finer than
        // CurrencyPrecision (0.01), so tolerance slack cannot span two
        // cent boundaries — but it can span one when the real-valued
        // threshold p* sits within a tolerance of a cent boundary from
        // below (e.g., p* = 0.00995). In that case bisection legitimately
        // returns `high ∈ [p*, p* + tolerance]` above the cent boundary,
        // and ceil-to-cents rounds to the NEXT cent (0.02) even though
        // the boundary cent (0.01) already satisfies the rule. That
        // violates R15.10's cent-level minimality contract, which the
        // property test in Property 11 witnesses. Walk down by one cent
        // while the previous cent still satisfies the rule, updating the
        // returned forecast to match; monotonicity in §8.9 guarantees
        // this loop terminates immediately at the true cent-minimum.
        //
        // The p == 0 case cannot be reached here because Phase 1 returned
        // early whenever Compute(inputs, 0) satisfied the rule.
        // ------------------------------------------------------------------
        while (rounded > 0m)
        {
            iterations++;
            if (iterations > ForecastConstants.SolverSafetyLimit)
            {
                return new SolverResult.Failure(
                    "Solver cent-level minimality walk-down did not complete within the safety limit.",
                    iterations);
            }

            var candidate = rounded - ForecastConstants.CurrencyPrecision;
            var candidateForecast = _forecastCalculator.Compute(inputs, candidate);
            if (!candidateForecast.CashPositiveRuleSatisfied)
            {
                break;
            }

            rounded = candidate;
            finalForecast = candidateForecast;
        }

        return new SolverResult.Success(rounded, finalForecast, iterations);
    }
}
