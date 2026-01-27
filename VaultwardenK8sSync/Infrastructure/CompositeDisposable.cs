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

        List<Exception>? exceptions = null;

        // Dispose in reverse order (LIFO)
        for (var i = _disposables.Count - 1; i >= 0; i--)
        {
            try
            {
                _disposables[i].Dispose();
            }
            catch (Exception ex)
            {
                exceptions ??= new List<Exception>();
                exceptions.Add(ex);
            }
        }

        if (exceptions != null)
        {
            throw new AggregateException("One or more disposables threw exceptions during disposal.", exceptions);
        }
    }
}
