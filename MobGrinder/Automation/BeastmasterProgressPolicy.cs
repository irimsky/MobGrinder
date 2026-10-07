namespace MobGrinder;

/// <summary>Test runs must defeat a target before consulting its bestiary state.</summary>
public static class BeastmasterProgressPolicy
{
    public static bool ShouldCheckUnlocks(bool testMode, int confirmedDefeats) =>
        !testMode || confirmedDefeats > 0;

    public static bool IsComplete(bool testMode, int confirmedDefeats, bool dataReady, bool unlocked) =>
        ShouldCheckUnlocks(testMode, confirmedDefeats) && dataReady && unlocked;
}
