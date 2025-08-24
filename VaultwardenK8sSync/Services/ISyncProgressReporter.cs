namespace VaultwardenK8sSync.Services;

/// <summary>
/// Interface for reporting sync progress
/// </summary>
public interface ISyncProgressReporter
{
    void SetPhase(string phase);
    void AddItem(string key, string name, string status = "Pending");
    void UpdateItem(string key, string status, string? details = null, SyncItemOutcome? outcome = null);
    void Start(string phase, int totalItems = 0);
    void Complete(string? finalMessage = null);
}

/// <summary>
/// No-op progress reporter for when dynamic display is not needed
/// </summary>
public class NullProgressReporter : ISyncProgressReporter
{
    public void SetPhase(string phase) { }
    public void AddItem(string key, string name, string status = "Pending") { }
    public void UpdateItem(string key, string status, string? details = null, SyncItemOutcome? outcome = null) { }
    public void Start(string phase, int totalItems = 0) { }
    public void Complete(string? finalMessage = null) { }
}

/// <summary>
/// Wrapper that makes DynamicSyncProgressDisplay implement ISyncProgressReporter
/// </summary>
public class DynamicProgressReporter : ISyncProgressReporter, IDisposable
{
    private readonly DynamicSyncProgressDisplay _display;

    public DynamicProgressReporter(DynamicSyncProgressDisplay display)
    {
        _display = display;
    }

    public void SetPhase(string phase) => _display.SetPhase(phase);
    public void AddItem(string key, string name, string status = "Pending") => _display.AddItem(key, name, status);
    public void UpdateItem(string key, string status, string? details = null, SyncItemOutcome? outcome = null) 
        => _display.UpdateItem(key, status, details, outcome);
    public void Start(string phase, int totalItems = 0) => _display.Start(phase, totalItems);
    public void Complete(string? finalMessage = null) => _display.Complete(finalMessage);

    public void Dispose() => _display.Dispose();
}


