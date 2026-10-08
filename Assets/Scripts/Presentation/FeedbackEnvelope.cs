using UnityEngine;

/// <summary>
/// Attack, hold, release envelope with smooth-step easing. A new trigger restarts the envelope
/// from its current value and keeps the stronger magnitude so rapid hits never dip.
/// </summary>
public struct FeedbackEnvelope
{
    private float _from;
    private float _peak;
    private float _attack;
    private float _hold;
    private float _release;
    private float _elapsed;

    /// <summary>
    /// Value at the last call to <see cref="Trigger"/> or <see cref="Advance"/>.
    /// </summary>
    public float Value { get; private set; }

    /// <summary>
    /// Whether the envelope has not yet finished releasing.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Restarts the envelope toward <paramref name="peak"/>, or toward the current value when that is stronger.
    /// </summary>
    public void Trigger(float peak, float attack, float hold, float release)
    {
        _from = Value;
        _peak = Mathf.Abs(peak) > Mathf.Abs(Value) ? peak : Value;
        _attack = Mathf.Max(0f, attack);
        _hold = Mathf.Max(0f, hold);
        _release = Mathf.Max(0f, release);
        _elapsed = 0f;
        IsActive = true;
        Value = Evaluate();
    }

    /// <summary>
    /// Moves the envelope forward and returns its new value.
    /// </summary>
    public float Advance(float deltaTime)
    {
        if (!IsActive)
        {
            return Value;
        }

        _elapsed += deltaTime;
        Value = Evaluate();
        return Value;
    }

    /// <summary>
    /// Stops the envelope and returns its value to zero.
    /// </summary>
    public void Clear()
    {
        _elapsed = 0f;
        Value = 0f;
        IsActive = false;
    }

    private float Evaluate()
    {
        if (_elapsed < _attack)
        {
            return Mathf.SmoothStep(_from, _peak, _elapsed / _attack);
        }

        float held = _elapsed - _attack;
        if (held < _hold)
        {
            return _peak;
        }

        float released = held - _hold;
        if (released < _release)
        {
            return Mathf.SmoothStep(_peak, 0f, released / _release);
        }

        IsActive = false;
        return 0f;
    }
}
