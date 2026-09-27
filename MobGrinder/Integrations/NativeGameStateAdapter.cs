using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace MobGrinder;

/// <summary>
/// Framework-thread-only boundary for native ClientStructs reads used by automation.
/// </summary>
public readonly record struct NativeMobState(ushort FateId, byte CombatTagType, ulong CombatTaggerId);

public sealed unsafe class NativeGameStateAdapter
{
    public uint GetGeneralActionStatus(uint actionId)
    {
        ActionManager* manager = ActionManager.Instance();
        return manager is null ? uint.MaxValue : manager->GetActionStatus(ActionType.GeneralAction, actionId);
    }

    public bool TryUseGeneralAction(uint actionId)
    {
        ActionManager* manager = ActionManager.Instance();
        return manager is not null && manager->UseAction(ActionType.GeneralAction, actionId);
    }

    public NativeMobState ReadMobState(IBattleNpc mob)
    {
        if (mob.Address == nint.Zero)
            return default;

        BattleChara* battleChara = (BattleChara*)mob.Address;
        return new(battleChara->FateId, battleChara->CombatTagType, battleChara->CombatTaggerId);
    }

    public bool IsFriendly(IGameObject obj)
    {
        if (obj.Address == nint.Zero)
            return false;

        GameObjectStruct* gameObject = (GameObjectStruct*)obj.Address;
        return ActionManager.CanUseActionOnTarget(7568, gameObject)
            || (obj.ObjectKind == ObjectKind.EventNpc && ActionManager.CanUseActionOnTarget(120, gameObject));
    }

    public bool IsInCombat(IBattleNpc npc) =>
        npc.Address != nint.Zero && ((BattleChara*)npc.Address)->InCombat;
}
