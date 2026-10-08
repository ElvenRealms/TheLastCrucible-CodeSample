#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Provides F1/F2 keyboard toggles for player and boss god mode. Compiled into the
/// Editor and development builds only, so release builds never ship it.
/// </summary>
public sealed class GodModeDebugControls : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindAnyObjectByType<GodModeDebugControls>() != null) return;

        var holder = new GameObject(nameof(GodModeDebugControls));
        DontDestroyOnLoad(holder);
        holder.AddComponent<GodModeDebugControls>();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.f1Key.wasPressedThisFrame)
        {
            TogglePlayerGodMode();
        }
        if (keyboard.f2Key.wasPressedThisFrame)
        {
            ToggleBossGodMode();
        }
    }

    private void TogglePlayerGodMode()
    {
        var player = FindAnyObjectByType<PlayerStateMachine>();
        ToggleGodMode(player != null ? player.Health : null, "Player", player);
    }

    private void ToggleBossGodMode()
    {
        var boss = FindAnyObjectByType<BossStateMachine>();
        ToggleGodMode(boss != null ? boss.Health : null, "Boss", boss);
    }

    private void ToggleGodMode(Health health, string actorLabel, Object context)
    {
        if (health == null)
        {
            Debug.LogWarning($"[GodModeDebugControls] {actorLabel} god mode could not be toggled because no target was found.", context);
            return;
        }
        if (health.IsDead)
        {
            Debug.LogWarning($"[GodModeDebugControls] {actorLabel} god mode could not be toggled because the target is dead.", context);
            return;
        }

        health.SetGodMode(!health.GodModeEnabled);
        var status = health.GodModeEnabled ? "enabled" : "disabled";
        Debug.Log($"[GodModeDebugControls] {actorLabel} god mode {status} for '{health.name}'.", context);
    }
}
#endif
