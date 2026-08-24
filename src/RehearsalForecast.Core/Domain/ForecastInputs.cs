namespace RehearsalForecast.Core.Domain;

/// <summary>
/// Aggregate of every user-supplied input the calculator needs to produce a
/// 36-month forecast. Grouped by the input sections in Requirement 17. The
/// authoritative price (<c>Flat_Price_Per_Sqft</c>) is NOT part of this record;
/// it is the solver's output and is passed to
/// <c>IForecastCalculator.Compute</c> separately.
/// </summary>
/// <param name="Capital">One-time capital line items whose sum is <c>Total_Capital</c> (Requirement 9.1).</param>
/// <param name="Marketing">The four marketing line items (Requirement 6.1).</param>
/// <param name="Operations">The fourteen operational line items (Requirement 7.1).</param>
/// <param name="Building">Building geometry, depreciable cost, and occupancy schedule (Requirements 3, 4, 8).</param>
/// <param name="Loan">Loan interest rate and term (Requirement 11).</param>
/// <param name="Taxes">Income tax rate (Requirement 12).</param>
/// <param name="OwnerActivity">Owner investment and constant per-month withdrawals (Requirements 1.6, 10).</param>
/// <param name="ForecastControls">Opening cash and target cash-positive month (Requirements 13.2, 14.1).</param>
public sealed record ForecastInputs(
    CapitalInputs Capital,
    MarketingInputs Marketing,
    OperationsInputs Operations,
    BuildingInputs Building,
    LoanInputs Loan,
    TaxInputs Taxes,
    OwnerActivityInputs OwnerActivity,
    ForecastControlInputs ForecastControls);
