// Focused unit tests for the Marketing pass (design §6.4 — Pass 4).
//
// The internal helper under test is
//   RehearsalForecast.Core.Forecast.MarketingCalculator.Compute(MarketingInputs)
//     : IReadOnlyList<decimal>
// which returns the 36-entry Marketing_Total vector defined by
// Requirement 6.3:
//
//   Marketing_Total[m] = Print[m] + Search[m] + Social[m] + OtherMarketing[m]
//
// The vector is emitted in Month 1 .. Month 36 order (index m - 1 == Month m).
//
// Coverage (Requirements 6.1, 6.2, 6.3, 22.2):
//   * Exactly 36 monthly totals are produced.
//   * Sum-of-four-line-items identity holds every month across every
//     constant / variable mode combination reachable through MarketingInputs.
//   * All-zero inputs collapse to an all-zero vector.
//   * All-nonzero constant inputs produce a constant vector equal to the
//     arithmetic sum of the four line items.
//   * Only the four line items (Print, Search, Social, OtherMarketing) are
//     summed; this is enforced structurally by MarketingInputs having exactly
//     those four record parameters, and each per-month sum test would fail if
//     any additional contributor were introduced.

using System;
using System.Collections.Generic;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;
using RehearsalForecast.Core.Schedules;
using Xunit;

namespace RehearsalForecast.Core.Tests;

public class MarketingSumTests
{
    private const int ForecastMonths = 36;

    // ---- Test data helpers ------------------------------------------------

    private static IReadOnlyList<decimal> PerMonth(Func<int, decimal> monthToValue)
    {
        var values = new decimal[ForecastMonths];
        for (var m = 1; m <= ForecastMonths; m++)
        {
            values[m - 1] = monthToValue(m);
        }

        return values;
    }

    private static MarketingInputs AllConstant(
        decimal print,
        decimal search,
        decimal social,
        decimal otherMarketing) =>
        new(
            MonthlySchedule<decimal>.Constant(print),
            MonthlySchedule<decimal>.Constant(search),
            MonthlySchedule<decimal>.Constant(social),
            MonthlySchedule<decimal>.Constant(otherMarketing));

    // ---- Length / structural shape ---------------------------------------

    [Fact]
    public void MarketingTotal_HasExactlyThirtySixMonthlyEntries()
    {
        // Requirement 22.2: the forecast is exactly 36 months.
        var totals = MarketingCalculator.Compute(AllConstant(0m, 0m, 0m, 0m));

        Assert.Equal(ForecastMonths, totals.Count);
    }

    // ---- All-zero baseline (Requirement 6.3) -----------------------------

    [Fact]
    public void MarketingTotal_IsZero_WhenAllFourLineItemsAreZero()
    {
        // Requirement 6.3: sum of four zero line items is zero for every month.
        var totals = MarketingCalculator.Compute(AllConstant(0m, 0m, 0m, 0m));

        for (var m = 1; m <= ForecastMonths; m++)
        {
            Assert.Equal(0m, totals[m - 1]);
        }
    }

    // ---- All-nonzero constant mode (Requirements 6.2, 6.3) ---------------

    [Fact]
    public void MarketingTotal_EqualsSumOfFourConstants_InEveryMonth()
    {
        // Requirement 6.2: each constant-mode line item applies to every month.
        // Requirement 6.3: Marketing_Total[m] is the sum of the four line items.
        const decimal print = 100m;
        const decimal search = 250.75m;
        const decimal social = 42.10m;
        const decimal otherMarketing = 17.50m;
        const decimal expected = print + search + social + otherMarketing;

        var totals = MarketingCalculator.Compute(
            AllConstant(print, search, social, otherMarketing));

        for (var m = 1; m <= ForecastMonths; m++)
        {
            Assert.Equal(expected, totals[m - 1]);
        }
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(10, 20, 30, 40)]
    [InlineData(1000, 2500, 425, 175)]
    [InlineData(1, 0, 0, 0)]
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 0, 0, 1)]
    public void MarketingTotal_ConstantInputs_SumAcrossFourLineItems(
        int print,
        int search,
        int social,
        int otherMarketing)
    {
        // Requirements 6.2 + 6.3 across representative constant-mode cases.
        // The single-nonzero-line rows also demonstrate each line item is
        // counted exactly once with coefficient 1 (Requirement 6.1: exactly
        // four line items, no duplicates or omissions).
        var totals = MarketingCalculator.Compute(
            AllConstant(print, search, social, otherMarketing));

        var expected = (decimal)(print + search + social + otherMarketing);
        for (var m = 1; m <= ForecastMonths; m++)
        {
            Assert.Equal(expected, totals[m - 1]);
        }
    }

    // ---- Mixed constant / variable modes (Requirements 6.2, 6.3) --------

    [Fact]
    public void MarketingTotal_MixedConstantAndVariableModes_SumsPerMonth()
    {
        // Requirement 6.3 across mixed schedule modes: the At(m) accessor on
        // MonthlySchedule<decimal> is used uniformly, so the calculator must
        // not care whether any given line item is Constant or Variable.
        var print = MonthlySchedule<decimal>.Constant(50m);
        var search = MonthlySchedule<decimal>.Variable(PerMonth(m => 10m * m));
        var social = MonthlySchedule<decimal>.Constant(25m);
        var otherMarketing = MonthlySchedule<decimal>.Variable(PerMonth(m => 0.5m * m));

        var totals = MarketingCalculator.Compute(
            new MarketingInputs(print, search, social, otherMarketing));

        for (var m = 1; m <= ForecastMonths; m++)
        {
            var expected = 50m + (10m * m) + 25m + (0.5m * m);
            Assert.Equal(expected, totals[m - 1]);
        }
    }

    [Fact]
    public void MarketingTotal_AllVariableModes_SumsPerMonth()
    {
        // Requirement 6.3 under all-variable inputs, with distinct per-month
        // values on every line item so that any wiring error (e.g. reading the
        // wrong line item for a given month) would surface.
        var print = MonthlySchedule<decimal>.Variable(PerMonth(m => 1m * m));
        var search = MonthlySchedule<decimal>.Variable(PerMonth(m => 2m * m));
        var social = MonthlySchedule<decimal>.Variable(PerMonth(m => 3m * m));
        var otherMarketing = MonthlySchedule<decimal>.Variable(PerMonth(m => 4m * m));

        var totals = MarketingCalculator.Compute(
            new MarketingInputs(print, search, social, otherMarketing));

        for (var m = 1; m <= ForecastMonths; m++)
        {
            Assert.Equal(10m * m, totals[m - 1]);
        }
    }

    [Fact]
    public void MarketingTotal_AllNonzeroPerMonthVariable_HasCorrectSpecificValues()
    {
        // Behaviour under all-nonzero inputs (task 17 explicit coverage) with
        // hand-picked specific values so the assertions serve as a plain-text
        // executable example of Requirement 6.3.
        var print = MonthlySchedule<decimal>.Variable(PerMonth(m => 100m + m));            // 101, 102, ...
        var search = MonthlySchedule<decimal>.Variable(PerMonth(m => 200m + (2m * m)));    // 202, 204, ...
        var social = MonthlySchedule<decimal>.Variable(PerMonth(m => 300m + (3m * m)));    // 303, 306, ...
        var otherMarketing = MonthlySchedule<decimal>.Variable(PerMonth(m => 400m + (4m * m))); // 404, 408, ...

        var totals = MarketingCalculator.Compute(
            new MarketingInputs(print, search, social, otherMarketing));

        for (var m = 1; m <= ForecastMonths; m++)
        {
            var expected = (100m + m) + (200m + (2m * m)) + (300m + (3m * m)) + (400m + (4m * m));
            Assert.Equal(expected, totals[m - 1]);
            Assert.True(totals[m - 1] > 0m, $"Marketing_Total[{m}] must be strictly positive when every line item is nonzero.");
        }
    }

    // ---- Line-item-isolation checks (Requirement 6.1) -------------------

    [Theory]
    [InlineData("Print")]
    [InlineData("Search")]
    [InlineData("Social")]
    [InlineData("OtherMarketing")]
    public void MarketingTotal_IsSensitiveTo_EachOfTheFourLineItems(string lineItem)
    {
        // Requirement 6.1: exactly four line items contribute to Marketing_Total.
        // Raising one line item by a fixed delta must raise every monthly
        // total by exactly that delta (coefficient 1, counted once).
        const decimal baseValue = 10m;
        const decimal delta = 7m;

        var baseline = MarketingCalculator.Compute(
            AllConstant(baseValue, baseValue, baseValue, baseValue));

        var bumped = MarketingCalculator.Compute(new MarketingInputs(
            MonthlySchedule<decimal>.Constant(lineItem == "Print" ? baseValue + delta : baseValue),
            MonthlySchedule<decimal>.Constant(lineItem == "Search" ? baseValue + delta : baseValue),
            MonthlySchedule<decimal>.Constant(lineItem == "Social" ? baseValue + delta : baseValue),
            MonthlySchedule<decimal>.Constant(lineItem == "OtherMarketing" ? baseValue + delta : baseValue)));

        for (var m = 1; m <= ForecastMonths; m++)
        {
            Assert.Equal(baseline[m - 1] + delta, bumped[m - 1]);
        }
    }
}
