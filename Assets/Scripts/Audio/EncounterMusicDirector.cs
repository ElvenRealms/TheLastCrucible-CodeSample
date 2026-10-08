using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Ties the combat music loop to the encounter lifecycle: silent until the fight
/// starts, fades in on activation, and fades out when the player dies or the boss
/// falls. Observes the ArenaManager and never drives it.
/// </summary>
[DisallowMultipleComponent]
public class EncounterMusicDirector : MonoBehaviour
{
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    private ArenaManager arenaManager;
    [FoldoutGroup("References"), SerializeField, Required, SceneObjectsOnly]
    [Tooltip("Music loop to drive. Turn off its Play On Enable so it stays silent before the fight.")]
    private AudioLoopPlayer musicLoop;

    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    private float fadeInSeconds = 1.5f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    [Tooltip("Fade when the player dies and the encounter ends.")]
    private float defeatFadeOutSeconds = 2f;
    [FoldoutGroup("Timing"), SerializeField, MinValue(0f)]
    [Tooltip("Fade when the boss falls. Longer, so the victory beat plays out over the tail.")]
    private float victoryFadeOutSeconds = 4f;

    [FoldoutGroup("Debug"), SerializeField]
    private bool showDebugLogs;

    private void OnEnable()
    {
        if (arenaManager == null || musicLoop == null)
        {
            return;
        }

        arenaManager.EncounterActivated += HandleEncounterActivated;
        arenaManager.EncounterDeactivated += HandleEncounterDeactivated;
        arenaManager.EncounterWon += HandleEncounterWon;
        if (arenaManager.IsActive)
        {
            musicLoop.FadeIn(fadeInSeconds);
        }
    }

    private void OnDisable()
    {
        if (arenaManager == null)
        {
            return;
        }

        arenaManager.EncounterActivated -= HandleEncounterActivated;
        arenaManager.EncounterDeactivated -= HandleEncounterDeactivated;
        arenaManager.EncounterWon -= HandleEncounterWon;
    }

    private void HandleEncounterActivated(Transform player)
    {
        musicLoop.FadeIn(fadeInSeconds);
        Log("Encounter started: music in.");
    }

    private void HandleEncounterDeactivated()
    {
        musicLoop.FadeOut(defeatFadeOutSeconds);
        Log("Encounter ended: music out.");
    }

    private void HandleEncounterWon(BossStateMachine boss)
    {
        musicLoop.FadeOut(victoryFadeOutSeconds);
        Log("Encounter won: music out.");
    }

    private void Log(string message)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[EncounterMusicDirector] {message}", this);
        }
    }
}
