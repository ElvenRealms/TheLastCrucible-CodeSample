using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sole writer of Time.timeScale and Time.fixedDeltaTime. Gameplay systems slow or freeze time
/// through claims, tooling sets the base scale, and the lowest of the base and every active
/// claim wins. Releasing a claim restores whatever the remaining claims and base resolve to.
/// </summary>
public static class TimeScaleArbiter
{
    private static readonly List<TimeScaleClaim> _claims = new();
    private static float _baseScale = 1f;
    private static float _defaultFixedDeltaTime;
    private static bool _fixedDeltaTimeScaled;

    /// <summary>
    /// Scale applied when no claim is lower. Set by tooling such as trials and scenarios.
    /// </summary>
    public static float BaseScale => _baseScale;

    /// <summary>
    /// Scale currently resolved from the base and active claims.
    /// </summary>
    public static float EffectiveScale
    {
        get
        {
            float scale = _baseScale;
            foreach (var claim in _claims)
            {
                scale = Mathf.Min(scale, claim.Scale);
            }
            return scale;
        }
    }

    /// <summary>
    /// Active claims in request order, for debugging.
    /// </summary>
    public static IReadOnlyList<TimeScaleClaim> Claims => _claims;

    /// <summary>
    /// Holds time at or below <paramref name="scale"/> until the returned claim is released.
    /// The claim can change its scale meanwhile through <see cref="TimeScaleClaim.SetScale"/>.
    /// </summary>
    public static TimeScaleClaim Claim(float scale, Object owner)
    {
        var claim = new TimeScaleClaim(Mathf.Max(0f, scale), owner);
        _claims.Add(claim);
        Apply();
        return claim;
    }

    /// <summary>
    /// Sets the scale time returns to once every claim is released.
    /// </summary>
    public static void SetBaseScale(float scale)
    {
        _baseScale = Mathf.Max(0f, scale);
        Apply();
    }

    /// <summary>
    /// Drops every claim and restores a base scale of 1 with the default physics step.
    /// Runs on each Play Mode entry because domain reload may be disabled.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        foreach (var claim in _claims)
        {
            claim.Deactivate();
        }
        _claims.Clear();
        _baseScale = 1f;

        // Only trust the live step as the project default when this arbiter has not scaled it.
        if (!_fixedDeltaTimeScaled)
        {
            _defaultFixedDeltaTime = Time.fixedDeltaTime;
        }
        Apply();
    }

    internal static void Refresh()
    {
        Apply();
    }

    internal static void Release(TimeScaleClaim claim)
    {
        if (!_claims.Remove(claim))
        {
            return;
        }
        claim.Deactivate();
        Apply();
    }

    private static void Apply()
    {
        if (_defaultFixedDeltaTime <= 0f)
        {
            _defaultFixedDeltaTime = Time.fixedDeltaTime;
        }

        float scale = EffectiveScale;
        Time.timeScale = scale;

        // Scale the physics step with slow motion so it stays smooth. Physics does not step at 0,
        // and fixedDeltaTime must never be 0, so a freeze keeps the default. Speed-ups keep the
        // default too, rather than coarsening the step.
        _fixedDeltaTimeScaled = scale > 0f && scale < 1f;
        Time.fixedDeltaTime = _fixedDeltaTimeScaled ? _defaultFixedDeltaTime * scale : _defaultFixedDeltaTime;
    }
}

/// <summary>
/// Handle for one <see cref="TimeScaleArbiter"/> claim. Release it when the effect ends.
/// </summary>
public sealed class TimeScaleClaim
{
    internal TimeScaleClaim(float scale, Object owner)
    {
        Scale = scale;
        Owner = owner;
        IsActive = true;
    }

    public float Scale { get; private set; }
    public Object Owner { get; }
    public bool IsActive { get; private set; }

    /// <summary>
    /// Changes the scale this claim holds time at and re-resolves the effective scale.
    /// Does nothing once the claim is released.
    /// </summary>
    public void SetScale(float scale)
    {
        if (!IsActive)
        {
            return;
        }

        Scale = Mathf.Max(0f, scale);
        TimeScaleArbiter.Refresh();
    }

    /// <summary>
    /// Returns this claim's hold on time. Safe to call more than once.
    /// </summary>
    public void Release()
    {
        if (IsActive)
        {
            TimeScaleArbiter.Release(this);
        }
    }

    internal void Deactivate()
    {
        IsActive = false;
    }
}
