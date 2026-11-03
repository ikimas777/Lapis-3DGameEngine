using System.Numerics;

namespace Lapis;

/// <summary>An axis-aligned rectangle: top-left corner (<see cref="X"/>, <see cref="Y"/>), Y pointing down.</summary>
public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    /// <summary>Top-left corner.</summary>
    public Vector2 Min => new(X, Y);

    /// <summary>Bottom-right corner.</summary>
    public Vector2 Max => new(X + Width, Y + Height);

    /// <summary>Width and height.</summary>
    public Vector2 Size => new(Width, Height);

    /// <summary>The middle of the rectangle.</summary>
    public Vector2 Center => new(X + Width * 0.5f, Y + Height * 0.5f);

    /// <summary>The rectangle between two corners.</summary>
    public static Rect FromMinMax(Vector2 min, Vector2 max) => new(min.X, min.Y, max.X - min.X, max.Y - min.Y);

    /// <summary>True when the point lies inside the rectangle (edges included).</summary>
    public bool Contains(Vector2 point) => point.X >= X && point.Y >= Y && point.X <= X + Width && point.Y <= Y + Height;
}
