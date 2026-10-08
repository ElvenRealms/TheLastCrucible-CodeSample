using System;

/// <summary>
/// Names an event to watch: a component type, and an event on it. A dotted path reaches events on the component's
/// fields and properties, private ones included (for example "_states.Changed"). Used by the Play Mode recorder's
/// watched events.
/// </summary>
public class EventSpec
{
    public string component;
    public string @event;

    /// <summary>
    /// Only watch components whose GameObject name contains this text.
    /// </summary>
    public string objectName;

    /// <summary>
    /// Space-separated terms that must all appear (ignoring case) in the event's description, such as
    /// "Type=AttackTelegraphed".
    /// </summary>
    public string match;

    public bool Matches(string description)
    {
        if (string.IsNullOrEmpty(match))
        {
            return true;
        }
        foreach (var term in match.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (description.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }
        return true;
    }
}
