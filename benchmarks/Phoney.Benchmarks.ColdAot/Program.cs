using System.Diagnostics;
using System.Globalization;
using Phoney;
using Phoney.Generation;

// Prints the milliseconds from here to the first generated object, like `cold-child` of the comparison project does for the JIT build.
// Publish:  dotnet publish benchmarks/Phoney.Benchmarks.ColdAot -c Release -o artifacts/cold-aot
// Measure:  dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- cold-aot artifacts/cold-aot/Phoney.Benchmarks.ColdAot
var clock = Stopwatch.StartNew();
var first = ColdFakes.FlatCustomer.Seed(1).Generate();
clock.Stop();
GC.KeepAlive(first);
Console.WriteLine(clock.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture));

/// <summary>The same object as <c>FlatCustomer</c> of the comparison project.</summary>
public sealed class FlatCustomer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string Company { get; set; } = null!;
    public string City { get; set; } = null!;
    public string ZipCode { get; set; } = null!;
    public DateTime BirthDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal Balance { get; set; }
}

[FakeFor<FlatCustomer>]
public static partial class ColdFakes;
