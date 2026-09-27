using Dalamud.Configuration;

using Newtonsoft.Json;

namespace MobGrinder;

public enum MobStopConditionKind
{
    MobCount,
    ItemCount,
}

public enum MobRunMode
{
    StopAfterOneCycle,
    Loop,
}

public sealed class MobStopCondition
{
    public MobStopConditionKind Kind { get; set; } = MobStopConditionKind.MobCount;
    public int MobCount { get; set; } = 1;
    public uint ItemId { get; set; }
    public int ItemCount { get; set; } = 1;
}

public sealed class MobTargetPreset
{
    public uint BNpcNameId { get; set; }
    public uint TerritoryTypeId { get; set; }
    public List<MobStopCondition> StopConditions { get; set; } = [];
}

public sealed class MobGrinderPreset
{
    public string Name { get; set; } = "默认预设";
    public List<MobTargetPreset> Targets { get; set; } = [];
}

public sealed class MobGrinderConfiguration : IPluginConfiguration
{
    public const int CurrentVersion = 5;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// Runtime state only. It must never cause the plugin to resume after a reload.
    /// </summary>
    [JsonIgnore]
    public bool Enabled { get; set; }

    public List<MobGrinderPreset> PresetLists { get; set; } = [];

    public int ActivePresetListIndex { get; set; }

    /// <summary>Behavior after every target in the active preset has completed.</summary>
    public MobRunMode RunMode { get; set; } = MobRunMode.Loop;

    /// <summary>Maximum number of mob snapshots shown in the UI.</summary>
    public int MaxTrackedMobs { get; set; } = 12;

    /// <summary>Optional case-insensitive substring filter for mob names.</summary>
    public string NameFilter { get; set; } = string.Empty;

    /// <summary>Desired height above the resolved ground while travelling, in yalms.</summary>
    public float FlightHeight { get; set; } = 18f;

    /// <summary>Horizontal arrival tolerance for a spawn point, in yalms.</summary>
    public float SpawnPointArrivalRadius { get; set; } = 6f;

    /// <summary>How long to observe each spawn point before advancing.</summary>
    public float SpawnPointWaitSeconds { get; set; } = 3f;

    /// <summary>Horizontal distance at which the player is close enough to land for combat.</summary>
    public float CombatApproachRadius { get; set; } = 4f;

    /// <summary>Whether to play a game sound after one complete preset cycle.</summary>
    public bool EnableSoundAlerts { get; set; } = true;

    /// <summary>Game built-in sound effect id, 0 disables the sound.</summary>
    public uint SoundAlertCycleCompletedEffectId { get; set; } = 1;

    /// <summary>Whether the compact always-available run overlay is shown.</summary>
    public bool ShowOverlayWindow { get; set; }

    /// <summary>
    /// Applies schema changes before value clamping. Keep this separate from Normalize so a
    /// configuration loaded from an older plugin version has an explicit migration path.
    /// </summary>
    public void Migrate()
    {
        int sourceVersion = Math.Max(0, this.Version);
        this.PresetLists ??= [];

        if (sourceVersion < 1)
        {
            this.RunMode = MobRunMode.Loop;
            this.NameFilter ??= string.Empty;
        }

        if (sourceVersion < 2)
        {
            if (this.MaxTrackedMobs == 0)
                this.MaxTrackedMobs = 12;
            if (this.FlightHeight == 0)
                this.FlightHeight = 18f;
            if (this.SpawnPointArrivalRadius == 0)
                this.SpawnPointArrivalRadius = 6f;
            if (this.SpawnPointWaitSeconds == 0)
                this.SpawnPointWaitSeconds = 3f;
            if (this.CombatApproachRadius == 0)
                this.CombatApproachRadius = 4f;
        }

        if (sourceVersion < 3)
        {
            foreach (MobGrinderPreset preset in this.PresetLists)
            {
                preset.Targets ??= [];
                foreach (MobTargetPreset target in preset.Targets)
                    target.StopConditions ??= [];
            }
        }

        if (sourceVersion < 4 && this.SoundAlertCycleCompletedEffectId == 0)
            this.SoundAlertCycleCompletedEffectId = 1;

        if (sourceVersion < 5)
        {
            // An item stop condition without an item id can never be satisfied. Older UI builds
            // could save one while the item selector was empty; discard only that invalid entry.
            foreach (MobGrinderPreset preset in this.PresetLists)
            {
                foreach (MobTargetPreset target in preset.Targets ?? [])
                    target.StopConditions?.RemoveAll(condition =>
                        condition.Kind == MobStopConditionKind.ItemCount && condition.ItemId == 0);
            }
        }

        this.Version = CurrentVersion;
    }

    public void Normalize()
    {
        this.MaxTrackedMobs = Math.Clamp(this.MaxTrackedMobs, 1, 50);
        if (!Enum.IsDefined(this.RunMode))
            this.RunMode = MobRunMode.Loop;
        this.NameFilter ??= string.Empty;
        this.FlightHeight = Math.Clamp(this.FlightHeight, 8f, 40f);
        this.SpawnPointArrivalRadius = Math.Clamp(this.SpawnPointArrivalRadius, 2f, 20f);
        this.SpawnPointWaitSeconds = Math.Clamp(this.SpawnPointWaitSeconds, 1f, 30f);
        this.CombatApproachRadius = Math.Clamp(this.CombatApproachRadius, 2f, 10f);
        this.SoundAlertCycleCompletedEffectId = Math.Clamp(this.SoundAlertCycleCompletedEffectId, 0u, 16u);

        this.PresetLists ??= [];
        if (this.PresetLists.Count == 0)
            this.PresetLists.Add(new MobGrinderPreset());

        this.ActivePresetListIndex = Math.Clamp(this.ActivePresetListIndex, 0, this.PresetLists.Count - 1);
        foreach (MobGrinderPreset preset in this.PresetLists)
        {
            preset.Name ??= string.Empty;
            preset.Targets ??= [];
            foreach (MobTargetPreset target in preset.Targets)
            {
                target.StopConditions ??= [];
                target.StopConditions.RemoveAll(condition => condition is null);
                foreach (MobStopCondition condition in target.StopConditions)
                {
                    if (!Enum.IsDefined(condition.Kind))
                        condition.Kind = MobStopConditionKind.MobCount;
                    condition.MobCount = Math.Clamp(condition.MobCount, 1, 1_000_000);
                    condition.ItemCount = Math.Clamp(condition.ItemCount, 1, 1_000_000);
                }
            }
        }

        this.Version = CurrentVersion;
    }

    public MobGrinderPreset GetActivePresetList()
    {
        this.Normalize();
        return this.PresetLists[this.ActivePresetListIndex];
    }
}
