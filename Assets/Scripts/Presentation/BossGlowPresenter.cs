using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Composes phase emission and the unblockable warning without changing shared materials.
/// Each renderer retains its authored property block when this owner releases it.
/// </summary>
[DisallowMultipleComponent]
public class BossGlowPresenter : MonoBehaviour
{
    private static readonly int EmissionMapId = Shader.PropertyToID("_Emission_Map");
    private static readonly int EmissionColorId = Shader.PropertyToID("_Emission_Color");
    private static readonly int EnableEmissionId = Shader.PropertyToID("_Enable_Emission");

    [FoldoutGroup("References"), SerializeField, Required]
    private BossStateMachine boss;
    [FoldoutGroup("References"), SerializeField, ChildGameObjectsOnly]
    private Renderer weaponRenderer;

    [FoldoutGroup("Phase Glow"), SerializeField, ListDrawerSettings(ShowFoldout = false)]
    private GlowTarget[] targets = Array.Empty<GlowTarget>();
    [FoldoutGroup("Phase Glow"), SerializeField, MinValue(0.01f)]
    private float fadeInDuration = 2.6f;
    [FoldoutGroup("Phase Glow"), SerializeField, MinValue(0.01f)]
    private float fadeOutDuration = 1f;

    [FoldoutGroup("Unblockable Tell"), SerializeField, ColorUsage(false, true)]
    private Color glowColor = new Color(4f, 0.15f, 0.1f);
    [FoldoutGroup("Unblockable Tell"), SerializeField, MinValue(0f), SuffixLabel("s")]
    private float fadeDuration = 0.15f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private float _weight;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private bool isShowing;
    [FoldoutGroup("Debug"), ShowInInspector, ReadOnly]
    private float glowStrength;

    private RendererGlow[] _renderers = Array.Empty<RendererGlow>();

    public float PhaseWeight => _weight;
    public float TellStrength => glowStrength;
    public bool IsShowing => isShowing;

    private sealed class RendererGlow
    {
        public Renderer renderer;
        public MaterialPropertyBlock authored;
        public MaterialPropertyBlock phase;
        public MaterialPropertyBlock tell;
        public Color baseColor;
        public Color enragedColor;
        public bool hasPhase;
        public bool hasTell;
        public bool authoredEmpty;
        public bool written;
    }

    private void OnEnable()
    {
        CacheAuthoredProperties();
    }

    private void OnDisable()
    {
        _weight = 0f;
        isShowing = false;
        glowStrength = 0f;
        RestoreAuthoredProperties();
    }

    private void LateUpdate()
    {
        bool glowing = boss != null && boss.isActiveAndEnabled && boss.IsEnraged && boss.CurrentState is not BossDeadState;
        bool shouldShow = boss != null && boss.isActiveAndEnabled && boss.CurrentState is BossAttackState attack &&
            attack.ActiveStep != null && !attack.IsBlockable;
        Tick(glowing, shouldShow, Time.deltaTime);
    }

    private void Tick(bool glowing, bool shouldShow, float deltaTime)
    {
        if (shouldShow != isShowing)
        {
            isShowing = shouldShow;
            if (isShowing && showDebugLogs)
            {
                Debug.Log($"{name} showing the Unblockable Tell.", this);
            }
        }
        float duration = glowing ? fadeInDuration : fadeOutDuration;
        _weight = Mathf.MoveTowards(_weight, glowing ? 1f : 0f, deltaTime / Mathf.Max(0.01f, duration));
        float step = fadeDuration > 0f ? deltaTime / fadeDuration : 1f;
        glowStrength = shouldShow ? Mathf.MoveTowards(glowStrength, 1f, step) : 0f;
        Apply();
    }

    private void Apply()
    {
        float blend = _weight * _weight;
        foreach (var state in _renderers)
        {
            if (state.renderer == null)
            {
                continue;
            }
            bool phaseActive = state.hasPhase && blend > 0f;
            bool tellActive = state.hasTell && glowStrength > 0f;
            if (!phaseActive && !tellActive)
            {
                Restore(state);
                continue;
            }
            Color phaseColor = state.hasPhase ? Color.Lerp(state.baseColor, state.enragedColor, blend) : state.baseColor;
            MaterialPropertyBlock block = tellActive ? state.tell : state.phase;
            block.SetColor(EmissionColorId, tellActive ? Color.Lerp(phaseColor, glowColor, glowStrength) : phaseColor);
            state.renderer.SetPropertyBlock(block);
            state.written = true;
        }
    }

    private void CacheAuthoredProperties()
    {
        var states = new List<RendererGlow>();
        foreach (var target in targets)
        {
            RendererGlow state = FindOrCreate(states, target.renderer);
            if (state != null)
            {
                state.hasPhase = true;
                state.enragedColor = target.enragedEmission;
            }
        }
        RendererGlow weapon = FindOrCreate(states, weaponRenderer);
        if (weapon != null)
        {
            weapon.hasTell = true;
        }
        _renderers = states.ToArray();
    }

    private static RendererGlow FindOrCreate(List<RendererGlow> states, Renderer renderer)
    {
        if (renderer == null)
        {
            return null;
        }
        foreach (var existing in states)
        {
            if (existing.renderer == renderer)
            {
                return existing;
            }
        }
        var state = new RendererGlow
        {
            renderer = renderer,
            authored = new MaterialPropertyBlock(),
            phase = new MaterialPropertyBlock(),
            tell = new MaterialPropertyBlock()
        };
        renderer.GetPropertyBlock(state.authored);
        renderer.GetPropertyBlock(state.phase);
        renderer.GetPropertyBlock(state.tell);
        state.authoredEmpty = state.authored.isEmpty;
        Material material = renderer.sharedMaterial;
        state.baseColor = state.authored.HasProperty(EmissionColorId) ? state.authored.GetColor(EmissionColorId) :
            material != null && material.HasProperty(EmissionColorId) ? material.GetColor(EmissionColorId) : Color.black;
        state.tell.SetFloat(EnableEmissionId, 1f);
        state.tell.SetTexture(EmissionMapId, Texture2D.whiteTexture);
        states.Add(state);
        return state;
    }

    private void RestoreAuthoredProperties()
    {
        foreach (var state in _renderers)
        {
            Restore(state);
        }
    }

    private static void Restore(RendererGlow state)
    {
        if (!state.written || state.renderer == null)
        {
            return;
        }
        state.renderer.SetPropertyBlock(state.authoredEmpty ? null : state.authored);
        state.written = false;
    }

    [Serializable]
    private struct GlowTarget
    {
        [Required]
        public Renderer renderer;
        [ColorUsage(false, true)]
        public Color enragedEmission;
    }
}
