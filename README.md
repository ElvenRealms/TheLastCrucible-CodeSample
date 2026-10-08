# The Last Crucible: Code Sample

**The Last Crucible** is a soulslike boss fight: one arena, one two-phase boss, and a player
who has to read, block, parry and dodge to win. I built it solo in Unity 6 with C# in 2026.

- **Play the demo:** https://elvenslayer.itch.io/the-last-crucible-demo
- **Case study:** https://elvenrealms.dev/projects/the-last-crucible/

This repository holds the gameplay code from the project (211 C# files) and its Play Mode
tests. It is a code sample, not an openable Unity project. See [What's not included](#whats-not-included).

## A short tour

### Boss decisions: utility AI with scored options
`Assets/Scripts/Boss/Decision/`

The boss chooses what to do by scoring a flat list of authored options (attacks, Enrage, Combat Turn,
Strafe, Approach and Hold).

- `BossBrain.cs` goes through the priorities from highest to lowest and scores every option at
  each one. It then picks by weighted chance among the options that score close enough to the best;
  the `ScoreCutoff` sets how predictable the boss is. A sustained option (such as strafing) keeps
  running while it stays on offer, so the boss doesn't change its mind every frame.
- `BossOptionScoring.cs` gives each option a score: its weight, gated by conditions that must all
  hold (`BossRangeCondition`, `BossFacingCondition`, `BossHealthCondition`, `BossCooldownCondition`,
  `BossSituationCondition`) and scaled by considerations. `BossCurveConsideration` reads
  distance, angle, health or time since last use through an animation curve.
  `BossRepeatConsideration` stops the boss opening with the same move twice in a row.
- `BossOptionMemory.cs` tracks cooldowns and repeats on the boss's own clock, so they pause when
  the boss does. `Boss/BossReactions.cs` reads the player (healing, guarding, attacking, out of
  stamina, behind the boss, backing away) for the conditions to use.
- Adding a new rule or action means adding a subclass of `BossCondition`, `BossConsideration`
  or `BossAction`. Nothing else in the decision code has to change.
- `Assets/Data/Boss/BossDemonDecisions.asset` is the actual tuning asset for the boss, and
  [`Docs/Systems/BossAI.md`](Docs/Systems/BossAI.md) walks through how it fits together.

Committed actions run as plain C# states in `Assets/Scripts/Boss/States/` (attack chains, turn,
stagger, enrage, intro, death), owned by `Boss/BossStateMachine.cs`.

### Player: state machine and input buffer
`Assets/Scripts/Player/`

- `PlayerStateMachine.cs` owns the plain C# states in `Player/States/` (locomotion, attack, dodge,
  block, heal, flinch, dead). A request passes two gates: the current state's timing window (or a
  higher priority), and the new state's own entry rules.
- `PlayerInputBuffer.cs` holds a single pending action, and the last press wins. Its lifetime is
  set by the current action's authored buffer window, not by a timer. A held press fires on the
  first frame the current state lets go, or when that state finishes.
- `PlayerActionPriority.cs` lets a higher-ranked state (a flinch or death) interrupt the current one
  at any time, while equal or lower ranks wait for the timing window.
- `States/AttackState.cs` runs the light combo, the heavy combo and the running attack, sharing
  chaining, hit windows, root motion, tracking and buffering. `Combat/AttackPlayback.cs` applies
  each attack's authored speed segments and holds.
- `PlayerStamina.cs` and `PlayerDefense.cs` handle stamina costs, guarding, parry windows and
  guard breaks.

### Damage pipeline with interceptors
`Assets/Scripts/Combat/`

- `MeleeHitbox.cs` sweeps a blade capsule between last frame's pose and this frame's, in substeps,
  so fast swings can't pass through a target. It damages each target at most once per hit window.
- `Health.cs` resolves each hit. Sibling components that implement `IDamageInterceptor.cs` get
  first refusal: `Player/PlayerDefense.cs` uses this for dodge invulnerability, blocks and parries.
- Every hit returns a `HitResult` (`Applied`, `Parried`, `Blocked`, `Evaded`, `Ignored`) to whoever
  dealt it. Only an applied hit raises `Damaged`, `Changed` and, when lethal, `Died`.
- `Poise.cs` drains on the `Damaged` event and raises `Broken` when the pool is empty. Both the boss
  and the player use it.

### Event-driven UI and VFX
`Assets/Scripts/Presentation/`, `Assets/Scripts/UI/`

Gameplay code doesn't know who is listening. Presentation components subscribe to its events
instead. For example, `Presentation/BossCombatFeedback.cs` listens to `Health.Damaged`,
`Poise.Broken` and the hitbox's `HitResolved`, and `UI/Toolkit/CompactHudBossPresenter.cs` drives the
UI Toolkit HUD from `Health.Changed`, `Health.Died` and the arena's phase events.

- `CombatFeedbackController.cs` sends feedback requests to effect executors that register
  themselves, such as hit-stop, camera shake, FOV kick, slow motion, screen pulse, hit flash,
  gamepad rumble, and impact audio and VFX. It holds no references to the effects themselves.
- `Core/TimeScaleArbiter.cs` is the only thing that writes `Time.timeScale`. Hit-stop, slow motion,
  pause and cinematics each take a claim, the lowest scale wins, and releasing a claim restores
  whatever the remaining claims resolve to.
- `ClipEvents/ClipEventEmitter.cs` plays authored sound, VFX and camera events at points in each
  animation clip, and stays correct through speed changes, holds and loops. `VfxPool.cs` pools the
  effects.

### Play Mode tests
`Assets/Tests/PlayMode/`

There are 35 NUnit tests in seven fixtures, grouped into categories in `TestGroups.cs`:

- `BossBrainTests` covers the priority order, the score cutoff and sustained options.
- `BossOptionScoringTests` covers conditions, considerations, cooldowns and the weighted pick.
- `BossStateMachineTests` covers cooldowns in selection, stagger and death priority, and reset on
  player respawn.
- `PlayerDefenseTests` covers parry windows, block stamina costs, unblockable hits, guard arc and
  guard break.
- `PlayerStaminaTests` covers recovery delay, sprint exhaustion and stamina costs on actions.
- `HealthBoundaryTests` covers invalid input, clamping and event order.
- `TimeScaleArbiterTests` covers how competing time-scale claims resolve.

### Editor tooling
`Assets/Scripts/Editor/`

- The **Attack Timing** window (`AttackTimingWindow.cs`, `AttackTimeline.cs`) previews an attack
  clip on an isolated model, with draggable markers for hit windows, speed segments and holds.
- The **Clip Events** window (`ClipEvents/`) is used to author sound, VFX and camera events
  on animation clips.
- `Portfolio/PortfolioRecorder.cs` records Play Mode sessions to video, with a timestamped log of
  the gameplay events it's set to watch, so key moments are easy to find.

## What's not included

Art, animation, VFX, audio, scenes, prefabs and paid third-party plugins are left out for licence
reasons, so the project won't open or compile as it is. The code depends on these plugins:

- **Odin Inspector** (Sirenix) for Inspector attributes and Editor windows
- **Animancer** (Kybernetik) for animation playback
- **DOTween** (Demigiant) for UI and audio tweens

It also uses these Unity packages: Input System, Cinemachine, UI Toolkit/uGUI, TextMesh Pro,
URP, NavMesh, Recorder and Newtonsoft JSON.

`Assets/Scripts/Input/InputSystem_Actions.cs` is generated by the Input System from the project's
input actions.

## Code style

[`STYLE-GUIDE.md`](STYLE-GUIDE.md) lists the conventions the code follows: event-driven managers,
data, logic and presentation kept separate, and ScriptableObjects for shared tuning.
