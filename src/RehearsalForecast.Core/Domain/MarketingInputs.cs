using RehearsalForecast.Core.Schedules;

namespace RehearsalForecast.Core.Domain;

/// <summary>
/// The four marketing line items (Requirement 6.1). Each item is either a single
/// constant value applied to every month (Requirement 1.2) or an explicit 36-month
/// schedule (Requirement 1.4). The per-month sum is <c>Marketing_Total[m]</c>
/// (Requirement 6.3).
/// </summary>
/// <param name="Print">Print marketing spend (Requirement 6.1).</param>
/// <param name="Search">Search advertising spend (Requirement 6.1).</param>
/// <param name="Social">Social media advertising spend (Requirement 6.1).</param>
/// <param name="OtherMarketing">All other marketing spend not captured above (Requirement 6.1).</param>
public sealed record MarketingInputs(
    MonthlySchedule<decimal> Print,
    MonthlySchedule<decimal> Search,
    MonthlySchedule<decimal> Social,
    MonthlySchedule<decimal> OtherMarketing);
