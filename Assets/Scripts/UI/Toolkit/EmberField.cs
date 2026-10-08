using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Draws slowly rising embers as a single mesh. The stylesheet supplies the sprite and tint
/// through <c>--ember-texture</c> and <c>--ember-color</c>.
/// </summary>
[UxmlElement]
public partial class EmberField : VisualElement
{
    private struct Ember
    {
        public Vector2 position;
        public float riseSpeed;
        public float swayPhase;
        public float size;
        public float age;
        public float lifetime;
    }

    private const int EmberCount = 56;
    private const long TickMilliseconds = 16;
    private const float MaxTickSeconds = 0.1f;
    private const float SwaySpeed = 0.9f;
    private const float SwayDistance = 0.006f;
    private const float FadeInPortion = 0.15f;
    private const float FadeOutPortion = 0.35f;

    private static readonly CustomStyleProperty<Texture2D> TextureProperty = new("--ember-texture");
    private static readonly CustomStyleProperty<Color> ColorProperty = new("--ember-color");

    private readonly Ember[] _embers = new Ember[EmberCount];
    private Texture2D _texture;
    private Color _color = Color.white;

    public EmberField()
    {
        pickingMode = PickingMode.Ignore;
        for (int i = 0; i < _embers.Length; i++)
        {
            // Start part-way through each life so the field is already full when it first shows.
            Ember ember = Spawn();
            ember.age = Random.value * ember.lifetime;
            ember.position.y -= ember.riseSpeed * ember.age;
            _embers[i] = ember;
        }

        generateVisualContent += Draw;
        RegisterCallback<CustomStyleResolvedEvent>(ReadStyle);
        schedule.Execute(Advance).Every(TickMilliseconds);
    }

    // Positions are fractions of the element's size, so the field survives a resize.
    private static Ember Spawn()
    {
        return new Ember
        {
            position = new Vector2(Random.value, Random.Range(0.35f, 1.05f)),
            riseSpeed = Random.Range(0.015f, 0.05f),
            swayPhase = Random.value * Mathf.PI * 2f,
            size = Random.Range(4f, 12f),
            lifetime = Random.Range(5f, 11f),
        };
    }

    private void ReadStyle(CustomStyleResolvedEvent evt)
    {
        evt.customStyle.TryGetValue(TextureProperty, out _texture);
        if (!evt.customStyle.TryGetValue(ColorProperty, out _color))
        {
            _color = Color.white;
        }

        MarkDirtyRepaint();
    }

    private void Advance(TimerState timer)
    {
        float deltaTime = Mathf.Min(timer.deltaTime / 1000f, MaxTickSeconds);
        for (int i = 0; i < _embers.Length; i++)
        {
            Ember ember = _embers[i];
            ember.age += deltaTime;
            if (ember.age >= ember.lifetime)
            {
                ember = Spawn();
            }

            ember.position.x += Mathf.Sin(ember.swayPhase + ember.age * SwaySpeed) * SwayDistance * deltaTime;
            ember.position.y -= ember.riseSpeed * deltaTime;
            _embers[i] = ember;
        }

        MarkDirtyRepaint();
    }

    private void Draw(MeshGenerationContext context)
    {
        Rect rect = contentRect;
        if (_texture == null || rect.width < 1f || rect.height < 1f)
        {
            return;
        }

        MeshWriteData mesh = context.Allocate(_embers.Length * 4, _embers.Length * 6, _texture);
        for (int i = 0; i < _embers.Length; i++)
        {
            Ember ember = _embers[i];
            float x = ember.position.x * rect.width;
            float y = ember.position.y * rect.height;
            float half = ember.size * 0.5f;
            Color tint = _color;
            tint.a *= Opacity(ember);

            mesh.SetNextVertex(Corner(x - half, y + half, 0f, 0f, tint));
            mesh.SetNextVertex(Corner(x - half, y - half, 0f, 1f, tint));
            mesh.SetNextVertex(Corner(x + half, y - half, 1f, 1f, tint));
            mesh.SetNextVertex(Corner(x + half, y + half, 1f, 0f, tint));

            int first = i * 4;
            mesh.SetNextIndex((ushort)first);
            mesh.SetNextIndex((ushort)(first + 1));
            mesh.SetNextIndex((ushort)(first + 2));
            mesh.SetNextIndex((ushort)(first + 2));
            mesh.SetNextIndex((ushort)(first + 3));
            mesh.SetNextIndex((ushort)first);
        }
    }

    private static float Opacity(Ember ember)
    {
        float life = ember.age / ember.lifetime;
        float envelope = Mathf.Clamp01(life / FadeInPortion) * Mathf.Clamp01((1f - life) / FadeOutPortion);
        float twinkle = 0.75f + 0.25f * Mathf.Sin(ember.swayPhase + ember.age * 5f);
        return envelope * twinkle;
    }

    private static Vertex Corner(float x, float y, float u, float v, Color tint)
    {
        return new Vertex
        {
            position = new Vector3(x, y, Vertex.nearZ),
            uv = new Vector2(u, v),
            tint = tint,
        };
    }
}
