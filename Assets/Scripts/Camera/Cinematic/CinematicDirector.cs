using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Automatic camera operator for the cinematic camera. Cuts between the shots of a
/// <see cref="CinematicShotSet"/>, skipping angles that a wall would block, and follows the fight with
/// smoothed motion and a light handheld wobble. Runs on real time, so shots keep moving while game
/// time is slowed or frozen.
/// </summary>
public class CinematicDirector
{
    private const float MinAxisSeparation = 0.1f;
    private const float MaxSmoothingDelta = 0.1f;

    private readonly CinematicShotSet _shots;
    private readonly List<int> _candidates = new List<int>();
    private int _shotIndex = -1;
    private float _shotElapsed;
    private float _shotLength;
    private float _axisYaw;
    private float _axisYawVelocity;
    private Vector3 _position;
    private Vector3 _positionVelocity;
    private Vector3 _aim;
    private Vector3 _aimVelocity;
    private float _wobbleTime;

    public CinematicDirector(CinematicShotSet shots)
    {
        _shots = shots;
    }

    public CinematicShot CurrentShot => _shotIndex >= 0 ? _shots.Shots[_shotIndex] : null;
    public bool IsHolding { get; private set; }

    /// <summary>
    /// Starts directing from the current fight axis, cutting straight to a fresh shot.
    /// </summary>
    public void Begin(Vector3 player, Vector3 boss)
    {
        _axisYaw = AxisYaw(player, boss, 0f);
        _axisYawVelocity = 0f;
        Cut(player, boss);
    }

    /// <summary>
    /// Cuts to a different shot, preferring ones the camera can reach without a wall in the way.
    /// </summary>
    public void Cut(Vector3 player, Vector3 boss)
    {
        if (_shots == null || _shots.Shots.Count == 0)
        {
            return;
        }

        _shotIndex = PickShot(player, boss);
        _shotElapsed = 0f;
        _shotLength = CurrentShot.PickDuration();

        CinematicFraming framing = Clear(CurrentShot.Frame(player, boss, _axisYaw, 0f), out _);
        _position = framing.Position;
        _aim = framing.Aim;
        _positionVelocity = Vector3.zero;
        _aimVelocity = Vector3.zero;
    }

    public void SetHolding(bool holding)
    {
        IsHolding = holding;
    }

    /// <summary>
    /// Advances the current shot by <paramref name="deltaTime"/> real seconds and returns the camera
    /// pose, cutting first when the shot has run its length.
    /// </summary>
    public CinematicCameraPose Tick(Vector3 player, Vector3 boss, float deltaTime)
    {
        if (CurrentShot == null)
        {
            Begin(player, boss);
            if (CurrentShot == null)
            {
                return new CinematicCameraPose(_position, Quaternion.identity, 40f, 10f);
            }
        }

        _shotElapsed += deltaTime;
        _wobbleTime += deltaTime;
        if (!IsHolding && _shotElapsed >= _shotLength)
        {
            Cut(player, boss);
        }

        // Large frame gaps would overshoot the critically damped smoothing.
        float smoothingDelta = Mathf.Min(deltaTime, MaxSmoothingDelta);
        float targetYaw = AxisYaw(player, boss, _axisYaw);
        _axisYaw = Mathf.SmoothDampAngle(_axisYaw, targetYaw, ref _axisYawVelocity, _shots.AxisSmoothing,
            Mathf.Infinity, smoothingDelta);

        CinematicFraming framing = Clear(CurrentShot.Frame(player, boss, _axisYaw, _shotElapsed), out _);
        _position = Vector3.SmoothDamp(_position, framing.Position, ref _positionVelocity, _shots.PositionSmoothing,
            Mathf.Infinity, smoothingDelta);
        _aim = Vector3.SmoothDamp(_aim, framing.Aim, ref _aimVelocity, _shots.AimSmoothing,
            Mathf.Infinity, smoothingDelta);

        Vector3 toAim = _aim - _position;
        Quaternion rotation = toAim.sqrMagnitude > Mathf.Epsilon
            ? Quaternion.LookRotation(toAim) * Wobble(CurrentShot.Handheld)
            : Quaternion.identity;
        return new CinematicCameraPose(_position, rotation, CurrentShot.FieldOfView, toAim.magnitude);
    }

    private int PickShot(Vector3 player, Vector3 boss)
    {
        _candidates.Clear();
        for (int i = 0; i < _shots.Shots.Count; i++)
        {
            if (i != _shotIndex && _shots.Shots[i].Weight > 0f)
            {
                _candidates.Add(i);
            }
        }
        if (_candidates.Count == 0)
        {
            return Mathf.Max(0, _shotIndex);
        }

        int fallback = _candidates[0];
        float bestClearance = -1f;
        while (_candidates.Count > 0)
        {
            int index = PickWeighted();
            Clear(_shots.Shots[index].Frame(player, boss, _axisYaw, 0f), out float clearance);
            if (clearance >= _shots.MinClearFraction)
            {
                return index;
            }
            if (clearance > bestClearance)
            {
                bestClearance = clearance;
                fallback = index;
            }
            _candidates.Remove(index);
        }
        return fallback;
    }

    private int PickWeighted()
    {
        float total = 0f;
        foreach (var index in _candidates)
        {
            total += _shots.Shots[index].Weight;
        }

        float roll = Random.value * total;
        foreach (var index in _candidates)
        {
            roll -= _shots.Shots[index].Weight;
            if (roll <= 0f)
            {
                return index;
            }
        }
        return _candidates[_candidates.Count - 1];
    }

    // Pulls the camera in front of anything between it and its aim point. The clearance is the
    // fraction of the planned distance that stayed free.
    private CinematicFraming Clear(CinematicFraming framing, out float clearance)
    {
        Vector3 toCamera = framing.Position - framing.Aim;
        float distance = toCamera.magnitude;
        clearance = 1f;
        if (distance < Mathf.Epsilon)
        {
            return framing;
        }

        Vector3 direction = toCamera / distance;
        if (!Physics.SphereCast(framing.Aim, _shots.CameraRadius, direction, out RaycastHit hit, distance,
                _shots.ObstacleLayers, QueryTriggerInteraction.Ignore))
        {
            return framing;
        }

        clearance = hit.distance / distance;
        return new CinematicFraming(framing.Aim + direction * hit.distance, framing.Aim);
    }

    private Quaternion Wobble(float amplitude)
    {
        if (amplitude <= 0f)
        {
            return Quaternion.identity;
        }

        float t = _wobbleTime * _shots.HandheldFrequency;
        float pitch = (Mathf.PerlinNoise(t, 0.37f) - 0.5f) * 2f * amplitude;
        float yaw = (Mathf.PerlinNoise(0.71f, t) - 0.5f) * 2f * amplitude;
        return Quaternion.Euler(pitch, yaw, 0f);
    }

    private static float AxisYaw(Vector3 player, Vector3 boss, float fallback)
    {
        Vector3 axis = boss - player;
        axis.y = 0f;
        return axis.sqrMagnitude < MinAxisSeparation * MinAxisSeparation
            ? fallback
            : Vector3.SignedAngle(Vector3.forward, axis, Vector3.up);
    }
}
