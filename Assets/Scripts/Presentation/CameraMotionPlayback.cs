using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The camera moves that are playing, added together into one offset. Moves overlap freely, so a
/// combo's next swing starts its move while the last one is still settling.
/// </summary>
public sealed class CameraMotionPlayback
{
    private readonly List<ActiveMove> _moves = new();

    /// <summary>Summed camera-space position offset at the last trigger or advance.</summary>
    public Vector3 PositionOffset { get; private set; }
    /// <summary>Summed camera-space rotation in degrees at the last trigger or advance.</summary>
    public Vector3 RotationOffset { get; private set; }
    public int ActiveCount => _moves.Count;

    /// <summary>Starts a move from its first frame alongside any still playing.</summary>
    public void Trigger(CameraMotionData data)
    {
        if (data == null)
        {
            return;
        }

        _moves.Add(new ActiveMove { data = data });
        Evaluate();
    }

    /// <summary>Moves every active move forward and drops the finished ones.</summary>
    public void Advance(float deltaTime)
    {
        for (int i = _moves.Count - 1; i >= 0; i--)
        {
            ActiveMove move = _moves[i];
            move.elapsed += deltaTime;
            // A destroyed asset ends its move, so the camera can't hold an offset forever.
            if (move.data == null || move.elapsed >= move.data.duration)
            {
                _moves.RemoveAt(i);
                continue;
            }
            _moves[i] = move;
        }
        Evaluate();
    }

    /// <summary>Stops every move and returns the camera to rest.</summary>
    public void Clear()
    {
        _moves.Clear();
        Evaluate();
    }

    private void Evaluate()
    {
        Vector3 position = Vector3.zero;
        Vector3 rotation = Vector3.zero;
        foreach (var move in _moves)
        {
            float weight = move.data.GetWeight(move.elapsed);
            position += move.data.positionOffset * weight;
            rotation += move.data.rotationOffset * weight;
        }
        PositionOffset = position;
        RotationOffset = rotation;
    }

    private struct ActiveMove
    {
        public CameraMotionData data;
        public float elapsed;
    }
}
