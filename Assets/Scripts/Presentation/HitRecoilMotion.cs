using System;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
public struct HitRecoilSettings
{
    public HumanBodyBones[] bones;
    [MinValue(0f)]
    public float maxAngleDegrees;
    [MinValue(0.0001f)]
    public float referenceDamage;
    [Range(0f, 1f)]
    public float sidewaysBias;
    [Range(0f, 1f)]
    public float twistRatio;
    [MinValue(0f)]
    public float holdSeconds;
    [MinValue(0.01f)]
    public float recoverySeconds;
    public bool showDebugLogs;

    public static HitRecoilSettings Default => new HitRecoilSettings
    {
        bones = new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest },
        maxAngleDegrees = 30f,
        referenceDamage = 45f,
        sidewaysBias = 0.7f,
        twistRatio = 0.5f,
        holdSeconds = 0.05f,
        recoverySeconds = 0.3f
    };
}

/// <summary>Shared damage scaling, side selection and scaled-time recovery for additive hit reactions.</summary>
public sealed class HitRecoilMotion
{
    private const float CentredHitOffset = 0.15f;
    private float _peakAngleDegrees;
    private float _elapsedSeconds;
    private float _currentAngleDegrees;
    private float _lastHitSide;
    private Vector3 _leanDirection;

    public float CurrentAngleDegrees => _currentAngleDegrees;
    public float LastHitSide => _lastHitSide;
    public float PeakAngleDegrees => _peakAngleDegrees;

    public void Receive(DamageInfo info, Transform actor, HitRecoilSettings settings)
    {
        Vector3 hitOrigin = info.source != null ? info.source.position : info.hitPoint;
        Vector3 towardAttacker = hitOrigin - actor.position;
        towardAttacker.y = 0f;
        towardAttacker = towardAttacker.sqrMagnitude > 0.0001f ? towardAttacker.normalized : actor.forward;
        Vector3 attackerRight = Vector3.Cross(Vector3.up, towardAttacker);
        float lateralOffset = Vector3.Dot(info.hitPoint - actor.position, attackerRight);
        _lastHitSide = Mathf.Abs(lateralOffset) >= CentredHitOffset ? Mathf.Sign(lateralOffset) : _lastHitSide > 0f ? -1f : 1f;
        _leanDirection = Vector3.Lerp(-towardAttacker, -_lastHitSide * attackerRight, settings.sidewaysBias).normalized;
        float hitAngle = settings.maxAngleDegrees * Mathf.Clamp01(info.amount / Mathf.Max(0.0001f, settings.referenceDamage));
        _peakAngleDegrees = Mathf.Max(_currentAngleDegrees, hitAngle);
        _elapsedSeconds = 0f;
    }

    public void Tick(float deltaTime, HitRecoilSettings settings)
    {
        _elapsedSeconds += deltaTime;
        float recovery = Mathf.Clamp01((_elapsedSeconds - settings.holdSeconds) / Mathf.Max(0.01f, settings.recoverySeconds));
        if (recovery >= 1f)
        {
            _peakAngleDegrees = 0f;
        }
        float remaining = 1f - recovery;
        _currentAngleDegrees = _peakAngleDegrees * remaining * remaining;
    }

    public Quaternion GetOffset(int boneCount, float twistRatio)
    {
        if (boneCount == 0 || _currentAngleDegrees <= 0f)
        {
            return Quaternion.identity;
        }
        float boneAngle = _currentAngleDegrees / boneCount;
        Vector3 tiltAxis = Vector3.Cross(Vector3.up, _leanDirection);
        return Quaternion.AngleAxis(boneAngle, tiltAxis)
            * Quaternion.AngleAxis(boneAngle * twistRatio * _lastHitSide, Vector3.up);
    }

    public void Reset()
    {
        _peakAngleDegrees = 0f;
        _currentAngleDegrees = 0f;
        _elapsedSeconds = 0f;
    }
}
