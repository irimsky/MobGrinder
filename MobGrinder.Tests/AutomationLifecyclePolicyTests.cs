namespace MobGrinder.Tests;

public sealed class AutomationLifecyclePolicyTests
{
    [Fact]
    public void LogoutStopsNavigationAndAbortsTravelWithoutFailingAutomation()
    {
        AutomationInterruption result = AutomationLifecyclePolicy.Evaluate(
            AutomationState.FlyingToTarget,
            isLoggedIn: false,
            playerLoaded: false,
            betweenAreas: false,
            lifestreamAvailable: true,
            vnavmeshAvailable: true);

        Assert.True(result.ShouldStopNavigation);
        Assert.False(result.ShouldFailAutomation);
        Assert.True(result.ShouldAbortTravel);
    }

    [Fact]
    public void MissingVnavmeshFailsActiveNavigation()
    {
        AutomationInterruption result = AutomationLifecyclePolicy.Evaluate(
            AutomationState.NavigatingToSpawnPoint,
            isLoggedIn: true,
            playerLoaded: true,
            betweenAreas: false,
            lifestreamAvailable: true,
            vnavmeshAvailable: false);

        Assert.True(result.ShouldStopNavigation);
        Assert.True(result.ShouldFailAutomation);
        Assert.False(result.ShouldAbortTravel);
    }

    [Fact]
    public void WaitingForVnavmeshDoesNotFailWhileDependencyLoads()
    {
        AutomationInterruption result = AutomationLifecyclePolicy.Evaluate(
            AutomationState.WaitingForVnavmesh,
            isLoggedIn: true,
            playerLoaded: true,
            betweenAreas: false,
            lifestreamAvailable: true,
            vnavmeshAvailable: false);

        Assert.False(result.ShouldStopNavigation);
    }
}
