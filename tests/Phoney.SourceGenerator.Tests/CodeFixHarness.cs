using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Phoney.SourceGenerator.Tests;

/// <summary>Runs a code fix over documents in an <see cref="AdhocWorkspace"/> with the Phoney runtime referenced.</summary>
internal static class CodeFixHarness
{
    /// <summary>A fix that was offered, and the project as it looks after applying it.</summary>
    public sealed record Fix(string Title, Project Result)
    {
        /// <summary>The text of the document called <paramref name="name"/> after the fix.</summary>
        public async Task<string> TextOf(string name, CancellationToken cancellationToken) =>
            (await Result.Documents.Single(d => d.Name == name).GetTextAsync(cancellationToken)).ToString();
    }

    public static Project CreateProject(params (string Name, string Text)[] documents)
    {
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = trusted
            .Select(path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Faker).Assembly.Location))
            .ToList();

        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Create(), "Tests", "Tests", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: references));
        foreach (var (name, text) in documents)
            project = project.AddDocument(name, SourceText.From(text)).Project;
        return project;
    }

    /// <summary>The diagnostics <paramref name="analyzer"/> reports for the project, with the given id.</summary>
    public static async Task<ImmutableArray<Diagnostic>> Diagnose(Project project, DiagnosticAnalyzer analyzer, string id, CancellationToken cancellationToken)
    {
        var compilation = (await project.GetCompilationAsync(cancellationToken))!;
        var all = await compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync(cancellationToken);
        return [.. all.Where(d => d.Id == id)];
    }

    /// <summary>The fixes <paramref name="provider"/> offers for <paramref name="diagnostic"/>, each applied to its own copy of the project.</summary>
    public static async Task<List<Fix>> Fixes(Project project, CodeFixProvider provider, Diagnostic diagnostic, CancellationToken cancellationToken)
    {
        var document = project.GetDocument(diagnostic.Location.SourceTree)!;
        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), cancellationToken));

        var fixes = new List<Fix>();
        foreach (var action in actions)
        {
            var operations = await action.GetOperationsAsync(cancellationToken);
            var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
            fixes.Add(new Fix(action.Title, changed.GetProject(project.Id)!));
        }

        return fixes;
    }
}
