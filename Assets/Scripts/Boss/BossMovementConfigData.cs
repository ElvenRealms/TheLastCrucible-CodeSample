using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Navigation, facing, and attack-gating tuning for the boss.
/// </summary>
[CreateAssetMenu(fileName = "BossMovementConfig", menuName = "BossFight/Boss Movement Config")]
public class BossMovementConfigData : ScriptableObject
{
    [BoxGroup("Navigation"), SuffixLabel("m/s"), MinValue(0f)]
    public float speed = 3.5f;
    [BoxGroup("Navigation"), SuffixLabel("m/s²"), MinValue(0f)]
    public float acceleration = 8f;

    [BoxGroup("Gait"), SuffixLabel("m/s"), MinValue(0f), Tooltip("Pursuit speed while the target is inside Walk Distance.")]
    public float walkSpeed = 1.5f;
    [BoxGroup("Gait"), SuffixLabel("m"), MinValue(0f), Tooltip("Planar distance to the target inside which pursuit walks at Walk Speed instead of running at Speed. 0 always runs.")]
    public float walkDistance;
    [BoxGroup("Gait"), SuffixLabel("m"), MinValue(0f), Tooltip("How far beyond Walk Distance the target must move before a walking boss runs again, so the gait does not flicker at the boundary.")]
    public float runHysteresis = 1f;

    [BoxGroup("Rotation"), SuffixLabel("deg/s"), MinValue(0f)]
    public float rotationSpeed = 120f;
    [BoxGroup("Rotation"), SuffixLabel("s"), MinValue(0f), Tooltip("Smoothing time for procedural facing corrections (Mathf.SmoothDampAngle), capped by rotationSpeed.")]
    public float rotationSmoothTime = 0.12f;
    [BoxGroup("Rotation"), MinValue(0.1f), SuffixLabel("x"), Tooltip("Playback speed multiplier applied to every authored turn clip on top of the clip transition's own Speed. Root-motion yaw scales with it.")]
    public float turnSpeedMultiplier = 1f;
    [BoxGroup("Rotation"), SuffixLabel("deg"), Range(0f, 90f), Tooltip("An authored turn ends early once the boss is within this many degrees of its target heading, instead of playing out the clip's settle.")]
    public float turnExitAngle = 10f;
    [BoxGroup("Rotation"), SuffixLabel("deg"), Range(0f, 180f), Tooltip("Maximum live-target correction applied around an authored turn's entry heading as the target or path moves.")]
    [FormerlySerializedAs("combatPivotMaxTargetAdjustment")]
    public float turnMaxTargetAdjustment = 35f;

    [BoxGroup("Distance Matching"), SuffixLabel("m/s"), MinValue(0f), Tooltip("Fastest distance matching may stretch or shorten travel, on top of the clip's own root motion.")]
    public float maxDistanceMatchSpeed = 6f;

    [BoxGroup("Attack Gating"), Tooltip("Environment layers that block a Line of Attack.")]
    public LayerMask lineOfAttackObstacles;
    [BoxGroup("Attack Gating"), SuffixLabel("m"), MinValue(0f), Tooltip("While an attack in range cannot start yet (attack delay or facing), keep closing in when the target is farther than this instead of holding position. 0 always holds.")]
    public float closeInDistance;

    [BoxGroup("Strafe"), SuffixLabel("m/s"), MinValue(0f), Tooltip("Sideways speed while the boss circles the target, waiting in attack range for its next attack. Match the strafe clips' stride. 0 holds position instead.")]
    public float strafeSpeed = 0.7f;
    [BoxGroup("Strafe"), SuffixLabel("s"), MinValue(0f), Tooltip("Shortest time the boss circles one way before switching sides.")]
    public float strafeMinDuration = 1.5f;
    [BoxGroup("Strafe"), SuffixLabel("s"), MinValue(0f), Tooltip("Longest time the boss circles one way before switching sides.")]
    public float strafeMaxDuration = 3f;

    [BoxGroup("Combat Facing"), SuffixLabel("deg"), Range(0f, 180f), Tooltip("Facing error at which a large combat pivot may begin.")]
    public float combatPivotStartAngle = 110f;
    [BoxGroup("Combat Facing"), SuffixLabel("deg"), Range(0f, 180f), Tooltip("Facing error at or below which a completed combat pivot may arm again.")]
    public float combatPivotRearmAngle = 35f;

    [BoxGroup("Hit Shove"), SuffixLabel("m"), MinValue(0f), Tooltip("Distance a hit pushes the boss at or above Full Poise Damage, outside of its attacks. 0 disables the shove.")]
    public float hitShoveDistance = 0.35f;
    [BoxGroup("Hit Shove"), SuffixLabel("poise"), MinValue(0f), Tooltip("Hits at or below this Poise Damage do not move the boss.")]
    public float hitShoveMinPoiseDamage = 10f;
    [BoxGroup("Hit Shove"), SuffixLabel("poise"), MinValue(0f), Tooltip("Poise Damage that pushes the full Hit Shove Distance. Hits between the two values scale linearly.")]
    public float hitShoveFullPoiseDamage = 50f;
    [BoxGroup("Hit Shove"), SuffixLabel("s"), MinValue(0f), Tooltip("Seconds the push takes, fastest at the start. 0 moves the boss at once.")]
    public float hitShoveDuration = 0.18f;
    [BoxGroup("Hit Shove"), Range(0f, 1f), Tooltip("0 pushes straight away from the attacker; 1 pushes away from the point that was struck, so a hit on a flank moves the boss sideways.")]
    public float hitShoveHitPointBias = 0.5f;

    [BoxGroup("Pursuit"), Tooltip("Speed multiplier by heading error (degrees) between forward and the path direction. Only applies below BossTurnSet.minimumTurnAngle; larger errors start a turn instead.")]
    public AnimationCurve speedByHeadingError = new(
        new Keyframe(0f, 1f, 0f, -0.04f),
        new Keyframe(30f, 0.4f, 0f, 0f));
}
