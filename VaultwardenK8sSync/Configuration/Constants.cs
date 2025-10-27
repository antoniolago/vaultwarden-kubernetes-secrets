namespace VaultwardenK8sSync.Configuration;

public static class Constants
{
    public static class Timeouts
    {
        public const int DefaultCommandTimeoutSeconds = 30;
        public const int LoginTimeoutSeconds = 60;
        public const int SyncTimeoutSeconds = 120;
        public const int UnlockTimeoutSeconds = 60;
    }
    
    public static class Kubernetes
    {
        public const string ManagedByLabel = "app.kubernetes.io/managed-by";
        public const string CreatedByLabel = "app.kubernetes.io/created-by";
        public const string ManagedByValue = "vaultwarden-kubernetes-secrets";
        public const string SyncServiceValue = "vaultwarden-k8s-sync"; // Specific to sync service
        public const string HashAnnotationKey = "vaultwarden-kubernetes-secrets/content-hash";
        public const string SecretType = "Opaque";
    }
    
    public static class Cache
    {
        public const int SecretExistsCacheTimeoutSeconds = 30;
    }
    
    public static class Delays
    {
        public const int PostCommandDelayMs = 1000;
        public const int PostUnlockDelayMs = 1500;
    }
}

