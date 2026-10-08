using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Displays the pause controller's state using the dark fantasy menu.</summary>
[RequireComponent(typeof(UIDocument))]
public class ToolkitPauseMenu : MonoBehaviour
{
    [FoldoutGroup("References"), SerializeField, Required]
    private PauseMenuController pauseController;
    [FoldoutGroup("References"), SerializeField, Required]
    private AudioSettingsController audioSettings;
    [FoldoutGroup("References"), SerializeField, Required]
    private UIDocument gameplayHud;

    [FoldoutGroup("Audio"), SerializeField, InlineEditor, Tooltip("Optional UI-category cue for hovering a button.")]
    private AudioCue buttonHoverCue;
    [FoldoutGroup("Audio"), SerializeField, InlineEditor, Tooltip("Optional UI-category cue for activating a button.")]
    private AudioCue buttonPressCue;
    [FoldoutGroup("Audio"), SerializeField, InlineEditor, Tooltip("Optional UI-category cue for changing an audio slider.")]
    private AudioCue sliderChangeCue;
    [FoldoutGroup("Audio"), SerializeField, MinValue(0f), Tooltip("Minimum unscaled seconds between slider sounds while dragging.")]
    private float sliderSoundInterval = 0.08f;

    private readonly List<Action> _unbind = new();
    private VisualElement _root;
    private VisualElement _pausePage;
    private VisualElement _audioPage;
    private Label _title;
    private Label _hint;
    private Button _resume;
    private Button _audio;
    private AudioSettingsSliders _audioSliders;
    private bool _visible;
    private bool _audioVisible;
    private StyleEnum<Visibility> _hudVisibility;
    private float _nextSliderSoundTime;

    private void OnEnable()
    {
        _root = GetComponent<UIDocument>().rootVisualElement.Q("pauseOverlay");
        _pausePage = _root.Q("pausePage");
        _audioPage = _root.Q("audioPage");
        _title = _root.Q<Label>("menuTitle");
        _hint = _root.Q<Label>("menuHint");
        _resume = BindButton("resumeButton", pauseController.Resume);
        _audio = BindButton("audioButton", pauseController.OpenAudioMenu);
        BindButton("quitButton", pauseController.Quit);
        BindButton("backButton", pauseController.CloseAudioMenu);
        _audioSliders = new AudioSettingsSliders(_audioPage, audioSettings);
        _audioSliders.Changed += PlaySliderSound;
        _unbind.Add(_audioSliders.Unbind);
        _root.style.display = DisplayStyle.None;
        _visible = false;
        _audioVisible = false;
        _nextSliderSoundTime = Time.unscaledTime;
        _hudVisibility = gameplayHud.rootVisualElement.style.visibility;
    }

    private void OnDisable()
    {
        foreach (var unbind in _unbind)
        {
            unbind();
        }
        _unbind.Clear();
        if (_root != null)
        {
            _root.style.display = DisplayStyle.None;
        }
        if (gameplayHud != null && gameplayHud.rootVisualElement != null)
        {
            gameplayHud.rootVisualElement.style.visibility = _hudVisibility;
        }
    }

    private void Update()
    {
        bool visible = pauseController.IsMenuVisible;
        bool audioVisible = visible && pauseController.IsAudioMenuOpen;
        if (_visible == visible && _audioVisible == audioVisible)
        {
            return;
        }

        bool returningFromAudio = _audioVisible && !audioVisible;
        _visible = visible;
        _audioVisible = audioVisible;
        _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        gameplayHud.rootVisualElement.style.visibility = visible ? Visibility.Hidden : _hudVisibility;
        _pausePage.style.display = audioVisible ? DisplayStyle.None : DisplayStyle.Flex;
        _audioPage.style.display = audioVisible ? DisplayStyle.Flex : DisplayStyle.None;
        _title.text = audioVisible ? "AUDIO" : "PAUSED";
        _hint.text = audioVisible ? "ESC / B   BACK" : "ESC / START   RESUME";
        if (!visible)
        {
            return;
        }

        if (audioVisible)
        {
            _audioSliders.Sync();
            _audioSliders.FocusFirst();
        }
        else
        {
            (returningFromAudio ? _audio : _resume).Focus();
        }
    }

    private Button BindButton(string name, Action action)
    {
        var button = _root.Q<Button>(name);
        EventCallback<PointerEnterEvent> focus = _ =>
        {
            if (!button.enabledInHierarchy)
            {
                return;
            }

            button.Focus();
            PlayCue(buttonHoverCue);
        };
        EventCallback<PointerLeaveEvent> blur = _ => button.Blur();
        EventCallback<ClickEvent> click = evt =>
        {
            if (evt.button == 0 && button.worldBound.Contains(evt.position))
            {
                PlayCue(buttonPressCue);
                action();
            }
        };
        EventCallback<NavigationSubmitEvent> submit = evt =>
        {
            evt.StopImmediatePropagation();
            PlayCue(buttonPressCue);
            action();
        };
        button.RegisterCallback(focus);
        button.RegisterCallback(blur);
        button.RegisterCallback(click);
        button.RegisterCallback(submit, TrickleDown.TrickleDown);
        _unbind.Add(() =>
        {
            button.UnregisterCallback(focus);
            button.UnregisterCallback(blur);
            button.UnregisterCallback(click);
            button.UnregisterCallback(submit, TrickleDown.TrickleDown);
        });
        return button;
    }

    private void PlaySliderSound()
    {
        if (Time.unscaledTime < _nextSliderSoundTime)
        {
            return;
        }

        _nextSliderSoundTime = Time.unscaledTime + sliderSoundInterval;
        PlayCue(sliderChangeCue);
    }

    private void PlayCue(AudioCue cue)
    {
        if (cue == null || AudioPlaybackService.Instance == null)
        {
            return;
        }

        AudioPlaybackService.Instance.Play(cue);
    }
}
