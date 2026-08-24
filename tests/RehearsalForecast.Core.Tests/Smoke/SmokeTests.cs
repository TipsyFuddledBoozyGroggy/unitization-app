// Smoke tests for the RehearsalForecast.Core.Tests project.
//
// Purpose: prove that both the xUnit runner (via [Fact]) and the FsCheck.Xunit
// runner (via [Property]) execute under `dotnet test` from the repository root.
//
// Property-test conventions for this suite:
//   * Every [Property] runs at least 100 iterations (the FsCheck default,
//     Config.QuickThrowOnFailure). Individual tests may raise this bound but
//     must never lower it. See design.md §15.1 and §15.7.
//   * Failures are deterministically reproducible: when investigating a
//     counterexample, copy the "Replay" hint FsCheck prints into a
//     [Property(Replay = "seed1,seed2,size")] attribute on the failing test
//     to re-run with the same PRNG state.

using FsCheck.Xunit;
using Xunit;

namespace RehearsalForecast.Core.Tests.Smoke;

public class SmokeTests
{
    [Fact]
    public void Xunit_Fact_Runs()
    {
        Assert.True(true);
    }

    [Property]
    public bool Fscheck_Property_Runs(int _) => true;
}
