using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Web.ViewModels;

/// <summary>
/// View-model section for the fourteen operational line items (Requirement 7.1).
/// <c>Payroll_Tax</c> is derived from <see cref="Wages"/> at
/// <c>0.0765</c> (Requirement 7.2) and is NOT a user input.
/// </summary>
public sealed class OperationsInputSection
{
    /// <summary>Accounting service fees (Requirement 7.1).</summary>
    public MonthlyScheduleViewModel Accounting { get; set; } = new();

    /// <summary>Custodial and cleaning costs (Requirement 7.1).</summary>
    public MonthlyScheduleViewModel Custodial { get; set; } = new();

    /// <summary>Natural gas or fuel costs (Requirement 7.1).</summary>
    public MonthlyScheduleViewModel Gas { get; set; } = new();

    /// <summary>Business insurance premiums (Requirement 7.1).</summary>
    public MonthlyScheduleViewModel Insurance { get; set; } = new();

    /// <summary>Information technology and software costs (Requirement 7.1).</summary>
    public MonthlyScheduleViewModel It { get; set; } = new();

    /// <summary>Consumable office supplies (Requirement 7.1).</summary>
    public MonthlyScheduleViewModel OfficeSupplies { get; set; } = new();

    /// <summary>Legal and professional service fees (Requirement 7.1).</summary>
    public MonthlyScheduleViewModel ProfessionalServices { get; set; } = new();

    /// <summary>Rent for premises not owned by the business (Requirement 7.1).</summary>
    public MonthlyScheduleViewModel RentExpense { get; set; } = new();

    /// <summary>Repairs and maintenance (Requirement 7.1).</summary>
    public MonthlyScheduleViewModel Repairs { get; set; } = new();

    /// <summary>Shipping and freight (Requirement 7.1).</summary>
    public MonthlyScheduleViewModel Shipping { get; set; } = new();

    /// <summary>Property tax expense (Requirement 7.1).</summary>
    public MonthlyScheduleViewModel PropertyTax { get; set; } = new();

    /// <summary>Utilities other than gas (Requirement 7.1).</summary>
    public MonthlyScheduleViewModel Utilities { get; set; } = new();

    /// <summary>
    /// Payroll wages. Drives <c>Payroll_Tax[m] = Wages[m] * 0.0765</c>
    /// (Requirements 7.1, 7.2).
    /// </summary>
    public MonthlyScheduleViewModel Wages { get; set; } = new();

    /// <summary>Operational spend not captured above (Requirement 7.1).</summary>
    public MonthlyScheduleViewModel OtherOperations { get; set; } = new();

    /// <summary>Maps this section to the domain <see cref="OperationsInputs"/> record.</summary>
    public OperationsInputs ToDomain() =>
        new(
            Accounting.ToDomain(),
            Custodial.ToDomain(),
            Gas.ToDomain(),
            Insurance.ToDomain(),
            It.ToDomain(),
            OfficeSupplies.ToDomain(),
            ProfessionalServices.ToDomain(),
            RentExpense.ToDomain(),
            Repairs.ToDomain(),
            Shipping.ToDomain(),
            PropertyTax.ToDomain(),
            Utilities.ToDomain(),
            Wages.ToDomain(),
            OtherOperations.ToDomain());
}
