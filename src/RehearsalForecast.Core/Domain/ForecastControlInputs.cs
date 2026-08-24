namespace RehearsalForecast.Core.Domain;

/// <summary>
/// Forecast-wide control values that shape the cash-flow roll-forward and the
/// cash-positive rule but are not themselves financial line items.
/// </summary>
/// <param name="BeginningCashMonth1">
/// Opening cash balance used as <c>Beginning_Cash[1]</c> in the cash-flow
/// roll-forward (Requirement 13.2). Nonnegative amount in USD.
/// </param>
/// <param name="TargetCashPositiveMonth">
/// Month the user must be cash-positive by, integer in the inclusive range
/// <c>[1, 36]</c> (Requirements 2.8, 14.1). Contract only, not enforced by the
/// type.
/// </param>
public sealed record ForecastControlInputs(
    decimal BeginningCashMonth1,
    int TargetCashPositiveMonth);
