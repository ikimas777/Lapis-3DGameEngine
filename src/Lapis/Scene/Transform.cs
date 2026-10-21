using System.Numerics;

namespace Lapis;

/// <summary>
/// Position, rotation and scale of an entity, and its place in the scene hierarchy.
/// <para>
/// Lapis uses a right-handed coordinate system: +X right, +Y up, -Z forward.
/// "Local" values are relative to the parent; the others are in world space.
/// </para>
/// </summary>
public sealed class Transform : Component
{
    private readonly List<Transform> _children = [];
    private Vector3 _localPosition;
    private Quaternion _localRotation = Quaternion.Identity;
    private Vector3 _localScale = Vector3.One;
    private Transform? _parent;
    private Matrix4x4 _localToWorld = Matrix4x4.Identity;
    private bool _dirty = true;

    internal Transform()
    {
    }

    /// <summary>The parent transform, or null when the entity is at the root of the scene.</summary>
    public Transform? Parent => _parent;

    /// <summary>Child transforms in hierarchy order.</summary>
    public IReadOnlyList<Transform> Children => _children;

    /// <summary>Position relative to the parent.</summary>
    public Vector3 LocalPosition
    {
        get => _localPosition;
        set
        {
            _localPosition = value;
            MarkDirty();
        }
    }

    /// <summary>Rotation relative to the parent. Assigned values are normalised.</summary>
    public Quaternion LocalRotation
    {
        get => _localRotation;
        set
        {
            _localRotation = value == default ? Quaternion.Identity : Quaternion.Normalize(value);
            MarkDirty();
        }
    }

    /// <summary>Scale relative to the parent.</summary>
    public Vector3 LocalScale
    {
        get => _localScale;
        set
        {
            _localScale = value;
            MarkDirty();
        }
    }

    /// <summary>Local rotation as Euler angles in degrees (X = pitch, Y = yaw, Z = roll).</summary>
    public Vector3 LocalEulerAngles
    {
        get => MathUtil.ToEulerDegrees(_localRotation);
        set => LocalRotation = MathUtil.FromEulerDegrees(value);
    }

    /// <summary>World-space position.</summary>
    public Vector3 Position
    {
        get => LocalToWorld.Translation;
        set => LocalPosition = _parent is null ? value : Vector3.Transform(value, _parent.WorldToLocal);
    }

    /// <summary>World-space rotation.</summary>
    public Quaternion Rotation
    {
        get => _parent is null ? _localRotation : _parent.Rotation * _localRotation;
        set => LocalRotation = _parent is null ? value : Quaternion.Inverse(_parent.Rotation) * value;
    }

    /// <summary>World rotation as Euler angles in degrees.</summary>
    public Vector3 EulerAngles
    {
        get => MathUtil.ToEulerDegrees(Rotation);
        set => Rotation = MathUtil.FromEulerDegrees(value);
    }

    /// <summary>
    /// Approximate world scale (product of local scales up the hierarchy).
    /// Exact only when no parent is both rotated and non-uniformly scaled.
    /// </summary>
    public Vector3 LossyScale => _parent is null ? _localScale : _parent.LossyScale * _localScale;

    /// <summary>Unit vector pointing forward (-Z) in world space.</summary>
    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Rotation);

    /// <summary>Unit vector pointing right (+X) in world space.</summary>
    public Vector3 Right => Vector3.Transform(Vector3.UnitX, Rotation);

    /// <summary>Unit vector pointing up (+Y) in world space.</summary>
    public Vector3 Up => Vector3.Transform(Vector3.UnitY, Rotation);

    /// <summary>Matrix from local space to the parent's space.</summary>
    public Matrix4x4 LocalMatrix =>
        Matrix4x4.CreateScale(_localScale) * Matrix4x4.CreateFromQuaternion(_localRotation) * Matrix4x4.CreateTranslation(_localPosition);

    /// <summary>Matrix from local space to world space (row-vector convention: <c>v * M</c>).</summary>
    public Matrix4x4 LocalToWorld
    {
        get
        {
            if (_dirty)
            {
                _localToWorld = _parent is null ? LocalMatrix : LocalMatrix * _parent.LocalToWorld;
                _dirty = false;
            }

            return _localToWorld;
        }
    }

    /// <summary>Matrix from world space to local space.</summary>
    public Matrix4x4 WorldToLocal => Matrix4x4.Invert(LocalToWorld, out var inverse) ? inverse : Matrix4x4.Identity;

    /// <summary>Position, sibling index and parent of this transform among its siblings.</summary>
    public int SiblingIndex => Siblings.IndexOf(this);

    private List<Transform> Siblings => _parent?._children ?? entity.Scene.Roots;

    /// <summary>Moves this transform under a new parent (or to the scene root when null).</summary>
    /// <param name="parent">The new parent, or null for the scene root.</param>
    /// <param name="keepWorldTransform">Keep the current world position, rotation and scale.</param>
    /// <exception cref="InvalidOperationException">The parent is this transform or one of its descendants, or belongs to another scene.</exception>
    public void SetParent(Transform? parent, bool keepWorldTransform = true)
    {
        if (parent == _parent)
        {
            return;
        }

        if (parent is not null)
        {
            if (parent.entity.Scene != entity.Scene)
            {
                throw new InvalidOperationException("Cannot parent an entity to an entity in another scene.");
            }

            for (var ancestor = parent; ancestor is not null; ancestor = ancestor._parent)
            {
                if (ancestor == this)
                {
                    throw new InvalidOperationException($"Cannot parent '{entity.Name}' to itself or one of its children.");
                }
            }
        }

        var worldPosition = Position;
        var worldRotation = Rotation;
        var worldScale = LossyScale;

        Siblings.Remove(this);
        _parent = parent;
        Siblings.Add(this);
        MarkDirty();

        if (keepWorldTransform)
        {
            Position = worldPosition;
            Rotation = worldRotation;
            LocalScale = parent is null ? worldScale : SafeDivide(worldScale, parent.LossyScale);
        }
    }

    /// <summary>Moves this transform to <paramref name="index"/> among its siblings.</summary>
    public void SetSiblingIndex(int index)
    {
        var siblings = Siblings;
        siblings.Remove(this);
        siblings.Insert(Math.Clamp(index, 0, siblings.Count), this);
    }

    /// <summary>Returns true when <paramref name="ancestor"/> is this transform's parent, grandparent, and so on.</summary>
    public bool IsChildOf(Transform ancestor)
    {
        for (var current = _parent; current is not null; current = current._parent)
        {
            if (current == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Rotates so that <see cref="Forward"/> points at <paramref name="worldTarget"/>.</summary>
    public void LookAt(Vector3 worldTarget, Vector3? worldUp = null)
    {
        var direction = worldTarget - Position;
        if (direction.LengthSquared() < 1e-12f)
        {
            return;
        }

        var view = Matrix4x4.CreateLookAt(Vector3.Zero, direction, worldUp ?? Vector3.UnitY);
        Rotation = Quaternion.Inverse(Quaternion.CreateFromRotationMatrix(view));
    }

    /// <summary>Converts a point from local space to world space.</summary>
    public Vector3 TransformPoint(Vector3 localPoint) => Vector3.Transform(localPoint, LocalToWorld);

    /// <summary>Converts a point from world space to local space.</summary>
    public Vector3 InverseTransformPoint(Vector3 worldPoint) => Vector3.Transform(worldPoint, WorldToLocal);

    /// <summary>Rotates a direction from local space to world space (ignores scale).</summary>
    public Vector3 TransformDirection(Vector3 localDirection) => Vector3.Transform(localDirection, Rotation);

    internal void DetachFromHierarchy()
    {
        Siblings.Remove(this);
        _parent = null;
    }

    private void MarkDirty()
    {
        if (_dirty)
        {
            return; // children are already dirty: a child can only be clean if its parent is clean
        }

        _dirty = true;
        foreach (var child in _children)
        {
            child.MarkDirty();
        }
    }

    private static Vector3 SafeDivide(Vector3 value, Vector3 divisor) => new(
        divisor.X == 0f ? value.X : value.X / divisor.X,
        divisor.Y == 0f ? value.Y : value.Y / divisor.Y,
        divisor.Z == 0f ? value.Z : value.Z / divisor.Z);
}
