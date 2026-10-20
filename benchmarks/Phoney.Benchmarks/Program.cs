using BenchmarkDotNet.Running;
using Phoney.Benchmarks;

// Run all:     dotnet run -c Release --project benchmarks/Phoney.Benchmarks -- --filter '*'
// Quick check: dotnet run -c Release --project benchmarks/Phoney.Benchmarks -- --filter '*' --job short
BenchmarkSwitcher.FromAssembly(typeof(ValueBenchmarks).Assembly).Run(args);
