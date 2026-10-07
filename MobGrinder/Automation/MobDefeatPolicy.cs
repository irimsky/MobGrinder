namespace MobGrinder;

/// <summary>Leaving combat or losing a target is not evidence of a kill.</summary>
public static class MobDefeatPolicy
{
    public static bool ShouldRecordDefeat(bool wasEngaged, bool observedDeath) => wasEngaged && observedDeath;

    public static int RecordDefeat(int killCount, bool wasEngaged, bool observedDeath) =>
        ShouldRecordDefeat(wasEngaged, observedDeath) ? killCount + 1 : killCount;
}
