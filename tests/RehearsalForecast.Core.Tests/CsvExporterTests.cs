// CsvExporterTests — CSV export (Requirement 18, design §12, §15.3 → CsvExporterTests).
//
// These tests are written tests-first against the intended CsvExporter that
// task 42 (Phase Q) will introduce. The public API contract is fixed by
// design §4.5:
//
//     namespace RehearsalForecast.Core.Export;
//
//     public interface ICsvExporter
//     {
//         byte[] Export(ForecastResult result);
//         string FileName(DateTimeOffset now);
//     }
//
//     public sealed class CsvExporter : ICsvExporter { ... }
//
// Design §12.4 documents a "defensive formula-injection prefix helper" applied
// to any user-controlled text cell whose first character is '=', '+', '-',
// '@', '\t', or '\r', and design §12.3 documents RFC 4180 quoting/escape
// rules. Because the row schema in design §12.1 currently contains only
// numeric decimals plus a Yes/No status flag, neither helper is exercised by
// ordinary CSV output. The tests therefore call the helpers directly under
// the following assumed internal API — matching the `internal static` pattern
// already used by BuildingGeometryCalculator, CapitalCalculator, etc.:
//
//     namespace RehearsalForecast.Core.Export;
//
//     public sealed class CsvExporter : ICsvExporter
//     {
//         internal static string PrefixIfFormula(string text);
//         internal static string EscapeField(string text);
//     }
//
// `InternalsVisibleTo` on the Core csproj already exposes these to this test
// project. If task 42 chooses to house these helpers on a separate internal
// utility class (e.g. `CsvFieldFormatter`) or under different names, only the
// helper-focused test-method call sites need to change; the arithmetic /
// structural assertions in the other tests remain unchanged.
//
// FSNM note (Requirement 14.5, task-41 coverage bullet):
//   Design §12.1 lists the CSV row columns explicitly and
//   First_Sustained_Nonnegative_Month is NOT among them — FSNM is a summary
//   metric on ForecastResult, not a per-row column. Requirement 14.5's
//   "CSV renders FSNM as None" clause is therefore satisfied vacuously by
//   the row-only schema. The FSNM test in this file asserts the design's
//   structural stance: mutating FSNM on the ForecastResult (null vs. an int
//   value) must not change the exported bytes, because FSNM is not part of
//   the row schema.
//
// Validates:
//   * Requirement 18.1 — exactly 37 records (1 header + 36 data rows).
//   * Requirement 18.2 — stable header column names and order per design §12.1.
//   * Requirement 18.3 — Flat_Price_Per_Sqft emitted as a repeated column
//                        value on every row.
//   * Requirement 18.4 — RFC 4180 quoting for ',', '"', CR, LF with doubled
//                        internal quotes.
//   * Requirement 18.5 — CultureInfo.InvariantCulture numeric formatting
//                        (period decimal separator, no thousands separator).
//   * Requirement 18.6 — formula-injection prefix for user-controlled text
//                        starting with '=', '+', '-', '@', '\t', or '\r'.
//   * Requirement 18.9 — determinism: repeated calls produce byte-for-byte
//                        identical output for the same input.
//   * Requirement 22.2 — test names identify the business rule under test.
//   * Design §12.5   — Cash_Positive_Status renders as "Yes" / "No".

using System.Globalization;
using System.Text;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Export;
using RehearsalForecast.Core.Forecast;
using RehearsalForecast.Core.Loan;
using RehearsalForecast.Core.Schedules;
using Xunit;

namespace RehearsalForecast.Core.Tests;

public class CsvExporterTests
{
    // ---------------------------------------------------------------------
    // Fixed CSV schema per design §12.1 — 30 columns in this order, with
    // Flat_Price_Per_Sqft appended as a repeated column value on every row
    // (Requirement 18.3).
    // ---------------------------------------------------------------------

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

    // Zero-based column indices for tests that inspect specific columns.
    private const int CashPositiveStatusColumn = 28;
    private const int FlatPricePerSqftColumn = 29;

    // ---------------------------------------------------------------------
    // Requirement 18.1 — exactly 37 records: one header + 36 data rows.
    // ---------------------------------------------------------------------

    [Fact]
    public void Export_Produces_Exactly_37_Records_One_Header_And_36_Data_Rows()
    {
        var exporter = new CsvExporter();
        var result = MakeSampleResult();

        var records = SplitRecords(exporter.Export(result));

        Assert.Equal(37, records.Count);
    }

    // ---------------------------------------------------------------------
    // Requirement 18.2 / 18.3 — header column names and order match the
    // fixed schema in design §12.1 (Flat_Price_Per_Sqft as the final column).
    // ---------------------------------------------------------------------

    [Fact]
    public void Export_Header_Column_Names_And_Order_Match_Design_12_1_Schema()
    {
        var exporter = new CsvExporter();
        var result = MakeSampleResult();

        var records = SplitRecords(exporter.Export(result));
        var headerFields = records[0].Split(',');

        Assert.Equal(ExpectedHeaders.Length, headerFields.Length);
        for (var i = 0; i < ExpectedHeaders.Length; i++)
        {
            Assert.Equal(ExpectedHeaders[i], headerFields[i]);
        }
    }

    // ---------------------------------------------------------------------
    // Requirement 18.2 — header stability across forecasts with equivalent
    // structure (i.e. header must not be derived from the runtime result).
    // ---------------------------------------------------------------------

    [Fact]
    public void Export_Header_Is_Identical_Across_Different_Forecasts()
    {
        var exporter = new CsvExporter();
        var firstResult = MakeSampleResult(flatPricePerSqft: 0.24m);
        var secondResult = MakeSampleResult(flatPricePerSqft: 3.50m);

        var firstHeader = SplitRecords(exporter.Export(firstResult))[0];
        var secondHeader = SplitRecords(exporter.Export(secondResult))[0];

        Assert.Equal(firstHeader, secondHeader);
    }

    // ---------------------------------------------------------------------
    // Requirement 18.1 (again) — each data row emits exactly the same number
    // of columns as the header, so no row is truncated or padded.
    // ---------------------------------------------------------------------

    [Fact]
    public void Export_Every_Data_Row_Has_Same_Column_Count_As_Header()
    {
        var exporter = new CsvExporter();
        var result = MakeSampleResult();

        var records = SplitRecords(exporter.Export(result));

        for (var i = 1; i < records.Count; i++)
        {
            var fields = records[i].Split(',');
            Assert.Equal(ExpectedHeaders.Length, fields.Length);
        }
    }

    // ---------------------------------------------------------------------
    // Requirement 18.5 — every numeric field uses CultureInfo.InvariantCulture:
    // period as the decimal separator, no thousands separator.
    //
    // A magnitude ≥ 1000 is chosen so that any incorrect thousands-grouping
    // culture (e.g. en-US "1,234.5") would produce an unparseable field
    // once we split by comma.
    // ---------------------------------------------------------------------

    [Fact]
    public void Export_Uses_Invariant_Culture_Numeric_Formatting_With_Period_Decimal_And_No_Thousands_Separator()
    {
        var exporter = new CsvExporter();
        var result = MakeSampleResult(flatPricePerSqft: 1234.5m);

        var records = SplitRecords(exporter.Export(result));

        for (var row = 1; row < records.Count; row++)
        {
            var fields = records[row].Split(',');
            Assert.Equal(ExpectedHeaders.Length, fields.Length);

            for (var col = 0; col < fields.Length; col++)
            {
                if (col == CashPositiveStatusColumn)
                {
                    // Cash_Positive_Status is the sole non-numeric data field.
                    Assert.True(
                        fields[col] == "Yes" || fields[col] == "No",
                        $"Row {row} '{ExpectedHeaders[col]}' value '{fields[col]}' must be 'Yes' or 'No'.");
                    continue;
                }

                // Every remaining field is either an integer count (Month,
                // Total_Rental_Units, Rented_Units) or a decimal — both must
                // parse under InvariantCulture and never contain a
                // thousands-grouping comma (the split-by-comma above already
                // implies this structurally; the assertion is explicit for
                // clarity).
                Assert.True(
                    decimal.TryParse(fields[col], NumberStyles.Number, CultureInfo.InvariantCulture, out _),
                    $"Row {row} column '{ExpectedHeaders[col]}' value '{fields[col]}' must parse as decimal under InvariantCulture.");
                Assert.DoesNotContain(",", fields[col]);
            }

            // Flat_Price_Per_Sqft must render exactly the value we asked for,
            // using invariant-culture "0.############" (design §12.2). No
            // trailing zero, no thousands comma, period decimal.
            Assert.Equal("1234.5", fields[FlatPricePerSqftColumn]);
        }
    }

    // ---------------------------------------------------------------------
    // Requirement 18.4 — RFC 4180 quoting and doubled-quote escaping.
    //
    // The row schema in this phase contains only numeric decimals plus
    // "Yes"/"No", so ordinary CSV output cannot exercise the quoting path.
    // These tests exercise the escape helper directly via the internal API
    // assumed at the top of this file.
    // ---------------------------------------------------------------------

    [Fact]
    public void EscapeField_Wraps_Field_In_Quotes_When_It_Contains_A_Comma()
    {
        Assert.Equal("\"a,b\"", CsvExporter.EscapeField("a,b"));
    }

    [Fact]
    public void EscapeField_Wraps_Field_In_Quotes_And_Doubles_Internal_Quote()
    {
        // Per RFC 4180: `a"b` → `"a""b"`.
        Assert.Equal("\"a\"\"b\"", CsvExporter.EscapeField("a\"b"));
    }

    [Fact]
    public void EscapeField_Wraps_Field_In_Quotes_When_It_Contains_A_Carriage_Return()
    {
        Assert.Equal("\"a\rb\"", CsvExporter.EscapeField("a\rb"));
    }

    [Fact]
    public void EscapeField_Wraps_Field_In_Quotes_When_It_Contains_A_Line_Feed()
    {
        Assert.Equal("\"a\nb\"", CsvExporter.EscapeField("a\nb"));
    }

    [Fact]
    public void EscapeField_Doubles_Every_Internal_Quote_When_Field_Contains_Multiple_Quotes()
    {
        // `a"b"c` → `"a""b""c"`.
        Assert.Equal("\"a\"\"b\"\"c\"", CsvExporter.EscapeField("a\"b\"c"));
    }

    [Fact]
    public void EscapeField_Leaves_Plain_Field_Unquoted()
    {
        Assert.Equal("plain", CsvExporter.EscapeField("plain"));
    }

    [Fact]
    public void EscapeField_Leaves_Empty_Field_Unquoted()
    {
        Assert.Equal(string.Empty, CsvExporter.EscapeField(string.Empty));
    }

    // ---------------------------------------------------------------------
    // Requirement 18.6 — formula-injection prevention: user-controlled text
    // fields whose first character is '=', '+', '-', '@', '\t', or '\r'
    // must be prefixed with a single leading apostrophe (design §12.4).
    //
    // Exercised via the internal helper because the phase-1 row schema does
    // not export any user-controlled text.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("=1+1", "'=1+1")]           // '=' — Excel formula start.
    [InlineData("+SUM(A1:A2)", "'+SUM(A1:A2)")] // '+' — Excel formula start.
    [InlineData("-2+3", "'-2+3")]           // '-' — Excel formula start (also negative number).
    [InlineData("@cmd", "'@cmd")]           // '@' — Excel legacy function invocation.
    [InlineData("\tab", "'\tab")]           // '\t' — Excel accepts as formula prefix.
    [InlineData("\rreturn", "'\rreturn")]   // '\r' — Excel accepts as formula prefix.
    public void PrefixIfFormula_Prefixes_Apostrophe_When_Text_Starts_With_Dangerous_Character(
        string input,
        string expected)
    {
        Assert.Equal(expected, CsvExporter.PrefixIfFormula(input));
    }

    [Fact]
    public void PrefixIfFormula_Leaves_Ordinary_Text_Unchanged()
    {
        Assert.Equal("hello", CsvExporter.PrefixIfFormula("hello"));
    }

    [Fact]
    public void PrefixIfFormula_Leaves_Empty_Text_Unchanged()
    {
        // Nothing to prefix; the helper must be a no-op on empty input.
        Assert.Equal(string.Empty, CsvExporter.PrefixIfFormula(string.Empty));
    }

    [Fact]
    public void PrefixIfFormula_Leaves_Text_Containing_But_Not_Starting_With_Dangerous_Character_Unchanged()
    {
        // A dangerous character in the interior is not a formula-injection
        // risk in Excel/Sheets; only leading characters trigger evaluation.
        Assert.Equal("a=b", CsvExporter.PrefixIfFormula("a=b"));
        Assert.Equal("hello+world", CsvExporter.PrefixIfFormula("hello+world"));
    }

    // ---------------------------------------------------------------------
    // Requirement 18.9 — determinism: repeated calls produce byte-for-byte
    // identical output for the same input.
    // ---------------------------------------------------------------------

    [Fact]
    public void Export_Is_Deterministic_Producing_Byte_For_Byte_Identical_Output_Across_Repeated_Calls()
    {
        var exporter = new CsvExporter();
        var result = MakeSampleResult();

        var first = exporter.Export(result);
        var second = exporter.Export(result);
        var third = exporter.Export(result);

        Assert.Equal(first, second);
        Assert.Equal(second, third);
    }

    [Fact]
    public void Export_Is_Deterministic_Across_Two_Freshly_Constructed_Exporters()
    {
        // A second CsvExporter instance must produce the same bytes as the
        // first for the same ForecastResult: exporter identity must not
        // affect output (Requirement 18.9).
        var result = MakeSampleResult();

        var first = new CsvExporter().Export(result);
        var second = new CsvExporter().Export(result);

        Assert.Equal(first, second);
    }

    // ---------------------------------------------------------------------
    // Design §12.5 — Cash_Positive_Status renders as "Yes" when the row's
    // EndingCash is nonnegative and "No" when it is negative.
    //
    // Uses a hand-constructed ForecastResult to guarantee a mix of both
    // states independent of the calculator's behaviour.
    // ---------------------------------------------------------------------

    [Fact]
    public void Export_Renders_CashPositiveStatus_As_Yes_When_Row_Is_Nonnegative_And_No_When_Row_Is_Negative()
    {
        // Alternate positive/negative rows so we hit both mappings in a
        // single Export call. Month 6, 12, 18, 24, 30, 36 are negative;
        // all other months are nonnegative.
        var negativeMonths = new HashSet<int> { 6, 12, 18, 24, 30, 36 };

        var rows = new List<MonthlyForecastRow>(capacity: 36);
        for (var m = 1; m <= 36; m++)
        {
            var isPositive = !negativeMonths.Contains(m);
            rows.Add(MakeMinimalRow(
                month: m,
                endingCash: isPositive ? 100m : -100m,
                cashPositiveStatus: isPositive));
        }

        var result = MakeMinimalResult(rows);
        var exporter = new CsvExporter();

        var records = SplitRecords(exporter.Export(result));

        for (var m = 1; m <= 36; m++)
        {
            var fields = records[m].Split(',');
            var expected = negativeMonths.Contains(m) ? "No" : "Yes";
            Assert.Equal(expected, fields[CashPositiveStatusColumn]);
        }
    }

    // ---------------------------------------------------------------------
    // Requirement 18.3 — Flat_Price_Per_Sqft is emitted as a repeated column
    // value on every row (design §12.1: last column). Every data row must
    // carry the same value that the ForecastResult was computed with.
    // ---------------------------------------------------------------------

    [Fact]
    public void Export_Emits_FlatPricePerSqft_As_Repeated_Column_Value_On_Every_Row()
    {
        const decimal flatPrice = 4.25m;
        var exporter = new CsvExporter();
        var result = MakeSampleResult(flatPricePerSqft: flatPrice);

        var records = SplitRecords(exporter.Export(result));

        for (var m = 1; m <= 36; m++)
        {
            var fields = records[m].Split(',');
            var flatPriceField = fields[FlatPricePerSqftColumn];

            Assert.True(
                decimal.TryParse(flatPriceField, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed),
                $"Month {m}: Flat_Price_Per_Sqft field '{flatPriceField}' must parse under InvariantCulture.");
            Assert.Equal(flatPrice, parsed);
        }
    }

    [Fact]
    public void Export_FlatPricePerSqft_Repeated_Value_Is_Identical_On_Every_Row()
    {
        // Cross-check the "same value every row" claim by taking the raw
        // string in row 1 and asserting bytewise equality against every
        // other row's Flat_Price_Per_Sqft field. This catches subtle
        // formatting inconsistencies (e.g., differing trailing zeros) that
        // the numeric-parse test above would otherwise mask.
        var exporter = new CsvExporter();
        var result = MakeSampleResult(flatPricePerSqft: 0.24m);

        var records = SplitRecords(exporter.Export(result));
        var firstRowFlatPrice = records[1].Split(',')[FlatPricePerSqftColumn];

        for (var m = 2; m <= 36; m++)
        {
            var rowFlatPrice = records[m].Split(',')[FlatPricePerSqftColumn];
            Assert.Equal(firstRowFlatPrice, rowFlatPrice);
        }
    }

    // ---------------------------------------------------------------------
    // Requirement 14.5 (CSV facet, per file-header FSNM note) —
    // First_Sustained_Nonnegative_Month is NOT part of the design-§12.1
    // row schema, so mutating it on the ForecastResult must not change the
    // exported bytes. This documents the design's structural stance that
    // FSNM is a summary metric rendered by the Web UI, not by the CSV
    // per-row body.
    // ---------------------------------------------------------------------

    [Fact]
    public void Export_Is_Invariant_To_FSNM_Because_FSNM_Is_Not_In_The_Design_12_1_Row_Schema()
    {
        var withNull = MakeSampleResult(firstSustainedNonnegativeMonth: null);
        var withValue = MakeSampleResult(firstSustainedNonnegativeMonth: 12);
        var exporter = new CsvExporter();

        // Same row data + different FSNM sentinel ⇒ identical bytes because
        // FSNM is not one of the 30 columns in design §12.1.
        Assert.Equal(exporter.Export(withNull), exporter.Export(withValue));
    }

    // =====================================================================
    // Fixtures
    // =====================================================================

    /// <summary>
    /// Produces a realistic <see cref="ForecastResult"/> by running the full
    /// <see cref="ForecastCalculator"/> pipeline against a modest, hand-picked
    /// input set. Optional overrides let individual tests vary the flat price
    /// or the FSNM sentinel without disturbing the rest of the fixture.
    /// </summary>
    private static ForecastResult MakeSampleResult(
        decimal flatPricePerSqft = 0.24m,
        int? firstSustainedNonnegativeMonth = null)
    {
        var inputs = new ForecastInputs(
            Capital: new CapitalInputs(
                Equipment: 50_000m,
                TotalImprovementCost: 30_000m,
                BuildingPurchaseCost: 100_000m,
                OtherCapitalCost: 10_000m),
            Marketing: new MarketingInputs(
                Print: MonthlySchedule<decimal>.Constant(100m),
                Search: MonthlySchedule<decimal>.Constant(150m),
                Social: MonthlySchedule<decimal>.Constant(200m),
                OtherMarketing: MonthlySchedule<decimal>.Constant(50m)),
            Operations: new OperationsInputs(
                Accounting: MonthlySchedule<decimal>.Constant(200m),
                Custodial: MonthlySchedule<decimal>.Constant(300m),
                Gas: MonthlySchedule<decimal>.Constant(150m),
                Insurance: MonthlySchedule<decimal>.Constant(500m),
                It: MonthlySchedule<decimal>.Constant(100m),
                OfficeSupplies: MonthlySchedule<decimal>.Constant(50m),
                ProfessionalServices: MonthlySchedule<decimal>.Constant(100m),
                RentExpense: MonthlySchedule<decimal>.Constant(0m),
                Repairs: MonthlySchedule<decimal>.Constant(200m),
                Shipping: MonthlySchedule<decimal>.Constant(50m),
                PropertyTax: MonthlySchedule<decimal>.Constant(400m),
                Utilities: MonthlySchedule<decimal>.Constant(300m),
                Wages: MonthlySchedule<decimal>.Constant(3_000m),
                OtherOperations: MonthlySchedule<decimal>.Constant(100m)),
            Building: new BuildingInputs(
                TotalSqft: 10_000m,
                PercentageAvailableForRent: 0.80m,
                TotalBuildingCost: 100_000m,
                LandValue: 50_000m,
                DepreciationPeriodYears: 30,
                Occupancy: new OccupancySchedule(UseDefault: true, UserRates: null)),
            Loan: new LoanInputs(
                AnnualLoanInterestRate: 0.06m,
                LoanTermMonths: 60),
            Taxes: new TaxInputs(IncomeTaxRate: 0.25m),
            OwnerActivity: new OwnerActivityInputs(
                OwnerInvestment: 50_000m,
                OwnerWithdrawals: 500m),
            ForecastControls: new ForecastControlInputs(
                BeginningCashMonth1: 25_000m,
                TargetCashPositiveMonth: 24));

        var calculator = new ForecastCalculator(new LoanCalculator());
        var real = calculator.Compute(inputs, flatPricePerSqft);

        // Overlay caller-supplied summary sentinels while preserving the row
        // data produced by the calculator. Positional-record `with` uses
        // parameter names identical to the constructor parameters (design §5.5).
        return real with
        {
            FirstSustainedNonnegativeMonth = firstSustainedNonnegativeMonth,
        };
    }

    /// <summary>
    /// Builds a <see cref="MonthlyForecastRow"/> with the caller's month
    /// number, ending cash, and cash-positive status; every other numeric
    /// field is <c>0m</c>. Sufficient for tests that only care about
    /// <c>Cash_Positive_Status</c> rendering.
    /// </summary>
    private static MonthlyForecastRow MakeMinimalRow(
        int month,
        decimal endingCash,
        bool cashPositiveStatus) =>
        new(
            Month: month,
            OccupancyRate: 0m,
            TotalRentalUnits: 0,
            RentedUnits: 0,
            RentedSqft: 0m,
            MonthlyPricePerSqft: 0m,
            GrossRevenue: 0m,
            GrossIncome: 0m,
            MarketingTotal: 0m,
            OperationsTotal: 0m,
            Wages: 0m,
            PayrollTax: 0m,
            LoanBeginningBalance: 0m,
            MonthlyLoanPayment: 0m,
            MonthlyLoanInterest: 0m,
            MonthlyLoanPrincipal: 0m,
            LoanEndingBalance: 0m,
            MonthlyDepreciation: 0m,
            PreTaxIncome: 0m,
            IncomeTax: 0m,
            TotalExpenses: 0m,
            NetIncome: 0m,
            BeginningCash: 0m,
            OwnerInvestmentInMonth: 0m,
            LoanProceedsInMonth: 0m,
            CapitalExpendituresInMonth: 0m,
            OwnerWithdrawals: 0m,
            EndingCash: endingCash,
            CashPositiveStatus: cashPositiveStatus);

    /// <summary>
    /// Wraps a caller-supplied 36-row set into a fully-populated but
    /// otherwise-zero <see cref="ForecastResult"/>. Used by tests that
    /// bypass the calculator to isolate CSV-rendering behaviour.
    /// </summary>
    private static ForecastResult MakeMinimalResult(IReadOnlyList<MonthlyForecastRow> rows) =>
        new(
            TotalCapital: 0m,
            OwnerInvestment: 0m,
            LoanProceeds: 0m,
            RentableSqft: 0m,
            TotalRentalUnits: 0,
            FlatPricePerSqft: 0m,
            MonthlyPricePerSqft: 0m,
            TargetCashPositiveMonth: 24,
            CashPositiveRuleSatisfied: false,
            FirstSustainedNonnegativeMonth: null,
            Rows: rows);

    /// <summary>
    /// Splits the exporter's raw UTF-8 bytes into logical CSV records. The
    /// line terminator per design §12.3 is <c>\r\n</c>. A trailing empty
    /// record after the final <c>\r\n</c>, if present, is dropped so the
    /// caller can assert the record count directly (Requirement 18.1).
    /// </summary>
    private static IReadOnlyList<string> SplitRecords(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var lines = new List<string>(text.Split(new[] { "\r\n" }, StringSplitOptions.None));
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }
        return lines;
    }
}
