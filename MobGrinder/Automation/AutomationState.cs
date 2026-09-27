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
