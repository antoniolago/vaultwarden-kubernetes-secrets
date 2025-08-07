using System.Text.Json.Serialization;

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

    public string? ExtractNamespace()
    {
        if (string.IsNullOrEmpty(Notes))
            return null;

        var lines = Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("#namespace:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmedLine.Substring("#namespace:".Length).Trim();
            }
        }

        return null;
    }

    public List<string> ExtractNamespaces()
    {
        var namespaces = new List<string>();
        
        if (string.IsNullOrEmpty(Notes))
            return namespaces;

        var lines = Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("#namespace:", StringComparison.OrdinalIgnoreCase))
            {
                var namespaceValue = trimmedLine.Substring("#namespace:".Length).Trim();
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