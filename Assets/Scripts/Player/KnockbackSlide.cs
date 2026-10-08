using UnityEngine;

/// <summary>
/// A short planar slide away from an attacker that starts at its fastest and
/// eases out to rest over an authored distance. Shared by the player's
/// reactions to a hit: the Flinch knockback and the pushback of a blocked hit.
/// </summary>
public class KnockbackSlide
{
    private Vector3 _direction;
    private float _distance;
    private float _duration;
    private float _elapsed;

    /// <summary>
    /// Starts a slide that carries a body at <paramref name="position"/> away from
    /// <paramref name="attacker"/>. Without an attacker, or directly on top of it, the body stays put.
    /// </summary>
    public void StartAwayFrom(Transform attacker, Vector3 position, float distance, float duration)
    {
        Stop();
        if (attacker == null)
        {
            return;
        }

        Vector3 away = position - attacker.position;
        away.y = 0f;
        if (away.sqrMagnitude <= 0.0001f)
        {
            return;
        }
        _direction = away.normalized;
        _distance = distance;
        _duration = duration;
    }

    public void Stop()
    {
        _direction = Vector3.zero;
        _elapsed = 0f;
    }

    /// <summary>The slide's velocity for the coming frame, which is zero once it has run its course.</summary>
    public Vector3 Advance(float deltaTime)
    {
        if (_direction == Vector3.zero || _elapsed >= _duration)
        {
            return Vector3.zero;
        }

        // Linear ease-out: starting at twice the average speed and reaching zero at
        // the end covers exactly the authored distance.
        float peakSpeed = 2f * _distance / _duration;
        Vector3 velocity = _direction * (peakSpeed * (1f - _elapsed / _duration));
        _elapsed += deltaTime;
        return velocity;
    }
}
