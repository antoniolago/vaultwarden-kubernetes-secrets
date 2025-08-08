using System.ComponentModel.DataAnnotations;

namespace VaultwardenK8sSync;

public class AppSettings
{
    public VaultwardenSettings Vaultwarden { get; set; } = new();
    public KubernetesSettings Kubernetes { get; set; } = new();
    public SyncSettings Sync { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();

    public static AppSettings FromEnvironment()
    {
        return new AppSettings
        {
            Vaultwarden = new VaultwardenSettings
            {
                ServerUrl = Environment.GetEnvironmentVariable("VAULTWARDEN__SERVERURL") ?? "",
                MasterPassword = Environment.GetEnvironmentVariable("VAULTWARDEN__MASTERPASSWORD") ?? "",
                ClientId = Environment.GetEnvironmentVariable("BW_CLIENTID"),
                ClientSecret = Environment.GetEnvironmentVariable("BW_CLIENTSECRET"),
                OrganizationId = Environment.GetEnvironmentVariable("VAULTWARDEN__ORGANIZATIONID"),
                OrganizationName = Environment.GetEnvironmentVariable("VAULTWARDEN__ORGANIZATIONNAME"),
                FolderId = Environment.GetEnvironmentVariable("VAULTWARDEN__FOLDERID"),
                FolderName = Environment.GetEnvironmentVariable("VAULTWARDEN__FOLDERNAME"),
                CollectionId = Environment.GetEnvironmentVariable("VAULTWARDEN__COLLECTIONID"),
                CollectionName = Environment.GetEnvironmentVariable("VAULTWARDEN__COLLECTIONNAME")
            },
            Kubernetes = new KubernetesSettings
            {
                KubeConfigPath = Environment.GetEnvironmentVariable("KUBERNETES__KUBECONFIGPATH"),
                Context = Environment.GetEnvironmentVariable("KUBERNETES__CONTEXT"),
                DefaultNamespace = Environment.GetEnvironmentVariable("KUBERNETES__DEFAULTNAMESPACE") ?? "default",
                InCluster = bool.TryParse(Environment.GetEnvironmentVariable("KUBERNETES__INCLUSTER"), out var inCluster) && inCluster
            },
            Sync = new SyncSettings
            {
                NamespaceTag = Environment.GetEnvironmentVariable("SYNC__NAMESPACETAG") ?? "#namespaces:",
                DryRun = bool.TryParse(Environment.GetEnvironmentVariable("SYNC__DRYRUN"), out var dryRun) && dryRun,
                DeleteOrphans = bool.TryParse(Environment.GetEnvironmentVariable("SYNC__DELETEORPHANS"), out var deleteOrphans) ? deleteOrphans : true,
                SyncIntervalSeconds = int.TryParse(Environment.GetEnvironmentVariable("SYNC__SYNCINTERVALSECONDS"), out var syncInterval) ? syncInterval : 3600,
                ContinuousSync = bool.TryParse(Environment.GetEnvironmentVariable("SYNC__CONTINUOUSSYNC"), out var continuousSync) && continuousSync
            },
            Logging = new LoggingSettings
            {
                DefaultLevel = Environment.GetEnvironmentVariable("LOGGING__LOGLEVEL__DEFAULT") ?? "Information",
                MicrosoftLevel = Environment.GetEnvironmentVariable("LOGGING__LOGLEVEL__MICROSOFT") ?? "Warning",
                MicrosoftHostingLifetimeLevel = Environment.GetEnvironmentVariable("LOGGING__LOGLEVEL__MICROSOFT__HOSTING__LIFETIME") ?? "Information"
            }
        };
    }

    public bool Validate(out List<ValidationResult> validationResults)
    {
        var context = new ValidationContext(this);
        validationResults = new List<ValidationResult>();
        return Validator.TryValidateObject(this, context, validationResults, true);
    }
}

public class VaultwardenSettings
{
    [Required(ErrorMessage = "Vaultwarden ServerUrl is required")]
    public string ServerUrl { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vaultwarden MasterPassword is required")]
    public string MasterPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "BW_CLIENTID is required for API key authentication")]
    public string? ClientId { get; set; }

    [Required(ErrorMessage = "BW_CLIENTSECRET is required for API key authentication")]
    public string? ClientSecret { get; set; }

    // Optional: limit syncing to a specific organization
    public string? OrganizationId { get; set; }
    public string? OrganizationName { get; set; }

    // Optional: restrict items to a specific folder
    public string? FolderId { get; set; }
    public string? FolderName { get; set; }

    // Optional: restrict items to a specific collection
    public string? CollectionId { get; set; }
    public string? CollectionName { get; set; }

    // Password login removed; API key is the only supported mode
}

public class KubernetesSettings
{
    public string? KubeConfigPath { get; set; }
    public string? Context { get; set; }
    public string DefaultNamespace { get; set; } = "default";
    public bool InCluster { get; set; } = false;
}

public class SyncSettings
{
    public string NamespaceTag { get; set; } = "#namespaces:";
    public bool DryRun { get; set; } = false;
    public bool DeleteOrphans { get; set; } = true;
    public int SyncIntervalSeconds { get; set; } = 3600; // 60 minutes in seconds
    public bool ContinuousSync { get; set; } = false;
}

public class LoggingSettings
{
    public string DefaultLevel { get; set; } = "Information";
    public string MicrosoftLevel { get; set; } = "Warning";
    public string MicrosoftHostingLifetimeLevel { get; set; } = "Information";
} 