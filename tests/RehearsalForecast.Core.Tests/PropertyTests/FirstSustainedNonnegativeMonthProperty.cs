// Property-based test for Property 10 — First_Sustained_Nonnegative_Month semantics
// (design §10, Property 10; §15.4).
//
// Property 10 states that for any valid Ending_Cash vector produced by the
// forecast pipeline (or an equivalent hand-generated 36-entry decimal
// vector) and any Target_Cash_Positive_Month ∈ [1, 36]:
//
//   1. If FSNM is a value M ∈ [1, 36]: Ending_Cash[m] ≥ 0 for every
//      m ∈ [M, 36] AND no M' ∈ [1, M − 1] satisfies the same suffix
//      property (minimality).                                              (R14.4)
//   2. If FSNM is null ("None"): Ending_Cash[36] < 0 (equivalently, no
//      window [k, 36] is entirely nonnegative).                             (R14.5)
//   3. Cash_Positive_Rule_Satisfied ⇔ Ending_Cash[m] ≥ 0 for every
//      m ∈ [target, 36].                                                    (R14.1)
//   4. FSNM is independent of the target month; only the rule signal
//      depends on the target (R14.2 semantics).
//   5. Target = 36 collapses the rule to Ending_Cash[36] ≥ 0.               (R27.8)
//
// The test drives the pass-11 internal helper (design §6.11,
// CashPositiveRuleEvaluator) directly with a hand-generated 36-entry
// Ending_Cash vector. Taking the vector as input — rather than assembling
// a full ForecastInputs and running the whole pipeline — pins the property
// to the semantics of R14 and avoids coupling the test to unrelated passes.
//
// Bounding strategy: 36 element-wise-distinct signed decimals generated from
// a single seed using a per-month linear shift. Values span roughly
// [−10,000, +10,000] with cent precision so both signs (nonnegative and
// strictly negative) are hit across the 100-iteration run. All arithmetic
// remains in decimal per Requirement 19.1.
//
// Validates: Requirements 14.1, 14.2, 14.4, 14.5, 27.8

using System.Collections.Generic;
using FsCheck.Xunit;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Forecast;
using Xunit;

namespace RehearsalForecast.Core.Tests.PropertyTests;

public class FirstSustainedNonnegativeMonthProperty
{
    private const int Months = ForecastConstants.ForecastMonths;

    // ------------------------------------------------------------------
    // Bounded generators
    // ------------------------------------------------------------------

    private static decimal BoundSigned(int raw) =>
        (decimal)(((long)raw) % 2_000_001L) / 100m; // roughly [−20,000, +20,000]

    private static int BoundTargetMonth(int raw) =>
        (int)(Math.Abs((long)raw) % 36L) + 1; // [1, 36]

    /// <summary>
    /// Materialises a 36-entry signed <c>Ending_Cash</c> vector where each
    /// month carries a distinct-under-most-seeds value derived from
    /// <paramref name="seed"/> by a linear per-month shift. Values can be
    /// negative so that FSNM and the Cash_Positive_Rule flip across both
    /// branches of the property universe.
    /// </summary>
    private static IReadOnlyList<decimal> MakeEndingCash(int seed)
    {
        var xs = new decimal[Months];
        for (var i = 0; i < Months; i++)
        {
            xs[i] = BoundSigned(seed + (13 * i));
        }
        return xs;
    }

    // ------------------------------------------------------------------
    // Property 10 — full universal statement
    //
    // Validates: Requirements 14.1, 14.2, 14.4, 14.5, 27.8
    // ------------------------------------------------------------------

    [Property]
    public void Property_10_First_Sustained_Nonnegative_Month_Semantics(
        int seedEndingCash,
        int rawTargetMonth)
    {
        var endingCash = MakeEndingCash(seedEndingCash);
        var target = BoundTargetMonth(rawTargetMonth);

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, target);

        // ---- FSNM well-formedness (R14.4, R14.5) ----------------------

        if (result.FirstSustainedNonnegativeMonth is int fsnm)
        {
            // R14.4 range: FSNM ∈ [1, 36].
            Assert.InRange(fsnm, 1, Months);

            // R14.4 suffix property: every month in [FSNM, 36] is nonnegative.
            for (var m = fsnm; m <= Months; m++)
            {
                Assert.True(
                    endingCash[m - 1] >= 0m,
                    $"FSNM = {fsnm} but Ending_Cash[{m}] = {endingCash[m - 1]} < 0.");
            }

            // R14.4 minimality: no M' ∈ [1, FSNM − 1] satisfies the same
            // suffix property. Equivalently, when FSNM > 1, the month
            // immediately before FSNM must be strictly negative (otherwise
            // it would begin an even-earlier valid suffix).
            if (fsnm > 1)
            {
                Assert.True(
                    endingCash[fsnm - 2] < 0m,
                    $"FSNM = {fsnm} is not minimal: Ending_Cash[{fsnm - 1}] = {endingCash[fsnm - 2]} ≥ 0.");
            }
        }
        else
        {
            // R14.5: FSNM is null ⇔ no nonnegative suffix ending at Month 36
            // ⇔ Ending_Cash[36] < 0. (If Ending_Cash[36] ≥ 0, then M = 36
            // trivially begins a valid one-month suffix, contradicting null.)
            Assert.True(
                endingCash[Months - 1] < 0m,
                $"FSNM is null but Ending_Cash[36] = {endingCash[Months - 1]} ≥ 0.");
        }

        // ---- Cash_Positive_Rule ⇔ nonnegative suffix from target (R14.1) ----

        var expectedRule = true;
        for (var m = target; m <= Months; m++)
        {
            if (endingCash[m - 1] < 0m)
            {
                expectedRule = false;
                break;
            }
        }
        Assert.Equal(expectedRule, result.CashPositiveRuleSatisfied);

        // ---- FSNM independence from target (R14.2 semantics) ----------
        //
        // Target only influences the rule signal, not FSNM. Recomputing at
        // a different target month must yield the same FSNM value.
        var alternateTarget = target == Months ? 1 : target + 1;
        var alternateResult = CashPositiveRuleEvaluator.Evaluate(endingCash, alternateTarget);
        Assert.Equal(result.FirstSustainedNonnegativeMonth, alternateResult.FirstSustainedNonnegativeMonth);

        // ---- R27.8: target = 36 collapses the rule to Ending_Cash[36] ≥ 0 ----
        //
        // We drive this branch structurally by asking the evaluator at
        // target = 36 for the same Ending_Cash vector; the rule must equal
        // the sign check on the final month regardless of any earlier
        // negatives.
        var atMonth36 = CashPositiveRuleEvaluator.Evaluate(endingCash, 36);
        Assert.Equal(endingCash[Months - 1] >= 0m, atMonth36.CashPositiveRuleSatisfied);
    }
}
