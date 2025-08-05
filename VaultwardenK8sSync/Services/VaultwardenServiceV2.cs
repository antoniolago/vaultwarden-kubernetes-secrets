using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Models;
using VwConnector;
using VwConnector.Agent;

namespace VaultwardenK8sSync.Services;

public class VaultwardenServiceV2 : IVaultwardenServiceV2
{
    private readonly ILogger<VaultwardenServiceV2> _logger;
    private readonly VaultwardenConfig _config;
    private VaultwardenAgent? _agent;
    private bool _isAuthenticated = false;

    public VaultwardenServiceV2(ILogger<VaultwardenServiceV2> logger, VaultwardenConfig config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task<bool> AuthenticateAsync()
    {
        try
        {
            _logger.LogInformation("Authenticating with Vaultwarden using VwConnector...");

            if (string.IsNullOrEmpty(_config.ServerUrl) || string.IsNullOrEmpty(_config.Email) || string.IsNullOrEmpty(_config.MasterPassword))
            {
                _logger.LogError("Missing required configuration: ServerUrl, Email, or MasterPassword");
                return false;
            }

            var service = new Uri(_config.ServerUrl);
            var credentials = new VaultwardenCredentials(_config.Email, _config.MasterPassword);

            _agent = await VaultwardenAgent.CreateAsync(service, credentials);
            _isAuthenticated = true;

            _logger.LogInformation("Successfully authenticated with Vaultwarden using VwConnector");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to authenticate with Vaultwarden using VwConnector");
            return false;
        }
    }

    public async Task<List<VaultwardenItemV2>> GetItemsAsync()
    {
        if (_agent == null || !_isAuthenticated)
        {
            throw new InvalidOperationException("Not authenticated. Call AuthenticateAsync first.");
        }

        try
        {
            _logger.LogInformation("Retrieving items from Vaultwarden using VwConnector...");

            var items = await _agent.GetItemsAsync();
            var vaultwardenItems = new List<VaultwardenItemV2>();

            foreach (var item in items)
            {
                var vaultwardenItem = MapToVaultwardenItem(item);
                vaultwardenItems.Add(vaultwardenItem);
            }

            _logger.LogInformation("Retrieved {Count} items from Vaultwarden using VwConnector", vaultwardenItems.Count);
            return vaultwardenItems;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve items from Vaultwarden using VwConnector");
            return new List<VaultwardenItemV2>();
        }
    }

    public async Task<VaultwardenItemV2?> GetItemAsync(string id)
    {
        if (_agent == null || !_isAuthenticated)
        {
            throw new InvalidOperationException("Not authenticated. Call AuthenticateAsync first.");
        }

        try
        {
            var items = await _agent.GetItemsAsync();
            var item = items.FirstOrDefault(i => i.Id == id);
            
            if (item != null)
            {
                return MapToVaultwardenItem(item);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve item {Id} using VwConnector", id);
            return null;
        }
    }

    public async Task<string> GetItemPasswordAsync(string id)
    {
        if (_agent == null || !_isAuthenticated)
        {
            throw new InvalidOperationException("Not authenticated. Call AuthenticateAsync first.");
        }

        try
        {
            var items = await _agent.GetItemsAsync();
            var item = items.FirstOrDefault(i => i.Id == id);
            
            if (item != null)
            {
                return item.Password ?? string.Empty;
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve password for item {Id} using VwConnector", id);
            return string.Empty;
        }
    }

    public Task<bool> IsAuthenticatedAsync()
    {
        return Task.FromResult(_isAuthenticated);
    }

    public Task LogoutAsync()
    {
        try
        {
            _agent?.Dispose();
            _agent = null;
            _isAuthenticated = false;
            _logger.LogInformation("Logged out from Vaultwarden using VwConnector");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to logout from Vaultwarden using VwConnector");
        }

        return Task.CompletedTask;
    }

    private VaultwardenItemV2 MapToVaultwardenItem(VwConnector.Models.VaultwardenItem item)
    {
        return new VaultwardenItemV2
        {
            Id = item.Id ?? string.Empty,
            Name = item.Name ?? string.Empty,
            Description = item.Description ?? string.Empty,
            Username = item.Username ?? string.Empty,
            Password = item.Password ?? string.Empty,
            Url = item.Url ?? string.Empty,
            Notes = item.Notes ?? string.Empty,
            FolderId = item.FolderId,
            Type = item.Type,
            Favorite = item.Favorite,
            Reprompt = item.Reprompt,
            Login = item.Login != null ? new LoginInfoV2
            {
                Username = item.Login.Username ?? string.Empty,
                Password = item.Login.Password ?? string.Empty,
                Totp = item.Login.Totp,
                Uris = item.Login.Uris?.Select(u => new UriInfoV2
                {
                    Uri = u.Uri ?? string.Empty,
                    Match = u.Match
                }).ToList()
            } : null,
            SecureNote = item.SecureNote != null ? new SecureNoteInfoV2
            {
                Type = item.SecureNote.Type
            } : null,
            Card = item.Card != null ? new CardInfoV2
            {
                CardholderName = item.Card.CardholderName ?? string.Empty,
                Brand = item.Card.Brand ?? string.Empty,
                Number = item.Card.Number ?? string.Empty,
                ExpMonth = item.Card.ExpMonth ?? string.Empty,
                ExpYear = item.Card.ExpYear ?? string.Empty,
                Code = item.Card.Code ?? string.Empty
            } : null,
            Identity = item.Identity != null ? new IdentityInfoV2
            {
                Title = item.Identity.Title ?? string.Empty,
                FirstName = item.Identity.FirstName ?? string.Empty,
                MiddleName = item.Identity.MiddleName ?? string.Empty,
                LastName = item.Identity.LastName ?? string.Empty,
                Address1 = item.Identity.Address1 ?? string.Empty,
                Address2 = item.Identity.Address2 ?? string.Empty,
                Address3 = item.Identity.Address3 ?? string.Empty,
                City = item.Identity.City ?? string.Empty,
                State = item.Identity.State ?? string.Empty,
                PostalCode = item.Identity.PostalCode ?? string.Empty,
                Country = item.Identity.Country ?? string.Empty,
                Company = item.Identity.Company ?? string.Empty,
                Email = item.Identity.Email ?? string.Empty,
                Phone = item.Identity.Phone ?? string.Empty,
                Ssn = item.Identity.Ssn ?? string.Empty,
                Username = item.Identity.Username ?? string.Empty,
                PassportNumber = item.Identity.PassportNumber ?? string.Empty,
                LicenseNumber = item.Identity.LicenseNumber ?? string.Empty
            } : null,
            Fields = item.Fields?.Select(f => new FieldInfoV2
            {
                Name = f.Name ?? string.Empty,
                Value = f.Value ?? string.Empty,
                Type = f.Type
            }).ToList(),
            Attachments = item.Attachments?.Select(a => new AttachmentInfoV2
            {
                Id = a.Id ?? string.Empty,
                FileName = a.FileName ?? string.Empty,
                Size = a.Size,
                SizeName = a.SizeName ?? string.Empty,
                Url = a.Url ?? string.Empty
            }).ToList(),
            OrganizationId = item.OrganizationId,
            CollectionIds = item.CollectionIds?.ToList(),
            RevisionDate = item.RevisionDate,
            CreationDate = item.CreationDate,
            DeletedDate = item.DeletedDate,
            Deleted = item.Deleted,
            OrgName = item.OrgName
        };
    }
} 