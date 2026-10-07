namespace MobGrinder;

public enum AutomationRunKind { Regular, Beastmaster, BeastmasterTest }

/// <summary>A paused run retains ownership; another mode must wait until it is stopped.</summary>
public static class AutomationStartPolicy
{
    public static bool CanStart(AutomationState state, AutomationRunKind active, AutomationRunKind requested) =>
        state == AutomationState.Stopped || (state == AutomationState.Paused && active == requested);
}
