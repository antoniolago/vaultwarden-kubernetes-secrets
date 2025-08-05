using System.Text.Json.Serialization;

namespace VaultwardenK8sSync.Models;

public class VaultwardenItemV2
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

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
    public LoginInfoV2? Login { get; set; }

    [JsonPropertyName("secureNote")]
    public SecureNoteInfoV2? SecureNote { get; set; }

    [JsonPropertyName("card")]
    public CardInfoV2? Card { get; set; }

    [JsonPropertyName("identity")]
    public IdentityInfoV2? Identity { get; set; }

    [JsonPropertyName("fields")]
    public List<FieldInfoV2>? Fields { get; set; }

    [JsonPropertyName("attachments")]
    public List<AttachmentInfoV2>? Attachments { get; set; }

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

    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }

    [JsonPropertyName("orgName")]
    public string? OrgName { get; set; }

    public string? ExtractNamespace()
    {
        if (string.IsNullOrEmpty(Description))
            return null;

        var lines = Description.Split('\n', StringSplitOptions.RemoveEmptyEntries);
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
}

public class LoginInfoV2
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("totp")]
    public string? Totp { get; set; }

    [JsonPropertyName("uris")]
    public List<UriInfoV2>? Uris { get; set; }
}

public class UriInfoV2
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("match")]
    public int? Match { get; set; }
}

public class SecureNoteInfoV2
{
    [JsonPropertyName("type")]
    public int Type { get; set; }
}

public class CardInfoV2
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

public class IdentityInfoV2
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

public class FieldInfoV2
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public int Type { get; set; }
}

public class AttachmentInfoV2
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