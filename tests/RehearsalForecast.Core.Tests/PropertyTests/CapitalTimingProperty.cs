// Property-based test for Property 8 — Capital summation and financing timing
// (design §10, Property 8; §15.4).
//
// Property 8 states that for any valid capital line items and any
// Owner_Investment ≥ 0:
//
//   1. Total_Capital = Equipment + TotalImprovementCost + BuildingPurchaseCost
//                    + OtherCapitalCost                                            (R9.1)
//   2. Loan_Proceeds = max(Total_Capital − Owner_Investment, 0)                    (R10.1)
//   3. When Owner_Investment > Total_Capital: Loan_Proceeds = 0 AND
//      Capital_Expenditures_In_Month[1] = Total_Capital
//      (owner over-investment does not shrink the capex)                           (R10.2, R27.3-adj)
//   4. Capital_Expenditures_In_Month[1] = Total_Capital;
//      Capital_Expenditures_In_Month[m] = 0 for m ∈ [2, 36]                         (R9.2, R9.3)
//   5. Owner_Investment_In_Month[1] = Owner_Investment;
//      Owner_Investment_In_Month[m] = 0 for m ∈ [2, 36]                             (R10.3)
//   6. Loan_Proceeds_In_Month[1] = Loan_Proceeds;
//      Loan_Proceeds_In_Month[m] = 0 for m ∈ [2, 36]                                (R10.4)
//
// The test drives the pass-7 internal helper (design §6.7) directly. That
// keeps the property focused on the arithmetic identities named by R9 and
// R10 without pulling in the rest of the pipeline. The test also exercises
// the Requirement 27.3 corollary (Total_Capital == 0 AND Owner_Investment == 0
// implies zero loan) because Math.Max(0 − 0, 0) = 0 falls out for free from
// the identity in bullet 2 above; a dedicated Total_Capital == 0 case is
// still exercised by the "OwnerInvestment > TotalCapital ⇒ LoanProceeds == 0"
// assertion whenever both bounded generators happen to produce zero.
//
// Bounding strategy: raw FsCheck-generated System.Int32 values fold into
// non-negative decimal amounts in a modest range. Each capital line item and
// Owner_Investment are generated independently so the property covers the
// full input lattice, including owner-over-investment. All arithmetic
// remains in decimal per Requirement 19.1.
//
// Validates: Requirements 9.1, 9.2, 9.3, 10.1, 10.2, 10.3, 10.4, 27.3

using FsCheck.Xunit;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;
using Xunit;

namespace RehearsalForecast.Core.Tests.PropertyTests;

public class CapitalTimingProperty
{
    private const int Months = ForecastConstants.ForecastMonths;

    // ------------------------------------------------------------------
    // Bounded generator: raw Int32 → nonnegative USD amount with cent
    // precision, capped to ~1,000,000 so the sum of four line items plus
    // owner investment stays well below decimal overflow bounds.
    // ------------------------------------------------------------------

    private static decimal BoundMoney(int raw) =>
        (decimal)(Math.Abs((long)raw) % 100_000_000L) / 100m;

    // ------------------------------------------------------------------
    // Property 8 — full universal statement
    //
    // Validates: Requirements 9.1, 9.2, 9.3, 10.1, 10.2, 10.3, 10.4, 27.3
    // ------------------------------------------------------------------

    [Property]
    public void Property_8_Capital_Summation_And_Financing_Timing(
        int rawEquipment,
        int rawImprovement,
        int rawBuildingPurchase,
        int rawOtherCapital,
        int rawOwnerInvestment)
    {
        var equipment = BoundMoney(rawEquipment);
        var improvement = BoundMoney(rawImprovement);
        var buildingPurchase = BoundMoney(rawBuildingPurchase);
        var otherCapital = BoundMoney(rawOtherCapital);
        var ownerInvestment = BoundMoney(rawOwnerInvestment);

        var capital = new CapitalInputs(
            Equipment: equipment,
            TotalImprovementCost: improvement,
            BuildingPurchaseCost: buildingPurchase,
            OtherCapitalCost: otherCapital);

        var result = CapitalCalculator.Compute(capital, ownerInvestment);

        // R9.1: Total_Capital sums the four capital line items exactly.
        var expectedTotalCapital = equipment + improvement + buildingPurchase + otherCapital;
        Assert.Equal(expectedTotalCapital, result.TotalCapital);

        // R10.1: Loan_Proceeds = max(Total_Capital − Owner_Investment, 0).
        var expectedLoanProceeds = Math.Max(expectedTotalCapital - ownerInvestment, 0m);
        Assert.Equal(expectedLoanProceeds, result.LoanProceeds);

        // R10.2: Owner-over-investment ⇒ Loan_Proceeds = 0 AND
        // Capital_Expenditures_In_Month[1] still equals Total_Capital
        // (i.e., the capex is not netted against the owner investment).
        if (ownerInvestment > expectedTotalCapital)
        {
            Assert.Equal(0m, result.LoanProceeds);
            Assert.Equal(expectedTotalCapital, result.CapitalExpendituresInMonth[0]);
        }

        // R27.3 corollary: Total_Capital == 0 AND Owner_Investment == 0
        // ⇒ Loan_Proceeds == 0. The max(0 − 0, 0) identity guarantees this
        // whenever both bounded generators produce zero.
        if (expectedTotalCapital == 0m && ownerInvestment == 0m)
        {
            Assert.Equal(0m, result.LoanProceeds);
        }

        // ---- Month-1 timing on every vector (R9.2, R9.3, R10.3, R10.4) ----
        //
        // All three vectors must have exactly ForecastMonths (36) entries,
        // with index 0 (Month 1) carrying the scalar and indices 1..35 all
        // zero. Any leak — a nonzero entry in months 2..36, or a mismatch
        // between the scalar and index 0 — surfaces here.
        Assert.Equal(Months, result.CapitalExpendituresInMonth.Count);
        Assert.Equal(Months, result.OwnerInvestmentInMonth.Count);
        Assert.Equal(Months, result.LoanProceedsInMonth.Count);

        Assert.Equal(expectedTotalCapital, result.CapitalExpendituresInMonth[0]);
        Assert.Equal(ownerInvestment, result.OwnerInvestmentInMonth[0]);
        Assert.Equal(expectedLoanProceeds, result.LoanProceedsInMonth[0]);

        for (var i = 1; i < Months; i++)
        {
            Assert.Equal(0m, result.CapitalExpendituresInMonth[i]);
            Assert.Equal(0m, result.OwnerInvestmentInMonth[i]);
            Assert.Equal(0m, result.LoanProceedsInMonth[i]);
        }

        // R27.3 (loan-sizing floor at zero): Loan_Proceeds is nonnegative
        // for every input in the universe covered by this property.
        Assert.True(result.LoanProceeds >= 0m);
    }
}
