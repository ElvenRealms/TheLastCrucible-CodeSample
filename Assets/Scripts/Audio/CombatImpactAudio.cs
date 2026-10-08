using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Executes explicit spatial combat-impact audio requests.
/// </summary>
public class CombatImpactAudio : MonoBehaviour, ICombatFeedbackExecutor
{
    [FoldoutGroup("Config"), SerializeField, Required, ChildGameObjectsOnly]
    [ValidateInput(nameof(ImpactSourceIsSpatial), "impactSource must use spatial audio.")]
    private AudioSource impactSource;
    [FoldoutGroup("Config"), SerializeField, Tooltip("Resolves via FindAnyObjectByType at runtime when left empty.")]
    private CombatFeedbackController controller;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private AudioClip lastPlayedClip;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private bool lastImpactWasLethal;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int playedImpactCount;

    private int lastNormalIndex = -1;
    private int lastLethalIndex = -1;

    private bool ImpactSourceIsSpatial => impactSource == null || impactSource.spatialBlend > 0f;

    public AudioClip LastPlayedClip => lastPlayedClip;
    public bool LastImpactWasLethal => lastImpactWasLethal;
    public int PlayedImpactCount => playedImpactCount;
    public bool TryExecute(CombatFeedbackEffectData effectData, in CombatFeedbackRequest request)
    {
        CombatImpactAudioData data = effectData as CombatImpactAudioData;
        if (data == null || impactSource == null || !isActiveAndEnabled) return false;

        return RequestImpact(data, request.position, request.lethal);
    }

    private void OnEnable()
    {
        if (controller == null) controller = FindAnyObjectByType<CombatFeedbackController>();
        if (controller == null) Debug.LogWarning($"{name} has no CombatFeedbackController to register with.", this);
        else controller.Register(this);
    }

    private void OnDisable()
    {
        if (controller != null) controller.Unregister(this);
    }

    public bool RequestImpact(CombatImpactAudioData data, Vector3 position, bool lethal)
    {
        if (data == null || impactSource == null) return false;

        impactSource.transform.position = position;
        if (lethal)
        {
            return PlayImpact(data, data.lethalImpactClips, data.lethalVolume, ref lastLethalIndex, true);
        }

        return PlayImpact(data, data.normalImpactClips, data.normalVolume, ref lastNormalIndex, false);
    }

    private bool PlayImpact(
        CombatImpactAudioData data,
        AudioClip[] clips,
        float volume,
        ref int lastIndex,
        bool lethal)
    {
        int index = SelectClipIndex(clips, lastIndex);
        if (index < 0)
        {
            if (showDebugLogs) Debug.LogWarning($"{name} has no valid {(lethal ? "lethal" : "normal")} impact clips.", this);
            return false;
        }

        lastIndex = index;
        lastPlayedClip = clips[index];
        lastImpactWasLethal = lethal;
        playedImpactCount++;
        impactSource.pitch = Random.Range(data.pitchRange.x, data.pitchRange.y);
        impactSource.PlayOneShot(lastPlayedClip, volume);
        if (showDebugLogs) Debug.Log($"{name} played {(lethal ? "lethal" : "normal")} impact clip '{lastPlayedClip.name}'.", this);
        return true;
    }

    private static int SelectClipIndex(AudioClip[] clips, int lastIndex)
    {
        if (clips == null || clips.Length == 0) return -1;

        int startIndex = Random.Range(0, clips.Length);
        if (clips.Length > 1 && startIndex == lastIndex)
            startIndex = (startIndex + Random.Range(1, clips.Length)) % clips.Length;

        for (int offset = 0; offset < clips.Length; offset++)
        {
            int index = (startIndex + offset) % clips.Length;
            if (clips[index] != null) return index;
        }

        return -1;
    }
}
