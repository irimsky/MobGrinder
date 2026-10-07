using System.Numerics;
using System.Reflection;
using System.Text.Json;

using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;

namespace MobGrinder.Tests;

public sealed class MobSpawnSupplementDataTests
{
    [Fact]
    public void AllManualPointsHaveValidMapCoordinatesAndDistinctTargetLocations()
    {
        IReadOnlyList<MobSpawnPosition> entries = GetSupplements();
        Assert.NotEmpty(entries);
        foreach (MobSpawnPosition entry in entries)
        {
            Assert.True(entry.BNpcBaseId > 0 && entry.BNpcNameId > 0 && entry.TerritoryTypeId > 0);
            Assert.True(float.IsFinite(entry.Position.X) && float.IsFinite(entry.Position.Y));
            Assert.InRange(entry.Position.X, 1f, 42f);
            Assert.InRange(entry.Position.Y, 1f, 42f);
            Assert.Equal(0f, entry.Position.Z);
            Assert.False(MobCoordinateService.IsUnresolvedMapPosition(entry));
        }

        Assert.Equal(entries.Count, entries.DistinctBy(entry =>
            (entry.BNpcNameId, entry.TerritoryTypeId, entry.Position.X, entry.Position.Y)).Count());
    }

    [Fact]
    public void BookPointsMatchReviewedWikiAndActualEmbeddedBaseline()
    {
        List<MobSpawnPosition> baseline = CsvLoader.LoadResource<MobSpawnPosition>(
            CsvLoader.MobSpawnResourceName, includesHeaders: true,
            out List<string> failedLines, out List<Exception> exceptions);
        Assert.Empty(failedLines);
        Assert.Empty(exceptions);
        baseline.RemoveAll(MobCoordinateService.IsUnresolvedMapPosition);

        using JsonDocument report = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "zodiac-book-spawns.json")));
        JsonElement[] groups = report.RootElement.GetProperty("groups").EnumerateArray().ToArray();
        Assert.Equal(9, report.RootElement.GetProperty("sources").EnumerateObject().Count());
        Assert.Equal(92, groups.Length);
        Assert.Equal(90, groups.Select(group => group.GetProperty("nameId").GetUInt32()).Distinct().Count());
        Assert.Equal(groups.Length, groups.Select(group =>
            (group.GetProperty("nameId").GetUInt32(), group.GetProperty("territoryId").GetUInt32())).Distinct().Count());

        IReadOnlyList<MobSpawnPosition> supplements = GetSupplements();
        HashSet<(uint NameId, uint TerritoryId)> reviewedTargets = [];
        int additionCount = 0;
        int supplementedTargets = 0;
        foreach (JsonElement group in groups)
        {
            uint nameId = group.GetProperty("nameId").GetUInt32();
            uint territoryId = group.GetProperty("territoryId").GetUInt32();
            reviewedTargets.Add((nameId, territoryId));
            MobSpawnPosition[] originalRows = baseline.Where(entry =>
                entry.BNpcNameId == nameId && entry.TerritoryTypeId == territoryId).ToArray();
            Vector2[] original = originalRows.Select(MapPoint).Distinct().ToArray();
            Vector2[] recordedOriginal = ReadPoints(group.GetProperty("baseline"));
            Assert.True(original.ToHashSet().SetEquals(recordedOriginal), $"Baseline changed: {nameId}/{territoryId}");

            Vector2[] wiki = ReadPoints(group.GetProperty("wiki"));
            Assert.Equal(wiki.Length, wiki.Distinct().Count());
            // Compare recorded locations to the actual resource, never the report's precomputed Δ.
            Vector2[] missing = wiki.Length > original.Length
                ? wiki.Where(point => original.All(existing => MapDistance(point, existing) > 0.500001)).ToArray()
                : [];
            MobSpawnPosition[] actual = supplements.Where(entry =>
                entry.BNpcNameId == nameId && entry.TerritoryTypeId == territoryId).ToArray();
            Assert.True(missing.Length == actual.Length, $"Wrong addition count: {nameId}/{territoryId}: {missing.Length} vs {actual.Length}");
            Assert.True(missing.ToHashSet().SetEquals(actual.Select(MapPoint)), $"Wrong additions: {nameId}/{territoryId}");
            Assert.True(missing.ToHashSet().SetEquals(ReadPoints(group.GetProperty("add"))));
            foreach (MobSpawnPosition added in actual)
            {
                MobSpawnPosition nearest = originalRows.MinBy(entry => Vector2.Distance(MapPoint(entry), MapPoint(added)))!;
                Assert.Equal(nearest.BNpcBaseId, added.BNpcBaseId);
            }

            additionCount += actual.Length;
            supplementedTargets += actual.Length > 0 ? 1 : 0;
        }

        Assert.Equal(211, additionCount);
        Assert.Equal(76, supplementedTargets);
        // The ARR additions must have a reviewed target; older Dawntrail entries are preserved.
        Assert.All(supplements.Where(entry => entry.TerritoryTypeId < 1000),
            entry => Assert.Contains((entry.BNpcNameId, entry.TerritoryTypeId), reviewedTargets));
        foreach (JsonElement excluded in report.RootElement.GetProperty("excludedFatePoints").EnumerateArray())
        {
            uint territoryId = excluded.GetProperty("territoryId").GetUInt32();
            Vector2 point = ReadPoint(excluded.GetProperty("mapPosition"));
            Assert.DoesNotContain(supplements, entry => entry.TerritoryTypeId == territoryId && MapPoint(entry) == point);
        }
    }

    private static IReadOnlyList<MobSpawnPosition> GetSupplements()
        => (IReadOnlyList<MobSpawnPosition>)typeof(MobSpawnDataService).Assembly
            .GetType("MobGrinder.MobSpawnSupplementData", throwOnError: true)!
            .GetProperty("Entries", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    private static Vector2 MapPoint(MobSpawnPosition entry) => new(entry.Position.X, entry.Position.Y);

    private static double MapDistance(Vector2 a, Vector2 b)
    {
        // Restore decimal map coordinates after the CSV loader stores them as float;
        // binary float noise must not turn an exact 0.5 boundary into a missing point.
        double x = Math.Round((double)a.X, 5) - Math.Round((double)b.X, 5);
        double y = Math.Round((double)a.Y, 5) - Math.Round((double)b.Y, 5);
        return Math.Sqrt((x * x) + (y * y));
    }

    private static Vector2 ReadPoint(JsonElement pair) => new(pair[0].GetSingle(), pair[1].GetSingle());

    private static Vector2[] ReadPoints(JsonElement points) => points.EnumerateArray().Select(ReadPoint).ToArray();
}
