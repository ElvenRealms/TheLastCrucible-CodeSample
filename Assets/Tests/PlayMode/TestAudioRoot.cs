#if UNITY_EDITOR
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Shared PlayMode fixture helper: spins up an enabled AudioPlaybackService so
/// that presenters calling AudioPlaybackService.Instance (parry, death, etc.)
/// have a real singleton instead of tripping the "no enabled
/// AudioPlaybackService exists" error. Fixtures that instantiate the Player
/// prefab should create one in Setup and destroy it in TearDown.
/// </summary>
public static class TestAudioRoot
{
    public static GameObject Create()
    {
        GameObject root = new("Test Audio Root");
        root.SetActive(false);
        // Test scenes have no camera listener; without one Unity logs on every cue and trips LogAssert.
        root.AddComponent<AudioListener>();
        AudioPlaybackService service = root.AddComponent<AudioPlaybackService>();
        SerializedObject serialized = new(service);
        bool mixerConfigured = TryAssignMixerGroups(serialized);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(mixerConfigured, Is.True,
            "TestAudioRoot requires the authored AudioMixer asset with Music, SFX, Ambience, and UI groups.");
        root.SetActive(true);
        return root;
    }

    public static void Destroy(GameObject root)
    {
        if (root != null) Object.Destroy(root);
    }

    public static bool TryAssignMixerGroups(SerializedObject serialized)
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioMixer");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            AudioMixer mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
            if (mixer == null) continue;

            AudioMixerGroup music = FindGroup(mixer, "Music");
            AudioMixerGroup sfx = FindGroup(mixer, "SFX");
            AudioMixerGroup ambience = FindGroup(mixer, "Ambience");
            AudioMixerGroup ui = FindGroup(mixer, "UI");
            if (music == null || sfx == null || ambience == null || ui == null) continue;

            serialized.FindProperty("musicMixerGroup").objectReferenceValue = music;
            serialized.FindProperty("sfxMixerGroup").objectReferenceValue = sfx;
            serialized.FindProperty("ambienceMixerGroup").objectReferenceValue = ambience;
            serialized.FindProperty("uiMixerGroup").objectReferenceValue = ui;
            return true;
        }

        return false;
    }

    private static AudioMixerGroup FindGroup(AudioMixer mixer, string groupName)
    {
        AudioMixerGroup[] groups = mixer.FindMatchingGroups(groupName);
        if (groups == null) return null;
        for (int i = 0; i < groups.Length; i++)
        {
            if (groups[i] != null && groups[i].name == groupName) return groups[i];
        }

        return null;
    }
}
#endif
