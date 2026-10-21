using System.Numerics;
using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>Thrown when GLSL source fails to compile or link.</summary>
public sealed class ShaderCompilationException(string message) : Exception(message);

/// <summary>
/// A linked GLSL program made of a vertex and a fragment stage.
/// </summary>
public sealed unsafe class Shader : IDisposable
{
    private readonly GL _gl;
    private readonly uint _program;
    private readonly Dictionary<string, int> _uniformLocations = [];
    private bool _disposed;

    private Shader(GL gl, uint program, string name)
    {
        _gl = gl;
        _program = program;
        Name = name;
    }

    /// <summary>Name used in error messages, usually the file name without extension.</summary>
    public string Name { get; }

    /// <summary>Compiles a program from GLSL source strings.</summary>
    /// <exception cref="ShaderCompilationException">A stage failed to compile or the program failed to link.</exception>
    public static Shader FromSource(GL gl, string name, string vertexSource, string fragmentSource)
    {
        ArgumentNullException.ThrowIfNull(gl);

        var vertex = CompileStage(gl, name, ShaderType.VertexShader, vertexSource);
        var fragment = CompileStage(gl, name, ShaderType.FragmentShader, fragmentSource);

        var program = gl.CreateProgram();
        gl.AttachShader(program, vertex);
        gl.AttachShader(program, fragment);
        gl.LinkProgram(program);

        gl.DetachShader(program, vertex);
        gl.DetachShader(program, fragment);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);

        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);
        if (linked == 0)
        {
            var log = gl.GetProgramInfoLog(program);
            gl.DeleteProgram(program);
            throw new ShaderCompilationException($"Shader '{name}' failed to link:{Environment.NewLine}{log}");
        }

        return new Shader(gl, program, name);
    }

    /// <summary>
    /// Compiles a program from <c>{name}.vert</c> and <c>{name}.frag</c> in <paramref name="directory"/>.
    /// </summary>
    public static Shader FromFiles(GL gl, string directory, string name)
    {
        var vertexSource = File.ReadAllText(Path.Combine(directory, name + ".vert"));
        var fragmentSource = File.ReadAllText(Path.Combine(directory, name + ".frag"));
        return FromSource(gl, name, vertexSource, fragmentSource);
    }

    /// <summary>Loads a built-in program whose vertex and fragment stages have different file names.</summary>
    public static Shader LoadBuiltIn(GL gl, string vertexName, string fragmentName)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Shaders");
        var vertexSource = File.ReadAllText(Path.Combine(directory, vertexName + ".vert"));
        var fragmentSource = File.ReadAllText(Path.Combine(directory, fragmentName + ".frag"));
        return FromSource(gl, $"{vertexName}+{fragmentName}", vertexSource, fragmentSource);
    }

    /// <summary>Loads one of the engine's built-in shaders shipped in the <c>Shaders</c> folder.</summary>
    public static Shader LoadBuiltIn(GL gl, string name) =>
        FromFiles(gl, Path.Combine(AppContext.BaseDirectory, "Shaders"), name);

    /// <summary>Makes this program current for subsequent draw calls.</summary>
    public void Use() => _gl.UseProgram(_program);

    /// <summary>Sets an <c>int</c> uniform.</summary>
    public void Set(string name, int value)
    {
        if (TryGetLocation(name, out var location))
        {
            _gl.ProgramUniform1(_program, location, value);
        }
    }

    /// <summary>Sets a <c>uint</c> uniform.</summary>
    public void Set(string name, uint value)
    {
        if (TryGetLocation(name, out var location))
        {
            _gl.ProgramUniform1(_program, location, value);
        }
    }

    /// <summary>Sets a <c>float</c> uniform.</summary>
    public void Set(string name, float value)
    {
        if (TryGetLocation(name, out var location))
        {
            _gl.ProgramUniform1(_program, location, value);
        }
    }

    /// <summary>Sets a <c>vec3</c> uniform.</summary>
    public void Set(string name, Vector3 value)
    {
        if (TryGetLocation(name, out var location))
        {
            _gl.ProgramUniform3(_program, location, value.X, value.Y, value.Z);
        }
    }

    /// <summary>Sets a <c>vec4</c> uniform.</summary>
    public void Set(string name, Vector4 value)
    {
        if (TryGetLocation(name, out var location))
        {
            _gl.ProgramUniform4(_program, location, value.X, value.Y, value.Z, value.W);
        }
    }

    /// <summary>
    /// Sets a <c>mat4</c> uniform. System.Numerics matrices (row vectors, <c>v * M</c>) are uploaded
    /// as-is, which GLSL reads as the equivalent column-vector matrix (<c>M * v</c>).
    /// </summary>
    public void Set(string name, Matrix4x4 value)
    {
        if (TryGetLocation(name, out var location))
        {
            _gl.ProgramUniformMatrix4(_program, location, 1, false, (float*)&value);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gl.DeleteProgram(_program);
        _disposed = true;
    }

    private bool TryGetLocation(string name, out int location)
    {
        if (!_uniformLocations.TryGetValue(name, out location))
        {
            location = _gl.GetUniformLocation(_program, name);
            _uniformLocations[name] = location;
        }

        // -1 means the uniform does not exist or was optimised away; setting it is a no-op.
        return location >= 0;
    }

    private static uint CompileStage(GL gl, string name, ShaderType type, string source)
    {
        var shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);

        gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
        if (compiled == 0)
        {
            var log = gl.GetShaderInfoLog(shader);
            gl.DeleteShader(shader);
            throw new ShaderCompilationException($"Shader '{name}' ({type}) failed to compile:{Environment.NewLine}{log}");
        }

        return shader;
    }
}
