using RehearsalForecast.Core.Constants;
using RehearsalForecast.Core.Domain;

namespace RehearsalForecast.Core.Forecast;

/// <summary>
/// Pass 7 of the forecast pipeline (design §6.7): sizes the total capital
/// requirement and the financing gap covered by the loan, then materialises
/// the three "in-month" cash-timing vectors that later passes consume.
/// </summary>
/// <remarks>
/// <para>
/// For a given <see cref="CapitalInputs"/> and scalar owner investment:
/// </para>
/// <code>
/// Total_Capital  = Equipment + TotalImprovementCost + BuildingPurchaseCost + OtherCapitalCost
/// Loan_Proceeds  = Max(Total_Capital − Owner_Investment, 0)
/// Capital_Expenditures_In_Month[1] = Total_Capital   (else 0)
/// Owner_Investment_In_Month[1]     = Owner_Investment (else 0)
/// Loan_Proceeds_In_Month[1]        = Loan_Proceeds    (else 0)
/// </code>
/// <para>
/// All three vectors have exactly <see cref="ForecastConstants.ForecastMonths"/>
/// (36) entries. Month <c>m</c> lives at index <c>m − 1</c>, so Month 1 is at
/// index <c>0</c> and months 2 through 36 are zero (Requirements 9.2, 9.3, 10.3,
/// 10.4).
/// </para>
/// <para>
/// The <see cref="Math.Max(decimal, decimal)"/> clamp on <c>Loan_Proceeds</c>
/// (Requirement 10.1) means owner over-investment cannot produce a negative
/// loan. When the owner over-invests, <c>Loan_Proceeds</c> is zero yet
/// <c>Capital_Expenditures_In_Month[1]</c> still equals <c>Total_Capital</c>
/// (Requirement 10.2) — the excess owner cash flows through the cash-flow
/// roll-forward via <c>Owner_Investment_In_Month[1]</c>, not by shrinking
/// capex.
/// </para>
/// <para>
/// This helper is deliberately <see langword="internal"/>: it is a per-pass
/// building block used by <c>ForecastCalculator</c> and exposed to the test
/// project via <c>InternalsVisibleTo</c>. Every intermediate value is
/// <see cref="decimal"/> (Requirement 19.1); no <see cref="double"/> or
/// <see cref="float"/> is used.
/// </para>
/// </remarks>
internal static class CapitalCalculator
{
    /// <summary>
    /// Computes <c>Total_Capital</c>, <c>Loan_Proceeds</c>, and the three
    /// Month-1 timing vectors for the supplied capital line items and owner
    /// investment.
    /// </summary>
    /// <param name="capital">
    /// The four one-time capital line items (Requirement 9.1). Must be
    /// non-null.
    /// </param>
    /// <param name="ownerInvestment">
    /// The scalar owner investment amount in USD (Requirement 10.3). No
    /// upper bound is enforced here — owner over-investment is allowed
    /// (Requirement 10.5) and simply clamps <c>Loan_Proceeds</c> to zero.
    /// </param>
    /// <returns>
    /// A <see cref="CapitalResult"/> whose scalar fields carry the sizing
    /// figures and whose three <see cref="IReadOnlyList{T}"/> fields each
    /// hold exactly <see cref="ForecastConstants.ForecastMonths"/> (36)
    /// decimal values with Month-1 timing.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="capital"/> is <see langword="null"/>.
    /// </exception>
    internal static CapitalResult Compute(CapitalInputs capital, decimal ownerInvestment)
    {
        ArgumentNullException.ThrowIfNull(capital);

        // Requirement 9.1: Total_Capital sums exactly the four capital line items.
        decimal totalCapital =
            capital.Equipment
            + capital.TotalImprovementCost
            + capital.BuildingPurchaseCost
            + capital.OtherCapitalCost;

        // Requirement 10.1: clamp to zero so owner over-investment cannot
        // produce a negative loan (Requirement 10.2).
        decimal loanProceeds = Math.Max(totalCapital - ownerInvestment, 0m);

        // All three per-month vectors are length 36 with Month 1 at index 0
        // and zeros for months 2..36 (Requirements 9.2, 9.3, 10.3, 10.4).
        // `new decimal[N]` initialises every element to 0m by default.
        var capexInMonth = new decimal[ForecastConstants.ForecastMonths];
        var ownerInMonth = new decimal[ForecastConstants.ForecastMonths];
        var loanInMonth = new decimal[ForecastConstants.ForecastMonths];

        capexInMonth[0] = totalCapital;
        ownerInMonth[0] = ownerInvestment;
        loanInMonth[0] = loanProceeds;

        return new CapitalResult(
            TotalCapital: totalCapital,
            LoanProceeds: loanProceeds,
            CapitalExpendituresInMonth: capexInMonth,
            OwnerInvestmentInMonth: ownerInMonth,
            LoanProceedsInMonth: loanInMonth);
    }
}

/// <summary>
/// The outputs of <see cref="CapitalCalculator.Compute"/> (design §6.7).
/// </summary>
/// <param name="TotalCapital">
/// Sum of the four capital line items (Requirement 9.1). Nonnegative for any
/// nonnegative <see cref="CapitalInputs"/>.
/// </param>
/// <param name="LoanProceeds">
/// <c>Max(TotalCapital − Owner_Investment, 0)</c> (Requirement 10.1); zero
/// when the owner funds the entire capital requirement (Requirement 10.2).
/// </param>
/// <param name="CapitalExpendituresInMonth">
/// 36-entry vector: index 0 (Month 1) equals <see cref="TotalCapital"/>,
/// indices 1..35 are 0 (Requirements 9.2, 9.3). Not netted against owner
/// investment — the full capital amount is still spent even when the owner
/// over-invests (Requirement 10.2).
/// </param>
/// <param name="OwnerInvestmentInMonth">
/// 36-entry vector: index 0 (Month 1) equals the scalar <c>ownerInvestment</c>
/// argument to <see cref="CapitalCalculator.Compute"/>, indices 1..35 are 0
/// (Requirement 10.3).
/// </param>
/// <param name="LoanProceedsInMonth">
/// 36-entry vector: index 0 (Month 1) equals <see cref="LoanProceeds"/>,
/// indices 1..35 are 0 (Requirement 10.4).
/// </param>
internal sealed record CapitalResult(
    decimal TotalCapital,
    decimal LoanProceeds,
    IReadOnlyList<decimal> CapitalExpendituresInMonth,
    IReadOnlyList<decimal> OwnerInvestmentInMonth,
    IReadOnlyList<decimal> LoanProceedsInMonth);
