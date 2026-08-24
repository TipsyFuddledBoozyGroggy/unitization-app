// Shared helpers for property-based tests in
// RehearsalForecast.Core.Tests.PropertyTests.
//
// Each of the six [Property]-based test files (Properties 1..6, design §10)
// uses these helpers to bound FsCheck-generated primitives to realistic
// financial ranges and to construct valid `ForecastInputs` instances. All
// arithmetic is `decimal` per Requirement 19.

using System.Collections.Generic;
using RehearsalForecast.Core.Domain;
using RehearsalForecast.Core.Forecast;
using RehearsalForecast.Core.Schedules;

namespace RehearsalForecast.Core.Tests.PropertyTests;

internal static class PropertyTestHelpers
{
    // ==================================================================
    // Bounded primitive generators (see the "FsCheck generator recipes"
    // in the task prompt). All property tests parameterise `[Property]`
    // methods on `uint` / `int` and cast to `decimal` here so that the
    // FsCheck-generated distribution never escapes into extreme decimal
    // values that would overflow the calculator's own bounds.
    // ==================================================================

    /// <summary>
    /// Maps an unbounded <see cref="uint"/> into a nonnegative
    /// <see cref="decimal"/> in the range [0, 10_000] with two decimal
    /// places of resolution. Used for monetary amounts.
    /// </summary>
    internal static decimal MoneyFromRaw(uint raw) =>
        (decimal)(raw % 1_000_001u) / 100m;

    /// <summary>
    /// Maps an unbounded <see cref="uint"/> into a nonnegative
    /// <see cref="decimal"/> in the range [0, 1_000_000] with two-decimal
    /// resolution. Used for larger capital / opening-cash amounts.
    /// </summary>
    internal static decimal LargeMoneyFromRaw(uint raw) =>
        (decimal)(raw % 100_000_001u) / 100m;

    /// <summary>
    /// Maps an unbounded <see cref="uint"/> into a <see cref="decimal"/>
    /// rate in the inclusive range [0, 1] with 1/100 resolution.
    /// Used for percentages/rates.
    /// </summary>
    internal static decimal RateFromRaw(uint raw) =>
        (decimal)(raw % 101u) / 100m;

    /// <summary>
    /// Maps an unbounded <see cref="uint"/> into a nonnegative
    /// <see cref="decimal"/> in the range [0, 100_000] with two decimal
    /// resolution. Used for square footage.
    /// </summary>
    internal static decimal SqftFromRaw(uint raw) =>
        (decimal)(raw % 10_000_001u) / 100m;

    /// <summary>
    /// Maps an unbounded <see cref="int"/> into an integer month index in
    /// the range [1, 36] (target-cash-positive month).
    /// </summary>
    internal static int TargetMonthFromRaw(int raw)
    {
        var normalised = (raw % 36 + 36) % 36; // [0, 35]
        return normalised + 1;
    }

    /// <summary>
    /// Maps an unbounded <see cref="int"/> into a positive integer in the
    /// range [1, 120]. Used for loan term months and depreciation years.
    /// </summary>
    internal static int PositiveTermFromRaw(int raw, int max = 120)
    {
        var normalised = (raw % max + max) % max; // [0, max - 1]
        return normalised + 1;
    }

    // ==================================================================
    // Rate-vector helper (36 rates in [0, 1] derived from a single seed).
    // FsCheck cannot cheaply generate a fixed-length decimal[] with a
    // uniform in-range distribution; a small LCG over the seed keeps the
    // generated vector varied while remaining deterministic per seed.
    // ==================================================================

    /// <summary>
    /// Derives 36 occupancy rates in [0, 1] from a single <see cref="uint"/>
    /// seed. The rate for month <c>m</c> is <c>((seed_m) mod 101) / 100</c>
    /// where <c>seed_m</c> advances via a linear congruential step; this
    /// gives 100 varied vectors across the FsCheck iteration budget while
    /// remaining reproducible for any given seed.
    /// </summary>
    internal static IReadOnlyList<decimal> RatesVectorFromSeed(uint seed)
    {
        var rates = new decimal[36];
        var s = seed;
        for (var i = 0; i < 36; i++)
        {
            s = unchecked(s * 1_103_515_245u + 12_345u);
            rates[i] = (decimal)(s % 101u) / 100m;
        }
        return rates;
    }

    // ==================================================================
    // Baseline ForecastInputs construction.
    //
    // Each optional argument corresponds to a single input field. Every
    // schedulable field defaults to `MonthlySchedule<decimal>.Constant(...)`
    // with a modest baseline amount so the calculator runs end-to-end
    // without special-casing zero income or zero expenses. Callers pass
    // only the fields they want to vary; the rest stay pinned.
    // ==================================================================

    /// <summary>
    /// Constructs a valid <see cref="ForecastInputs"/> instance with all
    /// schedulable fields in <see cref="ScheduleMode.Constant"/> mode.
    /// Every optional parameter defaults to a modest baseline that keeps
    /// the calculator well-behaved (nonzero geometry, positive revenue
    /// possible under any nonnegative price, etc.).
    /// </summary>
    internal static ForecastInputs MakeInputs(
        decimal print = 100m,
        decimal search = 100m,
        decimal social = 100m,
        decimal otherMarketing = 100m,
        decimal accounting = 500m,
        decimal custodial = 500m,
        decimal gas = 500m,
        decimal insurance = 500m,
        decimal it = 500m,
        decimal officeSupplies = 500m,
        decimal professionalServices = 500m,
        decimal rentExpense = 500m,
        decimal repairs = 500m,
        decimal shipping = 500m,
        decimal propertyTax = 500m,
        decimal utilities = 500m,
        decimal wages = 5000m,
        decimal otherOperations = 500m,
        decimal totalSqft = 10_000m,
        decimal percentageAvailableForRent = 0.8m,
        decimal totalBuildingCost = 500_000m,
        decimal landValue = 100_000m,
        int depreciationPeriodYears = 30,
        OccupancySchedule? occupancy = null,
        decimal equipment = 50_000m,
        decimal totalImprovementCost = 100_000m,
        decimal buildingPurchaseCost = 400_000m,
        decimal otherCapitalCost = 20_000m,
        decimal annualLoanInterestRate = 0.06m,
        int loanTermMonths = 36,
        decimal incomeTaxRate = 0.21m,
        decimal ownerInvestment = 100_000m,
        decimal ownerWithdrawals = 2_000m,
        decimal beginningCashMonth1 = 50_000m,
        int targetCashPositiveMonth = 24)
    {
        return new ForecastInputs(
            Capital: new CapitalInputs(
                Equipment: equipment,
                TotalImprovementCost: totalImprovementCost,
                BuildingPurchaseCost: buildingPurchaseCost,
                OtherCapitalCost: otherCapitalCost),
            Marketing: new MarketingInputs(
                Print: MonthlySchedule<decimal>.Constant(print),
                Search: MonthlySchedule<decimal>.Constant(search),
                Social: MonthlySchedule<decimal>.Constant(social),
                OtherMarketing: MonthlySchedule<decimal>.Constant(otherMarketing)),
            Operations: new OperationsInputs(
                Accounting: MonthlySchedule<decimal>.Constant(accounting),
                Custodial: MonthlySchedule<decimal>.Constant(custodial),
                Gas: MonthlySchedule<decimal>.Constant(gas),
                Insurance: MonthlySchedule<decimal>.Constant(insurance),
                It: MonthlySchedule<decimal>.Constant(it),
                OfficeSupplies: MonthlySchedule<decimal>.Constant(officeSupplies),
                ProfessionalServices: MonthlySchedule<decimal>.Constant(professionalServices),
                RentExpense: MonthlySchedule<decimal>.Constant(rentExpense),
                Repairs: MonthlySchedule<decimal>.Constant(repairs),
                Shipping: MonthlySchedule<decimal>.Constant(shipping),
                PropertyTax: MonthlySchedule<decimal>.Constant(propertyTax),
                Utilities: MonthlySchedule<decimal>.Constant(utilities),
                Wages: MonthlySchedule<decimal>.Constant(wages),
                OtherOperations: MonthlySchedule<decimal>.Constant(otherOperations)),
            Building: new BuildingInputs(
                TotalSqft: totalSqft,
                PercentageAvailableForRent: percentageAvailableForRent,
                TotalBuildingCost: totalBuildingCost,
                LandValue: landValue,
                DepreciationPeriodYears: depreciationPeriodYears,
                Occupancy: occupancy ?? new OccupancySchedule(UseDefault: true, UserRates: null)),
            Loan: new LoanInputs(
                AnnualLoanInterestRate: annualLoanInterestRate,
                LoanTermMonths: loanTermMonths),
            Taxes: new TaxInputs(IncomeTaxRate: incomeTaxRate),
            OwnerActivity: new OwnerActivityInputs(
                OwnerInvestment: ownerInvestment,
                OwnerWithdrawals: ownerWithdrawals),
            ForecastControls: new ForecastControlInputs(
                BeginningCashMonth1: beginningCashMonth1,
                TargetCashPositiveMonth: targetCashPositiveMonth));
    }

    // ==================================================================
    // Calculator factory. Every property test needs the same
    // ForecastCalculator wired to a real LoanCalculator; centralising the
    // construction avoids duplicated ceremony across the six files.
    // ==================================================================

    /// <summary>
    /// Constructs a <see cref="ForecastCalculator"/> wired to a real
    /// <see cref="RehearsalForecast.Core.Loan.LoanCalculator"/>. Both types
    /// are stateless, so a fresh instance per call is cheap and keeps the
    /// tests free of shared state.
    /// </summary>
    internal static ForecastCalculator NewForecastCalculator()
    {
        return new ForecastCalculator(new RehearsalForecast.Core.Loan.LoanCalculator());
    }
}
