namespace VaultwardenK8sSync.Models;

public class AppConfiguration
{
    public VaultwardenConfig Vaultwarden { get; set; } = new();
    public KubernetesConfig Kubernetes { get; set; } = new();
    public SyncConfig Sync { get; set; } = new();
}

public class VaultwardenConfig
{
    public string ServerUrl { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string MasterPassword { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public bool UseApiKey { get; set; } = false;
}

public class KubernetesConfig
{
    public string? KubeConfigPath { get; set; }
    public string? Context { get; set; }
    public string DefaultNamespace { get; set; } = "default";
    public bool InCluster { get; set; } = false;
}

public class SyncConfig
{
    public string NamespaceTag { get; set; } = "#namespace:";
    public bool DryRun { get; set; } = false;
    public bool DeleteOrphans { get; set; } = true;
    public string SecretPrefix { get; set; } = "vaultwarden-";
    public int SyncIntervalMinutes { get; set; } = 60;
    public bool ContinuousSync { get; set; } = false;
} 