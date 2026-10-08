using Sirenix.OdinInspector;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Executes camera shake requests via a Cinemachine Impulse Source, kicking
/// the impulse along the hit direction when the profile requests it.
/// </summary>
public class CameraShakeEffect : MonoBehaviour, ICombatFeedbackExecutor
{
    [FoldoutGroup("Config"), SerializeField, Required]
    private CinemachineImpulseSource impulseSource;
    [FoldoutGroup("Config"), SerializeField, Tooltip("Resolves via FindAnyObjectByType at runtime when left empty.")]
    private CombatFeedbackController controller;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private float lastAmplitude;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int shakeCount;

    public float LastAmplitude => lastAmplitude;
    public int ShakeCount => shakeCount;
    public bool TryExecute(CombatFeedbackEffectData effectData, in CombatFeedbackRequest request)
    {
        CameraShakeData data = effectData as CameraShakeData;
        if (data == null || impulseSource == null || !isActiveAndEnabled) return false;

        float amplitude = data.GetAmplitude(request.damage, request.lethal);
        if (request.receiver != null)
        {
            amplitude *= data.GetDistanceScale(Vector3.Distance(request.position, request.receiver.position));
        }
        lastAmplitude = amplitude;
        shakeCount++;

        if (data.directional && request.source != null && request.receiver != null)
        {
            Vector3 direction = request.receiver.position - request.source.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > Mathf.Epsilon)
            {
                direction.Normalize();
                impulseSource.GenerateImpulseAtPositionWithVelocity(request.position, direction * amplitude);
                if (showDebugLogs) Debug.Log($"{name} generated a directional shake with amplitude {amplitude:0.###}.", this);
                return true;
            }
        }

        impulseSource.GenerateImpulseWithForce(amplitude);
        if (showDebugLogs) Debug.Log($"{name} generated an undirected shake with amplitude {amplitude:0.###}.", this);
        return true;
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
