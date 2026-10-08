using Animancer;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Animation transitions for the boss. The locomotion mixer's parameter is
/// the planar speed in m/s, so its thresholds should match the movement
/// config's speed.
/// </summary>
[CreateAssetMenu(fileName = "BossAnimations", menuName = "BossFight/Boss Animation Data")]
public class BossAnimationData : ScriptableObject
{
    [FoldoutGroup("Locomotion"), SerializeField, InlineProperty, HideLabel]
    [ValidateInput(nameof(IsLocomotionMixerValid), "Locomotion mixer must contain valid clips and thresholds.")]
    private LinearMixerSettings locomotionMixer;

    [FoldoutGroup("Run"), SerializeField, InlineProperty, HideLabel]
    [ValidateInput(nameof(IsRunUsable), "Run needs a looping clip, a Legs Mask and a Match Speed above Blend From Speed, or no clip at all.")]
    private BossRunSettings run = new BossRunSettings();

    [FoldoutGroup("Strafe"), SerializeField, LabelText("Left")]
    [ValidateInput(nameof(IsStrafeLeftClipValid), "Left must contain a valid looping non-legacy AnimationClip.")]
    private ClipSettings strafeLeftClip;

    [FoldoutGroup("Strafe"), SerializeField, LabelText("Right")]
    [ValidateInput(nameof(IsStrafeRightClipValid), "Right must contain a valid looping non-legacy AnimationClip.")]
    private ClipSettings strafeRightClip;

    [FoldoutGroup("Stagger"), SerializeField, LabelText("Enter")]
    [FormerlySerializedAs("staggerClip")]
    [ValidateInput(nameof(IsStaggerEnterClipValid), "Enter must contain a valid non-looping non-legacy AnimationClip.")]
    private ClipSettings staggerEnterClip;

    [FoldoutGroup("Stagger"), SerializeField, LabelText("Loop")]
    [ValidateInput(nameof(IsStaggerLoopClipValid), "Loop must contain a valid looping non-legacy AnimationClip.")]
    private ClipSettings staggerLoopClip;

    [FoldoutGroup("Stagger"), SerializeField, LabelText("Exit")]
    [ValidateInput(nameof(IsStaggerExitClipValid), "Exit must contain a valid non-looping non-legacy AnimationClip.")]
    private ClipSettings staggerExitClip;

    [FoldoutGroup("Stagger"), SerializeField, LabelText("Hit")]
    [Tooltip("Optional flinch during the stagger loop. Returns to the loop without extending its deadline.")]
    private ClipSettings staggerHitClip;

    [FormerlySerializedAs("staggerDuration")]
    [FoldoutGroup("Stagger"), SerializeField, MinValue(0f), LabelText("Loop Duration"), SuffixLabel("seconds")]
    [Tooltip("Seconds the boss remains in the authored stagger loop after the enter clip completes.")]
    private float staggerLoopDuration = 2f;

    [FoldoutGroup("Intro"), SerializeField, InlineProperty, HideLabel]
    [ValidateInput(nameof(IsIntroClipUsable), "Intro must be empty or contain a valid non-legacy AnimationClip.")]
    [Tooltip("Played once, the first time the encounter is activated, before the boss starts to pursue. Leave empty for a boss that pursues at once.")]
    private ClipSettings introClip;

    [FoldoutGroup("Enrage"), SerializeField, InlineProperty, HideLabel]
    [ValidateInput(nameof(IsEnrageClipUsable), "Enrage must be empty or contain a valid non-legacy AnimationClip.")]
    [Tooltip("Played once when the boss becomes enraged. Leave empty to become enraged without a beat.")]
    private ClipSettings enrageClip;

    [FoldoutGroup("Death"), SerializeField, InlineProperty, HideLabel]
    [ValidateInput(nameof(IsDeathClipValid), "Death must contain a valid non-legacy AnimationClip.")]
    private ClipSettings deathClip;

    /// <summary>False until both strafe clips are valid; the boss then holds position instead of strafing.</summary>
    public bool HasStrafeClips => IsStrafeLeftClipValid && IsStrafeRightClipValid;
    public ClipTransition StaggerEnterClip => staggerEnterClip.Transition;
    public ClipTransition StaggerLoopClip => staggerLoopClip.Transition;
    public ClipTransition StaggerExitClip => staggerExitClip.Transition;
    public ClipTransition StaggerHitClip => staggerHitClip != null && staggerHitClip.IsValid
        ? staggerHitClip.Transition : null;
    public LinearMixerTransition LocomotionMixer => locomotionMixer.Transition;
    /// <summary>Null when no run is authored; the walk then speeds up to cover faster movement.</summary>
    public BossRunSettings Run => run != null && run.IsValid ? run : null;
    public ClipTransition StrafeLeftClip => strafeLeftClip.Transition;
    public ClipTransition StrafeRightClip => strafeRightClip.Transition;
    public float StaggerLoopDuration => staggerLoopDuration;
    public ClipTransition DeathClip => deathClip.Transition;
    /// <summary>False when no intro clip is authored; the boss then pursues as soon as the encounter is activated.</summary>
    public bool HasIntroClip => introClip != null && introClip.IsValid;
    public ClipTransition IntroClip => introClip.Transition;
    /// <summary>False when no enrage clip is authored; the boss then becomes enraged without a beat.</summary>
    public bool HasEnrageClip => enrageClip != null && enrageClip.IsValid;
    public ClipTransition EnrageClip => enrageClip.Transition;

    private bool IsLocomotionMixerValid => locomotionMixer != null && locomotionMixer.IsValid;

    private bool IsRunUsable => run == null || run.Clip == null || run.IsValid;

    private bool IsStrafeLeftClipValid =>
        strafeLeftClip != null && strafeLeftClip.IsValid && strafeLeftClip.IsLooping;

    private bool IsStrafeRightClipValid =>
        strafeRightClip != null && strafeRightClip.IsValid && strafeRightClip.IsLooping;

    private bool IsStaggerEnterClipValid =>
        staggerEnterClip != null && staggerEnterClip.IsValid && !staggerEnterClip.IsLooping;

    private bool IsStaggerLoopClipValid =>
        staggerLoopClip != null && staggerLoopClip.IsValid && staggerLoopClip.IsLooping;

    private bool IsStaggerExitClipValid =>
        staggerExitClip != null && staggerExitClip.IsValid && !staggerExitClip.IsLooping;

    private bool IsDeathClipValid => deathClip != null && deathClip.IsValid;

    private bool IsIntroClipUsable => introClip == null || introClip.Clip == null || HasIntroClip;

    private bool IsEnrageClipUsable => enrageClip == null || enrageClip.Clip == null || HasEnrageClip;

    private void OnValidate()
    {
        locomotionMixer?.ResetTransition();
        strafeLeftClip?.ResetTransition();
        strafeRightClip?.ResetTransition();
        staggerEnterClip?.ResetTransition();
        staggerLoopClip?.ResetTransition();
        staggerExitClip?.ResetTransition();
        staggerHitClip?.ResetTransition();
        deathClip?.ResetTransition();
        introClip?.ResetTransition();
        enrageClip?.ResetTransition();
    }
}
