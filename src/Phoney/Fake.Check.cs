using System.Diagnostics.CodeAnalysis;
using Phoney.Generation;

namespace Phoney;

// Property checks over the parameters of a lambda: Fake.Check((Order order, int discount) => …).
public static partial class Fake
{
    /// <summary>
    /// Checks that <paramref name="property"/> holds for <paramref name="count"/> generated sets of its parameters, property-based testing style:
    /// <c>Fake.Check((Order order, [Range(0, 100)] int discount) =&gt; Pricing.Total(order, discount) &gt;= 0)</c>. The parameters are filled like
    /// <c>[FakeData]</c> fills a test method (by name, type and annotations, and the values of one row agree), and a failing row is <em>shrunk</em> to the
    /// simplest row that still fails, which a <see cref="FakeCheckException{T}"/> of <c>object?[]</c> reports with the parameters by name.
    /// </summary>
    /// <remarks>
    /// Up to four parameters; for more, take an object or use <see cref="Arguments(System.Reflection.MethodBase)"/> and
    /// <see cref="ArgumentGenerator.Check(Func{object?[], bool}, int, int)"/>. The seed is the facade's when it is seeded (<see cref="Seeded(long, string?)"/>,
    /// <c>[FakeSeed]</c>), else <c>PHONEY_SEED</c>, else a new random one that the failure reports; <c>PHONEY_SEED=&lt;seed&gt; dotnet test</c> replays it.
    /// </remarks>
    /// <typeparam name="T1">The type of parameter 1.</typeparam>
    /// <param name="property">What must hold for every row.</param>
    /// <param name="count">How many rows to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler rows shrinking may try; 0 reports the original row.</param>
    /// <exception cref="FakeCheckException{T}">The property returned false or threw for a row.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static void Check<T1>(Func<T1, bool> property, int count = 100, int maxShrinks = 1000) =>
        ArgumentsOf(property).Check(row => property((T1)row[0]!), count, maxShrinks);

    /// <summary>Checks that <paramref name="property"/> doesn't throw for <paramref name="count"/> generated sets of its parameters, so any assertion library can be the property; see <see cref="Check{T1}(Func{T1, bool}, int, int)"/>.</summary>
    /// <typeparam name="T1">The type of parameter 1.</typeparam>
    /// <param name="property">The assertions for one row.</param>
    /// <param name="count">How many rows to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler rows shrinking may try; 0 reports the original row.</param>
    /// <exception cref="FakeCheckException{T}">The property threw for a row; what it threw is the inner exception.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static void Check<T1>(Action<T1> property, int count = 100, int maxShrinks = 1000) =>
        ArgumentsOf(property).Check(row => property((T1)row[0]!), count, maxShrinks);

    /// <summary>The asynchronous form of <see cref="Check{T1}(Action{T1}, int, int)"/>: a row fails when the property's task faults.</summary>
    /// <typeparam name="T1">The type of parameter 1.</typeparam>
    /// <param name="property">The asynchronous assertions for one row.</param>
    /// <param name="count">How many rows to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler rows shrinking may try; 0 reports the original row.</param>
    /// <param name="cancellationToken">Stops the check between rows.</param>
    /// <exception cref="FakeCheckException{T}">The property failed for a row.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static Task CheckAsync<T1>(Func<T1, Task> property, int count = 100, int maxShrinks = 1000, CancellationToken cancellationToken = default) =>
        ArgumentsOf(property).CheckAsync(row => property((T1)row[0]!), count, maxShrinks, cancellationToken);

    /// <summary>Checks that <paramref name="property"/> holds for <paramref name="count"/> generated sets of its 2 parameters, and shrinks a failing set; see <see cref="Check{T1}(Func{T1, bool}, int, int)"/>.</summary>
    /// <typeparam name="T1">The type of parameter 1.</typeparam>
    /// <typeparam name="T2">The type of parameter 2.</typeparam>
    /// <param name="property">What must hold for every row.</param>
    /// <param name="count">How many rows to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler rows shrinking may try; 0 reports the original row.</param>
    /// <exception cref="FakeCheckException{T}">The property returned false or threw for a row.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static void Check<T1, T2>(Func<T1, T2, bool> property, int count = 100, int maxShrinks = 1000) =>
        ArgumentsOf(property).Check(row => property((T1)row[0]!, (T2)row[1]!), count, maxShrinks);

    /// <summary>Checks that <paramref name="property"/> doesn't throw for <paramref name="count"/> generated sets of its parameters, so any assertion library can be the property; see <see cref="Check{T1}(Func{T1, bool}, int, int)"/>.</summary>
    /// <typeparam name="T1">The type of parameter 1.</typeparam>
    /// <typeparam name="T2">The type of parameter 2.</typeparam>
    /// <param name="property">The assertions for one row.</param>
    /// <param name="count">How many rows to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler rows shrinking may try; 0 reports the original row.</param>
    /// <exception cref="FakeCheckException{T}">The property threw for a row; what it threw is the inner exception.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static void Check<T1, T2>(Action<T1, T2> property, int count = 100, int maxShrinks = 1000) =>
        ArgumentsOf(property).Check(row => property((T1)row[0]!, (T2)row[1]!), count, maxShrinks);

    /// <summary>The asynchronous form of <see cref="Check{T1, T2}(Action{T1, T2}, int, int)"/>: a row fails when the property's task faults.</summary>
    /// <typeparam name="T1">The type of parameter 1.</typeparam>
    /// <typeparam name="T2">The type of parameter 2.</typeparam>
    /// <param name="property">The asynchronous assertions for one row.</param>
    /// <param name="count">How many rows to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler rows shrinking may try; 0 reports the original row.</param>
    /// <param name="cancellationToken">Stops the check between rows.</param>
    /// <exception cref="FakeCheckException{T}">The property failed for a row.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static Task CheckAsync<T1, T2>(Func<T1, T2, Task> property, int count = 100, int maxShrinks = 1000, CancellationToken cancellationToken = default) =>
        ArgumentsOf(property).CheckAsync(row => property((T1)row[0]!, (T2)row[1]!), count, maxShrinks, cancellationToken);

    /// <summary>Checks that <paramref name="property"/> holds for <paramref name="count"/> generated sets of its 3 parameters, and shrinks a failing set; see <see cref="Check{T1}(Func{T1, bool}, int, int)"/>.</summary>
    /// <typeparam name="T1">The type of parameter 1.</typeparam>
    /// <typeparam name="T2">The type of parameter 2.</typeparam>
    /// <typeparam name="T3">The type of parameter 3.</typeparam>
    /// <param name="property">What must hold for every row.</param>
    /// <param name="count">How many rows to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler rows shrinking may try; 0 reports the original row.</param>
    /// <exception cref="FakeCheckException{T}">The property returned false or threw for a row.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static void Check<T1, T2, T3>(Func<T1, T2, T3, bool> property, int count = 100, int maxShrinks = 1000) =>
        ArgumentsOf(property).Check(row => property((T1)row[0]!, (T2)row[1]!, (T3)row[2]!), count, maxShrinks);

    /// <summary>Checks that <paramref name="property"/> doesn't throw for <paramref name="count"/> generated sets of its parameters, so any assertion library can be the property; see <see cref="Check{T1}(Func{T1, bool}, int, int)"/>.</summary>
    /// <typeparam name="T1">The type of parameter 1.</typeparam>
    /// <typeparam name="T2">The type of parameter 2.</typeparam>
    /// <typeparam name="T3">The type of parameter 3.</typeparam>
    /// <param name="property">The assertions for one row.</param>
    /// <param name="count">How many rows to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler rows shrinking may try; 0 reports the original row.</param>
    /// <exception cref="FakeCheckException{T}">The property threw for a row; what it threw is the inner exception.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static void Check<T1, T2, T3>(Action<T1, T2, T3> property, int count = 100, int maxShrinks = 1000) =>
        ArgumentsOf(property).Check(row => property((T1)row[0]!, (T2)row[1]!, (T3)row[2]!), count, maxShrinks);

    /// <summary>The asynchronous form of <see cref="Check{T1, T2, T3}(Action{T1, T2, T3}, int, int)"/>: a row fails when the property's task faults.</summary>
    /// <typeparam name="T1">The type of parameter 1.</typeparam>
    /// <typeparam name="T2">The type of parameter 2.</typeparam>
    /// <typeparam name="T3">The type of parameter 3.</typeparam>
    /// <param name="property">The asynchronous assertions for one row.</param>
    /// <param name="count">How many rows to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler rows shrinking may try; 0 reports the original row.</param>
    /// <param name="cancellationToken">Stops the check between rows.</param>
    /// <exception cref="FakeCheckException{T}">The property failed for a row.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static Task CheckAsync<T1, T2, T3>(Func<T1, T2, T3, Task> property, int count = 100, int maxShrinks = 1000, CancellationToken cancellationToken = default) =>
        ArgumentsOf(property).CheckAsync(row => property((T1)row[0]!, (T2)row[1]!, (T3)row[2]!), count, maxShrinks, cancellationToken);

    /// <summary>Checks that <paramref name="property"/> holds for <paramref name="count"/> generated sets of its 4 parameters, and shrinks a failing set; see <see cref="Check{T1}(Func{T1, bool}, int, int)"/>.</summary>
    /// <typeparam name="T1">The type of parameter 1.</typeparam>
    /// <typeparam name="T2">The type of parameter 2.</typeparam>
    /// <typeparam name="T3">The type of parameter 3.</typeparam>
    /// <typeparam name="T4">The type of parameter 4.</typeparam>
    /// <param name="property">What must hold for every row.</param>
    /// <param name="count">How many rows to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler rows shrinking may try; 0 reports the original row.</param>
    /// <exception cref="FakeCheckException{T}">The property returned false or threw for a row.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static void Check<T1, T2, T3, T4>(Func<T1, T2, T3, T4, bool> property, int count = 100, int maxShrinks = 1000) =>
        ArgumentsOf(property).Check(row => property((T1)row[0]!, (T2)row[1]!, (T3)row[2]!, (T4)row[3]!), count, maxShrinks);

    /// <summary>Checks that <paramref name="property"/> doesn't throw for <paramref name="count"/> generated sets of its parameters, so any assertion library can be the property; see <see cref="Check{T1}(Func{T1, bool}, int, int)"/>.</summary>
    /// <typeparam name="T1">The type of parameter 1.</typeparam>
    /// <typeparam name="T2">The type of parameter 2.</typeparam>
    /// <typeparam name="T3">The type of parameter 3.</typeparam>
    /// <typeparam name="T4">The type of parameter 4.</typeparam>
    /// <param name="property">The assertions for one row.</param>
    /// <param name="count">How many rows to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler rows shrinking may try; 0 reports the original row.</param>
    /// <exception cref="FakeCheckException{T}">The property threw for a row; what it threw is the inner exception.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static void Check<T1, T2, T3, T4>(Action<T1, T2, T3, T4> property, int count = 100, int maxShrinks = 1000) =>
        ArgumentsOf(property).Check(row => property((T1)row[0]!, (T2)row[1]!, (T3)row[2]!, (T4)row[3]!), count, maxShrinks);

    /// <summary>The asynchronous form of <see cref="Check{T1, T2, T3, T4}(Action{T1, T2, T3, T4}, int, int)"/>: a row fails when the property's task faults.</summary>
    /// <typeparam name="T1">The type of parameter 1.</typeparam>
    /// <typeparam name="T2">The type of parameter 2.</typeparam>
    /// <typeparam name="T3">The type of parameter 3.</typeparam>
    /// <typeparam name="T4">The type of parameter 4.</typeparam>
    /// <param name="property">The asynchronous assertions for one row.</param>
    /// <param name="count">How many rows to test; 0 tests none.</param>
    /// <param name="maxShrinks">How many simpler rows shrinking may try; 0 reports the original row.</param>
    /// <param name="cancellationToken">Stops the check between rows.</param>
    /// <exception cref="FakeCheckException{T}">The property failed for a row.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> or <paramref name="maxShrinks"/> is negative.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static Task CheckAsync<T1, T2, T3, T4>(Func<T1, T2, T3, T4, Task> property, int count = 100, int maxShrinks = 1000, CancellationToken cancellationToken = default) =>
        ArgumentsOf(property).CheckAsync(row => property((T1)row[0]!, (T2)row[1]!, (T3)row[2]!, (T4)row[3]!), count, maxShrinks, cancellationToken);

    /// <summary>The generator of the parameters of <paramref name="property"/>, with the names and annotations the lambda declares.</summary>
    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    private static ArgumentGenerator ArgumentsOf(Delegate property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return Arguments(property.Method).WithoutReplayHint();
    }
}
