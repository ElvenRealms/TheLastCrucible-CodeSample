using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Spawns pooled, data-driven effect prefabs at requested combat contact points.
/// </summary>
[DisallowMultipleComponent]
public class CombatImpactVfx : MonoBehaviour, ICombatFeedbackExecutor
{
    [FoldoutGroup("Config"), SerializeField, Tooltip("Resolves via FindAnyObjectByType at runtime when left empty.")]
    private CombatFeedbackController controller;
    [FoldoutGroup("Config"), SerializeField, MinValue(1), Tooltip("Instances kept per prefab. The oldest is recycled when all of them are playing.")]
    private int poolSize = 6;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private CombatImpactVfxData lastData;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private Vector3 lastPosition;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private float lastScale;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private int playedBurstCount;
    [FoldoutGroup("Debug"), SerializeField, ReadOnly]
    private int activeInstanceCount;

    private VfxPool _pool;

    public CombatImpactVfxData LastData => lastData;
    public Vector3 LastPosition => lastPosition;
    public float LastScale => lastScale;
    public int PlayedBurstCount => playedBurstCount;
    public int ActiveInstanceCount => activeInstanceCount;

    private void Awake()
    {
        _pool = new VfxPool(transform, poolSize);
    }

    private void OnEnable()
    {
        if (controller == null)
        {
            controller = FindAnyObjectByType<CombatFeedbackController>();
        }

        if (controller == null)
        {
            Debug.LogWarning($"{name} has no CombatFeedbackController to register with.", this);
        }
        else
        {
            controller.Register(this);
        }
    }

    private void OnDisable()
    {
        if (controller != null)
        {
            controller.Unregister(this);
        }

        _pool.ReleaseAll();
        activeInstanceCount = _pool.ActiveCount;
    }

    private void Update()
    {
        _pool.Tick();
        activeInstanceCount = _pool.ActiveCount;
    }

    public bool TryExecute(CombatFeedbackEffectData effectData, in CombatFeedbackRequest request)
    {
        var data = effectData as CombatImpactVfxData;
        if (data == null || !isActiveAndEnabled)
        {
            return false;
        }

        if (data.prefab == null)
        {
            Debug.LogWarning($"{data.name} has no VFX prefab to play.", data);
            return false;
        }

        float scale = data.GetScale(request.lethal);
        var pose = new Pose(request.position, GetRotation(data, request));
        _pool.Play(data.prefab, pose, scale, data.useUnscaledTime, data.maxDuration);
        activeInstanceCount = _pool.ActiveCount;

        lastData = data;
        lastPosition = request.position;
        lastScale = scale;
        playedBurstCount++;
        if (showDebugLogs)
        {
            Debug.Log($"{name} played {data.prefab.name} at {lastPosition} with scale {lastScale}.", this);
        }

        return true;
    }

    private static Quaternion GetRotation(CombatImpactVfxData data, in CombatFeedbackRequest request)
    {
        if (data.orientation == ImpactVfxOrientation.Prefab || request.source == null)
        {
            return Quaternion.identity;
        }

        Vector3 towardSource = request.source.position - request.position;
        towardSource.y = 0f;
        if (towardSource.sqrMagnitude < 0.0001f)
        {
            return Quaternion.identity;
        }

        Vector3 forward = data.orientation == ImpactVfxOrientation.TowardSource ? towardSource : -towardSource;
        return Quaternion.LookRotation(forward);
    }
}
