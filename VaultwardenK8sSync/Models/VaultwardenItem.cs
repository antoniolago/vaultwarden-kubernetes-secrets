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

    /// <summary>
    /// Extract additional secret key/value entries defined in the item's notes.
    /// Supported syntaxes:
    /// - Inline key/value: lines starting with "#kv:" followed by key=value
    /// - Fenced blocks for multiline values:
    ///     ```secret:your_key
    ///     multi\nline\nvalue
    ///     ```
    /// Keys will be sanitized by the caller; this method returns raw keys/values.
    /// </summary>
    public Dictionary<string, string> ExtractSecretDataFromNotes()
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(Notes))
        {
            return results;
        }

        var text = Notes.Replace("\r\n", "\n").Replace("\r", "\n");

        // Parse fenced code blocks: ```secret:<key> ... ```
        var lines = text.Split('\n');
        bool inBlock = false;
        string currentKey = string.Empty;
        var blockBuffer = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine;
            if (!inBlock)
            {
                // Detect start of secret block
                if (line.StartsWith("```secret:", StringComparison.OrdinalIgnoreCase))
                {
                    var key = line.Substring("```secret:".Length).Trim();
                    if (!string.IsNullOrEmpty(key))
                    {
                        inBlock = true;
                        currentKey = key;
                        blockBuffer.Clear();
                        continue;
                    }
                }

                // Parse inline kv entries: #kv:key=value
                var trimmed = line.Trim();
                if (trimmed.StartsWith("#kv:", StringComparison.OrdinalIgnoreCase))
                {
                    var kv = trimmed.Substring("#kv:".Length);
                    var idx = kv.IndexOf('=');
                    if (idx > 0)
                    {
                        var k = kv.Substring(0, idx).Trim();
                        var v = kv.Substring(idx + 1);
                        if (!string.IsNullOrEmpty(k))
                        {
                            results[k] = v;
                        }
                    }
                }
            }
            else
            {
                // We are inside a fenced block
                if (line.StartsWith("```"))
                {
                    // End of block
                    var value = string.Join("\n", blockBuffer);
                    if (!string.IsNullOrEmpty(currentKey))
                    {
                        results[currentKey] = value;
                    }
                    inBlock = false;
                    currentKey = string.Empty;
                    blockBuffer.Clear();
                }
                else
                {
                    blockBuffer.Add(line);
                }
            }
        }

        // If notes ended while still in a block, flush it
        if (inBlock && !string.IsNullOrEmpty(currentKey))
        {
            results[currentKey] = string.Join("\n", blockBuffer);
        }

        return results;
    }

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
        // Prefer custom field if provided
        var fromField = GetCustomFieldValue(FieldNameConfig.NamespacesFieldName, "namespaces");
        if (!string.IsNullOrWhiteSpace(fromField))
        {
            return fromField.Trim();
        }

        if (string.IsNullOrEmpty(Notes))
            return null;

        var lines = Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("#namespaces:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmedLine.Substring("#namespaces:".Length).Trim();
            }
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

        if (string.IsNullOrEmpty(Notes))
            return namespaces;

        var lines = Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("#namespaces:", StringComparison.OrdinalIgnoreCase))
            {
                var namespaceValue = trimmedLine.Substring("#namespaces:".Length).Trim();
                if (!string.IsNullOrEmpty(namespaceValue))
                {
                    // Split by comma and add each namespace
                    var namespaceList = namespaceValue.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var ns in namespaceList)
                    {
                        var cleanNs = ns.Trim();
                        if (!string.IsNullOrEmpty(cleanNs) && !namespaces.Contains(cleanNs))
                        {
                            namespaces.Add(cleanNs);
                        }
                    }
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

        if (string.IsNullOrEmpty(Notes))
            return null;

        var lines = Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("#secret-name:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmedLine.Substring("#secret-name:".Length).Trim();
            }
        }

        return null;
    }

    public string? ExtractSecretKey()
    {
        // Prefer custom field if provided (legacy key name support)
        var fromField = GetCustomFieldValue(FieldNameConfig.LegacySecretKeyFieldName, "secret-key");
        if (!string.IsNullOrWhiteSpace(fromField))
            return fromField.Trim();

        if (string.IsNullOrEmpty(Notes))
            return null;

        var lines = Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("#secret-key:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmedLine.Substring("#secret-key:".Length).Trim();
            }
        }

        return null;
    }

    public string? ExtractSecretKeyPassword()
    {
        // Prefer custom field if provided
        var fromField = GetCustomFieldValue(FieldNameConfig.SecretKeyPasswordFieldName, "secret-key-password");
        if (!string.IsNullOrWhiteSpace(fromField))
            return fromField.Trim();

        if (string.IsNullOrEmpty(Notes))
            return null;

        var lines = Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("#secret-key-password:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmedLine.Substring("#secret-key-password:".Length).Trim();
            }
        }
        return null;
    }

    public string? ExtractSecretKeyUsername()
    {
        // Prefer custom field if provided
        var fromField = GetCustomFieldValue(FieldNameConfig.SecretKeyUsernameFieldName, "secret-key-username");
        if (!string.IsNullOrWhiteSpace(fromField))
            return fromField.Trim();

        if (string.IsNullOrEmpty(Notes))
            return null;

        var lines = Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("#secret-key-username:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmedLine.Substring("#secret-key-username:".Length).Trim();
            }
        }
        return null;
    }
}

internal static class FieldNameConfig
{
    // These can be overridden via environment variables
    // SYNC__FIELD__NAMESPACES, SYNC__FIELD__SECRETNAME, SYNC__FIELD__SECRETKEYPASSWORD, SYNC__FIELD__SECRETKEYUSERNAME, SYNC__FIELD__SECRETKEY
    public static readonly string NamespacesFieldName =
        Environment.GetEnvironmentVariable("SYNC__FIELD__NAMESPACES")?.Trim() ?? "namespaces";
    public static readonly string SecretNameFieldName =
        Environment.GetEnvironmentVariable("SYNC__FIELD__SECRETNAME")?.Trim() ?? "secret-name";
    public static readonly string SecretKeyPasswordFieldName =
        Environment.GetEnvironmentVariable("SYNC__FIELD__SECRETKEYPASSWORD")?.Trim() ?? "secret-key-password";
    public static readonly string SecretKeyUsernameFieldName =
        Environment.GetEnvironmentVariable("SYNC__FIELD__SECRETKEYUSERNAME")?.Trim() ?? "secret-key-username";
    public static readonly string LegacySecretKeyFieldName =
        Environment.GetEnvironmentVariable("SYNC__FIELD__SECRETKEY")?.Trim() ?? "secret-key";
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