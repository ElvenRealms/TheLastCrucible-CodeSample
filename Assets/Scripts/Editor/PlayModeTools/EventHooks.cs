using System;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Reflection helpers that let <see cref="EventWatcher"/> observe events on any component without a compile-time
/// reference to it, so the recorder stays independent of the code it watches.
/// </summary>
public static class EventHooks
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// Finds a component type by short or full name across the loaded assemblies.
    /// </summary>
    public static Type FindComponentType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in LoadableTypes(assembly))
            {
                if (typeof(Component).IsAssignableFrom(type) && (type.Name == name || type.FullName == name))
                {
                    return type;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Checks a dotted event path against a type before any instance exists. Returns the problem, or null.
    /// </summary>
    public static string ValidatePath(Type type, string path)
    {
        var segments = path.Split('.');
        for (int i = 0; i < segments.Length - 1; i++)
        {
            type = MemberType(type, segments[i]);
            if (type == null)
            {
                return $"no field or property '{segments[i]}' on the path '{path}'";
            }
        }
        var evt = type.GetEvent(segments[segments.Length - 1], InstanceMembers);
        if (evt == null)
        {
            return $"no event '{segments[segments.Length - 1]}' on {type.Name}";
        }
        return evt.EventHandlerType.GetMethod("Invoke").ReturnType == typeof(void) ? null : $"event '{path}' returns a value";
    }

    /// <summary>
    /// Follows a dotted path from a component to an event and returns the object that owns the event.
    /// </summary>
    public static bool TryResolve(Component component, string path, out object owner, out EventInfo evt)
    {
        evt = null;
        var separator = path.LastIndexOf('.');
        owner = separator < 0 ? component : ReadPath(component, path.Substring(0, separator));
        if (owner == null)
        {
            return false;
        }
        evt = owner.GetType().GetEvent(path.Substring(separator + 1), InstanceMembers);
        return evt != null;
    }

    /// <summary>
    /// Reads a dotted path of fields and properties, private ones included (for example "_states.Poise").
    /// Returns null when a step is missing or null.
    /// </summary>
    public static object ReadPath(object root, string path)
    {
        var value = root;
        foreach (var segment in path.Split('.'))
        {
            if (value == null)
            {
                return null;
            }
            value = MemberValue(value, segment);
        }
        return value;
    }

    /// <summary>
    /// Formats a value as <see cref="Describe"/> does for one argument.
    /// </summary>
    public static string Format(object value)
    {
        var text = new StringBuilder();
        AppendValue(text, value, true);
        return text.ToString();
    }

    /// <summary>
    /// Builds a delegate of the event's handler type that boxes its arguments and forwards them to the callback.
    /// Handles by-reference (`in`) parameters, such as a custom event delegate that takes its arguments by reference.
    /// </summary>
    public static Delegate CreateHandler(Type handlerType, Action<object[]> callback)
    {
        var parameters = handlerType.GetMethod("Invoke").GetParameters()
            .Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
        var boxed = Expression.NewArrayInit(typeof(object), parameters.Select(p => (Expression)Expression.Convert(p, typeof(object))));
        var body = Expression.Invoke(Expression.Constant(callback), boxed);
        return Expression.Lambda(handlerType, body, parameters).Compile();
    }

    /// <summary>
    /// Describes event arguments as "name=value" pairs. Structs and classes without their own ToString are expanded
    /// one level into their public fields and properties, and Unity objects show their name.
    /// </summary>
    public static string Describe(Type handlerType, object[] args)
    {
        var parameters = handlerType.GetMethod("Invoke").GetParameters();
        var text = new StringBuilder();
        for (int i = 0; i < args.Length; i++)
        {
            text.Append(i == 0 ? "" : " ").Append(parameters[i].Name).Append('=');
            AppendValue(text, args[i], true);
        }
        return text.ToString();
    }

    private static void AppendValue(StringBuilder text, object value, bool expand)
    {
        if (value == null)
        {
            text.Append("null");
            return;
        }
        if (value is UnityEngine.Object unityObject)
        {
            text.Append(unityObject ? unityObject.name : "destroyed");
            return;
        }
        var type = value.GetType();
        if (!expand || type.IsPrimitive || type.IsEnum || value is string || HasOwnToString(type))
        {
            // Invariant, so numbers read the same (and parse) on machines with a decimal comma.
            text.Append(value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value);
            return;
        }
        text.Append('{');
        var first = true;
        foreach (var member in type.GetMembers(BindingFlags.Instance | BindingFlags.Public))
        {
            object memberValue;
            if (member is FieldInfo field)
            {
                memberValue = field.GetValue(value);
            }
            else if (member is PropertyInfo property && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                memberValue = property.GetValue(value);
            }
            else
            {
                continue;
            }
            text.Append(first ? "" : " ").Append(member.Name).Append('=');
            AppendValue(text, memberValue, false);
            first = false;
        }
        text.Append('}');
    }

    private static bool HasOwnToString(Type type)
    {
        var declaring = type.GetMethod("ToString", Type.EmptyTypes).DeclaringType;
        return declaring != typeof(object) && declaring != typeof(ValueType);
    }

    private static Type MemberType(Type type, string name)
    {
        var field = type.GetField(name, InstanceMembers);
        if (field != null)
        {
            return field.FieldType;
        }
        return type.GetProperty(name, InstanceMembers)?.PropertyType;
    }

    private static object MemberValue(object owner, string name)
    {
        var type = owner.GetType();
        var field = type.GetField(name, InstanceMembers);
        if (field != null)
        {
            return field.GetValue(owner);
        }
        return type.GetProperty(name, InstanceMembers)?.GetValue(owner);
    }

    private static Type[] LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t != null).ToArray();
        }
    }
}
