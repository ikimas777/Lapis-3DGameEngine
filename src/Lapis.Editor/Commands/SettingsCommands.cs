using Lapis.Assets;
using Lapis.Serialization;

namespace Lapis.Editor.Commands;

/// <summary>Changes one scene-wide setting (<see cref="Scene.Environment"/> or <see cref="Scene.Physics"/>).</summary>
internal sealed class SetSceneSettingCommand : IEditorCommand
{
    private readonly bool _isPhysics;
    private readonly string _memberKey;
    private readonly MemberValue _before;
    private MemberValue _after;

    /// <param name="settings">The scene's <see cref="SceneEnvironment"/> or <see cref="ScenePhysics"/>.</param>
    /// <param name="member">The edited member.</param>
    /// <param name="before">Value before the edit.</param>
    /// <param name="after">Value after the edit.</param>
    public SetSceneSettingCommand(object settings, SerializedMember member, object? before, object? after)
    {
        _isPhysics = settings is ScenePhysics;
        _memberKey = member.Key;
        _before = MemberValue.Capture(before, member.ValueType);
        _after = MemberValue.Capture(after, member.ValueType);
        Name = $"Change {PropertyDrawers.PropertyGrid.Nicify(member.Name)}";
    }

    public string Name { get; }

    public void Execute(Scene scene) => Apply(scene, _after);

    public void Undo(Scene scene) => Apply(scene, _before);

    public bool TryMerge(IEditorCommand next)
    {
        if (next is not SetSceneSettingCommand other || other._isPhysics != _isPhysics || other._memberKey != _memberKey)
        {
            return false;
        }

        _after = other._after;
        return true;
    }

    private void Apply(Scene scene, MemberValue value)
    {
        object settings = _isPhysics ? scene.Physics : scene.Environment;
        var member = SerializedMember.For(settings.GetType()).First(m => m.Key == _memberKey);
        member.SetValue(settings, value.Restore(scene, member.ValueType));
    }
}

/// <summary>Changes one property of a material asset and saves the file.</summary>
internal sealed class SetMaterialMemberCommand : IEditorCommand
{
    private readonly Guid _assetId;
    private readonly string _memberKey;
    private readonly MemberValue _before;
    private MemberValue _after;

    public SetMaterialMemberCommand(AssetInfo asset, SerializedMember member, object? before, object? after)
    {
        _assetId = asset.Id;
        _memberKey = member.Key;
        _before = MemberValue.Capture(before, member.ValueType);
        _after = MemberValue.Capture(after, member.ValueType);
        Name = $"Change {PropertyDrawers.PropertyGrid.Nicify(member.Name)} of {asset.Name}";
    }

    public string Name { get; }

    public bool ChangesScene => false;

    public void Execute(Scene scene) => Apply(scene, _after);

    public void Undo(Scene scene) => Apply(scene, _before);

    public bool TryMerge(IEditorCommand next)
    {
        if (next is not SetMaterialMemberCommand other || other._assetId != _assetId || other._memberKey != _memberKey)
        {
            return false;
        }

        _after = other._after;
        return true;
    }

    private void Apply(Scene scene, MemberValue value)
    {
        var assets = AssetDatabase.Active ?? throw new InvalidOperationException("No project is open.");
        var asset = assets.Find(_assetId) ?? throw new InvalidOperationException("The material no longer exists.");
        var material = Material.Load(asset.FullPath);
        var member = SerializedMember.For(typeof(Material)).First(m => m.Key == _memberKey);
        member.SetValue(material, value.Restore(scene, member.ValueType));
        material.Save(asset.FullPath);
        assets.NotifyChanged(_assetId);
    }
}
