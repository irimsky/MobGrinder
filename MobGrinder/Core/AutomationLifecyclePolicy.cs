namespace MobGrinder;

public readonly record struct AutomationInterruption(
    bool ShouldStopNavigation,
    bool ShouldFailAutomation,
    bool ShouldAbortTravel,
    string Reason);

/// <summary>
/// Pure lifecycle decisions kept separate from game-service reads so interruption behavior can be
/// regression-tested without a running Dalamud client.
/// </summary>
public static class AutomationLifecyclePolicy
{
    public static AutomationInterruption Evaluate(
        AutomationState state,
        bool isLoggedIn,
        bool playerLoaded,
        bool betweenAreas,
        bool lifestreamAvailable,
        bool vnavmeshAvailable)
    {
        if (!isLoggedIn || !playerLoaded)
            return new(true, false, true, "等待角色登录和对象表就绪；导航已停止");
        if (betweenAreas)
            return new(true, false, true, "区域切换中；导航已停止，等待新区域就绪");
        if ((state is AutomationState.Teleporting or AutomationState.WaitingForTerritory)
            && !lifestreamAvailable)
            return new(true, true, true, "Lifestream 在传送过程中失效，已停止自动流程");
        if (RequiresVnavmesh(state) && !vnavmeshAvailable)
            return new(true, true, false, "vnavmesh 在导航过程中失效，已停止自动流程");
        return default;
    }

    private static bool RequiresVnavmesh(AutomationState state) => state is
        AutomationState.PreparingFlight
        or AutomationState.PreparingTargetApproach
        or AutomationState.NavigatingToSpawnPoint
        or AutomationState.WaitingAtSpawnPoint
        or AutomationState.FlyingToTarget
        or AutomationState.LandingForCombat
        or AutomationState.DismountingForCombat
        or AutomationState.WaitingForCombat
        or AutomationState.CleaningAggro;
}
