namespace RehearsalForecast.Core.Domain;

/// <summary>
/// The four one-time capital line items whose sum defines
/// <c>Total_Capital</c>. All amounts are recorded as capital expenditures in
/// Month 1 (Requirements 9.1, 9.2).
/// </summary>
/// <param name="Equipment">Capital spend on equipment. Nonnegative amount in USD (Requirement 9.1).</param>
/// <param name="TotalImprovementCost">Capital spend on building improvements. Nonnegative amount in USD (Requirement 9.1).</param>
/// <param name="BuildingPurchaseCost">Capital spend on the building purchase itself. Nonnegative amount in USD (Requirement 9.1).</param>
/// <param name="OtherCapitalCost">Capital spend that does not belong to any other line. Nonnegative amount in USD (Requirement 9.1).</param>
public sealed record CapitalInputs(
    decimal Equipment,
    decimal TotalImprovementCost,
    decimal BuildingPurchaseCost,
    decimal OtherCapitalCost);
