using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// One projectile volley: how many projectiles leave the boss, how they fly, what they deal and
/// how they look and sound. Fired by <see cref="BossProjectileLauncher"/>.
/// </summary>
[CreateAssetMenu(fileName = "BossProjectile", menuName = "BossFight/Attacks/Boss Projectile")]
public sealed class BossProjectileData : ScriptableObject
{
    [BoxGroup("Visuals"), SerializeField, Required, AssetsOnly]
    [Tooltip("Looping effect that rides each projectile. Its particle systems switch to Hierarchy scaling so Scale applies to all of them.")]
    private GameObject projectilePrefab;
    [BoxGroup("Visuals"), SerializeField, Tooltip("Scale of each projectile effect: X across the flight, Y up, Z along it.")]
    private Vector3 projectileScale = Vector3.one;
    [BoxGroup("Visuals"), SerializeField, AssetsOnly]
    [Tooltip("Looping effect that gathers on the boss's weapon from the start of the attack until the volley leaves, telling the player a ranged attack is coming.")]
    private GameObject chargePrefab;
    [BoxGroup("Visuals"), SerializeField, MinValue(0.01f), ShowIf(nameof(chargePrefab))]
    private float chargeScale = 1f;
    [BoxGroup("Visuals"), SerializeField, AssetsOnly, Tooltip("One-shot effect played where each projectile appears.")]
    private GameObject launchPrefab;
    [BoxGroup("Visuals"), SerializeField, MinValue(0.01f), ShowIf(nameof(launchPrefab))]
    private float launchScale = 1f;
    [BoxGroup("Visuals"), SerializeField, AssetsOnly, Tooltip("One-shot effect played where a projectile ends.")]
    private GameObject impactPrefab;
    [BoxGroup("Visuals"), SerializeField, MinValue(0.01f), ShowIf(nameof(impactPrefab))]
    private float impactScale = 1f;

    [BoxGroup("Volley"), SerializeField, MinValue(1)]
    private int count = 1;
    [BoxGroup("Volley"), SerializeField, Range(0f, 360f), SuffixLabel("deg")]
    [Tooltip("Angle the volley spreads over, centered on the aim direction. 360 spaces the projectiles evenly in a full ring.")]
    private float arc;
    [BoxGroup("Volley"), SerializeField, Range(0f, 180f), SuffixLabel("deg")]
    [Tooltip("Farthest the volley turns off the boss's facing, sideways or up and down, to aim at its target. 0 always fires straight ahead.")]
    private float aimAngle;
    [BoxGroup("Volley"), SerializeField, MinValue(0f), SuffixLabel("m"), Tooltip("Height above the boss's root at which the projectiles appear.")]
    private float height = 1.2f;
    [BoxGroup("Volley"), SerializeField, MinValue(0f), SuffixLabel("m"), Tooltip("Distance from the boss's root at which the projectiles appear.")]
    private float spawnDistance = 1f;

    [BoxGroup("Flight"), SerializeField, MinValue(0.1f), SuffixLabel("m/s")]
    private float speed = 12f;
    [BoxGroup("Flight"), SerializeField, MinValue(0.1f), SuffixLabel("m"), Tooltip("Flight distance from the boss's root before a projectile fizzles out.")]
    private float range = 20f;
    [BoxGroup("Flight"), SerializeField, MinValue(0.01f), SuffixLabel("m")]
    private float hitRadius = 0.6f;

    [BoxGroup("Damage"), SerializeField, MinValue(0f)]
    private float damage = 20f;
    [BoxGroup("Damage"), SerializeField, MinValue(0f), SuffixLabel("poise")]
    private float poiseDamage = 20f;
    [BoxGroup("Damage"), SerializeField]
    [Tooltip("Resolve each target once across the whole volley, including a dodge. Use for a ring whose neighbouring projectiles overlap.")]
    private bool hitOncePerVolley;
    [BoxGroup("Damage"), SerializeField]
    [Tooltip("Whether the defender's raised guard stops a projectile. An unblockable projectile lands through the guard.")]
    private bool blockable = true;
    [BoxGroup("Damage"), SerializeField]
    [Tooltip("Whether a projectile is parried when it lands inside the defender's Parry Window.")]
    private bool parryable;
    [BoxGroup("Damage"), SerializeField, MinValue(0f), SuffixLabel("poise"), ShowIf(nameof(parryable))]
    [Tooltip("Poise Damage the boss takes when one of these projectiles is parried.")]
    private float parryPoiseDamage = 15f;

    [BoxGroup("Feedback"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played once per volley, where it leaves the boss.")]
    private AudioCue launchCue;
    [BoxGroup("Feedback"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played where each projectile ends.")]
    private AudioCue impactCue;
    [BoxGroup("Feedback"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional feedback played when a projectile lands, on top of the target's own hit feedback.")]
    private CombatFeedbackProfile hitFeedback;

    public GameObject ProjectilePrefab => projectilePrefab;
    public Vector3 ProjectileScale => projectileScale;
    public GameObject ChargePrefab => chargePrefab;
    public float ChargeScale => chargeScale;
    public GameObject LaunchPrefab => launchPrefab;
    public float LaunchScale => launchScale;
    public GameObject ImpactPrefab => impactPrefab;
    public float ImpactScale => impactScale;
    public int Count => count;
    public float AimAngle => aimAngle;
    public float Height => height;
    public float SpawnDistance => spawnDistance;
    public float Speed => speed;
    public float Range => range;
    public float HitRadius => hitRadius;
    public float Damage => damage;
    public float PoiseDamage => poiseDamage;
    public bool HitOncePerVolley => hitOncePerVolley;
    public bool Blockable => blockable;
    public bool Parryable => parryable;
    public float ParryPoiseDamage => parryPoiseDamage;
    public AudioCue LaunchCue => launchCue;
    public AudioCue ImpactCue => impactCue;
    public CombatFeedbackProfile HitFeedback => hitFeedback;

    /// <summary>
    /// Yaw of one projectile off the volley's aim direction, in degrees. A full ring starts on the
    /// aim direction; a narrower arc is centered on it.
    /// </summary>
    public float GetYawOffset(int index)
    {
        if (count <= 1)
        {
            return 0f;
        }
        if (arc >= 360f)
        {
            return 360f * index / count;
        }
        return Mathf.Lerp(-arc * 0.5f, arc * 0.5f, (float)index / (count - 1));
    }
}
