using System.Text;

namespace VaultwardenK8sSync.Models;

public class SyncSummary
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public int SyncNumber { get; set; }
    public bool OverallSuccess { get; set; } = true;
    public int SyncIntervalSeconds { get; set; }
    
    public int TotalItemsFromVaultwarden { get; set; }
    public int TotalNamespaces { get; set; }
    public bool HasChanges { get; set; }
    
    public List<NamespaceSummary> Namespaces { get; set; } = new();
    public OrphanCleanupSummary? OrphanCleanup { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    
    public int TotalSecretsCreated => Namespaces.Sum(n => n.Created);
    public int TotalSecretsUpdated => Namespaces.Sum(n => n.Updated);
    public int TotalSecretsSkipped => Namespaces.Sum(n => n.Skipped);
    public int TotalSecretsFailed => Namespaces.Sum(n => n.Failed);
    public int TotalSecretsProcessed => TotalSecretsCreated + TotalSecretsUpdated + TotalSecretsSkipped + TotalSecretsFailed;
    
    public void AddNamespace(NamespaceSummary namespaceSummary)
    {
        Namespaces.Add(namespaceSummary);
        if (!namespaceSummary.Success)
        {
            OverallSuccess = false;
        }
    }
    
    public void AddError(string error)
    {
        Errors.Add(error);
        OverallSuccess = false;
    }
    
    public void AddWarning(string warning)
    {
        Warnings.Add(warning);
    }
    
    public string GetStatusIcon()
    {
        // Complete failure: no secrets processed successfully
        if (!OverallSuccess && TotalSecretsProcessed == 0) return "❌";
        
        // Partial success: some failures but also some successes
        if (TotalSecretsFailed > 0) return "⚠️";
        
        // Complete success with changes
        if (TotalSecretsCreated > 0 || TotalSecretsUpdated > 0) return "✅";
        
        // No changes needed
        return "⭕";
    }
    
    public string GetStatusText()
    {
        // Complete failure: no secrets processed successfully
        if (!OverallSuccess && TotalSecretsProcessed == 0) return "FAILED";
        
        // Partial success: some failures but also some successes
        if (TotalSecretsFailed > 0) return "PARTIAL";
        
        // Complete success with changes
        if (TotalSecretsCreated > 0 || TotalSecretsUpdated > 0) return "SUCCESS";
        
        // No changes needed
        return "UP-TO-DATE";
    }
}

public class NamespaceSummary
{
    public string Name { get; set; } = string.Empty;
    public bool Success { get; set; } = true;
    public int SourceItems { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<SecretSummary> Secrets { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    
    public string GetStatusIcon()
    {
        if (!Success || Failed > 0) return "❌";
        if (Created > 0 || Updated > 0) return "✅";
        return "⭕";
    }
    
    public void AddSecret(SecretSummary secret)
    {
        Secrets.Add(secret);
        switch (secret.Outcome)
        {
            case ReconcileOutcome.Created:
                Created++;
                break;
            case ReconcileOutcome.Updated:
                Updated++;
                break;
            case ReconcileOutcome.Skipped:
                Skipped++;
                break;
            case ReconcileOutcome.Failed:
                Failed++;
                Success = false;
                break;
        }
    }
}

public class SecretSummary
{
    public string Name { get; set; } = string.Empty;
    public ReconcileOutcome Outcome { get; set; }
    public string? ChangeReason { get; set; }
    public int SourceItemCount { get; set; }
    public string? Error { get; set; }
    
    public string GetStatusIcon()
    {
        return Outcome switch
        {
            ReconcileOutcome.Created => "🆕",
            ReconcileOutcome.Updated => "🔄",
            ReconcileOutcome.Skipped => "✅",
            ReconcileOutcome.Failed => "❌",
            _ => "❓"
        };
    }
    
    public string GetStatusText()
    {
        return Outcome switch
        {
            ReconcileOutcome.Created => "CREATED",
            ReconcileOutcome.Updated => $"UPDATED ({ChangeReason})",
            ReconcileOutcome.Skipped => "UP-TO-DATE",
            ReconcileOutcome.Failed => $"FAILED ({Error})",
            _ => "UNKNOWN"
        };
    }
}

public class OrphanCleanupSummary
{
    public bool Enabled { get; set; }
    public bool Success { get; set; } = true;
    public int TotalOrphansFound { get; set; }
    public int TotalOrphansDeleted { get; set; }
    public List<OrphanNamespaceSummary> Namespaces { get; set; } = new();
    
    public string GetStatusIcon()
    {
        if (!Success) return "❌";
        if (TotalOrphansDeleted > 0) return "🧹";
        return "✨";
    }
}

public class OrphanNamespaceSummary
{
    public string Name { get; set; } = string.Empty;
    public int OrphansFound { get; set; }
    public int OrphansDeleted { get; set; }
    public List<string> OrphanNames { get; set; } = new();
}

public enum ReconcileOutcome
{
    Created,
    Updated,
    Skipped,
    Failed
}
