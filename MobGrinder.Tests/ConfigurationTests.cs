using Newtonsoft.Json;

namespace MobGrinder.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public void Migrate_RemovesLegacyImpossibleItemConditionAndUpdatesVersion()
    {
        MobGrinderConfiguration configuration = new()
        {
            Version = 4,
            PresetLists =
            [
                new MobGrinderPreset
                {
                    Targets =
                    [
                        new MobTargetPreset
                        {
                            StopConditions =
                            [
                                new MobStopCondition { Kind = MobStopConditionKind.ItemCount },
                                new MobStopCondition { Kind = MobStopConditionKind.MobCount },
                            ],
                        },
                    ],
                },
            ],
        };

        configuration.Migrate();
        configuration.Normalize();

        Assert.Equal(MobGrinderConfiguration.CurrentVersion, configuration.Version);
        Assert.Single(configuration.PresetLists[0].Targets[0].StopConditions);
        Assert.Equal(MobStopConditionKind.MobCount, configuration.PresetLists[0].Targets[0].StopConditions[0].Kind);
    }

    [Fact]
    public void Normalize_ClampsValuesAndCreatesDefaultPreset()
    {
        MobGrinderConfiguration configuration = new()
        {
            Version = 0,
            MaxTrackedMobs = 999,
            FlightHeight = 1,
            SpawnPointArrivalRadius = 99,
            SpawnPointWaitSeconds = 99,
            CombatApproachRadius = 99,
            PresetLists = [],
        };

        configuration.Migrate();
        configuration.Normalize();

        Assert.Equal(50, configuration.MaxTrackedMobs);
        Assert.Equal(8f, configuration.FlightHeight);
        Assert.Equal(20f, configuration.SpawnPointArrivalRadius);
        Assert.Equal(30f, configuration.SpawnPointWaitSeconds);
        Assert.Equal(10f, configuration.CombatApproachRadius);
        Assert.Single(configuration.PresetLists);
    }

    [Fact]
    public void Configuration_RoundTripsWithoutPersistingRuntimeEnabledState()
    {
        MobGrinderConfiguration configuration = new() { Enabled = true };
        string json = JsonConvert.SerializeObject(configuration);
        MobGrinderConfiguration restored = JsonConvert.DeserializeObject<MobGrinderConfiguration>(json)!;

        Assert.False(restored.Enabled);
        Assert.Equal(MobGrinderConfiguration.CurrentVersion, restored.Version);
    }
}
