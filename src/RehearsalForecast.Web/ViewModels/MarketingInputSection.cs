using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Web.ViewModels;

/// <summary>
/// View-model section for the four marketing line items (Requirement 6.1). Each
/// item is either a constant applied to every month (Requirement 1.2) or an
/// explicit 36-month schedule (Requirement 1.4).
/// </summary>
public sealed class MarketingInputSection
{
    /// <summary>Print marketing spend (Requirement 6.1).</summary>
    public MonthlyScheduleViewModel Print { get; set; } = new();

    /// <summary>Search advertising spend (Requirement 6.1).</summary>
    public MonthlyScheduleViewModel Search { get; set; } = new();

    /// <summary>Social media advertising spend (Requirement 6.1).</summary>
    public MonthlyScheduleViewModel Social { get; set; } = new();

    /// <summary>All other marketing spend not captured above (Requirement 6.1).</summary>
    public MonthlyScheduleViewModel OtherMarketing { get; set; } = new();

    /// <summary>Maps this section to the domain <see cref="MarketingInputs"/> record.</summary>
    public MarketingInputs ToDomain() =>
        new(Print.ToDomain(), Search.ToDomain(), Social.ToDomain(), OtherMarketing.ToDomain());
}
