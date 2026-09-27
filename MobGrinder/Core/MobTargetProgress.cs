namespace MobGrinder;

/// <summary>
/// A UI-safe snapshot of one target in the active run. It contains copied values only;
/// the live preset and game objects remain owned by the controller/framework thread.
/// </summary>
public sealed record MobTargetProgress(
    int Index,
    string DisplayName,
    uint BNpcNameId,
    uint TerritoryTypeId,
    int KillCount,
    bool IsCompleted,
    string StopConditionProgress);
