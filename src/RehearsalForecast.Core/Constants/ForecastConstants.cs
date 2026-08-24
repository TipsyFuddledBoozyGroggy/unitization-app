namespace RehearsalForecast.Core.Constants;

/// <summary>
/// Fixed numeric constants used across the rehearsal-forecast calculation engine.
/// Every value here is <see cref="decimal"/> unless it represents a pure count, in which
/// case it is <see cref="int"/>. No <c>double</c> or <c>float</c> is permitted.
/// </summary>
/// <remarks>
/// The literal <c>150m</c> appears only in <see cref="StandardUnitSize"/>. All calculation
/// code must reference the named constant rather than repeating the literal.
/// </remarks>
public static class ForecastConstants
{
    /// <summary>Floor area of one rental unit in square feet, fixed for this phase. Requirement 3.4.</summary>
    public const decimal StandardUnitSize = 150m;

    /// <summary>Derived payroll-tax rate applied to Wages. Requirement 7.2.</summary>
    public const decimal PayrollTaxRate = 0.0765m;

    /// <summary>Two decimal places (USD cents). Requirement 15.8.</summary>
    public const int CurrencyDecimals = 2;

    /// <summary>Smallest positive step in USD cents: 0.01. Requirement 15.10.</summary>
    public const decimal CurrencyPrecision = 0.01m;

    /// <summary>Convergence tolerance for the target-price binary search (USD). Requirement 15.6.</summary>
    public const decimal SolverTolerance = 0.0001m;

    /// <summary>Maximum solver iterations before returning a failure. Requirement 15.11.</summary>
    public const int SolverSafetyLimit = 200;

    /// <summary>Forecast horizon in months.</summary>
    public const int ForecastMonths = 36;
}
