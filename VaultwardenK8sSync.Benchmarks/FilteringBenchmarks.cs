using BenchmarkDotNet.Attributes;

namespace VaultwardenK8sSync.Benchmarks;

/// <summary>
/// Benchmarks comparing intermediate ToList() allocations vs chained predicates.
/// Demonstrates memory savings from P2 optimization.
/// </summary>
[MemoryDiagnoser]
public class FilteringBenchmarks
{
    private List<TestItem> _items = null!;
    private readonly string _orgId = "org-123";
    private readonly string _folderId = "folder-456";
    private readonly string _collectionId = "collection-789";

    [Params(100, 1000, 10000)]
    public int ItemCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _items = Enumerable.Range(0, ItemCount)
            .Select(i => new TestItem
            {
                Id = $"item-{i}",
                OrganizationId = i % 3 == 0 ? _orgId : $"other-org-{i % 5}",
                FolderId = i % 4 == 0 ? _folderId : $"other-folder-{i % 7}",
                CollectionIds = i % 5 == 0
                    ? new List<string> { _collectionId, "other-collection" }
                    : new List<string> { "other-collection" }
            })
            .ToList();
    }

    [Benchmark(Baseline = true)]
    public List<TestItem> IntermediateToList()
    {
        // Original implementation with 3 intermediate allocations
        var items = _items;

        items = items.Where(i => string.Equals(i.OrganizationId, _orgId, StringComparison.OrdinalIgnoreCase)).ToList();
        items = items.Where(i => string.Equals(i.FolderId, _folderId, StringComparison.OrdinalIgnoreCase)).ToList();
        items = items.Where(i => i.CollectionIds != null && i.CollectionIds.Contains(_collectionId, StringComparer.OrdinalIgnoreCase)).ToList();

        return items;
    }

    [Benchmark]
    public List<TestItem> ChainedPredicates()
    {
        // Optimized implementation with single allocation
        var query = _items.AsEnumerable();

        query = query.Where(i => string.Equals(i.OrganizationId, _orgId, StringComparison.OrdinalIgnoreCase));
        query = query.Where(i => string.Equals(i.FolderId, _folderId, StringComparison.OrdinalIgnoreCase));
        query = query.Where(i => i.CollectionIds != null && i.CollectionIds.Contains(_collectionId, StringComparer.OrdinalIgnoreCase));

        return query.ToList();
    }

    public class TestItem
    {
        public string Id { get; set; } = "";
        public string? OrganizationId { get; set; }
        public string? FolderId { get; set; }
        public List<string>? CollectionIds { get; set; }
    }
}
