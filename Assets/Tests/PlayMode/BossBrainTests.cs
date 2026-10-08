using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// How BossBrain chooses among options: higher priorities first, a declined option leaving the
/// choice to the priorities below, and a sustained option kept for as long as it stays on offer.
/// </summary>
[Category(TestGroups.Boss)]
public class BossBrainTests
{
    private const int Phase = 4;
    private const int Attack = 3;
    private const int Reposition = 2;
    private const int Movement = 1;
    private const int Idle = 0;

    private readonly List<string> _calls = new();

    [SetUp]
    public void SetUp()
    {
        _calls.Clear();
    }

    [Test]
    public void Decide_StartsTheHighestPriorityOnOffer()
    {
        var phase = new TestOption("phase", Phase, _calls);
        var attack = new TestOption("attack", Attack, _calls);
        var move = new TestOption("move", Movement, _calls) { sustained = true };
        BossBrain brain = CreateBrain(move, attack, phase);

        brain.Decide();

        Assert.That(_calls, Is.EqualTo(new[] { "phase.start" }));
        Assert.That(brain.Chosen, Is.SameAs(phase));
    }

    [Test]
    public void Decide_SkipsOptionsThatScoreZero()
    {
        var attack = new TestOption("attack", Attack, _calls) { score = 0f };
        var turn = new TestOption("turn", Reposition, _calls);
        BossBrain brain = CreateBrain(attack, turn);

        brain.Decide();

        Assert.That(_calls, Is.EqualTo(new[] { "turn.start" }));
    }

    [Test]
    public void Decide_DeclinedOptionLeavesTheChoiceToLowerPriorities()
    {
        var attack = new TestOption("attack", Attack, _calls) { starts = false };
        var turn = new TestOption("turn", Reposition, _calls) { starts = false };
        var move = new TestOption("move", Movement, _calls) { sustained = true };
        BossBrain brain = CreateBrain(attack, turn, move);

        brain.Decide();

        Assert.That(_calls, Is.EqualTo(new[] { "attack.start", "turn.start", "move.begin", "move.tick" }));
        Assert.That(brain.Chosen, Is.SameAs(move), "A declined option is not the one the boss went with.");
        Assert.That(brain.Sustained, Is.SameAs(move));
    }

    [Test]
    public void Decide_StopsTheSustainedOptionWhenAnotherTakesOver()
    {
        var move = new TestOption("move", Movement, _calls) { sustained = true };
        var hold = new TestOption("hold", Idle, _calls) { sustained = true };
        BossBrain brain = CreateBrain(move, hold);

        brain.Decide();
        move.score = 0f;
        brain.Decide();

        Assert.That(_calls, Is.EqualTo(new[] { "move.begin", "move.tick", "move.stop", "hold.begin", "hold.tick" }));
    }

    [Test]
    public void Decide_NeverStartsAnOptionUnderTheScoreCutoff()
    {
        var strong = new TestOption("strong", Attack, _calls) { score = 3f };
        var weak = new TestOption("weak", Attack, _calls) { score = 1f };
        var brain = new BossBrain(new TestSource { ScoreCutoff = 0.5f }, new BossOption[] { weak, strong });

        for (int i = 0; i < 200; i++)
        {
            brain.Decide();
        }

        Assert.That(_calls, Has.None.EqualTo("weak.start"));
        Assert.That(_calls.Count, Is.EqualTo(200));
    }

    private static BossBrain CreateBrain(params BossOption[] options)
    {
        return new BossBrain(new TestSource(), options);
    }

    private class TestSource : IBossDecisionSource
    {
        public float ScoreCutoff { get; set; }

        public BossDecisionContext CreateDecisionContext()
        {
            return new BossDecisionContext(1f, 0f);
        }
    }

    private class TestOption : BossOption
    {
        public float score = 1f;
        public bool sustained;
        public bool starts = true;

        private readonly string _name;
        private readonly int _priority;
        private readonly List<string> _calls;

        public TestOption(string name, int priority, List<string> calls) : base(null, null)
        {
            _name = name;
            _priority = priority;
            _calls = calls;
        }

        public override string Name => _name;
        public override int Priority => _priority;
        public override bool IsSustained => sustained;

        public override float Score(in BossDecisionContext context) => score;

        public override bool TryStart()
        {
            _calls.Add(_name + ".start");
            return starts;
        }

        public override void Begin() => _calls.Add(_name + ".begin");

        public override void Tick() => _calls.Add(_name + ".tick");

        public override void Stop() => _calls.Add(_name + ".stop");
    }
}
