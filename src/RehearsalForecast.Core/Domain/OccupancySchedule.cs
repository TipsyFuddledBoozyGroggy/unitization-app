namespace RehearsalForecast.Core.Domain;

/// <summary>
/// Discriminated occupancy input. When <see cref="UseDefault"/> is <see langword="true"/>
/// the calculator uses the built-in ramp <c>Occupancy_Rate[m] = Min(m * 0.10, 1.00)</c>
/// (Requirement 4.1). Otherwise it uses the 36 user-supplied rates in
/// <see cref="UserRates"/> (Requirement 4.2).
/// </summary>
/// <param name="UseDefault">
/// <see langword="true"/> to use the built-in ramp <c>Min(m * 0.10, 1.00)</c>
/// (Requirement 4.1); <see langword="false"/> to use <see cref="UserRates"/>
/// (Requirement 4.2).
/// </param>
/// <param name="UserRates">
/// Exactly 36 user-supplied monthly occupancy rates, each a <see cref="decimal"/> in
/// the inclusive range <c>[0, 1]</c> (Requirement 4.2). Contract only, not enforced
/// by the type; validated by <c>InputValidator</c>. Must be <see langword="null"/>
/// when <see cref="UseDefault"/> is <see langword="true"/>.
/// </param>
public sealed record OccupancySchedule(
    bool UseDefault,
    IReadOnlyList<decimal>? UserRates);
