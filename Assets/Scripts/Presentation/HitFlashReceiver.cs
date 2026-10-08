using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Owns the renderers of a damageable and fades a shared emission/tint flash across
/// them on hit. Runs on unscaled time so the flash stays visible during hit stop.
/// </summary>
public class HitFlashReceiver : MonoBehaviour
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int HitFlashColorId = Shader.PropertyToID("_HitFlashColor");
    private static readonly int SyntyEmissionColorId = Shader.PropertyToID("_Emission_Color");
    private static readonly int SyntyEmissionMapId = Shader.PropertyToID("_Emission_Map");
    private static readonly int SyntyEnableEmissionId = Shader.PropertyToID("_Enable_Emission");

    [FoldoutGroup("Config"), SerializeField]
    private Renderer[] renderers;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private float flashRemaining;
    [FoldoutGroup("Debug"), ReadOnly, ShowInInspector]
    private int flashCount;

    // Per-renderer flash strategy, resolved once at Awake from each renderer's shared material.
    private class TintProperties
    {
        public int[] ids;
        public Color[] originals;
    }

    private class SyntyEmissionProperties
    {
        public Color color;
        public float enabled;
        public Texture map;
    }

    private MaterialPropertyBlock propertyBlock;
    private bool[] usesEmissionColor;
    private int[] _emissionColorIds;
    private Color[] restingEmission;
    private TintProperties[] tintProperties;
    private SyntyEmissionProperties[] _syntyEmissionProperties;
    private float flashDuration;
    private Color flashColor;
    private float flashIntensity;
    private bool pauseSuppressed;

    public int FlashCount => flashCount;

    // Active renderers only: modular characters carry hundreds of disabled wardrobe
    // meshes, and flashing those costs a property-block write per renderer per frame.
    private void Reset()
    {
        renderers = GetComponentsInChildren<Renderer>(false);
    }

    private void Awake()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(false);
        propertyBlock = new MaterialPropertyBlock();
        CacheFlashStrategies();
    }

    // Synty Generic needs its emission mask overridden to light the whole mesh.
    // Other shaders retain their existing emission or tint strategy.
    private void CacheFlashStrategies()
    {
        usesEmissionColor = new bool[renderers.Length];
        _emissionColorIds = new int[renderers.Length];
        restingEmission = new Color[renderers.Length];
        tintProperties = new TintProperties[renderers.Length];
        _syntyEmissionProperties = new SyntyEmissionProperties[renderers.Length];
        Dictionary<Material, TintProperties> tintCache = new Dictionary<Material, TintProperties>();

        for (int i = 0; i < renderers.Length; i++)
        {
            Material material = renderers[i] != null ? renderers[i].sharedMaterial : null;
            if (material == null) continue;

            if (material.HasProperty(HitFlashColorId))
            {
                usesEmissionColor[i] = true;
                _emissionColorIds[i] = HitFlashColorId;
                restingEmission[i] = material.GetColor(HitFlashColorId);
                continue;
            }

            if (material.HasProperty(SyntyEmissionColorId)
                && material.HasProperty(SyntyEmissionMapId)
                && material.HasProperty(SyntyEnableEmissionId))
            {
                _syntyEmissionProperties[i] = new SyntyEmissionProperties
                {
                    color = material.GetColor(SyntyEmissionColorId),
                    enabled = material.GetFloat(SyntyEnableEmissionId),
                    map = material.GetTexture(SyntyEmissionMapId) ?? Texture2D.whiteTexture
                };
                continue;
            }

            if (material.HasProperty(EmissionColorId))
            {
                usesEmissionColor[i] = true;
                _emissionColorIds[i] = EmissionColorId;
                // Preserve authored emission when a URP flash fades out.
                restingEmission[i] = material.GetColor(EmissionColorId);
                continue;
            }

            if (!tintCache.TryGetValue(material, out TintProperties tint))
            {
                tint = DiscoverTintProperties(material);
                tintCache[material] = tint;
            }
            tintProperties[i] = tint;
        }
    }

    private static TintProperties DiscoverTintProperties(Material material)
    {
        Shader shader = material.shader;
        List<int> ids = new List<int>();
        List<Color> originals = new List<Color>();
        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
            string propertyName = shader.GetPropertyName(i);
            if (!propertyName.StartsWith("_Color") && propertyName != "_BaseColor") continue;

            int id = Shader.PropertyToID(propertyName);
            ids.Add(id);
            originals.Add(material.GetColor(id));
        }
        return new TintProperties { ids = ids.ToArray(), originals = originals.ToArray() };
    }

    public void Flash(HitFlashData data, bool lethal)
    {
        if (data == null) return;

        float newDuration = data.GetDuration(lethal);
        if (newDuration <= 0f) return;

        flashDuration = Mathf.Max(flashRemaining, newDuration);
        flashRemaining = flashDuration;
        flashColor = data.flashColor;
        flashIntensity = data.intensity;
        flashCount++;
        ApplyFlash(1f);
        if (showDebugLogs) Debug.Log($"{name} flashed for {flashDuration:0.###} seconds.", this);
    }

    private void OnDisable()
    {
        // Never leave a renderer stuck lit if the flash is cut short.
        if (flashRemaining <= 0f) return;
        flashRemaining = 0f;
        ApplyFlash(0f);
    }

    private void Update()
    {
        if (pauseSuppressed) return;
        if (flashRemaining <= 0f) return;

        flashRemaining -= Time.unscaledDeltaTime;
        if (flashRemaining <= 0f)
        {
            flashRemaining = 0f;
            ApplyFlash(0f);
            return;
        }

        float t = flashDuration > 0f ? Mathf.Clamp01(flashRemaining / flashDuration) : 0f;
        ApplyFlash(t);
    }

    /// <summary>
    /// Freezes this receiver's unscaled flash timer while gameplay is paused.
    /// </summary>
    public void SetPauseSuppressed(bool suppressed)
    {
        pauseSuppressed = suppressed;
    }

    private void ApplyFlash(float t)
    {
        if (renderers == null || usesEmissionColor == null) return;

        Color emission = flashColor * flashIntensity * t;
        float strength = Mathf.Clamp01(t * flashIntensity);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer target = renderers[i];
            if (target == null) continue;

            target.GetPropertyBlock(propertyBlock);
            if (_syntyEmissionProperties[i] != null)
            {
                ApplySyntyEmission(_syntyEmissionProperties[i], emission, t);
            }
            else if (usesEmissionColor[i])
            {
                propertyBlock.SetColor(_emissionColorIds[i], restingEmission[i] + emission);
            }
            else if (tintProperties[i] != null)
            {
                TintProperties tint = tintProperties[i];
                for (int p = 0; p < tint.ids.Length; p++)
                    propertyBlock.SetColor(tint.ids[p], Color.Lerp(tint.originals[p], flashColor, strength));
            }
            target.SetPropertyBlock(propertyBlock);
        }
    }

    private void ApplySyntyEmission(SyntyEmissionProperties originals, Color emission, float t)
    {
        if (t > 0f)
        {
            propertyBlock.SetFloat(SyntyEnableEmissionId, 1f);
            propertyBlock.SetTexture(SyntyEmissionMapId, Texture2D.whiteTexture);
            propertyBlock.SetColor(SyntyEmissionColorId, emission);
            return;
        }

        propertyBlock.SetFloat(SyntyEnableEmissionId, originals.enabled);
        propertyBlock.SetTexture(SyntyEmissionMapId, originals.map);
        propertyBlock.SetColor(SyntyEmissionColorId, originals.color);
    }
}
