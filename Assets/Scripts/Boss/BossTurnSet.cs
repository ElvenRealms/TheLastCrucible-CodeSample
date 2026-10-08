using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Authored turn clips keyed by the signed yaw they rotate through. BossMotor
/// routes the clip's root yaw through OnAnimatorMove, scaled to land on the
/// angle actually requested rather than the clip's authored one, and applies
/// the clip's small root translation so stepping feet stay planted.
/// </summary>
[CreateAssetMenu(fileName = "BossTurnSet", menuName = "BossFight/Boss Turn Set")]
public class BossTurnSet : ScriptableObject
{
    [MinValue(0f), SuffixLabel("deg"), Tooltip("Smallest navigation path angle that plays a turn clip instead of a procedural in-place correction.")]
    public float minimumTurnAngle = 90f;
    [ListDrawerSettings(ShowFoldout = true, ListElementLabelName = "InspectorLabel", DefaultExpandedState = false)]
    public BossTurnDefinition[] turns;

    private void OnValidate()
    {
        if (turns == null)
        {
            return;
        }

        foreach (var turn in turns)
        {
            turn?.clip?.ResetTransition();
        }
    }

    /// <summary>
    /// Returns the turn whose authored yaw is closest to the requested signed
    /// angle (positive is clockwise / right), or null when none is authored.
    /// </summary>
    public BossTurnDefinition Select(float signedAngle)
    {
        if (turns == null) return null;

        BossTurnDefinition best = null;
        float bestDelta = float.MaxValue;
        for (int i = 0; i < turns.Length; i++)
        {
            BossTurnDefinition turn = turns[i];
            if (turn == null || turn.clip == null || !turn.clip.IsValid) continue;
            float delta = Mathf.Abs(Mathf.DeltaAngle(signedAngle, turn.signedAngle));
            if (delta >= bestDelta) continue;
            bestDelta = delta;
            best = turn;
        }

        return best;
    }
}

[System.Serializable]
public class BossTurnDefinition
{
    [SuffixLabel("deg"), Tooltip("Signed root yaw the clip actually rotates through (measure it; positive turns right). The motor scales it to the requested angle.")]
    public float signedAngle;
    [ValidateInput(nameof(ClipValid), "clip must contain a valid non-legacy AnimationClip.")]
    public ClipSettings clip;

    public string InspectorLabel => $"{signedAngle:+0;-0;0}° — {(clip != null && clip.Clip != null ? clip.Clip.name : "Unassigned Turn")}";

    private bool ClipValid => clip != null && clip.IsValid;
}
