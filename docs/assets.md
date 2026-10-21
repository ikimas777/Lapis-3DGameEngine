# Assets

Everything a game uses lives in the project's `Assets` folder and appears in the **Assets** panel
(bottom of the editor, next to the Console).

## Supported files

| Type | Extensions | Notes |
|---|---|---|
| Model | `.gltf`, `.glb`, `.obj`, `.stl` | All parts are merged into one mesh. The first material's colour and texture are used. STL is converted from Z-up to Y-up. |
| Texture | `.png`, `.jpg`, `.jpeg`, `.tga`, `.bmp` | Treated as sRGB colour, with mipmaps and repeat wrapping. |
| Material | `.lmat` | Colour, texture, metallic and roughness shared by many objects. Edited in the Inspector. |
| Scene | `.lscene` | See [scene-format.md](scene-format.md). |
| Script | `.cs` | See [scripting.md](scripting.md). |

## Bringing files in

- Drag files or folders from Explorer (or a Linux file manager) onto the editor window: they are copied into the folder shown
  in the Assets panel.
- Or use **Import...** in the panel's toolbar, or copy files into `Assets` yourself; the editor notices
  changes on disk within a moment.
- glTF: keep `.bin` and image files next to the `.gltf` file. GLB files carry everything inside.
- OBJ: keep the `.mtl` file and textures next to it (paths in the `.mtl` are relative to it).

## IDs and `.lmeta` files

Each asset gets a `.lmeta` file next to it containing a GUID. Scenes and materials refer to assets by this
id (`asset:…`), so renaming and moving files inside the editor never breaks references. When moving files
outside the editor, move the `.lmeta` file together with its asset; commit `.lmeta` files to version control.

## Surfaces and lighting

Lapis uses physically based shading. A surface has a **Color** (optionally multiplied by a texture),
**Metallic** (0 for plastic, wood, stone; 1 for metal) and **Roughness** (0 = polished, 1 = matte).
The first directional light is the sun: it casts soft shadows (turn off with **Cast Shadows** on the light or on
individual Mesh Renderers) and appears as a sun disk in the sky. Sky colours, ambient light and the shadow
distance are scene settings: click empty space in the Hierarchy to see them in the Inspector.

## Using assets

| Drag … | onto … | Result |
|---|---|---|
| a model | the Scene view or empty space in the Hierarchy | a new object with a Mesh Renderer showing the model |
| a model | an object in the Hierarchy | the new object becomes its child |
| a material or texture | an object in the Scene view or Hierarchy | applied to its Mesh Renderer |
| any asset | a matching field in the Inspector | assigned to that field |

Clicking a slot field (Mesh Id, Material Id, Texture Id) opens a list of matching assets; right-click clears it.
A Mesh Renderer with a material uses the material's colour, texture, metallic and roughness instead of its own.

Right-click in the Assets panel to create folders, scripts, materials and scenes, or to rename, delete
(to the Recycle Bin, or the Trash on Linux) and show items in Explorer / the file manager. Double-click opens folders and scenes, and opens scripts
in VS Code. Selecting an asset shows a preview and details in the Inspector.
