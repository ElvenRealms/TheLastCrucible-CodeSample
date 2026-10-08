using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Presents the title backdrop as a live scene: keeps the still image covering the screen at its
/// own aspect ratio, drifts it slowly and flickers the firelight glows laid over it.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class TitleScreenBackdrop : MonoBehaviour
{
    private const string GlowClass = "title-glow";

    [FoldoutGroup("Backdrop"), SerializeField, MinValue(0.1f)]
    [Tooltip("Width divided by height of the backdrop image. The glows are placed against this frame.")]
    private float aspectRatio = 16f / 9f;

    [FoldoutGroup("Drift"), SerializeField, Range(0f, 0.1f)]
    [Tooltip("How far the backdrop zooms and pans, as a fraction of its size. Zero holds it still.")]
    private float driftAmount = 0.025f;
    [FoldoutGroup("Drift"), SerializeField, MinValue(1f)]
    [Tooltip("Unscaled seconds for one full zoom in and out.")]
    private float driftPeriod = 60f;

    [FoldoutGroup("Firelight"), SerializeField, Range(0f, 1f)]
    [Tooltip("How much of each glow's strength the flicker can take away.")]
    private float flickerDepth = 0.45f;
    [FoldoutGroup("Firelight"), SerializeField, MinValue(0f)]
    [Tooltip("How fast the firelight wavers.")]
    private float flickerSpeed = 2.4f;

    private readonly List<VisualElement> _glows = new();
    private VisualElement _screen;
    private VisualElement _stage;

    private void OnEnable()
    {
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;
        _screen = root.Q("titleScreen");
        _stage = root.Q("stage");
        if (_screen == null || _stage == null)
        {
            Debug.LogError("[TitleScreenBackdrop] The UIDocument is missing titleScreen or stage.", this);
            enabled = false;
            return;
        }

        _stage.Query(className: GlowClass).ToList(_glows);
        _stage.usageHints = UsageHints.DynamicTransform;
        _screen.RegisterCallback<GeometryChangedEvent>(HandleScreenResized);
        FitStage();
    }

    private void OnDisable()
    {
        _screen?.UnregisterCallback<GeometryChangedEvent>(HandleScreenResized);
        _glows.Clear();
    }

    private void Update()
    {
        Drift();
        Flicker();
    }

    private void HandleScreenResized(GeometryChangedEvent evt)
    {
        FitStage();
    }

    private void FitStage()
    {
        Rect screen = _screen.contentRect;
        if (float.IsNaN(screen.width) || screen.width < 1f || screen.height < 1f)
        {
            return;
        }

        float width = Mathf.Max(screen.width, screen.height * aspectRatio);
        float height = width / aspectRatio;
        _stage.style.width = width;
        _stage.style.height = height;
        _stage.style.left = (screen.width - width) * 0.5f;
        _stage.style.top = (screen.height - height) * 0.5f;
    }

    // The zoom never drops below the pan's reach, so the drift cannot expose the stage's edge.
    private void Drift()
    {
        float phase = Time.unscaledTime * Mathf.PI * 2f / driftPeriod;
        float zoom = driftAmount * (1.5f + 0.5f * Mathf.Sin(phase));
        float pan = driftAmount * 0.5f * Mathf.Sin(phase * 0.6f);
        _stage.style.scale = new Scale(Vector2.one * (1f + zoom));
        _stage.style.translate = new Translate(Length.Percent(pan * 100f), 0f);
    }

    private void Flicker()
    {
        float time = Time.unscaledTime * flickerSpeed;
        for (int i = 0; i < _glows.Count; i++)
        {
            float noise = Mathf.PerlinNoise(time, i * 7.31f);
            _glows[i].style.opacity = 1f - flickerDepth * noise;
        }
    }
}
