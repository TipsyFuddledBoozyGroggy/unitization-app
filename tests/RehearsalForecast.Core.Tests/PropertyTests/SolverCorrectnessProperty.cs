// Property-based test for Property 11 — Solver correctness contract
// (design §10, Property 11; §15.4).
//
// Property 11 states that for any valid ForecastInputs, Solver.Solve(inputs)
// produces one of two outcomes, and both obey the following:
//
//   1. Monotonicity underpins bisection: cash-positive predicate is
//      monotone non-decreasing in Flat_Price_Per_Sqft.                     (§8.9)
//   2. Success case (p, forecast, iterations):
//        - p ≥ 0
//        - p == round_to_cents(p) (cent-precise)
//        - forecast == Compute(inputs, p) and CashPositiveRuleSatisfied
//        - Cent-level minimality: either p == 0, or
//          Compute(inputs, p − 0.01).CashPositiveRuleSatisfied == false.
//        - iterations ≤ SolverSafetyLimit                                   (R15.1–R15.10)
//   3. Failure case (reason, iterations):
//        - iterations == SolverSafetyLimit + 1
//        - Solve did NOT throw and did NOT loop forever                    (R15.11)
//   4. Determinism: repeated Solve(inputs) returns equal results.          (R15.2)
//
// The test drives the public solver seam end-to-end against the real
// PriceSolver + ForecastCalculator + LoanCalculator composition, matching
// how the web layer calls it. Each 100-iteration run therefore exercises the
// full pipeline over a distribution of bounded input configurations.
//
// Bounding strategy: the solver must converge quickly for the property to
// be tractable, so every drain (owner withdrawals, marketing, ops, etc.) is
// held small and the rentable capacity is generated at meaningful sizes so
// a modest positive Flat_Price_Per_Sqft usually satisfies the rule. The
// target month is bounded to [12, 36] so a growing occupancy ramp has time
// to close the gap. All arithmetic remains in decimal per Requirement 19.1
// / R15.12.
//
// Validates: Requirements 15.1, 15.2, 15.3, 15.4, 15.5, 15.6, 15.7, 15.8,
//                          15.9, 15.10, 15.11, 15.12, 15.13, 27.6

using FsCheck.Xunit;
using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;
using RehearsalForecast.Core.Loan;
using RehearsalForecast.Core.Schedules;
using RehearsalForecast.Core.Solving;
using Xunit;

namespace RehearsalForecast.Core.Tests.PropertyTests;

public class SolverCorrectnessProperty
{
    // ------------------------------------------------------------------
    // Bounded generators (kept small so the solver converges within its
    // safety budget on every generated scenario).
    // ------------------------------------------------------------------

    /// <summary>Nonnegative USD amount in [0, ~10,000) with cent precision.</summary>
    private static decimal BoundSmallMoney(int raw) =>
        (decimal)(Math.Abs((long)raw) % 1_000_000L) / 100m;

    /// <summary>Owner withdrawal in [0, ~1000). Kept small so the solver has
    /// a shot at balancing the drain within reasonable prices.</summary>
    private static decimal BoundWithdrawal(int raw) =>
        (decimal)(Math.Abs((long)raw) % 100_000L) / 100m;

    /// <summary>Total sqft in [1000, 11000] to guarantee meaningful rentable
    /// capacity so revenue can grow with the flat price.</summary>
    private static decimal BoundTotalSqft(int raw) =>
        1000m + (decimal)(Math.Abs((long)raw) % 10_001L);

    /// <summary>Percentage available in [0.5, 1.0] so rentable capacity is
    /// substantial and revenue can outstrip drains at modest prices.</summary>
    private static decimal BoundPercentage(int raw) =>
        0.5m + ((decimal)(Math.Abs((long)raw) % 51L) / 100m);

    /// <summary>Income tax rate in [0, 0.35] — realistic range.</summary>
    private static decimal BoundTaxRate(int raw) =>
        (decimal)(Math.Abs((long)raw) % 36L) / 100m;

    /// <summary>Target month in [12, 36] so the occupancy ramp has time to
    /// grow before the rule kicks in.</summary>
    private static int BoundTargetMonth(int raw) =>
        (int)(Math.Abs((long)raw) % 25L) + 12;

    /// <summary>Loan term in [12, 60] months.</summary>
    private static int BoundLoanTerm(int raw) =>
        (int)(Math.Abs((long)raw) % 49L) + 12;

    private static MonthlySchedule<decimal> Zero() =>
        MonthlySchedule<decimal>.Constant(0m);

    private static MonthlySchedule<decimal> BoundedMonthly(int raw) =>
        MonthlySchedule<decimal>.Constant(BoundSmallMoney(raw));

    /// <summary>
    /// Assembles a bounded <see cref="ForecastInputs"/>. Capital line items
    /// and owner investment are zero so the loan is trivial (Loan_Proceeds = 0);
    /// this keeps the amortization pass quick while still exercising the
    /// solver's monotonicity in Flat_Price_Per_Sqft (revenue enters through
    /// Rented_Sqft × Monthly_Price_Per_Sqft regardless of loan state).
    /// </summary>
    private static ForecastInputs MakeInputs(
        int rawTotalSqft,
        int rawPercentage,
        int rawWithdrawal,
        int rawMarketing,
        int rawOperations,
        int rawTaxRate,
        int rawTargetMonth,
        int rawLoanTerm,
        int rawBeginningCash)
    {
        return new ForecastInputs(
            Capital: new CapitalInputs(
                Equipment: 0m,
                TotalImprovementCost: 0m,
                BuildingPurchaseCost: 0m,
                OtherCapitalCost: 0m),
            Marketing: new MarketingInputs(
                Print: BoundedMonthly(rawMarketing),
                Search: Zero(),
                Social: Zero(),
                OtherMarketing: Zero()),
            Operations: new OperationsInputs(
                Accounting: BoundedMonthly(rawOperations),
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
                TotalSqft: BoundTotalSqft(rawTotalSqft),
                PercentageAvailableForRent: BoundPercentage(rawPercentage),
                TotalBuildingCost: 0m,
                LandValue: 0m,
                DepreciationPeriodYears: 30,
                Occupancy: new OccupancySchedule(UseDefault: true, UserRates: null)),
            Loan: new LoanInputs(
                AnnualLoanInterestRate: 0m,
                LoanTermMonths: BoundLoanTerm(rawLoanTerm)),
            Taxes: new TaxInputs(IncomeTaxRate: BoundTaxRate(rawTaxRate)),
            OwnerActivity: new OwnerActivityInputs(
                OwnerInvestment: 0m,
                OwnerWithdrawals: BoundWithdrawal(rawWithdrawal)),
            ForecastControls: new ForecastControlInputs(
                BeginningCashMonth1: BoundSmallMoney(rawBeginningCash),
                TargetCashPositiveMonth: BoundTargetMonth(rawTargetMonth)));
    }

    private static PriceSolver MakeSolver() =>
        new PriceSolver(new ForecastCalculator(new LoanCalculator()));

    // ------------------------------------------------------------------
    // Property 11 — solver correctness contract
    //
    // Validates: Requirements 15.1, 15.2, 15.3, 15.4, 15.5, 15.6, 15.7,
    //                          15.8, 15.9, 15.10, 15.11, 15.12, 15.13, 27.6
    // ------------------------------------------------------------------

    [Property]
    public void Property_11_Solver_Correctness_Contract(
        int rawTotalSqft,
        int rawPercentage,
        int rawWithdrawal,
        int rawMarketing,
        int rawOperations,
        int rawTaxRate,
        int rawTargetMonth,
        int rawLoanTerm,
        int rawBeginningCash)
    {
        var inputs = MakeInputs(
            rawTotalSqft,
            rawPercentage,
            rawWithdrawal,
            rawMarketing,
            rawOperations,
            rawTaxRate,
            rawTargetMonth,
            rawLoanTerm,
            rawBeginningCash);

        var solver = MakeSolver();
        var result = solver.Solve(inputs);

        // Determinism (R15.2): a second Solve on the same inputs returns
        // an equal result. Positional records give us structural equality
        // for free via record.Equals; two Success or two Failure results
        // with matching fields compare equal.
        var second = MakeSolver().Solve(inputs);
        Assert.Equal(result, second);

        switch (result)
        {
            case SolverResult.Success success:
                AssertSuccessInvariants(success, inputs);
                break;

            case SolverResult.Failure failure:
                AssertFailureInvariants(failure);
                break;

            default:
                Assert.Fail($"Unknown SolverResult variant: {result.GetType().FullName}");
                break;
        }
    }

    /// <summary>
    /// Verifies every invariant a <see cref="SolverResult.Success"/> must
    /// uphold per Property 11 bullets 2 and 4 (R15.1, R15.7, R15.8, R15.10).
    /// </summary>
    private static void AssertSuccessInvariants(SolverResult.Success success, ForecastInputs inputs)
    {
        // R15.1 / R15.4: Flat_Price_Per_Sqft is nonnegative (zero permitted).
        Assert.True(success.FlatPricePerSqft >= 0m,
            $"Expected Flat_Price_Per_Sqft ≥ 0, got {success.FlatPricePerSqft}.");

        // R15.8: cent-precise. Math.Ceiling(x * 100m) / 100m must be a
        // no-op when applied to a cent-precise decimal.
        var ceilingToCents = Math.Ceiling(success.FlatPricePerSqft * 100m) / 100m;
        Assert.Equal(ceilingToCents, success.FlatPricePerSqft);

        // R15.7 / R15.9: the embedded forecast is a fresh Compute at the
        // returned price and satisfies the rule.
        Assert.Equal(success.FlatPricePerSqft, success.Forecast.FlatPricePerSqft);
        Assert.True(success.Forecast.CashPositiveRuleSatisfied,
            "Expected Success.Forecast.CashPositiveRuleSatisfied = true.");

        // Cross-check R15.7 by recomputing independently at the same price.
        var independentCalc = new ForecastCalculator(new LoanCalculator());
        var independent = independentCalc.Compute(inputs, success.FlatPricePerSqft);
        Assert.True(independent.CashPositiveRuleSatisfied,
            "Independent Compute at the returned price must also satisfy the rule.");

        // R15.11: safety limit was respected on success.
        Assert.True(success.Iterations <= ForecastConstants.SolverSafetyLimit,
            $"Iterations ({success.Iterations}) must not exceed SolverSafetyLimit "
            + $"({ForecastConstants.SolverSafetyLimit}) on Success.");

        // Cent-level minimality (R15.1 at cent granularity after R15.8's
        // UP-rounding): either p == 0 (vacuously minimal), or p − 0.01 is
        // strictly infeasible.
        if (success.FlatPricePerSqft > 0m)
        {
            var oneCentLower = success.FlatPricePerSqft - ForecastConstants.CurrencyPrecision;
            var forecast = independentCalc.Compute(inputs, oneCentLower);
            Assert.False(forecast.CashPositiveRuleSatisfied,
                $"Cent-level minimality violated: Compute(inputs, {oneCentLower}) "
                + $"satisfied the rule; solver returned {success.FlatPricePerSqft}.");
        }
    }

    /// <summary>
    /// Verifies every invariant a <see cref="SolverResult.Failure"/> must
    /// uphold per Property 11 bullet 3 (R15.11). The parent test method
    /// having returned normally already witnesses "did not throw" and
    /// "did not loop forever".
    /// </summary>
    private static void AssertFailureInvariants(SolverResult.Failure failure)
    {
        // R15.11: safety limit was breached. Design §8.8 terminates each
        // guarded loop the instant iterations exceeds SolverSafetyLimit —
        // that is precisely SolverSafetyLimit + 1.
        Assert.Equal(ForecastConstants.SolverSafetyLimit + 1, failure.Iterations);

        // R15.11: the failure carries a human-readable reason.
        Assert.False(string.IsNullOrWhiteSpace(failure.Reason));
    }
}
