using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace MobGrinder;

/// <summary>
/// Framework-thread-only boundary for native ClientStructs reads used by automation.
/// </summary>
public readonly record struct NativeMobState(ushort FateId, byte CombatTagType, ulong CombatTaggerId);

public readonly record struct NativeComboState(uint ActionId, float TimeRemaining);

public sealed unsafe class NativeGameStateAdapter
{
    public NativeComboState ReadComboState()
    {
        ActionManager* manager = ActionManager.Instance();
        return manager is null ? default : new(manager->Combo.Action, manager->Combo.Timer);
    }

    public void StopAutoAttack()
    {
        UIState* state = UIState.Instance();
        if (state is not null && state->WeaponState.AutoAttackState.Get())
            state->WeaponState.AutoAttackState.Set(false);
    }

    public bool TryUseTargetedAction(uint actionId, IPlayerCharacter player, IBattleNpc target)
    {
        ActionManager* manager = ActionManager.Instance();
        if (manager is null || player.Address == nint.Zero || target.Address == nint.Zero)
            return false;
        var delta = target.Position - player.Position;
        ((GameObjectStruct*)player.Address)->SetRotation(MathF.Atan2(delta.X, delta.Z));
        return manager is not null
            && manager->GetActionStatus(ActionType.Action, actionId, target.GameObjectId) == 0
            && manager->UseAction(ActionType.Action, actionId, target.GameObjectId);
    }

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
