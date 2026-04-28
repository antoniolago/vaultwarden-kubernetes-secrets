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

    public string GetStateKey()
    {
        var nsParts = Namespaces
            .OrderBy(n => n.Name)
            .Select(n =>
            {
                var secrets = string.Join(",", n.Secrets.OrderBy(s => s.Name).Select(s => $"{s.Name}={s.Outcome}:{s.ChangeReason}:{s.Error ?? ""}"));
                var errors = string.Join(",", n.Errors.OrderBy(e => e));
                return $"{n.Name}:{n.Created}:{n.Updated}:{n.Skipped}:{n.Failed}:{secrets}|{errors}";
            });
        var errorsStr = string.Join(",", Errors.OrderBy(e => e));
        var warningsStr = string.Join(",", Warnings.OrderBy(w => w));
        return string.Join("|", nsParts) + $"|e:{errorsStr}|w:{warningsStr}";
    }

    /// <summary>
    /// Builds a delta summary comparing current state against a previous state.
    /// Only includes items that changed status (created, updated, failed, removed).
    /// Returns null if no changes detected.
    /// </summary>
    public SyncSummary? BuildDeltaSummary(SyncSummary? previous)
    {
        if (previous == null || previous.Namespaces.Count == 0)
            return this;

        if (!HasChanges && Namespaces.Count == 0)
            return null;

        var delta = new SyncSummary
        {
            StartTime = StartTime,
            EndTime = EndTime,
            SyncNumber = SyncNumber,
            OverallSuccess = OverallSuccess,
            SyncIntervalSeconds = SyncIntervalSeconds,
            TotalItemsFromVaultwarden = TotalItemsFromVaultwarden,
            TotalNamespaces = 0,  // Will be set after delta namespace loop
            HasChanges = HasChanges,
            OrphanCleanup = BuildOrphanDelta(previous.OrphanCleanup),
        };

        var previousNsMap = previous.Namespaces.ToDictionary(n => n.Name, n => n);
        var hasAnyChanges = false;

        foreach (var currentNs in Namespaces)
        {
            var deltaNs = new NamespaceSummary { Name = currentNs.Name, Success = currentNs.Success, SourceItems = currentNs.SourceItems };
            var previousNs = previousNsMap.GetValueOrDefault(currentNs.Name);

            var previousSecretsMap = previousNs?.Secrets.ToDictionary(s => s.Name, s => s) ?? new Dictionary<string, SecretSummary>();
            var currentSecretsMap = currentNs.Secrets.ToDictionary(s => s.Name, s => s);

            foreach (var (name, currentSecret) in currentSecretsMap)
            {
                if (!previousSecretsMap.TryGetValue(name, out var prevSecret))
                {
                    deltaNs.AddSecret(currentSecret);
                    hasAnyChanges = true;
                }
                else if (currentSecret.Outcome != prevSecret.Outcome || currentSecret.Error != prevSecret.Error)
                {
                    deltaNs.AddSecret(currentSecret);
                    hasAnyChanges = true;
                }
            }

            foreach (var (name, prevSecret) in previousSecretsMap)
            {
                if (!currentSecretsMap.ContainsKey(name))
                {
                    deltaNs.AddSecret(new SecretSummary
                    {
                        Name = name,
                        Outcome = ReconcileOutcome.Skipped,
                        ChangeReason = "removed",
                        Error = "no longer in Vaultwarden"
                    });
                    hasAnyChanges = true;
                }
            }

            var previousErrors = new HashSet<string>(previousNs?.Errors ?? []);
            foreach (var error in currentNs.Errors)
            {
                if (!previousErrors.Contains(error))
                {
                    deltaNs.Errors.Add(error);
                    hasAnyChanges = true;
                }
            }

            if (deltaNs.Secrets.Count > 0 || deltaNs.Errors.Count > 0)
            {
                // Only include namespace in delta if it has actual changes
                // (Created/Updated/Failed/removed), not just items settling to Up-to-date.
                // This prevents noise when only 1 item changes but all namespaces
                // get re-processed — only namespaces with non-Skipped activity appear.
                var hasRealChanges = deltaNs.Created > 0 || deltaNs.Updated > 0 || deltaNs.Failed > 0 
                    || deltaNs.Secrets.Any(s => s.ChangeReason == "removed")
                    || deltaNs.Errors.Count > 0;
                if (hasRealChanges)
                {
                    delta.AddNamespace(deltaNs);
                }
            }
        }

        var previousErrorSet = new HashSet<string>(previous.Errors);
        foreach (var error in Errors)
        {
            if (!previousErrorSet.Contains(error))
            {
                delta.Errors.Add(error);
                hasAnyChanges = true;
            }
        }

        var previousWarningSet = new HashSet<string>(previous.Warnings);
        foreach (var warning in Warnings)
        {
            if (!previousWarningSet.Contains(warning))
            {
                delta.Warnings.Add(warning);
                hasAnyChanges = true;
            }
        }

        // Set namespace count to reflect only namespaces with actual delta changes
        delta.TotalNamespaces = delta.Namespaces.Count;

        return hasAnyChanges ? delta : null;
    }

    private OrphanCleanupSummary? BuildOrphanDelta(OrphanCleanupSummary? previous)
    {
        if (OrphanCleanup == null) return null;
        if (previous == null) return OrphanCleanup;

        // Only include if orphans were actually deleted (new action)
        if (OrphanCleanup.TotalOrphansDeleted > 0 && OrphanCleanup.TotalOrphansDeleted != previous.TotalOrphansDeleted)
            return OrphanCleanup;

        return null;
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
                // Propagate secret error to namespace errors for summary display
                if (!string.IsNullOrEmpty(secret.Error))
                {
                    Errors.Add($"Secret '{secret.Name}': {secret.Error}");
                }
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
