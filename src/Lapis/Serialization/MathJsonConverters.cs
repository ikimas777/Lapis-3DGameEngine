using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lapis.Serialization;

/// <summary>
/// Writes vector-like structs as compact JSON number arrays, e.g. <c>[1, 2, 3]</c>.
/// </summary>
internal abstract class FloatArrayConverter<T>(int length) : JsonConverter<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected an array of {length} numbers for {typeof(T).Name}.");
        }

        Span<float> values = stackalloc float[length];
        var count = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (count >= length)
            {
                throw new JsonException($"Too many numbers for {typeof(T).Name}; expected {length}.");
            }

            values[count++] = reader.GetSingle();
        }

        if (count != length)
        {
            throw new JsonException($"Expected {length} numbers for {typeof(T).Name}, got {count}.");
        }

        return Create(values);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        Span<float> values = stackalloc float[length];
        Fill(value, values);

        writer.WriteStartArray();
        foreach (var component in values)
        {
            writer.WriteNumberValue(component);
        }

        writer.WriteEndArray();
    }

    protected abstract T Create(ReadOnlySpan<float> values);

    protected abstract void Fill(T value, Span<float> values);
}

internal sealed class Vector2JsonConverter() : FloatArrayConverter<Vector2>(2)
{
    protected override Vector2 Create(ReadOnlySpan<float> v) => new(v[0], v[1]);

    protected override void Fill(Vector2 value, Span<float> v) => value.CopyTo(v);
}

internal sealed class Vector3JsonConverter() : FloatArrayConverter<Vector3>(3)
{
    protected override Vector3 Create(ReadOnlySpan<float> v) => new(v[0], v[1], v[2]);

    protected override void Fill(Vector3 value, Span<float> v) => value.CopyTo(v);
}

internal sealed class Vector4JsonConverter() : FloatArrayConverter<Vector4>(4)
{
    protected override Vector4 Create(ReadOnlySpan<float> v) => new(v[0], v[1], v[2], v[3]);

    protected override void Fill(Vector4 value, Span<float> v) => value.CopyTo(v);
}

internal sealed class QuaternionJsonConverter() : FloatArrayConverter<Quaternion>(4)
{
    protected override Quaternion Create(ReadOnlySpan<float> v) => new(v[0], v[1], v[2], v[3]);

    protected override void Fill(Quaternion value, Span<float> v)
    {
        v[0] = value.X;
        v[1] = value.Y;
        v[2] = value.Z;
        v[3] = value.W;
    }
}

internal sealed class ColorJsonConverter() : FloatArrayConverter<Color>(4)
{
    protected override Color Create(ReadOnlySpan<float> v) => new(v[0], v[1], v[2], v[3]);

    protected override void Fill(Color value, Span<float> v)
    {
        v[0] = value.R;
        v[1] = value.G;
        v[2] = value.B;
        v[3] = value.A;
    }
}
