using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Presentation-only death card: dims the screen and fades in the title after the
/// player dies, blacks out before the respawn teleport, then fades back in once the
/// player has respawned. Observes the player and never drives the respawn itself.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class PlayerDeathScreen : MonoBehaviour
{
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private PlayerStateMachine player;

    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    [Tooltip("Unscaled seconds after death before the card starts to appear, so the killing blow and the fall read first.")]
    private float showDelay = 0.8f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    [Tooltip("Unscaled seconds for the title and dim to fade in.")]
    private float showDuration = 1.2f;
    [FoldoutGroup("Timing"), SerializeField, Range(0f, 1f)]
    [Tooltip("Screen dim behind the title while it is shown.")]
    private float dimOpacity = 0.5f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    [Tooltip("Unscaled seconds after death when the screen starts fading to black. Must finish before the player respawns (death clip + respawn delay).")]
    private float blackoutDelay = 3.8f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float blackoutDuration = 0.6f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    [Tooltip("Unscaled seconds to fade back in after the respawn.")]
    private float revealDuration = 1.2f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    public bool IsShowing { get; private set; }

    private Health _playerHealth;
    private VisualElement _overlay;
    private VisualElement _shade;
    private VisualElement _card;
    private Sequence _sequence;

    // Resolved directly: PlayerStateMachine assigns its Health property in its own Awake,
    // which can run after this component is enabled.
    private void Awake()
    {
        _playerHealth = player != null ? player.GetComponent<Health>() : null;
    }

    private void OnEnable()
    {
        if (player == null || _playerHealth == null)
        {
            return;
        }

        _playerHealth.Died += HandleDied;
        player.Respawned += HandleRespawned;
    }

    private void Start()
    {
        if (!TryResolveElements())
        {
            Debug.LogError("[PlayerDeathScreen] The UIDocument is missing deathOverlay, deathShade or deathCard.", this);
            return;
        }

        HideImmediately();
    }

    private void OnDisable()
    {
        if (player != null && _playerHealth != null)
        {
            _playerHealth.Died -= HandleDied;
            player.Respawned -= HandleRespawned;
        }

        if (TryResolveElements())
        {
            HideImmediately();
        }
    }

    private void HandleDied()
    {
        if (!TryResolveElements())
        {
            return;
        }

        StartSequence();
        IsShowing = true;
        _overlay.style.display = DisplayStyle.Flex;
        _sequence
            .AppendInterval(showDelay)
            .Append(Fade(_shade, dimOpacity, showDuration))
            .Join(Fade(_card, 1f, showDuration))
            .Insert(blackoutDelay, Fade(_shade, 1f, blackoutDuration));
        Log("Shown.");
    }

    private void HandleRespawned()
    {
        if (!IsShowing || !TryResolveElements())
        {
            return;
        }

        StartSequence();
        _sequence
            .Append(Fade(_card, 0f, 0.3f))
            .Join(Fade(_shade, 1f, 0.3f))
            .Append(Fade(_shade, 0f, revealDuration))
            .OnComplete(HideImmediately);
        Log("Revealing.");
    }

    // Queried on use because the UIDocument rebuilds its tree whenever it is re-enabled.
    private bool TryResolveElements()
    {
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;
        _overlay = root?.Q("deathOverlay");
        _shade = root?.Q("deathShade");
        _card = root?.Q("deathCard");
        return _overlay != null && _shade != null && _card != null;
    }

    // Unscaled: hit-stop and pause can hold Time.timeScale at 0 while the card plays.
    private void StartSequence()
    {
        _sequence?.Kill();
        _sequence = DOTween.Sequence().SetUpdate(true).SetTarget(this);
    }

    private static Tween Fade(VisualElement element, float target, float duration)
    {
        return DOTween.To(() => element.resolvedStyle.opacity, value => element.style.opacity = value, target, duration);
    }

    private void HideImmediately()
    {
        _sequence?.Kill();
        _sequence = null;
        IsShowing = false;
        _shade.style.opacity = 0f;
        _card.style.opacity = 0f;
        _overlay.style.display = DisplayStyle.None;
    }

    private void Log(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[PlayerDeathScreen] {message}", this);
        }
    }
}
