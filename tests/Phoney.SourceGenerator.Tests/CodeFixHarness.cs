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
        return GeneratorHarness.InSourceOrder(all.Where(d => d.Id == id));
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

    /// <summary>Applies the provider's fix-all to every diagnostic in <paramref name="diagnostics"/>, starting from the fix offered for the first one, as the IDE's "Fix all in document" does.</summary>
    public static async Task<Project> FixAll(Project project, CodeFixProvider provider, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var first = diagnostics[0];
        var document = project.GetDocument(first.Location.SourceTree)!;
        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, first, (action, _) => actions.Add(action), cancellationToken));

        var context = new FixAllContext(
            document, provider, FixAllScope.Document, actions.Single().EquivalenceKey, [first.Id], new FixedDiagnostics(diagnostics), cancellationToken);
        var action = (await provider.GetFixAllProvider()!.GetFixAsync(context))!;
        var operations = await action.GetOperationsAsync(cancellationToken);
        return operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetProject(project.Id)!;
    }

    /// <summary>Hands the fix-all the diagnostics a test made, in place of running the generator that reports them.</summary>
    private sealed class FixedDiagnostics(ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            Task.FromResult(diagnostics.Where(d => d.Location.SourceTree == document.GetSyntaxTreeAsync(cancellationToken).Result));

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
    }
}
