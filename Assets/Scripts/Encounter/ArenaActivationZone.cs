using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Tracks which player may activate the encounter and forwards Interact intent
/// to the scene-local arena manager.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ArenaActivationZone : MonoBehaviour
{
    [FoldoutGroup("Config"), SerializeField, Required, SceneObjectsOnly]
    private ArenaManager arenaManager;
    [FoldoutGroup("Config"), SerializeField, Required, AssetsOnly]
    private InputReader inputReader;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private PlayerStateMachine eligiblePlayer;

    public ArenaManager ArenaManager => arenaManager;
    public PlayerStateMachine EligiblePlayer => eligiblePlayer;

    private readonly HashSet<Collider> eligibleColliders = new HashSet<Collider>();

    private void Awake()
    {
        Collider activationZone = GetComponent<Collider>();
        if (!activationZone.isTrigger)
        {
            Debug.LogError("ArenaActivationZone requires its Collider to be a trigger.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (inputReader != null) inputReader.Interact += HandleInteract;
    }

    private void OnDisable()
    {
        if (inputReader != null) inputReader.Interact -= HandleInteract;
        eligibleColliders.Clear();
        eligiblePlayer = null;
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerStateMachine player = other.GetComponentInParent<PlayerStateMachine>();
        if (player == null || eligiblePlayer != null && player != eligiblePlayer) return;

        eligiblePlayer = player;
        eligibleColliders.Add(other);
        if (showDebugLogs) Debug.Log("[ArenaActivationZone] Player became eligible.", this);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!eligibleColliders.Remove(other) || eligibleColliders.Count > 0) return;

        eligiblePlayer = null;
        if (showDebugLogs) Debug.Log("[ArenaActivationZone] Player is no longer eligible.", this);
    }

    private void HandleInteract()
    {
        if (arenaManager != null) arenaManager.TryActivate(eligiblePlayer);
    }
}
