using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Presentation-only title card that names the boss. Something else decides
/// when to show it; once shown it fades in, holds and fades out by itself.
/// </summary>
public class BossNameCard : MonoBehaviour
{
    [FoldoutGroup("View"), SerializeField, Required, SceneObjectsOnly]
    private UIDocument uiDocument;

    [FoldoutGroup("Timing"), SerializeField, MinValue(0f), SuffixLabel("s")]
    private float fadeInDuration = 0.8f;
    [Tooltip("Real seconds the card stays fully visible before it fades out.")]
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f), SuffixLabel("s")]
    private float holdDuration = 2f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f), SuffixLabel("s")]
    private float fadeOutDuration = 1f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool IsVisible { get; private set; }

    private Tween _tween;
    private VisualElement _cardRoot;
    private float _opacity;

    private void Awake()
    {
        SetOpacity(0f);
    }

    private void OnDisable()
    {
        _tween?.Kill();
        SetOpacity(0f);
        IsVisible = false;
    }

    public void Show()
    {
        _tween?.Kill();
        IsVisible = true;
        // Unscaled time, like the other screen cards, so hit-stop and slow motion do not stretch it.
        _tween = DOTween.Sequence()
            .Append(FadeTo(1f, fadeInDuration))
            .AppendInterval(holdDuration)
            .Append(FadeTo(0f, fadeOutDuration))
            .OnComplete(() => IsVisible = false)
            .SetUpdate(true);
        if (showDebugLogs) Debug.Log("[BossNameCard] Shown.", this);
    }

    public void Hide()
    {
        if (!IsVisible) return;

        _tween?.Kill();
        _tween = FadeTo(0f, fadeOutDuration).OnComplete(() => IsVisible = false).SetUpdate(true);
        if (showDebugLogs) Debug.Log("[BossNameCard] Hidden.", this);
    }

    private Tween FadeTo(float target, float duration)
    {
        return DOTween.To(() => _opacity, SetOpacity, target, duration);
    }

    private void SetOpacity(float opacity)
    {
        _opacity = opacity;
        // The document's tree is rebuilt when it is re-enabled, so re-query until the card exists.
        if (_cardRoot == null || _cardRoot.panel == null)
        {
            _cardRoot = uiDocument.rootVisualElement?.Q("bossNameCard");
        }
        if (_cardRoot != null)
        {
            _cardRoot.style.opacity = opacity;
            _cardRoot.style.display = opacity > 0f ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
