using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace MobGrinder;

/// <summary>Only called on the Framework thread; publishes copied unlock values to the UI.</summary>
public sealed class BeastmasterGameAdapter(IDataManager dataManager, IUnlockState unlockState)
{
    public bool IsAttackLearned(uint actionId, int effectiveLevel)
    {
        var actions = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        return actions.TryGetRow(actionId, out var row)
            && row.ClassJob.RowId == BeastmasterCatalog.JobId
            && effectiveLevel >= row.ClassJobLevel
            && unlockState.IsActionUnlocked(row);
    }

    public bool TryReadUnlocks(out IReadOnlySet<uint> unlocked, out string reason)
    {
        unlocked = new HashSet<uint>();
        try
        {
            if (!unlockState.IsXBMPetListLoaded)
            {
                reason = "图鉴尚未加载，请在游戏中打开一次魔兽图鉴";
                return false;
            }

            var sheet = dataManager.GetExcelSheet<XBMPet>();
            HashSet<uint> result = [];
            foreach (BeastmasterEntry entry in BeastmasterCatalog.Entries)
            {
                if (!sheet.TryGetRow(entry.Number, out XBMPet row))
                {
                    reason = $"客户端缺少第 {entry.Number:00} 号魔兽图鉴数据";
                    return false;
                }
                if (unlockState.IsXBMPetUnlocked(row))
                    result.Add(entry.Number);
            }
            unlocked = result;
            reason = "图鉴状态已更新";
            return true;
        }
        catch (Exception ex)
        {
            reason = $"无法读取魔兽图鉴：{ex.Message}";
            return false;
        }
    }
}
