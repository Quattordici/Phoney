namespace Phoney.Tests;

/// <summary>
/// How the processor time of some work grows with its size. The ratio of two sizes measured on the same machine at the same time doesn't depend on how
/// fast the machine is, so a test can tell linear from quadratic anywhere; measuring processor time (<see cref="CpuTime"/>) leaves out the time a busy CI
/// runner keeps the thread waiting for a core.
/// </summary>
internal static class Growth
{
    /// <summary>
    /// How many times longer <paramref name="work"/> takes for <paramref name="size"/> × <paramref name="scale"/> than for <paramref name="size"/>: about
    /// <paramref name="scale"/> when it is linear, <paramref name="scale"/>² when it is quadratic. Both sizes run once to warm up and then alternate, each
    /// one first every other round, and the runs of each size are added up, so the coarse steps of a processor-time clock (15.6 ms on Windows) even out.
    /// </summary>
    public static double Factor(Action<int> work, int size, int scale, int rounds = 5)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(scale, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(rounds, 1);

        work(size);
        work(size * scale);
        var small = TimeSpan.Zero;
        var large = TimeSpan.Zero;
        for (var round = 0; round < rounds; round++)
        {
            // Every other round starts with the large size, so something the machine does on a schedule doesn't always hit the same size.
            if (round % 2 == 0)
            {
                small += CpuTime.Of(() => work(size));
                large += CpuTime.Of(() => work(size * scale));
            }
            else
            {
                large += CpuTime.Of(() => work(size * scale));
                small += CpuTime.Of(() => work(size));
            }
        }

        return large / (small > TimeSpan.Zero ? small : TimeSpan.FromTicks(1));
    }
}
