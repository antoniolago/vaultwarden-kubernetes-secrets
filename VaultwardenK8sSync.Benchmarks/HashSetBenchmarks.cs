using BenchmarkDotNet.Attributes;

namespace VaultwardenK8sSync.Benchmarks;

/// <summary>
/// Benchmarks comparing List.Contains vs HashSet.Contains for managed keys lookup.
/// Demonstrates CPU savings from P3 optimization.
/// </summary>
[MemoryDiagnoser]
public class HashSetBenchmarks
{
    private List<string> _managedKeysList = null!;
    private HashSet<string> _managedKeysSet = null!;
    private Dictionary<string, byte[]> _secretData = null!;

    [Params(10, 50, 100)]
    public int ManagedKeysCount { get; set; }

    [Params(10, 50, 100)]
    public int SecretDataKeysCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Create managed keys
        _managedKeysList = Enumerable.Range(0, ManagedKeysCount)
            .Select(i => $"managed-key-{i}")
            .ToList();

        _managedKeysSet = new HashSet<string>(_managedKeysList, StringComparer.OrdinalIgnoreCase);

        // Create secret data where all keys are managed (simulating HasOnlyManagedKeysAsync scenario)
        _secretData = Enumerable.Range(0, SecretDataKeysCount)
            .ToDictionary(
                i => $"managed-key-{i % ManagedKeysCount}",  // Keys exist in managed keys
                _ => new byte[] { 1, 2, 3 }
            );
    }

    [Benchmark(Baseline = true)]
    public bool ListContains()
    {
        // Original O(n×m) implementation
        return _secretData.All(kvp => _managedKeysList.Contains(kvp.Key));
    }

    [Benchmark]
    public bool HashSetContains()
    {
        // Optimized O(n) implementation
        return _secretData.All(kvp => _managedKeysSet.Contains(kvp.Key));
    }

    [Benchmark]
    public bool HashSetContainsWithCreation()
    {
        // Including HashSet creation cost (real-world scenario)
        var managedKeysSet = new HashSet<string>(_managedKeysList, StringComparer.OrdinalIgnoreCase);
        return _secretData.All(kvp => managedKeysSet.Contains(kvp.Key));
    }
}
