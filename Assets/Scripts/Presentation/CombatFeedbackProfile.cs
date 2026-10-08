using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Selects and orders the presentation effects for a combat event.
/// </summary>
[CreateAssetMenu(fileName = "CombatFeedback", menuName = "BossFight/Combat Feedback Profile")]
public class CombatFeedbackProfile : ScriptableObject
{
    [BoxGroup("Effects"), AssetsOnly, InlineEditor, ListDrawerSettings(ShowFoldout = false, DraggableItems = true, ShowIndexLabels = true)]
    [ValidateInput(nameof(EffectsListValid), "effects must contain only valid combat-feedback assets.")]
    [ValidateInput(nameof(NoDuplicateEffects), "Repeated effect references are intentional but advisory.", InfoMessageType.Warning)]
    public List<CombatFeedbackEffectData> effects = new();

    public IReadOnlyList<CombatFeedbackEffectData> Effects => effects;

    private bool EffectsListValid
    {
        get
        {
            if (effects == null) return false;
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i] == null) return false;
            }

            return true;
        }
    }

    private bool NoDuplicateEffects
    {
        get
        {
            if (effects == null) return true;
            for (int i = 0; i < effects.Count; i++)
            {
                CombatFeedbackEffectData effect = effects[i];
                if (effect == null) continue;
                for (int j = i + 1; j < effects.Count; j++)
                {
                    if (ReferenceEquals(effect, effects[j])) return false;
                }
            }

            return true;
        }
    }
}
