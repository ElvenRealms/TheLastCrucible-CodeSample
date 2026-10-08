using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// A choreographed fight: beats played in order by a <see cref="FightChoreographer"/>. Each beat
/// waits for its cue, then has the player perform an action or the boss start a chosen attack.
/// Player beats press the same inputs a person would, so every move goes through normal gameplay rules.
/// </summary>
[CreateAssetMenu(fileName = "FightScript", menuName = "BossFight/Choreography/Fight Script")]
public class FightScript : ScriptableObject
{
    [FoldoutGroup("Setup"), SerializeField]
    [Tooltip("Lock on to the boss when the script starts.")]
    private bool startLockedOn = true;
    [FoldoutGroup("Setup"), SerializeField]
    [Tooltip("God mode for the player while the script plays, so a mistimed beat can't end the take. Health then stays full: the bar never drops and heals are refused.")]
    private bool playerInvulnerable;
    [FoldoutGroup("Setup"), SerializeField]
    [Tooltip("God mode for the boss while the script plays, for looping scripts that would otherwise kill it. Its health bar then never drops.")]
    private bool bossInvulnerable;
    [FoldoutGroup("Setup"), SerializeField]
    [Tooltip("Start again from the first beat after the last one.")]
    private bool loop;
    [FoldoutGroup("Setup"), SerializeField, MinValue(0.5f), SuffixLabel("s")]
    [Tooltip("Game seconds a beat waits for its boss cue, or for its boss attack to start, before it is skipped.")]
    private float cueTimeout = 8f;

    [SerializeField, ListDrawerSettings(ListElementLabelName = "Label", ShowIndexLabels = true)]
    private List<FightBeat> beats = new List<FightBeat>();

    public bool StartLockedOn => startLockedOn;
    public bool PlayerInvulnerable => playerInvulnerable;
    public bool BossInvulnerable => bossInvulnerable;
    public bool Loop => loop;
    public float CueTimeout => cueTimeout;
    public IReadOnlyList<FightBeat> Beats => beats;
}

/// <summary>Who performs a beat.</summary>
public enum FightActor
{
    Player,
    Boss
}

/// <summary>What a beat waits for before it starts.</summary>
public enum FightCue
{
    /// <summary>The previous beat finishing.</summary>
    AfterPrevious,
    /// <summary>A boss attack step starting its wind-up.</summary>
    BossWindUp,
    /// <summary>A boss attack step ending its wind-up, the moment its hit window opens.</summary>
    BossSwing,
    /// <summary>The boss leaving an attack sequence.</summary>
    BossAttackEnd,
    /// <summary>The boss entering stagger, after its poise breaks.</summary>
    BossStagger
}

/// <summary>The input a player beat presses when it starts. Block is a tap, timed against a swing to parry it.</summary>
public enum FightPlayerAction
{
    None,
    Attack,
    HeavyAttack,
    Dodge,
    Block,
    Heal,
    LockOn
}

/// <summary>Movement held during a player beat, relative to the boss.</summary>
public enum FightDirection
{
    None,
    Toward,
    Away,
    Left,
    Right
}

/// <summary>
/// One step of a <see cref="FightScript"/>. A player beat holds a direction for its duration and
/// presses its action at the start. A boss beat queues one of the boss's attack options.
/// </summary>
[System.Serializable]
public class FightBeat
{
    [SerializeField, Tooltip("Optional note shown in the list.")]
    private string note;
    [SerializeField, EnumToggleButtons]
    private FightActor actor;

    [BoxGroup("Cue"), SerializeField, LabelText("Wait For")]
    private FightCue cue;
    [BoxGroup("Cue"), SerializeField, AssetsOnly, ShowIf(nameof(UsesCueAttack)), LabelText("Of Attack")]
    [Tooltip("Only this attack counts. Wind-up and swing cues match one attack step, such as the second hit of a combo; attack end matches the opening attack. Empty matches any attack.")]
    private BossAttackData cueAttack;
    [BoxGroup("Cue"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Game seconds between the cue and the beat starting. Tune parries and dodges here.")]
    private float delay;

    [BoxGroup("Player"), SerializeField, ShowIf(nameof(IsPlayer))]
    private FightPlayerAction action;
    [BoxGroup("Player"), SerializeField, ShowIf(nameof(IsPlayer))]
    [Tooltip("Movement held for the beat's duration. A dodge rolls this way.")]
    private FightDirection direction;
    [BoxGroup("Player"), SerializeField, ShowIf(nameof(IsPlayer)), MinValue(0f), SuffixLabel("s")]
    [Tooltip("Game seconds before the next beat may start, with the direction held throughout.")]
    private float duration = 0.25f;
    [BoxGroup("Player"), SerializeField, ShowIf(nameof(ShowsSprint))]
    [Tooltip("Run while moving. Attack at the end of a run for a running attack.")]
    private bool sprint;
    [BoxGroup("Player"), SerializeField, ShowIf(nameof(ShowsStopDistance)), MinValue(0f), SuffixLabel("m")]
    [Tooltip("Ends the beat early once the player is this close to the boss (Toward) or this far away (Away). 0 always runs the full duration.")]
    private float stopDistance;

    [BoxGroup("Boss"), SerializeField, ShowIf(nameof(IsBoss)), AssetsOnly, Required]
    [Tooltip("Opening attack of one of the boss's attack options in its current phase. The boss moves into its range and facing, then starts it.")]
    private BossAttackData bossAttack;
    [BoxGroup("Boss"), SerializeField, ShowIf(nameof(IsBoss))]
    [Tooltip("Hold the next beat until the attack's wind-up starts. Off lets the next beat play while the boss moves into range.")]
    private bool waitForWindUp = true;

    public FightActor Actor => actor;
    public FightCue Cue => cue;
    public BossAttackData CueAttack => cueAttack;
    public float Delay => delay;
    public FightPlayerAction Action => action;
    public FightDirection Direction => direction;
    public float Duration => duration;
    public bool Sprint => sprint;
    public float StopDistance => stopDistance;
    public BossAttackData BossAttack => bossAttack;
    public bool WaitForWindUp => waitForWindUp;

    public string Label
    {
        get
        {
            string what = actor == FightActor.Boss
                ? $"Boss {(bossAttack != null ? bossAttack.name : "(no attack)")}"
                : $"Player {action}{(direction != FightDirection.None ? $" {direction}" : string.Empty)}";
            string when = cue == FightCue.AfterPrevious ? string.Empty : $" on {cue}";
            return string.IsNullOrEmpty(note) ? what + when : $"{note}: {what}{when}";
        }
    }

    private bool IsPlayer => actor == FightActor.Player;
    private bool IsBoss => actor == FightActor.Boss;
    private bool UsesCueAttack => cue is FightCue.BossWindUp or FightCue.BossSwing or FightCue.BossAttackEnd;
    private bool ShowsSprint => IsPlayer && direction != FightDirection.None;
    private bool ShowsStopDistance => IsPlayer && direction is FightDirection.Toward or FightDirection.Away;
}
