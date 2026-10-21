namespace Phoney;

/// <summary>
/// Thrown by <see cref="ObjectGenerator{T}.Check(Func{T, bool}, int, int)"/> when a property does not hold for a generated object. The message shows the
/// shrunk counterexample, the seed and how to replay it; <see cref="FakeCheckException{T}"/> carries the objects.
/// </summary>
public abstract class FakeCheckException : Exception
{
    /// <summary>Creates the exception.</summary>
    private protected FakeCheckException(string message, Exception? innerException, long seed, int index, int tested, int shrinkSteps)
        : base(message, innerException)
    {
        Seed = seed;
        Index = index;
        Tested = tested;
        ShrinkSteps = shrinkSteps;
    }

    /// <summary>The seed of the run: with it, the generator makes the same objects and the check finds and shrinks the same counterexample.</summary>
    public long Seed { get; }

    /// <summary>The position of the first object the property failed for (0 is the first object).</summary>
    public int Index { get; }

    /// <summary>How many objects were tested, the failing one included.</summary>
    public int Tested { get; }

    /// <summary>How many times a simpler failing object replaced the one before (0 when the original could not be simplified).</summary>
    public int ShrinkSteps { get; }
}

/// <summary>
/// The <see cref="FakeCheckException"/> of a <typeparamref name="T"/> check: <see cref="Original"/> is the generated object the property failed for, and
/// <see cref="Counterexample"/> the simplest failing object found by shrinking. The <see cref="Exception.InnerException"/> is what the property threw for
/// the counterexample (<see langword="null"/> when it returned <see langword="false"/>).
/// </summary>
/// <typeparam name="T">The checked type.</typeparam>
public sealed class FakeCheckException<T> : FakeCheckException
{
    internal FakeCheckException(string message, Exception? innerException, long seed, int index, int tested, int shrinkSteps, T original, T counterexample)
        : base(message, innerException, seed, index, tested, shrinkSteps)
    {
        Original = original;
        Counterexample = counterexample;
    }

    /// <summary>The generated object the property first failed for.</summary>
    public T Original { get; }

    /// <summary>
    /// The simplest failing object shrinking found: made by the same generator, rules and annotations as <see cref="Original"/>, with smaller numbers, shorter
    /// collections and texts, and the first values of lists wherever the property still fails with them.
    /// </summary>
    public T Counterexample { get; }
}
