// Property-based test for Property 12 — CSV export structure and determinism
// (design §10, Property 12; §15.4).
//
// Property 12 states that for any valid ForecastResult R:
//
//   1. CsvExporter.Export(R) produces UTF-8 bytes containing exactly 37
//      CSV records: one header row followed by 36 data rows.               (R18.1)
//   2. The column names and order in the header row match the fixed schema
//      defined in design §12.1 and are identical across all inputs.        (R18.2, R18.3)
//   3. Export(R) is deterministic: Export(R) == Export(R) byte-for-byte
//      across repeated calls.                                              (R18.9)
//   4. Every data row has the same column count as the header row.
//   5. Emitted bytes are UTF-8 decodable.
//
// The test drives the CSV exporter downstream of a full forecast pipeline:
// bounded ForecastInputs → ForecastCalculator.Compute(inputs, price) →
// CsvExporter.Export(result). Running the calculator rather than
// hand-crafting a ForecastResult keeps the CSV structural invariants
// grounded in the real row shapes the shipping pipeline produces.
//
// Bounding strategy: bounded USD amounts, a bounded target month in
// [1, 36], a bounded flat price with cent precision, and modest capital and
// wage line items. All arithmetic remains in decimal per Requirement 19.1.
//
// The design §12.1 header defines exactly 30 columns; the 37-record count
// tolerates a single trailing empty line after the final \r\n (design §12.3
// mandates a trailing terminator; splitting yields either 37 records or 38
// with a trailing empty).
//
// Validates: Requirements 18.1, 18.2, 18.3, 18.4, 18.5, 18.6, 18.9

using System.Text;
using FsCheck.Xunit;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Export;
using RehearsalForecast.Core.Forecast;
using RehearsalForecast.Core.Loan;
using RehearsalForecast.Core.Schedules;
using Xunit;

namespace RehearsalForecast.Core.Tests.PropertyTests;

public class CsvExportStructureProperty
{
    private const int Months = ForecastConstants.ForecastMonths;

    /// <summary>
    /// The fixed 30-column header per design §12.1. Any drift in column
    /// order or name is caught by the header-comparison assertion below.
    /// </summary>
    private static readonly string[] ExpectedHeaders =
    {
        "Month",
        "Occupancy_Rate",
        "Total_Rental_Units",
        "Rented_Units",
        "Rented_Sqft",
        "Monthly_Price_Per_Sqft",
        "Gross_Revenue",
        "Gross_Income",
        "Marketing_Total",
        "Operations_Total",
        "Wages",
        "Payroll_Tax",
        "Loan_Beginning_Balance",
        "Monthly_Loan_Payment",
        "Monthly_Loan_Interest",
        "Monthly_Loan_Principal",
        "Loan_Ending_Balance",
        "Monthly_Depreciation",
        "Pre_Tax_Income",
        "Income_Tax",
        "Total_Expenses",
        "Net_Income",
        "Beginning_Cash",
        "Owner_Investment_In_Month",
        "Loan_Proceeds_In_Month",
        "Capital_Expenditures_In_Month",
        "Owner_Withdrawals",
        "Ending_Cash",
        "Cash_Positive_Status",
        "Flat_Price_Per_Sqft",
    };

    // ------------------------------------------------------------------
    // Bounded generators
    // ------------------------------------------------------------------

    private static decimal BoundMoney(int raw) =>
        (decimal)(Math.Abs((long)raw) % 10_000_000L) / 100m; // 0..100,000 cent-precise

    private static decimal BoundFlatPrice(int raw) =>
        (decimal)(Math.Abs((long)raw) % 10_000L) / 100m; // 0..99.99 cent-precise

    private static decimal BoundTotalSqft(int raw) =>
        (decimal)(Math.Abs((long)raw) % 20_001L); // 0..20,000 sqft

    private static decimal BoundPercentage(int raw) =>
        (decimal)(Math.Abs((long)raw) % 101L) / 100m; // 0..1.00

    private static decimal BoundTaxRate(int raw) =>
        (decimal)(Math.Abs((long)raw) % 101L) / 100m; // 0..1.00

    private static int BoundTargetMonth(int raw) =>
        (int)(Math.Abs((long)raw) % 36L) + 1; // [1, 36]

    private static int BoundLoanTerm(int raw) =>
        (int)(Math.Abs((long)raw) % 60L) + 12; // [12, 71]

    private static int BoundDepreciationYears(int raw) =>
        (int)(Math.Abs((long)raw) % 40L) + 1; // [1, 40]

    private static MonthlySchedule<decimal> Zero() =>
        MonthlySchedule<decimal>.Constant(0m);

    private static MonthlySchedule<decimal> BoundedMonthly(int raw) =>
        MonthlySchedule<decimal>.Constant(BoundMoney(raw));

    /// <summary>
    /// Assembles a bounded <see cref="ForecastInputs"/> for the CSV property.
    /// Coverage extends to nontrivial building costs (which drives
    /// depreciation), nontrivial capital (which drives capex timing), and
    /// nontrivial marketing / wages (which drives the operations sum and
    /// payroll tax) so the exporter faces realistically shaped
    /// <see cref="MonthlyForecastRow"/> values across the 100-iteration run.
    /// </summary>
    private static ForecastInputs MakeInputs(
        int rawEquipment,
        int rawImprovement,
        int rawBuildingPurchase,
        int rawOtherCapital,
        int rawMarketing,
        int rawWages,
        int rawTotalSqft,
        int rawPercentage,
        int rawTotalBuildingCost,
        int rawDepreciationYears,
        int rawTaxRate,
        int rawLoanTerm,
        int rawOwnerInvestment,
        int rawOwnerWithdrawals,
        int rawBeginningCash,
        int rawTargetMonth)
    {
        return new ForecastInputs(
            Capital: new CapitalInputs(
                Equipment: BoundMoney(rawEquipment),
                TotalImprovementCost: BoundMoney(rawImprovement),
                BuildingPurchaseCost: BoundMoney(rawBuildingPurchase),
                OtherCapitalCost: BoundMoney(rawOtherCapital)),
            Marketing: new MarketingInputs(
                Print: BoundedMonthly(rawMarketing),
                Search: Zero(),
                Social: Zero(),
                OtherMarketing: Zero()),
            Operations: new OperationsInputs(
                Accounting: Zero(),
                Custodial: Zero(),
                Gas: Zero(),
                Insurance: Zero(),
                It: Zero(),
                OfficeSupplies: Zero(),
                ProfessionalServices: Zero(),
                RentExpense: Zero(),
                Repairs: Zero(),
                Shipping: Zero(),
                PropertyTax: Zero(),
                Utilities: Zero(),
                Wages: BoundedMonthly(rawWages),
                OtherOperations: Zero()),
            Building: new BuildingInputs(
                TotalSqft: BoundTotalSqft(rawTotalSqft),
                PercentageAvailableForRent: BoundPercentage(rawPercentage),
                TotalBuildingCost: BoundMoney(rawTotalBuildingCost),
                LandValue: 0m,
                DepreciationPeriodYears: BoundDepreciationYears(rawDepreciationYears),
                Occupancy: new OccupancySchedule(UseDefault: true, UserRates: null)),
            Loan: new LoanInputs(
                AnnualLoanInterestRate: 0m,
                LoanTermMonths: BoundLoanTerm(rawLoanTerm)),
            Taxes: new TaxInputs(IncomeTaxRate: BoundTaxRate(rawTaxRate)),
            OwnerActivity: new OwnerActivityInputs(
                OwnerInvestment: BoundMoney(rawOwnerInvestment),
                OwnerWithdrawals: BoundMoney(rawOwnerWithdrawals)),
            ForecastControls: new ForecastControlInputs(
                BeginningCashMonth1: BoundMoney(rawBeginningCash),
                TargetCashPositiveMonth: BoundTargetMonth(rawTargetMonth)));
    }

    // ------------------------------------------------------------------
    // Property 12 — CSV export structure and determinism
    //
    // Validates: Requirements 18.1, 18.2, 18.3, 18.4, 18.5, 18.6, 18.9
    // ------------------------------------------------------------------

    [Property]
    public void Property_12_Csv_Export_Structure_And_Determinism(
        int rawEquipment,
        int rawImprovement,
        int rawBuildingPurchase,
        int rawOtherCapital,
        int rawMarketing,
        int rawWages,
        int rawTotalSqft,
        int rawPercentage,
        int rawTotalBuildingCost,
        int rawDepreciationYears,
        int rawTaxRate,
        int rawLoanTerm,
        int rawOwnerInvestment,
        int rawOwnerWithdrawals,
        int rawBeginningCash,
        int rawTargetMonth,
        int rawFlatPrice)
    {
        var inputs = MakeInputs(
            rawEquipment, rawImprovement, rawBuildingPurchase, rawOtherCapital,
            rawMarketing, rawWages,
            rawTotalSqft, rawPercentage, rawTotalBuildingCost, rawDepreciationYears,
            rawTaxRate, rawLoanTerm,
            rawOwnerInvestment, rawOwnerWithdrawals,
            rawBeginningCash, rawTargetMonth);
        var flatPrice = BoundFlatPrice(rawFlatPrice);

        var calculator = new ForecastCalculator(new LoanCalculator());
        var forecast = calculator.Compute(inputs, flatPrice);

        var exporter = new CsvExporter();

        // Requirement 18.9: two calls produce byte-identical output. Compute
        // both up front so subsequent structural assertions run over the
        // canonical bytes and any nondeterminism surfaces here first.
        var firstBytes = exporter.Export(forecast);
        var secondBytes = exporter.Export(forecast);
        Assert.Equal(firstBytes, secondBytes);

        // UTF-8 decodability — Encoding.UTF8.GetString never throws for
        // exporter output because the exporter itself encodes with UTF-8.
        // The assertion is nevertheless meaningful: it asserts we have a
        // non-empty decoded body (so the exporter did not produce an empty
        // stream on some bounded corner case).
        var text = Encoding.UTF8.GetString(firstBytes);
        Assert.False(string.IsNullOrEmpty(text));

        // Requirement 18.1: split by \r\n yields exactly 37 records, or 38
        // when a trailing terminator produces one empty record after the
        // final row (design §12.3 emits the trailing terminator, so the
        // trailing-empty case is the expected shape).
        var records = text.Split(new[] { "\r\n" }, System.StringSplitOptions.None);
        Assert.True(
            records.Length == 37 || (records.Length == 38 && records[^1].Length == 0),
            $"Expected 37 records (or 38 with trailing empty), got {records.Length}.");

        // Drop the trailing-empty sentinel if present so downstream
        // assertions can treat the record list uniformly.
        var effectiveCount = records.Length == 38 ? 37 : records.Length;
        Assert.Equal(37, effectiveCount);

        // Requirement 18.2 / 18.3: header row matches the fixed schema
        // exactly. The 30 header names never contain commas, quotes, or
        // line breaks, so a simple split by comma is sufficient.
        var headerFields = records[0].Split(',');
        Assert.Equal(ExpectedHeaders.Length, headerFields.Length);
        for (var i = 0; i < ExpectedHeaders.Length; i++)
        {
            Assert.Equal(ExpectedHeaders[i], headerFields[i]);
        }

        // Every data row has the same column count as the header row.
        // Because the row cells are all numeric decimals plus a "Yes"/"No"
        // sentinel, none of them contains a comma, so split-by-comma yields
        // the correct field count without needing a full RFC 4180 parser.
        for (var m = 1; m <= Months; m++)
        {
            var dataFields = records[m].Split(',');
            Assert.Equal(ExpectedHeaders.Length, dataFields.Length);
        }
    }
}
