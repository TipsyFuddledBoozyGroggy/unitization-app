// Tests for the operations expense total and derived payroll tax
// (design §6.5, §15.3 → OperationsSumTests).
//
// Validates:
//   * Requirement 7.1  — 14 operations line items are captured individually.
//   * Requirement 7.2  — Payroll_Tax[m] = Wages[m] × 0.0765.
//   * Requirement 7.3  — Payroll_Tax is derived; users cannot supply it.
//   * Requirement 7.4  — Operations_Total[m] sums all 14 line items + Payroll_Tax[m].
//   * Requirement 7.5  — Operations_Total[m] excludes Monthly_Loan_Interest[m]
//                       and Monthly_Depreciation.
//   * Requirement 22.2 — Test names identify the business rule under test.
//
// The 0.0765 rate is referenced as ForecastConstants.PayrollTaxRate (§5.1)
// rather than hardcoded, per the "constants live in one place" convention
// carried by ForecastConstants (Requirement 19).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;
using RehearsalForecast.Core.Schedules;
using Xunit;

namespace RehearsalForecast.Core.Tests;

public class OperationsSumTests
{
    private const int Months = ForecastConstants.ForecastMonths;
    private const decimal PayrollTaxRate = ForecastConstants.PayrollTaxRate;

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static OperationsInputs MakeConstantInputs(
        decimal accounting = 0m,
        decimal custodial = 0m,
        decimal gas = 0m,
        decimal insurance = 0m,
        decimal it = 0m,
        decimal officeSupplies = 0m,
        decimal professionalServices = 0m,
        decimal rentExpense = 0m,
        decimal repairs = 0m,
        decimal shipping = 0m,
        decimal propertyTax = 0m,
        decimal utilities = 0m,
        decimal wages = 0m,
        decimal otherOperations = 0m)
        => new(
            MonthlySchedule<decimal>.Constant(accounting),
            MonthlySchedule<decimal>.Constant(custodial),
            MonthlySchedule<decimal>.Constant(gas),
            MonthlySchedule<decimal>.Constant(insurance),
            MonthlySchedule<decimal>.Constant(it),
            MonthlySchedule<decimal>.Constant(officeSupplies),
            MonthlySchedule<decimal>.Constant(professionalServices),
            MonthlySchedule<decimal>.Constant(rentExpense),
            MonthlySchedule<decimal>.Constant(repairs),
            MonthlySchedule<decimal>.Constant(shipping),
            MonthlySchedule<decimal>.Constant(propertyTax),
            MonthlySchedule<decimal>.Constant(utilities),
            MonthlySchedule<decimal>.Constant(wages),
            MonthlySchedule<decimal>.Constant(otherOperations));

    private static IReadOnlyList<decimal> Ramp(decimal step)
    {
        var xs = new decimal[Months];
        for (var i = 0; i < Months; i++)
        {
            xs[i] = step * (i + 1);
        }
        return xs;
    }

    // ---------------------------------------------------------------
    // R7.2 & R22.2: Payroll_Tax[m] = Wages[m] × 0.0765
    // ---------------------------------------------------------------

    [Fact]
    public void OperationsCalculator_ComputesPayrollTaxAsWagesTimesRate_ForConstantWages()
    {
        const decimal wages = 1_000m;
        var inputs = MakeConstantInputs(wages: wages);

        var result = OperationsCalculator.Compute(inputs);

        Assert.Equal(Months, result.PayrollTax.Count);
        Assert.All(result.PayrollTax, tax => Assert.Equal(wages * PayrollTaxRate, tax));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1234.56)]
    [InlineData(987654.321)]
    public void OperationsCalculator_ComputesPayrollTaxAsWagesTimesRate(double wagesInput)
    {
        var wages = (decimal)wagesInput;
        var inputs = MakeConstantInputs(wages: wages);

        var result = OperationsCalculator.Compute(inputs);

        Assert.All(result.PayrollTax, tax => Assert.Equal(wages * PayrollTaxRate, tax));
    }

    [Fact]
    public void OperationsCalculator_ComputesPayrollTaxPerMonth_ForVariableWages()
    {
        var wages = Ramp(100m); // 100, 200, ..., 3600
        var inputs = MakeConstantInputs() with
        {
            Wages = MonthlySchedule<decimal>.Variable(wages),
        };

        var result = OperationsCalculator.Compute(inputs);

        Assert.Equal(Months, result.Wages.Count);
        Assert.Equal(Months, result.PayrollTax.Count);
        for (var m = 0; m < Months; m++)
        {
            Assert.Equal(wages[m], result.Wages[m]);
            Assert.Equal(wages[m] * PayrollTaxRate, result.PayrollTax[m]);
        }
    }

    [Fact]
    public void OperationsCalculator_PayrollTax_IsZero_WhenWagesAreZero()
    {
        var inputs = MakeConstantInputs(wages: 0m);

        var result = OperationsCalculator.Compute(inputs);

        Assert.All(result.PayrollTax, tax => Assert.Equal(0m, tax));
    }

    [Fact]
    public void OperationsCalculator_PayrollTax_UsesForecastConstantsPayrollTaxRate()
    {
        // Regression guard: the derived rate must come from ForecastConstants
        // (0.0765), not a stray literal in the calculator. Verified indirectly
        // by asserting the exact wages × ForecastConstants.PayrollTaxRate value.
        const decimal wages = 12_345.67m;
        var inputs = MakeConstantInputs(wages: wages);

        var result = OperationsCalculator.Compute(inputs);

        Assert.All(result.PayrollTax, tax => Assert.Equal(wages * PayrollTaxRate, tax));
    }

    // ---------------------------------------------------------------
    // R7.3: User cannot supply Payroll_Tax
    // ---------------------------------------------------------------

    [Fact]
    public void OperationsInputs_DoesNotExposePayrollTaxProperty()
    {
        var payrollProps = typeof(OperationsInputs)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.Contains("Payroll", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(payrollProps);
    }

    [Fact]
    public void OperationsInputs_ConstructorDoesNotAcceptPayrollTaxArgument()
    {
        var ctorParamNames = typeof(OperationsInputs)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters())
            .Select(p => p.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(
            ctorParamNames,
            name => name.Contains("Payroll", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------
    // R7.4 & R22.2: Operations_Total sums all 14 line items + Payroll_Tax
    // ---------------------------------------------------------------

    [Fact]
    public void OperationsCalculator_OperationsTotal_SumsAllFourteenLineItemsPlusPayrollTax()
    {
        // Distinct values per line item so any omitted addend is visible.
        const decimal accounting = 1m;
        const decimal custodial = 2m;
        const decimal gas = 3m;
        const decimal insurance = 4m;
        const decimal it = 5m;
        const decimal officeSupplies = 6m;
        const decimal professionalServices = 7m;
        const decimal rentExpense = 8m;
        const decimal repairs = 9m;
        const decimal shipping = 10m;
        const decimal propertyTax = 11m;
        const decimal utilities = 12m;
        const decimal wages = 1_000m;
        const decimal otherOperations = 14m;

        var inputs = MakeConstantInputs(
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
            otherOperations: otherOperations);

        var expectedLineSum = accounting + custodial + gas + insurance + it
            + officeSupplies + professionalServices + rentExpense + repairs
            + shipping + propertyTax + utilities + wages + otherOperations;
        var expectedPayrollTax = wages * PayrollTaxRate;
        var expectedTotal = expectedLineSum + expectedPayrollTax;

        var result = OperationsCalculator.Compute(inputs);

        Assert.Equal(Months, result.OperationsTotal.Count);
        Assert.All(result.OperationsTotal, total => Assert.Equal(expectedTotal, total));
    }

    [Fact]
    public void OperationsCalculator_OperationsTotal_CountsWagesExactlyOnce()
    {
        // Wages appears both as a line item and as the base for Payroll_Tax.
        // Operations_Total = (sum of 14 line items, including Wages once) + (Wages × 0.0765).
        // This test verifies Wages is not summed twice into the line-item bucket.
        const decimal wages = 1_000m;
        var inputs = MakeConstantInputs(wages: wages);

        var result = OperationsCalculator.Compute(inputs);

        var expected = wages + (wages * PayrollTaxRate);
        Assert.All(result.OperationsTotal, total => Assert.Equal(expected, total));
    }

    [Fact]
    public void OperationsCalculator_OperationsTotal_IsZero_WhenAllLineItemsAreZero()
    {
        var inputs = MakeConstantInputs();

        var result = OperationsCalculator.Compute(inputs);

        Assert.Equal(Months, result.Wages.Count);
        Assert.Equal(Months, result.PayrollTax.Count);
        Assert.Equal(Months, result.OperationsTotal.Count);
        Assert.All(result.Wages, w => Assert.Equal(0m, w));
        Assert.All(result.PayrollTax, tax => Assert.Equal(0m, tax));
        Assert.All(result.OperationsTotal, total => Assert.Equal(0m, total));
    }

    [Fact]
    public void OperationsCalculator_OperationsTotal_HandlesMixedConstantAndVariableSchedules()
    {
        var wages = Ramp(100m);          // 100, 200, ..., 3600
        var accounting = Ramp(1m);        //   1,   2, ...,   36
        var utilities = Ramp(3m);         //   3,   6, ...,  108

        var inputs = new OperationsInputs(
            MonthlySchedule<decimal>.Variable(accounting),
            MonthlySchedule<decimal>.Constant(0m),
            MonthlySchedule<decimal>.Constant(0m),
            MonthlySchedule<decimal>.Constant(0m),
            MonthlySchedule<decimal>.Constant(0m),
            MonthlySchedule<decimal>.Constant(0m),
            MonthlySchedule<decimal>.Constant(0m),
            MonthlySchedule<decimal>.Constant(0m),
            MonthlySchedule<decimal>.Constant(0m),
            MonthlySchedule<decimal>.Constant(0m),
            MonthlySchedule<decimal>.Constant(0m),
            MonthlySchedule<decimal>.Variable(utilities),
            MonthlySchedule<decimal>.Variable(wages),
            MonthlySchedule<decimal>.Constant(0m));

        var result = OperationsCalculator.Compute(inputs);

        for (var m = 0; m < Months; m++)
        {
            var expectedPayrollTax = wages[m] * PayrollTaxRate;
            var expectedTotal = accounting[m] + utilities[m] + wages[m] + expectedPayrollTax;
            Assert.Equal(wages[m], result.Wages[m]);
            Assert.Equal(expectedPayrollTax, result.PayrollTax[m]);
            Assert.Equal(expectedTotal, result.OperationsTotal[m]);
        }
    }

    // ---------------------------------------------------------------
    // R7.5: Operations_Total excludes Monthly_Loan_Interest and Monthly_Depreciation
    // ---------------------------------------------------------------

    [Fact]
    public void OperationsCalculator_ComputeSignature_AcceptsOnlyOperationsInputs()
    {
        // Structural guarantee for R7.5: the operations helper's API surface
        // provides no channel through which Monthly_Loan_Interest or
        // Monthly_Depreciation could be added to Operations_Total.
        var computeMethods = typeof(OperationsCalculator)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)
            .Where(m => m.Name == "Compute")
            .ToList();

        Assert.Single(computeMethods);

        var parameters = computeMethods[0].GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(OperationsInputs), parameters[0].ParameterType);
    }

    [Fact]
    public void OperationsResult_ExposesNoLoanInterestOrDepreciationMember()
    {
        // R7.5: The result carries only Wages/PayrollTax/OperationsTotal. Loan
        // interest and depreciation belong to later passes and must not appear
        // on this record.
        var propertyNames = typeof(OperationsResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain(
            propertyNames,
            n => n.Contains("Interest", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            propertyNames,
            n => n.Contains("Depreciation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OperationsCalculator_OperationsTotal_EqualsExactLineSumPlusPayrollTax()
    {
        // Concrete, hand-transcribed check: with these inputs, the only
        // arithmetically valid result is the sum below. If the calculator
        // were secretly adding loan interest or depreciation, this equality
        // would fail (there are no other inputs from which to draw them).
        const decimal wages = 2_000m;
        var inputs = MakeConstantInputs(
            accounting: 100m,
            custodial: 200m,
            gas: 50m,
            insurance: 400m,
            it: 300m,
            officeSupplies: 75m,
            professionalServices: 500m,
            rentExpense: 1_200m,
            repairs: 150m,
            shipping: 25m,
            propertyTax: 600m,
            utilities: 350m,
            wages: wages,
            otherOperations: 80m);

        const decimal expectedLineSum = 100m + 200m + 50m + 400m + 300m + 75m + 500m
            + 1_200m + 150m + 25m + 600m + 350m + wages + 80m;
        var expectedPayrollTax = wages * PayrollTaxRate;
        var expectedTotal = expectedLineSum + expectedPayrollTax;

        var result = OperationsCalculator.Compute(inputs);

        Assert.All(result.OperationsTotal, total => Assert.Equal(expectedTotal, total));
    }
}
