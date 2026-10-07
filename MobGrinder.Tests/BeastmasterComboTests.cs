namespace MobGrinder.Tests;

public sealed class BeastmasterComboTests
{
    private const uint Smash = BeastmasterCatalog.AttackActionId;
    private const uint Bite = BeastmasterComboPolicy.AxebladeBiteActionId;
    private const uint Split = BeastmasterComboPolicy.ShieldsplitterActionId;

    [Fact]
    public void OpeningCaptureAndPendingThresholdCaptureKeepSingleAttackUntilIssued()
    {
        BeastmasterCombatPolicy capture = new();
        BeastmasterComboPolicy combo = new();
        capture.CaptureIssued(100, 100, 30);
        Assert.False(capture.HasThresholdCapture);
        Assert.Equal(Smash, combo.NextAttack(1, capture.HasThresholdCapture, Bite, 20, true, true));
        combo.AttackIssued(Smash);
        Assert.Equal(Smash, combo.NextAttack(1, capture.HasThresholdCapture, Smash, 20, true, true));

        Assert.True(capture.ShouldCapture(30, 20, 30, 100, 30));
        Assert.True(capture.ShouldCapture(30, 20, 30, 100, 30));
        Assert.False(capture.HasThresholdCapture);
        capture.CaptureIssued(30, 100, 30);
        Assert.True(capture.HasThresholdCapture);
        // Capture preserves the old combo, but the new phase must start with Smash.
        Assert.Equal(Smash, combo.NextAttack(1, capture.HasThresholdCapture, Smash, 20, true, true));
        combo.AttackIssued(Smash);
        Assert.Equal(Bite, combo.NextAttack(1, capture.HasThresholdCapture, Smash, 20, true, true));
        capture.Reset();
        Assert.False(capture.HasThresholdCapture);
        Assert.Equal(Smash, combo.NextAttack(1, capture.HasThresholdCapture, Bite, 20, true, true));
    }

    [Fact]
    public void CompleteComboRepeatsAndRequiresActualGameComboProgress()
    {
        BeastmasterComboPolicy combo = new();
        Assert.Equal(Smash, combo.NextAttack(1, true, Bite, 20, true, true));
        combo.AttackIssued(Smash);
        Assert.Equal(Bite, combo.NextAttack(1, true, Smash, 20, true, true));
        combo.AttackIssued(Bite);
        // Issuing a request does not invent the next combo stage.
        Assert.Equal(Bite, combo.NextAttack(1, true, Smash, 20, true, true));
        Assert.Equal(Split, combo.NextAttack(1, true, Bite, 20, true, true));
        combo.AttackIssued(Split);
        Assert.Equal(Smash, combo.NextAttack(1, true, Split, 20, true, true));
        combo.AttackIssued(Smash);
        Assert.Equal(Bite, combo.NextAttack(1, true, Smash, 20, true, true));
    }

    [Theory]
    [InlineData(false, false, Smash, Smash)]
    [InlineData(false, true, Smash, Smash)]
    [InlineData(true, false, Bite, Smash)]
    [InlineData(true, true, Bite, Split)]
    public void UnlearnedFollowupRestartsWithoutSkippingPredecessor(bool biteLearned, bool splitLearned,
        uint afterSmash, uint afterBite)
    {
        BeastmasterComboPolicy combo = new();
        Assert.Equal(Smash, combo.NextAttack(1, true, 0, 0, biteLearned, splitLearned));
        combo.AttackIssued(Smash);
        Assert.Equal(afterSmash, combo.NextAttack(1, true, Smash, 20, biteLearned, splitLearned));
        Assert.Equal(afterBite, combo.NextAttack(1, true, Bite, 20, biteLearned, splitLearned));
    }

    [Theory]
    [InlineData(Smash, 0f)]
    [InlineData(Bite, -1f)]
    [InlineData(Smash, float.NaN)]
    [InlineData(999u, 20f)]
    public void ExpiredOrUnknownComboRestarts(uint previous, float remaining)
    {
        BeastmasterComboPolicy combo = new();
        combo.NextAttack(1, true, 0, 0, true, true);
        combo.AttackIssued(Smash);
        Assert.Equal(Smash, combo.NextAttack(1, true, previous, remaining, true, true));
    }

    [Fact]
    public void NewTargetAndResetStartAtSmashEvenWithRemainingGameCombo()
    {
        BeastmasterComboPolicy combo = new();
        combo.NextAttack(1, true, 0, 0, true, true);
        combo.AttackIssued(Smash);
        Assert.Equal(Bite, combo.NextAttack(1, true, Smash, 20, true, true));
        Assert.Equal(Smash, combo.NextAttack(2, true, Bite, 20, true, true));
        combo.AttackIssued(Smash);
        Assert.Equal(Bite, combo.NextAttack(2, true, Smash, 20, true, true));
        combo.Reset();
        Assert.Equal(Smash, combo.NextAttack(2, true, Smash, 20, true, true));
    }

    [Fact]
    public void FailedOpenerAndUnavailableFollowupDoNotAdvanceOrSkip()
    {
        BeastmasterComboPolicy combo = new();
        Assert.Equal(Smash, combo.NextAttack(1, true, Bite, 20, true, true));
        // A failed opener does not call AttackIssued, even when an old native combo remains.
        Assert.Equal(Smash, combo.NextAttack(1, true, Bite, 20, true, true));
        combo.AttackIssued(Smash);
        Assert.Equal(Bite, combo.NextAttack(1, true, Smash, 20, true, true));
        Assert.Equal(Bite, combo.NextAttack(1, true, Smash, 20, true, true));
        Assert.Equal(Split, combo.NextAttack(1, true, Bite, 20, true, true));
        Assert.Equal(Split, combo.NextAttack(1, true, Bite, 20, true, true));
    }
}
