# Animation

Lapis plays the animations stored in glTF models (`.gltf` / `.glb`): characters and creatures with a skeleton
(skinned meshes), and objects whose parts move on their own (a door on its hinge, a turning fan). The animations
come with the model; Lapis does not edit them.

## Exporting from Blender

**File > Export > glTF 2.0**, with:

| Setting | Value |
|---|---|
| Format | **glTF Binary (.glb)** (one file with the textures inside) |
| Include | the armature and its mesh (Selected Objects, or the whole scene) |
| Transform | **+Y Up** (the default) |
| Data > Mesh | **Apply Modifiers** as you need; normals and UVs on |
| Data > Armature / Skinning | on |
| Animation | on; **Actions** (or NLA strips): every action becomes one animation with its name |

Before exporting:

- Select the mesh and the armature and use **Object > Apply > All Transforms** (Ctrl+A), so the model is not
  rotated or scaled differently from what you see.
- Keep Blender's unit at **1 m**; Lapis also uses metres.
- A vertex may follow at most **4 bones** (the exporter keeps the 4 strongest), and a model may have at most
  **128 bones**, plenty for characters and creatures.
- Models from other programs or stores that only come as **FBX** (e.g. Mixamo): import the FBX into Blender, check
  it, and export it as `.glb` as above.

## Using an animated model

Drop the `.glb` into `Assets`, then drag it into the scene. An animated model gets an **Animator** component next
to its Mesh Renderer, which plays the first animation when the game runs.

| Setting | Meaning |
|---|---|
| **Clip** | The animation played when the game starts (chosen from the model's animations). |
| **Play On Start** | Start that animation by itself. |
| **Loop** | Start over at the end; otherwise stop on the last pose. |
| **Speed** | 1 = as made, 2 = twice as fast, 0 = frozen. |
| **Preview** | Buttons that play each animation in the Scene view without running the game; **Stop** returns to the rest pose. |

Without an Animator, the model stands in its rest pose (as in Blender's edit mode). The model's colliders and
the Set Origin tool use the rest pose too.

## From scripts

```csharp
using Lapis;

/// <summary>Walks while a movement key is held, runs with Shift, and steps with a sound.</summary>
public class CreatureAnimation : Behaviour
{
    private Animator? _animator;

    public override void Start()
    {
        _animator = entity.GetComponent<Animator>();
        _animator!.AddEvent("Walk", 0.35f, () => Log.Info("step"));
    }

    public override void Update()
    {
        var moving = Input.GetAxis("Vertical") != 0f;
        var running = Input.GetKey(KeyCode.LeftShift);
        _animator!.Play(!moving ? "Idle" : running ? "Run" : "Walk"); // blends over 0.2 s
    }
}
```

| API | Purpose |
|---|---|
| `Play(name, fade = 0.2f)` | Plays an animation from its start, blending from the current pose over `fade` seconds. Playing the animation that already plays changes nothing. |
| `Restart()`, `Stop()` | Start the current animation over; stop on the current pose. |
| `CurrentClip`, `IsPlaying`, `Time`, `NormalizedTime` | What plays, whether it plays, seconds into it, and 0–1 through it. |
| `ClipNames`, `ClipLength(name)` | The model's animations and their lengths. |
| `AddEvent(name, seconds, action)`, `ClearEvents()` | Call `action` each time the animation passes that moment, e.g. footsteps or the hit of an attack. |
| `Speed`, `Loop` | As in the Inspector. |

Animations follow game time: they stop while the game is paused.

## Notes

- The animation moves the model's parts, not the entity: a walk animation walks on the spot. Move the entity
  (or its Character Controller) from a script.
- glTF smooth curves (cubic splines) are played as straight lines between their keys, which looks the same for
  usual exports with a key on every frame.
- Shape keys (morph targets) are not played yet.
