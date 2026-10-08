using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Resolves the HitFlashReceiver on the request's receiver and forwards the
/// flash to it. Receiver lookups are cached per damageable transform.
/// </summary>
public class HitFlashEffect : MonoBehaviour, ICombatFeedbackExecutor
{
    [FoldoutGroup("Config"), SerializeField, Tooltip("Resolves via FindAnyObjectByType at runtime when left empty.")]
    private CombatFeedbackController controller;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;

    private readonly Dictionary<Transform, HitFlashReceiver> receiverCache = new();

    public bool TryExecute(CombatFeedbackEffectData effectData, in CombatFeedbackRequest request)
    {
        HitFlashData data = effectData as HitFlashData;
        if (data == null || !isActiveAndEnabled) return false;

        if (request.receiver == null)
        {
            if (showDebugLogs) Debug.Log($"{name} got a hit flash request with no receiver.", this);
            return false;
        }

        HitFlashReceiver receiver = ResolveReceiver(request.receiver);
        if (receiver == null)
        {
            if (showDebugLogs) Debug.Log($"{name} found no HitFlashReceiver on '{request.receiver.name}'.", this);
            return false;
        }

        receiver.Flash(data, request.lethal);
        return true;
    }

    private HitFlashReceiver ResolveReceiver(Transform receiverTransform)
    {
        if (receiverCache.TryGetValue(receiverTransform, out HitFlashReceiver cached) && cached != null)
            return cached;

        HitFlashReceiver resolved = receiverTransform.GetComponentInChildren<HitFlashReceiver>();
        receiverCache[receiverTransform] = resolved;
        return resolved;
    }

    private void OnEnable()
    {
        if (controller == null) controller = FindAnyObjectByType<CombatFeedbackController>();
        if (controller == null) Debug.LogWarning($"{name} has no CombatFeedbackController to register with.", this);
        else controller.Register(this);
    }

    private void OnDisable()
    {
        if (controller != null) controller.Unregister(this);
    }
}
