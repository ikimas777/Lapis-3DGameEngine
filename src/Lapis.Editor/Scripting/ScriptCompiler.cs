using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Lapis.Editor.Scripting;

/// <summary>A compiler error or warning in a script.</summary>
internal sealed record ScriptDiagnostic(bool IsError, string Id, string Message, string? FilePath, int Line, int Column)
{
    public override string ToString() =>
        FilePath is null ? $"{Id}: {Message}" : $"{Path.GetFileName(FilePath)}({Line},{Column}): {Id}: {Message}";
}

/// <summary>Where a script type is declared, for "Edit Script".</summary>
internal sealed record ScriptLocation(string FilePath, int Line);

/// <summary>Output of <see cref="ScriptCompiler.Compile"/>.</summary>
internal sealed class ScriptCompilation
{
    public required bool Success { get; init; }

    public required IReadOnlyList<ScriptDiagnostic> Diagnostics { get; init; }

    public byte[]? AssemblyBytes { get; init; }

    public byte[]? SymbolBytes { get; init; }

    /// <summary>Source location of every class, by full type name.</summary>
    public IReadOnlyDictionary<string, ScriptLocation> TypeLocations { get; init; } = ImmutableDictionary<string, ScriptLocation>.Empty;

    public int ErrorCount => Diagnostics.Count(d => d.IsError);
}

/// <summary>
/// Compiles game scripts in memory with Roslyn. Scripts reference the engine and the .NET base
/// libraries, and get a few global <c>using</c> directives so they can start with the class itself.
/// </summary>
internal static class ScriptCompiler
{
    /// <summary>Name of the compiled script assembly.</summary>
    public const string AssemblyName = "Game";

    /// <summary>Namespaces every script can use without a <c>using</c> directive.</summary>
    public static readonly IReadOnlyList<string> GlobalUsings =
        ["Lapis", "System", "System.Collections.Generic", "System.Linq", "System.Numerics"];

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(LoadReferences);

    /// <summary>Compiles the given source files into a single assembly.</summary>
    public static ScriptCompilation Compile(IReadOnlyList<string> sourceFiles)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(
                SourceText.From(string.Concat(GlobalUsings.Select(ns => $"global using {ns};\n")), Encoding.UTF8),
                parseOptions,
                path: "GlobalUsings.g.cs"),
        };

        foreach (var file in sourceFiles)
        {
            var text = SourceText.From(File.ReadAllText(file), Encoding.UTF8);
            trees.Add(CSharpSyntaxTree.ParseText(text, parseOptions, path: file));
        }

        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Debug,
            nullableContextOptions: NullableContextOptions.Annotations,
            concurrentBuild: true);

        var compilation = CSharpCompilation.Create(AssemblyName, trees, References.Value, options);

        using var assemblyStream = new MemoryStream();
        using var symbolStream = new MemoryStream();
        var emitOptions = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb);
        var result = compilation.Emit(assemblyStream, symbolStream, options: emitOptions);

        var diagnostics = result.Diagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(ToScriptDiagnostic)
            .OrderByDescending(d => d.IsError)
            .ToList();

        return new ScriptCompilation
        {
            Success = result.Success,
            Diagnostics = diagnostics,
            AssemblyBytes = result.Success ? assemblyStream.ToArray() : null,
            SymbolBytes = result.Success ? symbolStream.ToArray() : null,
            TypeLocations = result.Success ? FindTypeLocations(compilation) : ImmutableDictionary<string, ScriptLocation>.Empty,
        };
    }

    /// <summary>Paths of the assemblies scripts are compiled against (for diagnostics and the generated .csproj).</summary>
    public static string EngineAssemblyPath => typeof(Entity).Assembly.Location;

    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        // The running .NET libraries double as reference assemblies; the engine is referenced directly.
        var platformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => IsBaseLibrary(Path.GetFileNameWithoutExtension(path)));

        var engine = EngineAssemblyPath;
        if (string.IsNullOrEmpty(engine) || !File.Exists(engine))
        {
            throw new InvalidOperationException("The engine assembly (Lapis.dll) was not found on disk, so scripts cannot be compiled.");
        }

        return [.. platformAssemblies.Append(engine).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))];
    }

    private static bool IsBaseLibrary(string name) =>
        name is "mscorlib" or "netstandard" or "System" ||
        name.StartsWith("System.", StringComparison.Ordinal) ||
        name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal) ||
        name == "Microsoft.CSharp" || name == "Microsoft.VisualBasic";

    private static ScriptDiagnostic ToScriptDiagnostic(Diagnostic diagnostic)
    {
        string? file = null;
        int line = 0, column = 0;
        if (diagnostic.Location.IsInSource)
        {
            var span = diagnostic.Location.GetLineSpan();
            file = span.Path;
            line = span.StartLinePosition.Line + 1;
            column = span.StartLinePosition.Character + 1;
        }

        return new ScriptDiagnostic(
            diagnostic.Severity == DiagnosticSeverity.Error,
            diagnostic.Id,
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            file,
            line,
            column);
    }

    private static Dictionary<string, ScriptLocation> FindTypeLocations(CSharpCompilation compilation)
    {
        var locations = new Dictionary<string, ScriptLocation>(StringComparer.Ordinal);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is not { } symbol)
                {
                    continue;
                }

                var name = symbol.ContainingNamespace.IsGlobalNamespace
                    ? symbol.MetadataName
                    : $"{symbol.ContainingNamespace.ToDisplayString()}.{symbol.MetadataName}";
                var line = declaration.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                locations.TryAdd(name, new ScriptLocation(tree.FilePath, line));
            }
        }

        return locations;
    }
}
