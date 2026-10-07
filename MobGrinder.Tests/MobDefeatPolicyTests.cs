namespace MobGrinder.Tests;

public sealed class MobDefeatPolicyTests
{
    [Fact]
    public void IpcRunWithFiveKillsAndTwoLostTargetsDoesNotCompleteSevenKillObjective()
    {
        MobTargetPreset target = new()
        {
            BNpcNameId = 1840,
            TerritoryTypeId = 146,
            StopConditions = [new MobStopCondition { Kind = MobStopConditionKind.MobCount, MobCount = 7 }],
        };
        int confirmedKills = 0;
        // The caller already had one quest kill and requested seven more.
        // Replay five real fights, then both losses of 0x4007E59A during navigation.
        for (int fight = 0; fight < 5; fight++)
            RecordFight(wasEngaged: true, observedDeath: true);
        RecordFight(wasEngaged: false, observedDeath: false);
        RecordFight(wasEngaged: false, observedDeath: false);

        Assert.Equal(5, confirmedKills);
        Assert.False(MobTargetProgressPolicy.AreStopConditionsMet(target, confirmedKills, _ => 0));

        RecordFight(wasEngaged: true, observedDeath: true);
        Assert.False(MobTargetProgressPolicy.AreStopConditionsMet(target, confirmedKills, _ => 0));
        RecordFight(wasEngaged: true, observedDeath: true);
        Assert.Equal(7, confirmedKills);
        Assert.True(MobTargetProgressPolicy.AreStopConditionsMet(target, confirmedKills, _ => 0));

        void RecordFight(bool wasEngaged, bool observedDeath)
        {
            confirmedKills = MobDefeatPolicy.RecordDefeat(confirmedKills, wasEngaged, observedDeath);
        }
    }

    [Fact]
    public void ReachingRequestedNumberOfCleanupEventsDoesNotCompleteEightKillObjective()
    {
        MobTargetPreset target = new()
        {
            StopConditions = [new MobStopCondition { Kind = MobStopConditionKind.MobCount, MobCount = 8 }],
        };
        (bool Engaged, bool Dead)[] outcomes =
        [
            (true, true), (true, true), (true, true), (true, true), (true, true), (true, true),
            (true, false), // An engaged target disappears; its death was not observed.
            (false, true), // A target dies before this run engages it.
        ];
        int confirmedKills = outcomes.Aggregate(0, (count, outcome) =>
            MobDefeatPolicy.RecordDefeat(count, outcome.Engaged, outcome.Dead));

        Assert.Equal(6, confirmedKills);
        Assert.False(MobTargetProgressPolicy.AreStopConditionsMet(target, confirmedKills, _ => 0));
    }
}
