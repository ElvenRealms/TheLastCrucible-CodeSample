using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// SFX presentation for the player's actions and outcomes; observes the
/// player's gameplay components and never drives them.
/// </summary>
public class PlayerAudioPresenter : MonoBehaviour
{
    [FoldoutGroup("References"), SerializeField, Required]
    private PlayerStateMachine stateMachine;
    [FoldoutGroup("References"), SerializeField, Required]
    private MeleeHitbox hitbox;
    [FoldoutGroup("References"), SerializeField, Required]
    private Health health;
    [FoldoutGroup("References"), SerializeField, Required]
    private PlayerDefense defense;
    [FoldoutGroup("References"), SerializeField, Required]
    private PlayerStamina stamina;
    [FoldoutGroup("References"), SerializeField, Required]
    private LockOnController lockOn;

    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when the attack Hit Window opens.")]
    private AudioCue attackSwingCue;
    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when a dodge starts.")]
    private AudioCue dodgeCue;
    [FormerlySerializedAs("parryStartCue")]
    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when a lowered guard comes up.")]
    private AudioCue guardRaisedCue;
    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when the guard stops a hit.")]
    private AudioCue blockCue;
    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when a hit is parried.")]
    private AudioCue parrySuccessCue;
    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when a blocked hit breaks the guard.")]
    private AudioCue guardBrokenCue;
    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when nonlethal damage is accepted.")]
    private AudioCue hurtCue;
    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when the player dies.")]
    private AudioCue deathCue;
    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when an action is refused for lack of stamina.")]
    private AudioCue staminaRejectedCue;
    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when stamina exhaustion begins.")]
    private AudioCue exhaustedCue;
    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when a lock-on target is acquired.")]
    private AudioCue lockOnAcquiredCue;
    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when lock-on is released.")]
    private AudioCue lockOnReleasedCue;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly, InlineEditor]
    private AudioCue _lastPlayedCue;

    private void Awake()
    {
        if (stateMachine == null) stateMachine = GetComponent<PlayerStateMachine>();
        if (hitbox == null) hitbox = GetComponent<MeleeHitbox>();
        if (health == null) health = GetComponent<Health>();
        if (defense == null) defense = GetComponent<PlayerDefense>();
        if (stamina == null) stamina = GetComponent<PlayerStamina>();
        if (lockOn == null) lockOn = GetComponent<LockOnController>();
    }

    private void OnEnable()
    {
        if (stateMachine != null) stateMachine.StateChanged += HandleStateChanged;
        if (hitbox != null) hitbox.WindowOpened += HandleHitWindowOpened;
        if (health != null)
        {
            health.Damaged += HandleDamaged;
            health.Died += HandleDied;
        }
        if (defense != null)
        {
            defense.GuardRaised += HandleGuardRaised;
            defense.Parried += HandleParried;
            defense.Blocked += HandleBlocked;
            defense.GuardBroken += HandleGuardBroken;
        }
        if (stamina != null)
        {
            stamina.SpendRejected += HandleSpendRejected;
            stamina.ExhaustionChanged += HandleExhaustionChanged;
        }
        if (lockOn != null) lockOn.TargetChanged += HandleTargetChanged;
    }

    private void OnDisable()
    {
        if (stateMachine != null) stateMachine.StateChanged -= HandleStateChanged;
        if (hitbox != null) hitbox.WindowOpened -= HandleHitWindowOpened;
        if (health != null)
        {
            health.Damaged -= HandleDamaged;
            health.Died -= HandleDied;
        }
        if (defense != null)
        {
            defense.GuardRaised -= HandleGuardRaised;
            defense.Parried -= HandleParried;
            defense.Blocked -= HandleBlocked;
            defense.GuardBroken -= HandleGuardBroken;
        }
        if (stamina != null)
        {
            stamina.SpendRejected -= HandleSpendRejected;
            stamina.ExhaustionChanged -= HandleExhaustionChanged;
        }
        if (lockOn != null) lockOn.TargetChanged -= HandleTargetChanged;
    }

    private void HandleStateChanged(PlayerState state)
    {
        if (state is DodgeState) PlayCue(dodgeCue);
    }

    private void HandleHitWindowOpened()
    {
        PlayCue(attackSwingCue);
    }

    private void HandleDamaged(DamageInfo info)
    {
        // The lethal hit is voiced by deathCue.
        if (health.IsDead) return;
        PlayCue(hurtCue);
    }

    private void HandleDied()
    {
        PlayCue(deathCue);
    }

    private void HandleGuardRaised()
    {
        PlayCue(guardRaisedCue);
    }

    private void HandleParried(DamageInfo info)
    {
        PlayCue(parrySuccessCue);
    }

    private void HandleBlocked(DamageInfo info)
    {
        PlayCue(blockCue);
    }

    private void HandleGuardBroken(DamageInfo info)
    {
        PlayCue(guardBrokenCue);
    }

    private void HandleSpendRejected(StaminaAction action)
    {
        // Sprint exhaustion is voiced by exhaustedCue.
        if (action == StaminaAction.Sprint) return;
        PlayCue(staminaRejectedCue);
    }

    private void HandleExhaustionChanged(bool exhausted)
    {
        if (exhausted) PlayCue(exhaustedCue);
    }

    private void HandleTargetChanged(LockOnTarget target)
    {
        PlayCue(target != null ? lockOnAcquiredCue : lockOnReleasedCue);
    }

    private void PlayCue(AudioCue cue)
    {
        if (cue == null) return;

        if (cue.CountValidClips() == 0)
        {
            Debug.LogWarning($"[PlayerAudioPresenter] Skipped '{cue.name}': no AudioClip is assigned.", this);
            return;
        }

        if (cue.SpatialMode == AudioSpatialMode.Spatial) AudioPlaybackService.Instance?.PlayAtPosition(cue, transform.position);
        else AudioPlaybackService.Instance?.Play(cue);

        _lastPlayedCue = cue;
        if (showDebugLogs) Debug.Log($"[PlayerAudioPresenter] Played '{cue.name}'.", this);
    }
}
