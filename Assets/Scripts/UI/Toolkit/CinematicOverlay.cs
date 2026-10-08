using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen layer of the cinematic camera: letterbox bars, the controls panel with a status line, and a
/// short toast. Fades are USS transitions on the hidden classes. Timers run in real time, so they keep
/// working while time is frozen.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class CinematicOverlay : MonoBehaviour
{
    private const string RootName = "cinematicOverlay";
    private const string HiddenRootClass = "cinematic-overlay--hidden";
    private const string HiddenBarClass = "letterbox-bar--hidden";
    private const string HiddenHelpClass = "cinematic-help--hidden";
    private const string HiddenToastClass = "cinematic-toast--hidden";
    private const string CapturingClass = "cinematic-overlay--capturing";

    [FoldoutGroup("Config"), SerializeField, Range(1.5f, 3f)]
    [Tooltip("Picture aspect ratio the letterbox bars crop to.")]
    private float letterboxAspect = 2.39f;
    [FoldoutGroup("Config"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Real seconds a toast stays up before fading.")]
    private float toastDuration = 1.6f;

    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private bool isShown;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private bool isHelpVisible;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private bool isLetterboxed;

    private VisualElement _root;
    private VisualElement _barTop;
    private VisualElement _barBottom;
    private VisualElement _help;
    private Label _status;
    private Label _toast;
    private float _toastHideTime;
    private float _barPercent = -1f;

    public bool IsHelpVisible => isHelpVisible;
    public bool IsLetterboxed => isLetterboxed;

    // UIDocument builds its tree in its own OnEnable, which runs before default-order scripts.
    private void OnEnable()
    {
        VisualElement documentRoot = GetComponent<UIDocument>().rootVisualElement;
        _root = documentRoot?.Q<VisualElement>(RootName);
        if (_root == null)
        {
            Debug.LogError($"[CinematicOverlay] The UIDocument has no '{RootName}' element.", this);
            return;
        }

        _barTop = _root.Q<VisualElement>("letterboxTop");
        _barBottom = _root.Q<VisualElement>("letterboxBottom");
        _help = _root.Q<VisualElement>("help");
        _status = _root.Q<Label>("status");
        _toast = _root.Q<Label>("toast");
        Show(false);
    }

    private void Update()
    {
        if (!isShown || _root == null)
        {
            return;
        }

        if (isLetterboxed)
        {
            UpdateBarHeight();
        }
        if (Time.unscaledTime >= _toastHideTime)
        {
            _toast.AddToClassList(HiddenToastClass);
        }
    }

    /// <summary>
    /// Shows or hides the whole overlay. Hiding also clears the letterbox, help and toast.
    /// </summary>
    public void Show(bool shown)
    {
        if (_root == null)
        {
            return;
        }

        isShown = shown;
        _root.EnableInClassList(HiddenRootClass, !shown);
        if (!shown)
        {
            SetLetterbox(false);
            SetHelpVisible(false);
            _toast.AddToClassList(HiddenToastClass);
        }
    }

    public void SetLetterbox(bool letterboxed)
    {
        if (_root == null)
        {
            return;
        }

        isLetterboxed = letterboxed;
        _barTop.EnableInClassList(HiddenBarClass, !letterboxed);
        _barBottom.EnableInClassList(HiddenBarClass, !letterboxed);
        _barPercent = -1f;
    }

    public void SetHelpVisible(bool visible)
    {
        if (_root == null)
        {
            return;
        }

        isHelpVisible = visible;
        _help.EnableInClassList(HiddenHelpClass, !visible);
    }

    /// <summary>
    /// Removes the help panel and toast instantly, skipping their fades, so a screenshot shows only
    /// the picture and letterbox.
    /// </summary>
    public void SetCapturing(bool capturing)
    {
        _root?.EnableInClassList(CapturingClass, capturing);
    }

    public void SetStatus(string status)
    {
        if (_status != null)
        {
            _status.text = status;
        }
    }

    public void ShowToast(string message)
    {
        if (_toast == null)
        {
            return;
        }

        _toast.text = message;
        _toast.RemoveFromClassList(HiddenToastClass);
        _toastHideTime = Time.unscaledTime + toastDuration;
    }

    // The bar height depends on the live screen aspect, which USS can't express, so it is set inline.
    private void UpdateBarHeight()
    {
        float screenAspect = Screen.height > 0 ? (float)Screen.width / Screen.height : letterboxAspect;
        float percent = Mathf.Max(0f, (1f - screenAspect / letterboxAspect) * 50f);
        if (Mathf.Approximately(percent, _barPercent))
        {
            return;
        }

        _barPercent = percent;
        _barTop.style.height = Length.Percent(percent);
        _barBottom.style.height = Length.Percent(percent);
    }
}
