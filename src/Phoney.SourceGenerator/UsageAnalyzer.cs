using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Phoney.SourceGenerator;

/// <summary>
/// Reports calls that silently do nothing or throw when a generator is first used: results of immutable generators that
/// are thrown away (PHONEY005), <c>Fake.Seeded</c> scopes that are never disposed (PHONEY006), rules for members that
/// can't be generated (PHONEY007) and rules that replace each other in one chain (PHONEY008).
/// </summary>
/// <remarks>
/// The member checks mirror <c>Generator&lt;T&gt;.Compile</c> and use <see cref="ModelBuilder.DescribeMembers"/>, so they
/// are skipped for generators over a hand-written model (<c>new Generator&lt;T&gt;(model)</c>), whose members are unknown.
/// </remarks>
[DiagnosticAnalyzer(Microsoft.CodeAnalysis.LanguageNames.CSharp)]
public sealed class UsageAnalyzer : DiagnosticAnalyzer
{
    private static readonly HashSet<string> RuleMethods = new(StringComparer.Ordinal) { "With", "WithOneOf", "Ignore", "Unique" };

    /// <summary>The rules that occupy a member's one rule slot; <c>Unique</c> is kept separately and combines with them.</summary>
    private static readonly HashSet<string> SlotRules = new(StringComparer.Ordinal) { "With", "WithOneOf", "Ignore" };

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Diagnostics.DiscardedResult, Diagnostics.SeededNotDisposed, Diagnostics.RuleNotGenerated, Diagnostics.RuleReplaced);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var generator = start.Compilation.GetTypeByMetadataName("Phoney.Generator`1");
            var fake = start.Compilation.GetTypeByMetadataName("Phoney.Fake");
            if (generator is null || fake is null)
                return;
            var symbols = new Symbols(start.Compilation, generator, fake);
            start.RegisterOperationAction(c => Analyze(c, symbols), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, Symbols symbols)
    {
        var call = (IInvocationOperation)context.Operation;
        var method = call.TargetMethod;
        var onFake = AnalyzerSupport.IsDefinition(method.ContainingType, symbols.Fake);
        var onGenerator = !onFake && AnalyzerSupport.IsDefinition(method.ContainingType, symbols.Generator);
        if (!onFake && !onGenerator)
            return;

        if (onFake && method.Name == "Seeded")
            CheckSeeded(context, call);
        else if (method.Name == "Populate")
            CheckPopulate(context, call, onFake);
        else if (onGenerator)
            CheckGenerator(context, call, symbols);
    }

    /// <summary>PHONEY005: a configuration method's new generator is dropped, so the call changes nothing.</summary>
    private static void CheckDiscarded(OperationAnalysisContext context, IInvocationOperation call, Symbols symbols)
    {
        if (call.Parent is IExpressionStatementOperation && AnalyzerSupport.IsDefinition(call.TargetMethod.ReturnType as INamedTypeSymbol, symbols.Generator))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.DiscardedResult,
                AnalyzerSupport.NameLocation(call),
                $"The result of '{call.TargetMethod.Name}' is discarded. Generators are immutable: every configuration method returns a new generator, so this call has no effect. Use the returned generator."));
        }
    }

    /// <summary>PHONEY005: <c>Populate</c> on a struct returns the populated copy; the original stays as it was.</summary>
    private static void CheckPopulate(OperationAnalysisContext context, IInvocationOperation call, bool onFake)
    {
        if (call.Parent is not IExpressionStatementOperation)
            return;
        var method = call.TargetMethod;
        var type = onFake ? method.TypeArguments.FirstOrDefault() : method.ContainingType.TypeArguments.FirstOrDefault();
        if (type is not { IsValueType: true })
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.DiscardedResult,
            AnalyzerSupport.NameLocation(call),
            $"'Populate' returns a populated copy of the struct '{type.Name}' and leaves the original unchanged. Use the returned value."));
    }

    /// <summary>PHONEY006: the scope of <c>Fake.Seeded</c> lasts until it is disposed; dropping it seeds everything after it.</summary>
    private static void CheckSeeded(OperationAnalysisContext context, IInvocationOperation call)
    {
        var dropped = call.Parent switch
        {
            IExpressionStatementOperation => true,
            ISimpleAssignmentOperation { Target: IDiscardOperation } => true,
            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } => IsUnusedPlainLocal(declarator),
            _ => false,
        };
        if (dropped)
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.SeededNotDisposed, AnalyzerSupport.NameLocation(call)));
    }

    /// <summary>A local declared without <c>using</c> that nothing refers to afterwards, so it can't be disposed.</summary>
    private static bool IsUnusedPlainLocal(IVariableDeclaratorOperation declarator)
    {
        var group = declarator.Parent?.Parent as IVariableDeclarationGroupOperation;
        if (group?.Parent is IUsingDeclarationOperation or IUsingOperation)
            return false;

        var root = (IOperation)declarator;
        while (root.Parent is { } parent)
            root = parent;
        return !root.DescendantsAndSelf().OfType<ILocalReferenceOperation>()
            .Any(reference => SymbolEqualityComparer.Default.Equals(reference.Local, declarator.Symbol));
    }

    private static void CheckGenerator(OperationAnalysisContext context, IInvocationOperation call, Symbols symbols)
    {
        CheckDiscarded(context, call, symbols);
        var name = call.TargetMethod.Name;
        if (!RuleMethods.Contains(name))
            return;

        var selector = RuleSelector.Read(call);
        if (selector.Kind == SelectorKind.Unknown)
            return;
        CheckRule(context, call, selector, symbols);
        if (selector.Kind == SelectorKind.Member && SlotRules.Contains(name))
            CheckReplaced(context, call, selector, symbols);
    }

    /// <summary>PHONEY007: what <c>Generator&lt;T&gt;.Compile</c> and <c>MemberName</c> reject at runtime.</summary>
    private static void CheckRule(OperationAnalysisContext context, IInvocationOperation call, RuleSelector selector, Symbols symbols)
    {
        var method = call.TargetMethod;
        if (selector.Kind == SelectorKind.NotDirect)
        {
            Report(context, selector, "The rule must select a member of the generated type directly, like x => x.Name. For nested members use .With(x => x.Address, Fake.For<Address>().With(...)).");
            return;
        }

        if (method.ContainingType.TypeArguments.FirstOrDefault() is not INamedTypeSymbol type || StartsFromCustomModel(call, symbols))
            return;
        if (symbols.Describe(type) is not { } members)
            return;

        var member = members.FirstOrDefault(m => m.Name == selector.Name);
        if (member is null)
        {
            Report(context, selector, $"'{type.Name}.{selector.Name}' is not a public settable member or constructor parameter, so it can't be generated and the generator throws when it is first used.");
        }
        else if (member.IsConstructorParameter && IsDependentRule(method))
        {
            Report(context, selector, $"'{type.Name}.{selector.Name}' is a constructor parameter, so it can't depend on the object. Use a rule without the object parameter.");
        }
    }

    private static void Report(OperationAnalysisContext context, RuleSelector selector, string message) =>
        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.RuleNotGenerated, selector.Location, message));

    /// <summary><c>With(member, (faker, obj) =&gt; …)</c>: the rule reads the object after it is created.</summary>
    private static bool IsDependentRule(IMethodSymbol method) =>
        method.Name == "With" && method.Parameters.Length == 2 && method.Parameters[1].Type is INamedTypeSymbol { Name: "Func", TypeArguments.Length: 3 };

    /// <summary>PHONEY008: an earlier call in the same chain set the same member, and this one replaces it.</summary>
    private static void CheckReplaced(OperationAnalysisContext context, IInvocationOperation call, RuleSelector selector, Symbols symbols)
    {
        var receiver = call.Instance;
        while (receiver is IInvocationOperation earlier && AnalyzerSupport.IsDefinition(earlier.TargetMethod.ContainingType, symbols.Generator))
        {
            if (SlotRules.Contains(earlier.TargetMethod.Name) && RuleSelector.Read(earlier) is { Kind: SelectorKind.Member } previous && previous.Name == selector.Name)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.RuleReplaced, selector.Location, selector.Name));
                return;
            }

            receiver = earlier.Instance;
        }
    }

    /// <summary>Whether the chain starts with <c>new Generator&lt;T&gt;(model)</c>, whose members are whatever the model says.</summary>
    private static bool StartsFromCustomModel(IInvocationOperation call, Symbols symbols)
    {
        var receiver = call.Instance;
        while (receiver is IInvocationOperation earlier && AnalyzerSupport.IsDefinition(earlier.TargetMethod.ContainingType, symbols.Generator))
            receiver = earlier.Instance;
        return receiver is IObjectCreationOperation;
    }

    /// <summary>The symbols of one compilation, with the members of each generated type described once.</summary>
    private sealed class Symbols(Compilation compilation, INamedTypeSymbol generator, INamedTypeSymbol fake)
    {
        private readonly ConcurrentDictionary<INamedTypeSymbol, List<MemberModel>?> _members = new(SymbolEqualityComparer.Default);

        public INamedTypeSymbol Generator { get; } = generator;

        public INamedTypeSymbol Fake { get; } = fake;

        /// <summary>The members Phoney generates for <paramref name="type"/>, or <see langword="null"/> when it can't be generated.</summary>
        public List<MemberModel>? Describe(INamedTypeSymbol type) =>
            _members.GetOrAdd(type, t => ModelBuilder.DescribeMembers(compilation, t));
    }
}
