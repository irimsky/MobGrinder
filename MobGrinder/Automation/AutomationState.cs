namespace MobGrinder;

public enum AutomationState
{
    Stopped,
    WaitingForPlayer,
    ValidatingPlan,
    WaitingForVnavmesh,
    Teleporting,
    WaitingForTerritory,
    PreparingFlight,
    NavigatingToSpawnPoint,
    WaitingAtSpawnPoint,
    PreparingTargetApproach,
    FlyingToTarget,
    LandingForCombat,
    DismountingForCombat,
    WaitingForCombat,
    CleaningAggro,
    AdvancingSpawnPoint,
    AdvancingTarget,
    Scanning,
    Paused,
}

public static class AutomationStateExtensions
{
    public static string ToDisplayName(this AutomationState state) => state switch
    {
        AutomationState.Stopped => "已停止",
        AutomationState.WaitingForPlayer => "等待角色就绪",
        AutomationState.ValidatingPlan => "检查目标",
        AutomationState.WaitingForVnavmesh => "等待导航就绪",
        AutomationState.Teleporting => "传送中",
        AutomationState.WaitingForTerritory => "等待地图加载",
        AutomationState.PreparingFlight => "准备出发",
        AutomationState.NavigatingToSpawnPoint => "前往刷新点",
        AutomationState.WaitingAtSpawnPoint => "等待目标出现",
        AutomationState.PreparingTargetApproach => "准备接近目标",
        AutomationState.FlyingToTarget => "接近目标",
        AutomationState.LandingForCombat => "准备落地",
        AutomationState.DismountingForCombat => "下坐骑",
        AutomationState.WaitingForCombat => "等待战斗完成",
        AutomationState.CleaningAggro => "处理其他敌人",
        AutomationState.AdvancingSpawnPoint => "切换刷新点",
        AutomationState.AdvancingTarget => "切换目标",
        AutomationState.Scanning => "搜索野怪",
        AutomationState.Paused => "已暂停",
        _ => "未知状态",
    };
}
