using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Builders;
using NUnit.Framework.Internal.Commands;
using Phoney.Generation;

namespace Phoney.NUnit;

/// <summary>
/// Runs an NUnit test method as a property check: put it on a method <i>instead of</i> <c>[Test]</c>, like <c>[FakeData]</c>. The test runs once for
/// each of <see cref="Count"/> generated rows of its parameters and, when it fails for one, the row is <em>shrunk</em> to the simplest row that still
/// fails, and the test fails with a message that shows it.
/// </summary>
/// <remarks>
/// <para>
/// Parameters are filled like <c>[FakeData]</c> fills them (by name, type and data annotations; the values of a row agree), but the rows are new on every
/// run, so a suite explores more data over time. The seed is <see cref="Seed"/> when set, else <c>PHONEY_SEED</c>, else a new random one, and a failure
/// reports it as <c>[FakeCheck(Seed = …)]</c>, which replays the failure and the shrinking exactly.
/// </para>
/// <para>
/// The rows run one after the other between the test's <c>[SetUp]</c> and <c>[TearDown]</c>, which run once. NUnit shows one test named after the
/// method; the failure message names the failing row. <c>Assert.Pass()</c> passes a row.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [FakeCheck]
/// public void Total_is_never_negative(Order order, [Range(0, 100)] int discount) =&gt;
///     Assert.That(Pricing.Total(order, discount), Is.GreaterThanOrEqualTo(0));
/// // The property failed for row number 12 of 100 (seed 812734, shrunk in 31 steps).
/// // Counterexample:
/// //   order = Order { Lines = [OrderLine { Sku = "AAA-00000", Quantity = 1, Price = 0.01 }] }, discount = 2
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class FakeCheckAttribute : NUnitAttribute, ITestBuilder, IImplyFixture, IWrapTestMethod
{
    private readonly FakeCheckOptions _options = new();

    /// <summary>How many rows to test. Default 100.</summary>
    public int Count
    {
        get => _options.Count;
        set => _options.Count = value;
    }

    /// <summary>How many simpler rows shrinking may try; 0 reports the original row. Default 1000.</summary>
    public int MaxShrinks
    {
        get => _options.MaxShrinks;
        set => _options.MaxShrinks = value;
    }

    /// <summary>Pins the seed, to replay a failure. Default: <c>PHONEY_SEED</c>, else a new random seed on every run.</summary>
    public long Seed
    {
        get => _options.Seed ?? 0;
        set => _options.Seed = value;
    }

    /// <summary>Locale of the data, e.g. <c>sv</c> or <c>de-AT</c>. Default: <see cref="Fake.Locale"/>.</summary>
    public string? Locale
    {
        get => _options.Locale;
        set => _options.Locale = value;
    }

    /// <summary>Probability (0–1) that nullable parameters (<c>string?</c>, <c>int?</c>…) are <see langword="null"/>. Default 0.</summary>
    public double NullProbability
    {
        get => _options.NullProbability;
        set => _options.NullProbability = value;
    }

    /// <summary>
    /// The point in time relative dates (past, recent, birthdates, ages) are computed from, in ISO 8601
    /// (<c>2030-01-01</c>). Default: <see cref="Faker.DefaultSeededReferenceDate"/>.
    /// </summary>
    public string? ReferenceDate
    {
        get => _options.ReferenceDate;
        set => _options.ReferenceDate = value;
    }

    /// <summary>One test named after the method, with a sample row that the check replaces with its rows.</summary>
    /// <param name="method">The test method.</param>
    /// <param name="suite">The suite the test belongs to.</param>
    /// <returns>The test.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "[FakeCheck] reads the parameters of test methods with reflection by design; NUnit test projects are not trimmed.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "[FakeCheck] reads the parameters of test methods with reflection by design; NUnit test projects are not published with Native AOT.")]
    public IEnumerable<TestMethod> BuildFrom(IMethodInfo method, Test? suite)
    {
        ArgumentNullException.ThrowIfNull(method);
        var arguments = Arguments(method);
        var sample = _options.CreateGenerator(arguments).Seed(FakeDataOptions.DefaultSeed(FakeDataOptions.TestName(method.TypeInfo.Type, method.Name), arguments));
        return [new NUnitTestCaseBuilder().BuildTestMethod(method, suite, new TestCaseParameters(sample.Row(0)) { TestName = method.Name })];
    }

    /// <summary>Runs the test method once per row.</summary>
    /// <param name="command">The command that invokes the test method.</param>
    /// <returns>The command that runs the check.</returns>
    public TestCommand Wrap(TestCommand command) => new CheckCommand(command, _options);

    private static List<FakeArgument> Arguments(IMethodInfo method) => [.. method.GetParameters().Select(p => FakeArgument.From(p.ParameterInfo))];

    /// <summary>
    /// Copies each row into the arguments NUnit invokes the method with (the array <see cref="TestMethodCommand"/> holds) and runs the method; the
    /// result of a row is reset before the next one, so failed assertions of the rows tried don't fail the test.
    /// </summary>
    private sealed class CheckCommand(TestCommand inner, FakeCheckOptions options) : DelegatingTestCommand(inner)
    {
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "[FakeCheck] reads the parameters of test methods with reflection by design; NUnit test projects are not trimmed.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "[FakeCheck] reads the parameters of test methods with reflection by design; NUnit test projects are not published with Native AOT.")]
        public override TestResult Execute(TestExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            var test = (TestMethod)Test;
            var values = test.Arguments;
            try
            {
                options.CreateGenerator(Arguments(test.Method)).Check(
                    row =>
                    {
                        row.CopyTo(values, 0);
                        context.CurrentResult = test.MakeTestResult();
                        RunRow(context);
                    },
                    options.Count,
                    options.MaxShrinks);
            }
            catch (FakeCheckException<object?[]> failure)
            {
                context.CurrentResult = test.MakeTestResult();
                context.CurrentResult.SetResult(ResultState.Failure, failure.Message, failure.StackTrace);
            }

            return context.CurrentResult;
        }

        /// <summary>Runs the method on the current row; throws what it threw, without NUnit's <see cref="NUnitException"/> around it.</summary>
        private void RunRow(TestExecutionContext context)
        {
            try
            {
                innerCommand.Execute(context);
            }
            catch (Exception e)
            {
                var thrown = e;
                while (thrown is NUnitException { InnerException: { } inner })
                    thrown = inner;
                if (thrown is SuccessException)
                {
                    context.CurrentResult.SetResult(ResultState.Success, thrown.Message);
                    return;
                }
                if (ReferenceEquals(thrown, e))
                    throw;
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(thrown);
            }
        }
    }
}
