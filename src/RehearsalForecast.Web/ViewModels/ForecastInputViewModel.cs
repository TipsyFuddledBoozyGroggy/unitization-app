using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Web.ViewModels;

/// <summary>
/// Top-level view model bound from the input page. Mirrors
/// <see cref="ForecastInputs"/> but exposes each domain section as a
/// framework-friendly settable class carrying data-annotation range checks for
/// the single-field validations in Requirement 2. Cross-field and structural
/// checks live in <c>InputValidator</c>.
/// </summary>
/// <remarks>
/// <para>
/// The eight sub-sections cover, in the order they appear on the input page
/// (Requirement 17.1, Design §13.1): Capital, Marketing, Operations, Building,
/// Loan, Taxes, Owner_Activity, Forecast_Controls.
/// </para>
/// <para>
/// <see cref="ToDomain"/> is the single conversion point from the wire view
/// model to the immutable <see cref="ForecastInputs"/> record consumed by the
/// calculator, solver, and validator. The controller MUST NOT invoke
/// <c>Compute</c> or <c>Solve</c> when either <c>ModelState.IsValid</c> is
/// <see langword="false"/> or <c>InputValidator.Validate(vm.ToDomain())</c>
/// returns invalid (Requirement 2.13).
/// </para>
/// </remarks>
public sealed class ForecastInputViewModel
{
    /// <summary>The four one-time capital line items (Requirement 9.1).</summary>
    public CapitalInputSection Capital { get; set; } = new();

    /// <summary>The four marketing line items (Requirement 6.1).</summary>
    public MarketingInputSection Marketing { get; set; } = new();

    /// <summary>The fourteen operational line items (Requirement 7.1).</summary>
    public OperationsInputSection Operations { get; set; } = new();

    /// <summary>Building geometry, depreciable cost, and occupancy (Requirements 3, 4, 8).</summary>
    public BuildingInputSection Building { get; set; } = new();

    /// <summary>Loan interest rate and term (Requirement 11).</summary>
    public LoanInputSection Loan { get; set; } = new();

    /// <summary>Income tax rate (Requirement 12).</summary>
    public TaxInputSection Taxes { get; set; } = new();

    /// <summary>Owner investment and constant per-month withdrawals (Requirements 1.6, 10).</summary>
    public OwnerActivityInputSection OwnerActivity { get; set; } = new();

    /// <summary>Opening cash and target cash-positive month (Requirements 13.2, 14.1).</summary>
    public ForecastControlInputSection ForecastControls { get; set; } = new();

    /// <summary>
    /// Maps every sub-section to its domain counterpart and assembles a
    /// <see cref="ForecastInputs"/> record. Idempotent and free of side
    /// effects.
    /// </summary>
    /// <returns>The immutable domain input record.</returns>
    public ForecastInputs ToDomain() =>
        new(
            Capital.ToDomain(),
            Marketing.ToDomain(),
            Operations.ToDomain(),
            Building.ToDomain(),
            Loan.ToDomain(),
            Taxes.ToDomain(),
            OwnerActivity.ToDomain(),
            ForecastControls.ToDomain());
}
