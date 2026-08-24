using RehearsalForecast.Core.Constants;

namespace RehearsalForecast.Core.Forecast;

/// <summary>
/// Output of Pass 11 (Cash-Positive Rule).
/// </summary>
/// <remarks>
/// <para>
/// Groups the two related outputs described in design §6.11:
/// </para>
/// <list type="bullet">
///   <item>
///     <term><see cref="CashPositiveRuleSatisfied"/></term>
///     <description>Requirement 14.1 — <c>Ending_Cash[target] ≥ 0</c> AND
///     <c>Ending_Cash[m] ≥ 0</c> for every <c>m</c> in
///     <c>[target + 1, 36]</c>. When <c>target = 36</c> the second conjunct
///     is vacuously true and the rule collapses to <c>Ending_Cash[36] ≥ 0</c>
///     only (Requirement 27.8).</description>
///   </item>
///   <item>
///     <term><see cref="FirstSustainedNonnegativeMonth"/></term>
///     <description>Requirement 14.4 — the smallest integer <c>M</c> in
///     <c>[1, 36]</c> such that <c>Ending_Cash[m] ≥ 0</c> for every
///     <c>m</c> in <c>[M, 36]</c>. <c>null</c> encodes the "None" case
///     from Requirement 14.5 / Design Decision 9, which occurs iff
///     <c>Ending_Cash[36] &lt; 0</c>.</description>
///   </item>
/// </list>
/// </remarks>
/// <param name="CashPositiveRuleSatisfied">
/// Whether the Cash_Positive_Rule holds for the supplied
/// <c>Target_Cash_Positive_Month</c> (Requirement 14.1).
/// </param>
/// <param name="FirstSustainedNonnegativeMonth">
/// The smallest <c>M</c> in <c>[1, 36]</c> beginning a suffix of nonnegative
/// months, or <c>null</c> when no such <c>M</c> exists (Requirement 14.4,
/// 14.5).
/// </param>
internal sealed record CashPositiveRuleResult(
    bool CashPositiveRuleSatisfied,
    int? FirstSustainedNonnegativeMonth);

/// <summary>
/// Pass 11 of the forecast pipeline — Cash-Positive Rule (design §6.11,
/// Requirement 14).
/// </summary>
/// <remarks>
/// <para>
/// Consumes the <c>Ending_Cash</c> vector produced by Pass 10 (§6.10) and the
/// user-supplied <c>Target_Cash_Positive_Month</c> and produces the two
/// derived signals described by Requirement 14: whether the
/// Cash_Positive_Rule is satisfied at the target, and the first month that
/// begins a sustained-nonnegative run through Month 36.
/// </para>
/// <para>
/// Both signals are computed in a single walk over the vector so the two
/// outputs are guaranteed to agree on the same array bounds and the same
/// definition of "nonnegative" (<c>≥ 0</c>, per Requirements 14.1 and 14.4).
/// </para>
/// </remarks>
internal static class CashPositiveRuleEvaluator
{
    /// <summary>
    /// Evaluates the Cash_Positive_Rule and computes
    /// First_Sustained_Nonnegative_Month.
    /// </summary>
    /// <param name="endingCash">
    /// The 36-entry <c>Ending_Cash</c> vector from Pass 10, indexed
    /// zero-based (<c>Ending_Cash[m]</c> is at <c>endingCash[m - 1]</c>).
    /// Must contain exactly <see cref="ForecastConstants.ForecastMonths"/>
    /// entries.
    /// </param>
    /// <param name="targetCashPositiveMonth">
    /// The user-supplied Target_Cash_Positive_Month, an integer in
    /// <c>[1, 36]</c> (Requirement 1.7, Requirement 2.8).
    /// </param>
    /// <returns>
    /// A <see cref="CashPositiveRuleResult"/> carrying both Pass 11 outputs.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="endingCash"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="endingCash"/> does not contain exactly 36 entries, or
    /// <paramref name="targetCashPositiveMonth"/> is not in <c>[1, 36]</c>.
    /// </exception>
    internal static CashPositiveRuleResult Evaluate(
        IReadOnlyList<decimal> endingCash,
        int targetCashPositiveMonth)
    {
        ArgumentNullException.ThrowIfNull(endingCash);

        if (endingCash.Count != ForecastConstants.ForecastMonths)
        {
            throw new ArgumentException(
                $"Expected exactly {ForecastConstants.ForecastMonths} Ending_Cash entries, got {endingCash.Count}.",
                nameof(endingCash));
        }

        if (targetCashPositiveMonth < 1 || targetCashPositiveMonth > ForecastConstants.ForecastMonths)
        {
            throw new ArgumentException(
                $"Target_Cash_Positive_Month must be in [1, {ForecastConstants.ForecastMonths}], got {targetCashPositiveMonth}.",
                nameof(targetCashPositiveMonth));
        }

        // ---- First_Sustained_Nonnegative_Month (Requirement 14.4, 14.5) ----
        //
        // Walk M from 36 downward while Ending_Cash[M] >= 0. The smallest
        // such M is FSNM; if Ending_Cash[36] < 0 no suffix qualifies and we
        // emit null ("None", per Design Decision 9).
        //
        // Because we walk contiguously from the end, the loop invariant is
        // "every month strictly greater than the current M has already been
        // verified nonnegative" — so the smallest M we reach without
        // breaking the run is exactly the start of the longest nonnegative
        // suffix.
        int? firstSustainedNonnegativeMonth = null;
        for (var m = ForecastConstants.ForecastMonths; m >= 1; m--)
        {
            if (endingCash[m - 1] < 0m)
            {
                break;
            }
            firstSustainedNonnegativeMonth = m;
        }

        // ---- Cash_Positive_Rule_Satisfied (Requirement 14.1, 14.2, 27.8) ----
        //
        // Equivalent to: every month in [target, 36] has Ending_Cash >= 0.
        // Months strictly earlier than target are unconstrained (14.2), so
        // they do not participate in the scan. When target = 36 the loop
        // reduces to a single check on Ending_Cash[36] (27.8).
        //
        // Equivalent phrasing using FSNM: the rule is satisfied iff
        // firstSustainedNonnegativeMonth is not null AND is <= target.
        // Using the direct scan below keeps the two computations
        // independent for clarity.
        var cashPositiveRuleSatisfied = true;
        for (var m = targetCashPositiveMonth; m <= ForecastConstants.ForecastMonths; m++)
        {
            if (endingCash[m - 1] < 0m)
            {
                cashPositiveRuleSatisfied = false;
                break;
            }
        }

        return new CashPositiveRuleResult(
            cashPositiveRuleSatisfied,
            firstSustainedNonnegativeMonth);
    }
}
