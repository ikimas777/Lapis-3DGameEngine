# Prefabs

A prefab is a ready-made object saved as a `.lprefab` file in `Assets`: a door with its frame and hinge script, a
lamp with its light, a whole corridor module. Place it in scenes as many times as you like; every copy follows the
prefab, so changing the prefab changes all of them.

## Making a prefab

Drag an entity from the **Hierarchy** into the **Assets** panel (onto the files, or onto a folder). Lapis saves it,
with its children and components, as `<Name>.lprefab` in that folder, and the entity becomes a copy of the new
prefab.

## Placing copies

Drag a `.lprefab` from the Assets panel:

- into the **Scene view**: the copy lands where the cursor points at the ground;
- into the **Hierarchy**: onto empty space for the top level, or onto an entity to place it under that entity.

Copies of prefabs (instances) are **blue** in the Hierarchy.

## What can change in a copy

Like a sealed box, a copy takes its contents from the prefab. In the scene you can only change its **root**:
name, tag, active flag, Hierarchy folder and **Transform** (move, rotate and scale it; for UI, its Rect Transform).
Its components and children are shown in the Inspector but greyed out, and nothing can be added to, removed
from or moved into or out of it.

Clicking a copy in the Scene view selects the whole copy; click again to select the part under the cursor (to
look at it).

To change a copy, the Inspector (and the Hierarchy's right-click menu) offers:

| Button | What it does |
|---|---|
| **Open Prefab** | Edits the prefab itself (also: double-click the `.lprefab`). Every copy changes. |
| **Unpack** | Turns this copy into ordinary entities you can change freely. It no longer follows the prefab. Undo brings the link back. |
| **Select Asset** | Shows the prefab's file in the Assets panel. |

## Editing a prefab

**Open Prefab** shows only the prefab's entities in the Hierarchy and the Scene view, with a **Prefab: Name** bar
at the top of the Hierarchy. Change anything as in a scene, then:

- **Save** (or Ctrl+S) writes the prefab; every copy in your scenes changes.
- **Back to Scene** returns to the scene where you were, with its selection, its unsaved changes and its undo
  history. With unsaved changes to the prefab, it asks whether to save them.

A prefab has exactly one top-level entity: put everything else under it. Play mode is not available while a
prefab is open.

## Prefabs in scripts

Scripts place copies while the game runs, e.g. enemies, bullets or pickups. Keep the prefab in a field and drop
the `.lprefab` file onto it in the Inspector:

```csharp
using System.Numerics;
using Lapis;
using Lapis.Assets;

/// <summary>Spawns an enemy every few seconds.</summary>
public class EnemySpawner : Behaviour
{
    [AssetReference(AssetType.Prefab)]
    public string enemy = "";

    [Range(1, 30)]
    public float interval = 5f;

    private float _timer;

    public override void Update()
    {
        _timer += Time.DeltaTime;
        if (_timer >= interval)
        {
            _timer = 0f;
            Instantiate(enemy, transform.Position + new Vector3(0f, 0f, -2f));
        }
    }
}
```

`Instantiate(prefab, position, rotation, parent)` returns the root of the new copy; `rotation` and `parent` are
optional. Copies made while playing are ordinary entities of the running game (they disappear when it stops).
`entity.Prefab` tells which prefab an entity is a copy of (null when it is none), and `entity.PrefabInstanceRoot`
finds the root of the copy an entity belongs to.

## How it works

- A scene stores a copy as its root entity with a `prefab` reference, followed by its contents. When the scene
  is opened, the contents are rebuilt from the prefab file, so changes to the prefab always reach every copy. The
  stored contents are used only when the prefab file is missing.
- The entities inside a copy keep the same ids every time it is rebuilt, so scripts and fields that refer to them
  keep working.
- Prefabs inside a prefab are not supported yet: a copy placed inside a prefab while editing it becomes part of
  it (unpacked) when the prefab is saved.
