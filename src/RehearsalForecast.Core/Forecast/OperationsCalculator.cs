using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Core.Forecast;

/// <summary>
/// Pass 5 of the forecast pipeline (design §6.5).
/// </summary>
/// <remarks>
/// <para>
/// For each month <c>m</c> in <c>[1, 36]</c>, this pass materialises three per-month
/// quantities from <see cref="OperationsInputs"/>:
/// </para>
/// <list type="bullet">
///   <item>
///     <term><c>Wages[m]</c></term>
///     <description>Direct pass-through of <see cref="OperationsInputs.Wages"/>.</description>
///   </item>
///   <item>
///     <term><c>Payroll_Tax[m]</c></term>
///     <description>Derived as <c>Wages[m] × <see cref="ForecastConstants.PayrollTaxRate"/></c>
///     (Requirement 7.2). Users cannot supply this quantity — it is not a field of
///     <see cref="OperationsInputs"/> (Requirement 7.3).</description>
///   </item>
///   <item>
///     <term><c>Operations_Total[m]</c></term>
///     <description>Sum of the fourteen operational line items at month <c>m</c>
///     (Requirement 7.1, Requirement 7.4) plus the derived <c>Payroll_Tax[m]</c>.
///     <c>Wages</c> is one of the fourteen line items and is counted exactly once
///     in the line-item sum; its payroll tax is added on top.</description>
///   </item>
/// </list>
/// <para>
/// <c>Monthly_Loan_Interest</c> and <c>Monthly_Depreciation</c> are explicitly
/// excluded from <c>Operations_Total</c> (Requirement 7.5). Those figures are
/// produced by later passes (§6.6, §6.8) and combined with operations only inside
/// <c>Expenses_Before_Income_Tax</c> in Pass 9 (§6.9). This helper's API surface
/// admits no <c>LoanInputs</c> or depreciation parameter, structurally guaranteeing
/// the exclusion.
/// </para>
/// </remarks>
internal static class OperationsCalculator
{
    /// <summary>
    /// Computes per-month <c>Wages</c>, derived <c>Payroll_Tax</c>, and
    /// <c>Operations_Total</c> from <paramref name="inputs"/>.
    /// </summary>
    /// <param name="inputs">The fourteen operational line-item schedules.</param>
    /// <returns>An <see cref="OperationsResult"/> whose three lists each contain
    /// exactly <see cref="ForecastConstants.ForecastMonths"/> (36) values, ordered
    /// from Month 1 to Month 36.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is <see langword="null"/>.</exception>
    internal static OperationsResult Compute(OperationsInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var months = ForecastConstants.ForecastMonths;
        var wages = new decimal[months];
        var payrollTax = new decimal[months];
        var operationsTotal = new decimal[months];

        for (var m = 1; m <= months; m++)
        {
            var i = m - 1;

            var wagesM = inputs.Wages.At(m);
            var payrollTaxM = wagesM * ForecastConstants.PayrollTaxRate;

            // Sum of the fourteen operational line items at month m (R7.1, R7.4).
            // Wages is counted exactly once here; Payroll_Tax is added on top.
            var lineItemSum =
                inputs.Accounting.At(m)
                + inputs.Custodial.At(m)
                + inputs.Gas.At(m)
                + inputs.Insurance.At(m)
                + inputs.It.At(m)
                + inputs.OfficeSupplies.At(m)
                + inputs.ProfessionalServices.At(m)
                + inputs.RentExpense.At(m)
                + inputs.Repairs.At(m)
                + inputs.Shipping.At(m)
                + inputs.PropertyTax.At(m)
                + inputs.Utilities.At(m)
                + wagesM
                + inputs.OtherOperations.At(m);

            wages[i] = wagesM;
            payrollTax[i] = payrollTaxM;
            operationsTotal[i] = lineItemSum + payrollTaxM;
        }

        return new OperationsResult(wages, payrollTax, operationsTotal);
    }
}

/// <summary>
/// The three per-month vectors produced by <see cref="OperationsCalculator.Compute"/>
/// (design §6.5). Each list contains exactly
/// <see cref="ForecastConstants.ForecastMonths"/> (36) values indexed from Month 1
/// to Month 36 (i.e. <c>list[m - 1]</c>).
/// </summary>
/// <param name="Wages">Per-month wages, mirrored from
/// <see cref="OperationsInputs.Wages"/> (Requirement 7.1).</param>
/// <param name="PayrollTax">Per-month payroll tax, derived as
/// <c>Wages[m] × <see cref="ForecastConstants.PayrollTaxRate"/></c>
/// (Requirement 7.2).</param>
/// <param name="OperationsTotal">Per-month sum of the fourteen operational line
/// items plus <see cref="PayrollTax"/>; excludes loan interest and depreciation
/// (Requirements 7.4, 7.5).</param>
internal sealed record OperationsResult(
    IReadOnlyList<decimal> Wages,
    IReadOnlyList<decimal> PayrollTax,
    IReadOnlyList<decimal> OperationsTotal);
