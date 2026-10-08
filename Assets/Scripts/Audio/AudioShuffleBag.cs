using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stateful no-repeat selection for one authored audio cue.
/// </summary>
public sealed class AudioShuffleBag
{
    private readonly List<AudioClip> _remainingClips = new();
    private readonly List<AudioClip> _uniqueClips = new();

    public int RemainingCount => _remainingClips.Count;

    /// <summary>
    /// Takes one valid clip, consuming every unique clip once before the bag
    /// refills. A refill avoids repeating the clip selected immediately before
    /// the boundary when another unique clip is available.
    /// </summary>
    public bool TryTake(IList<AudioClip> clips, AudioClip previousClip, out AudioClip selectedClip)
    {
        selectedClip = null;
        CollectUniqueClips(clips);
        if (_uniqueClips.Count == 0)
        {
            _remainingClips.Clear();
            return false;
        }

        RemoveInvalidRemainingClips();
        if (_remainingClips.Count == 0)
        {
            _remainingClips.AddRange(_uniqueClips);
        }

        int selectedIndex = SelectIndex(previousClip);
        if (selectedIndex < 0)
        {
            return false;
        }

        selectedClip = _remainingClips[selectedIndex];
        _remainingClips.RemoveAt(selectedIndex);
        return selectedClip != null;
    }

    private void CollectUniqueClips(IList<AudioClip> clips)
    {
        _uniqueClips.Clear();
        if (clips == null)
        {
            return;
        }

        for (int i = 0; i < clips.Count; i++)
        {
            AudioClip clip = clips[i];
            if (clip == null || _uniqueClips.Contains(clip))
            {
                continue;
            }
            _uniqueClips.Add(clip);
        }
    }

    private void RemoveInvalidRemainingClips()
    {
        for (int i = _remainingClips.Count - 1; i >= 0; i--)
        {
            AudioClip clip = _remainingClips[i];
            if (clip == null || !_uniqueClips.Contains(clip))
            {
                _remainingClips.RemoveAt(i);
            }
        }
    }

    private int SelectIndex(AudioClip previousClip)
    {
        if (_remainingClips.Count == 0)
        {
            return -1;
        }

        int index = Random.Range(0, _remainingClips.Count);
        if (_remainingClips.Count > 1 && _remainingClips[index] == previousClip)
        {
            index = (index + Random.Range(1, _remainingClips.Count)) % _remainingClips.Count;
        }

        return index;
    }
}
