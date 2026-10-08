/// <summary>
/// One held player action, last press wins. Lifetime is bounded by the
/// active action's authored buffer window and by drain outcomes, not by time.
/// </summary>
public class PlayerInputBuffer
{
    public bool HasPending => Pending.HasValue;
    public PlayerAction? Pending { get; private set; }

    public void Set(PlayerAction action)
    {
        Pending = action;
    }

    public bool TryConsume(PlayerAction action)
    {
        if (Pending != action) return false;
        Pending = null;
        return true;
    }

    public bool TryTake(out PlayerAction action)
    {
        if (!Pending.HasValue)
        {
            action = default;
            return false;
        }

        action = Pending.Value;
        Pending = null;
        return true;
    }

    public void Clear()
    {
        Pending = null;
    }
}
