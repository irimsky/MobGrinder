namespace MobGrinder;

/// <summary>Per-object capture decisions. A successful Capture marks the beast; it must still be defeated.</summary>
public sealed class BeastmasterCombatPolicy
{
    private bool openingCaptureUsed;
    private bool thresholdCaptureUsed;

    public bool HasThresholdCapture => this.thresholdCaptureUsed;

    public void Reset() => (this.openingCaptureUsed, this.thresholdCaptureUsed) = (false, false);

    public static bool CanCapture(int playerLevel, int mobLevel) => mobLevel > 0 && mobLevel <= playerLevel;

    public static bool NeedsOpeningCapture(int playerLevel, int mobLevel) =>
        CanCapture(playerLevel, mobLevel) && (mobLevel < 10 || playerLevel - mobLevel >= 10);

    public bool ShouldCapture(int playerLevel, int mobLevel, uint hp, uint maxHp, float threshold) =>
        hp > 0 && maxHp > 0 && CanCapture(playerLevel, mobLevel)
        && ((!this.openingCaptureUsed && NeedsOpeningCapture(playerLevel, mobLevel))
            || (!this.thresholdCaptureUsed && hp * 100d / maxHp <= threshold));

    public void CaptureIssued(uint hp, uint maxHp, float threshold)
    {
        this.openingCaptureUsed = true;
        if (maxHp > 0 && hp * 100d / maxHp <= threshold)
            this.thresholdCaptureUsed = true;
    }
}
