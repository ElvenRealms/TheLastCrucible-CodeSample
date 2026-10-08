using UnityEngine;

/// <summary>Schedules one encounter voice at a time; urgent lines replace the current voice lease.</summary>
public sealed class BossVoicePlayback
{
    private readonly Health _health;
    private readonly Transform _emitter;
    private readonly BossVoiceSet _voiceSet;
    private AudioVoiceHandle _activeVoiceHandle;
    private AudioCue _lastPlayedCue;
    private float _nextMidBattleTime;
    private bool _isEncounterActive;
    private bool _lowHealthPlayed;
    private bool _deathRequested;

    public AudioVoiceHandle ActiveVoiceHandle => _activeVoiceHandle;
    public AudioCue LastPlayedCue => _lastPlayedCue;
    public bool IsEncounterActive => _isEncounterActive;
    public bool LowHealthPlayed => _lowHealthPlayed;
    public bool DeathRequested => _deathRequested;

    public BossVoicePlayback(Health health, Transform emitter, BossVoiceSet voiceSet)
    {
        _health = health;
        _emitter = emitter;
        _voiceSet = voiceSet;
    }

    public void Tick()
    {
        if (!_isEncounterActive || _deathRequested || _health == null || _health.IsDead || Mathf.Approximately(Time.timeScale, 0f))
        {
            return;
        }
        if (_voiceSet == null || _voiceSet.MidBattleCue == null || Time.time < _nextMidBattleTime)
        {
            return;
        }
        ScheduleNextMidBattleVoice();
        PlayVoice(_voiceSet.MidBattleCue, false);
    }

    public void Activate()
    {
        if (_health == null || _health.IsDead || _isEncounterActive)
        {
            return;
        }
        Deactivate();
        _isEncounterActive = true;
        ScheduleNextMidBattleVoice();
        PlayVoice(_voiceSet != null ? _voiceSet.BattleStartCue : null, false);
    }

    public void Deactivate()
    {
        StopCurrentVoice();
        _isEncounterActive = false;
        _lowHealthPlayed = false;
        _deathRequested = false;
        _nextMidBattleTime = 0f;
    }

    public void HealthChanged(float currentHealth, float maximumHealth)
    {
        if (_health == null || _health.IsDead)
        {
            return;
        }
        if (_deathRequested)
        {
            Deactivate();
            return;
        }
        if (!_isEncounterActive || _lowHealthPlayed || maximumHealth <= 0f)
        {
            return;
        }
        float threshold = _voiceSet != null ? _voiceSet.LowHealthThreshold : 0f;
        if (currentHealth > maximumHealth * threshold)
        {
            return;
        }
        _lowHealthPlayed = true;
        AudioCue lowHealthCue = _voiceSet != null ? _voiceSet.LowHealthCue : null;
        if (lowHealthCue != null && lowHealthCue.CountValidClips() > 0)
        {
            PlayVoice(lowHealthCue, true);
        }
    }

    public void Enrage()
    {
        if (_isEncounterActive && !_deathRequested)
        {
            PlayVoice(_voiceSet != null ? _voiceSet.EnrageCue : null, true);
        }
    }

    public void Die()
    {
        _isEncounterActive = false;
        _deathRequested = true;
        StopCurrentVoice();
        PlayVoice(_voiceSet != null ? _voiceSet.DeathCue : null, false);
    }

    private void ScheduleNextMidBattleVoice()
    {
        if (_voiceSet == null || _voiceSet.MidBattleCue == null)
        {
            _nextMidBattleTime = float.PositiveInfinity;
            return;
        }
        Vector2 intervalRange = _voiceSet.MidBattleIntervalRange;
        float minimum = Mathf.Max(0f, intervalRange.x);
        float maximum = Mathf.Max(minimum, intervalRange.y);
        _nextMidBattleTime = maximum > 0f ? Time.time + Random.Range(minimum, maximum) : float.PositiveInfinity;
    }

    private void PlayVoice(AudioCue cue, bool interruptActiveVoice)
    {
        if (interruptActiveVoice)
        {
            StopCurrentVoice();
        }
        else if (_activeVoiceHandle != null && _activeVoiceHandle.IsValid)
        {
            return;
        }
        if (cue == null || cue.CountValidClips() == 0 || !AudioPlaybackService.HasInstance)
        {
            return;
        }
        AudioPlaybackService playbackService = AudioPlaybackService.Instance;
        if (playbackService == null)
        {
            return;
        }
        _activeVoiceHandle = cue.SpatialMode == AudioSpatialMode.Spatial
            ? playbackService.PlayAtPosition(cue, _emitter.position)
            : playbackService.Play(cue);
        if (_activeVoiceHandle != null)
        {
            _lastPlayedCue = cue;
        }
    }

    private void StopCurrentVoice()
    {
        _activeVoiceHandle?.Stop();
        _activeVoiceHandle = null;
    }
}
