using System.Collections.Concurrent;
using System.Reflection;

namespace Lapis.Serialization;

/// <summary>
/// A field or property that is saved with the scene and edited in the editor.
/// </summary>
/// <remarks>
/// A member is serialized when it is an instance field or property that can be both read and written and:
/// <list type="bullet">
/// <item>it is public (and, for a property, both accessors are public), or</item>
/// <item>it has <see cref="SerializeAttribute"/>,</item>
/// </list>
/// and it does not have <see cref="DoNotSerializeAttribute"/>. Read-only fields are never serialized.
/// </remarks>
public sealed class SerializedMember
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<SerializedMember>> Cache = new();

    private readonly FieldInfo? _field;
    private readonly PropertyInfo? _property;

    private SerializedMember(MemberInfo member, Type valueType)
    {
        Member = member;
        ValueType = valueType;
        _field = member as FieldInfo;
        _property = member as PropertyInfo;
        Key = ToKey(member.Name);
    }

    /// <summary>The underlying field or property.</summary>
    public MemberInfo Member { get; }

    /// <summary>The C# name of the member.</summary>
    public string Name => Member.Name;

    /// <summary>The name used in JSON (camelCase, without a leading underscore).</summary>
    public string Key { get; }

    /// <summary>Type of the stored value.</summary>
    public Type ValueType { get; }

    /// <summary>Returns the attribute of type <typeparamref name="T"/> on the member, or null.</summary>
    public T? GetAttribute<T>()
        where T : Attribute => Member.GetCustomAttribute<T>();

    /// <summary>Reads the value from <paramref name="target"/>.</summary>
    public object? GetValue(object target) => _field is not null ? _field.GetValue(target) : _property!.GetValue(target);

    /// <summary>Writes the value to <paramref name="target"/>.</summary>
    public void SetValue(object target, object? value)
    {
        if (_field is not null)
        {
            _field.SetValue(target, value);
        }
        else
        {
            _property!.SetValue(target, value);
        }
    }

    /// <summary>Serialized members of <paramref name="type"/>, base-class members first.</summary>
    public static IReadOnlyList<SerializedMember> For(Type type) => Cache.GetOrAdd(type, Discover);

    /// <summary>Forgets cached member lists, e.g. after game scripts are reloaded.</summary>
    public static void ClearCache() => Cache.Clear();

    private static List<SerializedMember> Discover(Type type)
    {
        var hierarchy = new Stack<Type>();
        for (var current = type; current is not null && current != typeof(Component) && current != typeof(object); current = current.BaseType)
        {
            hierarchy.Push(current);
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var members = new List<SerializedMember>();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var level in hierarchy)
        {
            foreach (var member in level.GetMembers(flags).OrderBy(member => member.MetadataToken))
            {
                var serialized = member switch
                {
                    FieldInfo field when IsSerializable(field) => new SerializedMember(field, field.FieldType),
                    PropertyInfo property when IsSerializable(property) => new SerializedMember(property, property.PropertyType),
                    _ => null,
                };

                if (serialized is not null && keys.Add(serialized.Key))
                {
                    members.Add(serialized);
                }
            }
        }

        return members;
    }

    private static bool IsSerializable(FieldInfo field)
    {
        if (field.IsInitOnly || field.IsLiteral || field.IsDefined(typeof(DoNotSerializeAttribute)) ||
            field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)))
        {
            return false;
        }

        return field.IsPublic || field.IsDefined(typeof(SerializeAttribute));
    }

    private static bool IsSerializable(PropertyInfo property)
    {
        if (property.GetIndexParameters().Length > 0 || property.IsDefined(typeof(DoNotSerializeAttribute)))
        {
            return false;
        }

        var getter = property.GetMethod;
        var setter = property.SetMethod;
        if (getter is null || setter is null)
        {
            return false;
        }

        return (getter.IsPublic && setter.IsPublic) || property.IsDefined(typeof(SerializeAttribute));
    }

    private static string ToKey(string name)
    {
        var trimmed = name.TrimStart('_');
        if (trimmed.Length == 0)
        {
            return name;
        }

        return char.ToLowerInvariant(trimmed[0]) + trimmed[1..];
    }
}
