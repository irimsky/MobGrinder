namespace MobGrinder;

/// <summary>Pure rules used by the controller to decide which configured target is complete.</summary>
public static class MobTargetProgressPolicy
{
    public static bool AreStopConditionsMet(
        MobTargetPreset target,
        int killCount,
        Func<uint, int> inventoryCount)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(inventoryCount);
        if (target.StopConditions.Count == 0)
            return false;

        return target.StopConditions.All(condition => condition.Kind switch
        {
            MobStopConditionKind.MobCount => killCount >= Math.Max(1, condition.MobCount),
            MobStopConditionKind.ItemCount => condition.ItemId != 0
                && inventoryCount(condition.ItemId) >= Math.Max(1, condition.ItemCount),
            _ => false,
        });
    }

    public static int FindNextIncompleteIndex(IReadOnlyList<bool> completed, int startIndex)
    {
        ArgumentNullException.ThrowIfNull(completed);
        if (completed.Count == 0)
            return -1;

        int normalizedStart = ((startIndex % completed.Count) + completed.Count) % completed.Count;
        for (int offset = 0; offset < completed.Count; offset++)
        {
            int index = (normalizedStart + offset) % completed.Count;
            if (!completed[index])
                return index;
        }

        return -1;
    }
}
