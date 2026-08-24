namespace RehearsalForecast.Core.Domain;

/// <summary>
/// Tax-related user inputs. <c>Payroll_Tax_Rate</c> is a fixed derived constant
/// (0.0765, Requirement 7.2) and is NOT a user input.
/// </summary>
/// <param name="IncomeTaxRate">
/// Income tax rate applied to positive monthly pre-tax income only, as a
/// <see cref="decimal"/> in the inclusive range <c>[0, 1]</c> (Requirements 2.6,
/// 12.3). Contract only, not enforced by the type.
/// </param>
public sealed record TaxInputs(
    decimal IncomeTaxRate);
