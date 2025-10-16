using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Infrastructure;
using VaultwardenK8sSync.Configuration;

namespace VaultwardenK8sSync.Services;

public class VaultwardenService : IVaultwardenService
{
    private readonly ILogger<VaultwardenService> _logger;
    private readonly VaultwardenSettings _config;
    private readonly IProcessFactory _processFactory;
    private readonly IProcessRunner _processRunner;
    private bool _isAuthenticated = false;
    private string? _sessionToken;

    public VaultwardenService(
        ILogger<VaultwardenService> logger,
        VaultwardenSettings config,
        IProcessFactory processFactory,
        IProcessRunner processRunner)
    {
        _logger = logger;
        _config = config;
        _processFactory = processFactory;
        _processRunner = processRunner;
    }

    public async Task<bool> AuthenticateAsync()
    {
        try
        {
            _logger.LogDebug("Authenticating with Vaultwarden (API key)...");
            await LogBwVersionAsync();
            await LogBwStatusAsync("pre-login");

            // Logout first to ensure clean state
            await LogoutAsync();

            // Give the CLI time to write state changes to disk
            await Task.Delay(Constants.Delays.PostCommandDelayMs);

            // Set the server URL first
            if (!string.IsNullOrEmpty(_config.ServerUrl))
            {
                var setServerResult = await SetServerUrlAsync();
                if (!setServerResult)
                {
                    _logger.LogError("Failed to set server URL: {ServerUrl}", _config.ServerUrl);
                    return false;
                }

                // Give the CLI time to write server config to disk
                await Task.Delay(Constants.Delays.PostCommandDelayMs);
                await LogBwStatusAsync("post-server-config");
            }

            var ok = await AuthenticateWithApiKeyAsync();
            await LogBwStatusAsync("post-login");
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to authenticate with Vaultwarden");
            return false;
        }
    }

    private async Task<bool> SetServerUrlAsync()
    {
        try
        {
            _logger.LogDebug("Configuring server: {ServerUrl}", _config.ServerUrl);

            var process = _processFactory.CreateBwProcess($"config server {_config.ServerUrl}");
            ApplyCommonEnv(process.StartInfo);

            var result = await _processRunner.RunAsync(process, Constants.Timeouts.DefaultCommandTimeoutSeconds);

            if (result.Success)
            {
                _logger.LogDebug("Server URL set");
                return true;
            }

            _logger.LogError("Failed to set server URL: {Error}", result.Error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set server URL");
            return false;
        }
    }

    private async Task<bool> AuthenticateWithApiKeyAsync()
    {
        if (string.IsNullOrEmpty(_config.ClientId) || string.IsNullOrEmpty(_config.ClientSecret))
        {
            _logger.LogError("ClientId and ClientSecret are required for API key authentication");
            return false;
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bw",
                Arguments = $"login --apikey --raw",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        ApplyCommonEnv(process.StartInfo);

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var exitTask = process.WaitForExitAsync();
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
        var completed = await Task.WhenAny(exitTask, timeoutTask);
        if (completed == timeoutTask)
        {
            _logger.LogError("bw login timed out after 60 seconds");
            try { process.Kill(); } catch { }
            return false;
        }

        await exitTask;
        var stdout = await outputTask;
        var stderr = await errorTask;

        if (process.ExitCode == 0)
        {
            // Log exactly what the login command returned
            _logger.LogDebug("bw login stdout: '{Stdout}'", stdout ?? "null");
            _logger.LogDebug("bw login stderr: '{Stderr}'", stderr ?? "null");

            _isAuthenticated = true;

            // Give the CLI time to write authentication state to disk
            await Task.Delay(Constants.Delays.PostUnlockDelayMs);
            await LogBwStatusAsync("post-api-login");

            // For API key authentication, always check vault status and unlock if needed
            _logger.LogDebug("API key login succeeded - checking vault status");
            return await TestVaultAccessAsync();
        }

        _logger.LogError("Authentication failed (exit {Code}). stderr: {Stderr} | stdout: {Stdout}", process.ExitCode, stderr, stdout);
        return false;
    }

    // Password login removed

    private async Task<bool> TestVaultAccessAsync()
    {
        try
        {
            _logger.LogDebug("Testing vault access without unlock...");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = $"status --raw{GetSessionArgs()}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo);

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(exitTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("bw status timed out after 30 seconds");
                try { process.Kill(); } catch { }
                return false;
            }

            await exitTask;
            var output = await outputTask;
            var error = await errorTask;

            _logger.LogDebug("Vault status: {Status}", output?.Trim());

            if (process.ExitCode == 0)
            {
                // Parse the JSON status to see if we need to unlock
                var statusText = output?.Trim();
                var vaultStatus = "unknown";

                try
                {
                    if (!string.IsNullOrEmpty(statusText))
                    {
                        var statusJson = System.Text.Json.JsonDocument.Parse(statusText);
                        if (statusJson.RootElement.TryGetProperty("status", out var statusProperty))
                        {
                            vaultStatus = statusProperty.GetString()?.ToLowerInvariant() ?? "unknown";
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse status JSON, treating as text: {Status}", statusText);
                    vaultStatus = statusText?.ToLowerInvariant() ?? "unknown";
                }

                if (vaultStatus == "unlocked")
                {
                    _logger.LogInformation("Vault is already unlocked - API key authentication complete");
                    return true;
                }
                else if (vaultStatus == "locked")
                {
                    _logger.LogInformation("Vault is locked - attempting unlock");
                    return await UnlockVaultAsync();
                }
                else
                {
                    _logger.LogWarning("Unknown vault status: {Status} - attempting unlock anyway", vaultStatus);
                    return await UnlockVaultAsync();
                }
            }
            else
            {
                _logger.LogWarning("Failed to get vault status (exit {Code}): {Error} - attempting unlock", process.ExitCode, error);
                return await UnlockVaultAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to test vault access - attempting unlock");
            return await UnlockVaultAsync();
        }
    }

    private async Task<bool> UnlockVaultAsync()
    {
        try
        {
            _logger.LogInformation("Unlocking vault...");

            // Verify we're in the correct state before attempting unlock
            await LogBwStatusAsync("pre-unlock");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = "unlock --raw",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo);

            // Log what session token we're using
            if (!string.IsNullOrWhiteSpace(_sessionToken))
            {
                _logger.LogInformation("Using session token for unlock (length: {Len})", _sessionToken.Length);
            }
            // else
            // {
            //     // _logger.LogWarning("No session token available for unlock - this may cause the 'You are not logged in' error");
            // }

            process.Start();
            try
            {
                if (string.IsNullOrWhiteSpace(_config.MasterPassword))
                {
                    _logger.LogError("Master password is not configured (VAULTWARDEN__MASTERPASSWORD)");
                }
                await process.StandardInput.WriteLineAsync(_config.MasterPassword);
                await process.StandardInput.FlushAsync();
                process.StandardInput.Close();
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Could not write master password to bw stdin: {Msg}", ex.Message);
            }

            // Read output/error concurrently with timeout
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
            var completedTask = await Task.WhenAny(exitTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                _logger.LogError("bw unlock timed out after 60 seconds");
                try { process.Kill(); } catch { }
                return false;
            }

            await exitTask;

            var stdOut = await outputTask;
            var stdErr = await errorTask;

            if (process.ExitCode == 0)
            {
                var token = (stdOut ?? string.Empty).Trim();
                var len = string.IsNullOrEmpty(token) ? 0 : token.Length;

                if (!string.IsNullOrEmpty(token))
                {
                    _sessionToken = token;
                    _logger.LogInformation("Vault unlocked successfully - session token captured (length: {Len})", len);
                }
                else
                {
                    _logger.LogInformation("Vault unlocked successfully - no session token in output, will use existing authentication");
                }

                await LogBwStatusAsync("post-unlock");
                return true;
            }

            _logger.LogError("Failed to unlock vault (exit {Code}). stderr: {Stderr}. stdout: {Stdout}", process.ExitCode, stdErr, stdOut);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unlock vault");
            return false;
        }
    }

    private async Task LogBwVersionAsync()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode == 0)
            {
                _logger.LogDebug("bw version: {Version}", output.Trim());
            }
            else
            {
                _logger.LogWarning("Could not determine bw version (exit {Code}): {Error}", process.ExitCode, error.Trim());
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get bw version");
        }
    }

    private async Task LogBwStatusAsync(string stage)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = $"status --raw{GetSessionArgs()}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo);

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogDebug("bw status at {Stage}: {Output}", stage, output.Trim());
            }
            else
            {
                _logger.LogDebug("bw status at {Stage} failed (exit {Code}): {Err}", stage, process.ExitCode, error.Trim());
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to get bw status at {Stage}", stage);
        }
    }

    public async Task<List<VaultwardenItem>> GetItemsAsync()
    {
        if (!_isAuthenticated)
        {
            throw new InvalidOperationException("Not authenticated. Call AuthenticateAsync first.");
        }

        try
        {
            _logger.LogDebug("=== GetItemsAsync START ===");
            _logger.LogDebug("Ensuring Vaultwarden vault is synced...");
            await SyncVaultAsync();
            _logger.LogDebug("Vault sync completed");

            _logger.LogDebug("Fetching items from Vaultwarden...");
            _logger.LogDebug("Session token for list items: {HasToken} (length: {Len})",
                !string.IsNullOrWhiteSpace(_sessionToken),
                string.IsNullOrWhiteSpace(_sessionToken) ? 0 : _sessionToken!.Length);



            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = $"list items --raw{GetSessionArgs()}{GetOrganizationArgs()}{GetFolderArgs()}{GetCollectionArgs()}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo);

            if (string.IsNullOrWhiteSpace(_sessionToken))
            {
                _logger.LogDebug("No session token available for 'bw list items'. This may cause prompts or failures.");
            }
            else
            {
                _logger.LogDebug("Using --session parameter for 'bw list items'");
            }

            _logger.LogDebug("Starting 'bw list items' process...");

            process.Start();
            _logger.LogDebug("Process started, waiting for completion...");
            // Read output and error streams asynchronously with timeout
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();

            // Wait for process to exit with timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(120)); // Increased timeout
            var completedTask = await Task.WhenAny(exitTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogError("bw list items timed out after 120 seconds");
                var gcMemoryAfter = GC.GetTotalMemory(false);
                _logger.LogError("Memory at timeout: {MemoryMB} MB", gcMemoryAfter / 1024 / 1024);
                try
                {
                    _logger.LogWarning("Attempting to kill hung bw process...");
                    process.Kill();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to kill bw process");
                }
                return new List<VaultwardenItem>();
            }

            await exitTask; // Ensure we get the actual exit code
            _logger.LogDebug("'bw list items' process completed with exit code: {ExitCode}", process.ExitCode);

            var output = await outputTask;
            var error = await errorTask;
            _logger.LogDebug("Got output length: {OutputLen}, error length: {ErrorLen}",
                output?.Length ?? 0, error?.Length ?? 0);

            if (process.ExitCode != 0)
            {
                _logger.LogError("Failed to retrieve items (exit {Code}). stderr: {Stderr} | stdout: {Stdout}",
                    process.ExitCode, error, output);
                return new List<VaultwardenItem>();
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                _logger.LogWarning("bw list items returned empty output");
                return new List<VaultwardenItem>();
            }

            List<VaultwardenItem> items;
            try
            {
                items = JsonSerializer.Deserialize<List<VaultwardenItem>>(output, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<VaultwardenItem>();

                _logger.LogDebug("Successfully parsed {Count} items from Vaultwarden", items.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse JSON response from bw list items. Output was: {Output}", output);
                return new List<VaultwardenItem>();
            }

            // Apply optional organization filter
            var resolvedOrgId = await ResolveOrganizationIdAsync();
            if (!string.IsNullOrWhiteSpace(resolvedOrgId))
            {
                items = items.Where(i => string.Equals(i.OrganizationId, resolvedOrgId, StringComparison.OrdinalIgnoreCase)).ToList();
                _logger.LogDebug("Retrieved {Count} items from Vaultwarden (filtered to organization {OrgId})", items.Count, resolvedOrgId);
            }

            // Apply optional folder filter
            var resolvedFolderId = await ResolveFolderIdAsync();
            if (!string.IsNullOrWhiteSpace(resolvedFolderId))
            {
                items = items.Where(i => string.Equals(i.FolderId, resolvedFolderId, StringComparison.OrdinalIgnoreCase)).ToList();
                _logger.LogDebug("Filtered to folder {FolderId}: {Count} items remain", resolvedFolderId, items.Count);
            }

            // Apply optional collection filter (item.CollectionIds contains zero or more collections)
            var resolvedCollectionId = await ResolveCollectionIdAsync(resolvedOrgId);
            if (!string.IsNullOrWhiteSpace(resolvedCollectionId))
            {
                items = items.Where(i => i.CollectionIds != null && i.CollectionIds.Contains(resolvedCollectionId, StringComparer.OrdinalIgnoreCase)).ToList();
                _logger.LogDebug("Filtered to collection {CollectionId}: {Count} items remain", resolvedCollectionId, items.Count);
            }

            _logger.LogDebug("Retrieved {Count} items from Vaultwarden after all filters", items.Count);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve items from Vaultwarden");
            return new List<VaultwardenItem>();
        }
    }

    private string GetOrganizationArgs()
    {
        if (string.IsNullOrWhiteSpace(_config.OrganizationId))
            return string.Empty;
        return $" --organizationid {_config.OrganizationId}";
    }

    private string GetFolderArgs()
    {
        if (string.IsNullOrWhiteSpace(_config.FolderId))
            return string.Empty;
        return $" --folderid {_config.FolderId}";
    }

    private string GetCollectionArgs()
    {
        if (string.IsNullOrWhiteSpace(_config.CollectionId))
            return string.Empty;
        return $" --collectionid {_config.CollectionId}";
    }

    private async Task<string?> ResolveOrganizationIdAsync()
    {
        if (!string.IsNullOrWhiteSpace(_config.OrganizationId))
        {
            return _config.OrganizationId;
        }
        if (string.IsNullOrWhiteSpace(_config.OrganizationName))
        {
            return null;
        }

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = $"list organizations --raw{GetSessionArgs()}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo);

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await errorTask;
                _logger.LogWarning("Failed to list organizations: {Error}", error);
                return null;
            }

            var output = await outputTask;
            var organizations = JsonSerializer.Deserialize<List<OrganizationInfo>>(output, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<OrganizationInfo>();

            var org = organizations.FirstOrDefault(o => string.Equals(o.Name, _config.OrganizationName, StringComparison.OrdinalIgnoreCase));
            if (org != null)
            {
                return org.Id;
            }

            _logger.LogWarning("Organization with name '{OrgName}' not found.", _config.OrganizationName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resolving organization by name");
            return null;
        }
    }

    public async Task<Dictionary<string, string>> GetOrganizationsMapAsync()
    {
        var orgMap = new Dictionary<string, string>();
        
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = $"list organizations --raw{GetSessionArgs()}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo);

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await errorTask;
                _logger.LogWarning("Failed to list organizations: {Error}", error);
                return orgMap;
            }

            var output = await outputTask;
            var organizations = JsonSerializer.Deserialize<List<OrganizationInfo>>(output, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<OrganizationInfo>();

            foreach (var org in organizations)
            {
                orgMap[org.Id] = org.Name;
            }
            
            _logger.LogInformation("Fetched {Count} organizations", orgMap.Count);
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
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = "status",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo);

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                var output = await outputTask;
                var status = JsonSerializer.Deserialize<JsonElement>(output);
                
                if (status.TryGetProperty("userEmail", out var emailElement))
                {
                    return emailElement.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching current user email");
        }
        
        return null;
    }

    private async Task<string?> ResolveFolderIdAsync()
    {
        if (!string.IsNullOrWhiteSpace(_config.FolderId))
            return _config.FolderId;
        if (string.IsNullOrWhiteSpace(_config.FolderName))
            return null;

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = $"list folders --raw{GetSessionArgs()}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo);

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await errorTask;
                _logger.LogWarning("Failed to list folders: {Error}", error);
                return null;
            }

            var output = await outputTask;
            var folders = JsonSerializer.Deserialize<List<FolderInfo>>(output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? new List<FolderInfo>();
            var folder = folders.FirstOrDefault(f => string.Equals(f.Name, _config.FolderName, StringComparison.OrdinalIgnoreCase));
            return folder?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resolving folder by name");
            return null;
        }
    }

    private async Task<string?> ResolveCollectionIdAsync(string? orgId)
    {
        if (!string.IsNullOrWhiteSpace(_config.CollectionId))
            return _config.CollectionId;
        if (string.IsNullOrWhiteSpace(_config.CollectionName))
            return null;

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = $"list collections --raw{GetSessionArgs()}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo);

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await errorTask;
                _logger.LogWarning("Failed to list collections: {Error}", error);
                return null;
            }

            var output = await outputTask;
            var collections = JsonSerializer.Deserialize<List<CollectionInfo>>(output, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                               ?? new List<CollectionInfo>();
            // If orgId is known, prefer matching collections from that org
            var candidates = !string.IsNullOrWhiteSpace(orgId)
                ? collections.Where(c => string.Equals(c.OrganizationId, orgId, StringComparison.OrdinalIgnoreCase))
                : collections;
            var col = candidates.FirstOrDefault(c => string.Equals(c.Name, _config.CollectionName, StringComparison.OrdinalIgnoreCase));
            return col?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resolving collection by name");
            return null;
        }
    }

    // OrganizationInfo is now defined in IVaultwardenService.cs

    private class FolderInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private class CollectionInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? OrganizationId { get; set; }
    }

    public async Task<VaultwardenItem?> GetItemAsync(string id)
    {
        if (!_isAuthenticated)
        {
            throw new InvalidOperationException("Not authenticated. Call AuthenticateAsync first.");
        }

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = $"get item {id} --raw{GetSessionArgs()}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo);

            process.Start();

            // Read output and error streams asynchronously with timeout
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();

            // Wait for process to exit with timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(exitTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogError("Process timed out after 30 seconds for item {Id}", id);
                try { process.Kill(); } catch { }
                return null;
            }

            await exitTask; // Ensure we get the actual exit code

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                _logger.LogError("Failed to retrieve item {Id}: {Error}", id, error);
                return null;
            }

            return JsonSerializer.Deserialize<VaultwardenItem>(output, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve item {Id}", id);
            return null;
        }
    }

    public async Task<string> GetItemPasswordAsync(string id)
    {
        if (!_isAuthenticated)
        {
            throw new InvalidOperationException("Not authenticated. Call AuthenticateAsync first.");
        }

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = $"get password {id} --raw{GetSessionArgs()}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo);

            process.Start();

            // Read output and error streams asynchronously with timeout
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();

            // Wait for process to exit with timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(exitTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogError("bw get password timed out after 30 seconds for item {Id}", id);
                try { process.Kill(); } catch { }
                return string.Empty;
            }

            await exitTask; // Ensure we get the actual exit code

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                _logger.LogError("Failed to retrieve password for item {Id}: {Error}", id, error);
                return string.Empty;
            }

            return output.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve password for item {Id}", id);
            return string.Empty;
        }
    }

    public Task<bool> IsAuthenticatedAsync()
    {
        return Task.FromResult(_isAuthenticated);
    }

    public async Task LogoutAsync()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = "logout",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            // Read output and error streams asynchronously with timeout
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();

            // Wait for process to exit with timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(exitTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("bw logout timed out after 30 seconds");
                try { process.Kill(); } catch { }
            }
            else
            {
                await exitTask; // Ensure we get the actual exit code
                var output = await outputTask;
                var error = await errorTask;

                // if (process.ExitCode != 0)
                // {
                //     _logger.LogWarning("bw logout returned non-zero exit code {Code}: {Error}", process.ExitCode, error);
                // }
            }

            _isAuthenticated = false;
            _sessionToken = null;
            _logger.LogDebug("Logged out from Vaultwarden");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to logout from Vaultwarden");
            _isAuthenticated = false;
            _sessionToken = null;
        }
    }

    private async Task SyncVaultAsync()
    {
        try
        {
            _logger.LogDebug("Starting vault sync...");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = $"sync --raw{GetSessionArgs()}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo);

            _logger.LogDebug("bw sync: session token available = {HasToken}", !string.IsNullOrWhiteSpace(_sessionToken));
            process.Start();

            // Read output and error streams asynchronously with timeout
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();

            // Wait for process to exit with timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
            var completedTask = await Task.WhenAny(exitTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("bw sync timed out after 60 seconds");
                try { process.Kill(); } catch { }
                return;
            }

            await exitTask; // Ensure we get the actual exit code

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("bw sync returned non-zero exit: {Error}", error);
            }
            else
            {
                _logger.LogDebug("Vault synced");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "bw sync failed (continuing)");
        }
    }

    private void ApplyCommonEnv(ProcessStartInfo startInfo)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_config.ClientId))
            {
                startInfo.Environment["BW_CLIENTID"] = _config.ClientId!;
            }
            if (!string.IsNullOrWhiteSpace(_config.ClientSecret))
            {
                startInfo.Environment["BW_CLIENTSECRET"] = _config.ClientSecret!;
            }
            // Note: Session token is now passed via --session parameter in GetSessionArgs()
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to apply bw environment variables");
        }
    }

    private string GetSessionArgs()
    {
        if (!string.IsNullOrWhiteSpace(_sessionToken))
        {
            return $" --session {_sessionToken}";
        }
        return string.Empty;
    }
}