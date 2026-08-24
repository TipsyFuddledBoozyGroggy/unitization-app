// Property 6: Monthly composition identities.
// Validates: Requirements 5.1, 5.2, 5.3, 5.4, 6.3, 7.2, 7.4, 7.5.
//
// Design §10 (Property 6), §15.4. For any valid ForecastInputs and any
// Flat_Price_Per_Sqft, for every month m in [1, 36]:
//
//   * Monthly_Price_Per_Sqft = Flat_Price_Per_Sqft / 36                    (R5.1, R5.4).
//   * Gross_Revenue[m]       = Rented_Sqft[m] * Monthly_Price_Per_Sqft     (R5.2).
//   * Gross_Income[m]        = Gross_Revenue[m]                            (R5.3).
//   * Marketing_Total[m]     = Print[m] + Search[m] + Social[m]
//                              + Other_Marketing[m]                        (R6.3).
//   * Payroll_Tax[m]         = Wages[m] * PayrollTaxRate                   (R7.2).
//   * Operations_Total[m]    = sum of 14 line items + Payroll_Tax[m]
//                              (excludes Monthly_Loan_Interest[m] and
//                              Monthly_Depreciation)                       (R7.4, R7.5).
//
// FsCheck.Xunit runs the [Property] at least 100 iterations (the default).
// Each generated primitive is mapped to a bounded decimal via
// PropertyTestHelpers so decimal overflow paths in the calculator are not
// exercised by extreme inputs.

using FsCheck.Xunit;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;
using RehearsalForecast.Core.Schedules;
using Xunit;

namespace RehearsalForecast.Core.Tests.PropertyTests;

public class MonthlyCompositionProperty
{
    /// <summary>
    /// Builds a <see cref="ForecastInputs"/> whose marketing and operations
    /// line items carry the four / fourteen generated per-line values as
    /// Constant-mode schedules. This lets the property directly reconstruct
    /// Marketing_Total and Operations_Total from the individual line-item
    /// values and check the composition identities against the calculator's
    /// output.
    /// </summary>
    private static ForecastInputs BuildInputs(
        decimal print,
        decimal search,
        decimal social,
        decimal otherMarketing,
        decimal accounting,
        decimal custodial,
        decimal gas,
        decimal insurance,
        decimal it,
        decimal officeSupplies,
        decimal professionalServices,
        decimal rentExpense,
        decimal repairs,
        decimal shipping,
        decimal propertyTax,
        decimal utilities,
        decimal wages,
        decimal otherOperations,
        decimal totalSqft,
        decimal percentageAvailable,
        decimal annualLoanRate,
        int loanTermMonths,
        decimal incomeTaxRate)
    {
        return PropertyTestHelpers.MakeInputs(
            print: print,
            search: search,
            social: social,
            otherMarketing: otherMarketing,
            accounting: accounting,
            custodial: custodial,
            gas: gas,
            insurance: insurance,
            it: it,
            officeSupplies: officeSupplies,
            professionalServices: professionalServices,
            rentExpense: rentExpense,
            repairs: repairs,
            shipping: shipping,
            propertyTax: propertyTax,
            utilities: utilities,
            wages: wages,
            otherOperations: otherOperations,
            totalSqft: totalSqft,
            percentageAvailableForRent: percentageAvailable,
            annualLoanInterestRate: annualLoanRate,
            loanTermMonths: loanTermMonths,
            incomeTaxRate: incomeTaxRate);
    }

    /// <summary>
    /// Property 6: monthly composition identities hold for every month
    /// across arbitrary valid inputs and any nonnegative Flat_Price_Per_Sqft.
    /// Validates: Requirements 5.1, 5.2, 5.3, 5.4, 6.3, 7.2, 7.4, 7.5.
    /// </summary>
    [Property]
    public void MonthlyComposition_MatchesRequirementIdentities(
        uint priceRaw,
        uint printRaw,
        uint searchRaw,
        uint socialRaw,
        uint otherMarketingRaw,
        uint accountingRaw,
        uint custodialRaw,
        uint gasRaw,
        uint insuranceRaw,
        uint itRaw,
        uint officeSuppliesRaw,
        uint professionalServicesRaw,
        uint rentExpenseRaw,
        uint repairsRaw,
        uint shippingRaw,
        uint propertyTaxRaw,
        uint utilitiesRaw,
        uint wagesRaw,
        uint otherOperationsRaw,
        uint totalSqftRaw,
        uint percentageRaw,
        uint annualRateRaw,
        int loanTermRaw,
        uint incomeTaxRateRaw)
    {
        // ---- Map generated raw primitives to bounded decimals. ----

        var price = PropertyTestHelpers.MoneyFromRaw(priceRaw);
        var print = PropertyTestHelpers.MoneyFromRaw(printRaw);
        var search = PropertyTestHelpers.MoneyFromRaw(searchRaw);
        var social = PropertyTestHelpers.MoneyFromRaw(socialRaw);
        var otherMarketing = PropertyTestHelpers.MoneyFromRaw(otherMarketingRaw);
        var accounting = PropertyTestHelpers.MoneyFromRaw(accountingRaw);
        var custodial = PropertyTestHelpers.MoneyFromRaw(custodialRaw);
        var gas = PropertyTestHelpers.MoneyFromRaw(gasRaw);
        var insurance = PropertyTestHelpers.MoneyFromRaw(insuranceRaw);
        var it = PropertyTestHelpers.MoneyFromRaw(itRaw);
        var officeSupplies = PropertyTestHelpers.MoneyFromRaw(officeSuppliesRaw);
        var professionalServices = PropertyTestHelpers.MoneyFromRaw(professionalServicesRaw);
        var rentExpense = PropertyTestHelpers.MoneyFromRaw(rentExpenseRaw);
        var repairs = PropertyTestHelpers.MoneyFromRaw(repairsRaw);
        var shipping = PropertyTestHelpers.MoneyFromRaw(shippingRaw);
        var propertyTax = PropertyTestHelpers.MoneyFromRaw(propertyTaxRaw);
        var utilities = PropertyTestHelpers.MoneyFromRaw(utilitiesRaw);
        var wages = PropertyTestHelpers.MoneyFromRaw(wagesRaw);
        var otherOperations = PropertyTestHelpers.MoneyFromRaw(otherOperationsRaw);
        var totalSqft = PropertyTestHelpers.SqftFromRaw(totalSqftRaw);
        var percentageAvailable = PropertyTestHelpers.RateFromRaw(percentageRaw);
        var annualLoanRate = PropertyTestHelpers.RateFromRaw(annualRateRaw);
        var loanTermMonths = PropertyTestHelpers.PositiveTermFromRaw(loanTermRaw);
        var incomeTaxRate = PropertyTestHelpers.RateFromRaw(incomeTaxRateRaw);

        var inputs = BuildInputs(
            print, search, social, otherMarketing,
            accounting, custodial, gas, insurance, it,
            officeSupplies, professionalServices, rentExpense, repairs,
            shipping, propertyTax, utilities, wages, otherOperations,
            totalSqft, percentageAvailable, annualLoanRate, loanTermMonths, incomeTaxRate);

        var calculator = PropertyTestHelpers.NewForecastCalculator();
        var result = calculator.Compute(inputs, price);

        // ---- R5.1 / R5.4: Monthly_Price_Per_Sqft = Flat / 36. ----

        var expectedMonthlyPrice = price / ForecastConstants.ForecastMonths;
        Assert.Equal(expectedMonthlyPrice, result.MonthlyPricePerSqft);

        // Expected line-item sums, computed once outside the loop since the
        // schedules are Constant-mode and the calculator sees the same
        // value in every month.
        var expectedMarketingTotal = print + search + social + otherMarketing;
        var expectedPayrollTax = wages * ForecastConstants.PayrollTaxRate;
        var expectedOperationsLineSum =
            accounting
            + custodial
            + gas
            + insurance
            + it
            + officeSupplies
            + professionalServices
            + rentExpense
            + repairs
            + shipping
            + propertyTax
            + utilities
            + wages
            + otherOperations;
        var expectedOperationsTotal = expectedOperationsLineSum + expectedPayrollTax;

        for (var m = 1; m <= 36; m++)
        {
            var row = result.Rows[m - 1];

            // R5.1: Monthly_Price_Per_Sqft is constant across months (echo).
            Assert.Equal(expectedMonthlyPrice, row.MonthlyPricePerSqft);

            // R5.2: Gross_Revenue[m] = Rented_Sqft[m] * Monthly_Price_Per_Sqft.
            Assert.Equal(row.RentedSqft * expectedMonthlyPrice, row.GrossRevenue);

            // R5.3: Gross_Income[m] = Gross_Revenue[m] (COGS out of scope).
            Assert.Equal(row.GrossRevenue, row.GrossIncome);

            // R6.3: Marketing_Total[m] = sum of the four marketing line items.
            Assert.Equal(expectedMarketingTotal, row.MarketingTotal);

            // R7.2: Payroll_Tax[m] = Wages[m] * PayrollTaxRate.
            Assert.Equal(wages, row.Wages);
            Assert.Equal(expectedPayrollTax, row.PayrollTax);

            // R7.4: Operations_Total[m] = sum of the 14 line items + Payroll_Tax.
            Assert.Equal(expectedOperationsTotal, row.OperationsTotal);

            // R7.5: Operations_Total[m] excludes Monthly_Loan_Interest[m] and
            // Monthly_Depreciation. Structural check: subtracting the sum of
            // the 14 line items from Operations_Total must recover exactly
            // Payroll_Tax; if either interest or depreciation had leaked in,
            // this identity would break by their nonzero contribution.
            Assert.Equal(
                expectedPayrollTax,
                row.OperationsTotal - expectedOperationsLineSum);
        }
    }
}
