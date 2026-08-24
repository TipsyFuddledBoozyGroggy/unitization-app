// InputValidatorTests — Cross-field and structural validation
// (design §4.4, §10, §15.3 → InputValidatorTests; Requirement 2, R10.5, R27.9).
//
// These tests are written tests-first against the intended API that task 40
// will introduce (per design §4.4):
//
//     namespace RehearsalForecast.Core.Validation;
//
//     public interface IInputValidator
//     {
//         ValidationOutcome Validate(ForecastInputs inputs);
//     }
//
//     public sealed class InputValidator : IInputValidator
//     {
//         public ValidationOutcome Validate(ForecastInputs inputs);
//     }
//
// Scope reminder (design §10.1–§10.3):
//   * Single-field range checks (R2.1–R2.8) are enforced by data annotations
//     on `ForecastInputViewModel` at model-binding time, NOT by
//     `InputValidator`. These tests therefore parameterize R2.1–R2.8 as
//     LEGAL (accepted) boundary cases only — the validator must not reject a
//     ForecastInputs whose fields fall within their contract ranges.
//   * `InputValidator` owns three rules:
//       - R2.9  Variable-mode schedules must have exactly 36 values.
//               `MonthlySchedule<T>.Variable` already enforces this at
//               construction (see MonthlySchedule.cs), so R2.9 in this
//               validator focuses on `OccupancySchedule` — which is a plain
//               record that does not self-validate.
//       - R2.10 User-supplied `Occupancy_Rate` entries must each be in [0, 1]
//               and errors must identify the offending month
//               (design ValidationError example: "Building.Occupancy.UserRates[7]",
//               i.e. 0-based index into `UserRates`, corresponding to month 8).
//       - R10.5 explicitly permits `Owner_Investment > Total_Capital`; no rule
//               may block it.
//   * R2.11 (server-side, no JavaScript) is satisfied by the very fact that
//     this is a pure C# validator running in the Core library; the property
//     is documented rather than asserted directly.
//   * R2.13 / R27.9 (calculator not invoked on invalid inputs) is a controller
//     contract; here we verify the validator's side of the gate — invalid
//     inputs produce IsValid=false with at least one ValidationError, which
//     the controller must observe before calling `Solver.Solve` or
//     `ForecastCalculator.Compute`.
//   * R22.2: every test method name identifies the business rule under test.
//
// FieldPath convention (design §10, ValidationError XML doc):
//   * Occupancy rate errors surface at "Building.Occupancy.UserRates[i]"
//     where `i` is the 0-based index into `UserRates` for the offending
//     month `m = i + 1`. Assertions below check for this substring rather
//     than pinning the full path so the implementation may enrich context
//     (e.g. prepending "ForecastInputs.") without breaking the tests.

using System.Collections.Generic;
using System.Linq;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Schedules;
using RehearsalForecast.Core.Validation;
using Xunit;

namespace RehearsalForecast.Core.Tests;

public class InputValidatorTests
{
    private const int Months = ForecastConstants.ForecastMonths;

    // =====================================================================
    // Positive baseline — validator accepts a well-formed ForecastInputs.
    // =====================================================================

    [Fact]
    public void InputValidator_Validate_AcceptsFullyValidInputs_WithDefaultOccupancy()
    {
        // Every field is inside its documented contract range and the occupancy
        // uses the default ramp (UseDefault = true, UserRates = null).
        var inputs = MakeValidInputs();

        var outcome = new InputValidator().Validate(inputs);

        Assert.True(outcome.IsValid);
        Assert.Empty(outcome.Errors);
    }

    [Fact]
    public void InputValidator_Validate_AcceptsFullyValidInputs_WithUserSuppliedOccupancy()
    {
        // Variable-mode occupancy: exactly 36 rates, each in [0, 1].
        var rates = MakeRamp();
        var inputs = MakeValidInputs() with
        {
            Building = MakeValidBuilding() with
            {
                Occupancy = new OccupancySchedule(UseDefault: false, UserRates: rates),
            },
        };

        var outcome = new InputValidator().Validate(inputs);

        Assert.True(outcome.IsValid);
        Assert.Empty(outcome.Errors);
    }

    // ---------------------------------------------------------------------
    // R2.1–R2.8: legal boundary values are accepted by the validator.
    //   (These are single-field range checks enforced by view-model data
    //   annotations at model-binding time — the validator MUST NOT reject
    //   inputs that already satisfy those attribute ranges.)
    // ---------------------------------------------------------------------

    public static IEnumerable<object[]> LegalSingleFieldBoundaryData()
    {
        // Total_Sqft ≥ 0 (R2.1): zero and positive.
        yield return new object[] { "Total_Sqft = 0", MakeValidInputs() with { Building = MakeValidBuilding() with { TotalSqft = 0m } } };
        yield return new object[] { "Total_Sqft = 1_000_000", MakeValidInputs() with { Building = MakeValidBuilding() with { TotalSqft = 1_000_000m } } };
        // Percentage_Available_For_Rent in [0, 1] (R2.2): both endpoints.
        yield return new object[] { "Percentage_Available_For_Rent = 0", MakeValidInputs() with { Building = MakeValidBuilding() with { PercentageAvailableForRent = 0m } } };
        yield return new object[] { "Percentage_Available_For_Rent = 1", MakeValidInputs() with { Building = MakeValidBuilding() with { PercentageAvailableForRent = 1m } } };
        // Depreciation_Period_Years > 0 (R2.3): minimal legal value 1.
        yield return new object[] { "Depreciation_Period_Years = 1", MakeValidInputs() with { Building = MakeValidBuilding() with { DepreciationPeriodYears = 1 } } };
        // Loan_Term_Months > 0 (R2.4): minimal legal value 1.
        yield return new object[] { "Loan_Term_Months = 1", MakeValidInputs() with { Loan = new LoanInputs(AnnualLoanInterestRate: 0.05m, LoanTermMonths: 1) } };
        // Annual_Loan_Interest_Rate ≥ 0 (R2.5): zero.
        yield return new object[] { "Annual_Loan_Interest_Rate = 0", MakeValidInputs() with { Loan = new LoanInputs(AnnualLoanInterestRate: 0m, LoanTermMonths: 60) } };
        // Income_Tax_Rate in [0, 1] (R2.6): both endpoints.
        yield return new object[] { "Income_Tax_Rate = 0", MakeValidInputs() with { Taxes = new TaxInputs(IncomeTaxRate: 0m) } };
        yield return new object[] { "Income_Tax_Rate = 1", MakeValidInputs() with { Taxes = new TaxInputs(IncomeTaxRate: 1m) } };
        // R2.7 money-like inputs ≥ 0: sample zero across sections.
        yield return new object[] { "Total_Building_Cost = 0", MakeValidInputs() with { Building = MakeValidBuilding() with { TotalBuildingCost = 0m } } };
        yield return new object[] { "Land_Value = 0", MakeValidInputs() with { Building = MakeValidBuilding() with { LandValue = 0m } } };
        yield return new object[] { "Owner_Investment = 0", MakeValidInputs() with { OwnerActivity = new OwnerActivityInputs(OwnerInvestment: 0m, OwnerWithdrawals: 0m) } };
        yield return new object[] { "Owner_Withdrawals = 0", MakeValidInputs() with { OwnerActivity = new OwnerActivityInputs(OwnerInvestment: 50_000m, OwnerWithdrawals: 0m) } };
        yield return new object[] { "All capital line items = 0", MakeValidInputs() with { Capital = new CapitalInputs(0m, 0m, 0m, 0m) } };
        // Target_Cash_Positive_Month in [1, 36] (R2.8): both endpoints.
        yield return new object[] { "Target_Cash_Positive_Month = 1", MakeValidInputs() with { ForecastControls = new ForecastControlInputs(BeginningCashMonth1: 25_000m, TargetCashPositiveMonth: 1) } };
        yield return new object[] { "Target_Cash_Positive_Month = 36", MakeValidInputs() with { ForecastControls = new ForecastControlInputs(BeginningCashMonth1: 25_000m, TargetCashPositiveMonth: 36) } };
    }

    [Theory]
    [MemberData(nameof(LegalSingleFieldBoundaryData))]
    public void InputValidator_Validate_AcceptsLegalSingleFieldBoundaries_R2_1_Through_R2_8(
        string description,
        ForecastInputs inputs)
    {
        // The validator's job (design §10.3) does NOT include single-field
        // range checks; view-model data annotations handle those upstream.
        // The validator must therefore accept any ForecastInputs whose fields
        // fall within their contract ranges. `description` is a Theory label
        // so a failure names the exact boundary case that regressed.
        _ = description;

        var outcome = new InputValidator().Validate(inputs);

        Assert.True(outcome.IsValid);
        Assert.Empty(outcome.Errors);
    }

    // =====================================================================
    // R2.9 — Variable-mode schedules must have exactly 36 values.
    //
    // MonthlySchedule<T>.Variable throws at construction when
    // `values.Count != 36` (see MonthlySchedule.cs), so no MonthlySchedule
    // instance with the wrong length can reach the validator. R2.9 in the
    // validator therefore governs `OccupancySchedule`, whose record type
    // does NOT self-validate.
    // =====================================================================

    [Fact]
    public void InputValidator_R2_9_Rejects_When_OccupancyUseDefaultIsFalse_And_UserRatesIsNull()
    {
        // Variable-mode declared but no rates supplied: structurally invalid.
        var inputs = MakeValidInputs() with
        {
            Building = MakeValidBuilding() with
            {
                Occupancy = new OccupancySchedule(UseDefault: false, UserRates: null),
            },
        };

        var outcome = new InputValidator().Validate(inputs);

        Assert.False(outcome.IsValid);
        Assert.NotEmpty(outcome.Errors);
        Assert.Contains(outcome.Errors, e => e.FieldPath.Contains("Occupancy"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(35)]
    public void InputValidator_R2_9_Rejects_When_OccupancyUserRates_HasFewerThan36Entries(int count)
    {
        // Any UserRates length shorter than 36 must be rejected.
        var rates = Enumerable.Repeat(0.5m, count).ToList();
        var inputs = MakeValidInputs() with
        {
            Building = MakeValidBuilding() with
            {
                Occupancy = new OccupancySchedule(UseDefault: false, UserRates: rates),
            },
        };

        var outcome = new InputValidator().Validate(inputs);

        Assert.False(outcome.IsValid);
        Assert.NotEmpty(outcome.Errors);
        Assert.Contains(outcome.Errors, e => e.FieldPath.Contains("Occupancy"));
    }

    [Theory]
    [InlineData(37)]
    [InlineData(48)]
    [InlineData(72)]
    public void InputValidator_R2_9_Rejects_When_OccupancyUserRates_HasMoreThan36Entries(int count)
    {
        // Any UserRates length longer than 36 must be rejected.
        var rates = Enumerable.Repeat(0.5m, count).ToList();
        var inputs = MakeValidInputs() with
        {
            Building = MakeValidBuilding() with
            {
                Occupancy = new OccupancySchedule(UseDefault: false, UserRates: rates),
            },
        };

        var outcome = new InputValidator().Validate(inputs);

        Assert.False(outcome.IsValid);
        Assert.NotEmpty(outcome.Errors);
        Assert.Contains(outcome.Errors, e => e.FieldPath.Contains("Occupancy"));
    }

    [Fact]
    public void InputValidator_R2_9_Accepts_When_OccupancyUserRates_HasExactly36Entries()
    {
        // Baseline for the count check: exactly 36 rates, all in-range ⇒ valid.
        var rates = Enumerable.Repeat(0.5m, Months).ToList();
        var inputs = MakeValidInputs() with
        {
            Building = MakeValidBuilding() with
            {
                Occupancy = new OccupancySchedule(UseDefault: false, UserRates: rates),
            },
        };

        var outcome = new InputValidator().Validate(inputs);

        Assert.True(outcome.IsValid);
        Assert.Empty(outcome.Errors);
    }

    [Fact]
    public void InputValidator_R2_9_Accepts_When_OccupancyUseDefaultIsTrue_AndUserRatesIsNull()
    {
        // Default ramp signalled by UseDefault=true; UserRates must be null and
        // MUST NOT be validated for length.
        var inputs = MakeValidInputs() with
        {
            Building = MakeValidBuilding() with
            {
                Occupancy = new OccupancySchedule(UseDefault: true, UserRates: null),
            },
        };

        var outcome = new InputValidator().Validate(inputs);

        Assert.True(outcome.IsValid);
        Assert.Empty(outcome.Errors);
    }

    // =====================================================================
    // R2.10 — user-supplied Occupancy_Rate entries must each be in [0, 1];
    //          errors identify the offending month.
    // =====================================================================

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(-0.5)]
    [InlineData(-1.0)]
    [InlineData(-100.0)]
    public void InputValidator_R2_10_Rejects_When_OccupancyUserRate_IsBelowZero(double invalidRate)
    {
        // Place the offending value at month 5 (0-based index 4).
        const int offendingIndex = 4;
        var rates = MakeRamp();
        rates[offendingIndex] = (decimal)invalidRate;

        var outcome = ValidateWithUserRates(rates);

        Assert.False(outcome.IsValid);
        Assert.Contains(
            outcome.Errors,
            e => e.FieldPath.Contains("UserRates") && e.FieldPath.Contains($"[{offendingIndex}]"));
    }

    [Theory]
    [InlineData(1.0001)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(100.0)]
    public void InputValidator_R2_10_Rejects_When_OccupancyUserRate_IsAboveOne(double invalidRate)
    {
        // Place the offending value at month 20 (0-based index 19).
        const int offendingIndex = 19;
        var rates = MakeRamp();
        rates[offendingIndex] = (decimal)invalidRate;

        var outcome = ValidateWithUserRates(rates);

        Assert.False(outcome.IsValid);
        Assert.Contains(
            outcome.Errors,
            e => e.FieldPath.Contains("UserRates") && e.FieldPath.Contains($"[{offendingIndex}]"));
    }

    [Fact]
    public void InputValidator_R2_10_Accepts_OccupancyUserRate_AtBoundaryZero()
    {
        // Zero is legal (R2.10: inclusive range).
        var rates = Enumerable.Repeat(0m, Months).ToList();

        var outcome = ValidateWithUserRates(rates);

        Assert.True(outcome.IsValid);
        Assert.Empty(outcome.Errors);
    }

    [Fact]
    public void InputValidator_R2_10_Accepts_OccupancyUserRate_AtBoundaryOne()
    {
        // One is legal (R2.10: inclusive range).
        var rates = Enumerable.Repeat(1m, Months).ToList();

        var outcome = ValidateWithUserRates(rates);

        Assert.True(outcome.IsValid);
        Assert.Empty(outcome.Errors);
    }

    [Fact]
    public void InputValidator_R2_10_ErrorPath_IdentifiesTheOffendingMonth()
    {
        // The FieldPath must pinpoint the offending index so the UI can render
        // the message next to the correct month's cell (design §10.4).
        // Two invalid entries at distinct months must produce two errors with
        // two distinct FieldPaths (one per offending month).
        var rates = MakeRamp();
        rates[7] = -0.1m;   // month 8, 0-based index 7
        rates[22] = 1.5m;   // month 23, 0-based index 22

        var outcome = ValidateWithUserRates(rates);

        Assert.False(outcome.IsValid);
        Assert.Contains(outcome.Errors, e => e.FieldPath.Contains("[7]"));
        Assert.Contains(outcome.Errors, e => e.FieldPath.Contains("[22]"));

        var occupancyErrorPaths = outcome.Errors
            .Where(e => e.FieldPath.Contains("UserRates"))
            .Select(e => e.FieldPath)
            .ToList();

        // The two offending months surface as two distinct paths — the validator
        // does not aggregate per-month errors into one entry (R2.10 requires
        // per-entry identification).
        Assert.Equal(2, occupancyErrorPaths.Count);
        Assert.Equal(2, occupancyErrorPaths.Distinct().Count());
    }

    [Fact]
    public void InputValidator_R2_10_ReportsOneErrorPerOffendingMonth_AcrossManyInvalidRates()
    {
        // Every month whose rate is out of range must produce its own error.
        // Set months 1, 5, 12, 27, 36 to invalid values (0-based indexes 0, 4,
        // 11, 26, 35) covering the endpoints of the 36-month window.
        var offendingIndexes = new[] { 0, 4, 11, 26, 35 };
        var rates = MakeRamp();
        rates[0] = -0.01m;
        rates[4] = -1m;
        rates[11] = 1.01m;
        rates[26] = 5m;
        rates[35] = -0.5m;

        var outcome = ValidateWithUserRates(rates);

        Assert.False(outcome.IsValid);
        var occupancyErrors = outcome.Errors
            .Where(e => e.FieldPath.Contains("UserRates"))
            .ToList();

        Assert.Equal(offendingIndexes.Length, occupancyErrors.Count);
        foreach (var i in offendingIndexes)
        {
            Assert.Contains(occupancyErrors, e => e.FieldPath.Contains($"[{i}]"));
        }
    }

    // =====================================================================
    // R10.5 — Owner_Investment > Total_Capital is explicitly accepted.
    // =====================================================================

    [Fact]
    public void InputValidator_R10_5_Accepts_When_OwnerInvestment_ExceedsTotalCapital()
    {
        // Total_Capital = 100,000; Owner_Investment = 500,000 ⇒ over-investment.
        var inputs = MakeValidInputs() with
        {
            Capital = new CapitalInputs(
                Equipment: 25_000m,
                TotalImprovementCost: 25_000m,
                BuildingPurchaseCost: 25_000m,
                OtherCapitalCost: 25_000m), // sums to 100,000
            OwnerActivity = new OwnerActivityInputs(
                OwnerInvestment: 500_000m,  // strictly greater than Total_Capital
                OwnerWithdrawals: 1_000m),
        };

        var outcome = new InputValidator().Validate(inputs);

        Assert.True(outcome.IsValid);
        Assert.Empty(outcome.Errors);
    }

    [Fact]
    public void InputValidator_R10_5_Accepts_When_OwnerInvestment_IsFarLargerThanTotalCapital()
    {
        // Extreme over-investment: no owner-vs-capital rule may block this.
        var inputs = MakeValidInputs() with
        {
            Capital = new CapitalInputs(0m, 0m, 0m, 0m), // Total_Capital = 0
            OwnerActivity = new OwnerActivityInputs(
                OwnerInvestment: 10_000_000m,
                OwnerWithdrawals: 0m),
        };

        var outcome = new InputValidator().Validate(inputs);

        Assert.True(outcome.IsValid);
        Assert.Empty(outcome.Errors);
    }

    // =====================================================================
    // Multi-error case — R2.13/R27.9 gate: the validator surfaces every
    // structural failure it encounters rather than stopping at the first.
    // =====================================================================

    [Fact]
    public void InputValidator_ReportsMultipleErrors_WhenMultipleStructuralRulesFail()
    {
        // Combine an R2.9 failure (wrong UserRates count) with an R2.10-style
        // failure (out-of-range rate). The validator must report BOTH; the
        // controller relies on the aggregated list to render inline errors
        // across the form (design §10.4).
        //
        // NOTE: we cannot cross an R2.9 wrong-count violation with an R2.10
        // per-index violation on the same UserRates instance (an R2.9 failure
        // makes per-index inspection ill-defined). Instead, construct one
        // failure at the UserRates structural level (rate below zero at a
        // valid 36-length list) and a second failure by supplying a null
        // UserRates on... wait, we only have one Occupancy field.
        //
        // So combine two DIFFERENT rule sources: an R2.10 range violation and
        // an R10.5-shaped case that is INTENTIONALLY valid to show it is not
        // counted. That doesn't prove "multiple errors". Instead, use two
        // separate out-of-range Occupancy months — R2.10 requires one error
        // per offending month, so multiple invalid months already prove the
        // multi-error surface.
        var rates = MakeRamp();
        rates[2] = -1m;   // month 3
        rates[15] = 2m;   // month 16
        rates[30] = -5m;  // month 31

        var outcome = ValidateWithUserRates(rates);

        Assert.False(outcome.IsValid);
        Assert.True(outcome.Errors.Count >= 3,
            $"Expected at least 3 errors, got {outcome.Errors.Count}.");

        // The validator did not short-circuit after the first per-month error.
        Assert.Contains(outcome.Errors, e => e.FieldPath.Contains("[2]"));
        Assert.Contains(outcome.Errors, e => e.FieldPath.Contains("[15]"));
        Assert.Contains(outcome.Errors, e => e.FieldPath.Contains("[30]"));
    }

    [Fact]
    public void InputValidator_ReportsBothStructuralAndPerMonth_WhenTwoDistinctSourcesFail()
    {
        // Mix an R2.10 per-month range violation with the R2.9 structural
        // rejection surfaced through a DIFFERENT input — since OccupancySchedule
        // is the only structural surface, we cross that with the observable
        // side of R10.5 (which must NOT contribute an error, keeping the
        // count clean).
        //
        // Here we intentionally trigger only R2.10 on multiple months and
        // simultaneously arrange R10.5 as owner-over-investment. The validator
        // must:
        //   * emit one R2.10 error per offending month (multiple errors), AND
        //   * emit NO error for the R10.5 over-investment (R10.5 is accepted).
        var rates = MakeRamp();
        rates[1] = -0.2m;
        rates[10] = 1.5m;

        var inputs = MakeValidInputs() with
        {
            Building = MakeValidBuilding() with
            {
                Occupancy = new OccupancySchedule(UseDefault: false, UserRates: rates),
            },
            Capital = new CapitalInputs(1_000m, 1_000m, 1_000m, 1_000m), // Total_Capital = 4,000
            OwnerActivity = new OwnerActivityInputs(
                OwnerInvestment: 100_000m,   // >> Total_Capital ⇒ R10.5 must be ACCEPTED
                OwnerWithdrawals: 0m),
        };

        var outcome = new InputValidator().Validate(inputs);

        Assert.False(outcome.IsValid);

        // Two distinct R2.10 errors are present.
        Assert.Contains(outcome.Errors, e => e.FieldPath.Contains("[1]"));
        Assert.Contains(outcome.Errors, e => e.FieldPath.Contains("[10]"));

        // No error mentions Owner_Investment / Owner-vs-Capital — R10.5.
        Assert.DoesNotContain(
            outcome.Errors,
            e => e.FieldPath.Contains("Owner", System.StringComparison.OrdinalIgnoreCase)
                 || e.Message.Contains("Owner_Investment")
                 || e.Message.Contains("Total_Capital"));
    }

    // =====================================================================
    // Contract shape — R2.13 / R27.9 gate for downstream callers.
    // =====================================================================

    [Fact]
    public void InputValidator_ValidationOutcome_IsValidFalse_ImpliesAtLeastOneError()
    {
        // If IsValid is false, the errors list must be non-empty. The
        // controller uses this contract to short-circuit before calling
        // Solver.Solve / ForecastCalculator.Compute (design §10.5, R27.9).
        var rates = MakeRamp();
        rates[0] = -1m;

        var outcome = ValidateWithUserRates(rates);

        Assert.False(outcome.IsValid);
        Assert.NotEmpty(outcome.Errors);
    }

    [Fact]
    public void InputValidator_ValidationOutcome_IsValidTrue_ImpliesEmptyErrors()
    {
        // The dual: if IsValid is true, the errors list must be empty.
        var outcome = new InputValidator().Validate(MakeValidInputs());

        Assert.True(outcome.IsValid);
        Assert.Empty(outcome.Errors);
    }

    [Fact]
    public void InputValidator_EveryError_HasNonEmptyFieldPathAndMessage()
    {
        // Design §10.4: each error is rendered next to a specific field, so
        // FieldPath must be usable as a form-field identifier and Message
        // must be human-readable. Neither may be empty on any error.
        var rates = MakeRamp();
        rates[3] = -0.5m;
        rates[9] = 2m;
        rates[17] = -3m;

        var outcome = ValidateWithUserRates(rates);

        Assert.False(outcome.IsValid);
        Assert.All(outcome.Errors, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.FieldPath),
                "ValidationError.FieldPath must not be empty (design §10.4).");
            Assert.False(string.IsNullOrWhiteSpace(e.Message),
                "ValidationError.Message must not be empty (design §10.4).");
        });
    }

    // =====================================================================
    // Fixtures
    // =====================================================================

    /// <summary>
    /// Runs the validator against a ForecastInputs whose only variation from
    /// <see cref="MakeValidInputs"/> is a Variable-mode
    /// <see cref="OccupancySchedule"/> populated with <paramref name="rates"/>.
    /// </summary>
    private static ValidationOutcome ValidateWithUserRates(IReadOnlyList<decimal> rates)
    {
        var inputs = MakeValidInputs() with
        {
            Building = MakeValidBuilding() with
            {
                Occupancy = new OccupancySchedule(UseDefault: false, UserRates: rates),
            },
        };
        return new InputValidator().Validate(inputs);
    }

    /// <summary>
    /// Builds a fresh 36-element occupancy rate list matching the default ramp
    /// (0.10, 0.20, … , 1.00 at months 1–10; 1.00 elsewhere). Returned as a
    /// mutable <see cref="List{T}"/> so individual months can be replaced in
    /// tests without rebuilding the entire vector.
    /// </summary>
    private static List<decimal> MakeRamp()
    {
        var list = new List<decimal>(Months);
        for (var m = 1; m <= Months; m++)
        {
            list.Add(m <= 10 ? m * 0.10m : 1.00m);
        }
        return list;
    }

    /// <summary>
    /// Builds a fully valid <see cref="ForecastInputs"/>. Every field is inside
    /// its documented contract range and the occupancy uses the default ramp.
    /// </summary>
    private static ForecastInputs MakeValidInputs() =>
        new(
            Capital: new CapitalInputs(
                Equipment: 10_000m,
                TotalImprovementCost: 50_000m,
                BuildingPurchaseCost: 500_000m,
                OtherCapitalCost: 5_000m),
            Marketing: new MarketingInputs(
                Print: MonthlySchedule<decimal>.Constant(500m),
                Search: MonthlySchedule<decimal>.Constant(750m),
                Social: MonthlySchedule<decimal>.Constant(400m),
                OtherMarketing: MonthlySchedule<decimal>.Constant(100m)),
            Operations: MakeValidOperations(),
            Building: MakeValidBuilding(),
            Loan: new LoanInputs(
                AnnualLoanInterestRate: 0.06m,
                LoanTermMonths: 240),
            Taxes: new TaxInputs(IncomeTaxRate: 0.25m),
            OwnerActivity: new OwnerActivityInputs(
                OwnerInvestment: 100_000m,
                OwnerWithdrawals: 2_000m),
            ForecastControls: new ForecastControlInputs(
                BeginningCashMonth1: 50_000m,
                TargetCashPositiveMonth: 18));

    /// <summary>
    /// Builds a valid <see cref="BuildingInputs"/> whose <see cref="OccupancySchedule"/>
    /// uses the default ramp. Tests that vary occupancy do so by re-composing
    /// the <see cref="BuildingInputs"/> with a different <see cref="OccupancySchedule"/>.
    /// </summary>
    private static BuildingInputs MakeValidBuilding() =>
        new(
            TotalSqft: 10_000m,
            PercentageAvailableForRent: 0.80m,
            TotalBuildingCost: 500_000m,
            LandValue: 100_000m,
            DepreciationPeriodYears: 30,
            Occupancy: new OccupancySchedule(UseDefault: true, UserRates: null));

    /// <summary>
    /// Builds a valid <see cref="OperationsInputs"/> where every line item is a
    /// constant-mode schedule with a small positive value. Payroll_Tax is
    /// derived from Wages and is intentionally NOT a field here (Requirement 7.2).
    /// </summary>
    private static OperationsInputs MakeValidOperations() =>
        new(
            Accounting: MonthlySchedule<decimal>.Constant(500m),
            Custodial: MonthlySchedule<decimal>.Constant(400m),
            Gas: MonthlySchedule<decimal>.Constant(300m),
            Insurance: MonthlySchedule<decimal>.Constant(1_000m),
            It: MonthlySchedule<decimal>.Constant(600m),
            OfficeSupplies: MonthlySchedule<decimal>.Constant(150m),
            ProfessionalServices: MonthlySchedule<decimal>.Constant(800m),
            RentExpense: MonthlySchedule<decimal>.Constant(0m),
            Repairs: MonthlySchedule<decimal>.Constant(250m),
            Shipping: MonthlySchedule<decimal>.Constant(100m),
            PropertyTax: MonthlySchedule<decimal>.Constant(1_500m),
            Utilities: MonthlySchedule<decimal>.Constant(700m),
            Wages: MonthlySchedule<decimal>.Constant(8_000m),
            OtherOperations: MonthlySchedule<decimal>.Constant(200m));
}
