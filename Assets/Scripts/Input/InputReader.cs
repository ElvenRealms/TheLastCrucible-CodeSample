using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Project-wide input hub. Wraps the generated input actions and exposes
/// C# events for discrete inputs plus pollable properties for continuous ones.
/// </summary>
[CreateAssetMenu(fileName = "InputReader", menuName = "BossFight/Input Reader")]
public class InputReader : ScriptableObject, InputSystem_Actions.IPlayerActions
{
    public event Action<Vector2> Move;
    public event Action<Vector2> Look;
    public event Action Attack;
    public event Action HeavyAttack;
    public event Action BlockPressed;
    public event Action Dodge;
    public event Action LockOn;
    public event Action Interact;
    public event Action Heal;
    public event Action SprintPressed;
    public event Action PlayerActionsDisabled;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public Vector2 MoveInput { get; private set; }
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public Vector2 LookInput { get; private set; }
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool SprintHeld { get; private set; }
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    public bool BlockHeld { get; private set; }

    /// <summary>True while device gameplay input is ignored, including while a fight script has control.</summary>
    public bool PlayerActionsSuppressed =>
        legacyPlayerActionsSuppressed || pausePlayerActionsSuppressed || cinematicPlayerActionsSuppressed ||
        scriptedControl;
    public bool IsScriptedControl => scriptedControl;

    private InputSystem_Actions actions;
    private bool legacyPlayerActionsSuppressed;
    private bool pausePlayerActionsSuppressed;
    private bool cinematicPlayerActionsSuppressed;
    private bool scriptedControl;

    private void OnEnable()
    {
        EnablePlayerActions();
    }

    public void EnablePlayerActions()
    {
        if (legacyPlayerActionsSuppressed) return;

        if (actions == null)
        {
            actions = new InputSystem_Actions();
            actions.Player.SetCallbacks(this);
        }
        actions.Player.Enable();
    }

    /// <summary>
    /// Temporarily ignores gameplay input while preserving the generated action
    /// map so it can be restored without reconstructing subscriptions.
    /// </summary>
    public void SuppressPlayerActions()
    {
        legacyPlayerActionsSuppressed = true;
        actions?.Player.Disable();
        ClearInputState();
        PlayerActionsDisabled?.Invoke();
    }

    public void RestorePlayerActions()
    {
        legacyPlayerActionsSuppressed = false;
        EnablePlayerActions();
    }

    /// <summary>
    /// Ignores gameplay callbacks while preserving active combat state and the
    /// generated action map. Unlike legacy suppression, this does not clear
    /// input-derived state or notify listeners.
    /// </summary>
    public void SetPauseSuppressed(bool suppressed)
    {
        pausePlayerActionsSuppressed = suppressed;
        if (!suppressed)
            RefreshContinuousInputState();
    }

    /// <summary>
    /// Ignores gameplay callbacks while the cinematic free camera uses the same keys. Unlike pause,
    /// time keeps running, so held movement is cleared rather than kept.
    /// </summary>
    public void SetCinematicSuppressed(bool suppressed)
    {
        cinematicPlayerActionsSuppressed = suppressed;
        // Under a fight script the held values are the script's, not the devices'.
        if (scriptedControl)
        {
            return;
        }
        if (suppressed)
        {
            ClearInputState();
            Move?.Invoke(MoveInput);
            Look?.Invoke(LookInput);
            PlayerActionsDisabled?.Invoke();
        }
        else
        {
            RefreshContinuousInputState();
        }
    }

    /// <summary>
    /// Hands gameplay input to a fight script. Device callbacks are ignored and the Inject methods
    /// raise the same events instead. Either way, held movement, sprint and block are cleared so
    /// neither source inherits the other's.
    /// </summary>
    public void SetScriptedControl(bool scripted)
    {
        if (scriptedControl == scripted)
        {
            return;
        }

        scriptedControl = scripted;
        ClearInputState();
        Move?.Invoke(MoveInput);
        Look?.Invoke(LookInput);
        PlayerActionsDisabled?.Invoke();
        RefreshContinuousInputState();
    }

    /// <summary>Scripted movement, in the same camera-relative space as the Move action.</summary>
    public void InjectMove(Vector2 move)
    {
        if (!CanInject)
        {
            return;
        }

        MoveInput = move;
        Move?.Invoke(MoveInput);
    }

    /// <summary>Scripted sprint button. Raises SprintPressed on the press, like the Sprint action.</summary>
    public void InjectSprint(bool held)
    {
        if (!CanInject)
        {
            return;
        }

        bool pressed = held && !SprintHeld;
        SprintHeld = held;
        if (pressed)
        {
            SprintPressed?.Invoke();
        }
    }

    public void InjectPress(ScriptedPress press)
    {
        if (!CanInject)
        {
            return;
        }

        Log($"{press} (scripted)");
        switch (press)
        {
            case ScriptedPress.Attack: Attack?.Invoke(); break;
            case ScriptedPress.HeavyAttack: HeavyAttack?.Invoke(); break;
            case ScriptedPress.Block: BlockPressed?.Invoke(); break;
            case ScriptedPress.Dodge: Dodge?.Invoke(); break;
            case ScriptedPress.LockOn: LockOn?.Invoke(); break;
            case ScriptedPress.Heal: Heal?.Invoke(); break;
            default: throw new ArgumentOutOfRangeException(nameof(press), press, null);
        }
    }

    private void OnDisable()
    {
        actions?.Player.Disable();
        legacyPlayerActionsSuppressed = false;
        pausePlayerActionsSuppressed = false;
        cinematicPlayerActionsSuppressed = false;
        scriptedControl = false;
        ClearInputState();
        PlayerActionsDisabled?.Invoke();
    }

    private void OnDestroy()
    {
        if (actions == null) return;

        actions.Dispose();
        actions = null;
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        if (PlayerActionsSuppressed) return;
        MoveInput = context.ReadValue<Vector2>();
        Move?.Invoke(MoveInput);
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        if (PlayerActionsSuppressed) return;
        LookInput = context.ReadValue<Vector2>();
        Look?.Invoke(LookInput);
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        if (PlayerActionsSuppressed || !context.performed)
        {
            return;
        }

        if (context.control.device is Mouse && IsShiftHeld())
        {
            Log("HeavyAttack");
            HeavyAttack?.Invoke();
            return;
        }

        Log("Attack");
        Attack?.Invoke();
    }

    public void OnHeavyAttack(InputAction.CallbackContext context)
    {
        if (PlayerActionsSuppressed || !context.performed)
        {
            return;
        }

        Log("HeavyAttack");
        HeavyAttack?.Invoke();
    }

    public void OnBlock(InputAction.CallbackContext context)
    {
        if (PlayerActionsSuppressed) return;
        if (context.performed)
        {
            Log("Block");
            BlockHeld = true;
            BlockPressed?.Invoke();
        }
        else if (context.canceled)
        {
            BlockHeld = false;
        }
    }

    public void OnDodge(InputAction.CallbackContext context)
    {
        if (PlayerActionsSuppressed || !context.performed) return;
        Log("Dodge");
        Dodge?.Invoke();
    }

    public void OnLockOn(InputAction.CallbackContext context)
    {
        if (PlayerActionsSuppressed || !context.performed) return;
        Log("LockOn");
        LockOn?.Invoke();
    }

    public void OnInteract(InputAction.CallbackContext context)
    {
        if (PlayerActionsSuppressed || !context.performed) return;
        Log("Interact");
        Interact?.Invoke();
    }

    public void OnHeal(InputAction.CallbackContext context)
    {
        if (PlayerActionsSuppressed || !context.performed) return;
        Log("Heal");
        Heal?.Invoke();
    }

    public void OnSprint(InputAction.CallbackContext context)
    {
        if (PlayerActionsSuppressed) return;
        if (context.performed)
        {
            SprintHeld = true;
            SprintPressed?.Invoke();
        }
        else if (context.canceled)
        {
            SprintHeld = false;
        }
    }

    // Pause and lifecycle suppression (death, victory) still apply to a script.
    private bool CanInject => scriptedControl && !legacyPlayerActionsSuppressed && !pausePlayerActionsSuppressed;

    private void Log(string message)
    {
        if (showDebugLogs) Debug.Log($"[InputReader] {message}", this);
    }

    private bool IsShiftHeld()
    {
        return Keyboard.current != null &&
               (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
    }

    private void ClearInputState()
    {
        MoveInput = Vector2.zero;
        LookInput = Vector2.zero;
        SprintHeld = false;
        BlockHeld = false;
    }

    private void RefreshContinuousInputState()
    {
        if (PlayerActionsSuppressed || actions == null) return;

        MoveInput = actions.Player.Move.ReadValue<Vector2>();
        LookInput = actions.Player.Look.ReadValue<Vector2>();
        SprintHeld = actions.Player.Sprint.IsPressed();
        BlockHeld = actions.Player.Block.IsPressed();
        Move?.Invoke(MoveInput);
        Look?.Invoke(LookInput);
    }
}

/// <summary>A discrete gameplay press a fight script can inject. Block is a tap: pressed and let go at once.</summary>
public enum ScriptedPress
{
    Attack,
    HeavyAttack,
    Block,
    Dodge,
    LockOn,
    Heal
}
