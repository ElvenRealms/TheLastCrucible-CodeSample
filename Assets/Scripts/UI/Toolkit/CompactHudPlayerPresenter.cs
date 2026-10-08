using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Presents player health, stamina, healing charges, and player HUD visibility
/// in the CompactHud UI Toolkit document.
/// </summary>
public class CompactHudPlayerPresenter : MonoBehaviour
{
    [FoldoutGroup("References"), SerializeField, Required]
    private UIDocument uiDocument;
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private Health playerHealth;
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private PlayerStamina playerStamina;
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private PlayerHealing playerHealing;
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    [Tooltip("The HUD stays visible while the encounter is active, so full bars never fade out mid-fight.")]
    private ArenaManager arenaManager;

    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float fullHoldDuration = 2f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float fadeDuration = 0.15f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float rejectionFlashDuration = 0.4f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float fillTweenDuration = 0.2f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float chipHoldDuration = 0.35f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float chipDrainDuration = 0.4f;

    [FoldoutGroup("Health Feedback"), SerializeField, Range(0f, 1f)]
    private float dangerThreshold = 0.6f;
    [FoldoutGroup("Health Feedback"), SerializeField]
    private Color dangerColor = new Color(0.45f, 0.05f, 0.05f, 1f);
    [FoldoutGroup("Health Feedback"), SerializeField]
    private Color dangerPulseColor = new Color(0.75f, 0.12f, 0.1f, 1f);
    [FoldoutGroup("Health Feedback"), SerializeField, MinValue(0f)]
    private float dangerTransitionDuration = 0.2f;
    [FoldoutGroup("Health Feedback"), SerializeField, MinValue(0.01f)]
    private float pulseHalfPeriod = 0.3f;
    [FoldoutGroup("Health Feedback"), SerializeField, MinValue(0f)]
    private float punchScale = 0.08f;
    [FoldoutGroup("Health Feedback"), SerializeField, MinValue(0f)]
    private float punchDuration = 0.25f;

    [FoldoutGroup("Stamina Feedback"), SerializeField]
    private Color exhaustedColor = new Color(0.9f, 0.55f, 0.12f, 1f);
    [FoldoutGroup("Stamina Feedback"), SerializeField]
    private Color rejectedColor = new Color(0.67f, 0.172f, 0.123f, 1f);

    [FoldoutGroup("Healing Hint"), SerializeField]
    [Tooltip("Authored from the Player/Heal keyboard binding in InputSystem_Actions.inputactions. The gamepad binding is d-pad up.")]
    private string healKeyHint = "R";

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;

    private VisualElement _styledRoot;
    private VisualElement _playerHud;
    private VisualElement _playerHealthFill;
    private VisualElement _playerHealthChip;
    private VisualElement _playerStaminaFill;
    private VisualElement _healingItem;
    private VisualElement _healingIcon;
    private Label _healingCount;
    private Label _healingKey;
    private VisualElement _healingProgressTrack;
    private VisualElement _healingProgress;
    private VisualElement _playerHealthFillVisual;
    private VisualElement _playerStaminaFillVisual;
    private ToolkitHudBar _healthBar;
    private ToolkitHudBar _staminaBar;

    private Color _restingHealthColor;
    private Color _healthColor;
    private Color _restingStaminaColor;
    private Color _staminaColor;
    private Color _restingHealingIconColor;
    private Color _healingIconColor;
    private Color _restingHealingCountColor;
    private float _healthTargetRatio;
    private float _staminaDisplayedRatio;
    private float _staminaStartRatio;
    private float _staminaTargetRatio;
    private float _staminaTweenElapsed;
    private Color _healthColorStart;
    private float _healthColorElapsed;
    private float _healthColorDuration;
    private float _healthPulseElapsed;
    private float _punchElapsed;
    private float _healingFeedbackElapsed;
    private float _healingFeedbackColorElapsed;
    private float _opacity;
    private float _opacityStart;
    private float _opacityTarget;
    private float _opacityElapsed;
    private float _opacityDuration;
    private float _opacityDelay;
    private bool _healthColorTransitioning;
    private bool _healthColorExiting;
    private bool _healthPulseActive;
    private bool _punchActive;
    private bool _healingFeedbackActive;
    private bool _healingFeedbackColorActive;
    private bool _opacityTransitioning;
    private bool _hasStarted;
    private bool _isBound;
    private bool _isSubscribed;
    private bool _missingViewLogged;

    private Color _staminaColorStart;
    private Color _staminaColorTarget;
    private float _staminaColorElapsed;
    private float _staminaColorDuration;
    private float _staminaRejectionRemaining;
    private bool _staminaColorTransitioning;
    private void Awake()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
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
            _playerHud.panel == null || uiDocument.rootVisualElement.Q<VisualElement>("playerHud") != _playerHud))
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

        float unscaledDeltaTime = Time.unscaledDeltaTime;
        TickHealthColor(unscaledDeltaTime);
        TickStamina(unscaledDeltaTime);
        TickPunch(unscaledDeltaTime);
        TickHealing(unscaledDeltaTime);
        TickOpacity(unscaledDeltaTime);
    }

    private void OnDisable()
    {
        Unsubscribe();
        _healthBar?.Stop();
        _staminaBar?.Stop();
        ResetAnimatedStyles();
        _isBound = false;
    }

    private void InitializeAndSubscribe()
    {
        if (playerHealth == null || playerStamina == null || playerHealing == null || arenaManager == null)
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
        _playerHud = root.Q<VisualElement>("playerHud");
        _playerHealthFill = root.Q<VisualElement>("playerHealthFill");
        _playerHealthChip = root.Q<VisualElement>("playerHealthChip");
        _playerStaminaFill = root.Q<VisualElement>("playerStaminaFill");
        _healingItem = root.Q<VisualElement>("healingItem");
        _healingIcon = root.Q<VisualElement>("healingIcon");
        _healingCount = root.Q<Label>("healingCount");
        _healingKey = root.Q<Label>("healingKey");
        _healingProgressTrack = root.Q<VisualElement>("healingProgressTrack");
        _healingProgress = root.Q<VisualElement>("healingProgress");

        if (_playerHud == null || _playerHealthChip == null || _playerHealthFill == null || _playerStaminaFill == null || _healingItem == null ||
            _healingIcon == null || _healingCount == null || _healingKey == null || _healingProgressTrack == null ||
            _healingProgress == null)
        {
            if (!_missingViewLogged)
            {
                Debug.LogError("CompactHudPlayerPresenter could not find the required CompactHud elements.", this);
                _missingViewLogged = true;
            }

            return false;
        }

        _playerHealthFillVisual = _playerHealthFill.Q<VisualElement>(className: "bar-fill");
        _playerStaminaFillVisual = _playerStaminaFill.Q<VisualElement>(className: "bar-fill");
        if (_playerHealthFillVisual == null || _playerStaminaFillVisual == null)
        {
            if (!_missingViewLogged)
            {
                Debug.LogError("CompactHudPlayerPresenter requires bar-fill children for the player bars.", this);
                _missingViewLogged = true;
            }

            return false;
        }

        _playerHud.style.opacity = 0f;
        if (_playerHud.panel == null || float.IsNaN(_playerHud.resolvedStyle.width) ||
            _playerHud.resolvedStyle.width <= 0f)
        {
            return false;
        }

        _healthBar = new ToolkitHudBar();
        _healthBar.Bind(root.Q<VisualElement>("playerHealthBar"), _playerHealthFill, _playerHealthChip);
        _staminaBar = new ToolkitHudBar();
        _staminaBar.Bind(root.Q<VisualElement>("playerStaminaBar"), _playerStaminaFill);

        if (_styledRoot != root)
        {
            _restingHealthColor = _playerHealthFillVisual.resolvedStyle.unityBackgroundImageTintColor;
            _healthColor = _restingHealthColor;
            _restingStaminaColor = _playerStaminaFillVisual.resolvedStyle.unityBackgroundImageTintColor;
            _staminaColor = _restingStaminaColor;
            _restingHealingIconColor = _healingIcon.resolvedStyle.unityBackgroundImageTintColor;
            _healingIconColor = _restingHealingIconColor;
            _restingHealingCountColor = _healingCount.resolvedStyle.color;
            _styledRoot = root;
        }
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

        if (playerHealth != null)
        {
            playerHealth.Changed += HandleHealthChanged;
            playerHealth.Damaged += HandleHealthDamaged;
        }

        if (playerStamina != null)
        {
            playerStamina.Changed += HandleStaminaChanged;
            playerStamina.SpendRejected += HandleSpendRejected;
            playerStamina.ExhaustionChanged += HandleExhaustionChanged;
        }

        if (playerHealing != null)
        {
            playerHealing.ChargesChanged += HandleChargesChanged;
            playerHealing.Used += HandleHealingUsed;
            playerHealing.AttemptStarted += HandleHealingAttemptStarted;
            playerHealing.AttemptEnded += HandleHealingAttemptEnded;
        }

        if (arenaManager != null)
        {
            arenaManager.PhaseChanged += HandlePhaseChanged;
        }

        _isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed)
        {
            return;
        }

        if (playerHealth != null)
        {
            playerHealth.Changed -= HandleHealthChanged;
            playerHealth.Damaged -= HandleHealthDamaged;
        }

        if (playerStamina != null)
        {
            playerStamina.Changed -= HandleStaminaChanged;
            playerStamina.SpendRejected -= HandleSpendRejected;
            playerStamina.ExhaustionChanged -= HandleExhaustionChanged;
        }

        if (playerHealing != null)
        {
            playerHealing.ChargesChanged -= HandleChargesChanged;
            playerHealing.Used -= HandleHealingUsed;
            playerHealing.AttemptStarted -= HandleHealingAttemptStarted;
            playerHealing.AttemptEnded -= HandleHealingAttemptEnded;
        }

        if (arenaManager != null)
        {
            arenaManager.PhaseChanged -= HandlePhaseChanged;
        }

        _isSubscribed = false;
    }

    private void AdoptCurrentState()
    {
        if (playerHealth == null || playerStamina == null || playerHealing == null)
        {
            return;
        }

        _punchActive = false;
        _healingFeedbackActive = false;
        _healingFeedbackColorActive = false;
        _staminaColorTransitioning = false;
        _staminaRejectionRemaining = 0f;
        _healingItem.style.scale = new Scale(Vector3.one);
        _healthBar.SetImmediate(GetHealthRatio());
        _healthTargetRatio = GetHealthRatio();
        _staminaDisplayedRatio = GetStaminaRatio();
        _staminaStartRatio = _staminaDisplayedRatio;
        _staminaTargetRatio = _staminaDisplayedRatio;
        _staminaTweenElapsed = fillTweenDuration;
        _staminaBar.SetImmediate(_staminaDisplayedRatio);

        bool healthInDanger = IsDangerRatio(_healthTargetRatio);
        _healthPulseActive = healthInDanger;
        _healthColorTransitioning = false;
        _healthColorExiting = false;
        _healthPulseElapsed = 0f;
        _healthColor = healthInDanger ? dangerColor : _restingHealthColor;
        ApplyHealthColor();
        _healthBar.Container.style.scale = new Scale(Vector3.one);

        _staminaColor = playerStamina.IsExhausted ? exhaustedColor : _restingStaminaColor;
        ApplyStaminaColor();
        _playerStaminaFillVisual.style.scale = new Scale(Vector3.one);

        UpdateHealingCount(playerHealing.CurrentCharges);
        UpdateHealingState();
        _healingKey.text = string.IsNullOrWhiteSpace(healKeyHint) ? "R" : healKeyHint.Trim().ToUpperInvariant();

        _opacity = CanAutoHide() ? 0f : 1f;
        _opacityTarget = _opacity;
        _opacityTransitioning = false;
        _opacityDelay = 0f;
        _playerHud.style.opacity = _opacity;
    }

    private void HandlePhaseChanged(EncounterPhase phase)
    {
        RefreshVisibility();
    }

    private void HandleHealthChanged(float current, float maximum)
    {
        _healthTargetRatio = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
        _healthBar.TrackTo(_healthTargetRatio, chipHoldDuration, chipDrainDuration);
        RefreshVisibility();

        bool nowInDanger = IsDangerRatio(_healthTargetRatio);
        if (nowInDanger != IsHealthInDanger())
        {
            SetHealthDanger(nowInDanger);
        }

        if (showDebugLogs)
        {
            Debug.Log($"[CompactHudPlayerPresenter] Health refreshed: {current}/{maximum} ({_healthTargetRatio:P0}).", this);
        }
    }

    private void HandleHealthDamaged(DamageInfo info)
    {
        if (IsDangerRatio(GetHealthRatio()))
        {
            _punchActive = punchDuration > 0f;
            _punchElapsed = 0f;
            if (!_punchActive)
            {
                _healthBar.Container.style.scale = new Scale(Vector3.one);
            }
        }
    }

    private void HandleStaminaChanged(float current, float maximum)
    {
        _staminaTargetRatio = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
        _staminaStartRatio = _staminaDisplayedRatio;
        _staminaTweenElapsed = 0f;
        RefreshVisibility();
    }

    private void HandleSpendRejected(StaminaAction action)
    {
        Show();
        _staminaColor = rejectedColor;
        ApplyStaminaColor();
        _staminaColorTransitioning = false;
        _opacityDelay = CanAutoHide() ? Mathf.Max(fullHoldDuration, rejectionFlashDuration) : 0f;
        if (CanAutoHide())
        {
            _opacity = 1f;
            _playerHud.style.opacity = 1f;
            ScheduleAutoHide(_opacityDelay);
        }

        _staminaRejectionRemaining = rejectionFlashDuration;
        if (rejectionFlashDuration <= 0f)
        {
            HandleExhaustionChanged(playerStamina.IsExhausted);
        }
        if (showDebugLogs)
        {
            Debug.Log($"[CompactHudPlayerPresenter] Flashed after rejected {action}.", this);
        }
    }

    private void HandleExhaustionChanged(bool exhausted)
    {
        _staminaRejectionRemaining = 0f;
        _staminaColorTarget = exhausted ? exhaustedColor : _restingStaminaColor;
        _staminaColorStart = _staminaColor;
        _staminaColorElapsed = 0f;
        _staminaColorDuration = fadeDuration;
        _staminaColorTransitioning = true;
        RefreshVisibility();
    }

    private void HandleChargesChanged(int current, int maximum)
    {
        UpdateHealingCount(current);
        ApplyHealingRestingState(current);
    }

    private void HandleHealingUsed()
    {
        _healingFeedbackActive = true;
        _healingFeedbackElapsed = 0f;
        _healingFeedbackColorActive = true;
        _healingFeedbackColorElapsed = 0f;
        _healingIconColor = new Color32(255, 190, 85, 255);
        ApplyHealingIconColor();
        if (showDebugLogs)
        {
            Debug.Log("[CompactHudPlayerPresenter] Played committed heal feedback.", this);
        }
    }

    private void HandleHealingAttemptStarted()
    {
        _healingItem.AddToClassList("is-healing");
        _healingProgressTrack.style.display = DisplayStyle.Flex;
        _healingProgress.style.width = Length.Percent(playerHealing.AttemptProgress * 100f);
    }

    private void HandleHealingAttemptEnded(bool completed)
    {
        UpdateHealingState();
    }

    private void TickHealthColor(float unscaledDeltaTime)
    {
        if (_healthColorTransitioning)
        {
            _healthColorElapsed += unscaledDeltaTime;
            float normalized = _healthColorDuration > 0f
                ? Mathf.Clamp01(_healthColorElapsed / _healthColorDuration)
                : 1f;
            _healthColor = Color.Lerp(_healthColorStart, _healthColorExiting ? _restingHealthColor : dangerColor, 1f - (1f - normalized) * (1f - normalized));
            ApplyHealthColor();
            if (normalized >= 1f)
            {
                _healthColorTransitioning = false;
                _healthPulseActive = !_healthColorExiting;
                _healthPulseElapsed = 0f;
            }

            return;
        }

        if (!_healthPulseActive)
        {
            return;
        }

        _healthPulseElapsed += unscaledDeltaTime;
        float halfPeriod = Mathf.Max(0.01f, pulseHalfPeriod);
        float cycle = halfPeriod * 2f;
        float phase = _healthPulseElapsed % cycle;
        float normalizedPulse = phase <= halfPeriod ? phase / halfPeriod : 2f - phase / halfPeriod;
        _healthColor = Color.Lerp(dangerColor, dangerPulseColor, (1f - Mathf.Cos(normalizedPulse * Mathf.PI)) * 0.5f);
        ApplyHealthColor();
    }

    private void TickStamina(float unscaledDeltaTime)
    {
        if (_staminaDisplayedRatio != _staminaTargetRatio)
        {
            _staminaTweenElapsed += unscaledDeltaTime;
            float normalized = fillTweenDuration > 0f
                ? Mathf.Clamp01(_staminaTweenElapsed / fillTweenDuration)
                : 1f;
            float eased = 1f - (1f - normalized) * (1f - normalized);
            _staminaDisplayedRatio = Mathf.Lerp(_staminaStartRatio, _staminaTargetRatio, eased);
            _staminaBar.SetImmediate(_staminaDisplayedRatio);
        }

        if (_staminaColorTransitioning)
        {
            _staminaColorElapsed += unscaledDeltaTime;
            float normalized = _staminaColorDuration > 0f
                ? Mathf.Clamp01(_staminaColorElapsed / _staminaColorDuration)
                : 1f;
            _staminaColor = Color.Lerp(_staminaColorStart, _staminaColorTarget, normalized);
            ApplyStaminaColor();
            if (normalized >= 1f)
            {
                _staminaColorTransitioning = false;
            }
        }
        else if (_staminaRejectionRemaining > 0f)
        {
            _staminaRejectionRemaining -= unscaledDeltaTime;
            if (_staminaRejectionRemaining <= 0f)
            {
                _staminaColorTarget = playerStamina.IsExhausted ? exhaustedColor : _restingStaminaColor;
                _staminaColorStart = _staminaColor;
                _staminaColorElapsed = 0f;
                _staminaColorDuration = fadeDuration;
                _staminaColorTransitioning = true;
            }
        }
    }

    private void TickPunch(float unscaledDeltaTime)
    {
        if (!_punchActive)
        {
            return;
        }

        _punchElapsed += unscaledDeltaTime;
        float normalized = punchDuration > 0f ? Mathf.Clamp01(_punchElapsed / punchDuration) : 1f;
        float scale = 1f + punchScale * Mathf.Sin(normalized * Mathf.PI * 4f) * (1f - normalized);
        _healthBar.Container.style.scale = new Scale(Vector3.one * scale);
        if (normalized >= 1f)
        {
            _punchActive = false;
            _healthBar.Container.style.scale = new Scale(Vector3.one);
        }
    }

    private void TickHealing(float unscaledDeltaTime)
    {
        if (playerHealing.IsAttempting)
        {
            _healingItem.AddToClassList("is-healing");
            _healingProgressTrack.style.display = DisplayStyle.Flex;
            _healingProgress.style.width = Length.Percent(playerHealing.AttemptProgress * 100f);
        }

        if (_healingFeedbackActive)
        {
            _healingFeedbackElapsed += unscaledDeltaTime;
            float normalized = Mathf.Clamp01(_healingFeedbackElapsed / 0.20f);
            float scale = normalized < 0.35f
                ? Mathf.Lerp(1f, 1.1f, normalized / 0.35f)
                : Mathf.Lerp(1.1f, 1f, (normalized - 0.35f) / 0.65f);
            _healingItem.style.scale = new Scale(Vector3.one * scale);
            if (normalized >= 1f)
            {
                _healingFeedbackActive = false;
                _healingItem.style.scale = new Scale(Vector3.one);
            }
        }

        if (!_healingFeedbackColorActive)
        {
            return;
        }

        _healingFeedbackColorElapsed += unscaledDeltaTime;
        float colorNormalized = Mathf.Clamp01(_healingFeedbackColorElapsed / 0.20f);
        Color target = playerHealing.CurrentCharges <= 0 ? new Color32(90, 90, 90, 170) : _restingHealingIconColor;
        _healingIconColor = Color.Lerp(new Color32(255, 190, 85, 255), target, colorNormalized);
        ApplyHealingIconColor();
        if (colorNormalized >= 1f)
        {
            _healingFeedbackColorActive = false;
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
        _playerHud.style.opacity = _opacity;
        if (normalized >= 1f)
        {
            _opacityTransitioning = false;
        }
    }

    private void SetHealthDanger(bool dangerous)
    {
        if (!dangerous)
        {
            _punchActive = false;
            _healthBar.Container.style.scale = new Scale(Vector3.one);
        }
        _healthPulseActive = false;
        _healthColorTransitioning = true;
        _healthColorExiting = !dangerous;
        _healthColorStart = _healthColor;
        _healthColorElapsed = 0f;
        _healthColorDuration = dangerTransitionDuration;
        if (dangerTransitionDuration <= 0f)
        {
            _healthColor = dangerous ? dangerColor : _restingHealthColor;
            _healthColorTransitioning = false;
            _healthPulseActive = dangerous;
            _healthPulseElapsed = 0f;
            ApplyHealthColor();
        }
    }

    private void RefreshVisibility()
    {
        if (CanAutoHide())
        {
            ScheduleAutoHide(0f);
        }
        else
        {
            Show();
        }
    }

    private void Show()
    {
        SetOpacityTarget(1f, fadeDuration, 0f);
    }

    private void ScheduleAutoHide(float delay)
    {
        SetOpacityTarget(0f, fadeDuration, Mathf.Max(fullHoldDuration, delay));
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
            _playerHud.style.opacity = _opacity;
            _opacityTransitioning = false;
        }
    }

    private void UpdateHealingCount(int current)
    {
        _healingCount.text = Mathf.Max(0, current).ToString();
    }

    private void UpdateHealingState()
    {
        int current = playerHealing.CurrentCharges;
        UpdateHealingCount(current);
        ApplyHealingRestingState(current);
        if (playerHealing.IsAttempting)
        {
            _healingItem.AddToClassList("is-healing");
            _healingProgressTrack.style.display = DisplayStyle.Flex;
            _healingProgress.style.width = Length.Percent(playerHealing.AttemptProgress * 100f);
        }
        else
        {
            _healingItem.RemoveFromClassList("is-healing");
            _healingProgress.style.width = Length.Percent(0f);
            _healingProgressTrack.style.display = DisplayStyle.None;
        }
    }

    private void ApplyHealingRestingState(int current)
    {
        bool empty = current <= 0;
        if (empty)
        {
            _healingItem.AddToClassList("is-empty");
            _healingCount.style.color = (Color)new Color32(170, 45, 35, 255);
            _healingIconColor = new Color32(90, 90, 90, 170);
        }
        else
        {
            _healingItem.RemoveFromClassList("is-empty");
            _healingCount.style.color = _restingHealingCountColor;
            _healingIconColor = _restingHealingIconColor;
        }

        ApplyHealingIconColor();
    }

    private void ApplyHealthColor()
    {
        _playerHealthFillVisual.style.unityBackgroundImageTintColor = _healthColor;
    }

    private void ApplyStaminaColor()
    {
        _playerStaminaFillVisual.style.unityBackgroundImageTintColor = _staminaColor;
    }

    private void ApplyHealingIconColor()
    {
        _healingIcon.style.unityBackgroundImageTintColor = _healingIconColor;
    }

    private bool CanAutoHide()
    {
        return !arenaManager.IsActive &&
               playerHealth.CurrentHealth >= playerHealth.MaxHealth &&
               playerStamina.NormalizedStamina >= 1f &&
               !playerStamina.IsExhausted;
    }

    private float GetHealthRatio()
    {
        return playerHealth.MaxHealth > 0f
            ? Mathf.Clamp01(playerHealth.CurrentHealth / playerHealth.MaxHealth)
            : 0f;
    }

    private float GetStaminaRatio()
    {
        return Mathf.Clamp01(playerStamina.NormalizedStamina);
    }

    private bool IsDangerRatio(float ratio)
    {
        return ratio > 0f && ratio <= dangerThreshold;
    }

    private bool IsHealthInDanger()
    {
        return _healthPulseActive || (_healthColorTransitioning && !_healthColorExiting);
    }

    private void ResetAnimatedStyles()
    {
        if (!_isBound)
        {
            return;
        }

        _playerHud.style.opacity = 0f;
        _healthBar.Container.style.scale = new Scale(Vector3.one);
        _healingItem.style.scale = new Scale(Vector3.one);
        _healingProgress.style.width = Length.Percent(0f);
        _healingProgressTrack.style.display = DisplayStyle.None;
        _playerHealthFillVisual.style.unityBackgroundImageTintColor = StyleKeyword.Null;
        _playerStaminaFillVisual.style.unityBackgroundImageTintColor = StyleKeyword.Null;
        _healingIcon.style.unityBackgroundImageTintColor = StyleKeyword.Null;
        _healingCount.style.color = StyleKeyword.Null;
    }

}
