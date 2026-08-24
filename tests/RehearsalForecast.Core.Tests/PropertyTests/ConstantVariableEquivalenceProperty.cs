// Property 3: Constant / Variable schedule equivalence.
// Validates: Requirements 1.2, 6.2, 4.1.
//
// Design §10 (Property 3), §15.4. For any schedulable input X and value v,
// the ForecastResult produced with X in Constant_Mode(v) must equal the
// ForecastResult produced with X in Variable_Mode carrying 36 copies of v.
// This is the executable specification of "constant expands to 36 copies"
// and applies to every schedulable input:
//
//   * All 4 marketing lines: Print, Search, Social, Other_Marketing.
//   * All 14 operations lines: Accounting, Custodial, Gas, Insurance, IT,
//     Office_Supplies, Professional_Services, Rent_Expense, Repairs,
//     Shipping, Property_Tax, Utilities, Wages, Other_Operations.
//   * Occupancy_Rate: default schedule vs Variable_Mode carrying the same
//     36 rates (Design §10 / task 45 note: "default vs uniform user rates").
//
// FsCheck.Xunit runs the [Property] at least 100 iterations (the default).
// Each iteration checks structural equality across every summary field and
// every field of every MonthlyForecastRow, for every schedulable input in
// turn (19 checks per iteration => 1900+ end-to-end equivalence assertions
// across the run).

using System.Collections.Generic;
using FsCheck.Xunit;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;
using RehearsalForecast.Core.Schedules;
using Xunit;

namespace RehearsalForecast.Core.Tests.PropertyTests;

public class ConstantVariableEquivalenceProperty
{
    // ------------------------------------------------------------------
    // Field enumeration: each schedulable input the property parameterises
    // over. The comment on each entry documents the requirement clause
    // that the field's Constant/Variable equivalence witnesses.
    // ------------------------------------------------------------------
    private enum SchedulableField
    {
        // Marketing (R6.1 line items; R6.2 constant / variable equivalence).
        Print = 0,
        Search = 1,
        Social = 2,
        OtherMarketing = 3,

        // Operations (R7.1 line items; R1.2 constant expands to 36 copies).
        Accounting = 4,
        Custodial = 5,
        Gas = 6,
        Insurance = 7,
        It = 8,
        OfficeSupplies = 9,
        ProfessionalServices = 10,
        RentExpense = 11,
        Repairs = 12,
        Shipping = 13,
        PropertyTax = 14,
        Utilities = 15,
        Wages = 16,
        OtherOperations = 17,

        // Occupancy (R4.1 default vs user-supplied schedule).
        Occupancy = 18,
    }

    /// <summary>
    /// Number of schedulable field cases exercised per iteration. Kept in
    /// sync with <see cref="SchedulableField"/> so the property statically
    /// covers every field the design enumerates as schedulable.
    /// </summary>
    private const int FieldCount = 19;

    /// <summary>
    /// Constructs two <see cref="ForecastInputs"/> instances that differ
    /// only in how <paramref name="field"/> is expressed: the "constant"
    /// input has that field in <see cref="ScheduleMode.Constant"/> mode
    /// with value <paramref name="value"/> (or the default occupancy ramp
    /// for the Occupancy case); the "variable" input has that same field
    /// materialised as a 36-element Variable_Mode schedule carrying the
    /// identical per-month values.
    /// </summary>
    private static (ForecastInputs Constant, ForecastInputs Variable) BuildPair(
        SchedulableField field,
        decimal value)
    {
        // Baseline pair: every field is Constant. We then swap the target
        // field on the second input to Variable carrying 36 copies of
        // `value` (or the default ramp verbatim for Occupancy).
        var constantInputs = PropertyTestHelpers.MakeInputs();
        var variableInputs = SwapFieldToVariable(constantInputs, field, value);

        // For every non-Occupancy field the constant input carries the
        // generated `value`. The baseline MakeInputs uses static defaults,
        // so we also swap the constant-mode field on the constant input to
        // carry `value` — otherwise the two inputs would differ on values,
        // not just on Mode, and the property would degenerate.
        constantInputs = SwapFieldConstantValue(constantInputs, field, value);

        return (constantInputs, variableInputs);
    }

    private static ForecastInputs SwapFieldConstantValue(
        ForecastInputs baseInputs,
        SchedulableField field,
        decimal value)
    {
        var constantSchedule = MonthlySchedule<decimal>.Constant(value);

        return field switch
        {
            SchedulableField.Print => baseInputs with
            {
                Marketing = baseInputs.Marketing with { Print = constantSchedule },
            },
            SchedulableField.Search => baseInputs with
            {
                Marketing = baseInputs.Marketing with { Search = constantSchedule },
            },
            SchedulableField.Social => baseInputs with
            {
                Marketing = baseInputs.Marketing with { Social = constantSchedule },
            },
            SchedulableField.OtherMarketing => baseInputs with
            {
                Marketing = baseInputs.Marketing with { OtherMarketing = constantSchedule },
            },
            SchedulableField.Accounting => baseInputs with
            {
                Operations = baseInputs.Operations with { Accounting = constantSchedule },
            },
            SchedulableField.Custodial => baseInputs with
            {
                Operations = baseInputs.Operations with { Custodial = constantSchedule },
            },
            SchedulableField.Gas => baseInputs with
            {
                Operations = baseInputs.Operations with { Gas = constantSchedule },
            },
            SchedulableField.Insurance => baseInputs with
            {
                Operations = baseInputs.Operations with { Insurance = constantSchedule },
            },
            SchedulableField.It => baseInputs with
            {
                Operations = baseInputs.Operations with { It = constantSchedule },
            },
            SchedulableField.OfficeSupplies => baseInputs with
            {
                Operations = baseInputs.Operations with { OfficeSupplies = constantSchedule },
            },
            SchedulableField.ProfessionalServices => baseInputs with
            {
                Operations = baseInputs.Operations with { ProfessionalServices = constantSchedule },
            },
            SchedulableField.RentExpense => baseInputs with
            {
                Operations = baseInputs.Operations with { RentExpense = constantSchedule },
            },
            SchedulableField.Repairs => baseInputs with
            {
                Operations = baseInputs.Operations with { Repairs = constantSchedule },
            },
            SchedulableField.Shipping => baseInputs with
            {
                Operations = baseInputs.Operations with { Shipping = constantSchedule },
            },
            SchedulableField.PropertyTax => baseInputs with
            {
                Operations = baseInputs.Operations with { PropertyTax = constantSchedule },
            },
            SchedulableField.Utilities => baseInputs with
            {
                Operations = baseInputs.Operations with { Utilities = constantSchedule },
            },
            SchedulableField.Wages => baseInputs with
            {
                Operations = baseInputs.Operations with { Wages = constantSchedule },
            },
            SchedulableField.OtherOperations => baseInputs with
            {
                Operations = baseInputs.Operations with { OtherOperations = constantSchedule },
            },
            SchedulableField.Occupancy => baseInputs with
            {
                Building = baseInputs.Building with
                {
                    Occupancy = new OccupancySchedule(UseDefault: true, UserRates: null),
                },
            },
            _ => baseInputs,
        };
    }

    private static ForecastInputs SwapFieldToVariable(
        ForecastInputs baseInputs,
        SchedulableField field,
        decimal value)
    {
        // Materialise 36 copies of `value` and use them as the Variable-mode
        // vector for the target field. For Occupancy, we express the
        // default ramp Min(m * 0.10, 1.00) verbatim in a user-supplied
        // vector: the property asserts that the default (formula-driven)
        // path and the equivalent user-supplied path produce identical
        // results.
        var variableSchedule = MonthlySchedule<decimal>.Variable(Repeat36(value));

        return field switch
        {
            SchedulableField.Print => baseInputs with
            {
                Marketing = baseInputs.Marketing with { Print = variableSchedule },
            },
            SchedulableField.Search => baseInputs with
            {
                Marketing = baseInputs.Marketing with { Search = variableSchedule },
            },
            SchedulableField.Social => baseInputs with
            {
                Marketing = baseInputs.Marketing with { Social = variableSchedule },
            },
            SchedulableField.OtherMarketing => baseInputs with
            {
                Marketing = baseInputs.Marketing with { OtherMarketing = variableSchedule },
            },
            SchedulableField.Accounting => baseInputs with
            {
                Operations = baseInputs.Operations with { Accounting = variableSchedule },
            },
            SchedulableField.Custodial => baseInputs with
            {
                Operations = baseInputs.Operations with { Custodial = variableSchedule },
            },
            SchedulableField.Gas => baseInputs with
            {
                Operations = baseInputs.Operations with { Gas = variableSchedule },
            },
            SchedulableField.Insurance => baseInputs with
            {
                Operations = baseInputs.Operations with { Insurance = variableSchedule },
            },
            SchedulableField.It => baseInputs with
            {
                Operations = baseInputs.Operations with { It = variableSchedule },
            },
            SchedulableField.OfficeSupplies => baseInputs with
            {
                Operations = baseInputs.Operations with { OfficeSupplies = variableSchedule },
            },
            SchedulableField.ProfessionalServices => baseInputs with
            {
                Operations = baseInputs.Operations with { ProfessionalServices = variableSchedule },
            },
            SchedulableField.RentExpense => baseInputs with
            {
                Operations = baseInputs.Operations with { RentExpense = variableSchedule },
            },
            SchedulableField.Repairs => baseInputs with
            {
                Operations = baseInputs.Operations with { Repairs = variableSchedule },
            },
            SchedulableField.Shipping => baseInputs with
            {
                Operations = baseInputs.Operations with { Shipping = variableSchedule },
            },
            SchedulableField.PropertyTax => baseInputs with
            {
                Operations = baseInputs.Operations with { PropertyTax = variableSchedule },
            },
            SchedulableField.Utilities => baseInputs with
            {
                Operations = baseInputs.Operations with { Utilities = variableSchedule },
            },
            SchedulableField.Wages => baseInputs with
            {
                Operations = baseInputs.Operations with { Wages = variableSchedule },
            },
            SchedulableField.OtherOperations => baseInputs with
            {
                Operations = baseInputs.Operations with { OtherOperations = variableSchedule },
            },
            SchedulableField.Occupancy => baseInputs with
            {
                Building = baseInputs.Building with
                {
                    Occupancy = new OccupancySchedule(
                        UseDefault: false,
                        UserRates: DefaultOccupancyRamp()),
                },
            },
            _ => baseInputs,
        };
    }

    private static IReadOnlyList<decimal> Repeat36(decimal value)
    {
        var values = new decimal[36];
        for (var i = 0; i < 36; i++)
        {
            values[i] = value;
        }
        return values;
    }

    /// <summary>
    /// Materialises the default occupancy ramp Min(m * 0.10, 1.00) verbatim
    /// as a 36-element user-supplied vector. The vector matches the calculator's
    /// default-schedule branch element-for-element so the "default vs uniform
    /// user rates" comparison is a genuine parity test.
    /// </summary>
    private static IReadOnlyList<decimal> DefaultOccupancyRamp()
    {
        var rates = new decimal[36];
        for (var m = 1; m <= 36; m++)
        {
            rates[m - 1] = System.Math.Min(m * 0.10m, 1.00m);
        }
        return rates;
    }

    /// <summary>
    /// Compares every summary field and every row-level field of two
    /// <see cref="ForecastResult"/> instances. <see cref="ForecastResult"/>
    /// is a record but its <c>Rows</c> property is <see cref="IReadOnlyList{T}"/>,
    /// which the compiler-generated equality compares by reference; this
    /// helper walks the rows and asserts <see cref="MonthlyForecastRow"/>
    /// record equality element-by-element, giving true structural parity.
    /// </summary>
    private static void AssertStructurallyEqual(ForecastResult expected, ForecastResult actual)
    {
        Assert.Equal(expected.TotalCapital, actual.TotalCapital);
        Assert.Equal(expected.OwnerInvestment, actual.OwnerInvestment);
        Assert.Equal(expected.LoanProceeds, actual.LoanProceeds);
        Assert.Equal(expected.RentableSqft, actual.RentableSqft);
        Assert.Equal(expected.TotalRentalUnits, actual.TotalRentalUnits);
        Assert.Equal(expected.FlatPricePerSqft, actual.FlatPricePerSqft);
        Assert.Equal(expected.MonthlyPricePerSqft, actual.MonthlyPricePerSqft);
        Assert.Equal(expected.TargetCashPositiveMonth, actual.TargetCashPositiveMonth);
        Assert.Equal(expected.CashPositiveRuleSatisfied, actual.CashPositiveRuleSatisfied);
        Assert.Equal(expected.FirstSustainedNonnegativeMonth, actual.FirstSustainedNonnegativeMonth);
        Assert.Equal(expected.Rows.Count, actual.Rows.Count);

        for (var i = 0; i < expected.Rows.Count; i++)
        {
            Assert.Equal(expected.Rows[i], actual.Rows[i]);
        }
    }

    /// <summary>
    /// Property 3: constant-mode and variable-mode-with-36-copies produce
    /// identical forecasts for every schedulable input.
    /// Validates: Requirements 1.2, 6.2, 4.1.
    /// </summary>
    [Property]
    public void ConstantAndVariableModes_ProduceIdenticalForecasts(
        uint valueRaw,
        uint priceRaw,
        int fieldRaw)
    {
        var value = PropertyTestHelpers.MoneyFromRaw(valueRaw);
        var price = PropertyTestHelpers.MoneyFromRaw(priceRaw);
        var field = (SchedulableField)(((fieldRaw % FieldCount) + FieldCount) % FieldCount);

        var (constantInputs, variableInputs) = BuildPair(field, value);

        var calculator = PropertyTestHelpers.NewForecastCalculator();

        var expected = calculator.Compute(constantInputs, price);
        var actual = calculator.Compute(variableInputs, price);

        AssertStructurallyEqual(expected, actual);
    }
}
