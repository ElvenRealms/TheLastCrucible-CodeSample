using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Presentation-only confirmation of an Encounter Victory. Something else decides
/// when to show it; this component only knows how to fade itself in and out.
/// </summary>
public class VictoryBanner : MonoBehaviour
{
    [FoldoutGroup("View"), SerializeField, Required, SceneObjectsOnly]
    private UIDocument uiDocument;

    [Tooltip("Duration of the fade-in when the banner is shown.")]
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float fadeInDuration = 0.3f;

    [Tooltip("Duration of the fade-out when the banner is hidden.")]
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float fadeOutDuration = 0.75f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;

    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool IsVisible { get; private set; }

    private Tween _fadeTween;
    private VisualElement _bannerRoot;
    private float _opacity;

    private void Awake()
    {
        SetOpacity(0f);
    }

    private void OnDisable()
    {
        _fadeTween?.Kill();
        SetOpacity(0f);
        IsVisible = false;
    }

    public void Show()
    {
        _fadeTween?.Kill();
        IsVisible = true;
        _fadeTween = FadeTo(1f, fadeInDuration);
        if (showDebugLogs) Debug.Log("[VictoryBanner] Shown.", this);
    }

    public void Hide()
    {
        _fadeTween?.Kill();
        _fadeTween = FadeTo(0f, fadeOutDuration).OnComplete(() => IsVisible = false);
        if (showDebugLogs) Debug.Log("[VictoryBanner] Hidden.", this);
    }

    /// <summary>Fills the stat row under the title. Call before <see cref="Show"/>.</summary>
    public void SetStats(FightStats stats)
    {
        VisualElement root = uiDocument.rootVisualElement;
        if (root == null) return;

        int seconds = Mathf.FloorToInt(stats.duration);
        SetStat(root, "statTime", $"{seconds / 60}:{seconds % 60:00}");
        SetStat(root, "statAttempts", stats.attempts.ToString());
        SetStat(root, "statHitsTaken", stats.hitsTaken.ToString());
        SetStat(root, "statParries", stats.parries.ToString());
        SetStat(root, "statDodges", stats.dodges.ToString());
        SetStat(root, "statHeals", stats.healsUsed.ToString());
    }

    private static void SetStat(VisualElement root, string labelName, string value)
    {
        Label label = root.Q<Label>(labelName);
        if (label != null) label.text = value;
    }

    // Unscaled time: the Victory Sequence can be running its slow-motion beat while this fades.
    private Tween FadeTo(float target, float duration)
    {
        return DOTween.To(() => _opacity, SetOpacity, target, duration).SetUpdate(true);
    }

    private void SetOpacity(float opacity)
    {
        _opacity = opacity;
        // The document's tree is rebuilt when it is re-enabled, so re-query until the overlay exists.
        if (_bannerRoot == null || _bannerRoot.panel == null)
        {
            _bannerRoot = uiDocument.rootVisualElement?.Q("victoryOverlay");
        }
        if (_bannerRoot != null)
        {
            _bannerRoot.style.opacity = opacity;
            _bannerRoot.style.display = opacity > 0f ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
