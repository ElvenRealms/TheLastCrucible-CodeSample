using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Hooks one <see cref="EventSpec"/> on every matching component in the loaded scenes and forwards each raise with
/// the source GameObject's name and a description of the arguments. Call <see cref="Rescan"/> periodically to hook
/// components that appear later, and <see cref="Unhook"/> when done.
/// </summary>
public sealed class EventWatcher
{
    private struct Subscription
    {
        public EventInfo evt;
        public object owner;
        public Delegate handler;
    }

    private readonly EventSpec _spec;
    private readonly Type _componentType;
    private readonly Action<string, string> _onRaised;
    private readonly HashSet<Component> _hooked = new HashSet<Component>();
    private readonly List<Subscription> _subscriptions = new List<Subscription>();

    private EventWatcher(EventSpec spec, Type componentType, Action<string, string> onRaised)
    {
        _spec = spec;
        _componentType = componentType;
        _onRaised = onRaised;
    }

    public int HookedCount => _hooked.Count;

    /// <summary>
    /// Creates a watcher that calls back with (source name, description) for every raise, matching or not.
    /// Returns null and sets the error when the component type or event path doesn't resolve.
    /// </summary>
    public static EventWatcher Create(EventSpec spec, Action<string, string> onRaised, out string error)
    {
        if (string.IsNullOrEmpty(spec.component) || string.IsNullOrEmpty(spec.@event))
        {
            error = "set component and event";
            return null;
        }
        var type = EventHooks.FindComponentType(spec.component);
        error = type == null ? $"no component type named '{spec.component}'" : EventHooks.ValidatePath(type, spec.@event);
        return error == null ? new EventWatcher(spec, type, onRaised) : null;
    }

    public void Rescan()
    {
        foreach (var found in UnityEngine.Object.FindObjectsByType(_componentType))
        {
            var component = (Component)found;
            var objectName = _spec.objectName;
            if (!string.IsNullOrEmpty(objectName) && component.gameObject.name.IndexOf(objectName, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            if (_hooked.Add(component))
            {
                Hook(component);
            }
        }
    }

    public void Unhook()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.evt.RemoveEventHandler(subscription.owner, subscription.handler);
        }
        _subscriptions.Clear();
    }

    private void Hook(Component component)
    {
        if (!EventHooks.TryResolve(component, _spec.@event, out var owner, out var evt))
        {
            return;
        }
        var source = component.gameObject.name;
        var handlerType = evt.EventHandlerType;
        var handler = EventHooks.CreateHandler(handlerType, args => _onRaised(source, EventHooks.Describe(handlerType, args)));
        evt.AddEventHandler(owner, handler);
        _subscriptions.Add(new Subscription { evt = evt, owner = owner, handler = handler });
    }
}
