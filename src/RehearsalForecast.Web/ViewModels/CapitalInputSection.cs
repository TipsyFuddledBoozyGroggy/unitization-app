using System.ComponentModel.DataAnnotations;
using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Web.ViewModels;

/// <summary>
/// View-model section for one-time capital line items (Requirement 9.1). All
/// amounts are nonnegative USD. The sum of the four fields is
/// <c>Total_Capital</c>, recorded as a Month-1 capital expenditure.
/// </summary>
public sealed class CapitalInputSection
{
    /// <summary>Capital spend on equipment (Requirement 9.1).</summary>
    [Display(Name = "Equipment")]
    [Range(0.0, double.MaxValue, ErrorMessage = "Equipment must be zero or greater.")]
    public decimal Equipment { get; set; }

    /// <summary>Capital spend on building improvements (Requirement 9.1).</summary>
    [Display(Name = "Total Improvement Cost")]
    [Range(0.0, double.MaxValue, ErrorMessage = "Total Improvement Cost must be zero or greater.")]
    public decimal TotalImprovementCost { get; set; }

    /// <summary>Capital spend on the building purchase (Requirement 9.1).</summary>
    [Display(Name = "Building Purchase Cost")]
    [Range(0.0, double.MaxValue, ErrorMessage = "Building Purchase Cost must be zero or greater.")]
    public decimal BuildingPurchaseCost { get; set; }

    /// <summary>Capital spend not captured by the other three fields (Requirement 9.1).</summary>
    [Display(Name = "Other Capital Cost")]
    [Range(0.0, double.MaxValue, ErrorMessage = "Other Capital Cost must be zero or greater.")]
    public decimal OtherCapitalCost { get; set; }

    /// <summary>Maps this section to the domain <see cref="CapitalInputs"/> record.</summary>
    public CapitalInputs ToDomain() =>
        new(Equipment, TotalImprovementCost, BuildingPurchaseCost, OtherCapitalCost);
}
