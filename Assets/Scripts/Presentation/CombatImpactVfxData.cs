using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Authored prefab and placement for a one-shot effect at the combat contact point.
/// </summary>
[CreateAssetMenu(fileName = "CombatImpactVfx", menuName = "BossFight/Combat Impact VFX Data")]
public class CombatImpactVfxData : CombatFeedbackEffectData
{
    [BoxGroup("Prefab"), AssetsOnly, Required, Tooltip("One-shot effect spawned at the contact point. Its particle systems switch to Hierarchy scaling so Scale applies to all of them.")]
    public GameObject prefab;
    [BoxGroup("Prefab"), MinValue(0.01f)]
    public float scale = 1f;
    [BoxGroup("Prefab"), Tooltip("Turns the prefab's forward axis on the ground plane. The prefab's own rotation is applied on top.")]
    public ImpactVfxOrientation orientation = ImpactVfxOrientation.TowardSource;
    [BoxGroup("Prefab"), Tooltip("Keeps the effect playing through hit stop.")]
    public bool useUnscaledTime = true;
    [BoxGroup("Prefab"), MinValue(0.1f), Tooltip("Seconds before a still-playing instance is stopped and returned to the pool. Catches looping prefabs.")]
    public float maxDuration = 2f;

    [BoxGroup("Lethal Hits"), MinValue(1f), Tooltip("Multiplies Scale for lethal impacts.")]
    public float lethalScale = 1.5f;

    public float GetScale(bool lethal)
    {
        return lethal ? scale * lethalScale : scale;
    }
}

/// <summary>
/// How an impact prefab is turned relative to the attacker.
/// </summary>
public enum ImpactVfxOrientation
{
    Prefab,
    TowardSource,
    AwayFromSource
}
