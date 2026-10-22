using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Phoney.Generation;

namespace Phoney.MSTest;

/// <summary>
/// Runs an MSTest test method as a property check: put it on a method <i>instead of</i> <c>[TestMethod]</c>. The test runs once for each of
/// <see cref="Count"/> generated rows of its parameters and, when it fails for one, the row is <em>shrunk</em> to the simplest row that still fails, and
/// the test fails with a <see cref="FakeCheckException{T}"/> that shows it.
/// </summary>
/// <remarks>
/// <para>
/// Parameters are filled like <c>[FakeData]</c> fills them (by name, type and data annotations; the values of a row agree), but the rows are new on every
/// run, so a suite explores more data over time. The seed is <see cref="Seed"/> when set, else <c>PHONEY_SEED</c>, else a new random one, and a failure
/// reports it as <c>[FakeCheck(Seed = …)]</c>, which replays the failure and the shrinking exactly.
/// </para>
/// <para>
/// Every row is a full MSTest invocation (a new instance of the test class, <c>[TestInitialize]</c> and <c>[TestCleanup]</c>); MSTest shows one result,
/// and the failure message names the failing row.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [FakeCheck]
/// public void Total_is_never_negative(Order order, [Range(0, 100)] int discount) =&gt;
///     Assert.IsTrue(Pricing.Total(order, discount) &gt;= 0);
/// // FakeCheckException: The property failed for row number 12 of 100 (seed 812734, shrunk in 31 steps).
/// // Counterexample:
/// //   order = Order { Lines = [OrderLine { Sku = "AAA-00000", Quantity = 1, Price = 0.01 }] }, discount = 2
/// </code>
/// </example>
/// <param name="callerFilePath">The file of the test method; filled in by the compiler.</param>
/// <param name="callerLineNumber">The line of the test method; filled in by the compiler.</param>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class FakeCheckAttribute([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = -1)
    : TestMethodAttribute(callerFilePath, callerLineNumber)
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

    /// <summary>Invokes the test once per row; a row fails when its result isn't <see cref="UnitTestOutcome.Passed"/>.</summary>
    /// <param name="testMethod">The test method.</param>
    /// <returns>One result: passed, or failed with the <see cref="FakeCheckException{T}"/> of the shrunk row.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "[FakeCheck] reads the parameters of test methods with reflection by design; MSTest projects are not trimmed.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "[FakeCheck] reads the parameters of test methods with reflection by design; MSTest projects are not published with Native AOT.")]
    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        ArgumentNullException.ThrowIfNull(testMethod);
        var generator = _options.CreateGenerator([.. testMethod.MethodInfo.GetParameters().Select(FakeArgument.From)]);
        var passed = new TestResult { Outcome = UnitTestOutcome.Passed };
        try
        {
            await generator.CheckAsync(
                async row =>
                {
                    var result = await testMethod.InvokeAsync(row!).ConfigureAwait(false);
                    if (result.Outcome != UnitTestOutcome.Passed)
                        throw WhatFailed(result);
                    passed = result;
                },
                Count,
                MaxShrinks).ConfigureAwait(false);
            return [passed];
        }
        catch (FakeCheckException<object?[]> failure)
        {
            return [new TestResult { Outcome = UnitTestOutcome.Failed, TestFailureException = failure }];
        }
    }

    /// <summary>
    /// What a row threw, so shrinking keeps rows that fail the same way: MSTest wraps an exception of the test in its own <c>TestFailedException</c>,
    /// which would make every failure look alike.
    /// </summary>
    private static Exception WhatFailed(TestResult result)
    {
        var exception = result.TestFailureException ?? new AssertFailedException($"The test's outcome was {result.Outcome}.");
        while (exception is { InnerException: { } inner } && exception.GetType().Namespace?.StartsWith("Microsoft.VisualStudio.TestPlatform.MSTest", StringComparison.Ordinal) == true)
            exception = inner;
        return exception;
    }
}
