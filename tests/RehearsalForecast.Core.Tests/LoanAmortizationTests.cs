// LoanAmortizationTests — Loan_Calculator amortization tests
// (design §7.1, §7.2, §7.3, §7.4, §7.5, §15.3 → LoanAmortizationTests).
//
// This file is co-authored by two tasks:
//   * Task 25 adds `LoanAmortizationZeroTests`      — zero-proceeds and zero-interest cases (R11.1, R11.2).
//   * Task 26 adds `LoanAmortizationPositiveTests`  — positive-interest amortization,
//                                                     term boundaries, and final-payment
//                                                     residual (R11.3–R11.12).
// Both classes live in this single file so the design's §15.3 mapping
// "LoanAmortizationTests" stays 1:1 with a single source file. The two
// classes use distinct names to coexist regardless of which task runs first
// (a `fs_append` from the second task is sufficient to add its class).
//
// This file (from task 26) contains only `LoanAmortizationPositiveTests`.
// Task 25 will append `LoanAmortizationZeroTests`.
//
// Validates:
//   * Requirement 11.3  — Positive-interest fixed-payment amortization formula.
//   * Requirement 11.4  — Loan_Beginning_Balance[1] = Loan_Proceeds.
//   * Requirement 11.5  — Monthly_Loan_Interest[m] = Loan_Beginning_Balance[m] × (annualRate / 12).
//   * Requirement 11.6  — Monthly_Loan_Principal[m] = Min(Payment − Interest[m], Balance[m])
//                         (never overshoots the beginning balance).
//   * Requirement 11.7  — Loan_Ending_Balance[m] = Max(BeginningBalance[m] − Principal[m], 0).
//   * Requirement 11.8  — Loan_Beginning_Balance[m+1] = Loan_Ending_Balance[m].
//   * Requirement 11.9  — Declining Monthly_Loan_Interest while balance > 0 and rate > 0.
//   * Requirement 11.10 — Term < 36: rows past Loan_Term_Months are all zeros.
//   * Requirement 11.11 — Term > 36: Loan_Ending_Balance[36] > 0, no forced early payoff.
//   * Requirement 11.12 — Final-month rounding residual is absorbed so
//                         Loan_Ending_Balance[Loan_Term_Months] = 0 when term ≤ 36.
//   * Requirement 22.2  — Test names identify the business rule under test.
//
// Public API surface tested (design §4.2):
//     namespace RehearsalForecast.Core.Loan;
//     public interface ILoanCalculator
//     {
//         LoanSchedule Compute(decimal loanProceeds,
//                              decimal annualInterestRate,
//                              int loanTermMonths);
//     }
//     public sealed class LoanCalculator : ILoanCalculator { }
//
// The tests instantiate `LoanCalculator` directly (it is stateless; no DI
// wiring is required to exercise it). The concrete class is defined by
// tasks 27–29.

using System.Collections.Generic;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Loan;
using Xunit;

namespace RehearsalForecast.Core.Tests;

public class LoanAmortizationPositiveTests
{
    private const int ForecastMonths = ForecastConstants.ForecastMonths;

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Integer-exponent power for <see cref="decimal"/> values, computed as a
    /// loop of decimal multiplications (no <c>double</c>/<c>float</c>
    /// conversion). Mirrors the technique described in design §7.3 for
    /// evaluating <c>(1 + i)^n</c> in the payment formula.
    /// </summary>
    private static decimal DecimalPow(decimal x, int n)
    {
        var result = 1m;
        for (var k = 0; k < n; k++)
        {
            result *= x;
        }
        return result;
    }

    /// <summary>
    /// Analytically expected fixed monthly payment per the standard
    /// fully-amortizing formula in design §7.3:
    ///     Payment = Loan_Proceeds · i · (1 + i)^n / ((1 + i)^n − 1)
    /// where i = annualRate / 12 and n = termMonths. Computed in the same
    /// order as the implementation so decimal round-tripping is identical.
    /// </summary>
    private static decimal ExpectedMonthlyPayment(
        decimal loanProceeds,
        decimal annualRate,
        int termMonths)
    {
        var monthlyRate = annualRate / 12m;
        var factor = DecimalPow(1m + monthlyRate, termMonths);
        return loanProceeds * monthlyRate * factor / (factor - 1m);
    }

    private static LoanSchedule Compute(
        decimal loanProceeds,
        decimal annualRate,
        int termMonths)
    {
        var calculator = new LoanCalculator();
        return calculator.Compute(loanProceeds, annualRate, termMonths);
    }

    public static IEnumerable<object[]> PositiveInterestLoanConfigurations()
    {
        // (loanProceeds, annualInterestRate, termMonths)
        yield return new object[] { 100_000m, 0.06m, 36 };
        yield return new object[] { 50_000m, 0.045m, 24 };
        yield return new object[] { 250_000m, 0.075m, 60 };
        yield return new object[] { 10_000m, 0.10m, 12 };
        yield return new object[] { 500_000m, 0.055m, 120 };
    }

    public static IEnumerable<object[]> TermEqualsForecastMonthsConfigurations()
    {
        // Loans whose term coincides with the 36-month forecast window
        // (Loan_Term_Months = 36); the final-month residual absorption
        // must drive Loan_Ending_Balance[36] to exactly zero (R11.12).
        yield return new object[] { 100_000m, 0.06m };
        yield return new object[] { 75_000m, 0.055m };
        yield return new object[] { 250_000m, 0.0725m };
    }

    // ------------------------------------------------------------------
    // R11.3: Positive-interest fixed-payment amortization formula.
    // ------------------------------------------------------------------

    [Fact]
    public void MonthlyLoanPayment_Matches_StandardFixedPaymentAmortizationFormula_ForPositiveInterest()
    {
        // A round, familiar loan configuration keeps the assertion easy to
        // read against a textbook amortization table: $100,000 @ 6% APR
        // amortized over 36 months.
        const decimal loanProceeds = 100_000m;
        const decimal annualRate = 0.06m;
        const int termMonths = 36;

        var schedule = Compute(loanProceeds, annualRate, termMonths);

        var expectedPayment = ExpectedMonthlyPayment(loanProceeds, annualRate, termMonths);
        Assert.Equal(expectedPayment, schedule.MonthlyPayment);
    }

    [Theory]
    [MemberData(nameof(PositiveInterestLoanConfigurations))]
    public void MonthlyLoanPayment_Matches_StandardFormula_AcrossVariedLoanConfigurations(
        decimal loanProceeds, decimal annualRate, int termMonths)
    {
        // The formula must hold uniformly across a range of realistic
        // (proceeds, rate, term) tuples — including terms shorter than,
        // equal to, and longer than the 36-month forecast window.
        var schedule = Compute(loanProceeds, annualRate, termMonths);

        var expectedPayment = ExpectedMonthlyPayment(loanProceeds, annualRate, termMonths);
        Assert.Equal(expectedPayment, schedule.MonthlyPayment);
    }

    // ------------------------------------------------------------------
    // R11.4: Loan_Beginning_Balance[1] = Loan_Proceeds.
    // ------------------------------------------------------------------

    [Fact]
    public void LoanBeginningBalance_Month1_Equals_LoanProceeds_ForPositiveInterestLoan()
    {
        const decimal loanProceeds = 200_000m;
        const decimal annualRate = 0.05m;
        const int termMonths = 60;

        var schedule = Compute(loanProceeds, annualRate, termMonths);

        Assert.Equal(loanProceeds, schedule.Entries[0].BeginningBalance);
    }

    // ------------------------------------------------------------------
    // R11.5: Monthly_Loan_Interest[m] = Loan_Beginning_Balance[m] × (annualRate / 12).
    // R11.8: Loan_Beginning_Balance[m+1] = Loan_Ending_Balance[m].
    // ------------------------------------------------------------------

    [Fact]
    public void MonthlyLoanInterest_Equals_BeginningBalance_Times_MonthlyRate_ForEveryActiveMonth()
    {
        const decimal loanProceeds = 100_000m;
        const decimal annualRate = 0.06m;
        const int termMonths = 36;
        var monthlyRate = annualRate / 12m;

        var schedule = Compute(loanProceeds, annualRate, termMonths);

        // Iterate over the active-amortization months (rows 1..termMonths).
        // Rows past termMonths are all-zero (covered separately by the
        // "term < 36" tests) and would trivially satisfy this identity, so
        // this test focuses on the active window.
        for (var m = 1; m <= termMonths; m++)
        {
            var entry = schedule.Entries[m - 1];
            Assert.Equal(entry.BeginningBalance * monthlyRate, entry.Interest);
        }
    }

    [Fact]
    public void LoanBeginningBalance_Rolls_Forward_From_PreviousEndingBalance()
    {
        const decimal loanProceeds = 100_000m;
        const decimal annualRate = 0.06m;
        const int termMonths = 36;

        var schedule = Compute(loanProceeds, annualRate, termMonths);

        // R11.8: for every m ∈ [1, 35], BeginningBalance[m+1] = EndingBalance[m].
        // Indexing here is 0-based, so we compare Entries[i].EndingBalance
        // against Entries[i + 1].BeginningBalance across the full 36-month
        // schedule (this also validates the roll-forward through all-zero
        // rows past payoff if any exist).
        for (var i = 0; i < ForecastMonths - 1; i++)
        {
            Assert.Equal(schedule.Entries[i].EndingBalance, schedule.Entries[i + 1].BeginningBalance);
        }
    }

    // ------------------------------------------------------------------
    // R11.6: Monthly_Loan_Principal[m] = Min(Payment − Interest, Balance);
    //        principal never overshoots the beginning balance.
    // R11.7: Loan_Ending_Balance[m] = Max(BeginningBalance − Principal, 0).
    // ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(PositiveInterestLoanConfigurations))]
    public void MonthlyLoanPrincipal_NeverExceeds_LoanBeginningBalance(
        decimal loanProceeds, decimal annualRate, int termMonths)
    {
        var schedule = Compute(loanProceeds, annualRate, termMonths);

        // R11.6 clamps Principal at the outstanding balance so the loan can
        // never be over-amortized in the final month. This must hold for
        // every m ∈ [1, 36], including rows past payoff where both values
        // are zero (0 ≤ 0 is trivially satisfied).
        for (var i = 0; i < ForecastMonths; i++)
        {
            var entry = schedule.Entries[i];
            Assert.True(
                entry.Principal <= entry.BeginningBalance,
                $"Principal ({entry.Principal}) must not exceed BeginningBalance " +
                $"({entry.BeginningBalance}) at month {entry.Month}.");
        }
    }

    [Theory]
    [MemberData(nameof(PositiveInterestLoanConfigurations))]
    public void LoanEndingBalance_Equals_BeginningBalance_Minus_Principal_FlooredAtZero(
        decimal loanProceeds, decimal annualRate, int termMonths)
    {
        var schedule = Compute(loanProceeds, annualRate, termMonths);

        // R11.7: EndingBalance = Max(BeginningBalance − Principal, 0). Given
        // the R11.6 clamp on Principal (tested above), the Max floor is not
        // strictly necessary — the difference is already non-negative — but
        // the identity itself must hold exactly for every m ∈ [1, 36].
        for (var i = 0; i < ForecastMonths; i++)
        {
            var entry = schedule.Entries[i];
            var expectedEnding = entry.BeginningBalance - entry.Principal;
            if (expectedEnding < 0m) expectedEnding = 0m;

            Assert.Equal(expectedEnding, entry.EndingBalance);
        }
    }

    // ------------------------------------------------------------------
    // R11.9: Declining monthly interest while balance > 0 and annualRate > 0.
    // ------------------------------------------------------------------

    [Fact]
    public void MonthlyLoanInterest_Declines_Monotonically_While_LoanBeginningBalance_IsPositive()
    {
        // Positive-rate, standard-amortization loan; interest must not rise
        // between consecutive months while the beginning balance is positive.
        const decimal loanProceeds = 250_000m;
        const decimal annualRate = 0.075m;
        const int termMonths = 60;    // > 36, so every month in the forecast window has a positive balance.

        var schedule = Compute(loanProceeds, annualRate, termMonths);

        // R11.9 states: while BeginningBalance[m] > 0 and rate > 0,
        // Interest[m+1] ≤ Interest[m]. Iterate over month pairs within the
        // 36-month window and only assert the inequality when the guard
        // (BeginningBalance[m+1] > 0) holds — this keeps the property well
        // defined regardless of where the balance reaches zero.
        for (var i = 0; i < ForecastMonths - 1; i++)
        {
            if (schedule.Entries[i + 1].BeginningBalance <= 0m) continue;

            Assert.True(
                schedule.Entries[i + 1].Interest <= schedule.Entries[i].Interest,
                $"Interest at month {schedule.Entries[i + 1].Month} " +
                $"({schedule.Entries[i + 1].Interest}) must be ≤ interest at " +
                $"month {schedule.Entries[i].Month} ({schedule.Entries[i].Interest}).");
        }
    }

    // ------------------------------------------------------------------
    // R11.10, R11.12 (term ≤ 36 branch): rows past Loan_Term_Months are
    // all-zero AND Loan_Ending_Balance[Loan_Term_Months] = 0.
    // ------------------------------------------------------------------

    [Fact]
    public void LoanScheduleEntries_BeyondTermMonths_AreAllZeros_WhenTermIsLessThan36()
    {
        const decimal loanProceeds = 30_000m;
        const decimal annualRate = 0.04m;
        const int termMonths = 12;

        var schedule = Compute(loanProceeds, annualRate, termMonths);

        // R11.10: rows past payoff must be (m, 0, 0, 0, 0, 0). We still emit
        // exactly 36 rows total (LoanSchedule invariant, R11.12 wording),
        // and their Month indices remain 1-based.
        Assert.Equal(ForecastMonths, schedule.Entries.Count);
        for (var m = termMonths + 1; m <= ForecastMonths; m++)
        {
            var entry = schedule.Entries[m - 1];
            Assert.Equal(m, entry.Month);
            Assert.Equal(0m, entry.BeginningBalance);
            Assert.Equal(0m, entry.Payment);
            Assert.Equal(0m, entry.Interest);
            Assert.Equal(0m, entry.Principal);
            Assert.Equal(0m, entry.EndingBalance);
        }
    }

    [Fact]
    public void LoanEndingBalance_At_LoanTermMonths_IsExactlyZero_WhenTermIsLessThan36()
    {
        // R11.12 requires the final-month principal to absorb any residual
        // so the balance reaches exactly zero at Loan_Term_Months, even
        // when standard amortization would leave a rounding remainder.
        const decimal loanProceeds = 30_000m;
        const decimal annualRate = 0.04m;
        const int termMonths = 12;

        var schedule = Compute(loanProceeds, annualRate, termMonths);

        Assert.Equal(0m, schedule.Entries[termMonths - 1].EndingBalance);
    }

    // ------------------------------------------------------------------
    // R11.11: Term > 36 ⇒ Loan_Ending_Balance[36] > 0 with no forced early payoff.
    // ------------------------------------------------------------------

    [Fact]
    public void LoanEndingBalance_At_Month36_IsPositive_WhenLoanTermMonthsExceeds36()
    {
        // R11.11: for a longer-than-forecast-window loan, the schedule shows
        // continuing amortization through month 36 with a positive residual;
        // it MUST NOT force early payoff.
        const decimal loanProceeds = 500_000m;
        const decimal annualRate = 0.055m;
        const int termMonths = 120;

        var schedule = Compute(loanProceeds, annualRate, termMonths);

        Assert.True(
            schedule.Entries[ForecastMonths - 1].EndingBalance > 0m,
            $"EndingBalance[36] must be positive when term > 36, but was " +
            $"{schedule.Entries[ForecastMonths - 1].EndingBalance}.");
    }

    [Fact]
    public void LoanSchedule_ContinuesActiveAmortization_ThroughMonth36_WhenTermExceeds36()
    {
        // A companion assertion to the "EndingBalance[36] > 0" test above:
        // month 36 must not look like a post-payoff (zero) row. Payment,
        // interest, principal, and balances must all be strictly positive.
        const decimal loanProceeds = 500_000m;
        const decimal annualRate = 0.055m;
        const int termMonths = 120;

        var schedule = Compute(loanProceeds, annualRate, termMonths);
        var month36 = schedule.Entries[ForecastMonths - 1];

        Assert.Equal(ForecastMonths, month36.Month);
        Assert.True(month36.BeginningBalance > 0m);
        Assert.True(month36.Payment > 0m);
        Assert.True(month36.Interest > 0m);
        Assert.True(month36.Principal > 0m);
        Assert.True(month36.EndingBalance > 0m);
    }

    // ------------------------------------------------------------------
    // R11.12 (term = 36 branch): final-month residual absorbed exactly so
    // Loan_Ending_Balance[36] = 0.
    // ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(TermEqualsForecastMonthsConfigurations))]
    public void LoanEndingBalance_At_Month36_IsExactlyZero_WhenLoanTermMonthsEquals36(
        decimal loanProceeds, decimal annualRate)
    {
        // Standard amortization on decimal arithmetic can leave a
        // sub-cent (or sub-nano-cent) residual at month 36 that the
        // final-month principal absorption must eliminate. The assertion
        // here is exact-zero, not a tolerance.
        const int termMonths = 36;

        var schedule = Compute(loanProceeds, annualRate, termMonths);

        Assert.Equal(0m, schedule.Entries[termMonths - 1].EndingBalance);
    }
}

// ======================================================================
// Task 25 — zero-proceeds and zero-interest regimes (Requirements 11.1, 11.2, 11.12).
// Appended to this file per the co-authoring plan documented in the header.
// ======================================================================

public class LoanAmortizationZeroTests
{
    private const int ForecastMonths = ForecastConstants.ForecastMonths;

    // =====================================================================
    // R11.1 — Zero-proceeds regime.
    //
    // When Loan_Proceeds is zero the schedule is fully collapsed: every
    // monetary field on every row is zero, Monthly_Loan_Payment is zero,
    // and the shape (36 rows, 1-based Month indexing) is preserved so
    // downstream passes can index `LoanSchedule.Entries` uniformly
    // regardless of whether a loan was actually taken.
    // =====================================================================

    public class ZeroProceedsCases
    {
        [Fact]
        public void MonthlyPayment_Is_Zero_When_LoanProceeds_Is_Zero()
        {
            var sut = new LoanCalculator();

            var schedule = sut.Compute(
                loanProceeds: 0m,
                annualInterestRate: 0.05m,
                loanTermMonths: 36);

            Assert.Equal(0m, schedule.MonthlyPayment);
        }

        [Fact]
        public void Schedule_Contains_Exactly_36_Rows_When_LoanProceeds_Is_Zero()
        {
            var sut = new LoanCalculator();

            var schedule = sut.Compute(
                loanProceeds: 0m,
                annualInterestRate: 0.05m,
                loanTermMonths: 36);

            Assert.Equal(ForecastMonths, schedule.Entries.Count);
        }

        [Fact]
        public void Every_Row_Is_All_Zero_With_Correct_MonthIndex_When_LoanProceeds_Is_Zero()
        {
            // R11.1 requires every one of the four monetary fields, plus
            // Payment, to be zero for every m in [1, 36]. Month indexing
            // itself remains 1-based.
            var sut = new LoanCalculator();

            var schedule = sut.Compute(
                loanProceeds: 0m,
                annualInterestRate: 0.05m,
                loanTermMonths: 36);

            for (var m = 1; m <= ForecastMonths; m++)
            {
                var row = schedule.Entries[m - 1];

                Assert.Equal(m, row.Month);
                Assert.Equal(0m, row.BeginningBalance);
                Assert.Equal(0m, row.Payment);
                Assert.Equal(0m, row.Interest);
                Assert.Equal(0m, row.Principal);
                Assert.Equal(0m, row.EndingBalance);
            }
        }

        [Theory]
        [InlineData(0.00)]
        [InlineData(0.05)]
        [InlineData(0.10)]
        [InlineData(1.00)]
        public void Zero_Proceeds_Yields_All_Zero_Schedule_Regardless_Of_Annual_Interest_Rate(double annualRate)
        {
            // R11.1 is unconditional on the annual rate — the calculator must
            // short-circuit on zero proceeds before applying any interest math.
            var sut = new LoanCalculator();

            var schedule = sut.Compute(
                loanProceeds: 0m,
                annualInterestRate: (decimal)annualRate,
                loanTermMonths: 36);

            Assert.Equal(0m, schedule.MonthlyPayment);
            Assert.All(schedule.Entries, row =>
            {
                Assert.Equal(0m, row.BeginningBalance);
                Assert.Equal(0m, row.Payment);
                Assert.Equal(0m, row.Interest);
                Assert.Equal(0m, row.Principal);
                Assert.Equal(0m, row.EndingBalance);
            });
        }

        [Theory]
        [InlineData(1)]
        [InlineData(12)]
        [InlineData(35)]
        [InlineData(36)]
        [InlineData(60)]
        public void Zero_Proceeds_Yields_All_Zero_Schedule_Regardless_Of_Loan_Term_Months(int loanTermMonths)
        {
            var sut = new LoanCalculator();

            var schedule = sut.Compute(
                loanProceeds: 0m,
                annualInterestRate: 0.05m,
                loanTermMonths: loanTermMonths);

            Assert.Equal(0m, schedule.MonthlyPayment);
            Assert.Equal(ForecastMonths, schedule.Entries.Count);
            Assert.All(schedule.Entries, row =>
            {
                Assert.Equal(0m, row.BeginningBalance);
                Assert.Equal(0m, row.Payment);
                Assert.Equal(0m, row.Interest);
                Assert.Equal(0m, row.Principal);
                Assert.Equal(0m, row.EndingBalance);
            });
        }
    }

    // =====================================================================
    // R11.2 — Zero-interest regime (Loan_Proceeds > 0, annual rate = 0).
    //
    // The fixed-payment formula degenerates to a linear amortization:
    //     Monthly_Loan_Payment = Loan_Proceeds / Loan_Term_Months
    //     Monthly_Loan_Interest[m] = 0
    //     Monthly_Loan_Principal[m] = min(Monthly_Loan_Payment, Balance[m])
    // R11.12 additionally requires Loan_Ending_Balance[Loan_Term_Months] = 0,
    // absorbing any decimal-division residual into the final month's
    // principal so the balance closes out exactly.
    // =====================================================================

    public class ZeroInterestCases
    {
        [Fact]
        public void MonthlyPayment_Equals_LoanProceeds_Divided_By_LoanTermMonths_When_ExactlyDivisible()
        {
            var sut = new LoanCalculator();

            var schedule = sut.Compute(
                loanProceeds: 36_000m,
                annualInterestRate: 0m,
                loanTermMonths: 36);

            Assert.Equal(1_000m, schedule.MonthlyPayment);
        }

        [Fact]
        public void MonthlyPayment_Equals_LoanProceeds_Divided_By_LoanTermMonths_When_NotEvenlyDivisible()
        {
            var sut = new LoanCalculator();

            var schedule = sut.Compute(
                loanProceeds: 1_000m,
                annualInterestRate: 0m,
                loanTermMonths: 36);

            Assert.Equal(1_000m / 36m, schedule.MonthlyPayment);
        }

        [Fact]
        public void MonthlyPayment_Equals_LoanProceeds_Divided_By_LoanTermMonths_When_Term_Less_Than_36()
        {
            var sut = new LoanCalculator();

            var schedule = sut.Compute(
                loanProceeds: 24_000m,
                annualInterestRate: 0m,
                loanTermMonths: 24);

            Assert.Equal(1_000m, schedule.MonthlyPayment);
        }

        [Fact]
        public void Schedule_Contains_Exactly_36_Rows_When_AnnualInterestRate_Is_Zero()
        {
            var sut = new LoanCalculator();

            var schedule = sut.Compute(
                loanProceeds: 24_000m,
                annualInterestRate: 0m,
                loanTermMonths: 24);

            Assert.Equal(ForecastMonths, schedule.Entries.Count);
        }

        [Fact]
        public void Every_MonthlyInterest_Value_Is_Zero_When_AnnualInterestRate_Is_Zero()
        {
            var sut = new LoanCalculator();

            var schedule = sut.Compute(
                loanProceeds: 1_000m,
                annualInterestRate: 0m,
                loanTermMonths: 36);

            Assert.All(schedule.Entries, row => Assert.Equal(0m, row.Interest));
        }

        [Fact]
        public void Every_MonthlyInterest_Value_Is_Zero_When_AnnualInterestRate_Is_Zero_And_Term_Less_Than_36()
        {
            var sut = new LoanCalculator();

            var schedule = sut.Compute(
                loanProceeds: 24_000m,
                annualInterestRate: 0m,
                loanTermMonths: 24);

            Assert.All(schedule.Entries, row => Assert.Equal(0m, row.Interest));
        }

        [Fact]
        public void Loan_Ending_Balance_At_LoanTermMonths_Is_Zero_When_Divisible_And_Term_Equals_36()
        {
            var sut = new LoanCalculator();

            var schedule = sut.Compute(
                loanProceeds: 36_000m,
                annualInterestRate: 0m,
                loanTermMonths: 36);

            Assert.Equal(0m, schedule.Entries[36 - 1].EndingBalance);
        }

        [Fact]
        public void Loan_Ending_Balance_At_LoanTermMonths_Is_Zero_When_NotEvenlyDivisible_And_Term_Equals_36()
        {
            // 1,000 / 36 is a non-terminating decimal; R11.12 requires the
            // final Monthly_Loan_Principal absorb the residual so month 36
            // ends at exactly zero. Marquee residual-absorption case.
            var sut = new LoanCalculator();

            var schedule = sut.Compute(
                loanProceeds: 1_000m,
                annualInterestRate: 0m,
                loanTermMonths: 36);

            Assert.Equal(0m, schedule.Entries[36 - 1].EndingBalance);
        }

        [Fact]
        public void Loan_Ending_Balance_At_LoanTermMonths_Is_Zero_When_Term_Less_Than_36()
        {
            var sut = new LoanCalculator();

            var schedule = sut.Compute(
                loanProceeds: 24_000m,
                annualInterestRate: 0m,
                loanTermMonths: 24);

            Assert.Equal(0m, schedule.Entries[24 - 1].EndingBalance);
        }

        [Fact]
        public void Loan_Ending_Balance_At_LoanTermMonths_Is_Zero_When_NotEvenlyDivisible_And_Term_Less_Than_36()
        {
            var sut = new LoanCalculator();

            var schedule = sut.Compute(
                loanProceeds: 1_000m,
                annualInterestRate: 0m,
                loanTermMonths: 24);

            Assert.Equal(0m, schedule.Entries[24 - 1].EndingBalance);
        }
    }
}
