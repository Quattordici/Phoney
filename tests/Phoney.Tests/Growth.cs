using System.Diagnostics;

namespace Phoney.Tests;

/// <summary>
/// How the running time of some work grows with its size. A test that asserts a time budget measures the machine as much as the code: on a CI runner
/// shared with other test processes the same work can take twenty times longer. The ratio of two sizes measured on the same machine at the same time
/// cancels that out, so a test can tell linear from quadratic without knowing how fast the machine is.
/// </summary>
internal static class Growth
{
    /// <summary>
    /// How many times longer <paramref name="work"/> takes for <paramref name="size"/> × <paramref name="scale"/> than for <paramref name="size"/>: about
    /// <paramref name="scale"/> when it is linear, <paramref name="scale"/>² when it is quadratic. Both sizes run once to warm up and then alternate, and the
    /// fastest run of each counts, so a pause of the machine during one run doesn't change the result.
    /// </summary>
    public static double Factor(Action<int> work, int size, int scale, int rounds = 5)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(scale, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(rounds, 1);

        work(size);
        work(size * scale);
        var small = long.MaxValue;
        var large = long.MaxValue;
        for (var round = 0; round < rounds; round++)
        {
            small = Math.Min(small, Time(work, size));
            large = Math.Min(large, Time(work, size * scale));
        }

        return (double)large / Math.Max(small, 1);
    }

    private static long Time(Action<int> work, int size)
    {
        var start = Stopwatch.GetTimestamp();
        work(size);
        return Stopwatch.GetTimestamp() - start;
    }
}
