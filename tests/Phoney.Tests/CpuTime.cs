using System.Runtime.InteropServices;

namespace Phoney.Tests;

/// <summary>
/// Processor time the current thread spends on some work. A wall-clock budget also counts the time the thread waits for a core, and on a CI runner shared
/// with other test processes (macOS has three cores for all of them) that wait can be fifty times the work. Processor time counts only the work, so a budget
/// in it fails when the code gets slower, not when the machine is busy. The work must run on the calling thread, as synchronous code does.
/// </summary>
internal static partial class CpuTime
{
    /// <summary>The processor time <paramref name="work"/> takes on the current thread, in user and kernel mode.</summary>
    public static TimeSpan Of(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);

        var start = OfCurrentThread();
        work();
        return OfCurrentThread() - start;
    }

    /// <summary>The processor time the current thread has used since it started.</summary>
    private static TimeSpan OfCurrentThread()
    {
        if (OperatingSystem.IsWindows())
        {
            if (!GetThreadTimes(GetCurrentThread(), out _, out _, out var kernel, out var user))
                throw new InvalidOperationException($"GetThreadTimes failed with error {Marshal.GetLastPInvokeError()}.");
            return TimeSpan.FromTicks(kernel + user); // FILETIME counts 100 ns, like ticks
        }

        // CLOCK_THREAD_CPUTIME_ID: 3 on Linux, 16 on macOS.
        var clock = OperatingSystem.IsMacOS() ? 16 : 3;
        if (ClockGetTime(clock, out var time) != 0)
            throw new InvalidOperationException($"clock_gettime failed with error {Marshal.GetLastPInvokeError()}.");
        return TimeSpan.FromTicks((time.Seconds * TimeSpan.TicksPerSecond) + (time.Nanoseconds / 100));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TimeSpec
    {
        public long Seconds;
        public long Nanoseconds;
    }

    [LibraryImport("libc", EntryPoint = "clock_gettime", SetLastError = true)]
    private static partial int ClockGetTime(int clock, out TimeSpec time);

    [LibraryImport("kernel32")]
    private static partial IntPtr GetCurrentThread();

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);
}
