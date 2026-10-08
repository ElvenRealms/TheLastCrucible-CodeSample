using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Routes explicit combat feedback requests to whichever effect executors
/// have self-registered. Executors register/unregister themselves; the
/// controller holds no direct references to concrete effect components.
/// </summary>
[DisallowMultipleComponent]
public class CombatFeedbackController : MonoBehaviour
{
    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private CombatFeedbackRequest lastRequest;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int requestCount;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int routedEffectCount;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int lastExecutionCount;

    private readonly List<ICombatFeedbackExecutor> executors = new();

    public CombatFeedbackRequest LastRequest => lastRequest;
    public int RequestCount => requestCount;
    public int RoutedEffectCount => routedEffectCount;
    public int LastExecutionCount => lastExecutionCount;

    public void Register(ICombatFeedbackExecutor executor)
    {
        if (executor == null || executors.Contains(executor)) return;
        executors.Add(executor);
    }

    public void Unregister(ICombatFeedbackExecutor executor)
    {
        if (executor == null) return;
        executors.Remove(executor);
    }

    /// <summary>
    /// Offers each authored effect to registered executors in list order.
    /// The first executor that accepts an entry owns its delivery.
    /// </summary>
    public int RequestFeedback(CombatFeedbackRequest request)
    {
        lastRequest = request;
        requestCount++;
        lastExecutionCount = 0;

        CombatFeedbackProfile profile = request.profile;
        if (profile == null)
        {
            if (showDebugLogs) Debug.LogWarning("Combat feedback request has no profile.", this);
            return 0;
        }

        List<CombatFeedbackEffectData> effects = profile.effects;
        if (effects == null)
        {
            Debug.LogWarning($"Combat feedback profile '{profile.name}' has no effect list.", this);
            return 0;
        }

        for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
        {
            CombatFeedbackEffectData effectData = effects[effectIndex];
            if (effectData == null)
            {
                Debug.LogWarning($"Combat feedback profile '{profile.name}' has a null effect entry at index {effectIndex}.", this);
                continue;
            }

            if (TryDeliver(effectData, in request))
            {
                lastExecutionCount++;
            }
            else
            {
                Debug.LogWarning(
                    $"Combat feedback profile '{profile.name}' effect '{effectData.name}' ({effectData.GetType().Name}) at index {effectIndex} could not be delivered by any registered executor.",
                    this);
            }
        }

        if (showDebugLogs)
            Debug.Log($"Combat feedback routed {lastExecutionCount} effect(s) from profile '{profile.name}'.", this);

        return lastExecutionCount;
    }

    /// <summary>
    /// Offers one effect outside any profile, such as an animation clip's camera shake, to the
    /// registered executors. The first executor that accepts it owns its delivery.
    /// </summary>
    public bool RequestEffect(CombatFeedbackEffectData effectData, in CombatFeedbackRequest request)
    {
        if (effectData == null)
        {
            return false;
        }
        if (TryDeliver(effectData, in request))
        {
            return true;
        }

        Debug.LogWarning($"Combat feedback effect '{effectData.name}' ({effectData.GetType().Name}) could not be delivered by any registered executor.", this);
        return false;
    }

    private bool TryDeliver(CombatFeedbackEffectData effectData, in CombatFeedbackRequest request)
    {
        for (int executorIndex = 0; executorIndex < executors.Count; executorIndex++)
        {
            ICombatFeedbackExecutor executor = executors[executorIndex];
            if (executor == null) continue;
            if (!executor.TryExecute(effectData, in request)) continue;

            routedEffectCount++;
            return true;
        }
        return false;
    }
}
