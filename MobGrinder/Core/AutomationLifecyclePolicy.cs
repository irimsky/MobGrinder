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
            return new(true, false, true, "等待角色登录并加载完成，已停止移动");
        if (betweenAreas)
            return new(true, false, true, "正在切换地图，已停止移动并等待加载完成");
        if ((state is AutomationState.Teleporting or AutomationState.WaitingForTerritory)
            && !lifestreamAvailable)
            return new(true, true, true, "传送期间 Lifestream 不可用，已停止运行");
        if (RequiresVnavmesh(state) && !vnavmeshAvailable)
            return new(true, true, false, "移动期间 vnavmesh 不可用，已停止运行");
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
