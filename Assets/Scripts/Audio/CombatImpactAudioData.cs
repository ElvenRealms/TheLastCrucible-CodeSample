using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Shared clip selection and variation settings for received combat impacts.
/// </summary>
[CreateAssetMenu(fileName = "CombatImpactAudio", menuName = "BossFight/Combat Impact Audio Data")]
public class CombatImpactAudioData : CombatFeedbackEffectData
{
    [BoxGroup("Normal Impact"), ValidateInput(nameof(NormalClipsValid), "normalImpactClips must contain at least one valid clip.")]
    public AudioClip[] normalImpactClips;
    [BoxGroup("Normal Impact"), Range(0f, 1f)]
    public float normalVolume = 1f;

    [BoxGroup("Lethal Impact"), ValidateInput(nameof(LethalClipsValid), "lethalImpactClips must contain at least one valid clip.")]
    public AudioClip[] lethalImpactClips;
    [BoxGroup("Lethal Impact"), Range(0f, 1f)]
    public float lethalVolume = 1f;

    [BoxGroup("Variation"), MinMaxSlider(0.5f, 1.5f, true)]
    public Vector2 pitchRange = new(0.95f, 1.05f);

    private bool NormalClipsValid => ClipsValid(normalImpactClips);
    private bool LethalClipsValid => ClipsValid(lethalImpactClips);

    private static bool ClipsValid(AudioClip[] clips)
    {
        return clips != null && clips.Length > 0 && System.Array.TrueForAll(clips, clip => clip != null);
    }
}
