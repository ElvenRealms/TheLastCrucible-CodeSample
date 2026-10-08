/// <summary>
/// Player-controllable audio classifications routed through the scene mixer.
/// </summary>
public enum AudioCategory
{
    Music,
    SFX,
    Ambience,
    UI
}

/// <summary>
/// Whether an authored one-shot is played without spatial attenuation or at a
/// captured world position.
/// </summary>
public enum AudioSpatialMode
{
    TwoD,
    Spatial
}

/// <summary>
/// The supported attenuation curves for a spatial audio cue.
/// </summary>
public enum AudioRolloffType
{
    Logarithmic,
    Linear
}

/// <summary>
/// Chooses how an AudioCue selects among its authored clips.
/// </summary>
public enum AudioCueSelectionMode
{
    Random,
    ShuffleBag
}
