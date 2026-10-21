# Scene file format (`.lscene`)

Scenes are stored as UTF-8 JSON so they are readable and produce small diffs in version control.
The reader and writer live in `src/Lapis/Serialization/SceneSerializer.cs`.

## Top level

```json
{
  "format": "lapis.scene",
  "version": 1,
  "name": "Demo",
  "environment": { "ambientSkyColor": [0.55, 0.62, 0.7, 1], "ambientGroundColor": [0.38, 0.33, 0.3, 1], "ambientIntensity": 1 },
  "entities": [ ... ]
}
```

| Field | Meaning |
|---|---|
| `format` | Always `lapis.scene`. |
| `version` | Format version. Files with a newer version than the engine supports are rejected. |
| `name` | Scene name. |
| `environment` | Scene-wide lighting (`SceneEnvironment`). |
| `entities` | Every entity, depth-first: a parent always comes before its children, and siblings keep their order. |

## Entity

```json
{
  "id": "0ed5aaaf-7504-46b0-ab66-ab56463b571e",
  "name": "Box Marker",
  "parent": "6a1f0c2e-...",
  "active": false,
  "transform": { "position": [0, 0.75, 0], "rotation": [0, 0, 0, 1], "scale": [0.3, 0.3, 0.3] },
  "components": [
    { "type": "Lapis.MeshRenderer", "data": { "meshId": "builtin:sphere", "color": [0.94, 0.78, 0.24, 1], "shininess": 32 } }
  ]
}
```

| Field | Meaning |
|---|---|
| `id` | GUID that stays the same across saves. References between objects use it. |
| `parent` | Id of the parent entity. Omitted for root entities. |
| `active` | Omitted when `true`. |
| `transform` | Local position, rotation (quaternion `x, y, z, w`) and scale. |
| `components` | Every component except the transform: the full C# type name and its saved members. |

## Values

| C# type | JSON |
|---|---|
| `float`, `int`, `bool`, `string` | number, number, `true`/`false`, string |
| enums | the member name, e.g. `"Point"` |
| `Vector2` / `Vector3` / `Vector4` | `[x, y]` / `[x, y, z]` / `[x, y, z, w]` |
| `Quaternion` | `[x, y, z, w]` |
| `Color` | `[r, g, b, a]` (sRGB, 0–1) |
| `Entity` | entity id, or `null` |
| any component type | id of the entity that owns it, or `null`; resolved with `GetComponent` on load |

Member names are camelCase; a leading underscore is removed (`_secret` → `secret`).

## Which members are saved

A field or property is saved when it can be read and written and either:

- it is `public` (for properties, both accessors are public), or
- it has `[Serialize]`,

and it does not have `[DoNotSerialize]`. `readonly` fields and get-only properties are never saved.

## Compatibility rules

- **Members added later** keep their default value when an older file is loaded.
- **Members removed later** are ignored when loading.
- **Unknown component types** (for example a script that was renamed or does not compile) are loaded as
  `MissingComponent`, and their data is written back unchanged when the scene is saved.
- A value that cannot be read (wrong type) is logged as a warning and the default value is kept.
