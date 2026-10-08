using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Shared plumbing for converting a gameplay event into a scene-level combat
/// feedback request.
/// </summary>
public abstract class CombatFeedbackRequesterBase : MonoBehaviour
{
    [FoldoutGroup("Config"), SerializeField, Tooltip("Resolves via FindAnyObjectByType at runtime when left empty.")]
    private CombatFeedbackController controller;
    [FoldoutGroup("Config"), SerializeField, Required, AssetsOnly]
    private CombatFeedbackProfile profile;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int forwardedRequestCount;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private CombatFeedbackRequest lastRequest;

    public int ForwardedRequestCount => forwardedRequestCount;
    public CombatFeedbackRequest LastRequest => lastRequest;

    protected virtual void Awake()
    {
        if (controller == null) controller = FindAnyObjectByType<CombatFeedbackController>();
        if (controller == null) Debug.LogWarning($"{name} has no CombatFeedbackController to forward feedback to.", this);
    }

    protected void Forward(float damage, bool lethal, Transform source, Vector3 position)
    {
        Forward(profile, damage, lethal, source, transform, position);
    }

    protected bool Forward(CombatFeedbackProfile requestProfile, float damage, bool lethal, Transform source, Vector3 position)
    {
        return Forward(requestProfile, damage, lethal, source, transform, position);
    }

    protected bool Forward(CombatFeedbackProfile requestProfile, float damage, bool lethal, Transform source, Transform receiver, Vector3 position)
    {
        if (controller == null || requestProfile == null)
        {
            return false;
        }

        lastRequest = new CombatFeedbackRequest(requestProfile, damage, lethal, source, receiver, position);
        forwardedRequestCount++;
        controller.RequestFeedback(lastRequest);
        if (showDebugLogs) Debug.Log($"{name} requested combat feedback for {damage} damage.", this);
        return true;
    }
}
