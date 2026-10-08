using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

/// <summary>
/// Records each Play Mode session to video while Tools/Portfolio/Record Play Mode is checked. Settings and the
/// events to log come from <see cref="PortfolioConfig"/>. Each take goes to Recordings/&lt;start time&gt;/ as
/// take.mp4 plus events.jsonl, which lists each watched event with its time in the video, for finding moments
/// while editing. The frame rate is fixed: when rendering can't keep up, the game slows down instead of dropping
/// frames, so video time follows rendered frames rather than game time (which hit stop scales). Capturing audio
/// silences the Editor's own playback until the take ends; that is how Unity's AudioRenderer works.
/// </summary>
[InitializeOnLoad]
public static class PortfolioRecorder
{
    private const string MenuPath = "Tools/Portfolio/Record Play Mode";
    private const string EnabledPrefKey = "BossFightDemo.PortfolioRecorder.Enabled";
    private const float RescanInterval = 0.5f;

    private static readonly List<EventWatcher> watchers = new List<EventWatcher>();
    private static RecorderController _controller;
    private static string _takeDir;
    private static float _frameRate;
    private static int _startFrame;
    private static float _nextRescanTime;

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(EnabledPrefKey);
        set => EditorPrefs.SetBool(EnabledPrefKey, value);
    }

    static PortfolioRecorder()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem(MenuPath)]
    private static void ToggleEnabled()
    {
        Enabled = !Enabled;
    }

    [MenuItem(MenuPath, true)]
    private static bool CanToggleEnabled()
    {
        Menu.SetChecked(MenuPath, Enabled);
        // A take starts on entering Play Mode, so toggling mid-session would do nothing until the next one.
        return !EditorApplication.isPlaying;
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode && Enabled)
        {
            Begin();
        }
        else if (change == PlayModeStateChange.ExitingPlayMode && _controller != null)
        {
            End();
        }
    }

    private static void Begin()
    {
        var config = PortfolioConfig.Load(out var error);
        if (config == null)
        {
            Debug.LogError($"Portfolio recording skipped: {error}");
            return;
        }
        var projectRoot = Path.GetDirectoryName(Application.dataPath);
        _takeDir = Path.GetFullPath(Path.Combine(projectRoot, config.outputDir, DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        Directory.CreateDirectory(_takeDir);
        _controller = new RecorderController(CreateSettings(config, Path.Combine(_takeDir, "take")));
        _controller.PrepareRecording();
        if (!_controller.StartRecording())
        {
            Debug.LogError("Portfolio recording failed to start; see the Recorder's console messages.");
            _controller = null;
            return;
        }
        _frameRate = config.frameRate;
        _startFrame = Time.frameCount;
        foreach (var spec in config.events)
        {
            AddWatcher(spec);
        }
        _nextRescanTime = 0f;
        EditorApplication.update += OnUpdate;
    }

    private static void End()
    {
        EditorApplication.update -= OnUpdate;
        foreach (var watcher in watchers)
        {
            watcher.Unhook();
        }
        watchers.Clear();
        _controller.StopRecording();
        _controller = null;
        Debug.Log($"Portfolio take saved to {_takeDir}");
    }

    private static RecorderControllerSettings CreateSettings(PortfolioConfig config, string outputFile)
    {
        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = "Portfolio Take";
        movie.Enabled = true;
        movie.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
        };
        movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = config.width, OutputHeight = config.height };
        movie.AudioInputSettings.PreserveAudio = true;
        movie.OutputFile = outputFile;

        var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        settings.AddRecorderSettings(movie);
        settings.SetRecordModeToManual();
        settings.FrameRatePlayback = FrameRatePlayback.Constant;
        settings.FrameRate = config.frameRate;
        settings.CapFrameRate = true;
        return settings;
    }

    private static void AddWatcher(PortfolioEvent spec)
    {
        var watcher = EventWatcher.Create(spec, (source, description) => OnRaised(spec, source, description), out var error);
        if (watcher == null)
        {
            Debug.LogWarning($"Portfolio event '{spec.label}' is not logged: {error}");
            return;
        }
        watchers.Add(watcher);
    }

    private static void OnUpdate()
    {
        if (!EditorApplication.isPlaying || Time.unscaledTime < _nextRescanTime)
        {
            return;
        }
        _nextRescanTime = Time.unscaledTime + RescanInterval;
        foreach (var watcher in watchers)
        {
            watcher.Rescan();
        }
    }

    private static void OnRaised(PortfolioEvent spec, string source, string description)
    {
        if (!spec.Matches(description))
        {
            return;
        }
        var entry = new
        {
            time = Math.Round((Time.frameCount - _startFrame) / _frameRate, 3),
            label = spec.label,
            source,
            @event = description,
        };
        File.AppendAllText(Path.Combine(_takeDir, "events.jsonl"), JsonConvert.SerializeObject(entry) + "\n");
    }
}
