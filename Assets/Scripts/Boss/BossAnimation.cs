using System;
using Animancer;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Focused Animancer wrapper for boss playback, mixer parameters, and owned
/// completion callbacks.
/// </summary>
[RequireComponent(typeof(AnimancerComponent))]
public class BossAnimation : MonoBehaviour
{
    [FoldoutGroup("Locomotion"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Smooths idle/walk/run blending without changing movement speed. Zero disables smoothing.")]
    private float locomotionSmoothTime = 0.15f;
    [FoldoutGroup("Locomotion"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Keeps the current gait during brief path recalculations, then blends toward idle if the wait continues.")]
    private float pathPendingGraceTime = 0.15f;

    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public float NormalizedTime => _currentState != null ? _currentState.NormalizedTime : 0f;

    /// <summary>True while the locomotion mixer or a turn clip is playing, false during attacks, stagger and death.</summary>
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool IsPlayingMovement => _isPlayingMovement;

    /// <summary>-1 while the left strafe clip plays, 1 for the right one, 0 otherwise.</summary>
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public float StrafeSide => _strafeSide;

    private AnimancerComponent _animancer;
    private AnimancerState _currentState;
    private LinearMixerState _locomotionState;
    private Action _completion;
    private float _locomotionBlendVelocity;
    private float _pathPendingTime;
    private bool _isPlayingMovement;
    private float _strafeSide;
    private float _nextLocomotionFade = float.NaN;
    private BossRunSettings _run;
    private AnimancerState _runLegs;
    private AnimancerState _runTorso;
    private float _runWeight;

    private const int RunLegsLayer = 1;
    private const int RunTorsoLayer = 2;

    private void Awake()
    {
        _animancer = GetComponent<AnimancerComponent>();
        // Animancer leaves Foot IK off. The clips are retargeted from a rig with different leg
        // proportions, so without it crouching attacks sink the feet up to 10 cm into the floor.
        _animancer.Graph.ApplyFootIK = true;
    }

    private void OnDisable()
    {
        ClearCompletion();
    }

    private void Update()
    {
        UpdateRun();
    }

    /// <summary>Sets the run layered over the locomotion mixer at speed. Null leaves the mixer on its own.</summary>
    public void SetRun(BossRunSettings run)
    {
        _run = run;
    }

    public void PlayLocomotion(LinearMixerTransition transition)
    {
        ClearCompletion();
        _currentState = float.IsNaN(_nextLocomotionFade)
            ? _animancer.Play(transition)
            : _animancer.Play(transition, _nextLocomotionFade);
        _nextLocomotionFade = float.NaN;
        _locomotionState = (LinearMixerState)_currentState;
        // The run covers speeds past the walk, so the walk's arms keep their authored pace.
        _locomotionState.ExtrapolateSpeed = _run == null;
        _isPlayingMovement = true;
        _strafeSide = 0f;
        _locomotionBlendVelocity = 0f;
        _pathPendingTime = 0f;
    }

    public void SetLocomotionSpeed(float speed)
    {
        _locomotionBlendVelocity = 0f;
        _pathPendingTime = 0f;
        if (_locomotionState != null)
        {
            _locomotionState.Parameter = speed;
        }
    }

    /// <summary>
    /// Filters pursuit speed for animation only. A bounded path wait preserves
    /// the current gait; deliberate stops bypass that grace period.
    /// </summary>
    public void SmoothLocomotionSpeed(float speed, bool pathPending)
    {
        if (_locomotionState == null || Time.deltaTime <= 0f)
        {
            return;
        }

        if (pathPending)
        {
            _pathPendingTime += Time.deltaTime;
            if (_pathPendingTime <= pathPendingGraceTime)
            {
                _locomotionBlendVelocity = 0f;
                return;
            }
            speed = 0f;
        }
        else
        {
            _pathPendingTime = 0f;
        }

        if (locomotionSmoothTime <= 0f)
        {
            _locomotionBlendVelocity = 0f;
            _locomotionState.Parameter = speed;
            return;
        }

        _locomotionState.Parameter = Mathf.SmoothDamp(_locomotionState.Parameter, speed,
            ref _locomotionBlendVelocity, locomotionSmoothTime, Mathf.Infinity, Time.deltaTime);
    }

    /// <summary>Loops a sideways walk in place of the locomotion mixer; <see cref="PlayLocomotion"/> returns to it.</summary>
    public void PlayStrafe(ClipTransition transition, float side)
    {
        ClearCompletion();
        _nextLocomotionFade = float.NaN;
        _locomotionState = null;
        _isPlayingMovement = true;
        _strafeSide = Mathf.Sign(side);
        _currentState = _animancer.Play(transition);
    }

    /// <summary>Overrides the blend of the next locomotion play only, e.g. out of an early attack release.</summary>
    public void SetNextLocomotionFade(float seconds)
    {
        _nextLocomotionFade = seconds;
    }

    public void PlayAttack(ClipTransition transition, Action onComplete)
    {
        PlayWithCompletion(transition, onComplete);
    }

    /// <summary>Overrides the live playback speed of the currently playing state, e.g. attack speed segments and holds.</summary>
    public void SetSpeed(float speed)
    {
        if (_currentState != null)
        {
            _currentState.Speed = speed;
        }
    }

    public void PlayTurn(ClipTransition transition, float speedMultiplier, Action onComplete)
    {
        AnimancerState state = PlayWithCompletion(transition, onComplete);
        state.Speed *= speedMultiplier;
        _isPlayingMovement = true;
    }

    public void PlayStagger(ClipTransition enterTransition, ClipTransition loopTransition, Action onLoopStarted)
    {
        PlayWithCompletion(enterTransition, () =>
        {
            PlayStaggerLoop(loopTransition);
            onLoopStarted?.Invoke();
        });
    }

    public void PlayStaggerExit(ClipTransition transition, Action onComplete)
    {
        PlayWithCompletion(transition, onComplete);
    }

    public void PlayStaggerHit(ClipTransition hitTransition, ClipTransition loopTransition)
    {
        if (hitTransition == null) return;
        PlayWithCompletion(hitTransition, () => PlayStaggerLoop(loopTransition));
    }

    public void PlayIntro(ClipTransition transition, Action onComplete)
    {
        PlayWithCompletion(transition, onComplete);
    }

    public void PlayEnrage(ClipTransition transition, Action onComplete)
    {
        PlayWithCompletion(transition, onComplete);
    }

    public void PlayDeath(ClipTransition transition, Action onComplete)
    {
        PlayWithCompletion(transition, onComplete);
    }

    public void ClearCompletion()
    {
        if (_currentState != null) _currentState.Events(this).OnEnd = null;
        _completion = null;
    }

    /// <summary>Seconds remaining until the active animation's authored end.</summary>
    public float GetRemainingDuration()
    {
        return _currentState != null ? _currentState.RemainingDuration : 0f;
    }

    // The run follows the mixer's smoothed speed, and blends out whenever anything else plays.
    private void UpdateRun()
    {
        if (_run == null)
        {
            return;
        }

        float speed = _locomotionState != null ? _locomotionState.Parameter : 0f;
        float target = _locomotionState != null ? _run.Weight(speed) : 0f;
        if (target <= 0f && _runWeight <= 0f)
        {
            return;
        }

        if (_runLegs == null)
        {
            CreateRunStates();
        }

        _runWeight = Mathf.MoveTowards(_runWeight, target, Time.deltaTime / _run.BlendTime);
        _animancer.Layers[RunLegsLayer].Weight = _runWeight;
        _animancer.Layers[RunTorsoLayer].Weight = _runWeight * _run.TorsoWeight;
        if (_locomotionState != null)
        {
            float playbackSpeed = _run.PlaybackSpeed(speed);
            _runLegs.Speed = playbackSpeed;
            _runTorso.Speed = playbackSpeed;
        }
    }

    // Two states of one clip, started together and given the same speed, so the torso stays in step with the legs.
    private void CreateRunStates()
    {
        _runLegs = PlayRunLayer(RunLegsLayer, _run.LegsMask);
        _runTorso = PlayRunLayer(RunTorsoLayer, _run.TorsoMask);
    }

    private AnimancerState PlayRunLayer(int index, AvatarMask mask)
    {
        AnimancerLayer layer = _animancer.Layers[index];
        // An unmasked layer would replace the whole pose, so a missing mask keeps its layer at no weight.
        layer.Mask = mask;
        var state = new ClipState(_run.Clip);
        layer.Play(state);
        layer.Weight = 0f;
        return state;
    }

    private AnimancerState PlayWithCompletion(ClipTransition transition, Action onComplete)
    {
        ClearCompletion();
        _nextLocomotionFade = float.NaN;
        _locomotionState = null;
        _isPlayingMovement = false;
        _strafeSide = 0f;
        _completion = onComplete;
        _currentState = _animancer.Play(transition);
        _currentState.Events(this).OnEnd = HandleCompleted;
        return _currentState;
    }

    private void PlayStaggerLoop(ClipTransition transition)
    {
        ClearCompletion();
        _locomotionState = null;
        _isPlayingMovement = false;
        _strafeSide = 0f;
        _currentState = _animancer.Play(transition);
    }

    private void HandleCompleted()
    {
        Action callback = _completion;
        ClearCompletion();
        callback?.Invoke();
    }
}
