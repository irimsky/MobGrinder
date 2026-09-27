using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace MobGrinder;

public sealed unsafe class MountAdapter(ICondition condition, IObjectTable objectTable) : FieldNavigation.ITravelRecoveryControl
{
    private const uint MountRouletteActionId = 9;
    private const uint DismountActionId = 23;

    public bool IsMounted
    {
        // ConditionFlag.Mounted is the state that the game has actually exposed to
        // plugins.  BattleChara.IsMounted() can change earlier/later while the mount
        // action is being processed, so it must not be used to authorize navigation
        // or to skip the dismount step.
        get => this.IsConditionMounted;
    }

    public bool IsConditionMounted => condition[ConditionFlag.Mounted] || condition[ConditionFlag.RidingPillion];

    public bool IsNativeMounted
    {
        get
        {
            IGameObject? player = objectTable.LocalPlayer;
            return player is { Address: not 0 } && ((BattleChara*)player.Address)->IsMounted();
        }
    }

    public bool HasMountedStateMismatch => this.IsConditionMounted != this.IsNativeMounted;

    public bool IsInFlight => condition[ConditionFlag.InFlight];
    public bool IsMountTransition => condition[ConditionFlag.Mounting] || condition[ConditionFlag.Mounting71] || condition[ConditionFlag.MountOrOrnamentTransition];
    public bool IsJumping => condition[ConditionFlag.Jumping] || condition[ConditionFlag.Jumping61];

    public bool CanAttemptMount => !this.IsConditionMounted
        && !this.IsNativeMounted
        && !this.IsMountTransition
        && !condition[ConditionFlag.InCombat]
        && !condition[ConditionFlag.Unconscious]
        && !condition[ConditionFlag.BetweenAreas]
        && !condition[ConditionFlag.BetweenAreas51]
        && !condition[ConditionFlag.OccupiedInCutSceneEvent]
        && !condition[ConditionFlag.Casting]
        && !this.IsJumping;

    public string MountBlockReason
    {
        get
        {
            if (this.IsConditionMounted)
                return "游戏条件已显示正在骑乘";
            if (this.IsNativeMounted)
                return "原生坐骑标志仍存在，等待游戏条件同步";
            if (this.IsMountTransition)
                return "坐骑切换进行中";
            if (condition[ConditionFlag.InCombat])
                return "战斗中";
            if (condition[ConditionFlag.Unconscious])
                return "角色失去意识";
            if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
                return "区域切换中";
            if (condition[ConditionFlag.OccupiedInCutSceneEvent])
                return "过场或事件占用中";
            if (condition[ConditionFlag.Casting])
                return "施法中";
            if (this.IsJumping)
                return "跳跃中";
            return string.Empty;
        }
    }

    public bool TryMount()
    {
        TargetSystem* targets = TargetSystem.Instance();
        if (targets != null)
        {
            targets->Target = null;
            targets->SoftTarget = null;
        }
        ActionManager* manager = ActionManager.Instance();
        return manager != null && manager->UseAction(ActionType.GeneralAction, MountRouletteActionId);
    }

    public bool TryDismount()
    {
        ActionManager* manager = ActionManager.Instance();
        return manager != null && manager->UseAction(ActionType.GeneralAction, DismountActionId);
    }

    public bool TryJump()
    {
        if (this.IsMountTransition || this.IsInFlight || this.IsJumping
            || condition[ConditionFlag.InCombat] || condition[ConditionFlag.Casting]
            || condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51])
            return false;

        ActionManager* manager = ActionManager.Instance();
        return manager != null && manager->UseAction(ActionType.GeneralAction, 2);
    }
}
