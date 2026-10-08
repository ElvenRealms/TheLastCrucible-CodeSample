# C# Style Guide

## Architecture

- Managers coordinate through events rather than reaching into owned components. Components
  report upward through public methods and remain unaware of subscribers.
- Separate data, logic, and presentation. Use ScriptableObjects for shared configuration and
  tuning, and serialized fields for per-instance runtime state.
- Give each class one responsibility.

## Naming

- Use `camelCase` for locals, parameters, and serialized fields; `_camelCase` for non-serialized
  private fields; `PascalCase` for types, methods, properties, events, and constants.
- Declare one variable per line. Omit explicit `0`, `null`, and `false` initializers.
- Use `var` when the type is explicit on the same line, and prefer it in `foreach`.
- Use one MonoBehaviour per file, with a matching filename. Component-specific helper types may share it.
- Keep all types in the global namespace, including generated code.

## Formatting

- Use Allman braces, four-space indentation, and braces around every block.
- Use `=>` for single-line read-only properties and accessor blocks for other properties.
- Use properties for simple value access and methods for computation or costly work.
- Keep fields within an Odin group together, with one blank line between groups.
- Keep dependent and similar methods adjacent. Split large classes instead of using `#region`.

## Inspector and Unity

- Expose Inspector fields with `[SerializeField] private`. Static, `const`, and `readonly` fields are not serialized.
- Constrain serialized values with attributes such as `[Range]`. Group related fields with Odin
  attributes by default, or a `[Serializable]` struct.
- Mark serialized ScriptableObject references with `[InlineEditor]`.
- Scripts that log use `[FoldoutGroup("Debug")]`, starting with `showDebugLogs`, followed by
  useful runtime state marked `[ReadOnly]`.
- For live runtime debugging, display the actual state in serialized fields with read-only public
  accessors. Do not rely on custom editors or continuous repaint hooks as a default workaround.
  Use serializable nested fields for state structs and an explicit enum value for unset states
  instead of displaying nullable wrappers. Initialize runtime fields when a new session starts.
- Avoid mutable static state in runtime code. Where it is needed, reset it in a
  `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` method so it
  stays correct if domain reload on entering Play Mode is disabled.
- Set an explicit normalized start time, normally `0`, for replayable one-shot Animancer transitions.
- Fully qualify uGUI component types, such as `UnityEngine.UI.Image`; omit `using UnityEngine.UI;`.

## Member order

- Order members: fields, properties, events/delegates, MonoBehaviour messages, public methods, private methods.
- Put high-level methods before the lower-level methods implementing them.

## Methods

- Each method performs one action or answers one question.
- Minimize arguments and overloads. Give each overload a distinct argument count.
- Keep behavior within the method's named purpose. Prefer passing by value; use `out` or `ref`
  results only when producing them is the method's whole purpose.
- Extend sealed and UnityEngine types with extension methods, grouped by target type
  (for example, `TransformExtensions`).
- Keep each piece of logic in one place. Extract shared logic; identical code alone need not imply shared logic.

## Comments

- Explain non-obvious reasons and constraints, not what the code already says.
- Refactor convoluted logic instead of explaining it with long comments.
- Use `[Tooltip]` instead of comments on serialized fields.
- Use `/// <summary>` on types and public methods where IntelliSense helps callers.
- Remove commented-out code, stale TODOs, dev journals, author attributions, and decorative banners.
  Retained TODOs include a name and date.
