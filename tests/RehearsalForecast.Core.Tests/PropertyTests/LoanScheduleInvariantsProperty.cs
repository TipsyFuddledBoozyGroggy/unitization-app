// Property 2: Loan schedule invariants.
// Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7, 11.8,
//                          11.9, 11.10, 11.11, 11.12.
//
// Design §10 (Property 2), §15.4. For any nonnegative Loan_Proceeds,
// nonnegative Annual_Loan_Interest_Rate, and positive Loan_Term_Months,
// the 36-row schedule produced by LoanCalculator.Compute satisfies:
//
//   * Exactly 36 entries; Loan_Beginning_Balance[1] = Loan_Proceeds        (R11.4).
//   * Roll-forward: Entries[i + 1].BeginningBalance = Entries[i].EndingBalance for i in [0, 34] (R11.8).
//   * Interest[m] = BeginningBalance[m] * (annualRate / 12)                 (R11.5).
//   * Principal[m] <= BeginningBalance[m]                                   (R11.6, R11.9).
//   * Declining interest while rate > 0 and beginning balance > 0           (R11.9).
//   * Term boundaries:
//       - term < 36  => zeros past term AND EndingBalance[term] == 0        (R11.10, R11.12).
//       - term > 36  => EndingBalance[36] > 0                                (R11.11).
//   * Zero-proceeds  => every row all zeros AND MonthlyPayment = 0          (R11.1).
//   * Zero-interest  => linear amortization, Interest[m] = 0                (R11.2).
//
// FsCheck.Xunit runs the [Property] at least 100 iterations (the default).
// uint / int parameters are mapped to bounded decimal / integer values via
// PropertyTestHelpers so overflow paths in the calculator's decimal power
// helper are not exercised by pathological generated inputs.

using FsCheck.Xunit;
using RehearsalForecast.Core.Loan;
using Xunit;

namespace RehearsalForecast.Core.Tests.PropertyTests;

public class LoanScheduleInvariantsProperty
{
    /// <summary>
    /// Property 2: exhaustive per-month invariants over the 36-row loan
    /// schedule. Covers the union of Requirements 11.1 through 11.12.
    /// Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 11.6, 11.7,
    ///            11.8, 11.9, 11.10, 11.11, 11.12.
    /// </summary>
    [Property]
    public void LoanSchedule_InvariantsHoldForAllInputs(
        uint proceedsRaw,
        uint annualRateRaw,
        int loanTermRaw)
    {
        var loanProceeds = PropertyTestHelpers.LargeMoneyFromRaw(proceedsRaw);
        var annualRate = PropertyTestHelpers.RateFromRaw(annualRateRaw);
        var loanTermMonths = PropertyTestHelpers.PositiveTermFromRaw(loanTermRaw);

        var calculator = new LoanCalculator();
        var schedule = calculator.Compute(loanProceeds, annualRate, loanTermMonths);

        // ---- Structural invariants (R11.4 & schedule shape). ----

        Assert.Equal(36, schedule.Entries.Count);

        // R11.4: initial balance equals proceeds (or 0 when proceeds = 0,
        // which the zero-proceeds branch collapses to all-zero rows).
        var expectedInitial = loanProceeds == 0m ? 0m : loanProceeds;
        Assert.Equal(expectedInitial, schedule.Entries[0].BeginningBalance);

        // R11.1: zero-proceeds implies every row all zeros AND payment = 0.
        if (loanProceeds == 0m)
        {
            Assert.Equal(0m, schedule.MonthlyPayment);
            for (var i = 0; i < 36; i++)
            {
                var entry = schedule.Entries[i];
                Assert.Equal(i + 1, entry.Month);
                Assert.Equal(0m, entry.BeginningBalance);
                Assert.Equal(0m, entry.Payment);
                Assert.Equal(0m, entry.Interest);
                Assert.Equal(0m, entry.Principal);
                Assert.Equal(0m, entry.EndingBalance);
            }

            // Zero-proceeds case is fully verified above; no further checks apply.
            return;
        }

        var monthlyRate = annualRate / 12m;

        // ---- Per-row and per-transition invariants. ----

        for (var i = 0; i < 36; i++)
        {
            var entry = schedule.Entries[i];
            Assert.Equal(i + 1, entry.Month);

            // R11.6, R11.9: principal never exceeds the beginning balance.
            // This must hold on active AND all-zero rows (0 <= 0 trivially).
            Assert.True(
                entry.Principal <= entry.BeginningBalance,
                $"Principal ({entry.Principal}) must not exceed BeginningBalance "
                + $"({entry.BeginningBalance}) at month {entry.Month}.");

            // R11.5: Interest = BeginningBalance * monthlyRate. Applies
            // uniformly; in the zero-proceeds/zero-interest branches the
            // right-hand side is zero and the equality still holds.
            Assert.Equal(entry.BeginningBalance * monthlyRate, entry.Interest);

            // R11.2: zero-interest implies zero interest on every row.
            if (annualRate == 0m)
            {
                Assert.Equal(0m, entry.Interest);
            }

            // R11.8: roll-forward from previous row's ending balance for i >= 1.
            if (i >= 1)
            {
                Assert.Equal(schedule.Entries[i - 1].EndingBalance, entry.BeginningBalance);
            }

            // R11.9 declining-interest monotonicity while the previous row's
            // beginning balance was positive and the annual rate is positive.
            // Guarded by (annualRate > 0 && prev.BeginningBalance > 0) so the
            // property is well defined on all-zero and zero-interest schedules.
            if (i >= 1 && annualRate > 0m && schedule.Entries[i - 1].BeginningBalance > 0m
                && entry.BeginningBalance > 0m)
            {
                Assert.True(
                    entry.Interest <= schedule.Entries[i - 1].Interest,
                    $"Interest at month {entry.Month} ({entry.Interest}) must be <= "
                    + $"interest at month {entry.Month - 1} ({schedule.Entries[i - 1].Interest}).");
            }
        }

        // ---- Term-boundary invariants (R11.10, R11.11, R11.12). ----

        if (loanTermMonths <= 36)
        {
            // R11.12: EndingBalance at Loan_Term_Months is exactly zero.
            Assert.Equal(0m, schedule.Entries[loanTermMonths - 1].EndingBalance);

            // R11.10: rows beyond Loan_Term_Months are all-zero.
            for (var m = loanTermMonths + 1; m <= 36; m++)
            {
                var entry = schedule.Entries[m - 1];
                Assert.Equal(0m, entry.BeginningBalance);
                Assert.Equal(0m, entry.Payment);
                Assert.Equal(0m, entry.Interest);
                Assert.Equal(0m, entry.Principal);
                Assert.Equal(0m, entry.EndingBalance);
            }
        }
        else
        {
            // R11.11: term > 36 implies EndingBalance[36] > 0 (no early payoff).
            Assert.True(
                schedule.Entries[36 - 1].EndingBalance > 0m,
                $"EndingBalance at month 36 must be positive when Loan_Term_Months ({loanTermMonths}) "
                + $"exceeds 36, but was {schedule.Entries[36 - 1].EndingBalance}.");
        }
    }
}
