using System.Numerics;

using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;

using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace MobGrinder;

/// <summary>
/// Loads the same generated mob spawn resource used by InventoryTools.
/// The resource is embedded in LuminaSupplemental.Excel and contains static
/// map positions, not the current positions of live objects.
/// </summary>
public sealed class MobSpawnDataService
{
    private readonly Dictionary<uint, IReadOnlyList<MobSpawnPosition>> byNameId;
    private readonly Dictionary<uint, IReadOnlyList<MobSpawnPosition>> byTerritoryId;
    private readonly Dictionary<(uint TerritoryTypeId, uint BNpcNameId), IReadOnlyList<Vector3>> worldPointsByTarget;
    private readonly IReadOnlyDictionary<uint, string> itemNamesById;

    public MobSpawnDataService(IPluginLog log, IDataManager dataManager, MobCoordinateService coordinates)
    {
        List<MobSpawnPosition> entries = CsvLoader.LoadResource<MobSpawnPosition>(
            CsvLoader.MobSpawnResourceName,
            includesHeaders: true,
            out List<string> failedLines,
            out List<Exception> exceptions);

        entries.AddRange(MobSpawnSupplementData.Entries);
        int unresolvedPositionCount = entries.Count(MobCoordinateService.IsUnresolvedMapPosition);
        if (unresolvedPositionCount != 0)
        {
            entries = entries
                .Where(entry => !MobCoordinateService.IsUnresolvedMapPosition(entry))
                .ToList();
        }

        this.Entries = entries;
        this.byNameId = entries
            .GroupBy(entry => entry.BNpcNameId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<MobSpawnPosition>)group.ToArray());
        this.byTerritoryId = entries
            .GroupBy(entry => entry.TerritoryTypeId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<MobSpawnPosition>)group.ToArray());
        this.worldPointsByTarget = entries
            .GroupBy(entry => (entry.TerritoryTypeId, entry.BNpcNameId))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Vector3>)group
                    .Select(entry => coordinates.TryConvert(entry, out Vector3 point) ? point : (Vector3?)null)
                    .Where(point => point is not null)
                    .Select(point => point!.Value)
                    .DistinctBy(point => new { X = MathF.Round(point.X, 1), Z = MathF.Round(point.Z, 1) })
                    .ToArray());

        Dictionary<uint, string> mobNames = dataManager.GetExcelSheet<BNpcName>()
            .Where(row => row.RowId != 0 && !string.IsNullOrWhiteSpace(row.Singular.ToString()))
            .ToDictionary(row => row.RowId, row => row.Singular.ToString());
        Dictionary<uint, string> territoryNames = dataManager.GetExcelSheet<TerritoryType>()
            .Where(row => row.RowId != 0)
            .Select(row => new
            {
                row.RowId,
                Name = row.PlaceName.ValueNullable?.Name.ToString()
                       ?? row.PlaceNameZone.ValueNullable?.Name.ToString()
                       ?? string.Empty,
            })
            .Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .ToDictionary(row => row.RowId, row => row.Name);

        this.MobTargets = entries
            .GroupBy(entry => (entry.TerritoryTypeId, entry.BNpcNameId))
            .Select(group => CreateSelectionEntry(
                group.Key.TerritoryTypeId,
                group.Key.BNpcNameId,
                group.Count(),
                mobNames,
                territoryNames))
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .OrderBy(entry => entry.MapName, StringComparer.CurrentCulture)
            .ThenBy(entry => entry.MobName, StringComparer.CurrentCulture)
            .ThenBy(entry => entry.TerritoryTypeId)
            .ThenBy(entry => entry.BNpcNameId)
            .ToArray();

        this.ItemNames = dataManager.GetExcelSheet<Item>()
            .Where(row => row.RowId != 0 && !string.IsNullOrWhiteSpace(row.Name.ToString()))
            .Select(row => new NamedItem(row.RowId, row.Name.ToString()))
            .OrderBy(item => item.Name, StringComparer.CurrentCulture)
            .ThenBy(item => item.Id)
            .ToArray();
        this.itemNamesById = this.ItemNames.ToDictionary(item => item.Id, item => item.Name);

        foreach (Exception exception in exceptions)
            log.Error(exception, "MobGrinder 加载野怪位置数据时解析失败");

        if (failedLines.Count != 0)
            log.Error("MobGrinder 加载野怪位置数据时有 {Count} 行失败", failedLines.Count);

        log.Information(
            "MobGrinder 已加载野怪位置数据：{Entries} 个可用位置（过滤 {Unresolved} 个无坐标占位记录），{WorldPoints} 个预计算世界坐标，{Names} 个 BNpcNameId，{Territories} 个 TerritoryTypeId",
            this.Entries.Count,
            unresolvedPositionCount,
            this.worldPointsByTarget.Values.Sum(points => points.Count),
            this.byNameId.Count,
            this.byTerritoryId.Count);
    }

    public IReadOnlyList<MobSpawnPosition> Entries { get; }

    public IReadOnlyList<MobSelectionEntry> MobTargets { get; }

    public IReadOnlyList<NamedItem> ItemNames { get; }

    public string GetItemName(uint itemId)
        => itemId == 0
            ? "未选择物品"
            : this.itemNamesById.GetValueOrDefault(itemId, $"未知物品（{itemId}）");

    public IReadOnlyList<MobSpawnPosition> GetByNameId(uint bNpcNameId)
        => this.byNameId.TryGetValue(bNpcNameId, out IReadOnlyList<MobSpawnPosition>? entries)
            ? entries
            : Array.Empty<MobSpawnPosition>();

    public IReadOnlyList<MobSpawnPosition> GetByTerritoryId(uint territoryTypeId)
        => this.byTerritoryId.TryGetValue(territoryTypeId, out IReadOnlyList<MobSpawnPosition>? entries)
            ? entries
            : Array.Empty<MobSpawnPosition>();

    public IReadOnlyList<MobSpawnPosition> GetByNameAndTerritory(uint bNpcNameId, uint territoryTypeId)
        => this.GetByNameId(bNpcNameId)
            .Where(entry => entry.TerritoryTypeId == territoryTypeId)
            .ToArray();

    public IReadOnlyList<Vector3> GetWorldPointsByNameAndTerritory(uint bNpcNameId, uint territoryTypeId)
        => this.worldPointsByTarget.TryGetValue((territoryTypeId, bNpcNameId), out IReadOnlyList<Vector3>? points)
            ? points
            : Array.Empty<Vector3>();

    private static MobSelectionEntry? CreateSelectionEntry(
        uint territoryTypeId,
        uint bNpcNameId,
        int positionCount,
        IReadOnlyDictionary<uint, string> mobNames,
        IReadOnlyDictionary<uint, string> territoryNames)
    {
        if (!mobNames.TryGetValue(bNpcNameId, out string? mobName)
            || !territoryNames.TryGetValue(territoryTypeId, out string? mapName))
            return null;

        return string.IsNullOrWhiteSpace(mobName) || string.IsNullOrWhiteSpace(mapName)
            ? null
            : new MobSelectionEntry(bNpcNameId, territoryTypeId, mapName, mobName, positionCount);
    }
}

public sealed record MobSelectionEntry(
    uint BNpcNameId,
    uint TerritoryTypeId,
    string MapName,
    string MobName,
    int PositionCount)
{
    public string DisplayName => $"{this.MapName} | {this.MobName}";
}

public sealed record NamedItem(uint Id, string Name);
