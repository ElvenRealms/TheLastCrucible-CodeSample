using System.Text;
using UnityEngine;

/// <summary>
/// A number about the fight that a curve consideration can read.
/// </summary>
public enum BossCurveInput
{
    /// <summary>Planar distance to the target, in metres.</summary>
    Distance,
    /// <summary>How far the target is from straight ahead, in degrees from 0 to 180. Left and right read the same.</summary>
    FacingAngle,
    /// <summary>The boss's health as a share of its maximum, from 0 to 1.</summary>
    BossHealth,
    /// <summary>Seconds since the boss last started this option or one that opens the same way. Reads the right end of the curve until it has.</summary>
    SecondsSinceUsed
}

/// <summary>
/// Favours or avoids an option by one number about the fight, read from a curve. The curve's
/// horizontal axis is the number itself, in its own unit, and its height is the multiplier, so
/// a sweet spot is a bump drawn where the option should be chosen.
/// </summary>
[System.Serializable]
public class BossCurveConsideration : BossConsideration
{
    // More keys than this are summed up as a count, to keep the Inspector row short.
    private const int MaxKeysInLabel = 3;

    [SerializeField]
    [Tooltip("The number the curve reads.")]
    private BossCurveInput input;
    [SerializeField]
    [Tooltip("Multiplier on the option's weight at each value of the input. The horizontal axis is in the input's own unit: metres, degrees, seconds, or health from 0 to 1. Values beyond either end use the nearest key. 1 leaves the weight alone and 0 rules the option out.")]
    private AnimationCurve multiplier = AnimationCurve.Constant(0f, 1f, 1f);

    public override string Subject => input switch
    {
        BossCurveInput.FacingAngle => "Facing Angle",
        BossCurveInput.BossHealth => "Boss Health",
        BossCurveInput.SecondsSinceUsed => "Since Used",
        _ => "Distance"
    };

    public override string InspectorLabel
    {
        get
        {
            if (multiplier == null || multiplier.length == 0)
            {
                return $"{Subject}: no curve";
            }
            if (multiplier.length > MaxKeysInLabel)
            {
                return $"{Subject}: curve of {multiplier.length} keys";
            }

            var label = new StringBuilder(Subject).Append(": ");
            for (int i = 0; i < multiplier.length; i++)
            {
                Keyframe key = multiplier[i];
                label.Append(i > 0 ? ", " : string.Empty).Append(FormatInput(key.time))
                    .Append(System.FormattableString.Invariant($" x{key.value:0.##}"));
            }
            return label.ToString();
        }
    }

    public BossCurveConsideration()
    {
    }

    public BossCurveConsideration(BossCurveInput input, AnimationCurve multiplier)
    {
        this.input = input;
        this.multiplier = multiplier;
    }

    public override float Score(in BossDecisionContext context, BossOptionMemory memory)
    {
        if (multiplier == null || multiplier.length == 0)
        {
            return 1f;
        }

        // Clamped by hand because an option that has never started reads as infinitely long ago.
        float first = multiplier[0].time;
        float last = multiplier[multiplier.length - 1].time;
        return multiplier.Evaluate(Mathf.Clamp(Read(context, memory), first, last));
    }

    private float Read(in BossDecisionContext context, BossOptionMemory memory)
    {
        return input switch
        {
            BossCurveInput.FacingAngle => Mathf.Abs(context.facingAngle),
            BossCurveInput.BossHealth => context.healthFraction,
            BossCurveInput.SecondsSinceUsed => memory.SecondsSinceShown,
            _ => context.planarDistance
        };
    }

    private string FormatInput(float value)
    {
        return input switch
        {
            BossCurveInput.FacingAngle => System.FormattableString.Invariant($"{value:0.#} deg"),
            BossCurveInput.BossHealth => System.FormattableString.Invariant($"{value * 100f:0.#}%"),
            BossCurveInput.SecondsSinceUsed => System.FormattableString.Invariant($"{value:0.##} s"),
            _ => System.FormattableString.Invariant($"{value:0.##} m")
        };
    }
}
