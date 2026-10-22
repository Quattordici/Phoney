using System.Diagnostics.CodeAnalysis;
using Phoney.Generation;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Phoney.TUnit;

/// <summary>
/// Runs a <c>[FakeCheck]</c> test once per row. A test has only one executor, so the one it had before (<paramref name="inner"/>, e.g. from
/// <c>[TestExecutor&lt;T&gt;]</c>) runs each row.
/// </summary>
internal sealed class CheckingTestExecutor(FakeCheckOptions options, ITestExecutor? inner) : ITestExecutor
{
    /// <inheritdoc />
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "[FakeCheck] reads the parameters of test methods with reflection by design; TUnit test projects are not trimmed.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "[FakeCheck] reads the parameters of test methods with reflection by design; TUnit test projects are not published with Native AOT.")]
    public ValueTask ExecuteTest(TestContext context, Func<ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(context);
        var details = context.Metadata.TestDetails;
        var generator = options.CreateGenerator([.. details.MethodMetadata.Parameters.Select(TestArguments.From)]);
        return CheckedRun.Execute(generator, options, details.TestMethodArguments, inner is null ? action : () => inner.ExecuteTest(context, action));
    }
}

/// <summary>The rows of a check around a test body, apart from TUnit so it can be tested directly.</summary>
internal static class CheckedRun
{
    /// <summary>
    /// Checks <paramref name="body"/> on the rows of <paramref name="generator"/>: each row is copied into <paramref name="values"/> (the arguments the test
    /// method is invoked with) before the body runs, and a failing row is shrunk and thrown as a <see cref="FakeCheckException{T}"/>.
    /// </summary>
    public static async ValueTask Execute(ArgumentGenerator generator, FakeCheckOptions options, object?[] values, Func<ValueTask> body) =>
        await generator.CheckAsync(
            async row =>
            {
                row.CopyTo(values, 0);
                await body();
            },
            options.Count,
            options.MaxShrinks).ConfigureAwait(false);
}
