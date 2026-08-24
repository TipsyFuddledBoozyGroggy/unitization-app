using System.Globalization;
using System.Text;
using RehearsalForecast.Core.Forecast;

namespace RehearsalForecast.Core.Export;

/// <summary>
/// Serializes a <see cref="ForecastResult"/> to a deterministic UTF-8 CSV
/// document following design §12 and Requirement 18.
/// </summary>
/// <remarks>
/// <para>
/// The output is one header row (design §12.1) plus exactly 36 data rows
/// (Requirement 18.1) terminated by <c>\r\n</c> (design §12.3). Every numeric
/// field is formatted with <see cref="CultureInfo.InvariantCulture"/> using
/// <c>"0.############"</c> (design §12.2, Requirement 18.5). The final column
/// is <c>Flat_Price_Per_Sqft</c>, repeated verbatim on every data row
/// (Requirement 18.3, design §12.1).
/// </para>
/// <para>
/// RFC 4180 quoting is applied via <see cref="EscapeField(string)"/> and
/// defensive formula-injection prevention via
/// <see cref="PrefixIfFormula(string)"/>. The current row schema exports only
/// numeric decimals plus the <c>Yes</c>/<c>No</c> <c>Cash_Positive_Status</c>
/// enum-like value, so neither helper fires in ordinary output; both remain
/// available for future additions and are exercised directly by the test
/// suite (design §12.4, §12.5).
/// </para>
/// <para>
/// The exporter performs no I/O and no persistence (Requirement 18.8) — it
/// consumes a fully-computed <see cref="ForecastResult"/> and returns a fresh
/// <see cref="byte"/> array. Output is byte-for-byte deterministic for the
/// same input (Requirement 18.9): no timestamps, GUIDs, culture-dependent
/// number formats, or iteration-order-dependent data structures appear in
/// the body.
/// </para>
/// </remarks>
public sealed class CsvExporter : ICsvExporter
{
    /// <summary>
    /// Line terminator per RFC 4180 and design §12.3. A trailing terminator
    /// after the final record keeps the row count stable when consumers
    /// split by <c>\r\n</c> and preserves byte-for-byte determinism.
    /// </summary>
    private const string LineTerminator = "\r\n";

    /// <summary>
    /// Invariant-culture decimal format per design §12.2: period decimal
    /// separator, no thousands separator, no trailing zeros beyond those
    /// present. Applied uniformly to every numeric cell (both integer counts
    /// and monetary decimals).
    /// </summary>
    private const string DecimalFormat = "0.############";

    /// <summary>
    /// Fixed 30-column header, verbatim from design §12.1. Column names and
    /// order are stable across forecasts of equivalent structure
    /// (Requirement 18.2).
    /// </summary>
    private static readonly string[] HeaderColumns =
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

    /// <inheritdoc />
    public byte[] Export(ForecastResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var sb = new StringBuilder(capacity: 4096);

        // Header row (design §12.1). None of the column names contain
        // commas, quotes, or line breaks so EscapeField is a no-op here, but
        // we route them through it for uniformity and future-proofing.
        for (var i = 0; i < HeaderColumns.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append(EscapeField(HeaderColumns[i]));
        }
        sb.Append(LineTerminator);

        // 36 data rows. Flat_Price_Per_Sqft is emitted as the final column
        // on every row (Requirement 18.3, design §12.1).
        var flatPrice = FormatDecimal(result.FlatPricePerSqft);
        foreach (var row in result.Rows)
        {
            AppendRow(sb, row, flatPrice);
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <inheritdoc />
    public string FileName(DateTimeOffset now) =>
        $"rehearsal-forecast-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.csv";

    // -----------------------------------------------------------------
    // Row emission
    // -----------------------------------------------------------------

    /// <summary>
    /// Appends a single data row to <paramref name="sb"/>, in the fixed
    /// column order defined by design §12.1, followed by
    /// <paramref name="flatPriceCell"/> as the trailing
    /// <c>Flat_Price_Per_Sqft</c> column and the <c>\r\n</c> terminator.
    /// </summary>
    private static void AppendRow(StringBuilder sb, MonthlyForecastRow row, string flatPriceCell)
    {
        AppendField(sb, FormatInt(row.Month), first: true);
        AppendField(sb, FormatDecimal(row.OccupancyRate));
        AppendField(sb, FormatInt(row.TotalRentalUnits));
        AppendField(sb, FormatInt(row.RentedUnits));
        AppendField(sb, FormatDecimal(row.RentedSqft));
        AppendField(sb, FormatDecimal(row.MonthlyPricePerSqft));
        AppendField(sb, FormatDecimal(row.GrossRevenue));
        AppendField(sb, FormatDecimal(row.GrossIncome));
        AppendField(sb, FormatDecimal(row.MarketingTotal));
        AppendField(sb, FormatDecimal(row.OperationsTotal));
        AppendField(sb, FormatDecimal(row.Wages));
        AppendField(sb, FormatDecimal(row.PayrollTax));
        AppendField(sb, FormatDecimal(row.LoanBeginningBalance));
        AppendField(sb, FormatDecimal(row.MonthlyLoanPayment));
        AppendField(sb, FormatDecimal(row.MonthlyLoanInterest));
        AppendField(sb, FormatDecimal(row.MonthlyLoanPrincipal));
        AppendField(sb, FormatDecimal(row.LoanEndingBalance));
        AppendField(sb, FormatDecimal(row.MonthlyDepreciation));
        AppendField(sb, FormatDecimal(row.PreTaxIncome));
        AppendField(sb, FormatDecimal(row.IncomeTax));
        AppendField(sb, FormatDecimal(row.TotalExpenses));
        AppendField(sb, FormatDecimal(row.NetIncome));
        AppendField(sb, FormatDecimal(row.BeginningCash));
        AppendField(sb, FormatDecimal(row.OwnerInvestmentInMonth));
        AppendField(sb, FormatDecimal(row.LoanProceedsInMonth));
        AppendField(sb, FormatDecimal(row.CapitalExpendituresInMonth));
        AppendField(sb, FormatDecimal(row.OwnerWithdrawals));
        AppendField(sb, FormatDecimal(row.EndingCash));
        AppendField(sb, row.CashPositiveStatus ? "Yes" : "No");
        AppendField(sb, flatPriceCell);

        sb.Append(LineTerminator);
    }

    /// <summary>
    /// Writes one already-formatted cell to <paramref name="sb"/>, prefixed
    /// with a comma except for the first cell in a row.
    /// </summary>
    /// <remarks>
    /// Cells passed here have already been safely rendered by
    /// <see cref="FormatInt(int)"/>, <see cref="FormatDecimal(decimal)"/>,
    /// or a fixed literal (<c>"Yes"</c>/<c>"No"</c>). None can contain a
    /// comma, quote, or line terminator, so <see cref="EscapeField(string)"/>
    /// would be a no-op; we skip the call to keep hot-path output
    /// allocation-free.
    /// </remarks>
    private static void AppendField(StringBuilder sb, string value, bool first = false)
    {
        if (!first)
        {
            sb.Append(',');
        }
        sb.Append(value);
    }

    // -----------------------------------------------------------------
    // Numeric formatting (design §12.2, Requirement 18.5)
    // -----------------------------------------------------------------

    /// <summary>
    /// Formats an integer cell (<c>Month</c>, <c>Total_Rental_Units</c>,
    /// <c>Rented_Units</c>) using the same invariant-culture format string as
    /// decimals so all numeric cells share one representation contract.
    /// Integers render with no decimal point.
    /// </summary>
    private static string FormatInt(int value) =>
        value.ToString(DecimalFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a <see cref="decimal"/> cell with
    /// <see cref="CultureInfo.InvariantCulture"/> using
    /// <c>"0.############"</c> (design §12.2): period decimal separator, no
    /// thousands separator, no trailing zeros beyond those present.
    /// </summary>
    private static string FormatDecimal(decimal value) =>
        value.ToString(DecimalFormat, CultureInfo.InvariantCulture);

    // -----------------------------------------------------------------
    // Formula-injection defence (Requirement 18.6, design §12.4)
    // -----------------------------------------------------------------

    /// <summary>
    /// Prefixes <paramref name="text"/> with a single apostrophe when it
    /// begins with a character that spreadsheet applications treat as a
    /// formula lead-in: <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, TAB
    /// (<c>\t</c>), or CR (<c>\r</c>). Returns the input unchanged
    /// otherwise, including when <paramref name="text"/> is empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The current row schema exports only numeric decimals plus the
    /// <c>Yes</c>/<c>No</c> <c>Cash_Positive_Status</c> value, none of which
    /// can trigger this helper. The utility exists so future additions
    /// (labels, tags, notes) inherit the defence without a policy change
    /// (design §12.4).
    /// </para>
    /// <para>
    /// Only the first character is inspected; a dangerous character elsewhere
    /// in the string is not a formula-injection risk because spreadsheets
    /// only evaluate cells whose first character is a formula lead-in.
    /// </para>
    /// </remarks>
    internal static string PrefixIfFormula(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var first = text[0];
        return first switch
        {
            '=' or '+' or '-' or '@' or '\t' or '\r' => "'" + text,
            _ => text,
        };
    }

    // -----------------------------------------------------------------
    // RFC 4180 escaping (Requirement 18.4, design §12.3)
    // -----------------------------------------------------------------

    /// <summary>
    /// Applies RFC 4180 quoting to <paramref name="text"/>: if it contains a
    /// comma, double quote, CR, or LF, wraps the field in double quotes and
    /// doubles every internal double quote; otherwise returns the input
    /// unchanged. Empty input is returned as-is.
    /// </summary>
    /// <remarks>
    /// This helper is exercised directly by the test suite because the
    /// current row schema does not produce cells that require escaping.
    /// </remarks>
    internal static string EscapeField(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var needsQuoting = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == ',' || c == '"' || c == '\r' || c == '\n')
            {
                needsQuoting = true;
                break;
            }
        }

        if (!needsQuoting)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 2);
        sb.Append('"');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                sb.Append('"').Append('"');
            }
            else
            {
                sb.Append(c);
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
