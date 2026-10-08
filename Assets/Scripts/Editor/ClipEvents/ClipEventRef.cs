using System;

/// <summary>
/// Identifies one event on the Clip Events timeline by its lane and its index in that lane.
/// </summary>
public readonly struct ClipEventRef : IEquatable<ClipEventRef>
{
    public static readonly ClipEventRef None = new(null, -1);

    public ClipEventRef(ClipEventLane lane, int index)
    {
        Lane = lane;
        Index = index;
    }

    public ClipEventLane Lane { get; }
    public int Index { get; }

    /// <summary>True when this refers to the event at an index in a lane.</summary>
    public bool Is(ClipEventLane lane, int index) => Lane == lane && Index == index;

    public bool Equals(ClipEventRef other) => Is(other.Lane, other.Index);

    public override bool Equals(object obj) => obj is ClipEventRef other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Lane, Index);
}
