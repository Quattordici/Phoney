using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Phoney.Generation;
using Xunit.Sdk;
using Xunit.v3;

namespace Phoney.Xunit;

/// <summary>
/// Finds the <c>[FakeCheck]</c> tests: <see cref="FactDiscoverer"/> describes the method as if it had no parameters (as a <see cref="CheckedMethod"/>),
/// which keeps every option of <see cref="T:Xunit.FactAttribute"/> and works on xUnit.net 3 and 4 alike, and the test case is then made a
/// <see cref="FakeCheckTestCase"/> of the real method.
/// </summary>
internal sealed class FakeCheckDiscoverer : IXunitTestCaseDiscoverer
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(ITestFrameworkDiscoveryOptions discoveryOptions, IXunitTestMethod testMethod, IFactAttribute factAttribute)
    {
        ArgumentNullException.ThrowIfNull(testMethod);
        var asFact = new XunitTestMethod(testMethod.TestClass, new CheckedMethod(testMethod.Method), [], testMethod.UniqueID);
        var testCases = await new FactDiscoverer().Discover(discoveryOptions, asFact, factAttribute).ConfigureAwait(false);
        return [.. testCases.Select(c => c.GetType() == typeof(XunitTestCase) ? new FakeCheckTestCase((XunitTestCase)c, testMethod) : c)];
    }
}

/// <summary>
/// The test case of a <c>[FakeCheck]</c> method. Its test invokes a <see cref="CheckedMethod"/> instead of the method, which runs the method once per
/// row; that is the hook that xUnit.net 3 and 4 share (their runners differ), and the test case is otherwise a <c>[Fact]</c>.
/// </summary>
internal sealed class FakeCheckTestCase : XunitTestCase
{
    /// <summary>For the deserializer.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public FakeCheckTestCase()
    {
    }

    /// <summary>The test case <paramref name="fact"/> of the method <paramref name="testMethod"/>.</summary>
    public FakeCheckTestCase(XunitTestCase fact, IXunitTestMethod testMethod)
        : base(testMethod, fact.TestCaseDisplayName, fact.UniqueID, fact.Explicit, fact.SkipExceptions, fact.SkipReason, fact.SkipType, fact.SkipUnless, fact.SkipWhen,
            fact.Traits, null, fact.SourceFilePath, fact.SourceLineNumber, fact.Timeout)
    {
    }

    /// <summary>One test, of the method wrapped in a <see cref="CheckedMethod"/>.</summary>
    public override ValueTask<IReadOnlyCollection<IXunitTest>> CreateTests()
    {
        var checkedMethod = new XunitTestMethod(TestMethod.TestClass, new CheckedMethod(TestMethod.Method), [], TestMethod.UniqueID);
        var traits = Traits.ToDictionary(t => t.Key, IReadOnlyCollection<string> (t) => t.Value, StringComparer.OrdinalIgnoreCase);
        return new([new XunitTest(this, checkedMethod, Explicit, SkipReason, SkipType, SkipUnless, SkipWhen, TestCaseDisplayName, 0, traits, Timeout, [])]);
    }
}

/// <summary>
/// A test method seen as one without parameters, whose invocation is the check: <see cref="Invoke"/> runs the real method once per row on the same
/// instance, and its task faults with the <see cref="FakeCheckException{T}"/> of the shrunk row. Everything else is the real method's.
/// </summary>
internal sealed class CheckedMethod(MethodInfo method) : MethodInfo
{
    public override string Name => method.Name;
    public override Type? DeclaringType => method.DeclaringType;
    public override Type? ReflectedType => method.ReflectedType;
    public override Module Module => method.Module;
    public override int MetadataToken => method.MetadataToken;
    public override MethodAttributes Attributes => method.Attributes;
    public override RuntimeMethodHandle MethodHandle => method.MethodHandle;
    public override CallingConventions CallingConvention => method.CallingConvention;
    public override Type ReturnType => typeof(Task);
    public override ICustomAttributeProvider ReturnTypeCustomAttributes => method.ReturnTypeCustomAttributes;
    public override bool IsGenericMethod => method.IsGenericMethod;
    public override bool IsGenericMethodDefinition => method.IsGenericMethodDefinition;
    public override bool ContainsGenericParameters => method.ContainsGenericParameters;
    public override Type[] GetGenericArguments() => method.GetGenericArguments();

    public override MethodInfo GetBaseDefinition() => method.GetBaseDefinition();
    public override MethodImplAttributes GetMethodImplementationFlags() => method.GetMethodImplementationFlags();
    public override object[] GetCustomAttributes(bool inherit) => method.GetCustomAttributes(inherit);
    public override object[] GetCustomAttributes(Type attributeType, bool inherit) => method.GetCustomAttributes(attributeType, inherit);
    public override IList<CustomAttributeData> GetCustomAttributesData() => method.GetCustomAttributesData();
    public override bool IsDefined(Type attributeType, bool inherit) => method.IsDefined(attributeType, inherit);

    /// <summary>None: the check fills them.</summary>
    public override ParameterInfo[] GetParameters() => [];

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "[FakeCheck] reads the parameters of test methods with reflection by design; xUnit test projects are not trimmed.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "[FakeCheck] reads the parameters of test methods with reflection by design; xUnit test projects are not published with Native AOT.")]
    public override object Invoke(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters, CultureInfo? culture)
    {
        if (method.ReturnType == typeof(void) && method.IsDefined(typeof(AsyncStateMachineAttribute), false))
            throw new NotSupportedException($"[FakeCheck] can't wait for the 'async void' method {method.Name}; make it return Task.");

        var options = method.GetCustomAttribute<FakeCheckAttribute>()?.Options ?? new FakeCheckOptions();
        var generator = options.CreateGenerator([.. method.GetParameters().Select(FakeArgument.From)]);
        var values = new object?[method.GetParameters().Length];
        return Check();

        async Task Check()
        {
            if (await CheckedRun.Execute(generator, options, values, InvokeRow).ConfigureAwait(false) is { } failure)
                ExceptionDispatchInfo.Throw(failure);
        }

        async ValueTask<Exception?> InvokeRow()
        {
            try
            {
                switch (method.Invoke(obj, BindingFlags.DoNotWrapExceptions, binder, values, culture))
                {
                    case Task task:
                        await task.ConfigureAwait(false);
                        break;
                    case ValueTask valueTask:
                        await valueTask.ConfigureAwait(false);
                        break;
                }

                return null;
            }
#pragma warning disable CA1031 // any exception fails the row; the check reports it
            catch (Exception e)
            {
                return e;
            }
#pragma warning restore CA1031
        }
    }

    public override bool Equals(object? obj) => obj is CheckedMethod other && other.Name == Name && other.DeclaringType == DeclaringType && other.MetadataToken == MetadataToken;

    public override int GetHashCode() => method.GetHashCode();
}

/// <summary>The rows of a check around a test invocation, apart from xUnit.net so it can be tested directly.</summary>
internal static class CheckedRun
{
    /// <summary>
    /// Checks the rows of <paramref name="generator"/>: each row is copied into <paramref name="values"/> (the arguments the test method is invoked with)
    /// before <paramref name="invoke"/> runs it and returns what it threw, if anything.
    /// </summary>
    /// <returns>The <see cref="FakeCheckException{T}"/> of the shrunk row, or <see langword="null"/> when every row passed.</returns>
    public static async ValueTask<Exception?> Execute(ArgumentGenerator generator, FakeCheckOptions options, object?[] values, Func<ValueTask<Exception?>> invoke)
    {
        try
        {
            await generator.CheckAsync(
                async row =>
                {
                    row.CopyTo(values, 0);
                    if (await invoke().ConfigureAwait(false) is { } thrown)
                        ExceptionDispatchInfo.Throw(thrown);
                },
                options.Count,
                options.MaxShrinks).ConfigureAwait(false);
            return null;
        }
        catch (FakeCheckException<object?[]> failure)
        {
            return failure;
        }
    }
}
