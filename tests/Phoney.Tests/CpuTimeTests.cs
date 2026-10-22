using System.Diagnostics;

namespace Phoney.Tests;

public sealed class CpuTimeTests
{
    [Test]
    public void Waiting_costs_no_processor_time()
    {
        var wall = Stopwatch.StartNew();

        var cpu = CpuTime.Of(() => Thread.Sleep(300));

        wall.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
        cpu.ShouldBeLessThan(TimeSpan.FromMilliseconds(150));
    }

    [Test]
    public void Work_is_counted_however_long_the_thread_waits_for_a_core()
    {
        var sum = 0L;

        var cpu = CpuTime.Of(() =>
        {
            for (var i = 0L; i < 400_000_000; i++)
                sum += i ^ (sum >> 3);
        });

        sum.ShouldNotBe(0);
        cpu.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(20)); // the loop takes a few hundred milliseconds of processor time on any machine
    }
}
