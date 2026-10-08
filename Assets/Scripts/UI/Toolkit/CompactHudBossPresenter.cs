using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Presents boss encounter visibility, health chip damage, and the single
/// depleted-poise progress bar in the CompactHud UI Toolkit document.
/// </summary>
public class CompactHudBossPresenter : MonoBehaviour
{
    [FoldoutGroup("References"), SerializeField, Required]
    private UIDocument uiDocument;
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private ArenaManager arenaManager;
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private Health bossHealth;
    [FoldoutGroup("References"), SerializeField, SceneObjectsOnly]
    private BossStateMachine bossStateMachine;
    [FoldoutGroup("References"), SerializeField, SceneObjectsOnly]
    private Poise bossPoise;

    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float fadeInDuration = 0.4f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float fadeOutDuration = 0.6f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float deathHoldDuration = 1.2f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float chipHoldDuration = 0.35f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float chipDrainDuration = 0.4f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;

    private VisualElement _bossHud;
    private Label _startFightPrompt;
    private VisualElement _bossHealthFill;
    private VisualElement _bossHealthChip;
    private VisualElement _bossPoiseFill;
    private ToolkitHudBar _healthBar;
    private ToolkitHudBar _poiseBar;

    private float _opacity;
    private float _opacityStart;
    private float _opacityTarget;
    private float _opacityElapsed;
    private float _opacityDuration;
    private float _opacityDelay;
    private bool _opacityTransitioning;
    private bool _hasStarted;
    private bool _isBound;
    private bool _isSubscribed;
    private bool _missingViewLogged;

    private void Awake()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        if (bossStateMachine == null && bossHealth != null)
        {
            bossStateMachine = bossHealth.GetComponent<BossStateMachine>();
        }

        if (bossPoise == null && bossHealth != null)
        {
            bossPoise = bossHealth.GetComponent<Poise>();
        }
    }

    private void OnEnable()
    {
        if (_hasStarted)
        {
            InitializeAndSubscribe();
        }
    }

    private void Start()
    {
        _hasStarted = true;
        InitializeAndSubscribe();
    }

    private void Update()
    {
        if (_isBound && (uiDocument == null || !uiDocument.isActiveAndEnabled ||
            _bossHud.panel == null || uiDocument.rootVisualElement.Q<VisualElement>("bossHud") != _bossHud))
        {
            Unsubscribe();
            _healthBar.Stop();
            ResetAnimatedStyles();
            _isBound = false;
        }

        if (!_isBound)
        {
            InitializeAndSubscribe();
            if (!_isBound)
            {
                return;
            }
        }

        RefreshStartFightPrompt();
        float unscaledDeltaTime = Time.unscaledDeltaTime;
        if (bossPoise != null && bossPoise.Current <= 0f && IsPoiseBreakStagger())
        {
            RefreshPoise();
        }
        TickOpacity(unscaledDeltaTime);
    }

    private void OnDisable()
    {
        Unsubscribe();
        _healthBar?.Stop();
        _poiseBar?.Stop();
        ResetAnimatedStyles();
        _isBound = false;
    }

    private void InitializeAndSubscribe()
    {
        if (arenaManager == null || bossHealth == null || bossPoise == null)
        {
            Debug.LogError("Compact HUD gameplay references must be assigned.", this);
            enabled = false;
            return;
        }

        if (!TryBindView())
        {
            return;
        }

        if (!_isSubscribed)
        {
            Subscribe();
        }

        AdoptCurrentState();
    }

    private bool TryBindView()
    {
        if (uiDocument == null || !uiDocument.isActiveAndEnabled || uiDocument.rootVisualElement == null)
        {
            return false;
        }

        VisualElement root = uiDocument.rootVisualElement;
        _bossHud = root.Q<VisualElement>("bossHud");
        _startFightPrompt = root.Q<Label>("startFightPrompt");
        _bossHealthFill = root.Q<VisualElement>("bossHealthFill");
        _bossHealthChip = root.Q<VisualElement>("bossHealthChip");
        _bossPoiseFill = root.Q<VisualElement>("bossPoiseFill");

        if (_bossHud == null || _startFightPrompt == null || _bossHealthFill == null ||
            _bossHealthChip == null || _bossPoiseFill == null)
        {
            if (!_missingViewLogged)
            {
                Debug.LogError("CompactHudBossPresenter could not find the required CompactHud elements.", this);
                _missingViewLogged = true;
            }

            return false;
        }

        _bossHud.style.opacity = 0f;
        if (_bossHud.panel == null || float.IsNaN(_bossHud.resolvedStyle.width) ||
            _bossHud.resolvedStyle.width <= 0f)
        {
            return false;
        }

        _healthBar = new ToolkitHudBar();
        _healthBar.Bind(root.Q<VisualElement>("bossHealthBar"), _bossHealthFill, _bossHealthChip);
        _poiseBar = new ToolkitHudBar();
        _poiseBar.Bind(root.Q<VisualElement>("bossPoiseBar"), _bossPoiseFill);
        _isBound = true;
        _missingViewLogged = false;
        return true;
    }

    private void Subscribe()
    {
        if (_isSubscribed)
        {
            return;
        }

        if (arenaManager != null)
        {
            arenaManager.PhaseChanged += HandlePhaseChanged;
            arenaManager.EncounterReset += HandleEncounterReset;
        }

        if (bossHealth != null)
        {
            bossHealth.Changed += HandleBossHealthChanged;
            bossHealth.Died += HandleBossDied;
        }

        if (bossPoise != null)
        {
            bossPoise.Changed += HandlePoiseChanged;
        }

        if (bossStateMachine != null)
        {
            bossStateMachine.DeathAnimationCompleted += HandleDeathAnimationCompleted;
        }

        _isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed)
        {
            return;
        }

        if (arenaManager != null)
        {
            arenaManager.PhaseChanged -= HandlePhaseChanged;
            arenaManager.EncounterReset -= HandleEncounterReset;
        }

        if (bossHealth != null)
        {
            bossHealth.Changed -= HandleBossHealthChanged;
            bossHealth.Died -= HandleBossDied;
        }

        if (bossPoise != null)
        {
            bossPoise.Changed -= HandlePoiseChanged;
        }

        if (bossStateMachine != null)
        {
            bossStateMachine.DeathAnimationCompleted -= HandleDeathAnimationCompleted;
        }

        _isSubscribed = false;
    }

    private void AdoptCurrentState()
    {
        if (arenaManager == null || bossHealth == null)
        {
            return;
        }

        _opacity = arenaManager.IsActive && !bossHealth.IsDead ? 1f : 0f;
        _opacityTarget = _opacity;
        _opacityTransitioning = false;
        _opacityDelay = 0f;
        _bossHud.style.opacity = _opacity;
        RefreshStartFightPrompt();
        RefreshHealth(true);
        RefreshPoise();
    }

    // Won needs no bar change here: boss death and its animation drive the fade-out.
    private void HandlePhaseChanged(EncounterPhase phase)
    {
        RefreshStartFightPrompt();
        if (phase == EncounterPhase.Active)
        {
            Show();
        }
        else if (phase == EncounterPhase.Idle && !bossHealth.IsDead)
        {
            Hide();
        }
    }

    // The boss is back at full health, so the bar snaps rather than animating a heal.
    private void HandleEncounterReset()
    {
        RefreshStartFightPrompt();
        RefreshHealth(true);
        RefreshPoise();
    }

    // Waits for the reset that follows a player death, so the prompt returns with the
    // respawned player and the restored boss rather than over the death card.
    private void RefreshStartFightPrompt()
    {
        bool isVisible = arenaManager.Phase == EncounterPhase.Idle && !arenaManager.IsResetPending &&
            !bossHealth.IsDead;
        _startFightPrompt.EnableInClassList("start-fight-prompt--hidden", !isVisible);
    }

    private void HandleBossHealthChanged(float current, float maximum)
    {
        RefreshHealth(false);
    }

    private void HandleBossDied()
    {
        RefreshHealth(false);
        CancelOpacityTransition();
        if (bossStateMachine == null)
        {
            SetOpacityTarget(0f, fadeOutDuration, deathHoldDuration);
        }
    }

    private void HandleDeathAnimationCompleted()
    {
        CancelOpacityTransition();
        SetOpacityTarget(0f, fadeOutDuration, deathHoldDuration);
    }

    private void HandlePoiseChanged(float current, float maximum)
    {
        RefreshPoise();
        if (showDebugLogs)
        {
            Debug.Log($"[CompactHudBossPresenter] Poise refreshed: {current:F1}/{maximum:F1}.", this);
        }
    }

    private void Show()
    {
        if (bossHealth.IsDead)
        {
            return;
        }

        RefreshHealth(true);
        CancelOpacityTransition();
        SetOpacityTarget(1f, fadeInDuration, 0f);
        if (showDebugLogs)
        {
            Debug.Log("[CompactHudBossPresenter] Shown.", this);
        }
    }

    private void Hide()
    {
        CancelOpacityTransition();
        SetOpacityTarget(0f, fadeOutDuration, 0f);
    }

    private void RefreshHealth(bool immediate)
    {
        if (bossHealth == null)
        {
            return;
        }

        float ratio = bossHealth.MaxHealth > 0f
            ? Mathf.Clamp01(bossHealth.CurrentHealth / bossHealth.MaxHealth)
            : 0f;
        if (immediate)
        {
            _healthBar.SetImmediate(ratio);
        }
        else
        {
            _healthBar.TrackTo(ratio, chipHoldDuration, chipDrainDuration);
        }
    }

    // An empty poise gauge reads as a broken bar, so it only appears once the boss has taken poise damage.
    private void RefreshPoise()
    {
        float ratio = bossPoise != null ? GetPoiseRatio() : 0f;
        _poiseBar.SetImmediate(ratio);
        _poiseBar.Container.EnableInClassList("boss-poise-bar--empty", ratio <= 0f);
    }

    private float GetPoiseRatio()
    {
        if (bossPoise.Current <= 0f && IsPoiseBreakStagger())
        {
            return bossStateMachine.Stagger.GetRemainingFraction();
        }

        return bossPoise.Max > 0f
            ? Mathf.Clamp01(1f - bossPoise.Current / bossPoise.Max)
            : 0f;
    }

    private bool IsPoiseBreakStagger()
    {
        return bossStateMachine != null && bossStateMachine.Stagger != null &&
               bossStateMachine.CurrentState == bossStateMachine.Stagger;
    }

    private void SetOpacityTarget(float target, float duration, float delay)
    {
        _opacityStart = _opacity;
        _opacityTarget = target;
        _opacityElapsed = 0f;
        _opacityDuration = Mathf.Max(0f, duration);
        _opacityDelay = Mathf.Max(0f, delay);
        _opacityTransitioning = true;
        if (_opacityDelay <= 0f && _opacityDuration <= 0f)
        {
            _opacity = target;
            _bossHud.style.opacity = _opacity;
            _opacityTransitioning = false;
        }
    }

    private void TickOpacity(float unscaledDeltaTime)
    {
        if (_opacityDelay > 0f)
        {
            _opacityDelay -= unscaledDeltaTime;
            if (_opacityDelay > 0f)
            {
                return;
            }

            unscaledDeltaTime = -_opacityDelay;
            _opacityDelay = 0f;
        }

        if (!_opacityTransitioning)
        {
            return;
        }

        _opacityElapsed += unscaledDeltaTime;
        float normalized = _opacityDuration > 0f ? Mathf.Clamp01(_opacityElapsed / _opacityDuration) : 1f;
        _opacity = Mathf.Lerp(_opacityStart, _opacityTarget, 1f - (1f - normalized) * (1f - normalized));
        _bossHud.style.opacity = _opacity;
        if (normalized >= 1f)
        {
            _opacityTransitioning = false;
        }
    }

    private void CancelOpacityTransition()
    {
        _opacityDelay = 0f;
        _opacityTransitioning = false;
    }

    private void ResetAnimatedStyles()
    {
        if (!_isBound)
        {
            return;
        }

        _bossHud.style.opacity = 0f;
        _startFightPrompt.AddToClassList("start-fight-prompt--hidden");
    }
}
