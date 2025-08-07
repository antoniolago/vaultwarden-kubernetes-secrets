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
                Email = Environment.GetEnvironmentVariable("VAULTWARDEN__EMAIL") ?? "",
                MasterPassword = Environment.GetEnvironmentVariable("VAULTWARDEN__MASTERPASSWORD") ?? "",
                ClientId = Environment.GetEnvironmentVariable("BW_CLIENTID"),
                ClientSecret = Environment.GetEnvironmentVariable("BW_CLIENTSECRET"),
                UseApiKey = bool.TryParse(Environment.GetEnvironmentVariable("VAULTWARDEN__USEAPIKEY"), out var useApiKey) && useApiKey
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
                NamespaceTag = Environment.GetEnvironmentVariable("SYNC__NAMESPACETAG") ?? "#namespace:",
                DryRun = bool.TryParse(Environment.GetEnvironmentVariable("SYNC__DRYRUN"), out var dryRun) && dryRun,
                DeleteOrphans = bool.TryParse(Environment.GetEnvironmentVariable("SYNC__DELETEORPHANS"), out var deleteOrphans) ? deleteOrphans : true,
                SecretPrefix = Environment.GetEnvironmentVariable("SYNC__SECRETPREFIX") ?? "vaultwarden-",
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

    [Required(ErrorMessage = "Vaultwarden Email is required")]
    [EmailAddress(ErrorMessage = "Vaultwarden Email must be a valid email address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vaultwarden MasterPassword is required")]
    public string MasterPassword { get; set; } = string.Empty;
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public bool UseApiKey { get; set; } = true;

    public bool ValidateApiKeyMode(out List<ValidationResult> validationResults)
    {
        validationResults = new List<ValidationResult>();
        
        if (UseApiKey)
        {
            if (string.IsNullOrEmpty(ClientId))
                validationResults.Add(new ValidationResult("ClientId is required when UseApiKey is true"));
            if (string.IsNullOrEmpty(ClientSecret))
                validationResults.Add(new ValidationResult("ClientSecret is required when UseApiKey is true"));
        }

        return validationResults.Count == 0;
    }
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
    public string NamespaceTag { get; set; } = "#namespace:";
    public bool DryRun { get; set; } = false;
    public bool DeleteOrphans { get; set; } = true;
    public string SecretPrefix { get; set; } = "vaultwarden-";
    public int SyncIntervalSeconds { get; set; } = 3600; // 60 minutes in seconds
    public bool ContinuousSync { get; set; } = false;
}

public class LoggingSettings
{
    public string DefaultLevel { get; set; } = "Information";
    public string MicrosoftLevel { get; set; } = "Warning";
    public string MicrosoftHostingLifetimeLevel { get; set; } = "Information";
} 