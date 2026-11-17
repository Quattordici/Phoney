using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Phoney.DevAnalyzers;

/// <summary>
/// Reports conversions of a <c>double</c> or <c>float</c> to <c>decimal</c>: casts, and <c>CreateSaturating</c>/<c>CreateChecked</c>/<c>CreateTruncating</c>
/// into <c>decimal</c> or into a type parameter that may be one.
/// </summary>
/// <remarks>
/// .NET 11 rounds these exactly, earlier releases to 15 (double) or 7 (float) significant digits, with no switch back, so a seeded decimal made that
/// way would differ between target frameworks. <c>PortableDecimal</c> gives the old result everywhere. Where the value never becomes generated data,
/// suppress with a comment that says why.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DecimalConversionAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        "PHONEYDEV002",
        "Converting a binary floating-point number to decimal",
        "Converting a {0} to decimal rounds differently on .NET 11 than before. Use PortableDecimal (FromDouble, FromSingle, Create<T>) so seeded decimals are the same on every target.",
        "Phoney.Development",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly ImmutableArray<string> Factories = ImmutableArray.Create("CreateSaturating", "CreateChecked", "CreateTruncating");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeConversion, OperationKind.Conversion);
        context.RegisterOperationAction(AnalyzeFactory, OperationKind.Invocation);
    }

    private static void AnalyzeConversion(OperationAnalysisContext context)
    {
        var conversion = (IConversionOperation)context.Operation;
        if (conversion.Type?.SpecialType == SpecialType.System_Decimal && IsBinaryFloat(conversion.Operand.Type))
            context.ReportDiagnostic(Diagnostic.Create(Rule, conversion.Syntax.GetLocation(), conversion.Operand.Type!.ToDisplayString()));
    }

    private static void AnalyzeFactory(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (!method.IsStatic || !Factories.Contains(method.Name) || method.TypeArguments.Length != 1 || !IsBinaryFloat(method.TypeArguments[0]))
            return;
        // T.CreateSaturating(x) calls the interface method constrained to T.
        if (MayBeDecimal(invocation.ConstrainedToType ?? method.ContainingType, context.Compilation))
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), method.TypeArguments[0].ToDisplayString()));
    }

    private static bool IsBinaryFloat(ITypeSymbol? type) => type?.SpecialType is SpecialType.System_Double or SpecialType.System_Single;

    /// <summary>Whether the type is <c>decimal</c>, or a type parameter whose constraints <c>decimal</c> meets.</summary>
    private static bool MayBeDecimal(ITypeSymbol type, Compilation compilation)
    {
        if (type.SpecialType == SpecialType.System_Decimal)
            return true;
        if (type is not ITypeParameterSymbol parameter)
            return false;

        var @decimal = compilation.GetSpecialType(SpecialType.System_Decimal);
        if (parameter.HasReferenceTypeConstraint)
            return false;
        return parameter.ConstraintTypes.All(constraint => Satisfies(@decimal, constraint, parameter));
    }

    private static bool Satisfies(INamedTypeSymbol @decimal, ITypeSymbol constraint, ITypeParameterSymbol parameter)
    {
        if (constraint is not INamedTypeSymbol named)
            return false;
        // INumberBase<T> with T = decimal: INumberBase<decimal>.
        var arguments = named.TypeArguments.Select(a => SymbolEqualityComparer.Default.Equals(a, parameter) ? @decimal : a).ToArray();
        var substituted = arguments.Length == 0 ? named : named.OriginalDefinition.Construct(arguments);
        return SymbolEqualityComparer.Default.Equals(substituted, @decimal)
            || @decimal.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, substituted))
            || SymbolEqualityComparer.Default.Equals(substituted, @decimal.BaseType);
    }
}
