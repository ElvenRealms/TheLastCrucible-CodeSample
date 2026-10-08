# Boss AI

How the boss chooses what to do. Read before changing anything in `Assets/Scripts/Boss/`.

## Where to tune it

`Assets/Data/Boss/BossDemonDecisions.asset` (a `BossOptionSet`) is the one place. It is one flat list of options: Enrage, every attack of both phases, Combat Turn, Strafe, Approach and Hold. Each option holds its name, priority, weight, conditions, considerations and action inline, so no value lives in another asset. Below the list are `Score Cutoff`, which sets how predictable the boss is, and the pacing settings (attack delay, chaining, reactions) for each phase.

Two thresholds the conditions read live elsewhere: `closeInDistance` and the combat pivot angles on `BossDemonMovementConfig`.

## Shape

- `BossStateMachine` runs one state at a time. Attack, Turn, Stagger, Enrage, Intro, Dormant and Dead are committed: they play out and own their Enter and Exit clean-up.
- `BossPursueState` is the one state in which the boss is free to act. Each frame it asks `BossBrain` to decide.
- `BossBrain` builds one `BossOption` per entry in the option set. It works down the priorities from the highest. At the first priority with an option scoring above 0 it picks one: options scoring under `Score Cutoff` x the best score are dropped, and the rest are picked by weighted chance. An option that declines (`TryStart` returns false) leaves the choice to the priorities below.
- A score is `weight x every consideration`, and 0 when any condition fails (`BossOptionScoring`).
- Conditions (`BossCondition`), considerations (`BossConsideration`) and actions (`BossAction`) are plain serializable classes held by `[SerializeReference]`, each picked from a dropdown on the option.
- `BossOptionMemory` is what the boss remembers about one option: seconds since it last started, whether it was the last started at its priority, and whether it is set aside. Rules receive it next to `BossDecisionContext`.

## Invariants

- No state names what follows it. A committed state calls `BossStateMachine.FinishAction()`, which returns to pursuit, or to Dormant or Dead when the encounter has no live target.
- Death, a Poise Break, and encounter activation and deactivation are interrupts raised by events. They are not options and are never scored.
- When an option is on offer is authored on its entry. The option classes hold only what the option does.
- Priorities as authored: Enrage 4, attacks 3, Combat Turn 2, Strafe and Approach 1, Hold 0. Only options sharing a priority compete by weight.
- Every attack entry carries: its phase (`Enraged` or `Not Enraged`), `Attack Delay Elapsed`, a range, a facing angle, and the `Just Used` consideration. Leaving one out removes that rule for that attack only.
- An attack is "in reach" when it scores above 0 in `BossDecisionContext.AsIfReady()`: the fight as it would read once the boss faced its target with its wait over. That is what `Attack In Reach` and `Holding Range` mean, so the boss stops closing in for an attack it only has to turn or wait for.
- The option set needs one entry with no conditions, so the boss always has something to do. Hold is that entry.
- A sustained option (Approach, Strafe, Hold) keeps running while it scores above 0, even when another option at its priority scores higher. That is what keeps the boss from changing its mind every frame.
- Two attacks that open with the same clip count as the same move for repeats (`BossOption.OpensLike`): Rising and Cleave, Lunge and Charge, Overhead and FireWave. Starting one marks the others as just used and restarts their `Since Used` clock. Cooldowns stay per option.
- Phase-one melee attacks also carry a `Since Used` curve (x0.35 at 6 s to x1 at 18 s), so the move before last is less likely than one not seen for a while.
- `Just Used` is a multiplier, 0.1 as authored, on the attack the boss started last. On its own that puts the attack under the score cutoff, so it cannot repeat while another attack is on offer, and still repeats when nothing else is. In its sweet spot (x4) a phase-two attack scores 0.4, which stays in the running at about a one in ten chance; in phase one `Since Used` pushes it under the cutoff.
- `Score Cutoff` is 0.35 as authored: an option needs 35% of the best score at its priority to stay in the running. An attack scoring more than 2.86x every other is therefore certain, and anything closer is a weighted mix. 0 is a plain weighted pick; 1 always takes the best.
- Base weights run from 0.7 to 1.2 so that, with nothing favoured, every attack in range stays in the mix. A weight under 0.35x the highest would never be picked. Sweet spots peak at x4, which makes a weight-1 attack certain against the rest; the Cleave pair (0.8 and 0.7) peaks at 3.2 and 2.8, so they share the bait between them, and in phase two Crush (1.2) stays in at about one in six.
- A curve consideration (`BossCurveConsideration`) reads Distance, Facing Angle, Boss Health or Since Used. The curve's horizontal axis is the input in its own unit (metres, degrees, health from 0 to 1, seconds) and its height is the multiplier. Values beyond either end use the nearest key.
- Range and Facing stay conditions: they say where the attack can land at all. Curves shape preference inside them.
- An attack always goes ahead, whatever its start chance, while a situation one of its considerations favours holds (`BossOptionScoring.Favours`). A curve is a preference and never skips the start chance. A passed attack is set aside for its pass duration.
- While a fight script chooses the attacks, only the queued attack is on offer, and it needs only its range and facing conditions: the attack delay, cooldown, repeat penalty, start chance and its other conditions are ignored. `QueueScriptedAttack` takes the first option of the current phase, in authored order, that opens with the attack, and refuses when the phase has none.
- Chaining and the length of the attack delay are pacing rules of the state machine, outside scoring.
- `BossDecisionContext` is read once per decision, and reading it casts against the level.
- Renaming or moving a condition, consideration or action class drops it from the asset, because `[SerializeReference]` stores the class name. Keep the data with `[MovedFrom]`.

## Baits as authored

| Stand or do this | Favoured attack | Rule |
| --- | --- | --- |
| Hug the boss, under 1.5 m | Rising | Distance 1.5 m x4 to 2.5 m x1 |
| Edge of melee, 3.5 m and beyond | Lunge | Distance 3.1 m x1 to 3.5 m x4 |
| Off its shoulder, 40 degrees | Cleave | Facing Angle 20 deg x1 to 40 deg x4 |
| Hit it several times running | Rising (phase one), Area Burst (phase two) | Retaliating x3, x4 |
| Behind it | Sweep | Target Behind condition |
| Hold the guard up | Delayed swings | Target Guarding x2 |
| Run out of stamina | Overhead | Target Exhausted x3 |
| Heal | Charge, FireWave | Target Healing x4, x3 |
| Back away | Lunge | Target Retreating x3 |

In phase two, Area Burst grows more likely as the boss's health falls (x1 at 50% to x2.5 at 10%).

## How far an attack travels

Set on each `BossAttackData`, not on the option:

- `Reach Distance` is where the swing connects without help. Until the Hit Window opens, distance matching stretches or shortens the boss's travel so the swing lands on the target, by at most `Max Reach Extension` beyond the clip's own travel.
- `Travel By Time` is a curve over the clip's normalized time that multiplies the clip's horizontal travel, on top of `Root Motion Scale`. Distance matching follows the same curve, so the stretch is spent where the curve is high and none where it is 0, and it makes up any travel the curve holds back or adds, so the swing lands where it would without the curve (`BossMotor.SetTravelScale`). Draw it at 0 where the feet are planted.
- An option's Range condition should not reach past `Reach Distance + Max Reach Extension`, or the attack starts from where it cannot land.
- Lunge and Charge share a clip whose feet stay planted through the wind-up, so their curve is 0 until 0.3 and all travel happens in the thrust.

## Adding to it

- A new rule: subclass `BossCondition` (`IsMet`) or `BossConsideration` (`Score`, a multiplier where 1 is neutral), mark it `[System.Serializable]`, and give it an `InspectorLabel`. It then appears in the dropdown of every option's list. What a rule can read is `BossDecisionContext` and the option's `BossOptionMemory`; a new yes-or-no fact is a `BossSituation` value filled in `BossStateMachine.CreateDecisionContext` or, for a read of the target, `BossReactions.CreateContext`, and a new number for curves is a `BossCurveInput` value.
- A new action: subclass `BossAction` and return the `BossOption` that carries it out from `CreateOption`. Nothing else in the decision code changes.
- A new committed action: a `BossState` that ends with `FinishAction()`, started from an option's `TryStart`.

## Checking it

- The Debug foldout on `BossStateMachine` shows a score table in Play Mode, with a detail column: the condition that fails, or each consideration moving the weight (`Distance x2.4, Just Used x0.1`). Reading it casts against the level, so it is for the Inspector and checks only.
- The score table shows what every option scores at the moment it is read, including while the boss is in a committed state. It is not a record of the last decision.
- `BossOptionScoringTests` covers the scoring maths, curves and the cutoff, `BossBrainTests` the priority order, sustained options and option memory, and `BossStateMachineTests` the states, passes, cooldowns and enrage.
