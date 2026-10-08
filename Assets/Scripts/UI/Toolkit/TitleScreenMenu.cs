using System;
using System.Collections.Generic;
using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Runs the title menu: Begin hands the fight scene to the scene transition, Settings opens the
/// audio sliders, Quit exits the application. Web builds hide Quit, because quitting there
/// stops the player and leaves a dead canvas on the page.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class TitleScreenMenu : MonoBehaviour
{
    private const string HiddenPageClass = "title-page--hidden";
    private const string RevealClass = "title-reveal";
    private const string HiddenOptionClass = "title-option--hidden";

    [FoldoutGroup("Config"), SerializeField, Required]
    [Tooltip("Scene loaded by Begin. It must be enabled in the build scene list.")]
    private string gameplayScene = "Demo_Forge_01";

    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private SceneTransition transition;
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private AudioSettingsController audioSettings;
    [FoldoutGroup("References"), SerializeField, SceneObjectsOnly]
    [Tooltip("Optional ambience, faded in and out with the scene transition.")]
    private AudioLoopPlayer ambience;

    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    [Tooltip("Unscaled seconds after the scene opens before the menu starts to appear.")]
    private float revealDelay = 0.9f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    [Tooltip("Unscaled seconds each part of the menu takes to appear.")]
    private float revealDuration = 0.8f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    [Tooltip("Unscaled seconds between one part of the menu starting to appear and the next.")]
    private float revealStagger = 0.14f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    [Tooltip("Panel pixels each part of the menu slides in from the left.")]
    private float revealOffset = 28f;

    [FoldoutGroup("Audio"), SerializeField, InlineEditor, Tooltip("Optional UI-category cue for hovering a button.")]
    private AudioCue buttonHoverCue;
    [FoldoutGroup("Audio"), SerializeField, InlineEditor, Tooltip("Optional UI-category cue for activating a button.")]
    private AudioCue buttonPressCue;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool IsLoading { get; private set; }
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool IsSettingsOpen { get; private set; }

    private readonly List<Action> _unbind = new();
    private readonly List<VisualElement> _revealed = new();
    private VisualElement _root;
    private VisualElement _column;
    private VisualElement _mainPage;
    private VisualElement _settingsPage;
    private Button _beginButton;
    private Button _settingsButton;
    private AudioSettingsSliders _sliders;
    private Sequence _sequence;

    private void OnEnable()
    {
        _root = GetComponent<UIDocument>().rootVisualElement;
        _column = _root.Q("menuColumn");
        _mainPage = _root.Q("mainPage");
        _settingsPage = _root.Q("settingsPage");
        _root.Query(className: RevealClass).ToList(_revealed);

        _beginButton = BindButton("beginButton", BeginGame);
        _settingsButton = BindButton("settingsButton", OpenSettings);
        Button quitButton = BindButton("quitButton", Quit);
#if UNITY_WEBGL
        quitButton.AddToClassList(HiddenOptionClass);
#endif
        BindButton("backButton", CloseSettings);
        _sliders = new AudioSettingsSliders(_settingsPage, audioSettings);
        _unbind.Add(_sliders.Unbind);
        _root.RegisterCallback<NavigationCancelEvent>(HandleCancel);
        _unbind.Add(() => _root.UnregisterCallback<NavigationCancelEvent>(HandleCancel));

        IsLoading = false;
        ShowSettings(false);
    }

    private void Start()
    {
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;
        PlayEntrance();
        _beginButton.Focus();
        if (ambience != null)
        {
            ambience.FadeIn(transition.FadeInDuration);
        }
    }

    private void OnDisable()
    {
        _sequence?.Kill();
        _sequence = null;
        foreach (var unbind in _unbind)
        {
            unbind();
        }
        _unbind.Clear();
        _revealed.Clear();
    }

    /// <summary>
    /// Starts the transition to the gameplay scene once; further presses are ignored while it loads.
    /// </summary>
    public void BeginGame()
    {
        if (IsLoading || !transition.TryLoadScene(gameplayScene))
        {
            return;
        }

        IsLoading = true;
        _column.SetEnabled(false);
        if (ambience != null)
        {
            ambience.FadeOut(transition.FadeOutDuration);
        }
        Log($"Loading {gameplayScene}.");
    }

    /// <summary>
    /// Swaps the menu for the audio sliders, showing the saved volumes.
    /// </summary>
    public void OpenSettings()
    {
        if (IsLoading)
        {
            return;
        }

        ShowSettings(true);
        _sliders.Sync();
        _sliders.FocusFirst();
    }

    /// <summary>
    /// Returns from the audio sliders to the menu.
    /// </summary>
    public void CloseSettings()
    {
        ShowSettings(false);
        _settingsButton.Focus();
    }

    /// <summary>
    /// Exits the application, or leaves Play Mode in the Editor.
    /// </summary>
    public void Quit()
    {
        Log("Quit.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private Button BindButton(string buttonName, Action action)
    {
        var button = _root.Q<Button>(buttonName);
        EventCallback<PointerEnterEvent> hover = _ =>
        {
            if (!button.enabledInHierarchy || button.focusController.focusedElement == button)
            {
                return;
            }

            button.Focus();
            PlayCue(buttonHoverCue);
        };
        Action press = () =>
        {
            PlayCue(buttonPressCue);
            action();
        };
        button.RegisterCallback(hover);
        button.clicked += press;
        _unbind.Add(() =>
        {
            button.UnregisterCallback(hover);
            button.clicked -= press;
        });
        return button;
    }

    private void HandleCancel(NavigationCancelEvent evt)
    {
        if (!IsSettingsOpen)
        {
            return;
        }

        evt.StopPropagation();
        CloseSettings();
    }

    private void ShowSettings(bool open)
    {
        IsSettingsOpen = open;
        _mainPage.EnableInClassList(HiddenPageClass, open);
        _settingsPage.EnableInClassList(HiddenPageClass, !open);
    }

    // The scene transition fades the backdrop up first, then the title, rule and options arrive one after another.
    private void PlayEntrance()
    {
        _sequence?.Kill();
        _sequence = DOTween.Sequence().SetUpdate(true).SetTarget(this);

        float startTime = revealDelay;
        foreach (var element in _revealed)
        {
            element.style.opacity = 0f;
            _sequence.Insert(startTime, Reveal(element));
            startTime += revealStagger;
        }
    }

    private Tween Reveal(VisualElement element)
    {
        return DOTween.To(() => 0f, progress =>
        {
            element.style.opacity = progress;
            element.style.translate = new Translate((progress - 1f) * revealOffset, 0f);
        }, 1f, revealDuration).SetEase(Ease.OutCubic);
    }

    private void PlayCue(AudioCue cue)
    {
        if (cue == null || !AudioPlaybackService.HasInstance)
        {
            return;
        }

        AudioPlaybackService.Instance.Play(cue);
    }

    private void Log(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[TitleScreenMenu] {message}", this);
        }
    }
}
