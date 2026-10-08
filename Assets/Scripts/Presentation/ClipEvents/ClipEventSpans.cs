using Animancer;
using System.Collections.Generic;

/// <summary>
/// The timed clip events that are running for one emitter. Each span ends at the first of: its
/// state's playback crossing the end time, the state fading out or leaving its layer, or the state
/// restarting. Ending a VFX span stops its emission; ending a sound span stops its voice.
/// </summary>
public sealed class ClipEventSpans
{
    private readonly VfxPool _vfxPool;
    private readonly List<ActiveSpan> _spans = new();

    public ClipEventSpans(VfxPool vfxPool)
    {
        _vfxPool = vfxPool;
    }

    public int Count => _spans.Count;

    /// <summary>Tracks a VFX span that started in a loop cycle of the state at a normalized time.</summary>
    public void AddVfx(AnimancerState state, ClipEvent clipEvent, int cycle, float time, VfxPool.Handle vfx)
    {
        _spans.Add(new ActiveSpan(state, clipEvent, cycle, time) { vfx = vfx });
    }

    /// <summary>Tracks a sound span that started in a loop cycle of the state at a normalized time.</summary>
    public void AddSound(AnimancerState state, ClipEvent clipEvent, int cycle, float time, AudioVoiceHandle voice)
    {
        _spans.Add(new ActiveSpan(state, clipEvent, cycle, time) { voice = voice });
    }

    /// <summary>
    /// Ends every span whose state has finished it. <paramref name="currentTimes"/> holds this frame's
    /// normalized time for each state still playing at weight; a state missing from it has faded out
    /// or left its layer.
    /// </summary>
    public void EndFinished(IReadOnlyDictionary<AnimancerState, float> currentTimes)
    {
        for (int i = _spans.Count - 1; i >= 0; i--)
        {
            ActiveSpan span = _spans[i];
            if (currentTimes.TryGetValue(span.state, out float time) && time >= span.lastTime && time < span.End)
            {
                span.lastTime = time;
                continue;
            }

            End(i);
        }
    }

    /// <summary>Ends every running span.</summary>
    public void EndAll()
    {
        for (int i = _spans.Count - 1; i >= 0; i--)
        {
            End(i);
        }
    }

    private void End(int index)
    {
        ActiveSpan span = _spans[index];
        _spans.RemoveAt(index);
        _vfxPool.StopEmitting(span.vfx);
        span.voice?.Stop();
    }

    private sealed class ActiveSpan
    {
        public readonly AnimancerState state;
        public readonly ClipEvent clipEvent;
        public readonly int cycle;
        public float lastTime;
        public VfxPool.Handle vfx;
        public AudioVoiceHandle voice;

        public ActiveSpan(AnimancerState state, ClipEvent clipEvent, int cycle, float time)
        {
            this.state = state;
            this.clipEvent = clipEvent;
            this.cycle = cycle;
            lastTime = time;
        }

        // The end time lies within the start's cycle, so a span never crosses a loop boundary.
        public float End => cycle + clipEvent.EndTime;
    }
}
