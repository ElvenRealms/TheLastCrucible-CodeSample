using UnityEditor;
using UnityEngine;

/// <summary>Positions tool windows relative to the main Editor window.</summary>
public static class EditorWindowPlacement
{
    /// <summary>A window rect of the given size, centered on the main Editor window.</summary>
    public static Rect CenteredRect(float width, float height)
    {
        Rect mainWindow = EditorGUIUtility.GetMainWindowPosition();
        return new Rect(
            mainWindow.center.x - width * 0.5f,
            mainWindow.center.y - height * 0.5f,
            width,
            height);
    }
}
