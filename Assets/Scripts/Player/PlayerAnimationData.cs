using Animancer;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Animation transitions for the player. The locomotion mixer's parameter is
/// the planar speed in m/s, so its thresholds should match the movement config
/// speeds and the clips' travel speeds to avoid foot sliding.
/// </summary>
[CreateAssetMenu(fileName = "PlayerAnimations", menuName = "BossFight/Player Animation Data")]
public class PlayerAnimationData : ScriptableObject
{
    [BoxGroup("Locomotion"), SerializeField, LabelText("Free Mixer")]
    [InfoBox("Free-look mixer parameter: planar speed in m/s.")]
    [ValidateInput(nameof(IsLocomotionMixerValid), "Free Mixer must contain valid clips and thresholds.")]
    private LinearMixerSettings locomotionMixer;
    [BoxGroup("Locomotion"), SerializeField, LabelText("Locked-On Mixer")]
    [InfoBox("Locked-on mixer parameter: local velocity in m/s (x = strafe, y = forward).")]
    [ValidateInput(nameof(IsLockedLocomotionMixerValid), "Locked-On Mixer must contain valid clips and thresholds.")]
    private Mixer2DSettings lockedLocomotionMixer;

    [BoxGroup("Dodge"), SerializeField]
    [ListDrawerSettings(ListElementLabelName = "InspectorLabel", DefaultExpandedState = false)]
    [InfoBox("8 rolls clockwise from forward: F, FR, R, BR, B, BL, L, FL. Free-look dodge uses index 0.")]
    [ValidateInput(nameof(AreDodgeRollsValid), "Dodge Rolls must contain exactly eight valid clips.")]
    private ClipSettings[] dodgeRolls = new ClipSettings[8];
    [BoxGroup("Dodge"), SerializeField, Range(0f, 1f)]
    [Tooltip("Normalized time at which dodge i-frames begin. Shared by all rolls.")]
    private float dodgeIFrameStart = 0.1f;
    [BoxGroup("Dodge"), SerializeField, Range(0f, 1f)]
    [ValidateInput(nameof(IsIFrameWindowOrdered), "dodgeIFrameEnd must be greater than dodgeIFrameStart.")]
    [Tooltip("Normalized time at which dodge i-frames end.")]
    private float dodgeIFrameEnd = 0.6f;
    [BoxGroup("Dodge"), SerializeField, Range(0f, 1f)]
    [ValidateInput(nameof(IsDodgeBufferAfterIFrames), "dodgeInputBufferStart must be at or after dodgeIFrameEnd so a buffered action can never pre-empt i-frames.")]
    [Tooltip("Normalized time from which the latest Attack, Dodge, or Block input is buffered until the roll ends.")]
    private float dodgeInputBufferStart = 0.7f;

    [BoxGroup("Attacks"), SerializeField, AssetsOnly, InlineEditor, LabelText("Light Combo")]
    [ListDrawerSettings(ListElementLabelName = "InspectorLabel", DefaultExpandedState = false)]
    [InfoBox("Ordered light combo. Inputs chain or cancel during their authored windows.")]
    [ValidateInput(nameof(IsLightComboValid), "Light Combo must contain at least one valid Attack asset.")]
    private PlayerAttackData[] lightAttacks;

    [BoxGroup("Attacks"), SerializeField, AssetsOnly, InlineEditor, LabelText("Heavy Combo")]
    [ListDrawerSettings(ListElementLabelName = "InspectorLabel", DefaultExpandedState = false)]
    [InfoBox("Ordered heavy combo. Inputs chain or cancel during their authored windows.")]
    [ValidateInput(nameof(IsHeavyComboValid), "Heavy Combo must contain at least one valid Attack asset.")]
    private PlayerAttackData[] heavyAttacks;

    [BoxGroup("Attacks"), SerializeField, AssetsOnly, InlineEditor, LabelText("Running Attack")]
    [InfoBox("Optional. Light attack while sprinting; when empty, sprint-attack uses the Light Combo.")]
    [ValidateInput(nameof(IsRunningAttackValid), "Running Attack must be empty or a valid Attack asset.")]
    private PlayerAttackData runningAttack;

    [BoxGroup("Block"), SerializeField, LabelText("Begin")]
    [InfoBox("Begin raises the guard into the Loop, which holds while Block is held; End lowers it.")]
    [ValidateInput(nameof(IsBlockValid), "Begin, Loop and End must each contain a valid non-legacy AnimationClip.")]
    private ClipSettings blockBegin;
    [BoxGroup("Block"), SerializeField, LabelText("Loop")]
    private ClipSettings blockLoop;
    [BoxGroup("Block"), SerializeField, LabelText("End")]
    private ClipSettings blockEnd;
    [BoxGroup("Block"), SerializeField, Required, AssetsOnly, LabelText("Legs Mask")]
    [Tooltip("Body parts the Locked-On Mixer drives while the guard moves; the guard clips keep the rest. Without it the guard slides.")]
    private AvatarMask guardLegsMask;
    [BoxGroup("Block"), SerializeField, MinValue(0f), SuffixLabel("m")]
    [Tooltip("Distance a blocked hit slides the player away from the attacker. 0 keeps the guard in place.")]
    private float blockPushbackDistance = 0.35f;
    [BoxGroup("Block"), SerializeField, MinValue(0.01f), SuffixLabel("s")]
    [Tooltip("Seconds the pushback slide lasts, easing out from its fastest at the hit.")]
    private float blockPushbackDuration = 0.2f;

    [BoxGroup("Parry"), SerializeField, LabelText("Front")]
    [InfoBox("Played from the guard when a hit is parried, picked by the side the hit landed on.")]
    [ValidateInput(nameof(IsParryValid), "Front, Left and Right must each contain a valid non-legacy AnimationClip.")]
    private ClipSettings parryFront;
    [BoxGroup("Parry"), SerializeField, LabelText("Left")]
    private ClipSettings parryLeft;
    [BoxGroup("Parry"), SerializeField, LabelText("Right")]
    private ClipSettings parryRight;
    [BoxGroup("Parry"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Seconds after a parry before Attack or Dodge can start. Presses during it are buffered; Block parries again at once.")]
    private float parryRecovery = 0.3f;

    [BoxGroup("Flinch"), SerializeField, InlineProperty, HideLabel]
    [ValidateInput(nameof(IsFlinchValid), "Flinch must contain a valid non-legacy AnimationClip.")]
    private ClipSettings flinch;
    [BoxGroup("Flinch"), SerializeField, LabelText("Guard Break")]
    [Tooltip("Replaces the Flinch clip when a blocked hit breaks the guard.")]
    private ClipSettings guardBreak;
    [BoxGroup("Flinch"), SerializeField, Range(0f, 1f)]
    [Tooltip("Normalized time from which the latest Attack, Dodge, or Block input is buffered until the Flinch ends.")]
    private float flinchInputBufferStart = 0.6f;
    [BoxGroup("Flinch"), SerializeField, MinValue(0f), SuffixLabel("m")]
    [Tooltip("Distance the Flinch slides the player away from the attacker whose hit broke its Poise. 0 keeps the Flinch in place.")]
    private float flinchKnockbackDistance = 0.9f;
    [BoxGroup("Flinch"), SerializeField, MinValue(0.01f), SuffixLabel("s")]
    [Tooltip("Seconds the knockback slide lasts, easing out from its fastest at the hit.")]
    private float flinchKnockbackDuration = 0.3f;

    [BoxGroup("Death"), SerializeField, InlineProperty, HideLabel]
    [ValidateInput(nameof(IsDeathValid), "Death must contain a valid non-legacy AnimationClip.")]
    private ClipSettings death;
    [BoxGroup("Death"), SerializeField, MinValue(0f)]
    [Tooltip("Delay after the death animation ends before the player respawns.")]
    private float respawnDelay = 1f;

    [System.NonSerialized] private PlayerAttackData[] _runningCombo;

    public LinearMixerTransition LocomotionMixer => locomotionMixer.Transition;
    public MixerTransition2D LockedLocomotionMixer => lockedLocomotionMixer.Transition;
    public float LockedLocomotionSmoothTime => lockedLocomotionMixer.ParameterSmoothTime;
    public float DodgeIFrameStart => dodgeIFrameStart;
    public float DodgeIFrameEnd => dodgeIFrameEnd;
    public float DodgeInputBufferStart => dodgeInputBufferStart;
    public PlayerAttackData[] LightCombo => lightAttacks;
    public PlayerAttackData[] HeavyCombo => heavyAttacks;
    public PlayerAttackData[] RunningCombo => GetRunningCombo();
    public bool HasRunningAttack => runningAttack != null && runningAttack.IsPlayerValid;
    public ClipTransition BlockBegin => blockBegin.Transition;
    public ClipTransition BlockLoop => blockLoop.Transition;
    public ClipTransition BlockEnd => blockEnd.Transition;
    public AvatarMask GuardLegsMask => guardLegsMask;
    public float BlockPushbackDistance => blockPushbackDistance;
    public float BlockPushbackDuration => blockPushbackDuration;
    public ClipTransition ParryFront => parryFront.Transition;
    public ClipTransition ParryLeft => parryLeft.Transition;
    public ClipTransition ParryRight => parryRight.Transition;
    public float ParryRecovery => parryRecovery;
    public ClipTransition Flinch => flinch.Transition;
    public ClipTransition GuardBreak => guardBreak.Transition;
    public float FlinchInputBufferStart => flinchInputBufferStart;
    public float FlinchKnockbackDistance => flinchKnockbackDistance;
    public float FlinchKnockbackDuration => flinchKnockbackDuration;
    public ClipTransition Death => death.Transition;
    public float RespawnDelay => respawnDelay;
    public bool IsBlockConfigured => IsBlockValid && IsParryValid;
    public bool IsFlinchConfigured => IsFlinchValid;
    public bool IsGuardBreakConfigured => IsValidClip(guardBreak);

    private bool IsIFrameWindowOrdered => dodgeIFrameEnd > dodgeIFrameStart;
    private bool IsDodgeBufferAfterIFrames => dodgeInputBufferStart >= dodgeIFrameEnd;
    private bool IsLocomotionMixerValid => locomotionMixer != null && locomotionMixer.IsValid;
    private bool IsLockedLocomotionMixerValid => lockedLocomotionMixer != null && lockedLocomotionMixer.IsValid;
    private bool AreDodgeRollsValid =>
        dodgeRolls != null &&
        dodgeRolls.Length == 8 &&
        System.Array.TrueForAll(dodgeRolls, roll => roll != null && roll.IsValid);
    private bool IsLightComboValid => IsComboValid(lightAttacks);
    private bool IsHeavyComboValid => IsComboValid(heavyAttacks);
    private bool IsRunningAttackValid => runningAttack == null || runningAttack.IsPlayerValid;
    private bool IsBlockValid => IsValidClip(blockBegin) && IsValidClip(blockLoop) && IsValidClip(blockEnd);
    private bool IsParryValid => IsValidClip(parryFront) && IsValidClip(parryLeft) && IsValidClip(parryRight);
    private bool IsFlinchValid => IsValidClip(flinch);
    private bool IsDeathValid => IsValidClip(death);

    private void OnValidate()
    {
        _runningCombo = null;
        locomotionMixer?.ResetTransition();
        lockedLocomotionMixer?.ResetTransition();
        if (dodgeRolls != null)
        {
            foreach (var roll in dodgeRolls)
            {
                roll?.ResetTransition();
            }
        }
        blockBegin?.ResetTransition();
        blockLoop?.ResetTransition();
        blockEnd?.ResetTransition();
        parryFront?.ResetTransition();
        parryLeft?.ResetTransition();
        parryRight?.ResetTransition();
        flinch?.ResetTransition();
        guardBreak?.ResetTransition();
        death?.ResetTransition();
    }

    /// <summary>Returns the roll for a direction index, clockwise from forward.</summary>
    public ClipTransition GetDodgeRoll(int index)
    {
        return dodgeRolls[index].Transition;
    }

    private PlayerAttackData[] GetRunningCombo()
    {
        if (runningAttack == null)
        {
            return null;
        }
        if (_runningCombo == null || _runningCombo[0] != runningAttack)
        {
            _runningCombo = new[] { runningAttack };
        }
        return _runningCombo;
    }

    private static bool IsValidClip(ClipSettings settings) => settings != null && settings.IsValid;

    private static bool IsComboValid(PlayerAttackData[] combo) =>
        combo != null &&
        combo.Length > 0 &&
        System.Array.TrueForAll(combo, attack => attack != null && attack.IsPlayerValid);
}
