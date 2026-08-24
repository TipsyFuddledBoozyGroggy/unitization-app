// Unit tests for the Revenue pass (design §6.3, Requirements 5.1–5.4, 22.2).
//
// Assumed internal API (matched by the Pass 3 implementation in task 16):
//
//     namespace RehearsalForecast.Core.Forecast;
//
//     internal sealed record RevenueResult(
//         decimal MonthlyPricePerSqft,           // single scalar (R5.1)
//         IReadOnlyList<decimal> GrossRevenue,   // 36 monthly values (R5.2)
//         IReadOnlyList<decimal> GrossIncome);   // 36 monthly values (R5.3)
//
//     internal static class RevenueCalculator
//     {
//         internal static RevenueResult Compute(
//             IReadOnlyList<decimal> rentedSqft,     // exactly 36 monthly values from Pass 2
//             decimal flatPricePerSqft);              // candidate 36-month flat price
//     }
//
// The Revenue pass is decoupled from the rest of OccupancyResult because it only
// depends on Rented_Sqft. Task 16 wires it up inside ForecastCalculator by passing
// `occupancyResult.RentedSqft` and the current candidate `Flat_Price_Per_Sqft`.
//
// Every test name identifies the business rule it validates (Requirement 22.4).
// All arithmetic uses `decimal` (Requirement 19.1).

using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Forecast;
using Xunit;

namespace RehearsalForecast.Core.Tests;

public class RevenueTests
{
    private const int Months = ForecastConstants.ForecastMonths; // 36

    /// <summary>Returns a 36-element list where every entry equals <paramref name="value"/>.</summary>
    private static IReadOnlyList<decimal> ConstantRentedSqft(decimal value)
    {
        var list = new decimal[Months];
        for (var i = 0; i < Months; i++)
        {
            list[i] = value;
        }

        return list;
    }

    /// <summary>Returns a 36-element list where entry at index <c>m-1</c> equals <c>150 × m</c>.</summary>
    private static IReadOnlyList<decimal> RampRentedSqft()
    {
        var list = new decimal[Months];
        for (var m = 1; m <= Months; m++)
        {
            list[m - 1] = 150m * m;
        }

        return list;
    }

    // ---------------------------------------------------------------------
    // Requirement 5.1: Monthly_Price_Per_Sqft = Flat_Price_Per_Sqft / 36
    // ---------------------------------------------------------------------

    [Fact]
    public void MonthlyPricePerSqft_EqualsFlatPricePerSqftDividedBy36()
    {
        // 360 / 36 = 10 exactly in decimal.
        var result = RevenueCalculator.Compute(ConstantRentedSqft(0m), 360m);

        Assert.Equal(10m, result.MonthlyPricePerSqft);
    }

    [Fact]
    public void MonthlyPricePerSqft_IsZero_WhenFlatPricePerSqftIsZero()
    {
        // Requirement 15.4 / Design Decision 12: zero is a valid candidate price.
        var result = RevenueCalculator.Compute(ConstantRentedSqft(150m), 0m);

        Assert.Equal(0m, result.MonthlyPricePerSqft);
    }

    [Fact]
    public void MonthlyPricePerSqft_ScalesLinearly_WithFlatPricePerSqft()
    {
        // Sanity: doubling the flat price doubles the monthly price.
        var rented = ConstantRentedSqft(0m);
        var low = RevenueCalculator.Compute(rented, 360m);
        var high = RevenueCalculator.Compute(rented, 720m);

        Assert.Equal(10m, low.MonthlyPricePerSqft);
        Assert.Equal(20m, high.MonthlyPricePerSqft);
    }

    // ---------------------------------------------------------------------
    // Requirement 5.4: the same Flat_Price_Per_Sqft applies to every month
    // ---------------------------------------------------------------------

    [Fact]
    public void SameFlatPricePerSqft_AppliesToEveryMonth_Behaviourally()
    {
        // If two months carry identical Rented_Sqft, they must produce identical
        // Gross_Revenue — because a single Monthly_Price_Per_Sqft multiplies both.
        // Non-zero footage is placed only in Month 1 and Month 36 so that a bug
        // treating one endpoint differently from another would be visible.
        var rented = new decimal[Months];
        rented[0] = 200m;   // Month 1
        rented[35] = 200m;  // Month 36

        var result = RevenueCalculator.Compute(rented, 720m);

        // monthly = 720 / 36 = 20, so gross = 200 × 20 = 4000 in Month 1 and Month 36.
        Assert.Equal(20m, result.MonthlyPricePerSqft);
        Assert.Equal(4000m, result.GrossRevenue[0]);
        Assert.Equal(4000m, result.GrossRevenue[35]);
        for (var i = 1; i < Months - 1; i++)
        {
            Assert.Equal(0m, result.GrossRevenue[i]);
        }
    }

    [Fact]
    public void SameMonthlyPricePerSqft_IsUsedForEveryMonth_UnderConstantRentedSqft()
    {
        // Requirement 5.1 + 5.4 (behavioural check): with a constant rented-sqft
        // vector, Gross_Revenue must be identical for every m in [1, 36]. If the
        // implementation ever varied the price by month, this test would fail.
        var rented = ConstantRentedSqft(150m);

        var result = RevenueCalculator.Compute(rented, 360m);

        Assert.Equal(Months, result.GrossRevenue.Count);
        var expected = 150m * (360m / 36m); // 1500
        for (var m = 1; m <= Months; m++)
        {
            Assert.Equal(expected, result.GrossRevenue[m - 1]);
        }
    }

    // ---------------------------------------------------------------------
    // Requirement 5.2: Gross_Revenue[m] = Rented_Sqft[m] × Monthly_Price_Per_Sqft
    // ---------------------------------------------------------------------

    [Fact]
    public void GrossRevenue_EqualsRentedSqftTimesMonthlyPricePerSqft_ForEveryMonth()
    {
        // Ramp: Rented_Sqft[m-1] = 150 × m. Flat = 360 ⇒ monthly = 10.
        // Expected: Gross_Revenue[m-1] = 150 × m × 10 = 1500 × m.
        var rented = RampRentedSqft();

        var result = RevenueCalculator.Compute(rented, 360m);

        Assert.Equal(Months, result.GrossRevenue.Count);
        for (var m = 1; m <= Months; m++)
        {
            Assert.Equal(1500m * m, result.GrossRevenue[m - 1]);
        }
    }

    [Fact]
    public void GrossRevenue_IsZero_InEveryMonth_WhenRentedSqftIsZero()
    {
        // No occupancy ⇒ no revenue, regardless of price (Requirement 5.2).
        var result = RevenueCalculator.Compute(ConstantRentedSqft(0m), 999m);

        Assert.Equal(Months, result.GrossRevenue.Count);
        for (var m = 1; m <= Months; m++)
        {
            Assert.Equal(0m, result.GrossRevenue[m - 1]);
        }
    }

    [Fact]
    public void GrossRevenue_IsZero_InEveryMonth_WhenFlatPricePerSqftIsZero()
    {
        // Zero price collapses Monthly_Price_Per_Sqft to zero, so Gross_Revenue is
        // zero in every month regardless of occupancy (Requirement 5.1 + 5.2).
        var result = RevenueCalculator.Compute(RampRentedSqft(), 0m);

        Assert.Equal(Months, result.GrossRevenue.Count);
        for (var m = 1; m <= Months; m++)
        {
            Assert.Equal(0m, result.GrossRevenue[m - 1]);
        }
    }

    // ---------------------------------------------------------------------
    // Requirement 5.3: Gross_Income[m] = Gross_Revenue[m] (COGS out of scope)
    // ---------------------------------------------------------------------

    [Fact]
    public void GrossIncome_EqualsGrossRevenue_InEveryMonth_BecauseCogsIsOutOfScope()
    {
        // Design Decision 6: COGS is out of scope in this phase; Gross_Income tracks
        // Gross_Revenue exactly.
        var result = RevenueCalculator.Compute(RampRentedSqft(), 360m);

        Assert.Equal(Months, result.GrossIncome.Count);
        Assert.Equal(result.GrossRevenue.Count, result.GrossIncome.Count);
        for (var m = 1; m <= Months; m++)
        {
            Assert.Equal(result.GrossRevenue[m - 1], result.GrossIncome[m - 1]);
        }
    }

    [Fact]
    public void GrossIncome_MatchesGrossRevenue_UnderZeroPrice_AndZeroOccupancy()
    {
        // Edge cases: identity between Gross_Income and Gross_Revenue must hold even
        // when both sides are uniformly zero.
        var zeroPrice = RevenueCalculator.Compute(RampRentedSqft(), 0m);
        var zeroOcc = RevenueCalculator.Compute(ConstantRentedSqft(0m), 720m);

        for (var m = 1; m <= Months; m++)
        {
            Assert.Equal(zeroPrice.GrossRevenue[m - 1], zeroPrice.GrossIncome[m - 1]);
            Assert.Equal(zeroOcc.GrossRevenue[m - 1], zeroOcc.GrossIncome[m - 1]);
        }
    }

    // ---------------------------------------------------------------------
    // Structural: the result carries exactly 36 monthly entries (design §6.3).
    // ---------------------------------------------------------------------

    [Fact]
    public void RevenueResult_HasExactly36GrossRevenueAndGrossIncomeEntries()
    {
        var result = RevenueCalculator.Compute(ConstantRentedSqft(100m), 360m);

        Assert.Equal(Months, result.GrossRevenue.Count);
        Assert.Equal(Months, result.GrossIncome.Count);
    }
}
