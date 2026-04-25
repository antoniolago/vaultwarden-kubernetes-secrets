using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Configuration;

namespace VaultwardenK8sSync.Services;

/// <summary>
/// Vaultwarden service using direct API calls instead of bw CLI.
/// Handles authentication, vault sync, and item decryption directly.
/// </summary>
public class VaultwardenService : IVaultwardenService
{
    private readonly ILogger<VaultwardenService> _logger;
    private readonly VaultwardenSettings _config;
    private readonly HttpClient _httpClient;
    private bool _isAuthenticated = false;
    private string? _accessToken;
    private byte[]? _encryptionKey;
    private byte[]? _macKey;
    private readonly Dictionary<string, (byte[] encKey, byte[] macKey)> _orgKeys = new();
    private int _consecutiveEmptyResults = 0;
    private DateTime _lastSuccessfulFetch = DateTime.MinValue;

    public VaultwardenService(
        ILogger<VaultwardenService> logger,
        VaultwardenSettings config,
        IHttpClientFactory? httpClientFactory = null)
    {
        _logger = logger;
        _config = config;
        
        // Create HttpClient with SSL certificate validation disabled for self-signed certs
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        };
        _httpClient = httpClientFactory?.CreateClient("Vaultwarden") ?? new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<bool> AuthenticateAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_config.ServerUrl))
            {
                _logger.LogError("ServerUrl is not configured");
                return false;
            }

            if (!IsValidServerUrl(_config.ServerUrl))
            {
                _logger.LogError("Invalid ServerUrl format: {ServerUrl}", _config.ServerUrl);
                return false;
            }

            // Step 1: Login with API key to get access token
            var loginSuccess = await LoginWithApiKeyAsync();
            if (!loginSuccess)
            {
                _logger.LogError("API key login failed");
                return false;
            }

            // Step 2: Get encryption keys by "unlocking" with master password
            var unlockSuccess = await UnlockVaultAsync();
            if (!unlockSuccess)
            {
                _logger.LogError("Vault unlock (key derivation) failed");
                return false;
            }

            _isAuthenticated = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed: {Message}", ex.Message);
            return false;
        }
    }

    private async Task<bool> LoginWithApiKeyAsync()
    {
        if (string.IsNullOrEmpty(_config.ClientId) || string.IsNullOrEmpty(_config.ClientSecret))
        {
            _logger.LogError("ClientId and ClientSecret are required");
            return false;
        }

        try
        {
            var tokenUrl = $"{_config.ServerUrl}/identity/connect/token";
            var isOrgApiKey = _config.ClientId.StartsWith("organization.", StringComparison.OrdinalIgnoreCase);
            
            var formParams = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["scope"] = isOrgApiKey ? "api.organization" : "api",
                ["client_id"] = _config.ClientId,
                ["client_secret"] = _config.ClientSecret
            };
            
            // User API keys need device info, organization API keys don't
            if (!isOrgApiKey)
            {
                var deviceId = DeviceIdGenerator.GetOrGenerateDeviceId(_config);
                _logger.LogDebug("Using device identifier: {DeviceIdPrefix}...", deviceId[..8]);

                formParams["deviceType"] = "6";
                formParams["deviceIdentifier"] = deviceId;
                formParams["deviceName"] = "vaultwarden-k8s-sync";
            }
            
            var content = new FormUrlEncodedContent(formParams);

            var response = await _httpClient.PostAsync(tokenUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("API key login failed: {StatusCode} - {Response}", 
                    response.StatusCode, responseBody);
                return false;
            }

            var tokenResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
            _accessToken = tokenResponse.GetProperty("access_token").GetString();

            if (string.IsNullOrEmpty(_accessToken))
            {
                _logger.LogError("No access token in response");
                return false;
            }

            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _accessToken);

            _logger.LogInformation("API key login successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API key login exception: {Message}", ex.Message);
            return false;
        }
    }

    private async Task<bool> UnlockVaultAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.MasterPassword))
        {
            _logger.LogError("Master password is not configured");
            return false;
        }

        try
        {
            // Get user profile to get the encrypted symmetric key
            var profileUrl = $"{_config.ServerUrl}/api/accounts/profile";
            var response = await _httpClient.GetAsync(profileUrl);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get profile: {StatusCode} - {Response}", 
                    response.StatusCode, responseBody);
                return false;
            }

            var profile = JsonSerializer.Deserialize<JsonElement>(responseBody);
            
            // Get email and encrypted key - handle different casing
            string? email = null;
            string? encryptedKey = null;
            int kdfIterations = 600000;

            if (profile.TryGetProperty("email", out var emailProp))
                email = emailProp.GetString();
            else if (profile.TryGetProperty("Email", out emailProp))
                email = emailProp.GetString();

            if (profile.TryGetProperty("key", out var keyProp))
                encryptedKey = keyProp.GetString();
            else if (profile.TryGetProperty("Key", out keyProp))
                encryptedKey = keyProp.GetString();

            if (profile.TryGetProperty("kdfIterations", out var kdfProp))
                kdfIterations = kdfProp.GetInt32();
            else if (profile.TryGetProperty("KdfIterations", out kdfProp))
                kdfIterations = kdfProp.GetInt32();

            // Get KDF type (0 = PBKDF2, 1 = Argon2id)
            int kdfType = 0;
            if (profile.TryGetProperty("kdf", out var kdfTypeProp))
                kdfType = kdfTypeProp.GetInt32();
            else if (profile.TryGetProperty("Kdf", out kdfTypeProp))
                kdfType = kdfTypeProp.GetInt32();

            // Get Argon2 parameters if needed
            int? argonMemory = null, argonParallelism = null;
            if (kdfType == 1)
            {
                if (profile.TryGetProperty("kdfMemory", out var memProp) || profile.TryGetProperty("KdfMemory", out memProp))
                    argonMemory = memProp.GetInt32();
                if (profile.TryGetProperty("kdfParallelism", out var parProp) || profile.TryGetProperty("KdfParallelism", out parProp))
                    argonParallelism = parProp.GetInt32();
            }

            // Get encrypted private key for org key decryption
            string? encryptedPrivateKey = null;
            if (profile.TryGetProperty("privateKey", out var privKeyProp) || profile.TryGetProperty("PrivateKey", out privKeyProp))
                encryptedPrivateKey = privKeyProp.ValueKind != JsonValueKind.Null ? privKeyProp.GetString() : null;

            _logger.LogInformation("Profile KDF settings: type={KdfType}, iterations={Iterations}, hasPrivateKey={HasPrivKey}",
                kdfType, kdfIterations, !string.IsNullOrEmpty(encryptedPrivateKey));

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(encryptedKey))
            {
                _logger.LogError("Profile missing email or key. Response: {Response}", responseBody);
                return false;
            }

            _logger.LogDebug("KDF: type={KdfType}, iterations={Iterations}", kdfType, kdfIterations);

            // Derive master key from password
            byte[] masterKey;
            if (kdfType == 1)
            {
                // Argon2id
                if (argonMemory == null || argonParallelism == null)
                {
                    _logger.LogError("Argon2id KDF requires memory and parallelism parameters");
                    return false;
                }
                masterKey = DeriveKeyArgon2id(_config.MasterPassword, email.ToLowerInvariant(), kdfIterations, argonMemory.Value, argonParallelism.Value);
            }
            else
            {
                // PBKDF2-SHA256 - try without HKDF first (older accounts), then with HKDF (newer accounts)
                masterKey = DeriveKeyPbkdf2(_config.MasterPassword, email.ToLowerInvariant(), kdfIterations);
            }

            // Decrypt the symmetric key - try multiple approaches
            byte[]? symKey = null;
            string keyMethod = "none";
            
            if (kdfType == 0) // PBKDF2
            {
                // Try 1: Plain PBKDF2 without HKDF (legacy accounts, E2E tests)
                symKey = DecryptSymmetricKey(encryptedKey, masterKey);
                if (symKey != null && symKey.Length >= 64)
                {
                    keyMethod = "PBKDF2-plain";
                }
                else
                {
                    // Try 2: HKDF-stretched keys with MAC verification (modern accounts)
                    var stretchedEncKey = HkdfStretch(masterKey, "enc", 32);
                    var stretchedMacKey = HkdfStretch(masterKey, "mac", 32);
                    symKey = DecryptSymmetricKey(encryptedKey, stretchedEncKey, stretchedMacKey);
                    if (symKey != null && symKey.Length >= 64)
                    {
                        keyMethod = "PBKDF2-HKDF";
                    }
                    else
                    {
                        // Try 3: HKDF enc key only, no MAC verification
                        symKey = DecryptSymmetricKey(encryptedKey, stretchedEncKey);
                        if (symKey != null && symKey.Length >= 64)
                            keyMethod = "PBKDF2-HKDF-noMAC";
                    }
                }
            }
            else // Argon2id
            {
                // Argon2id already includes HKDF in DeriveKeyArgon2id, but need mac key too
                var macKey = HkdfStretch(masterKey, "mac", 32);
                symKey = DecryptSymmetricKey(encryptedKey, masterKey, macKey);
                if (symKey != null && symKey.Length >= 64)
                    keyMethod = "Argon2id-HKDF";
                else
                {
                    symKey = DecryptSymmetricKey(encryptedKey, masterKey);
                    if (symKey != null && symKey.Length >= 64)
                        keyMethod = "Argon2id-noMAC";
                }
            }
            
            _logger.LogInformation("Key derivation: KDF={KdfType}, iterations={Iterations}, method={Method}", 
                kdfType, kdfIterations, keyMethod);
            
            if (symKey == null || symKey.Length < 64)
            {
                _logger.LogError("Failed to decrypt symmetric key (got {Length} bytes)", symKey?.Length ?? 0);
                return false;
            }

            _logger.LogDebug("Symmetric key decrypted: {Length} bytes", symKey.Length);

            _encryptionKey = symKey[..32];
            _macKey = symKey[32..64];

            // Decrypt user's private key and organization keys from profile
            await DecryptOrgKeysFromProfile(profile, encryptedPrivateKey);

            _logger.LogInformation("Vault unlocked successfully (enc key first 4 hex: {EncHex}, org keys: {OrgCount})", 
                Convert.ToHexString(_encryptionKey[..4]), _orgKeys.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vault unlock exception: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<List<VaultwardenItem>> GetItemsAsync()
    {
        if (!_isAuthenticated)
        {
            _logger.LogWarning("Not authenticated - attempting authentication before fetching items...");
            var authSuccess = await AuthenticateAsync();
            if (!authSuccess)
            {
                throw new InvalidOperationException("Failed to authenticate with Vaultwarden");
            }
        }

        var items = await GetItemsInternalAsync();

        if (items.Count > 0)
        {
            _consecutiveEmptyResults = 0;
            _lastSuccessfulFetch = DateTime.UtcNow;
            return items;
        }

        _consecutiveEmptyResults++;
        _logger.LogWarning("No items retrieved (consecutive: {Count})", _consecutiveEmptyResults);

        var hadPreviousSuccess = _lastSuccessfulFetch != DateTime.MinValue;
        var timeSinceSuccess = DateTime.UtcNow - _lastSuccessfulFetch;
        var shouldReAuth = hadPreviousSuccess || _consecutiveEmptyResults >= 3 || timeSinceSuccess.TotalMinutes > 5;

        if (shouldReAuth)
        {
            _logger.LogWarning("Session may have expired. Attempting re-authentication...");
            _isAuthenticated = false;
            var reAuthSuccess = await AuthenticateAsync();

            if (reAuthSuccess)
            {
                _logger.LogInformation("Re-authentication successful, retrying item fetch...");
                _consecutiveEmptyResults = 0;
                items = await GetItemsInternalAsync();

                if (items.Count > 0)
                {
                    _lastSuccessfulFetch = DateTime.UtcNow;
                }
            }
            else
            {
                _logger.LogError("Re-authentication failed");
                if (hadPreviousSuccess)
                {
                    throw new InvalidOperationException("Session expired and re-authentication failed");
                }
            }
        }

        return items;
    }

    private async Task<List<VaultwardenItem>> GetItemsInternalAsync()
    {
        try
        {
            // First sync to get latest data
            await SyncVaultAsync();

            // Fetch all ciphers
            var ciphersUrl = $"{_config.ServerUrl}/api/ciphers";
            var response = await _httpClient.GetAsync(ciphersUrl);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to fetch ciphers: {StatusCode} - {Response}", 
                    response.StatusCode, responseBody);
                return new List<VaultwardenItem>();
            }

            var ciphersResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
            var items = new List<VaultwardenItem>();

            // Handle both array and object with "data" property
            JsonElement ciphersArray;
            if (ciphersResponse.ValueKind == JsonValueKind.Array)
            {
                ciphersArray = ciphersResponse;
            }
            else if (ciphersResponse.TryGetProperty("data", out var dataProp) || 
                     ciphersResponse.TryGetProperty("Data", out dataProp))
            {
                ciphersArray = dataProp;
            }
            else
            {
                _logger.LogWarning("Unexpected ciphers response format");
                return new List<VaultwardenItem>();
            }

            foreach (var cipher in ciphersArray.EnumerateArray())
            {
                try
                {
                    var item = ParseAndDecryptCipher(cipher);
                    if (item != null && item.DeletedDate == null)
                    {
                        items.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse cipher");
                }
            }

            // Apply filters
            var query = items.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(_config.OrganizationId))
            {
                query = query.Where(i => string.Equals(i.OrganizationId, _config.OrganizationId, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(_config.FolderId))
            {
                query = query.Where(i => string.Equals(i.FolderId, _config.FolderId, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(_config.CollectionId))
            {
                query = query.Where(i => i.CollectionIds != null && 
                    i.CollectionIds.Contains(_config.CollectionId, StringComparer.OrdinalIgnoreCase));
            }

            return query.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve items from Vaultwarden");
            return new List<VaultwardenItem>();
        }
    }

    private VaultwardenItem? ParseAndDecryptCipher(JsonElement cipher)
    {
        var item = new VaultwardenItem();

        // Get basic properties
        if (cipher.TryGetProperty("id", out var idProp) || cipher.TryGetProperty("Id", out idProp))
            item.Id = idProp.GetString() ?? "";

        if (cipher.TryGetProperty("organizationId", out var orgProp) || cipher.TryGetProperty("OrganizationId", out orgProp))
            item.OrganizationId = orgProp.ValueKind != JsonValueKind.Null ? orgProp.GetString() : null;

        if (cipher.TryGetProperty("folderId", out var folderProp) || cipher.TryGetProperty("FolderId", out folderProp))
            item.FolderId = folderProp.ValueKind != JsonValueKind.Null ? folderProp.GetString() : null;

        if (cipher.TryGetProperty("type", out var typeProp) || cipher.TryGetProperty("Type", out typeProp))
            item.Type = typeProp.GetInt32();

        if (cipher.TryGetProperty("deletedDate", out var deletedDateProp) || cipher.TryGetProperty("DeletedDate", out deletedDateProp))
            item.DeletedDate = deletedDateProp.ValueKind != JsonValueKind.Null ? deletedDateProp.GetDateTime() : null;

        // Decrypt name (use org key if item belongs to org)
        var orgId = item.OrganizationId;
        var hasOrgKey = !string.IsNullOrEmpty(orgId) && _orgKeys.ContainsKey(orgId);
        if (!string.IsNullOrEmpty(orgId) && !hasOrgKey)
            _logger.LogWarning("Item {Id}: orgId={OrgId} but NO matching org key!", item.Id, orgId);
        
        if (cipher.TryGetProperty("name", out var nameProp) || cipher.TryGetProperty("Name", out nameProp))
            item.Name = DecryptString(nameProp.GetString(), orgId) ?? string.Empty;

        // Decrypt notes
        if (cipher.TryGetProperty("notes", out var notesProp) || cipher.TryGetProperty("Notes", out notesProp))
            item.Notes = (notesProp.ValueKind != JsonValueKind.Null ? DecryptString(notesProp.GetString(), orgId) : null) ?? string.Empty;

        // Get collection IDs
        if (cipher.TryGetProperty("collectionIds", out var collProp) || cipher.TryGetProperty("CollectionIds", out collProp))
        {
            if (collProp.ValueKind == JsonValueKind.Array)
            {
                item.CollectionIds = collProp.EnumerateArray()
                    .Select(c => c.GetString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
        }

        // Parse login data (type 1)
        if (item.Type == 1)
        {
            if (cipher.TryGetProperty("login", out var loginProp) || cipher.TryGetProperty("Login", out loginProp))
            {
                item.Login = new LoginInfo();

                if (loginProp.TryGetProperty("username", out var userProp) || loginProp.TryGetProperty("Username", out userProp))
                    item.Login.Username = (userProp.ValueKind != JsonValueKind.Null ? DecryptString(userProp.GetString(), orgId) : null) ?? string.Empty;

                if (loginProp.TryGetProperty("password", out var passProp) || loginProp.TryGetProperty("Password", out passProp))
                    item.Login.Password = (passProp.ValueKind != JsonValueKind.Null ? DecryptString(passProp.GetString(), orgId) : null) ?? string.Empty;

                if (loginProp.TryGetProperty("totp", out var totpProp) || loginProp.TryGetProperty("Totp", out totpProp))
                    item.Login.Totp = totpProp.ValueKind != JsonValueKind.Null ? DecryptString(totpProp.GetString(), orgId) : null;

                // Parse URIs
                if (loginProp.TryGetProperty("uris", out var urisProp) || loginProp.TryGetProperty("Uris", out urisProp))
                {
                    if (urisProp.ValueKind == JsonValueKind.Array)
                    {
                        item.Login.Uris = new List<UriInfo>();
                        foreach (var uri in urisProp.EnumerateArray())
                        {
                            if (uri.TryGetProperty("uri", out var uriVal) || uri.TryGetProperty("Uri", out uriVal))
                            {
                                var decryptedUri = DecryptString(uriVal.GetString(), orgId);
                                if (!string.IsNullOrEmpty(decryptedUri))
                                {
                                    item.Login.Uris.Add(new UriInfo { Uri = decryptedUri });
                                }
                            }
                        }
                    }
                }
            }
        }

        // Parse SSH key data (type 5)
        if (item.Type == 5)
        {
            if (cipher.TryGetProperty("sshKey", out var sshKeyProp) || cipher.TryGetProperty("SshKey", out sshKeyProp))
            {
                item.SshKey = new SshKeyInfo();
                if (sshKeyProp.TryGetProperty("privateKey", out var privateKeyProp) || sshKeyProp.TryGetProperty("PrivateKey", out privateKeyProp))
                    item.SshKey.PrivateKey = (privateKeyProp.ValueKind != JsonValueKind.Null ? DecryptString(privateKeyProp.GetString(), orgId) : null) ?? string.Empty;
                if (sshKeyProp.TryGetProperty("publicKey", out var publicKeyProp) || sshKeyProp.TryGetProperty("PublicKey", out publicKeyProp))
                    item.SshKey.PublicKey = (publicKeyProp.ValueKind != JsonValueKind.Null ? DecryptString(publicKeyProp.GetString(), orgId) : null) ?? string.Empty;
                if (sshKeyProp.TryGetProperty("keyFingerprint", out var keyFingerprintProp) || sshKeyProp.TryGetProperty("KeyFingerprint", out keyFingerprintProp))
                    item.SshKey.Fingerprint = (keyFingerprintProp.ValueKind != JsonValueKind.Null ? DecryptString(keyFingerprintProp.GetString(), orgId) : null) ?? string.Empty;
            }
        }

        // Parse fields
        if (cipher.TryGetProperty("fields", out var fieldsProp) || cipher.TryGetProperty("Fields", out fieldsProp))
        {
            if (fieldsProp.ValueKind == JsonValueKind.Array)
            {
                var fieldCount = fieldsProp.GetArrayLength();
                _logger.LogDebug("Item {Name} has {FieldCount} fields", item.Name, fieldCount);
                item.Fields = new List<FieldInfo>();
                foreach (var field in fieldsProp.EnumerateArray())
                {
                    var vwField = new FieldInfo();

                    if (field.TryGetProperty("name", out var fnProp) || field.TryGetProperty("Name", out fnProp))
                        vwField.Name = (fnProp.ValueKind != JsonValueKind.Null ? DecryptString(fnProp.GetString(), orgId) : null) ?? string.Empty;

                    if (field.TryGetProperty("value", out var fvProp) || field.TryGetProperty("Value", out fvProp))
                        vwField.Value = (fvProp.ValueKind != JsonValueKind.Null ? DecryptString(fvProp.GetString(), orgId) : null) ?? string.Empty;

                    if (field.TryGetProperty("type", out var ftProp) || field.TryGetProperty("Type", out ftProp))
                        vwField.Type = ftProp.GetInt32();

                    _logger.LogDebug("Parsed field: name='{Name}', value='{Value}'", vwField.Name, vwField.Value?.Substring(0, Math.Min(20, vwField.Value?.Length ?? 0)));
                    item.Fields.Add(vwField);
                }
            }
        }

        return item;
    }

    private async Task<bool> SyncVaultAsync()
    {
        try
        {
            var syncUrl = $"{_config.ServerUrl}/api/sync";
            var response = await _httpClient.GetAsync(syncUrl);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Sync failed: {StatusCode} - {Error}", response.StatusCode, error);
                return false;
            }

            _logger.LogDebug("Vault synced successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sync failed with exception");
            return false;
        }
    }

    public async Task<VaultwardenItem?> GetItemAsync(string id)
    {
        if (!_isAuthenticated)
        {
            throw new InvalidOperationException("Not authenticated. Call AuthenticateAsync first.");
        }

        try
        {
            var url = $"{_config.ServerUrl}/api/ciphers/{id}";
            var response = await _httpClient.GetAsync(url);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get item {Id}: {StatusCode}", id, response.StatusCode);
                return null;
            }

            var cipher = JsonSerializer.Deserialize<JsonElement>(responseBody);
            return ParseAndDecryptCipher(cipher);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get item {Id}", id);
            return null;
        }
    }

    public async Task<string> GetItemPasswordAsync(string id)
    {
        var item = await GetItemAsync(id);
        return item?.Login?.Password ?? string.Empty;
    }

    public Task<bool> IsAuthenticatedAsync()
    {
        return Task.FromResult(_isAuthenticated);
    }

    public Task LogoutAsync()
    {
        _isAuthenticated = false;
        _accessToken = null;
        _encryptionKey = null;
        _macKey = null;
        _httpClient.DefaultRequestHeaders.Authorization = null;
        return Task.CompletedTask;
    }

    public async Task<Dictionary<string, string>> GetOrganizationsMapAsync()
    {
        var orgMap = new Dictionary<string, string>();

        try
        {
            var url = $"{_config.ServerUrl}/api/sync";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                return orgMap;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            var syncData = JsonSerializer.Deserialize<JsonElement>(responseBody);

            if (syncData.TryGetProperty("profile", out var profile) || syncData.TryGetProperty("Profile", out profile))
            {
                if (profile.TryGetProperty("organizations", out var orgs) || profile.TryGetProperty("Organizations", out orgs))
                {
                    foreach (var org in orgs.EnumerateArray())
                    {
                        string? id = null, name = null;
                        if (org.TryGetProperty("id", out var idProp) || org.TryGetProperty("Id", out idProp))
                            id = idProp.GetString();
                        if (org.TryGetProperty("name", out var nameProp) || org.TryGetProperty("Name", out nameProp))
                            name = nameProp.GetString();

                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        {
                            orgMap[id] = name;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching organizations map");
        }

        return orgMap;
    }

    public async Task<string?> GetCurrentUserEmailAsync()
    {
        try
        {
            var url = $"{_config.ServerUrl}/api/accounts/profile";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                var profile = JsonSerializer.Deserialize<JsonElement>(body);

                if (profile.TryGetProperty("email", out var emailProp) || profile.TryGetProperty("Email", out emailProp))
                {
                    return emailProp.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching current user email");
        }

        return null;
    }

    #region Cryptography

    private static byte[] DeriveKeyPbkdf2(string password, string salt, int iterations)
    {
        // PBKDF2-SHA256 to get master key (no HKDF - for older accounts)
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            Encoding.UTF8.GetBytes(salt),
            iterations,
            HashAlgorithmName.SHA256,
            32);
    }

    private static byte[] DeriveKeyArgon2id(string password, string salt, int iterations, int memoryKb, int parallelism)
    {
        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = Encoding.UTF8.GetBytes(salt),
            DegreeOfParallelism = parallelism,
            MemorySize = memoryKb * 1024, // Convert KB to bytes
            Iterations = iterations
        };
        var masterKey = argon2.GetBytes(32);
        
        // Step 2: HKDF-Expand to get stretched key
        var stretchedKey = HkdfStretch(masterKey, "enc", 32);
        return stretchedKey;
    }

    private static byte[] HkdfStretch(byte[] key, string info, int outputLength)
    {
        // HKDF-Expand (RFC 5869) - Bitwarden uses Expand directly on master key
        // .NET's HKDF.Expand expects a PRK, but we can use DeriveKey which handles this
        var infoBytes = Encoding.UTF8.GetBytes(info);
        
        // Manual HKDF-Expand implementation to match Bitwarden exactly
        using var hmac = new HMACSHA256(key);
        var hashLen = 32; // SHA256 output
        var n = (int)Math.Ceiling((double)outputLength / hashLen);
        var output = new byte[outputLength];
        var previousBlock = Array.Empty<byte>();
        
        for (int i = 1; i <= n; i++)
        {
            var input = new byte[previousBlock.Length + infoBytes.Length + 1];
            previousBlock.CopyTo(input, 0);
            infoBytes.CopyTo(input, previousBlock.Length);
            input[^1] = (byte)i;
            
            previousBlock = hmac.ComputeHash(input);
            var copyLen = Math.Min(hashLen, outputLength - (i - 1) * hashLen);
            Array.Copy(previousBlock, 0, output, (i - 1) * hashLen, copyLen);
        }
        
        return output;
    }

    private Task DecryptOrgKeysFromProfile(JsonElement profile, string? encryptedPrivateKey)
    {
        _orgKeys.Clear();
        
        if (!profile.TryGetProperty("organizations", out var orgs) && 
            !profile.TryGetProperty("Organizations", out orgs))
        {
            return Task.CompletedTask;
        }

        // Decrypt user's RSA private key first (needed for org key decryption)
        RSA? rsaPrivateKey = null;
        if (!string.IsNullOrEmpty(encryptedPrivateKey))
        {
            var privateKeyBytes = DecryptToBytes(encryptedPrivateKey);
            if (privateKeyBytes != null)
            {
                try
                {
                    rsaPrivateKey = RSA.Create();
                    rsaPrivateKey.ImportPkcs8PrivateKey(privateKeyBytes, out _);
                    _logger.LogDebug("Decrypted user's RSA private key");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Failed to import RSA private key: {Message}", ex.Message);
                    rsaPrivateKey = null;
                }
            }
        }

        foreach (var org in orgs.EnumerateArray())
        {
            string? orgId = null, encryptedKey = null;
            
            if (org.TryGetProperty("id", out var idProp) || org.TryGetProperty("Id", out idProp))
                orgId = idProp.GetString();
            if (org.TryGetProperty("key", out var keyProp) || org.TryGetProperty("Key", out keyProp))
                encryptedKey = keyProp.ValueKind != JsonValueKind.Null ? keyProp.GetString() : null;

            if (string.IsNullOrEmpty(orgId) || string.IsNullOrEmpty(encryptedKey))
                continue;

            // Org keys are RSA-encrypted (type 4) or AES-encrypted (type 2)
            byte[]? decryptedOrgKey = null;
            
            if (encryptedKey.StartsWith("4.") && rsaPrivateKey != null)
            {
                // RSA-OAEP encrypted org key
                decryptedOrgKey = DecryptRsaOaep(encryptedKey, rsaPrivateKey);
            }
            else if (encryptedKey.StartsWith("2."))
            {
                // AES encrypted (fallback)
                decryptedOrgKey = DecryptToBytes(encryptedKey);
            }

            if (decryptedOrgKey != null && decryptedOrgKey.Length >= 64)
            {
                _orgKeys[orgId] = (decryptedOrgKey[..32], decryptedOrgKey[32..64]);
                _logger.LogInformation("Decrypted org key for {OrgId}: {Len} bytes, first4hex={First4}", 
                    orgId, decryptedOrgKey.Length, Convert.ToHexString(decryptedOrgKey[..4]));
            }
            else
            {
                _logger.LogWarning("Failed to decrypt org key for {OrgId} (type: {Type}, gotLen: {Len})", 
                    orgId, encryptedKey.Length > 2 ? encryptedKey[..2] : "?", decryptedOrgKey?.Length ?? 0);
            }
        }

        rsaPrivateKey?.Dispose();
        return Task.CompletedTask;
    }

    private byte[]? DecryptRsaOaep(string encrypted, RSA rsaKey)
    {
        try
        {
            // Format: 4.{base64-encrypted-data}
            if (!encrypted.StartsWith("4."))
                return null;

            var encryptedData = Convert.FromBase64String(encrypted[2..]);
            return rsaKey.Decrypt(encryptedData, RSAEncryptionPadding.OaepSHA1);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("RSA decryption failed: {Message}", ex.Message);
            return null;
        }
    }

    private byte[]? DecryptToBytes(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted) || _encryptionKey == null)
            return null;

        try
        {
            if (!encrypted.StartsWith("2."))
                return null;

            var parts = encrypted[2..].Split('|');
            if (parts.Length < 2)
                return null;

            var iv = Convert.FromBase64String(parts[0]);
            var data = Convert.FromBase64String(parts[1]);

            using var aes = Aes.Create();
            aes.Key = _encryptionKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(data, 0, data.Length);
        }
        catch
        {
            return null;
        }
    }

    private byte[]? DecryptSymmetricKey(string encryptedKey, byte[] encKey, byte[]? macKey = null)
    {
        try
        {
            // Format: 2.{iv}|{data}|{mac} (AES-256-CBC with optional HMAC)
            var parts = encryptedKey.Split('.');
            if (parts.Length != 2 || parts[0] != "2")
            {
                _logger.LogError("Invalid key format: expected '2.xxx', got prefix '{Prefix}'", parts.Length > 0 ? parts[0] : "empty");
                return null;
            }
            
            var dataParts = parts[1].Split('|');
            
            if (dataParts.Length < 2)
            {
                _logger.LogError("Invalid key data format: expected at least 2 parts separated by |, got {Count}", dataParts.Length);
                return null;
            }
            
            var iv = Convert.FromBase64String(dataParts[0]);
            var data = Convert.FromBase64String(dataParts[1]);
            byte[]? mac = dataParts.Length > 2 ? Convert.FromBase64String(dataParts[2]) : null;

            // Verify MAC if present and macKey provided
            if (mac != null && macKey != null)
            {
                using var hmac = new HMACSHA256(macKey);
                var macData = new byte[iv.Length + data.Length];
                iv.CopyTo(macData, 0);
                data.CopyTo(macData, iv.Length);
                var computedMac = hmac.ComputeHash(macData);
                
                if (!computedMac.SequenceEqual(mac))
                {
                    _logger.LogDebug("MAC verification failed - wrong key");
                    return null;
                }
            }

            using var aes = Aes.Create();
            aes.Key = encKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("DecryptSymmetricKey failed: {Message}", ex.Message);
            return null;
        }
    }

    private string? DecryptString(string? encrypted, string? orgId = null)
    {
        // Get appropriate encryption key (org key if item belongs to org, otherwise user key)
        byte[]? encKey = _encryptionKey;
        bool usingOrgKey = false;
        if (!string.IsNullOrEmpty(orgId) && _orgKeys.TryGetValue(orgId, out var orgKeyPair))
        {
            encKey = orgKeyPair.encKey;
            usingOrgKey = true;
        }

        if (string.IsNullOrEmpty(encrypted) || encKey == null)
        {
            return null;
        }

        try
        {
            // Format: 2.{iv}|{data}|{mac}
            if (!encrypted.StartsWith("2."))
            {
                return encrypted; // Return as-is if not encrypted
            }

            var parts = encrypted[2..].Split('|');
            if (parts.Length < 2)
                return null;

            var iv = Convert.FromBase64String(parts[0]);
            var data = Convert.FromBase64String(parts[1]);

            using var aes = Aes.Create();
            aes.Key = encKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(data, 0, data.Length);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("DecryptString failed: {Error} (usingOrgKey={UsingOrgKey}, prefix: {Prefix})", 
                ex.Message, usingOrgKey, encrypted.Substring(0, Math.Min(20, encrypted.Length)));
            return null;
        }
    }

    #endregion

    /// <summary>
    /// Downloads the content of an attachment from Vaultwarden
    /// </summary>
    public async Task<byte[]?> DownloadAttachmentAsync(string attachmentUrl)
    {
        try
        {
            if (!_isAuthenticated || string.IsNullOrEmpty(_accessToken))
            {
                _logger.LogWarning("Cannot download attachment - not authenticated");
                return null;
            }

            var isAbsoluteUrl = Uri.TryCreate(attachmentUrl, UriKind.Absolute, out var uriResult) 
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
            
            var fullUrl = isAbsoluteUrl 
                ? attachmentUrl 
                : $"{_config.ServerUrl}{attachmentUrl}";

            using var response = await _httpClient.GetAsync(fullUrl);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to download attachment: {StatusCode}", response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception downloading attachment: {Url}", attachmentUrl);
            return null;
        }
    }

    private static bool IsValidServerUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != "https")
            return false;

        var dangerousChars = new[] { ";", "`", "$", "&", "|", "\n", "\r", "'", "\"", "<", ">", "(", ")" };
        if (dangerousChars.Any(url.Contains))
            return false;

        return true;
    }
}
