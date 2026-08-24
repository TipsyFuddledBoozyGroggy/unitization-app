namespace RehearsalForecast.Core.Domain;

/// <summary>
/// Owner-initiated capital movements. <see cref="OwnerWithdrawals"/> is a single
/// scalar applied to every month, NOT a <c>MonthlySchedule</c> (Requirement 1.6,
/// Design Decision 8).
/// </summary>
/// <param name="OwnerInvestment">
/// Owner's cash contribution received in Month 1 only (Requirement 10.3).
/// Nonnegative amount in USD. May exceed <c>Total_Capital</c> without invalidating
/// the submission (Requirement 10.5).
/// </param>
/// <param name="OwnerWithdrawals">
/// Constant per-month owner withdrawal, applied uniformly Month 1 through Month 36
/// (Requirements 1.6, 13.6). Nonnegative amount in USD.
/// </param>
public sealed record OwnerActivityInputs(
    decimal OwnerInvestment,
    decimal OwnerWithdrawals);
