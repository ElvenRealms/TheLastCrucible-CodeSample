/// <summary>
/// Scene-level executor that attempts to deliver one combat-feedback effect.
/// </summary>
public interface ICombatFeedbackExecutor
{
    bool TryExecute(CombatFeedbackEffectData effectData, in CombatFeedbackRequest request);
}
