using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// One directed camera move: a position and rotation offset in camera space that follows a weight
/// curve and returns to rest. Unlike a shake it has a direction, so it can follow a swing.
/// </summary>
[CreateAssetMenu(fileName = "CameraMotion", menuName = "BossFight/Camera Motion Data")]
public class CameraMotionData : CombatFeedbackEffectData
{
    [BoxGroup("Move"), SuffixLabel("m")]
    [Tooltip("Camera offset at full weight, in camera space: X right, Y up, Z into the view (positive pushes in).")]
    public Vector3 positionOffset;
    [BoxGroup("Move"), SuffixLabel("deg")]
    [Tooltip("Camera rotation at full weight, in camera space: X pitches down, Y turns right, Z rolls the camera anticlockwise, so the picture tilts clockwise.")]
    public Vector3 rotationOffset;

    [BoxGroup("Timing"), MinValue(0.01f), SuffixLabel("s")]
    [Tooltip("Seconds the move lasts, on scaled time, so hit-stop and slow motion hold it with the swing.")]
    public float duration = 0.3f;
    [BoxGroup("Timing"), ValidateInput(nameof(RestsAtBothEnds), "weightOverTime must be 0 at times 0 and 1, or the camera jumps.")]
    [Tooltip("Weight of the offsets over the move, from time 0 to 1. Negative weights move the opposite way, for an anticipation before the push.")]
    public AnimationCurve weightOverTime = new(
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(0.3f, 1f, 0f, 0f),
        new Keyframe(1f, 0f, 0f, 0f));

    private bool RestsAtBothEnds => weightOverTime != null && weightOverTime.length > 0 &&
        Mathf.Approximately(weightOverTime.Evaluate(0f), 0f) && Mathf.Approximately(weightOverTime.Evaluate(1f), 0f);

    /// <summary>Weight of the offsets this many seconds into the move; 0 once it has finished.</summary>
    public float GetWeight(float elapsed)
    {
        if (weightOverTime == null || duration <= 0f || elapsed >= duration) return 0f;
        return weightOverTime.Evaluate(Mathf.Max(0f, elapsed) / duration);
    }
}
