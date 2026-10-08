using UnityEditor;
using UnityEngine;

/// <summary>Creates <see cref="AudioCue"/> assets for Editor tooling, next to the project's other cues.</summary>
public static class AudioCueAssets
{
    private const string CueFolder = "Assets/Data/Audio";

    /// <summary>
    /// Asks where to save a new, empty cue and creates it there. Returns null when the dialog is cancelled.
    /// </summary>
    public static AudioCue CreateWithDialog(string suggestedName)
    {
        string path = EditorUtility.SaveFilePanelInProject("New Audio Cue", suggestedName, "asset",
            "Choose a name for the new audio cue.", CueFolder);
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var cue = ScriptableObject.CreateInstance<AudioCue>();
        AssetDatabase.CreateAsset(cue, path);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(cue);
        return cue;
    }
}
