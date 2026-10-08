using UnityEngine;

/// <summary>
/// What the boss remembers about one option between decisions: how long ago it last started it,
/// how long ago it last showed the same opening, whether that opening was the last one at its
/// priority, and whether it has set the option aside for a while. Runs on the boss's own clock, so it pauses when the boss does.
/// </summary>
public class BossOptionMemory
{
    private float _setAsideRemaining;

    /// <summary>Seconds since the option last started; infinite when it has not started this attempt.</summary>
    public float SecondsSinceStarted { get; private set; } = float.PositiveInfinity;
    /// <summary>Seconds since the boss last started this option or one that opens the same way; infinite when it has not this attempt.</summary>
    public float SecondsSinceShown { get; private set; } = float.PositiveInfinity;
    /// <summary>True while the option the boss last started among those sharing its priority was this one, or one that opens the same way.</summary>
    public bool StartedLast { get; set; }
    /// <summary>True while the boss leaves the option alone after deciding against it.</summary>
    public bool IsSetAside => _setAsideRemaining > 0f;

    public void Tick(float deltaTime)
    {
        SecondsSinceStarted += deltaTime;
        SecondsSinceShown += deltaTime;
        _setAsideRemaining = Mathf.Max(0f, _setAsideRemaining - deltaTime);
    }

    public void NoteStarted()
    {
        SecondsSinceStarted = 0f;
        SecondsSinceShown = 0f;
    }

    /// <summary>Records that the boss started another option that opens the same way as this one.</summary>
    public void NoteShown()
    {
        SecondsSinceShown = 0f;
    }

    /// <summary>Leaves the option alone for a while. Never shortens a wait already running.</summary>
    public void SetAside(float seconds)
    {
        _setAsideRemaining = Mathf.Max(_setAsideRemaining, seconds);
    }

    /// <summary>Forgets everything, for a new attempt.</summary>
    public void Reset()
    {
        SecondsSinceStarted = float.PositiveInfinity;
        SecondsSinceShown = float.PositiveInfinity;
        StartedLast = false;
        _setAsideRemaining = 0f;
    }
}
