namespace VaultwardenK8sSync.Infrastructure;

/// <summary>
/// Combines multiple IDisposable instances into a single IDisposable.
/// When disposed, all contained disposables are disposed in reverse order.
/// </summary>
public sealed class CompositeDisposable : IDisposable
{
    private readonly List<IDisposable> _disposables;
    private bool _disposed;

    public CompositeDisposable(params IDisposable[] disposables)
    {
        _disposables = new List<IDisposable>(disposables);
    }

    public CompositeDisposable(IEnumerable<IDisposable> disposables)
    {
        _disposables = new List<IDisposable>(disposables);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Dispose in reverse order (LIFO)
        for (var i = _disposables.Count - 1; i >= 0; i--)
        {
            _disposables[i].Dispose();
        }
    }
}
