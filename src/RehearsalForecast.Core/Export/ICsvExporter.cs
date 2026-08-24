using RehearsalForecast.Core.Forecast;

namespace RehearsalForecast.Core.Export;

/// <summary>
/// Serializes a completed <see cref="ForecastResult"/> to a deterministic CSV
/// byte stream and produces the download filename for HTTP responses.
/// </summary>
/// <remarks>
/// <para>
/// The exporter has no dependency on ASP.NET Core, Razor, or any UI abstraction
/// (Requirement 20.2) and does not persist to a database, disk, session, or
/// TempData (Requirement 18.8). The controller wraps the returned bytes in a
/// <c>FileContentResult</c> for streaming (design §12.6).
/// </para>
/// <para>
/// The CSV body is fully described by design §12: one header row plus exactly
/// 36 data rows (Requirement 18.1), fixed column order (Requirement 18.2),
/// <c>CultureInfo.InvariantCulture</c> numeric formatting (Requirement 18.5),
/// RFC 4180 quoting with doubled internal quotes (Requirement 18.4),
/// defensive formula-injection prefixing on user-controlled text
/// (Requirement 18.6), and byte-for-byte determinism across repeated exports
/// of the same input (Requirement 18.9).
/// </para>
/// </remarks>
public interface ICsvExporter
{
    /// <summary>
    /// Serializes <paramref name="result"/> to a UTF-8 encoded CSV document.
    /// </summary>
    /// <param name="result">
    /// The forecast to serialize. Must contain exactly 36
    /// <see cref="MonthlyForecastRow"/> entries in <see cref="ForecastResult.Rows"/>.
    /// </param>
    /// <returns>
    /// The full CSV document as a fresh <see cref="byte"/> array, terminated
    /// by <c>\r\n</c> after every record including the last (design §12.3).
    /// </returns>
    byte[] Export(ForecastResult result);

    /// <summary>
    /// Produces the download filename for a CSV export triggered at
    /// <paramref name="now"/>.
    /// </summary>
    /// <param name="now">The clock reading used to stamp the filename.</param>
    /// <returns>
    /// <c>rehearsal-forecast-{yyyyMMdd-HHmmss}.csv</c>, formatted in
    /// <see cref="System.Globalization.CultureInfo.InvariantCulture"/>
    /// (design §12.6).
    /// </returns>
    string FileName(System.DateTimeOffset now);
}
