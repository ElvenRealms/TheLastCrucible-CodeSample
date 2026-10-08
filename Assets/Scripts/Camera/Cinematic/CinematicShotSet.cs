using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// The shots the <see cref="CinematicDirector"/> cuts between, plus how its camera follows the action.
/// </summary>
[CreateAssetMenu(fileName = "CinematicShotSet", menuName = "BossFight/Camera/Cinematic Shot Set")]
public class CinematicShotSet : ScriptableObject
{
    [FoldoutGroup("Shots"), SerializeField, ListDrawerSettings(ShowFoldout = true, ListElementLabelName = "Name")]
    private List<CinematicShot> shots = new List<CinematicShot>();

    [FoldoutGroup("Motion"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Smoothing on the fight axis, so the orbit doesn't swing with every step.")]
    private float axisSmoothing = 1.2f;
    [FoldoutGroup("Motion"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Smoothing on the camera position.")]
    private float positionSmoothing = 0.45f;
    [FoldoutGroup("Motion"), SerializeField, MinValue(0f), SuffixLabel("s")]
    [Tooltip("Smoothing on the aim point; lower keeps fast attacks in frame.")]
    private float aimSmoothing = 0.2f;
    [FoldoutGroup("Motion"), SerializeField, MinValue(0.01f)]
    [Tooltip("Speed of the handheld wobble.")]
    private float handheldFrequency = 0.35f;

    [FoldoutGroup("Collision"), SerializeField]
    [Tooltip("Geometry the camera must stay in front of. Leave the fighters' layers out.")]
    private LayerMask obstacleLayers = 1;
    [FoldoutGroup("Collision"), SerializeField, MinValue(0.05f), SuffixLabel("m")]
    private float cameraRadius = 0.3f;
    [FoldoutGroup("Collision"), SerializeField, Range(0f, 1f)]
    [Tooltip("A shot whose clear distance falls below this fraction of its planned distance is skipped at the cut.")]
    private float minClearFraction = 0.6f;

    public IReadOnlyList<CinematicShot> Shots => shots;
    public float AxisSmoothing => axisSmoothing;
    public float PositionSmoothing => positionSmoothing;
    public float AimSmoothing => aimSmoothing;
    public float HandheldFrequency => handheldFrequency;
    public LayerMask ObstacleLayers => obstacleLayers;
    public float CameraRadius => cameraRadius;
    public float MinClearFraction => minClearFraction;
}
