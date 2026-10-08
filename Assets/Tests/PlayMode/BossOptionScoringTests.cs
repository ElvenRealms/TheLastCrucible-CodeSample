using NUnit.Framework;

/// <summary>
/// The scoring rules behind the boss's choices: conditions gate an option, considerations
/// scale its weight, what the boss remembers about an option feeds both, and a roll picks
/// among the scores.
/// </summary>
[Category(TestGroups.Boss)]
public class BossOptionScoringTests
{
    private readonly BossOptionMemory _memory = new();

    [SetUp]
    public void SetUp()
    {
        _memory.Reset();
    }

    [Test]
    public void Score_FailedCondition_IsZero()
    {
        var scoring = new BossOptionScoring(2f, new BossCondition[] { new BossSituationCondition(BossSituation.TargetBehind) });
        var behind = new BossDecisionContext(1f, 0f, false, true, false, false);

        Assert.That(scoring.Score(new BossDecisionContext(1f, 0f), _memory), Is.Zero);
        Assert.That(scoring.Score(behind, _memory), Is.EqualTo(2f));
    }

    [Test]
    public void Score_ConsiderationsMultiplyTheWeight()
    {
        var scoring = new BossOptionScoring(2f, null, new BossConsideration[]
        {
            new BossSituationConsideration(BossSituation.TargetHealing, 3f),
            new BossSituationConsideration(BossSituation.Retaliating, 4f)
        });
        var healing = new BossDecisionContext(1f, 0f, true, false, false, false);
        var healingAndRetaliating = new BossDecisionContext(1f, 0f, true, false, false, true);

        Assert.That(scoring.Score(new BossDecisionContext(1f, 0f), _memory), Is.EqualTo(2f));
        Assert.That(scoring.Score(healing, _memory), Is.EqualTo(6f));
        Assert.That(scoring.Score(healingAndRetaliating, _memory), Is.EqualTo(24f));
    }

    [Test]
    public void CooldownCondition_HoldsUntilStartedAndAgainOnceItHasRun()
    {
        var cooldown = new BossCooldownCondition(5f);
        var context = new BossDecisionContext(1f, 0f);

        Assert.That(cooldown.IsMet(context, _memory), Is.True, "An option never started is not cooling down.");
        _memory.NoteStarted();
        _memory.Tick(4.9f);
        Assert.That(cooldown.IsMet(context, _memory), Is.False);
        _memory.Tick(0.2f);
        Assert.That(cooldown.IsMet(context, _memory), Is.True);
    }

    [Test]
    public void PickIndex_PicksByCumulativeScore()
    {
        var scores = new[] { 1f, 2f, 1f };

        Assert.That(BossOptionPicker.PickIndex(scores, 0f), Is.EqualTo(0));
        Assert.That(BossOptionPicker.PickIndex(scores, 0.24f), Is.EqualTo(0));
        Assert.That(BossOptionPicker.PickIndex(scores, 0.25f), Is.EqualTo(1));
        Assert.That(BossOptionPicker.PickIndex(scores, 0.74f), Is.EqualTo(1));
        Assert.That(BossOptionPicker.PickIndex(scores, 0.75f), Is.EqualTo(2));
        Assert.That(BossOptionPicker.PickIndex(scores, 1f), Is.EqualTo(2));
    }

    [Test]
    public void PickIndex_NeverPicksAZeroScore()
    {
        Assert.That(BossOptionPicker.PickIndex(new[] { 0f, 1f, 0f }, 0f), Is.EqualTo(1));
        Assert.That(BossOptionPicker.PickIndex(new[] { 0f, 1f, 0f }, 1f), Is.EqualTo(1));
        Assert.That(BossOptionPicker.PickIndex(new[] { 0f, 0f }, 0.5f), Is.EqualTo(-1));
        Assert.That(BossOptionPicker.PickIndex(new float[0], 0.5f), Is.EqualTo(-1));
    }
}
