namespace MobGrinder.Tests;

public sealed class AutomationStartPolicyTests
{
    [Theory]
    [InlineData(AutomationRunKind.Regular, AutomationRunKind.Beastmaster)]
    [InlineData(AutomationRunKind.Regular, AutomationRunKind.BeastmasterTest)]
    [InlineData(AutomationRunKind.Beastmaster, AutomationRunKind.Regular)]
    [InlineData(AutomationRunKind.Beastmaster, AutomationRunKind.BeastmasterTest)]
    [InlineData(AutomationRunKind.BeastmasterTest, AutomationRunKind.Regular)]
    [InlineData(AutomationRunKind.BeastmasterTest, AutomationRunKind.Beastmaster)]
    public void PausedRunKeepsOwnershipUntilStopped(AutomationRunKind active, AutomationRunKind other)
    {
        Assert.False(AutomationStartPolicy.CanStart(AutomationState.Paused, active, other));
        Assert.True(AutomationStartPolicy.CanStart(AutomationState.Paused, active, active));
        Assert.True(AutomationStartPolicy.CanStart(AutomationState.Stopped, active, other));
    }

    [Fact]
    public void FirstFrameworkStartBlocksEveryFollowingStartDuringTheRun()
    {
        foreach (AutomationRunKind first in Enum.GetValues<AutomationRunKind>())
        {
            Assert.True(AutomationStartPolicy.CanStart(AutomationState.Stopped, AutomationRunKind.Regular, first));
            foreach (AutomationState state in Enum.GetValues<AutomationState>()
                         .Where(state => state is not (AutomationState.Stopped or AutomationState.Paused)))
            foreach (AutomationRunKind next in Enum.GetValues<AutomationRunKind>())
                Assert.False(AutomationStartPolicy.CanStart(state, first, next));
        }
    }
}
