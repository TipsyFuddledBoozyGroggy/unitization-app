namespace RehearsalForecast.Core.Forecast;

/// <summary>
/// Complete result of one forecast computation: 36 monthly rows plus the summary
/// metrics required by the results page (Requirement 16.3, 16.4) and the outputs of
/// the cash-positive rule (Requirement 14).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Rows"/> must have exactly 36 entries, one per forecast month.
/// The producer (<c>ForecastCalculator</c>) is responsible for upholding this
/// contract; consumers may treat the length as invariant.
/// </para>
/// <para>
/// <see cref="FirstSustainedNonnegativeMonth"/> is nullable: a <c>null</c> value
/// represents the "None" case in Requirement 14.5 — i.e., there is no month
/// <c>M ∈ [1, 36]</c> such that <c>Ending_Cash[m] ≥ 0</c> for every <c>m ∈ [M, 36]</c>.
/// </para>
/// <para>Every monetary field is <see cref="decimal"/> (Requirement 19.1).</para>
/// <para>
/// Equality is structural on every field including <see cref="Rows"/>: two
/// <see cref="ForecastResult"/> instances compare equal iff every summary field
/// is equal and their <see cref="Rows"/> sequences are element-wise equal. This
/// makes the solver's determinism contract (Requirement 15.2) expressible as
/// simple record equality on <c>SolverResult.Success</c>.
/// </para>
/// </remarks>
/// <param name="TotalCapital">Sum of Equipment, Total_Improvement_Cost, Building_Purchase_Cost, and Other_Capital_Cost (Requirement 9.1).</param>
/// <param name="OwnerInvestment">Owner_Investment supplied by the user (Requirement 10.1).</param>
/// <param name="LoanProceeds">Derived financing: <c>max(TotalCapital − OwnerInvestment, 0)</c> (Requirement 10.3).</param>
/// <param name="RentableSqft"><c>Total_Sqft × Percentage_Available_For_Rent</c> (Requirement 3.1).</param>
/// <param name="TotalRentalUnits">Rentable-unit capacity of the facility, <c>ceil(RentableSqft / 150)</c> (Requirement 3.2).</param>
/// <param name="FlatPricePerSqft">The authoritative solver output: constant 36-month price per square foot.</param>
/// <param name="MonthlyPricePerSqft">Derived display value <c>FlatPricePerSqft / 36</c> (Requirement 5.1).</param>
/// <param name="TargetCashPositiveMonth">User-selected target month from <c>ForecastControlInputs.TargetCashPositiveMonth</c>.</param>
/// <param name="CashPositiveRuleSatisfied"><c>true</c> iff <c>Ending_Cash[m] ≥ 0</c> for every <c>m ∈ [TargetCashPositiveMonth, 36]</c> (Requirement 14.1).</param>
/// <param name="FirstSustainedNonnegativeMonth">Smallest <c>M ∈ [1, 36]</c> such that <c>Ending_Cash[m] ≥ 0</c> for every <c>m ∈ [M, 36]</c>, or <c>null</c> for "None" (Requirement 14.5).</param>
/// <param name="Rows">Exactly 36 monthly forecast rows, ordered by <see cref="MonthlyForecastRow.Month"/> ascending.</param>
public sealed record ForecastResult(
    decimal TotalCapital,
    decimal OwnerInvestment,
    decimal LoanProceeds,
    decimal RentableSqft,
    int TotalRentalUnits,
    decimal FlatPricePerSqft,
    decimal MonthlyPricePerSqft,
    int TargetCashPositiveMonth,
    bool CashPositiveRuleSatisfied,
    int? FirstSustainedNonnegativeMonth,
    IReadOnlyList<MonthlyForecastRow> Rows)
{
    /// <summary>
    /// Structural equality: two <see cref="ForecastResult"/> instances are equal
    /// iff every summary field is equal and their <see cref="Rows"/> sequences are
    /// element-wise equal. Overrides the compiler-generated record equality, which
    /// would otherwise compare <see cref="Rows"/> by reference (an
    /// <see cref="IReadOnlyList{T}"/> has no structural <c>Equals</c>).
    /// </summary>
    public bool Equals(ForecastResult? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return TotalCapital == other.TotalCapital
            && OwnerInvestment == other.OwnerInvestment
            && LoanProceeds == other.LoanProceeds
            && RentableSqft == other.RentableSqft
            && TotalRentalUnits == other.TotalRentalUnits
            && FlatPricePerSqft == other.FlatPricePerSqft
            && MonthlyPricePerSqft == other.MonthlyPricePerSqft
            && TargetCashPositiveMonth == other.TargetCashPositiveMonth
            && CashPositiveRuleSatisfied == other.CashPositiveRuleSatisfied
            && FirstSustainedNonnegativeMonth == other.FirstSustainedNonnegativeMonth
            && Rows.Count == other.Rows.Count
            && Rows.SequenceEqual(other.Rows);
    }

    /// <summary>
    /// Hash code consistent with the structural <see cref="Equals(ForecastResult?)"/>:
    /// combines every summary field plus the row count (element-wise hashing is
    /// unnecessary since the count is a strong discriminator and
    /// <see cref="Equals(ForecastResult?)"/> does the deep check on hash collisions).
    /// </summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TotalCapital);
        hash.Add(OwnerInvestment);
        hash.Add(LoanProceeds);
        hash.Add(RentableSqft);
        hash.Add(TotalRentalUnits);
        hash.Add(FlatPricePerSqft);
        hash.Add(MonthlyPricePerSqft);
        hash.Add(TargetCashPositiveMonth);
        hash.Add(CashPositiveRuleSatisfied);
        hash.Add(FirstSustainedNonnegativeMonth);
        hash.Add(Rows.Count);
        return hash.ToHashCode();
    }
}
