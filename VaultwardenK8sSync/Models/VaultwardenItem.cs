using System.Text.Json.Serialization;
using System.Linq;
using System;

namespace VaultwardenK8sSync.Models;

public class VaultwardenItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("uris")]
    public List<UriInfo>? Uris { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = string.Empty;

    [JsonPropertyName("folderId")]
    public string? FolderId { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("favorite")]
    public bool Favorite { get; set; }

    [JsonPropertyName("reprompt")]
    public int Reprompt { get; set; }

    [JsonPropertyName("login")]
    public LoginInfo? Login { get; set; }

    [JsonPropertyName("secureNote")]
    public SecureNoteInfo? SecureNote { get; set; }

    [JsonPropertyName("card")]
    public CardInfo? Card { get; set; }

    [JsonPropertyName("identity")]
    public IdentityInfo? Identity { get; set; }

    [JsonPropertyName("sshKey")]
    public SshKeyInfo? SshKey { get; set; }
    

    [JsonPropertyName("fields")]
    public List<FieldInfo>? Fields { get; set; }

    [JsonPropertyName("attachments")]
    public List<AttachmentInfo>? Attachments { get; set; }

    [JsonPropertyName("organizationId")]
    public string? OrganizationId { get; set; }

    [JsonPropertyName("collectionIds")]
    public List<string>? CollectionIds { get; set; }

    [JsonPropertyName("revisionDate")]
    public DateTime RevisionDate { get; set; }

    [JsonPropertyName("creationDate")]
    public DateTime CreationDate { get; set; }

    [JsonPropertyName("deletedDate")]
    public DateTime? DeletedDate { get; set; }



    private string? GetCustomFieldValue(params string[] candidateNames)
    {
        if (Fields == null || Fields.Count == 0)
            return null;

        foreach (var name in candidateNames)
        {
            var match = Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match != null && !string.IsNullOrEmpty(match.Value))
            {
                return match.Value;
            }
        }
        return null;
    }

    public string? ExtractNamespace()
    {
        // Get from custom field
        var fromField = GetCustomFieldValue(FieldNameConfig.NamespacesFieldName, "namespaces");
        if (!string.IsNullOrWhiteSpace(fromField))
        {
            return fromField.Trim();
        }

        return null;
    }

    public List<string> ExtractNamespaces()
    {
        var namespaces = new List<string>();

        // First, parse from custom field if present (comma-separated)
        var fromField = GetCustomFieldValue(FieldNameConfig.NamespacesFieldName, "namespaces");
        if (!string.IsNullOrWhiteSpace(fromField))
        {
            var list = fromField.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var ns in list)
            {
                var cleanNs = ns.Trim();
                if (!string.IsNullOrEmpty(cleanNs) && !namespaces.Contains(cleanNs))
                {
                    namespaces.Add(cleanNs);
                }
            }
        }



        return namespaces;
    }

    public string? ExtractSecretName()
    {
        // Prefer custom field if provided
        var fromField = GetCustomFieldValue(FieldNameConfig.SecretNameFieldName, "secret-name");
        if (!string.IsNullOrWhiteSpace(fromField))
            return fromField.Trim();

        return null;
    }



    public string? ExtractSecretKeyPassword()
    {
        // Prefer custom field if provided
        var fromField = GetCustomFieldValue(FieldNameConfig.SecretKeyPasswordFieldName, "secret-key-password", "secret-key");
        if (!string.IsNullOrWhiteSpace(fromField))
            return fromField.Trim();

        return null;
    }

    public string? ExtractSecretKeyUsername()
    {
        // Prefer custom field if provided
        var fromField = GetCustomFieldValue(FieldNameConfig.SecretKeyUsernameFieldName, "secret-key-username");
        if (!string.IsNullOrWhiteSpace(fromField))
            return fromField.Trim();

        return null;
    }

    /// <summary>
    /// Extract the list of field names that should be ignored during synchronization.
    /// The ignore-field can contain a comma-separated list of field names.
    /// </summary>
    public HashSet<string> ExtractIgnoredFields()
    {
        var ignoredFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Get the ignore-field value from custom fields
        var ignoreFieldValue = GetCustomFieldValue(FieldNameConfig.IgnoreFieldName);
        if (!string.IsNullOrWhiteSpace(ignoreFieldValue))
        {
            // Split by comma and trim each field name
            var fieldNames = ignoreFieldValue.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var fieldName in fieldNames)
            {
                var trimmedName = fieldName.Trim();
                if (!string.IsNullOrEmpty(trimmedName))
                {
                    ignoredFields.Add(trimmedName);
                }
            }
        }
        

        
        // Always add the ignore-field itself to the ignored list to prevent it from being synced
        ignoredFields.Add(FieldNameConfig.IgnoreFieldName);
        
        // Also add secret-annotations and secret-labels to prevent them from being synced as secret data
        ignoredFields.Add(FieldNameConfig.SecretAnnotationsFieldName);
        ignoredFields.Add(FieldNameConfig.SecretLabelsFieldName);
        
        return ignoredFields;
    }

    /// <summary>
    /// Extract custom annotations from the secret-annotations field.
    /// Expected format: multiline Note field where each line is "key=value" or "key: value"
    /// </summary>
    public Dictionary<string, string> ExtractSecretAnnotations()
    {
        var annotations = new Dictionary<string, string>();
        
        var annotationsField = GetCustomFieldValue(FieldNameConfig.SecretAnnotationsFieldName);
        if (string.IsNullOrWhiteSpace(annotationsField))
            return annotations;

        // Parse multiline format where each line is "key=value" or "key: value"
        var lines = annotationsField.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                continue;

            // Try to split by '=' first, then ':'
            var separatorIndex = trimmedLine.IndexOf('=');
            if (separatorIndex < 0)
                separatorIndex = trimmedLine.IndexOf(':');
            
            if (separatorIndex > 0)
            {
                var key = trimmedLine.Substring(0, separatorIndex).Trim();
                var value = trimmedLine.Substring(separatorIndex + 1).Trim();
                
                if (!string.IsNullOrEmpty(key))
                {
                    annotations[key] = value;
                }
            }
        }
        
        return annotations;
    }

    /// <summary>
    /// Extract custom labels from the secret-labels field.
    /// Expected format: multiline Note field where each line is "key=value" or "key: value"
    /// </summary>
    public Dictionary<string, string> ExtractSecretLabels()
    {
        var labels = new Dictionary<string, string>();
        
        var labelsField = GetCustomFieldValue(FieldNameConfig.SecretLabelsFieldName);
        if (string.IsNullOrWhiteSpace(labelsField))
            return labels;

        // Parse multiline format where each line is "key=value" or "key: value"
        var lines = labelsField.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                continue;

            // Try to split by '=' first, then ':'
            var separatorIndex = trimmedLine.IndexOf('=');
            if (separatorIndex < 0)
                separatorIndex = trimmedLine.IndexOf(':');
            
            if (separatorIndex > 0)
            {
                var key = trimmedLine.Substring(0, separatorIndex).Trim();
                var value = trimmedLine.Substring(separatorIndex + 1).Trim();
                
                if (!string.IsNullOrEmpty(key))
                {
                    labels[key] = value;
                }
            }
        }
        
        return labels;
    }
}

internal static class FieldNameConfig
{
    // These can be overridden via environment variables
    // SYNC__FIELD__NAMESPACES, SYNC__FIELD__SECRETNAME, SYNC__FIELD__SECRETKEYPASSWORD, SYNC__FIELD__SECRETKEYUSERNAME, SYNC__FIELD__IGNOREFIELD
    // SYNC__FIELD__SECRETANNOTATIONS, SYNC__FIELD__SECRETLABELS
    public static readonly string NamespacesFieldName =
        Environment.GetEnvironmentVariable("SYNC__FIELD__NAMESPACES")?.Trim() ?? "namespaces";
    public static readonly string SecretNameFieldName =
        Environment.GetEnvironmentVariable("SYNC__FIELD__SECRETNAME")?.Trim() ?? "secret-name";
    public static readonly string SecretKeyPasswordFieldName =
        Environment.GetEnvironmentVariable("SYNC__FIELD__SECRETKEYPASSWORD")?.Trim() ?? "secret-key-password";
    public static readonly string SecretKeyUsernameFieldName =
        Environment.GetEnvironmentVariable("SYNC__FIELD__SECRETKEYUSERNAME")?.Trim() ?? "secret-key-username";
    public static readonly string IgnoreFieldName =
        Environment.GetEnvironmentVariable("SYNC__FIELD__IGNOREFIELD")?.Trim() ?? "ignore-field";
    public static readonly string SecretAnnotationsFieldName =
        Environment.GetEnvironmentVariable("SYNC__FIELD__SECRETANNOTATIONS")?.Trim() ?? "secret-annotations";
    public static readonly string SecretLabelsFieldName =
        Environment.GetEnvironmentVariable("SYNC__FIELD__SECRETLABELS")?.Trim() ?? "secret-labels";
}

public class LoginInfo
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("totp")]
    public string? Totp { get; set; }

    [JsonPropertyName("uris")]
    public List<UriInfo>? Uris { get; set; }
}

public class UriInfo
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("match")]
    public int? Match { get; set; }
}

public class SecureNoteInfo
{
    [JsonPropertyName("type")]
    public int Type { get; set; }
}

public class CardInfo
{
    [JsonPropertyName("cardholderName")]
    public string CardholderName { get; set; } = string.Empty;

    [JsonPropertyName("brand")]
    public string Brand { get; set; } = string.Empty;

    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("expMonth")]
    public string ExpMonth { get; set; } = string.Empty;

    [JsonPropertyName("expYear")]
    public string ExpYear { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
}

public class IdentityInfo
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("middleName")]
    public string MiddleName { get; set; } = string.Empty;

    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("address1")]
    public string Address1 { get; set; } = string.Empty;

    [JsonPropertyName("address2")]
    public string Address2 { get; set; } = string.Empty;

    [JsonPropertyName("address3")]
    public string Address3 { get; set; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("postalCode")]
    public string PostalCode { get; set; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("company")]
    public string Company { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("ssn")]
    public string Ssn { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("passportNumber")]
    public string PassportNumber { get; set; } = string.Empty;

    [JsonPropertyName("licenseNumber")]
    public string LicenseNumber { get; set; } = string.Empty;
}

public class SshKeyInfo
{
    [JsonPropertyName("privateKey")]
    public string? PrivateKey { get; set; }

    [JsonPropertyName("publicKey")]
    public string? PublicKey { get; set; }

    [JsonPropertyName("keyFingerprint")]
    public string? Fingerprint { get; set; }
}

public class FieldInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public int Type { get; set; }
}

public class AttachmentInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sizeName")]
    public string SizeName { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
} 