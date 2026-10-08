using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Drives a UI Toolkit bar whose fill is represented by a clipped child. An
/// optional chip clip trails the front fill after damage.
/// </summary>
public sealed class ToolkitHudBar
{
    private VisualElement _frontClip;
    private VisualElement _chipClip;
    private float _targetRatio;
    private float _chipRatio;
    private Tween _chipTween;

    public VisualElement Container { get; private set; }

    public void Bind(VisualElement container, VisualElement front, VisualElement chip = null)
    {
        Container = container;
        _frontClip = front;
        _chipClip = chip;
    }

    /// <summary>Sets both the front and chip edges without playing a transition.</summary>
    public void SetImmediate(float ratio)
    {
        ratio = SanitizeRatio(ratio);
        _chipTween?.Kill();
        _chipTween = null;
        _targetRatio = ratio;
        _chipRatio = ratio;
        SetFrontWidth(ratio);
        SetChipWidth(_chipRatio);
    }

    /// <summary>
    /// Snaps the front edge to a new ratio and lets a lower chip edge catch up
    /// after the supplied hold and drain durations.
    /// </summary>
    public void TrackTo(float ratio, float holdDuration, float drainDuration)
    {
        ratio = SanitizeRatio(ratio);
        SetFrontWidth(ratio);

        if (_chipClip == null)
        {
            _targetRatio = ratio;
            return;
        }

        if (Mathf.Approximately(ratio, _targetRatio))
        {
            return;
        }

        if (ratio > _targetRatio)
        {
            SetImmediate(ratio);
            return;
        }

        _targetRatio = ratio;
        _chipTween?.Kill();
        _chipTween = DOTween.To(
                () => _chipRatio,
                value =>
                {
                    _chipRatio = value;
                    SetChipWidth(value);
                },
                ratio,
                Mathf.Max(0f, drainDuration))
            .SetDelay(Mathf.Max(0f, holdDuration))
            .SetEase(Ease.InQuad)
            .SetUpdate(true)
            .SetTarget(this);
    }

    public void Stop()
    {
        _chipTween?.Kill();
        _chipTween = null;
    }

    private void SetFrontWidth(float ratio)
    {
        if (_frontClip != null)
        {
            _frontClip.style.width = Length.Percent(ratio * 100f);
        }
    }

    private void SetChipWidth(float ratio)
    {
        if (_chipClip != null)
        {
            _chipClip.style.width = Length.Percent(ratio * 100f);
        }
    }

    private static float SanitizeRatio(float ratio)
    {
        if (float.IsNaN(ratio) || float.IsInfinity(ratio))
        {
            return 0f;
        }

        return Mathf.Clamp01(ratio);
    }
}
