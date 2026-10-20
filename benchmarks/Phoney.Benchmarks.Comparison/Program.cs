using System.Diagnostics;
using System.Globalization;
using AutoBogus;
using AutoFixture;
using BenchmarkDotNet.Running;
using FizzWare.NBuilder;
using Phoney.Benchmarks.Comparison;

// Timed comparison:  dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- --filter '*'
// Cold start:        dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- cold [library...]   (all libraries, or just the ones named)
// Native AOT:        dotnet publish benchmarks/Phoney.Benchmarks.Comparison -c Release -r linux-x64 -p:AotComparison=true -o out-aot, then run out-aot/Phoney.Benchmarks.Comparison with `cold`, `check` or `sanity`
// Cold phases:       dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- cold-phases        (where Phoney's first call spends its time; set DOTNET_* variables to try runtime settings)
// Check:             dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- check   (prints one object per library, to see that they make comparable data)
// Summary:           dotnet run -c Release --project benchmarks/Phoney.Benchmarks.Comparison -- summarize <artifacts directory of the timed run>   (prints the README's comparison matrix)
if (args is ["cold-child", var library])
{
    return ColdChild(library);
}

if (args is ["cold", ..var only])
{
    return ColdStart.Run(only);
}

if (args is ["cold-phases-child"])
{
    return ColdPhases.Child();
}

if (args is ["cold-phases"])
{
    return ColdPhases.Run();
}

if (args is ["sanity"])
{
    return Sanity.Run();
}

if (args is ["check"])
{
    return Check.Run();
}

if (args is ["summarize", var directory])
{
    return Summary.Run(directory);
}

BenchmarkSwitcher.FromAssembly(typeof(ValueComparison).Assembly).Run(args);
return 0;

static int ColdChild(string library)
{
    // The time to the first object of a fresh process: runtime and library start-up are outside, loading, JIT, building rules and the first value are inside.
    var clock = Stopwatch.StartNew();
    object? first = library switch
    {
        LibraryNames.PhoneyReflection => Libraries.PhoneyFlatReflection().Generate(),
        LibraryNames.PhoneyGenerated => Libraries.PhoneyFlatGenerated().Generate(),
        LibraryNames.Bogus => Libraries.BogusFlat().Generate(),
        LibraryNames.AutoBogus => Libraries.AutoBogusFlat().Generate(),
        LibraryNames.AutoFixture => Libraries.AutoFixture().Create<FlatCustomer>(),
        LibraryNames.NBuilder => Builder<FlatCustomer>.CreateNew().Build(),
        LibraryNames.FakerNet => global::Faker.Name.First(),
        _ => throw new ArgumentException("Unknown library " + library),
    };
    clock.Stop();
    GC.KeepAlive(first);
    Console.WriteLine(clock.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture));
    return 0;
}
