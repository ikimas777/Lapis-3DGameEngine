using System.Globalization;
using System.Numerics;
using Lapis.Rendering;

namespace Lapis.Assets.Importers;

/// <summary>
/// Reads Wavefront OBJ files: positions, texture coordinates, normals and polygon faces (triangulated).
/// The diffuse colour and texture of the first material from the <c>.mtl</c> file are used.
/// </summary>
public static class ObjImporter
{
    /// <summary>Imports an OBJ file.</summary>
    public static ImportedModel Import(string path, AssetDatabase? assets)
    {
        var positions = new List<Vector3>();
        var texCoords = new List<Vector2>();
        var normals = new List<Vector3>();
        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        var lookup = new Dictionary<(int, int, int), uint>();
        var hasNormals = true;
        string? materialLibrary = null;
        string? firstMaterial = null;

        var lineNumber = 0;
        foreach (var rawLine in File.ReadLines(path))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "v":
                    positions.Add(new Vector3(Number(parts, 1, lineNumber), Number(parts, 2, lineNumber), Number(parts, 3, lineNumber)));
                    break;
                case "vt":
                    // OBJ texture space starts at the bottom; engine textures start at the top.
                    texCoords.Add(new Vector2(Number(parts, 1, lineNumber), 1f - (parts.Length > 2 ? Number(parts, 2, lineNumber) : 0f)));
                    break;
                case "vn":
                    normals.Add(new Vector3(Number(parts, 1, lineNumber), Number(parts, 2, lineNumber), Number(parts, 3, lineNumber)));
                    break;
                case "f" when parts.Length >= 4:
                    var corners = new uint[parts.Length - 1];
                    for (var i = 1; i < parts.Length; i++)
                    {
                        corners[i - 1] = Corner(parts[i], lineNumber);
                    }

                    // Triangle fan: (0, 1, 2), (0, 2, 3), …
                    for (var i = 1; i + 1 < corners.Length; i++)
                    {
                        indices.Add(corners[0]);
                        indices.Add(corners[i]);
                        indices.Add(corners[i + 1]);
                    }

                    break;
                case "mtllib" when parts.Length > 1:
                    materialLibrary ??= line[6..].Trim();
                    break;
                case "usemtl" when parts.Length > 1:
                    firstMaterial ??= line[6..].Trim();
                    break;
            }
        }

        var vertexArray = vertices.ToArray();
        var indexArray = indices.ToArray();
        if (!hasNormals)
        {
            ModelImporter.ComputeSmoothNormals(vertexArray, indexArray);
        }

        var (color, textureFile) = ReadMaterial(path, materialLibrary, firstMaterial);
        return new ImportedModel
        {
            Mesh = new MeshData(vertexArray, indexArray),
            BaseColor = color,
            TextureId = ModelImporter.TextureReferenceFor(path, textureFile, assets),
        };

        uint Corner(string token, int lineNo)
        {
            var fields = token.Split('/');
            var p = Index(fields[0], positions.Count, lineNo);
            var t = fields.Length > 1 && fields[1].Length > 0 ? Index(fields[1], texCoords.Count, lineNo) : -1;
            var n = fields.Length > 2 && fields[2].Length > 0 ? Index(fields[2], normals.Count, lineNo) : -1;
            hasNormals &= n >= 0;

            if (lookup.TryGetValue((p, t, n), out var existing))
            {
                return existing;
            }

            var index = (uint)vertices.Count;
            vertices.Add(new Vertex(positions[p], n >= 0 ? normals[n] : Vector3.UnitY, t >= 0 ? texCoords[t] : Vector2.Zero));
            lookup[(p, t, n)] = index;
            return index;
        }
    }

    private static int Index(string text, int count, int line)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value == 0)
        {
            throw new InvalidDataException($"Line {line}: '{text}' is not a valid index.");
        }

        // OBJ indices start at 1; negative indices count back from the last element.
        var index = value > 0 ? value - 1 : count + value;
        if (index < 0 || index >= count)
        {
            throw new InvalidDataException($"Line {line}: index {value} is out of range.");
        }

        return index;
    }

    private static float Number(string[] parts, int index, int line)
    {
        if (index >= parts.Length || !float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new InvalidDataException($"Line {line}: expected a number.");
        }

        return value;
    }

    private static (Color Color, string? Texture) ReadMaterial(string objPath, string? library, string? material)
    {
        if (library is null)
        {
            return (Color.White, null);
        }

        var mtlPath = Path.Combine(Path.GetDirectoryName(objPath)!, library);
        if (!File.Exists(mtlPath))
        {
            return (Color.White, null);
        }

        var color = Color.White;
        string? texture = null;
        var inMaterial = material is null;
        foreach (var raw in File.ReadLines(mtlPath))
        {
            var line = raw.Trim();
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            if (parts[0] == "newmtl")
            {
                if (inMaterial && material is null && (texture is not null || color != Color.White))
                {
                    break; // no usemtl: take the first material in the library
                }

                inMaterial = material is null || line[6..].Trim() == material;
                continue;
            }

            if (!inMaterial)
            {
                continue;
            }

            if (parts[0] == "Kd" && parts.Length >= 4)
            {
                color = new Color(Parse(parts[1]), Parse(parts[2]), Parse(parts[3]));
            }
            else if (parts[0] == "map_Kd" && parts.Length >= 2)
            {
                texture = parts[^1];
            }
        }

        return (color, texture);

        static float Parse(string text) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 1f;
    }
}
