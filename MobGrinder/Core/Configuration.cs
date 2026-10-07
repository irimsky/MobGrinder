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
    [JsonIgnore]
    public uint BeastmasterPetId { get; set; }
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
    public const int CurrentVersion = 8;

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

    /// <summary>Desired height above the resolved ground while travelling, in yalms.</summary>
    public float FlightHeight { get; set; } = 15f;

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

    public List<uint> BeastmasterSelectedPets { get; set; } = [];
    public float BeastmasterCaptureHpPercent { get; set; } = 30f;

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
        }

        if (sourceVersion < 2)
        {
            if (this.FlightHeight == 0)
                this.FlightHeight = 15f;
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

        if (sourceVersion < 6)
        {
            this.BeastmasterSelectedPets = [];
            this.BeastmasterCaptureHpPercent = 30f;
        }

        // Version 7 removes the old BeastmasterEnabled switch. Newtonsoft ignores that
        // obsolete JSON member; retain version 6 selections and the capture threshold.
        // Version 8 removes MaxTrackedMobs/NameFilter and changes the old default height.
        // Obsolete JSON members are ignored; preserve heights differing from the old default.
        if (sourceVersion < 8 && this.FlightHeight == 18f)
            this.FlightHeight = 15f;
        this.Version = CurrentVersion;
    }

    public void Normalize()
    {
        this.BeastmasterCaptureHpPercent = float.IsFinite(this.BeastmasterCaptureHpPercent)
            ? Math.Clamp(this.BeastmasterCaptureHpPercent, 0f, 100f) : 30f;
        this.BeastmasterSelectedPets = (this.BeastmasterSelectedPets ?? [])
            .Where(id => BeastmasterCatalog.Entries.Any(entry => entry.Number == id)).Distinct().Order().ToList();
        if (!Enum.IsDefined(this.RunMode))
            this.RunMode = MobRunMode.Loop;
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
