using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// World presentation for an open Heal Window: looping aura and sound that run for
/// the attempt's lifetime. It observes PlayerHealing and never drives the attempt itself.
/// </summary>
public class PlayerHealingPresenter : MonoBehaviour
{
    [FoldoutGroup("References"), SerializeField, Required]
    private PlayerHealing playerHealing;
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly, Tooltip("Separate VFX child under the player root. May be inactive in the prefab; activated while healing and deactivated after its particles fade.")]
    private ParticleSystem auraParticles;
    [FoldoutGroup("References"), SerializeField, SceneObjectsOnly]
    [Tooltip("Optional attached sound loop controlled for the heal attempt's lifetime.")]
    private AudioLoopPlayer healingLoop;

    [FoldoutGroup("Config"), SerializeField, Tooltip("Tint applied to live aura particles when the attempt is cancelled.")]
    private Color cancelTint = Color.red;
    [FoldoutGroup("Config"), SerializeField, MinValue(0), Tooltip("Fastest remaining lifetime allowed for live aura particles when the attempt is cancelled.")]
    private float cancelFadeDuration = 0.25f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private bool IsAttempting => playerHealing != null && playerHealing.IsAttempting;

    private bool _isAuraStopping;

    private void Awake()
    {
        if (playerHealing == null) playerHealing = GetComponent<PlayerHealing>();
        ConfigureAuraSimulationSpace();
    }

    private void OnEnable()
    {
        if (playerHealing != null)
        {
            playerHealing.AttemptStarted += HandleAttemptStarted;
            playerHealing.AttemptEnded += HandleAttemptEnded;
        }

        if (IsAttempting)
        {
            PlayAura();
            PlayHealingLoop();
        }
        else
        {
            StopAura();
            StopHealingLoop();
        }
    }

    private void OnDisable()
    {
        if (playerHealing != null)
        {
            playerHealing.AttemptStarted -= HandleAttemptStarted;
            playerHealing.AttemptEnded -= HandleAttemptEnded;
        }

        StopAura();
        StopHealingLoop();
    }

    private void Update()
    {
        if (_isAuraStopping && auraParticles != null && !auraParticles.IsAlive(true))
        {
            StopAura();
        }
    }

    private void HandleAttemptStarted()
    {
        PlayAura();
        PlayHealingLoop();
        if (showDebugLogs) Debug.Log("[PlayerHealingPresenter] Heal attempt presentation started.", this);
    }

    private void HandleAttemptEnded(bool completed)
    {
        if (completed) SoftStopAura();
        else CancelAura();
        StopHealingLoop();
        if (showDebugLogs) Debug.Log($"[PlayerHealingPresenter] Heal attempt presentation ended (completed: {completed}).", this);
    }

    private void PlayAura()
    {
        if (auraParticles == null) return;

        _isAuraStopping = false;
        auraParticles.gameObject.SetActive(true);
        auraParticles.Clear(true);
        auraParticles.Play(true);
    }

    private void ConfigureAuraSimulationSpace()
    {
        if (auraParticles == null) return;

        foreach (var particles in auraParticles.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = particles.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
        }
    }

    // Completed: stop emitting and let live particles fade out over their own lifetime.
    private void SoftStopAura()
    {
        if (auraParticles == null) return;

        auraParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        _isAuraStopping = true;
    }

    // Cancelled: tint live particles and shrink their remaining lifetime so they
    // vanish quickly, then fully clear. Shortening the lifetime is what guarantees
    // the fast disappearance; the tint only reads if color-over-lifetime uses it.
    private void CancelAura()
    {
        if (auraParticles == null) return;

        int count = auraParticles.particleCount;
        if (count > 0)
        {
            ParticleSystem.Particle[] particles = new ParticleSystem.Particle[count];
            int written = auraParticles.GetParticles(particles);
            for (int i = 0; i < written; i++)
            {
                particles[i].startColor = cancelTint;
                particles[i].remainingLifetime = Mathf.Min(particles[i].remainingLifetime, cancelFadeDuration);
            }
            auraParticles.SetParticles(particles, written);
        }

        auraParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        _isAuraStopping = true;
    }

    private void StopAura()
    {
        _isAuraStopping = false;
        if (auraParticles == null) return;

        auraParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        auraParticles.gameObject.SetActive(false);
    }

    private void PlayHealingLoop()
    {
        if (healingLoop == null)
        {
            return;
        }

        healingLoop.Play();
    }

    private void StopHealingLoop()
    {
        if (healingLoop == null)
        {
            return;
        }

        healingLoop.Stop();
    }
}
