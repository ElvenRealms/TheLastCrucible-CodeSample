using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Presents boss action SFX and encounter voice through independent playback policies.
/// Observes gameplay components and never drives them.
/// </summary>
[DisallowMultipleComponent]
public class BossAudioPresenter : MonoBehaviour
{
    [FoldoutGroup("References"), SerializeField, Required]
    private BossStateMachine stateMachine;
    [FoldoutGroup("References"), SerializeField, Required]
    private MeleeHitbox hitbox;
    [FoldoutGroup("References"), SerializeField, Required]
    private Health health;
    [FoldoutGroup("References"), SerializeField]
    private ArenaManager arenaManager;

    [FoldoutGroup("Voice Set"), SerializeField, AssetsOnly, InlineEditor]
    private BossVoiceSet voiceSet;

    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when the attack Hit Window opens.")]
    private AudioCue attackSwingCue;
    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when an attack wind-up starts.")]
    private AudioCue attackWindUpCue;
    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when the boss takes nonlethal damage.")]
    private AudioCue hurtCue;
    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when the boss dies.")]
    private AudioCue deathCue;
    [FoldoutGroup("Cues"), SerializeField, AssetsOnly, InlineEditor]
    [Tooltip("Optional one-shot played when the boss enters a stagger.")]
    private AudioCue staggerCue;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly, InlineEditor]
    private AudioCue _lastPlayedCue;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public AudioCue LastVoiceCue => _voicePlayback?.LastPlayedCue;

    private BossVoicePlayback _voicePlayback;

    public AudioVoiceHandle ActiveVoiceHandle => _voicePlayback?.ActiveVoiceHandle;
    public bool LowHealthVoicePlayed => _voicePlayback != null && _voicePlayback.LowHealthPlayed;
    public bool DeathVoiceRequested => _voicePlayback != null && _voicePlayback.DeathRequested;

    private void Awake()
    {
        if (stateMachine == null)
        {
            stateMachine = GetComponent<BossStateMachine>();
        }
        if (hitbox == null)
        {
            hitbox = GetComponent<MeleeHitbox>();
        }
        if (health == null)
        {
            health = GetComponent<Health>();
        }
        _voicePlayback = new BossVoicePlayback(health, transform, voiceSet);
    }

    private void OnEnable()
    {
        if (stateMachine != null)
        {
            stateMachine.StateChanged += HandleStateChanged;
            stateMachine.AttackWindUpStarted += HandleAttackWindUpStarted;
            stateMachine.Enraged += HandleEnraged;
        }
        if (hitbox != null)
        {
            hitbox.WindowOpened += HandleHitWindowOpened;
        }
        if (health != null)
        {
            health.Damaged += HandleDamaged;
            health.Died += HandleDied;
            health.Changed += HandleHealthChanged;
        }
        if (arenaManager != null)
        {
            arenaManager.EncounterActivated += HandleEncounterActivated;
            arenaManager.EncounterDeactivated += HandleEncounterDeactivated;
            if (arenaManager.IsActive && health != null && !health.IsDead)
            {
                HandleEncounterActivated(arenaManager.ActivePlayer != null ? arenaManager.ActivePlayer.transform : null);
            }
        }
    }

    private void OnDisable()
    {
        if (stateMachine != null)
        {
            stateMachine.StateChanged -= HandleStateChanged;
            stateMachine.AttackWindUpStarted -= HandleAttackWindUpStarted;
            stateMachine.Enraged -= HandleEnraged;
        }
        if (hitbox != null)
        {
            hitbox.WindowOpened -= HandleHitWindowOpened;
        }
        if (health != null)
        {
            health.Damaged -= HandleDamaged;
            health.Died -= HandleDied;
            health.Changed -= HandleHealthChanged;
        }
        if (arenaManager != null)
        {
            arenaManager.EncounterActivated -= HandleEncounterActivated;
            arenaManager.EncounterDeactivated -= HandleEncounterDeactivated;
        }
        _voicePlayback?.Deactivate();
    }

    private void Update()
    {
        _voicePlayback.Tick();
    }

    private void HandleEncounterActivated(Transform target)
    {
        _voicePlayback.Activate();
    }

    private void HandleEncounterDeactivated()
    {
        _voicePlayback.Deactivate();
    }

    private void HandleHealthChanged(float currentHealth, float maximumHealth)
    {
        _voicePlayback.HealthChanged(currentHealth, maximumHealth);
    }

    private void HandleEnraged()
    {
        _voicePlayback.Enrage();
    }

    private void HandleStateChanged(BossState state)
    {
        if (state is BossStaggerState)
        {
            PlayCue(staggerCue);
        }
    }

    private void HandleAttackWindUpStarted(BossAttackData attack)
    {
        PlayCue(attackWindUpCue);
    }

    private void HandleHitWindowOpened()
    {
        PlayCue(attackSwingCue);
    }

    private void HandleDamaged(DamageInfo info)
    {
        // The lethal hit is voiced by deathCue.
        if (health.IsDead)
        {
            return;
        }
        PlayCue(hurtCue);
    }

    private void HandleDied()
    {
        PlayCue(deathCue);
        _voicePlayback.Die();
    }

    private void PlayCue(AudioCue cue)
    {
        if (cue == null)
        {
            return;
        }

        if (cue.CountValidClips() == 0)
        {
            Debug.LogWarning($"[BossAudioPresenter] Skipped '{cue.name}': no AudioClip is assigned.", this);
            return;
        }

        if (cue.SpatialMode == AudioSpatialMode.Spatial)
        {
            AudioPlaybackService.Instance?.PlayAtPosition(cue, transform.position);
        }
        else
        {
            AudioPlaybackService.Instance?.Play(cue);
        }

        _lastPlayedCue = cue;
        if (showDebugLogs)
        {
            Debug.Log($"[BossAudioPresenter] Played '{cue.name}'.", this);
        }
    }
}
