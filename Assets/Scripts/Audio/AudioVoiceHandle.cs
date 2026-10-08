/// <summary>
/// Generation-checked reference to one rented pooled audio voice.
/// </summary>
public sealed class AudioVoiceHandle
{
    private readonly AudioPlaybackService service;
    private readonly int voiceIndex;
    private readonly int generation;

    internal AudioVoiceHandle(AudioPlaybackService service, int voiceIndex, int generation)
    {
        this.service = service;
        this.voiceIndex = voiceIndex;
        this.generation = generation;
    }

    /// <summary>
    /// Gets whether this handle still owns the voice lease it was created for.
    /// </summary>
    public bool IsValid => service != null && service.IsHandleValid(this);

    /// <summary>
    /// Gets whether the leased voice is currently playing.
    /// </summary>
    public bool IsPlaying => service != null && service.IsHandlePlaying(this);

    /// <summary>
    /// Stops the leased voice if this handle is still current.
    /// </summary>
    public bool Stop()
    {
        return service != null && service.StopHandle(this);
    }

    internal AudioPlaybackService Service => service;
    internal int VoiceIndex => voiceIndex;
    internal int Generation => generation;
}
