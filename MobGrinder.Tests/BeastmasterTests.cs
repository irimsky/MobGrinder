using System.Text.Json;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;
using Newtonsoft.Json;

namespace MobGrinder.Tests;

public sealed class BeastmasterTests
{
    [Theory]
    [InlineData(9, 9, true)]
    [InlineData(10, 10, false)]
    [InlineData(19, 10, false)]
    [InlineData(20, 10, true)]
    [InlineData(8, 9, false)]
    [InlineData(50, 51, false)]
    public void OpeningCaptureHonorsLevelBoundaries(int player, int mob, bool expected) =>
        Assert.Equal(expected, BeastmasterCombatPolicy.NeedsOpeningCapture(player, mob));

    [Fact]
    public void CaptureMarksThenAllowsAttackAndCapturesAgainAtLowerThreshold()
    {
        BeastmasterCombatPolicy policy = new();
        Assert.True(policy.ShouldCapture(30, 20, 1000, 1000, 30));
        policy.CaptureIssued(1000, 1000, 30);
        Assert.False(policy.ShouldCapture(30, 20, 1000, 1000, 30));
        Assert.False(policy.ShouldCapture(30, 20, 301, 1000, 30));
        Assert.True(policy.ShouldCapture(30, 20, 300, 1000, 30));
        policy.CaptureIssued(300, 1000, 30);
        Assert.False(policy.ShouldCapture(30, 20, 1, 1000, 30));
        policy.Reset();
        Assert.True(policy.ShouldCapture(30, 20, 1000, 1000, 30));
    }

    [Fact]
    public void FailedRequestsRemainPendingAndDeadOrHigherLevelMobsCannotBeCaptured()
    {
        BeastmasterCombatPolicy policy = new();
        Assert.True(policy.ShouldCapture(20, 19, 30, 100, 30));
        Assert.True(policy.ShouldCapture(20, 19, 30, 100, 30));
        Assert.False(policy.ShouldCapture(20, 21, 30, 100, 30));
        Assert.False(policy.ShouldCapture(20, 19, 0, 100, 30));
        Assert.False(policy.ShouldCapture(20, 19, 30, 0, 30));
    }

    [Fact]
    public void ThresholdEndpointsAndOpeningCaptureDoNotDoubleCast()
    {
        BeastmasterCombatPolicy policy = new();
        Assert.False(policy.ShouldCapture(20, 19, 1, 100, 0));
        Assert.True(policy.ShouldCapture(20, 19, 100, 100, 100));
        policy.CaptureIssued(100, 100, 100);
        Assert.False(policy.ShouldCapture(20, 19, 1, 100, 100));
        policy.Reset();
        Assert.True(policy.ShouldCapture(20, 9, 100, 100, 0));
        policy.CaptureIssued(100, 100, 0);
        Assert.False(policy.ShouldCapture(20, 9, 1, 100, 0));
    }

    [Fact]
    public void VersionFiveMigratesCaptureDefaultsWithoutChangingRegularPlan()
    {
        MobGrinderConfiguration configuration = new()
        {
            Version = 5, BeastmasterCaptureHpPercent = 99,
            BeastmasterSelectedPets = [2], RunMode = MobRunMode.Loop,
            PresetLists = [new() { Name = "原预设", Targets = [new() { BNpcNameId = 37, TerritoryTypeId = 148 }] }],
        };
        configuration.Migrate();
        configuration.Normalize();
        Assert.Equal(MobGrinderConfiguration.CurrentVersion, configuration.Version);
        Assert.Empty(configuration.BeastmasterSelectedPets);
        Assert.Equal(30, configuration.BeastmasterCaptureHpPercent);
        Assert.Equal("原预设", configuration.GetActivePresetList().Name);
        Assert.Equal(MobRunMode.Loop, configuration.RunMode);
    }

    [Fact]
    public void VersionSixPreservesCaptureSettingsAndDropsObsoleteModeSwitch()
    {
        var configuration = JsonConvert.DeserializeObject<MobGrinderConfiguration>(
            "{\"Version\":6,\"BeastmasterEnabled\":true,\"BeastmasterSelectedPets\":[2,46],\"BeastmasterCaptureHpPercent\":19}")!;
        configuration.Migrate();
        configuration.Normalize();
        Assert.Equal(MobGrinderConfiguration.CurrentVersion, configuration.Version);
        Assert.Equal(new uint[] { 2, 46 }, configuration.BeastmasterSelectedPets);
        Assert.Equal(19, configuration.BeastmasterCaptureHpPercent);
        Assert.DoesNotContain("BeastmasterEnabled", JsonConvert.SerializeObject(configuration));
    }

    [Fact]
    public void TestCaptureIgnoresPreUnlockedEntriesUntilEachOneHasBeenDefeated()
    {
        Assert.False(BeastmasterProgressPolicy.ShouldCheckUnlocks(testMode: true, confirmedDefeats: 0));
        Assert.False(BeastmasterProgressPolicy.IsComplete(true, 0, dataReady: true, unlocked: true));
        Assert.True(BeastmasterProgressPolicy.ShouldCheckUnlocks(testMode: true, confirmedDefeats: 1));
        Assert.False(BeastmasterProgressPolicy.IsComplete(true, 1, dataReady: false, unlocked: true));
        Assert.False(BeastmasterProgressPolicy.IsComplete(true, 1, dataReady: true, unlocked: false));
        Assert.True(BeastmasterProgressPolicy.IsComplete(true, 1, dataReady: true, unlocked: true));
        // A later selected entry still needs its own first fight, even after another entry was tested.
        Assert.False(BeastmasterProgressPolicy.IsComplete(true, 0, dataReady: true, unlocked: true));
    }

    [Fact]
    public void RegularCaptureCanSkipUnlockedEntriesAndStillWaitsForLoadedData()
    {
        Assert.True(BeastmasterProgressPolicy.ShouldCheckUnlocks(testMode: false, confirmedDefeats: 0));
        Assert.True(BeastmasterProgressPolicy.IsComplete(false, 0, dataReady: true, unlocked: true));
        Assert.False(BeastmasterProgressPolicy.IsComplete(false, 0, dataReady: false, unlocked: true));
        Assert.False(BeastmasterProgressPolicy.IsComplete(false, 0, dataReady: true, unlocked: false));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void DisappearanceOrUnengagedDeathDoesNotCountAsTestFight(bool engaged, bool death, bool expected) =>
        Assert.Equal(expected, MobDefeatPolicy.ShouldRecordDefeat(engaged, death));

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(101, 100)]
    [InlineData(float.NaN, 30)]
    public void ConfigurationNormalizesSelectionsAndThreshold(float threshold, float expected)
    {
        MobGrinderConfiguration configuration = new()
        {
            BeastmasterCaptureHpPercent = threshold,
            BeastmasterSelectedPets = [46, 2, 2, 1, 18, 999],
        };
        configuration.Normalize();
        Assert.Equal(expected, configuration.BeastmasterCaptureHpPercent);
        Assert.Equal(new uint[] { 2, 46 }, configuration.BeastmasterSelectedPets);
        string json = JsonConvert.SerializeObject(configuration);
        var restored = JsonConvert.DeserializeObject<MobGrinderConfiguration>(json)!;
        Assert.Equal(configuration.BeastmasterSelectedPets, restored.BeastmasterSelectedPets);
        Assert.DoesNotContain("BeastmasterPetId", JsonConvert.SerializeObject(new MobTargetPreset { BeastmasterPetId = 2 }));
    }

    [Fact]
    public void FieldCatalogMatchesWebsiteReportAndEverySpeciesHasPatrolCoordinates()
    {
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "beastmaster-sources.json")));
        var entries = report.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(40, BeastmasterCatalog.Entries.Count);
        Assert.Equal(40, entries.Length);
        Assert.Equal(40, BeastmasterCatalog.Entries.Select(entry => entry.Number).Distinct().Count());
        Assert.Equal(BeastmasterCatalog.Entries.OrderBy(entry => entry.Number), BeastmasterCatalog.Entries);
        Assert.DoesNotContain(BeastmasterCatalog.Entries, entry => new uint[] { 1, 18, 37, 38, 43, 45, 47, 48, 49, 50 }.Contains(entry.Number));
        List<MobSpawnPosition> baseline = CsvLoader.LoadResource<MobSpawnPosition>(
            CsvLoader.MobSpawnResourceName, includesHeaders: true, out var failedLines, out var exceptions);
        Assert.Empty(failedLines);
        Assert.Empty(exceptions);
        baseline.RemoveAll(MobCoordinateService.IsUnresolvedMapPosition);
        baseline.AddRange(BeastmasterCatalog.GetSpawnPositions());
        foreach (var entry in BeastmasterCatalog.Entries)
        {
            var source = entries.Single(item => item.GetProperty("number").GetUInt32() == entry.Number);
            Assert.Equal(source.GetProperty("territoryId").GetUInt32(), entry.TerritoryId);
            Assert.Equal(source.GetProperty("nameIds").EnumerateArray().Select(item => item.GetUInt32()), entry.NameIds);
            Assert.Equal(source.GetProperty("points").GetArrayLength(), entry.Spawns.Length);
            Assert.NotEmpty(entry.NameIds);
            Assert.All(entry.NameIds, id => Assert.NotEqual(0u, id));
            Assert.InRange(entry.MinLevel, 1, 50);
            Assert.InRange(entry.MaxLevel, entry.MinLevel, 50);
            Assert.Contains(baseline, point => point.BNpcNameId == entry.NameIds[0] && point.TerritoryTypeId == entry.TerritoryId);
            foreach (var point in entry.Spawns)
            {
                Assert.InRange(point.X, 1, 42);
                Assert.InRange(point.Y, 1, 42);
                Assert.InRange(point.Radius, 0, 5);
            }
        }
    }
}
