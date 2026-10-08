using System.Collections;
using DG.Tweening;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Fades the screen through black around a scene change. It fades in when its own scene opens;
/// <see cref="TryLoadScene"/> then fades out, loads the next scene behind the black and fades
/// that scene in before removing itself.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class SceneTransition : MonoBehaviour
{
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Unscaled seconds to fade in from black, both when this scene opens and after the next scene loads.")]
    private float fadeInDuration = 2.5f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Unscaled seconds to fade to black before the next scene loads.")]
    private float fadeOutDuration = 2f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Unscaled seconds held on black once the next scene has loaded, so its first-frame hitch stays hidden.")]
    private float blackHoldDuration = 0.6f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool IsTransitioning { get; private set; }

    private VisualElement _fade;
    private Tween _fadeTween;

    public float FadeInDuration => fadeInDuration;
    public float FadeOutDuration => fadeOutDuration;

    private void OnEnable()
    {
        _fade = GetComponent<UIDocument>().rootVisualElement.Q("screenFade");
        if (_fade == null)
        {
            Debug.LogError("[SceneTransition] The UIDocument is missing screenFade.", this);
            enabled = false;
        }
    }

    private void Start()
    {
        Fade(1f, 0f, fadeInDuration);
    }

    private void OnDisable()
    {
        _fadeTween?.Kill();
        _fadeTween = null;
    }

    /// <summary>
    /// Starts the transition to a scene in the build list. Returns false when the scene cannot be
    /// loaded or a transition is already running.
    /// </summary>
    public bool TryLoadScene(string sceneName)
    {
        if (IsTransitioning)
        {
            return false;
        }
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[SceneTransition] Scene '{sceneName}' is not in the build scene list.", this);
            return false;
        }

        IsTransitioning = true;
        StartCoroutine(RunTransition(sceneName));
        Log($"Loading {sceneName}.");
        return true;
    }

    private IEnumerator RunTransition(string sceneName)
    {
        // Outlives its own scene so the fade can finish over the next one.
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        // Loads behind the fade and holds activation, so the cut lands on black.
        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
        load.allowSceneActivation = false;
        yield return Fade(_fade.resolvedStyle.opacity, 1f, fadeOutDuration).WaitForCompletion();

        load.allowSceneActivation = true;
        yield return load;
        yield return new WaitForSecondsRealtime(blackHoldDuration);
        yield return Fade(1f, 0f, fadeInDuration).WaitForCompletion();
        Destroy(gameObject);
    }

    // Unscaled: the next scene may open with time slowed or stopped.
    private Tween Fade(float from, float to, float duration)
    {
        _fadeTween?.Kill();
        _fade.style.opacity = from;
        _fadeTween = DOTween.To(() => from, value => _fade.style.opacity = value, to, duration)
            .SetEase(Ease.InOutSine)
            .SetUpdate(true)
            .SetTarget(this);
        return _fadeTween;
    }

    private void Log(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[SceneTransition] {message}", this);
        }
    }
}
