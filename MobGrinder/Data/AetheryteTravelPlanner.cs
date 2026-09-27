using System.Numerics;

using Dalamud.Game.ClientState.Aetherytes;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace MobGrinder;

public sealed class AetheryteTravelPlanner
{
    private readonly IDataManager dataManager;
    private readonly IAetheryteList aetheryteList;
    private readonly Lazy<IReadOnlyDictionary<uint, AetherytePosition>> positions;

    public AetheryteTravelPlanner(IDataManager dataManager, IAetheryteList aetheryteList)
    {
        this.dataManager = dataManager;
        this.aetheryteList = aetheryteList;
        this.positions = new Lazy<IReadOnlyDictionary<uint, AetherytePosition>>(this.BuildAetherytePositions);
    }

    public bool TryFindBest(uint territoryId, Vector3 targetPosition, out AetheryteTravelPlan plan)
    {
        plan = default!;
        IEnumerable<AetheryteTravelPlan> candidates = this.aetheryteList
            .Where(entry => entry.TerritoryId == territoryId && !entry.IsSharedHouse && !entry.IsApartment)
            .GroupBy(entry => entry.AetheryteId)
            .Select(group => group.OrderBy(entry => entry.SubIndex != 0).ThenBy(entry => entry.GilCost).First())
            .Select(entry => this.CreatePlan(entry, territoryId, targetPosition))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!);

        plan = candidates.OrderBy(candidate => candidate.DistanceToTarget).ThenBy(candidate => candidate.AetheryteId).FirstOrDefault()!;
        return plan is not null;
    }

    private AetheryteTravelPlan? CreatePlan(IAetheryteEntry entry, uint territoryId, Vector3 targetPosition)
    {
        if (!this.positions.Value.TryGetValue(entry.AetheryteId, out AetherytePosition? position)
            || position.TerritoryId != territoryId)
            return null;

        return new AetheryteTravelPlan(
            entry.AetheryteId,
            entry.SubIndex,
            territoryId,
            new Vector3(position.Position.X, 0f, position.Position.Y),
            Vector2.Distance(position.Position, new Vector2(targetPosition.X, targetPosition.Z)));
    }

    private IReadOnlyDictionary<uint, AetherytePosition> BuildAetherytePositions()
    {
        try
        {
            ExcelSheet<Aetheryte> aetherytes = this.dataManager.GetExcelSheet<Aetheryte>();
            ExcelSheet<Map> maps = this.dataManager.GetExcelSheet<Map>();
            SubrowExcelSheet<MapMarker> markers = this.dataManager.GetSubrowExcelSheet<MapMarker>();
            Dictionary<uint, uint> aethernetToAetheryte = aetherytes
                .Where(row => row.AethernetName.RowId != 0)
                .ToDictionary(row => row.AethernetName.RowId, row => row.RowId);
            Dictionary<uint, AetherytePosition> result = [];

            foreach (Map map in maps)
            {
                if (map.MapMarkerRange == 0 || map.SizeFactor == 0 || !markers.TryGetRow(map.MapMarkerRange, out SubrowCollection<MapMarker> group))
                    continue;
                foreach (MapMarker marker in group)
                {
                    uint? id = marker.DataType switch
                    {
                        3 => marker.DataKey.RowId,
                        4 when aethernetToAetheryte.TryGetValue(marker.DataKey.RowId, out uint mapped) => mapped,
                        _ => null,
                    };
                    if (id is not { } aetheryteId || aetheryteId == 0)
                        continue;
                    Aetheryte? aetheryte = aetherytes.GetRowOrDefault(aetheryteId);
                    if (aetheryte is not { Territory.RowId: > 0 })
                        continue;

                    float scale = map.SizeFactor / 100f;
                    Vector2 position = new(
                        (marker.X - 1024f) / scale - map.OffsetX,
                        (marker.Y - 1024f) / scale - map.OffsetY);
                    uint territory = aetheryte.Value.Territory.RowId;
                    bool preferred = map.TerritoryType.RowId == territory;
                    if (preferred || !result.ContainsKey(aetheryteId))
                        result[aetheryteId] = new AetherytePosition(territory, position);
                }
            }
            return result;
        }
        catch
        {
            return new Dictionary<uint, AetherytePosition>();
        }
    }

    private sealed record AetherytePosition(uint TerritoryId, Vector2 Position);
}

public sealed record AetheryteTravelPlan(uint AetheryteId, byte SubIndex, uint TerritoryId, Vector3 Position, float DistanceToTarget);
