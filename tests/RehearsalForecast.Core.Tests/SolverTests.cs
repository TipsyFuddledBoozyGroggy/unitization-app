// Tests for the target-price solver
// (design §4.3, §5.7, §8, §15.3 → SolverTests).
//
// These tests are written tests-first (task 37) against the intended public API
// that task 38 will introduce. Design §4.3 spells out the interface exactly:
//
//     namespace RehearsalForecast.Core.Solving;
//
//     public interface ISolver
//     {
//         SolverResult Solve(ForecastInputs inputs);
//     }
//
//     public sealed class PriceSolver : ISolver
//     {
//         public PriceSolver(IForecastCalculator forecastCalculator);
//         public SolverResult Solve(ForecastInputs inputs);
//     }
//
// SolverResult is a closed discriminated union (§5.7) whose two concrete variants
// carry:
//   * Success(decimal FlatPricePerSqft, ForecastResult Forecast, int Iterations)
//   * Failure(string Reason, int Iterations)
//
// The tests exercise the algorithm at the public seam only. Each numbered
// bullet from task 37 maps to one or more [Fact]s below:
//
//   * Fast path at p = 0 (§8.2, R15.3, R15.4) ─── Solve_ReturnsZero_...
//   * Geometric upper-bound expansion (§8.3, R15.5) ─── Solve_ExpansionBeyondFastPath_...
//   * Bisection converges within SolverTolerance (§8.4, R15.6) ─── implicit in cent-level
//     minimality plus rounding tests; a converged bisection is precisely what
//     lets the cent-precise ceiling be provably minimal.
//   * Final answer is rounded UP to Currency_Precision (§8.5, R15.8) ─── Solve_Price_IsRoundedUp...
//   * Post-rounding re-verification (§8.6, §8.7, R15.9, R15.10, R27.6) ─── Solve_PostRoundingReverification_...
//   * Safety-limit breach ⇒ Failure with Iterations = SolverSafetyLimit + 1
//     (§8.8, R15.11) ─── Solve_ReturnsFailure_WithoutThrowing_...
//   * Determinism (R15.2) ─── Solve_IsDeterministic_...
//   * Cent-level minimality (§8.4, §8.5, R15.1) ─── Solve_CentLevelMinimality_...
//
// The solver is exercised end-to-end against a real `ForecastCalculator` +
// `LoanCalculator` composition (Requirement 15.7: "no cached forecasts").
// That keeps the tests aligned with the delivered pipeline and avoids
// asserting on a stub whose behavior might drift from the real calculator.
// The one exception is the never-satisfies scenario in the safety-limit test,
// which uses `ForecastInputs` engineered so that no p can satisfy the rule
// (per task 37's hint: "always-negative cash regardless of price").
//
// Validates:
//   * R15.1  ─ Minimum nonnegative Flat_Price_Per_Sqft that satisfies the rule.
//   * R15.2  ─ Deterministic bounded search.
//   * R15.3  ─ Search begins at Flat_Price_Per_Sqft = 0.
//   * R15.4  ─ p = 0 satisfying ⇒ Solver returns 0 (fast path).
//   * R15.5  ─ Geometric upper-bound expansion.
//   * R15.6  ─ Bisection terminates at Solver_Tolerance.
//   * R15.7  ─ Fresh Compute per candidate.
//   * R15.8  ─ Round UP to Currency_Precision.
//   * R15.9  ─ Post-rounding re-verification.
//   * R15.10 ─ Incremental raise by Currency_Precision when the rounded value fails.
//   * R15.11 ─ Safety-limit breach ⇒ Failure, no throw, no infinite loop.
//   * R15.12 ─ decimal throughout.
//   * R22.2  ─ Test names identify the business rule under test.
//   * R27.6  ─ Post-rounding raise when Currency_Precision moves the price under
//              the satisfying region.

using System;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;
using RehearsalForecast.Core.Loan;
using RehearsalForecast.Core.Schedules;
using RehearsalForecast.Core.Solving;
using Xunit;

namespace RehearsalForecast.Core.Tests;

public class SolverTests
{
    // ================================================================
    // Fixtures and helpers
    // ================================================================

    /// <summary>
    /// Builds a fresh <see cref="PriceSolver"/> composed with the real
    /// <see cref="ForecastCalculator"/> + <see cref="LoanCalculator"/> so the
    /// tests exercise the shipped pipeline end-to-end. The solver is stateless
    /// per design §8; a new instance per test enforces isolation.
    /// </summary>
    private static PriceSolver MakeSolver() =>
        new PriceSolver(MakeForecastCalculator());

    private static ForecastCalculator MakeForecastCalculator() =>
        new ForecastCalculator(new LoanCalculator());

    private static MonthlySchedule<decimal> Zero() =>
        MonthlySchedule<decimal>.Constant(0m);

    /// <summary>
    /// Builds a <see cref="ForecastInputs"/> with hand-picked knobs. All
    /// schedules default to a constant-zero <see cref="MonthlySchedule{T}"/>
    /// so that only the parameters supplied by a given test contribute to the
    /// forecast. This keeps every scenario minimal — the solver must find the
    /// minimum p under exactly the drains and revenues the test names.
    /// </summary>
    /// <param name="beginningCashMonth1">Opening cash used as Beginning_Cash[1].</param>
    /// <param name="ownerWithdrawals">Constant per-month owner draw applied uniformly (Requirement 1.6, 13.6).</param>
    /// <param name="totalSqft">Total warehouse floor area; combined with <paramref name="percentageAvailableForRent"/> to size Rentable_Sqft.</param>
    /// <param name="percentageAvailableForRent">Fraction of <paramref name="totalSqft"/> that is rentable.</param>
    /// <param name="targetCashPositiveMonth">Month the rule must hold from onward.</param>
    private static ForecastInputs MakeInputs(
        decimal beginningCashMonth1 = 0m,
        decimal ownerWithdrawals = 0m,
        decimal totalSqft = 0m,
        decimal percentageAvailableForRent = 0m,
        int targetCashPositiveMonth = 24)
    {
        return new ForecastInputs(
            Capital: new CapitalInputs(
                Equipment: 0m,
                TotalImprovementCost: 0m,
                BuildingPurchaseCost: 0m,
                OtherCapitalCost: 0m),
            Marketing: new MarketingInputs(
                Print: Zero(),
                Search: Zero(),
                Social: Zero(),
                OtherMarketing: Zero()),
            Operations: new OperationsInputs(
                Accounting: Zero(),
                Custodial: Zero(),
                Gas: Zero(),
                Insurance: Zero(),
                It: Zero(),
                OfficeSupplies: Zero(),
                ProfessionalServices: Zero(),
                RentExpense: Zero(),
                Repairs: Zero(),
                Shipping: Zero(),
                PropertyTax: Zero(),
                Utilities: Zero(),
                Wages: Zero(),
                OtherOperations: Zero()),
            Building: new BuildingInputs(
                TotalSqft: totalSqft,
                PercentageAvailableForRent: percentageAvailableForRent,
                TotalBuildingCost: 0m,
                LandValue: 0m,
                DepreciationPeriodYears: 30,
                Occupancy: new OccupancySchedule(UseDefault: true, UserRates: null)),
            Loan: new LoanInputs(
                AnnualLoanInterestRate: 0m,
                LoanTermMonths: 60),
            Taxes: new TaxInputs(
                IncomeTaxRate: 0m),
            OwnerActivity: new OwnerActivityInputs(
                OwnerInvestment: 0m,
                OwnerWithdrawals: ownerWithdrawals),
            ForecastControls: new ForecastControlInputs(
                BeginningCashMonth1: beginningCashMonth1,
                TargetCashPositiveMonth: targetCashPositiveMonth));
    }

    /// <summary>
    /// A scenario where the Cash_Positive_Rule already holds at p = 0: opening
    /// cash is large and every drain is zero, so Ending_Cash[m] stays at the
    /// opening balance for every month. This is the fast-path case that
    /// design §8.2 optimises for (R15.3, R15.4, DD12).
    /// </summary>
    private static ForecastInputs FastPathInputs() => MakeInputs(
        beginningCashMonth1: 1_000_000m,
        targetCashPositiveMonth: 24);

    /// <summary>
    /// A scenario where the fast path fails and the solver must iterate:
    /// modest opening cash and a positive per-month <c>Owner_Withdrawals</c>
    /// drain that only enough rental revenue can offset. With
    /// <c>Total_Sqft = 10,000</c> and <c>Percentage_Available_For_Rent = 1</c>
    /// the default occupancy ramp (10%, 20%, …, 100%, 100%, …) produces
    /// growing Rented_Sqft, so a modest positive Flat_Price_Per_Sqft — a
    /// few tenths of a dollar under this scenario — satisfies the rule.
    /// This gives the solver's geometric upper-bound expansion and bisection
    /// something meaningful to converge on.
    /// </summary>
    private static ForecastInputs IterationInputs() => MakeInputs(
        beginningCashMonth1: 1_000m,
        ownerWithdrawals: 100m,
        totalSqft: 10_000m,
        percentageAvailableForRent: 1m,
        targetCashPositiveMonth: 24);

    /// <summary>
    /// A scenario in which no <c>Flat_Price_Per_Sqft</c> can satisfy the rule:
    /// there is no rentable capacity (<c>Total_Sqft = 0</c>) so revenue is
    /// identically zero for every p, and the constant <c>Owner_Withdrawals</c>
    /// drain guarantees Ending_Cash goes strictly negative from Month 1 on.
    /// The solver must return <see cref="SolverResult.Failure"/> once its
    /// safety limit is breached (design §8.8, Requirement 15.11).
    /// </summary>
    private static ForecastInputs NeverSatisfiedInputs() => MakeInputs(
        beginningCashMonth1: 0m,
        ownerWithdrawals: 10m,
        totalSqft: 0m,
        percentageAvailableForRent: 1m,
        targetCashPositiveMonth: 1);

    // ================================================================
    // Fast path — Satisfies(0m) ⇒ Success(0, …, iterations = 1)
    // (design §8.2; R15.3, R15.4)
    // ================================================================

    [Fact]
    public void Solve_ReturnsZeroPrice_WithSingleIteration_WhenRuleAlreadyHoldsAtZero()
    {
        // Precondition: the rule really does hold at p = 0 for this scenario.
        // Verifying this against the real calculator pins the semantics of
        // "fast path" and prevents a silent regression where the solver
        // reports 0 for a scenario that doesn't actually satisfy at 0.
        var calc = MakeForecastCalculator();
        var inputs = FastPathInputs();
        Assert.True(calc.Compute(inputs, 0m).CashPositiveRuleSatisfied);

        var solver = MakeSolver();

        var result = solver.Solve(inputs);

        var success = Assert.IsType<SolverResult.Success>(result);
        Assert.Equal(0m, success.FlatPricePerSqft);
        Assert.Equal(1, success.Iterations);
        Assert.True(success.Forecast.CashPositiveRuleSatisfied);
        // §8.7 & DD-round-trip: the returned Forecast must be the one produced
        // at the returned FlatPricePerSqft, not a stale artefact.
        Assert.Equal(0m, success.Forecast.FlatPricePerSqft);
    }

    // ================================================================
    // Geometric upper-bound expansion beyond the fast path
    // (design §8.3; R15.5)
    // ================================================================

    [Fact]
    public void Solve_DoesNotFastPath_WhenZeroPriceFailsRule_PreconditionForIterationScenario()
    {
        // Guardrail: assert the fixture's contract that Sat(0) = false, so
        // the follow-up "expansion happens" tests aren't secretly passing
        // through the fast-path branch.
        var calc = MakeForecastCalculator();
        var forecast = calc.Compute(IterationInputs(), 0m);

        Assert.False(forecast.CashPositiveRuleSatisfied);
    }

    [Fact]
    public void Solve_UsesGeometricExpansionAndBisection_ReturningPositivePrice_WhenFastPathFails()
    {
        // With Sat(0) = false, the solver must:
        //   (a) leave the fast path unclaimed,
        //   (b) run the geometric expansion until Sat(high) = true (§8.3),
        //   (c) bisect to Solver_Tolerance (§8.4).
        // We assert the observable consequences: iteration count > 1 (at
        // least one iteration past the fast path check) and a positive
        // returned price.
        var solver = MakeSolver();

        var result = solver.Solve(IterationInputs());

        var success = Assert.IsType<SolverResult.Success>(result);
        Assert.True(
            success.FlatPricePerSqft > 0m,
            $"Expected a positive Flat_Price_Per_Sqft, got {success.FlatPricePerSqft}.");
        Assert.True(
            success.Iterations > 1,
            $"Expected Iterations > 1 (fast path skipped, expansion + bisection ran), got {success.Iterations}.");
        // §15.7 (R15.7): the returned Forecast is the fresh run at the
        // returned price, not a bisection midpoint's forecast.
        Assert.Equal(success.FlatPricePerSqft, success.Forecast.FlatPricePerSqft);
    }

    // ================================================================
    // Final answer is rounded UP to Currency_Precision
    // (design §8.5; R15.8)
    // ================================================================

    [Fact]
    public void Solve_FlatPricePerSqft_IsRoundedUpToTwoDecimals()
    {
        // §8.5: rounded = Math.Ceiling(high * 100m) / 100m. The returned
        // price must be exactly cent-precise — no sub-cent residue.
        var solver = MakeSolver();

        var result = solver.Solve(IterationInputs());

        var success = Assert.IsType<SolverResult.Success>(result);
        var scaled = success.FlatPricePerSqft * 100m;

        // A decimal is cent-precise iff its value in cents has no fractional part.
        Assert.Equal(Math.Truncate(scaled), scaled);

        // Round-trip equivalence: applying the §8.5 ceiling operation to the
        // returned price must be a no-op.
        var reroundedUp = Math.Ceiling(success.FlatPricePerSqft * 100m) / 100m;
        Assert.Equal(reroundedUp, success.FlatPricePerSqft);
    }

    [Fact]
    public void Solve_FastPathZero_IsAlreadyCentPrecise()
    {
        // The fast-path answer of 0 is trivially cent-precise; we still
        // exercise the invariant so the rounding contract is honoured
        // across both branches of §8.
        var solver = MakeSolver();

        var result = solver.Solve(FastPathInputs());

        var success = Assert.IsType<SolverResult.Success>(result);
        Assert.Equal(0m, success.FlatPricePerSqft);
        Assert.Equal(0m, success.FlatPricePerSqft * 100m);
    }

    // ================================================================
    // Post-rounding re-verification (direct or after incremental raises)
    // (design §8.6, §8.7; R15.9, R15.10, R27.6)
    // ================================================================

    [Fact]
    public void Solve_PostRoundingReverification_SucceedsAtReturnedPrice_ForFastPath()
    {
        var solver = MakeSolver();

        var result = solver.Solve(FastPathInputs());

        var success = Assert.IsType<SolverResult.Success>(result);
        Assert.True(success.Forecast.CashPositiveRuleSatisfied);
    }

    [Fact]
    public void Solve_PostRoundingReverification_SucceedsAtReturnedPrice_ForIterationScenario()
    {
        // §8.7: the emitted Forecast is a fresh Compute at the rounded price
        // and must itself satisfy the rule. §8.6 lets the solver raise by
        // Currency_Precision as many times as needed until Sat(rounded) holds;
        // either way, the FINAL Forecast has CashPositiveRuleSatisfied = true.
        var solver = MakeSolver();
        var inputs = IterationInputs();

        var result = solver.Solve(inputs);

        var success = Assert.IsType<SolverResult.Success>(result);
        Assert.True(success.Forecast.CashPositiveRuleSatisfied);
        Assert.Equal(success.FlatPricePerSqft, success.Forecast.FlatPricePerSqft);

        // Cross-check: an independent Compute at the same price agrees.
        // This defends against a solver that caches an old Forecast rather
        // than performing the R15.7 fresh run.
        var independent = MakeForecastCalculator().Compute(inputs, success.FlatPricePerSqft);
        Assert.True(independent.CashPositiveRuleSatisfied);
    }

    // ================================================================
    // Bisection convergence + cent-level minimality
    // (design §8.4, §8.5; R15.1, R15.6)
    // ================================================================

    [Fact]
    public void Solve_CentLevelMinimality_LoweringByOneCentBreaksTheRule()
    {
        // R15.1 asks for the MINIMUM nonnegative Flat_Price_Per_Sqft that
        // satisfies the rule; after §8.5's UP-rounding, "minimum" is
        // enforced at cent granularity. Either p = 0 (which trivially
        // satisfies the minimality claim vacuously) or p − 0.01 is
        // strictly infeasible.
        var solver = MakeSolver();
        var inputs = IterationInputs();

        var result = solver.Solve(inputs);

        var success = Assert.IsType<SolverResult.Success>(result);

        if (success.FlatPricePerSqft > 0m)
        {
            var oneCentLower = success.FlatPricePerSqft - ForecastConstants.CurrencyPrecision;
            var forecast = MakeForecastCalculator().Compute(inputs, oneCentLower);
            Assert.False(
                forecast.CashPositiveRuleSatisfied,
                $"Expected Compute(inputs, {oneCentLower}) to fail the rule (cent-level minimality); "
                    + $"solver returned {success.FlatPricePerSqft}.");
        }
    }

    [Fact]
    public void Solve_CentLevelMinimality_HoldsInFastPathScenario()
    {
        // The p = 0 fast-path branch satisfies minimality vacuously: there
        // is no lower nonnegative price to test. Documenting the branch
        // explicitly guards against a future change that starts returning
        // negative candidates.
        var solver = MakeSolver();

        var result = solver.Solve(FastPathInputs());

        var success = Assert.IsType<SolverResult.Success>(result);
        Assert.Equal(0m, success.FlatPricePerSqft);
        Assert.True(success.FlatPricePerSqft >= 0m);
    }

    // ================================================================
    // Safety limit breach ⇒ Failure with iterations = SolverSafetyLimit + 1
    // (design §8.8; R15.11)
    // ================================================================

    [Fact]
    public void Solve_ReturnsFailure_WithoutThrowing_WhenSafetyLimitIsExceeded()
    {
        // NeverSatisfiedInputs has Total_Sqft = 0 (zero revenue at every p)
        // and positive Owner_Withdrawals, so Ending_Cash is strictly
        // negative from Month 1 onward for every candidate. The solver
        // exhausts its safety limit across (in order) the geometric
        // expansion, bisection, and post-rounding raise loops.
        //
        // R15.11 mandates: return SolverResult.Failure, do NOT throw an
        // unhandled exception, do NOT loop indefinitely. Task 37 pins the
        // exact iteration count reported on the failure envelope:
        // Iterations = Solver_Safety_Limit + 1.
        var solver = MakeSolver();
        var inputs = NeverSatisfiedInputs();

        // Precondition sanity: the calculator itself agrees this scenario
        // is hopeless at some representative candidate prices, so the
        // failure below is genuinely the solver's safety-limit behaviour
        // rather than a Bug-Fixture-Doesn't-Do-What-It-Says accident.
        var calc = MakeForecastCalculator();
        Assert.False(calc.Compute(inputs, 0m).CashPositiveRuleSatisfied);
        Assert.False(calc.Compute(inputs, 1m).CashPositiveRuleSatisfied);
        Assert.False(calc.Compute(inputs, 1_000_000m).CashPositiveRuleSatisfied);

        // Under this fixture Solve MUST return a Failure without throwing.
        // If the implementation permitted an unhandled exception to escape,
        // xUnit would report the exception rather than a plain assertion
        // failure, which is itself a violation of R15.11 that this test
        // detects — no explicit try/catch is needed here.
        var result = solver.Solve(inputs);

        var failure = Assert.IsType<SolverResult.Failure>(result);
        Assert.Equal(ForecastConstants.SolverSafetyLimit + 1, failure.Iterations);
        Assert.False(
            string.IsNullOrWhiteSpace(failure.Reason),
            "Failure.Reason must be a human-readable message (R15.11).");
    }

    // ================================================================
    // Determinism — equal inputs ⇒ equal solver results
    // (R15.2, R22.2)
    // ================================================================

    [Fact]
    public void Solve_IsDeterministic_EqualIterationInputsProduceEqualPriceAndIterations()
    {
        // Two calls on the same solver instance with the same inputs.
        var solver = MakeSolver();
        var inputs = IterationInputs();

        var a = Assert.IsType<SolverResult.Success>(solver.Solve(inputs));
        var b = Assert.IsType<SolverResult.Success>(solver.Solve(inputs));

        Assert.Equal(a.FlatPricePerSqft, b.FlatPricePerSqft);
        Assert.Equal(a.Iterations, b.Iterations);
        Assert.Equal(
            a.Forecast.CashPositiveRuleSatisfied,
            b.Forecast.CashPositiveRuleSatisfied);
        Assert.Equal(
            a.Forecast.FirstSustainedNonnegativeMonth,
            b.Forecast.FirstSustainedNonnegativeMonth);
    }

    [Fact]
    public void Solve_IsDeterministic_AcrossFreshSolverInstances()
    {
        // A different solver instance built with a different calculator
        // instance (but identical inputs) must yield the same answer.
        // This defends against hidden state — e.g., an accidental static
        // cache in PriceSolver — that would violate R15.2.
        var inputs = IterationInputs();

        var a = Assert.IsType<SolverResult.Success>(MakeSolver().Solve(inputs));
        var b = Assert.IsType<SolverResult.Success>(MakeSolver().Solve(inputs));

        Assert.Equal(a.FlatPricePerSqft, b.FlatPricePerSqft);
        Assert.Equal(a.Iterations, b.Iterations);
    }

    [Fact]
    public void Solve_IsDeterministic_FastPathScenarioReturnsSameResultTwice()
    {
        // The fast-path branch has no numeric iteration; determinism there
        // is a trivial invariant of the Sat(0) predicate, but we still pin
        // it so future changes to §8.2 don't sneak in nondeterminism.
        var solver = MakeSolver();
        var inputs = FastPathInputs();

        var a = Assert.IsType<SolverResult.Success>(solver.Solve(inputs));
        var b = Assert.IsType<SolverResult.Success>(solver.Solve(inputs));

        Assert.Equal(a.FlatPricePerSqft, b.FlatPricePerSqft);
        Assert.Equal(a.Iterations, b.Iterations);
    }

    [Fact]
    public void Solve_IsDeterministic_SafetyLimitFailureIsReproducible()
    {
        // Two runs against the never-satisfies scenario must produce two
        // identical Failure envelopes. This exercises R15.2 on the failure
        // path — a solver that reports different Reason strings or
        // different Iterations across identical runs is not deterministic.
        var solver = MakeSolver();
        var inputs = NeverSatisfiedInputs();

        var a = Assert.IsType<SolverResult.Failure>(solver.Solve(inputs));
        var b = Assert.IsType<SolverResult.Failure>(solver.Solve(inputs));

        Assert.Equal(a.Iterations, b.Iterations);
        Assert.Equal(a.Reason, b.Reason);
    }
}
