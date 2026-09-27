using System.Numerics;

using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;

namespace MobGrinder;

/// <summary>
/// Converts the map coordinates stored in MobSpawn.csv back to world X/Z coordinates.
/// MobSpawn uses the same MapUtil.WorldToMap formula as InventoryTools; its Y component is
/// intentionally not used as a navigation height because a rounded map height is not a safe
/// flight altitude. The vnavmesh floor query supplies the live ground height instead.
/// </summary>
public sealed class MobCoordinateService
{
    private readonly Dictionary<uint, MapInfo> mapsByTerritory;

    public MobCoordinateService(IDataManager dataManager, IPluginLog log)
    {
        this.mapsByTerritory = dataManager.GetExcelSheet<TerritoryType>()
            .Where(row => row.RowId != 0 && row.Map.ValueNullable is not null)
            .ToDictionary(
                row => row.RowId,
                row => new MapInfo(
                    row.Map.Value.OffsetX,
                    row.Map.Value.OffsetY,
                    row.Map.Value.SizeFactor));

        log.Information("MobGrinder 已建立地图坐标反算索引：{Count} 个区域", this.mapsByTerritory.Count);
    }

    public bool TryConvert(MobSpawnPosition position, out Vector3 world)
    {
        world = default;
        if (IsUnresolvedMapPosition(position))
            return false;
        if (!this.mapsByTerritory.TryGetValue(position.TerritoryTypeId, out MapInfo? map)
            || map.SizeFactor == 0)
            return false;

        float x = MapToWorld(position.Position.X, map.SizeFactor, map.OffsetX);
        float z = MapToWorld(position.Position.Y, map.SizeFactor, map.OffsetY);
        if (!float.IsFinite(x) || !float.IsFinite(z))
            return false;

        world = new Vector3(x, 0f, z);
        return true;
    }

    /// <summary>
    /// MobSpawn.csv uses 21.48;21.48;-1 as a placeholder when a mob has no
    /// discoverable field position.  Converting that marker produces world
    /// (0, 0), which is a valid-looking coordinate but can send navigation to
    /// the opposite side of the map.
    /// </summary>
    public static bool IsUnresolvedMapPosition(MobSpawnPosition position)
    {
        Vector3 mapPosition = position.Position;
        return float.IsFinite(mapPosition.X)
            && float.IsFinite(mapPosition.Y)
            && float.IsFinite(mapPosition.Z)
            && MathF.Abs(mapPosition.X - 21.48f) < 0.01f
            && MathF.Abs(mapPosition.Y - 21.48f) < 0.01f
            && MathF.Abs(mapPosition.Z + 1f) < 0.01f;
    }

    private static float MapToWorld(float value, uint sizeFactor, int offset)
    {
        // Inverse of MapUtil.ConvertWorldCoordXZToMapCoord:
        // map = .02 * offset + 2048 / scale + .02 * world + 1.
        return (value - 1f - (2048f / sizeFactor) - (0.02f * offset)) / 0.02f;
    }

    private sealed record MapInfo(int OffsetX, int OffsetY, uint SizeFactor);
}
