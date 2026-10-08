using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Shows the combat controls reference in the CompactHud document while the encounter is
/// idle and the player is alive. The fade is a USS transition on the hidden class.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class ControlHintsPresenter : MonoBehaviour
{
    private const string HintsName = "controlHints";
    private const string HiddenClass = "control-hints--hidden";

    [Tooltip("Publishes the encounter phase this reference observes.")]
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private ArenaManager arenaManager;
    [Tooltip("Publishes completed respawns and owns the player's Health.")]
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private PlayerStateMachine player;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private bool isVisible;

    private UIDocument _uiDocument;
    private Health _playerHealth;
    private VisualElement _hints;

    public bool IsVisible => isVisible;

    // Resolved directly: PlayerStateMachine assigns its Health property in its own Awake,
    // which can run after this component is enabled.
    private void Awake()
    {
        _uiDocument = GetComponent<UIDocument>();
        _playerHealth = player != null ? player.GetComponent<Health>() : null;
    }

    // UIDocument builds its tree in its own OnEnable, which runs before default-order scripts.
    private void OnEnable()
    {
        if (arenaManager == null || _playerHealth == null)
        {
            Debug.LogError("[ControlHintsPresenter] ArenaManager and PlayerStateMachine must be assigned.", this);
            return;
        }

        _hints = _uiDocument.rootVisualElement?.Q<VisualElement>(HintsName);
        if (_hints == null)
        {
            Debug.LogError($"[ControlHintsPresenter] The UIDocument has no '{HintsName}' element.", this);
            return;
        }

        arenaManager.PhaseChanged += HandlePhaseChanged;
        arenaManager.EncounterReset += RefreshVisibility;
        _playerHealth.Died += RefreshVisibility;
        player.Respawned += RefreshVisibility;
        RefreshVisibility();
    }

    private void OnDisable()
    {
        if (_hints == null)
        {
            return;
        }

        arenaManager.PhaseChanged -= HandlePhaseChanged;
        arenaManager.EncounterReset -= RefreshVisibility;
        _playerHealth.Died -= RefreshVisibility;
        player.Respawned -= RefreshVisibility;
        _hints.AddToClassList(HiddenClass);
        isVisible = false;
        _hints = null;
    }

    private void HandlePhaseChanged(EncounterPhase phase)
    {
        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        bool visible = arenaManager.Phase == EncounterPhase.Idle && !_playerHealth.IsDead;
        _hints.EnableInClassList(HiddenClass, !visible);
        if (visible != isVisible && showDebugLogs)
        {
            Debug.Log($"[ControlHintsPresenter] {(visible ? "Shown" : "Hidden")} (phase {arenaManager.Phase}, player dead {_playerHealth.IsDead}).", this);
        }

        isVisible = visible;
    }
}
