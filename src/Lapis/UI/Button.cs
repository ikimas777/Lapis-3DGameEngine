using System.Reflection;

namespace Lapis;

/// <summary>
/// Makes a UI element clickable. The <see cref="Image"/> on the same entity is tinted to show the button's state
/// (normal, hovered, pressed, disabled). A click is a press and a release of the left mouse button over the button.
/// </summary>
/// <example>
/// From a script:
/// <code>
/// public override void Start()
/// {
///     entity.GetComponent&lt;Button&gt;()!.Clicked += () => Log.Info("Clicked!");
/// }
/// </code>
/// Or without code: drag an entity into <see cref="OnClickTarget"/> and type the name of a public method of one of
/// its scripts (with no parameters) into <see cref="OnClickMethod"/>.
/// </example>
public sealed class Button : Component
{
    /// <summary>Whether the button reacts to the mouse; a disabled button is drawn with <see cref="DisabledColor"/>.</summary>
    public bool Interactable { get; set; } = true;

    /// <summary>Tint of the image while nothing happens.</summary>
    public Color NormalColor { get; set; } = Color.White;

    /// <summary>Tint of the image while the mouse is over the button.</summary>
    public Color HoverColor { get; set; } = new(0.88f, 0.88f, 0.88f);

    /// <summary>Tint of the image while the button is held down.</summary>
    public Color PressedColor { get; set; } = new(0.7f, 0.7f, 0.7f);

    /// <summary>Tint of the image while <see cref="Interactable"/> is off.</summary>
    public Color DisabledColor { get; set; } = new(0.6f, 0.6f, 0.6f, 0.5f);

    /// <summary>Entity whose script gets <see cref="OnClickMethod"/> called on a click.</summary>
    [Tooltip("Entity with the script to call on a click. Together with On Click Method.")]
    public Entity? OnClickTarget { get; set; }

    /// <summary>Name of a public method without parameters, on a script of <see cref="OnClickTarget"/>.</summary>
    [Tooltip("Name of a public method with no parameters, on one of the On Click Target's scripts.")]
    public string OnClickMethod { get; set; } = string.Empty;

    /// <summary>Raised when the button is clicked.</summary>
    public event Action? Clicked;

    /// <summary>True while the mouse is over the button (and nothing above it catches the mouse).</summary>
    public bool IsHovered { get; internal set; }

    /// <summary>True while the button is held down.</summary>
    public bool IsPressed { get; internal set; }

    /// <summary>The tint for the button's current state.</summary>
    public Color CurrentTint => !Interactable ? DisabledColor : IsPressed && IsHovered ? PressedColor : IsHovered ? HoverColor : NormalColor;

    /// <summary>Clicks the button from code: raises <see cref="Clicked"/> and calls <see cref="OnClickMethod"/>.</summary>
    public void Click()
    {
        try
        {
            Clicked?.Invoke();
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"A Clicked handler of button '{entity.Name}' threw an exception.");
        }

        CallOnClickMethod();
    }

    private void CallOnClickMethod()
    {
        if (OnClickTarget is not { } target || string.IsNullOrWhiteSpace(OnClickMethod))
        {
            return;
        }

        var name = OnClickMethod.Trim();
        foreach (var behaviour in target.GetComponents<Behaviour>())
        {
            var method = behaviour.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
            if (method is null)
            {
                continue;
            }

            try
            {
                method.Invoke(behaviour, null);
            }
            catch (TargetInvocationException exception)
            {
                Log.Error(exception.InnerException ?? exception, $"{behaviour.GetType().Name}.{name} (clicked button '{entity.Name}') threw an exception.");
            }

            return;
        }

        Log.Warning($"Button '{entity.Name}': no script on '{target.Name}' has a public method {name}() without parameters.");
    }
}
