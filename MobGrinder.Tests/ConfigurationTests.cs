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
            FlightHeight = 1,
            SpawnPointArrivalRadius = 99,
            SpawnPointWaitSeconds = 99,
            CombatApproachRadius = 99,
            PresetLists = [],
        };

        configuration.Migrate();
        configuration.Normalize();

        Assert.Equal(8f, configuration.FlightHeight);
        Assert.Equal(20f, configuration.SpawnPointArrivalRadius);
        Assert.Equal(30f, configuration.SpawnPointWaitSeconds);
        Assert.Equal(10f, configuration.CombatApproachRadius);
        Assert.Single(configuration.PresetLists);
    }

    [Fact]
    public void Configuration_RoundTripsWithoutPersistingRuntimeEnabledState()
    {
        MobGrinderConfiguration configuration = new() { Enabled = true, ShowOverlayWindow = true };
        string json = JsonConvert.SerializeObject(configuration);
        MobGrinderConfiguration restored = JsonConvert.DeserializeObject<MobGrinderConfiguration>(json)!;

        Assert.False(restored.Enabled);
        Assert.True(restored.ShowOverlayWindow);
        Assert.Equal(MobGrinderConfiguration.CurrentVersion, restored.Version);
    }

    [Fact]
    public void VersionSevenDropsDisplayFiltersAndUpdatesDefaultHeightWithoutChangingRunSettings()
    {
        var configuration = JsonConvert.DeserializeObject<MobGrinderConfiguration>(
            "{\"Version\":7,\"MaxTrackedMobs\":1,\"NameFilter\":\"松鼠\",\"FlightHeight\":18,\"RunMode\":0,\"ShowOverlayWindow\":true,\"BeastmasterSelectedPets\":[2],\"BeastmasterCaptureHpPercent\":25}")!;
        configuration.Migrate();
        configuration.Normalize();
        Assert.Equal(MobGrinderConfiguration.CurrentVersion, configuration.Version);
        Assert.Equal(15f, configuration.FlightHeight);
        Assert.Equal(MobRunMode.StopAfterOneCycle, configuration.RunMode);
        Assert.True(configuration.ShowOverlayWindow);
        Assert.Equal(new uint[] { 2 }, configuration.BeastmasterSelectedPets);
        Assert.Equal(25f, configuration.BeastmasterCaptureHpPercent);
        string saved = JsonConvert.SerializeObject(configuration);
        Assert.DoesNotContain("MaxTrackedMobs", saved);
        Assert.DoesNotContain("NameFilter", saved);
    }

    [Theory]
    [InlineData(7, 23f)]
    [InlineData(8, 18f)]
    public void MigrationPreservesCustomizedHeight(int version, float height)
    {
        MobGrinderConfiguration configuration = new() { Version = version, FlightHeight = height };
        configuration.Migrate();
        configuration.Normalize();
        Assert.Equal(height, configuration.FlightHeight);
    }

    [Fact]
    public void NewAndLegacyMissingHeightUseFifteenYalms()
    {
        Assert.Equal(15f, new MobGrinderConfiguration().FlightHeight);
        MobGrinderConfiguration configuration = new() { Version = 1, FlightHeight = 0 };
        configuration.Migrate();
        configuration.Normalize();
        Assert.Equal(15f, configuration.FlightHeight);
    }

    [Fact]
    public void TargetProgressPolicyRequiresEveryStopCondition()
    {
        MobTargetPreset target = new()
        {
            StopConditions =
            [
                new MobStopCondition { Kind = MobStopConditionKind.MobCount, MobCount = 3 },
                new MobStopCondition { Kind = MobStopConditionKind.ItemCount, ItemId = 42, ItemCount = 2 },
            ],
        };

        Assert.False(MobTargetProgressPolicy.AreStopConditionsMet(target, 3, _ => 1));
        Assert.True(MobTargetProgressPolicy.AreStopConditionsMet(target, 3, _ => 2));
    }

    [Fact]
    public void TargetProgressPolicySkipsCompletedItemsAndWraps()
    {
        Assert.Equal(2, MobTargetProgressPolicy.FindNextIncompleteIndex([true, true, false, false], 0));
        Assert.Equal(0, MobTargetProgressPolicy.FindNextIncompleteIndex([false, true, true], 2));
        Assert.Equal(-1, MobTargetProgressPolicy.FindNextIncompleteIndex([true, true], 1));
    }
}
