using BenchmarkDotNet.Running;
using VaultwardenK8sSync.Benchmarks;

// Run all benchmarks
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
