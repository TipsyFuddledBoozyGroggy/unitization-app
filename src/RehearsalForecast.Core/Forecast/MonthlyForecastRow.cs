namespace RehearsalForecast.Core.Forecast;

/// <summary>
/// A single row in the 36-month rehearsal forecast, corresponding to one calendar
/// month <c>m ∈ [1, 36]</c>. Fields cover the full complement required by the
/// results table (Requirement 16.5) and follow the calculation passes laid out in
/// design §6.1–§6.11.
/// </summary>
/// <remarks>
/// Every monetary and rate field is <see cref="decimal"/>. <see cref="Month"/>,
/// <see cref="TotalRentalUnits"/>, and <see cref="RentedUnits"/> are integer counts.
/// <see cref="CashPositiveStatus"/> reflects <c>EndingCash &gt;= 0</c> for the row
/// only; it is not the fleet-wide Cash_Positive_Rule (see <see cref="ForecastResult.CashPositiveRuleSatisfied"/>).
/// </remarks>
/// <param name="Month">1-based month index in <c>[1, 36]</c>.</param>
/// <param name="OccupancyRate">Fraction of the fleet occupied in this month, in <c>[0, 1]</c>.</param>
/// <param name="TotalRentalUnits">Rentable-unit capacity of the facility (constant across all 36 months).</param>
/// <param name="RentedUnits">Occupied units in the month, clamped to <c>[0, TotalRentalUnits]</c>.</param>
/// <param name="RentedSqft">Occupied rentable square footage, clamped so it never exceeds <c>Rentable_Sqft</c> (design §6.2).</param>
/// <param name="MonthlyPricePerSqft">Derived monthly rate: <c>Flat_Price_Per_Sqft / 36</c> (Requirement 5.1).</param>
/// <param name="GrossRevenue">Revenue for the month: <c>RentedSqft × MonthlyPricePerSqft</c>.</param>
/// <param name="GrossIncome">Equal to <see cref="GrossRevenue"/> for this phase (COGS is out of scope, design decision 6).</param>
/// <param name="MarketingTotal">Sum of the four marketing line items for the month (design §6.4).</param>
/// <param name="OperationsTotal">Sum of the 14 operations line items plus <see cref="PayrollTax"/> (design §6.5, Requirement 7.4). Excludes loan interest and depreciation.</param>
/// <param name="Wages">Payroll expense for the month, echoed here for the results table.</param>
/// <param name="PayrollTax">Derived payroll tax: <c>Wages × 0.0765</c> (Requirement 7.2).</param>
/// <param name="LoanBeginningBalance">Outstanding loan principal at the start of the month.</param>
/// <param name="MonthlyLoanPayment">Constant loan payment amount for the month; zero when the loan is paid off or Loan_Proceeds is zero.</param>
/// <param name="MonthlyLoanInterest">Interest portion of the month's loan payment.</param>
/// <param name="MonthlyLoanPrincipal">Principal portion of the month's loan payment.</param>
/// <param name="LoanEndingBalance">Outstanding loan principal at the end of the month.</param>
/// <param name="MonthlyDepreciation">Straight-line building depreciation for the month (constant across all 36 months, Requirement 8.1).</param>
/// <param name="PreTaxIncome">Gross_Income minus expenses before income tax (design §6.9).</param>
/// <param name="IncomeTax"><c>max(PreTaxIncome, 0) × Income_Tax_Rate</c>. No cross-month carryforward (Requirement 12.7).</param>
/// <param name="TotalExpenses">Expenses before income tax plus <see cref="IncomeTax"/> (design §6.9).</param>
/// <param name="NetIncome"><c>GrossIncome − TotalExpenses</c>.</param>
/// <param name="BeginningCash">Cash balance at the start of the month.</param>
/// <param name="OwnerInvestmentInMonth">Owner_Investment applied in this month; nonzero only in Month 1 (design §6.7).</param>
/// <param name="LoanProceedsInMonth">Loan_Proceeds applied in this month; nonzero only in Month 1 (design §6.7).</param>
/// <param name="CapitalExpendituresInMonth">Total_Capital deployed in this month; nonzero only in Month 1 (design §6.7).</param>
/// <param name="OwnerWithdrawals">Constant per-month owner draw, applied uniformly in every month (Requirement 13.6).</param>
/// <param name="EndingCash">Cash balance at the end of the month, per the full accounting identity in Requirement 13.4.</param>
/// <param name="CashPositiveStatus"><c>true</c> iff <see cref="EndingCash"/> is nonnegative for this row only.</param>
public sealed record MonthlyForecastRow(
    int Month,
    decimal OccupancyRate,
    int TotalRentalUnits,
    int RentedUnits,
    decimal RentedSqft,
    decimal MonthlyPricePerSqft,
    decimal GrossRevenue,
    decimal GrossIncome,
    decimal MarketingTotal,
    decimal OperationsTotal,
    decimal Wages,
    decimal PayrollTax,
    decimal LoanBeginningBalance,
    decimal MonthlyLoanPayment,
    decimal MonthlyLoanInterest,
    decimal MonthlyLoanPrincipal,
    decimal LoanEndingBalance,
    decimal MonthlyDepreciation,
    decimal PreTaxIncome,
    decimal IncomeTax,
    decimal TotalExpenses,
    decimal NetIncome,
    decimal BeginningCash,
    decimal OwnerInvestmentInMonth,
    decimal LoanProceedsInMonth,
    decimal CapitalExpendituresInMonth,
    decimal OwnerWithdrawals,
    decimal EndingCash,
    bool CashPositiveStatus);
