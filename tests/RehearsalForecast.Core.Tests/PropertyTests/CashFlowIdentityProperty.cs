// Property 1: Cash-flow accounting identity.
// Validates: Requirements 13.1, 13.2, 13.3, 13.4, 13.5, 13.6, 13.7, 11.14.
//
// Design §10 (Property 1), §15.4. For any valid ForecastInputs and any
// Flat_Price_Per_Sqft >= 0, the resulting forecast satisfies the full
// Requirement 13.4 accounting identity for every month m in [1, 36]:
//
//   Beginning_Cash[1] == BeginningCashMonth1                              (R13.2)
//   Beginning_Cash[m] == Ending_Cash[m - 1]           for m >= 2          (R13.3)
//   Ending_Cash[m]    == Beginning_Cash[m]
//                        + Owner_Investment_In_Month[m]
//                        + Loan_Proceeds_In_Month[m]
//                        + Net_Income[m]
//                        + Monthly_Depreciation                            (R13.5 add-back)
//                        - Capital_Expenditures_In_Month[m]
//                        - Monthly_Loan_Principal[m]                       (R11.14: principal only)
//                        - Owner_Withdrawals                               (R13.6 every month)
//
// FsCheck.Xunit runs the [Property] at least 100 iterations (the default).
// Primitive uint / int parameters are mapped to bounded decimal values via
// PropertyTestHelpers so decimal overflow paths in the calculator are not
// exercised by extreme generated inputs.

using FsCheck.Xunit;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;
using Xunit;

namespace RehearsalForecast.Core.Tests.PropertyTests;

public class CashFlowIdentityProperty
{
    /// <summary>
    /// Constructs a <see cref="ForecastInputs"/> whose numeric fields vary
    /// per generated parameter set while every schedulable field stays in
    /// Constant mode with the baseline defaults from
    /// <see cref="PropertyTestHelpers.MakeInputs"/>.
    /// </summary>
    private static ForecastInputs BuildInputs(
        uint openingCashRaw,
        uint ownerInvestmentRaw,
        uint ownerWithdrawalsRaw,
        uint buildingCostRaw,
        uint equipmentRaw,
        uint improvementRaw,
        uint buildingPurchaseRaw,
        uint otherCapitalRaw,
        uint totalSqftRaw,
        uint percentageRaw,
        uint annualRateRaw,
        int loanTermRaw,
        uint incomeTaxRateRaw,
        int targetMonthRaw,
        int depreciationYearsRaw)
    {
        return PropertyTestHelpers.MakeInputs(
            beginningCashMonth1: PropertyTestHelpers.LargeMoneyFromRaw(openingCashRaw),
            ownerInvestment: PropertyTestHelpers.LargeMoneyFromRaw(ownerInvestmentRaw),
            ownerWithdrawals: PropertyTestHelpers.MoneyFromRaw(ownerWithdrawalsRaw),
            totalBuildingCost: PropertyTestHelpers.LargeMoneyFromRaw(buildingCostRaw),
            equipment: PropertyTestHelpers.LargeMoneyFromRaw(equipmentRaw),
            totalImprovementCost: PropertyTestHelpers.LargeMoneyFromRaw(improvementRaw),
            buildingPurchaseCost: PropertyTestHelpers.LargeMoneyFromRaw(buildingPurchaseRaw),
            otherCapitalCost: PropertyTestHelpers.LargeMoneyFromRaw(otherCapitalRaw),
            totalSqft: PropertyTestHelpers.SqftFromRaw(totalSqftRaw),
            percentageAvailableForRent: PropertyTestHelpers.RateFromRaw(percentageRaw),
            annualLoanInterestRate: PropertyTestHelpers.RateFromRaw(annualRateRaw),
            loanTermMonths: PropertyTestHelpers.PositiveTermFromRaw(loanTermRaw),
            incomeTaxRate: PropertyTestHelpers.RateFromRaw(incomeTaxRateRaw),
            targetCashPositiveMonth: PropertyTestHelpers.TargetMonthFromRaw(targetMonthRaw),
            depreciationPeriodYears: PropertyTestHelpers.PositiveTermFromRaw(depreciationYearsRaw, max: 50));
    }

    /// <summary>
    /// Property 1: for any valid inputs and any Flat_Price_Per_Sqft >= 0
    /// the cash-flow accounting identity in Requirement 13.4 (with the
    /// R13.2/R13.3 boundary conditions) holds for every month.
    /// Validates: Requirements 13.1, 13.2, 13.3, 13.4, 13.5, 13.6, 13.7, 11.14.
    /// </summary>
    [Property]
    public void CashFlowAccountingIdentity_HoldsForEveryMonth(
        uint priceRaw,
        uint openingCashRaw,
        uint ownerInvestmentRaw,
        uint ownerWithdrawalsRaw,
        uint buildingCostRaw,
        uint equipmentRaw,
        uint improvementRaw,
        uint buildingPurchaseRaw,
        uint otherCapitalRaw,
        uint totalSqftRaw,
        uint percentageRaw,
        uint annualRateRaw,
        int loanTermRaw,
        uint incomeTaxRateRaw,
        int targetMonthRaw,
        int depreciationYearsRaw)
    {
        var price = PropertyTestHelpers.MoneyFromRaw(priceRaw);

        var inputs = BuildInputs(
            openingCashRaw,
            ownerInvestmentRaw,
            ownerWithdrawalsRaw,
            buildingCostRaw,
            equipmentRaw,
            improvementRaw,
            buildingPurchaseRaw,
            otherCapitalRaw,
            totalSqftRaw,
            percentageRaw,
            annualRateRaw,
            loanTermRaw,
            incomeTaxRateRaw,
            targetMonthRaw,
            depreciationYearsRaw);

        var calculator = PropertyTestHelpers.NewForecastCalculator();
        var result = calculator.Compute(inputs, price);

        // Requirement 13.1: exactly 36 monthly rows.
        Assert.Equal(36, result.Rows.Count);

        for (var m = 1; m <= 36; m++)
        {
            var row = result.Rows[m - 1];

            // Requirement 13.2: Beginning_Cash[1] = user-supplied opening cash.
            if (m == 1)
            {
                Assert.Equal(inputs.ForecastControls.BeginningCashMonth1, row.BeginningCash);
            }
            else
            {
                // Requirement 13.3: Beginning_Cash[m] = Ending_Cash[m - 1] for m >= 2.
                Assert.Equal(result.Rows[m - 2].EndingCash, row.BeginningCash);
            }

            // Requirement 13.4 verbatim (additions then subtractions per
            // Requirement 13.7 sign convention). Non-cash Monthly_Depreciation
            // is added back per Requirement 13.5; only Monthly_Loan_Principal
            // reduces cash per Requirement 11.14; Owner_Withdrawals applies
            // every month per Requirement 13.6.
            var expectedEnding =
                row.BeginningCash
                + row.OwnerInvestmentInMonth
                + row.LoanProceedsInMonth
                + row.NetIncome
                + row.MonthlyDepreciation
                - row.CapitalExpendituresInMonth
                - row.MonthlyLoanPrincipal
                - row.OwnerWithdrawals;

            Assert.Equal(expectedEnding, row.EndingCash);

            // Requirement 13.6 cross-check: the OwnerWithdrawals value echoed
            // on each row is the same scalar supplied in the inputs. A
            // per-month drift would break the identity above, but this direct
            // check pins down the "applied uniformly to every month" clause.
            Assert.Equal(inputs.OwnerActivity.OwnerWithdrawals, row.OwnerWithdrawals);
        }
    }
}
