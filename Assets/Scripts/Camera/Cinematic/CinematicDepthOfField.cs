using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Depth of field for the cinematic camera. Blends a Volume holding a Depth Of Field override in and
/// out, and eases its focus distance toward whatever the camera is framing. Lens settings such as
/// aperture and focal length live on the Volume's profile.
/// </summary>
[RequireComponent(typeof(Volume))]
public class CinematicDepthOfField : MonoBehaviour
{
    [FoldoutGroup("Config"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Real-time smoothing on focus pulls.")]
    private float focusSmoothing = 0.15f;
    [FoldoutGroup("Config"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Real seconds to blend the effect in or out.")]
    private float fadeDuration = 0.3f;

    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private bool isOn;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private float focusDistance = 10f;

    private Volume _volume;
    private DepthOfField _depthOfField;
    private float _targetFocusDistance = 10f;
    private float _focusVelocity;

    public bool IsOn => isOn;

    private void Awake()
    {
        _volume = GetComponent<Volume>();
        _volume.weight = 0f;
        // Reading Volume.profile clones the shared asset, so focus pulls never write to it.
        if (_volume.profile == null || !_volume.profile.TryGet(out _depthOfField))
        {
            Debug.LogError("[CinematicDepthOfField] The Volume profile needs a Depth Of Field override.", this);
        }
    }

    private void Update()
    {
        float targetWeight = isOn ? 1f : 0f;
        _volume.weight = fadeDuration > 0f
            ? Mathf.MoveTowards(_volume.weight, targetWeight, Time.unscaledDeltaTime / fadeDuration)
            : targetWeight;
        if (_depthOfField == null || _volume.weight <= 0f)
        {
            return;
        }

        focusDistance = Mathf.SmoothDamp(focusDistance, _targetFocusDistance, ref _focusVelocity, focusSmoothing,
            Mathf.Infinity, Time.unscaledDeltaTime);
        _depthOfField.focusDistance.value = focusDistance;
    }

    public void SetOn(bool on)
    {
        if (on && !isOn)
        {
            focusDistance = _targetFocusDistance;
            _focusVelocity = 0f;
        }
        isOn = on;
    }

    public void SetFocusDistance(float distance)
    {
        _targetFocusDistance = Mathf.Max(0.1f, distance);
    }
}
