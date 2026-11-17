using Microsoft.CodeAnalysis;
using Phoney.DevAnalyzers;

namespace Phoney.SourceGenerator.Tests;

public sealed class DecimalConversionAnalyzerTests
{
    private static async Task<List<Diagnostic>> Diagnose(string body, CancellationToken cancellationToken) =>
        [.. (await GeneratorHarness.Analyze("using System; using System.Numerics;\npublic static class Usage\n{\n" + body + "\n}", new DecimalConversionAnalyzer(), cancellationToken))
            .Where(d => d.Id == "PHONEYDEV002")];

    private static string TextOf(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.ToString().Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);

    [Test]
    public async Task Converting_a_double_or_float_to_decimal_is_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            public static decimal A(double d) => (decimal)d;
            public static decimal B(float f) => (decimal)f;
            public static decimal C(double d) => decimal.CreateSaturating(d);
            public static T D<T>(double d) where T : INumberBase<T> => T.CreateSaturating(d);
            public static decimal E(double d) => decimal.CreateChecked(d);
            """, cancellationToken);

        diagnostics.Select(TextOf).ShouldBe(["(decimal)d", "(decimal)f", "decimal.CreateSaturating(d)", "T.CreateSaturating(d)", "decimal.CreateChecked(d)"]);
        diagnostics[0].GetMessage().ShouldContain("PortableDecimal");
    }

    [Test]
    public async Task Other_conversions_are_not_reported(CancellationToken cancellationToken)
    {
        var diagnostics = await Diagnose("""
            public static decimal A(int i) => (decimal)i;
            public static double B(decimal m) => (double)m;
            public static decimal C(long l) => decimal.CreateSaturating(l);
            public static int D(double d) => int.CreateSaturating(d);
            public static decimal E(decimal m) => m * 2;
            public static double F(float f) => f;
            """, cancellationToken);

        diagnostics.ShouldBeEmpty();
    }
}
