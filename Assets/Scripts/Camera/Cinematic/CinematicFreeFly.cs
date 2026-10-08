using System;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Hand-flown cinematic camera with smoothed movement, look and zoom, so recorded moves stay steady.
/// Runs on real time, so it flies normally while game time is slowed or frozen.
/// </summary>
[Serializable]
public class CinematicFreeFly
{
    private const float MaxPitch = 85f;
    private const float FocusProbeDistance = 60f;
    private const float FocusFallbackDistance = 10f;

    [SerializeField, MinValue(0.1f), SuffixLabel("m/s")]
    private float moveSpeed = 3f;
    [SerializeField, MinValue(1f)]
    [Tooltip("Speed multiplier while Fast is held.")]
    private float fastMultiplier = 4f;
    [SerializeField, Range(0.05f, 1f)]
    [Tooltip("Speed multiplier while Slow is held.")]
    private float slowMultiplier = 0.25f;
    [SerializeField, MinValue(0f), SuffixLabel("s")]
    private float moveSmoothing = 0.25f;
    [SerializeField, MinValue(0.001f), SuffixLabel("deg/px")]
    private float lookSensitivity = 0.08f;
    [SerializeField, MinValue(0f), SuffixLabel("s")]
    private float lookSmoothing = 0.08f;
    [SerializeField, MinValue(0f), SuffixLabel("deg/notch")]
    [Tooltip("Field of view change per scroll-wheel notch.")]
    private float zoomStep = 2.5f;
    [SerializeField, MinMaxSlider(5f, 100f, true)]
    private Vector2 fieldOfViewRange = new Vector2(15f, 80f);
    [SerializeField]
    [Tooltip("Surfaces the depth-of-field focus probe can land on.")]
    private LayerMask focusLayers = ~0;

    private Vector3 _position;
    private Vector3 _velocity;
    private float _yaw;
    private float _pitch;
    private float _targetYaw;
    private float _targetPitch;
    private float _yawVelocity;
    private float _pitchVelocity;
    private float _fieldOfView;
    private float _targetFieldOfView;
    private float _fieldOfViewVelocity;

    /// <summary>
    /// Takes over from the given camera pose, so switching to free flight doesn't jump.
    /// </summary>
    public void Begin(Vector3 position, Quaternion rotation, float fieldOfView)
    {
        Vector3 euler = rotation.eulerAngles;
        _position = position;
        _velocity = Vector3.zero;
        _yaw = _targetYaw = euler.y;
        _pitch = _targetPitch = Mathf.Clamp(Mathf.DeltaAngle(0f, euler.x), -MaxPitch, MaxPitch);
        _yawVelocity = 0f;
        _pitchVelocity = 0f;
        _fieldOfView = _targetFieldOfView = Mathf.Clamp(fieldOfView, fieldOfViewRange.x, fieldOfViewRange.y);
        _fieldOfViewVelocity = 0f;
    }

    public CinematicCameraPose Tick(in CinematicFlyInput input, float deltaTime)
    {
        _targetYaw += input.Look.x * lookSensitivity;
        _targetPitch = Mathf.Clamp(_targetPitch - input.Look.y * lookSensitivity, -MaxPitch, MaxPitch);
        _targetFieldOfView = Mathf.Clamp(_targetFieldOfView - input.ZoomNotches * zoomStep,
            fieldOfViewRange.x, fieldOfViewRange.y);

        _yaw = Mathf.SmoothDampAngle(_yaw, _targetYaw, ref _yawVelocity, lookSmoothing, Mathf.Infinity, deltaTime);
        _pitch = Mathf.SmoothDamp(_pitch, _targetPitch, ref _pitchVelocity, lookSmoothing, Mathf.Infinity, deltaTime);
        _fieldOfView = Mathf.SmoothDamp(_fieldOfView, _targetFieldOfView, ref _fieldOfViewVelocity, lookSmoothing,
            Mathf.Infinity, deltaTime);
        Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);

        Vector3 move = rotation * new Vector3(input.Move.x, 0f, input.Move.y) + Vector3.up * input.Vertical;
        float speed = moveSpeed * (input.Fast ? fastMultiplier : 1f) * (input.Slow ? slowMultiplier : 1f);
        Vector3 targetVelocity = Vector3.ClampMagnitude(move, 1f) * speed;
        _velocity = moveSmoothing > 0f
            ? Vector3.Lerp(_velocity, targetVelocity, 1f - Mathf.Exp(-deltaTime / moveSmoothing))
            : targetVelocity;
        _position += _velocity * deltaTime;

        return new CinematicCameraPose(_position, rotation, _fieldOfView, ProbeFocus(rotation));
    }

    private float ProbeFocus(Quaternion rotation)
    {
        return Physics.Raycast(_position, rotation * Vector3.forward, out RaycastHit hit, FocusProbeDistance,
            focusLayers, QueryTriggerInteraction.Ignore)
            ? hit.distance
            : FocusFallbackDistance;
    }
}

/// <summary>
/// One frame of free-camera controls. Look is in pixels, zoom in scroll-wheel notches.
/// </summary>
public readonly struct CinematicFlyInput
{
    public CinematicFlyInput(Vector2 move, float vertical, Vector2 look, float zoomNotches, bool fast, bool slow)
    {
        Move = move;
        Vertical = vertical;
        Look = look;
        ZoomNotches = zoomNotches;
        Fast = fast;
        Slow = slow;
    }

    public Vector2 Move { get; }
    public float Vertical { get; }
    public Vector2 Look { get; }
    public float ZoomNotches { get; }
    public bool Fast { get; }
    public bool Slow { get; }
}
