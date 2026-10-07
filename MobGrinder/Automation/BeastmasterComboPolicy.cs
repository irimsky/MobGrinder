namespace MobGrinder;

/// <summary>Restart on a new combat target/phase; advance only using the game's confirmed combo.</summary>
public sealed class BeastmasterComboPolicy
{
    public const uint AxebladeBiteActionId = 44883; // 碎咬斧, level 2
    public const uint ShieldsplitterActionId = 44885; // 裂盾劈, level 12
    private ulong targetId;
    private bool comboActive;
    private bool openerIssued;

    public void Reset() => (this.targetId, this.comboActive, this.openerIssued) = (0, false, false);

    public uint NextAttack(ulong target, bool useCombo, uint comboAction, float comboRemaining,
        bool biteLearned, bool shieldsplitterLearned)
    {
        if (target != this.targetId || useCombo != this.comboActive)
        {
            this.targetId = target;
            this.comboActive = useCombo;
            this.openerIssued = false;
        }

        if (!useCombo || !this.openerIssued || !(comboRemaining > 0))
            return BeastmasterCatalog.AttackActionId;

        return comboAction switch
        {
            BeastmasterCatalog.AttackActionId when biteLearned => AxebladeBiteActionId,
            AxebladeBiteActionId when biteLearned && shieldsplitterLearned => ShieldsplitterActionId,
            _ => BeastmasterCatalog.AttackActionId,
        };
    }

    public void AttackIssued(uint actionId)
    {
        if (actionId == BeastmasterCatalog.AttackActionId)
            this.openerIssued = true;
    }
}
