using RehearsalForecast.Core.Schedules;

namespace RehearsalForecast.Core.Domain;

/// <summary>
/// The fourteen operational line items (Requirement 7.1). Each item is either a
/// constant value applied to every month or an explicit 36-month schedule
/// (Requirements 1.2, 1.4). <c>Payroll_Tax</c> is derived from <see cref="Wages"/>
/// (Requirement 7.2) and is NOT a member of this record.
/// </summary>
/// <param name="Accounting">Accounting service fees (Requirement 7.1).</param>
/// <param name="Custodial">Custodial and cleaning costs (Requirement 7.1).</param>
/// <param name="Gas">Natural gas or fuel costs (Requirement 7.1).</param>
/// <param name="Insurance">Business insurance premiums (Requirement 7.1).</param>
/// <param name="It">Information technology and software costs (Requirement 7.1).</param>
/// <param name="OfficeSupplies">Consumable office supplies (Requirement 7.1).</param>
/// <param name="ProfessionalServices">Legal and professional service fees (Requirement 7.1).</param>
/// <param name="RentExpense">Rent for premises not owned by the business (Requirement 7.1).</param>
/// <param name="Repairs">Repairs and maintenance (Requirement 7.1).</param>
/// <param name="Shipping">Shipping and freight (Requirement 7.1).</param>
/// <param name="PropertyTax">Property tax expense (Requirement 7.1).</param>
/// <param name="Utilities">Utilities other than gas (Requirement 7.1).</param>
/// <param name="Wages">Payroll wages. Drives <c>Payroll_Tax[m] = Wages[m] * 0.0765</c> (Requirements 7.1, 7.2).</param>
/// <param name="OtherOperations">Operational spend not captured above (Requirement 7.1).</param>
public sealed record OperationsInputs(
    MonthlySchedule<decimal> Accounting,
    MonthlySchedule<decimal> Custodial,
    MonthlySchedule<decimal> Gas,
    MonthlySchedule<decimal> Insurance,
    MonthlySchedule<decimal> It,
    MonthlySchedule<decimal> OfficeSupplies,
    MonthlySchedule<decimal> ProfessionalServices,
    MonthlySchedule<decimal> RentExpense,
    MonthlySchedule<decimal> Repairs,
    MonthlySchedule<decimal> Shipping,
    MonthlySchedule<decimal> PropertyTax,
    MonthlySchedule<decimal> Utilities,
    MonthlySchedule<decimal> Wages,
    MonthlySchedule<decimal> OtherOperations);
