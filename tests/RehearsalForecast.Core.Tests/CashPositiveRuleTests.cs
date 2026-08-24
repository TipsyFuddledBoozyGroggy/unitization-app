// Tests for the Cash_Positive_Rule evaluator and First_Sustained_Nonnegative_Month
// (design §6.11, §15.3 → CashPositiveRuleTests).
//
// These tests are written tests-first against the intended internal API that
// task 35 (Pass 11) will introduce. Design §6.11 documents the two outputs
// (Cash_Positive_Rule_Satisfied, First_Sustained_Nonnegative_Month) but does
// not spell out the C# helper's exact name or signature, so this file assumes
// the following surface (per task 34's guidance):
//
//     namespace RehearsalForecast.Core.Forecast;
//
//     internal static class CashPositiveRuleEvaluator
//     {
//         internal static CashPositiveRuleResult Evaluate(
//             IReadOnlyList<decimal> endingCash,   // exactly 36 entries, 1-based via [m - 1]
//             int targetCashPositiveMonth);        // in [1, 36]
//     }
//
//     internal sealed record CashPositiveRuleResult(
//         bool CashPositiveRuleSatisfied,
//         int? FirstSustainedNonnegativeMonth);    // null encodes the "None" case
//
// Rationale for the assumption:
//   * §6.11 defines the two outputs as a single pass over Ending_Cash; grouping
//     them into one call keeps the two related answers coupled by construction
//     (they share the same suffix scan and the same array bounds).
//   * `Ending_Cash` is the only calculation-relevant state from Pass 10, so
//     accepting it as an `IReadOnlyList<decimal>` matches the record shape used
//     by earlier passes (e.g. `CapitalResult.CapitalExpendituresInMonth`).
//   * `Target_Cash_Positive_Month` is a scalar drawn from ForecastControlInputs
//     (Requirement 1.7); passing it as a bare `int` mirrors how prior passes
//     accept scalar controls (`ownerInvestment` in CapitalCalculator).
//   * `FirstSustainedNonnegativeMonth` is `int?` because Requirement 14.5 /
//     Design Decision 9 encode the "None" case as null (matches
//     ForecastResult.FirstSustainedNonnegativeMonth in §5.5).
//   * The helper is `internal` so it is not part of the Web API surface;
//     `InternalsVisibleTo` on the Core csproj exposes it to this test project.
//
// If task 35 chooses a different helper name or shape, only the `Evaluate(...)`
// call sites and the `CashPositiveRuleResult` field-name accessors need to
// change; the specification the assertions encode does not.
//
// Validates:
//   * Requirement 14.1 — Cash_Positive_Rule = Ending_Cash[target] ≥ 0 AND
//                        Ending_Cash[m] ≥ 0 for every m in [target + 1, 36].
//   * Requirement 14.2 — Months strictly earlier than target are unconstrained
//                        by the rule.
//   * Requirement 14.4 — First_Sustained_Nonnegative_Month is the smallest
//                        M in [1, 36] such that Ending_Cash[m] ≥ 0 for every
//                        m in [M, 36].
//   * Requirement 14.5 — When no such M exists, First_Sustained_Nonnegative_Month
//                        is emitted as "None" (encoded here as null).
//   * Requirement 22.2 — Test names identify the business rule under test.
//   * Requirement 27.8 — Target_Cash_Positive_Month = 36 collapses the rule to
//                        Ending_Cash[36] ≥ 0 only.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Forecast;
using Xunit;

namespace RehearsalForecast.Core.Tests;

public class CashPositiveRuleTests
{
    private const int Months = ForecastConstants.ForecastMonths;

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Builds a 36-entry Ending_Cash vector filled with <paramref name="fill"/>,
    /// then overrides individual 1-based months via <paramref name="overrides"/>.
    /// Using 1-based month keys keeps the test bodies aligned with the
    /// specification's month numbering.
    /// </summary>
    private static IReadOnlyList<decimal> EndingCash(
        decimal fill,
        params (int Month, decimal Value)[] overrides)
    {
        var xs = new decimal[Months];
        for (var i = 0; i < Months; i++)
        {
            xs[i] = fill;
        }
        foreach (var (month, value) in overrides)
        {
            xs[month - 1] = value;
        }
        return xs;
    }

    // ===============================================================
    // R14.1: Cash_Positive_Rule =
    //   Ending_Cash[target] ≥ 0
    //   AND Ending_Cash[m] ≥ 0 for every m in [target + 1, 36].
    // ===============================================================

    [Fact]
    public void CashPositiveRuleEvaluator_RuleSatisfied_WhenTargetAndAllLaterMonthsAreNonnegative()
    {
        // Every month from target = 12 onward is strictly positive; the rule
        // must hold regardless of pre-target values.
        var endingCash = EndingCash(fill: 1_000m);

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 12);

        Assert.True(result.CashPositiveRuleSatisfied);
    }

    [Fact]
    public void CashPositiveRuleEvaluator_RuleNotSatisfied_WhenEndingCashAtTargetIsNegative()
    {
        // Every month is +100 except target month itself (= -1). The rule
        // fails on the target month by the first conjunct of R14.1.
        var endingCash = EndingCash(fill: 100m, (Month: 12, Value: -1m));

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 12);

        Assert.False(result.CashPositiveRuleSatisfied);
    }

    [Fact]
    public void CashPositiveRuleEvaluator_RuleNotSatisfied_WhenAnyMonthAfterTargetIsNegative()
    {
        // Every month is +100 except a single dip at month 20. With target = 12,
        // month 20 ∈ [13, 36] so R14.1's second conjunct fails.
        var endingCash = EndingCash(fill: 100m, (Month: 20, Value: -0.01m));

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 12);

        Assert.False(result.CashPositiveRuleSatisfied);
    }

    [Fact]
    public void CashPositiveRuleEvaluator_RuleNotSatisfied_WhenEndingCashAtMonth36IsNegative()
    {
        // Sanity check: the last month is always in [target + 1, 36] when
        // target < 36, so a negative Month-36 value must always break the rule.
        var endingCash = EndingCash(fill: 100m, (Month: 36, Value: -1m));

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 12);

        Assert.False(result.CashPositiveRuleSatisfied);
    }

    [Fact]
    public void CashPositiveRuleEvaluator_RuleSatisfied_WhenTargetMonthIsExactlyZero()
    {
        // Boundary: the rule is "≥ 0", not "> 0". A target-month value of
        // exactly 0 satisfies the rule provided the rest of the suffix does.
        var endingCash = EndingCash(fill: 500m, (Month: 12, Value: 0m));

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 12);

        Assert.True(result.CashPositiveRuleSatisfied);
    }

    [Fact]
    public void CashPositiveRuleEvaluator_RuleSatisfied_WhenAllSuffixMonthsAreExactlyZero()
    {
        // Boundary: every month in [target, 36] is exactly 0. The rule uses
        // "≥ 0" (R14.1), so this must satisfy — Ending_Cash[m] = 0 is a
        // valid cash-positive month, not a violation.
        var endingCash = EndingCash(fill: 0m);

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 1);

        Assert.True(result.CashPositiveRuleSatisfied);
    }

    [Fact]
    public void CashPositiveRuleEvaluator_RuleSatisfied_WhenTargetIsOneAndAllMonthsNonnegative()
    {
        // target = 1 makes the rule cover the entire 36-month window.
        var endingCash = EndingCash(fill: 1m);

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 1);

        Assert.True(result.CashPositiveRuleSatisfied);
    }

    [Fact]
    public void CashPositiveRuleEvaluator_RuleNotSatisfied_WhenTargetIsOneAndAnyMonthIsNegative()
    {
        // target = 1: even a single negative month (here, month 5) breaks the
        // rule because every month in [1, 36] is part of the suffix.
        var endingCash = EndingCash(fill: 1m, (Month: 5, Value: -0.01m));

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 1);

        Assert.False(result.CashPositiveRuleSatisfied);
    }

    // ===============================================================
    // R14.2: Months strictly earlier than target are unconstrained.
    // ===============================================================

    [Fact]
    public void CashPositiveRuleEvaluator_RuleSatisfied_WhenMonthsBeforeTargetAreVeryNegative()
    {
        // Months 1..11 are wildly negative; the suffix [12, 36] is uniformly
        // positive. R14.2 says pre-target months must not affect the outcome.
        var endingCash = EndingCash(fill: 500m);
        var mutable = endingCash.ToArray();
        for (var m = 1; m <= 11; m++)
        {
            mutable[m - 1] = -1_000_000m;
        }
        var endingCashWithNegativePrefix = (IReadOnlyList<decimal>)mutable;

        var result = CashPositiveRuleEvaluator.Evaluate(
            endingCashWithNegativePrefix,
            targetCashPositiveMonth: 12);

        Assert.True(result.CashPositiveRuleSatisfied);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(18)]
    [InlineData(35)]
    public void CashPositiveRuleEvaluator_RuleIgnoresMonthsStrictlyBeforeTarget(int target)
    {
        // For each target, mark every pre-target month as strongly negative and
        // every month in [target, 36] as strongly positive. The rule must hold.
        var xs = new decimal[Months];
        for (var m = 1; m <= Months; m++)
        {
            xs[m - 1] = m < target ? -9_999m : 9_999m;
        }

        var result = CashPositiveRuleEvaluator.Evaluate(xs, targetCashPositiveMonth: target);

        Assert.True(result.CashPositiveRuleSatisfied);
    }

    // ===============================================================
    // R27.8: Target_Cash_Positive_Month = 36 ⇒ rule collapses to
    //        Ending_Cash[36] ≥ 0 only.
    // ===============================================================

    [Fact]
    public void CashPositiveRuleEvaluator_TargetThirtySix_RuleSatisfied_WhenOnlyMonth36IsNonnegative()
    {
        // R27.8: with target = 36 the interval [target + 1, 36] is empty, so
        // the rule reduces to Ending_Cash[36] ≥ 0. All prior months negative,
        // month 36 exactly 0.
        var endingCash = EndingCash(fill: -1_000m, (Month: 36, Value: 0m));

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 36);

        Assert.True(result.CashPositiveRuleSatisfied);
    }

    [Fact]
    public void CashPositiveRuleEvaluator_TargetThirtySix_RuleNotSatisfied_WhenMonth36IsNegative()
    {
        // R27.8 continued: month 36 is the only month that matters. A
        // negative Ending_Cash[36] fails the rule outright, no matter what
        // the earlier months look like.
        var endingCash = EndingCash(fill: 10_000m, (Month: 36, Value: -0.01m));

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 36);

        Assert.False(result.CashPositiveRuleSatisfied);
    }

    [Fact]
    public void CashPositiveRuleEvaluator_TargetThirtySix_RuleSatisfied_WhenMonth36IsPositive()
    {
        // Positive month 36, deeply negative earlier months. Rule holds.
        var endingCash = EndingCash(fill: -500_000m, (Month: 36, Value: 1m));

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 36);

        Assert.True(result.CashPositiveRuleSatisfied);
    }

    // ===============================================================
    // R14.4: First_Sustained_Nonnegative_Month is the smallest
    //        M in [1, 36] such that Ending_Cash[m] ≥ 0 for every
    //        m in [M, 36].
    // ===============================================================

    [Fact]
    public void FirstSustainedNonnegativeMonth_IsOne_WhenEveryMonthIsNonnegative()
    {
        // The whole 36-month window is one sustained-nonnegative run.
        var endingCash = EndingCash(fill: 100m);

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 12);

        Assert.Equal(1, result.FirstSustainedNonnegativeMonth);
    }

    [Fact]
    public void FirstSustainedNonnegativeMonth_IsTwo_WhenOnlyMonthOneIsNegative()
    {
        // The sustained-nonnegative run [2, 36]; the shorter run [1, 36] is
        // broken by the negative Ending_Cash[1].
        var endingCash = EndingCash(fill: 100m, (Month: 1, Value: -1m));

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 12);

        Assert.Equal(2, result.FirstSustainedNonnegativeMonth);
    }

    [Fact]
    public void FirstSustainedNonnegativeMonth_IsMonthAfterLastNegative()
    {
        // Negatives in months 1..10; nonnegative in [11, 36]. The smallest
        // M whose suffix is entirely ≥ 0 is 11 (R14.4).
        var xs = new decimal[Months];
        for (var m = 1; m <= 10; m++)
        {
            xs[m - 1] = -100m;
        }
        for (var m = 11; m <= Months; m++)
        {
            xs[m - 1] = 100m;
        }

        var result = CashPositiveRuleEvaluator.Evaluate(xs, targetCashPositiveMonth: 12);

        Assert.Equal(11, result.FirstSustainedNonnegativeMonth);
    }

    [Fact]
    public void FirstSustainedNonnegativeMonth_IsThirtySix_WhenOnlyMonth36IsNonnegative()
    {
        // The maximal FSNM value: only the last month begins a valid suffix.
        var endingCash = EndingCash(fill: -1m, (Month: 36, Value: 0m));

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 36);

        Assert.Equal(36, result.FirstSustainedNonnegativeMonth);
    }

    [Fact]
    public void FirstSustainedNonnegativeMonth_TreatsExactlyZeroAsNonnegative()
    {
        // R14.4 uses "≥ 0". A month whose Ending_Cash is exactly 0 does not
        // break the suffix; it participates in it.
        var xs = new decimal[Months];
        for (var m = 1; m <= 4; m++)
        {
            xs[m - 1] = -50m;
        }
        for (var m = 5; m <= Months; m++)
        {
            xs[m - 1] = 0m; // sustained at exactly zero
        }

        var result = CashPositiveRuleEvaluator.Evaluate(xs, targetCashPositiveMonth: 6);

        Assert.Equal(5, result.FirstSustainedNonnegativeMonth);
        Assert.True(result.CashPositiveRuleSatisfied);
    }

    [Fact]
    public void FirstSustainedNonnegativeMonth_ChoosesMinimalM_WhenEarlierIntermittentNonnegativesExist()
    {
        // Months 1, 3, 5, and 7 are nonnegative but they do NOT begin a
        // sustained run — a later month (here, month 2, 4, 6, 8) is negative.
        // The true sustained run begins at month 9.
        var xs = new decimal[Months];
        for (var m = 1; m <= 8; m++)
        {
            xs[m - 1] = m % 2 == 1 ? 5m : -5m;
        }
        for (var m = 9; m <= Months; m++)
        {
            xs[m - 1] = 5m;
        }

        var result = CashPositiveRuleEvaluator.Evaluate(xs, targetCashPositiveMonth: 12);

        Assert.Equal(9, result.FirstSustainedNonnegativeMonth);
    }

    [Fact]
    public void FirstSustainedNonnegativeMonth_IsIndependentOfTargetCashPositiveMonth()
    {
        // R14.4 does not reference target; FSNM must be computed purely from
        // Ending_Cash. Two different targets must yield the same FSNM.
        var endingCash = EndingCash(fill: 100m, (Month: 1, Value: -1m), (Month: 2, Value: -1m));

        var a = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 3);
        var b = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 30);

        Assert.Equal(3, a.FirstSustainedNonnegativeMonth);
        Assert.Equal(3, b.FirstSustainedNonnegativeMonth);
    }

    // ===============================================================
    // R14.5: FSNM = "None" (null) when no such M exists — which by
    //        R14.4's definition means Ending_Cash[36] < 0.
    // ===============================================================

    [Fact]
    public void FirstSustainedNonnegativeMonth_IsNull_WhenEndingCashAtMonth36IsNegative()
    {
        // A negative final month makes every candidate suffix M ≤ 36 fail
        // its final-month check, so no M in [1, 36] qualifies (R14.5).
        var endingCash = EndingCash(fill: 100m, (Month: 36, Value: -0.01m));

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 12);

        Assert.Null(result.FirstSustainedNonnegativeMonth);
    }

    [Fact]
    public void FirstSustainedNonnegativeMonth_IsNull_WhenEveryMonthIsNegative()
    {
        // A wholly negative forecast: FSNM is "None" (R14.5).
        var endingCash = EndingCash(fill: -1m);

        var result = CashPositiveRuleEvaluator.Evaluate(endingCash, targetCashPositiveMonth: 12);

        Assert.Null(result.FirstSustainedNonnegativeMonth);
        Assert.False(result.CashPositiveRuleSatisfied);
    }

    // ===============================================================
    // Structural guards on the result record itself.
    // ===============================================================

    [Fact]
    public void CashPositiveRuleResult_ExposesExactlyTheTwoSpecifiedOutputs()
    {
        // Design §6.11 defines exactly two outputs from Pass 11. Any drift on
        // this record risks silently expanding the pass's contract, so we
        // pin the record's public property set here.
        var propertyNames = typeof(CashPositiveRuleResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(
            new[] { "CashPositiveRuleSatisfied", "FirstSustainedNonnegativeMonth" },
            propertyNames);
    }
}
