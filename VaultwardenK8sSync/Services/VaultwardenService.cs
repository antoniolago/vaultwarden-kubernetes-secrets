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
    private int _consecutiveEmptyResults = 0;
    private DateTime _lastSuccessfulFetch = DateTime.MinValue;

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
        
        // Clean and initialize data directory on startup
        InitializeDataDirectory();
    }
    
    private void InitializeDataDirectory()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_config.DataDirectory))
            {
                // If directory exists, clean it to avoid stale state conflicts
                if (Directory.Exists(_config.DataDirectory))
                {
                    _logger.LogDebug("Cleaning existing bw data directory: {DataDir}", _config.DataDirectory);
                    Directory.Delete(_config.DataDirectory, recursive: true);
                }
                
                // Create fresh directory
                Directory.CreateDirectory(_config.DataDirectory);
                _logger.LogDebug("Initialized clean bw data directory: {DataDir}", _config.DataDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize bw data directory - continuing with existing state");
        }
    }

    public async Task<bool> AuthenticateAsync()
    {
        try
        {
            // Set the server URL first (logout first if needed for server change)
            if (!string.IsNullOrEmpty(_config.ServerUrl))
            {
                if (!IsValidServerUrl(_config.ServerUrl))
                {
                    _logger.LogError("Invalid ServerUrl format: {ServerUrl}", _config.ServerUrl);
                    return false;
                }
                var setServerResult = await SetServerUrlAsync();
                if (!setServerResult)
                {
                    await LogoutAsync();
                    await Task.Delay(Constants.Delays.PostCommandDelayMs);
                    setServerResult = await SetServerUrlAsync();
                    if (!setServerResult)
                    {
                        _logger.LogError("Failed to set server URL: {ServerUrl}", _config.ServerUrl);
                        return false;
                    }
                }
                await Task.Delay(Constants.Delays.PostCommandDelayMs);
            }

            var ok = await AuthenticateWithApiKeyAsync();
            
            if (ok && string.IsNullOrWhiteSpace(_sessionToken))
            {
                _logger.LogError("Authentication completed but no session token available");
                return false;
            }
            
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed: {Message}", ex.Message);
            return false;
        }
    }

    private async Task<bool> SetServerUrlAsync()
    {
        try
        {
            var process = _processFactory.CreateBwProcess($"config server {_config.ServerUrl}");
            ApplyCommonEnv(process.StartInfo);
            var result = await _processRunner.RunAsync(process, Constants.Timeouts.DefaultCommandTimeoutSeconds);
            
            if (!result.Success)
            {
                _logger.LogError("Failed to set server URL. ExitCode: {ExitCode}, Error: {Error}, Output: {Output}", 
                    result.ExitCode, result.Error ?? "(empty)", result.Output ?? "(empty)");
            }
            
            return result.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while setting server URL: {Message}", ex.Message);
            return false;
        }
    }

    private async Task<bool> AuthenticateWithApiKeyAsync()
    {
        if (string.IsNullOrEmpty(_config.ClientId) || string.IsNullOrEmpty(_config.ClientSecret))
        {
            _logger.LogError("ClientId and ClientSecret are required");
            return false;
        }

        var process = _processFactory.CreateBwProcess("login --apikey --raw");
        ApplyCommonEnv(process.StartInfo);

        var result = await _processRunner.RunAsync(process, Constants.Timeouts.DefaultCommandTimeoutSeconds);

        // Check if already logged in - if so, logout and retry
        if (!result.Success && result.Error?.Contains("You are already logged in") == true)
        {
            await LogoutAsync();
            await Task.Delay(Constants.Delays.PostCommandDelayMs);
            return await AuthenticateWithApiKeyAsync();
        }
        
        // Check for "Expected user never made active" error - bw CLI race condition
        if (!result.Success && result.Error?.Contains("Expected user never made active") == true)
        {
            await Task.Delay(2000);
            await LogoutAsync();
            await Task.Delay(Constants.Delays.PostCommandDelayMs);
            return await AuthenticateWithApiKeyAsync();
        }

        if (!result.Success)
        {
            _logger.LogError("API key login failed. ExitCode: {ExitCode}, Error: {Error}, Output: {Output}", 
                result.ExitCode, result.Error ?? "(empty)", result.Output ?? "(empty)");
            _isAuthenticated = false;
            return false;
        }

        // Give the CLI time to stabilize after login
        await Task.Delay(Constants.Delays.PostUnlockDelayMs + 1000);

        // Check vault status and unlock if needed
        var unlockSuccess = await TestVaultAccessAsync();
        
        _isAuthenticated = unlockSuccess;
        return unlockSuccess;
    }

    // Password login removed

    private async Task<bool> TestVaultAccessAsync()
    {
        try
        {
            var process = _processFactory.CreateBwProcess($"status --raw{GetSessionArgs()}");
            ApplyCommonEnv(process.StartInfo);
            var result = await _processRunner.RunAsync(process, Constants.Timeouts.DefaultCommandTimeoutSeconds);

            if (result.Success && !string.IsNullOrEmpty(result.Output))
            {
                try
                {
                    var statusJson = System.Text.Json.JsonDocument.Parse(result.Output.Trim());
                    if (statusJson.RootElement.TryGetProperty("status", out var statusProperty))
                    {
                        var vaultStatus = statusProperty.GetString()?.ToLowerInvariant() ?? "unknown";
                        if (vaultStatus == "unlocked")
                        {
                            // Extract session token if available
                            if (statusJson.RootElement.TryGetProperty("token", out var tokenProperty))
                            {
                                var token = tokenProperty.GetString();
                                if (!string.IsNullOrEmpty(token))
                                {
                                    _sessionToken = token;
                                    Environment.SetEnvironmentVariable("BW_SESSION", token);
                                    Environment.SetEnvironmentVariable("BW_SESSION", token, EnvironmentVariableTarget.Process);
                                }
                            }
                            return true;
                        }
                    }
                }
                catch
                {
                    // If parsing fails, assume locked and try unlock
                }
            }

            // Vault is locked or status check failed - attempt unlock
            return await UnlockVaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check vault status. Error: {Error}", ex.Message);
            return await UnlockVaultAsync();
        }
    }

    private async Task<bool> UnlockVaultAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_config.MasterPassword))
            {
                _logger.LogError("Master password is not configured");
                return false;
            }

            var process = _processFactory.CreateBwProcess("unlock --raw");
            ApplyCommonEnv(process.StartInfo);

            var result = await _processRunner.RunAsync(
                process, 
                Constants.Timeouts.DefaultCommandTimeoutSeconds, 
                _config.MasterPassword);

            if (result.Success)
            {
                var token = result.Output?.Trim();
                if (!string.IsNullOrEmpty(token))
                {
                    _sessionToken = token;
                    Environment.SetEnvironmentVariable("BW_SESSION", token);
                    Environment.SetEnvironmentVariable("BW_SESSION", token, EnvironmentVariableTarget.Process);
                    return true;
                }
                else
                {
                    _logger.LogError("Unlock succeeded but no session token returned. ExitCode: {ExitCode}, Output: {Output}, Error: {Error}", 
                        result.ExitCode, result.Output ?? "(empty)", result.Error ?? "(empty)");
                    return false;
                }
            }
            else
            {
                _logger.LogError("Vault unlock failed. ExitCode: {ExitCode}, Error: {Error}, Output: {Output}", 
                    result.ExitCode, result.Error ?? "(empty)", result.Output ?? "(empty)");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during vault unlock: {Message}", ex.Message);
            return false;
        }
    }


    public async Task<List<VaultwardenItem>> GetItemsAsync()
    {
        // If not authenticated, try to authenticate first
        if (!_isAuthenticated)
        {
            _logger.LogWarning("Not authenticated - attempting authentication before fetching items...");
            var authSuccess = await AuthenticateAsync();
            
            if (!authSuccess)
            {
                throw new InvalidOperationException("Failed to authenticate with Vaultwarden");
            }
        }

        // Try to get items
        var items = await GetItemsInternalAsync();
        
        // If we got items, reset failure counter and record success
        if (items.Count > 0)
        {
            _consecutiveEmptyResults = 0;
            _lastSuccessfulFetch = DateTime.UtcNow;
            return items;
        }
        
        _consecutiveEmptyResults++;
        _logger.LogWarning("No items retrieved (consecutive: {Count})", _consecutiveEmptyResults);
        
        // If we previously had items but now get empty, session likely expired - re-auth immediately
        var hadPreviousSuccess = _lastSuccessfulFetch != DateTime.MinValue;
        var timeSinceSuccess = DateTime.UtcNow - _lastSuccessfulFetch;
        var shouldReAuth = hadPreviousSuccess || _consecutiveEmptyResults >= 3 || timeSinceSuccess.TotalMinutes > 5;
        
        if (shouldReAuth)
        {
            _logger.LogWarning("Session may have expired (empty results: {Count}, time since success: {Minutes}min, had previous success: {HadSuccess}). Attempting re-authentication...", 
                _consecutiveEmptyResults, timeSinceSuccess.TotalMinutes, hadPreviousSuccess);
            
            var reAuthSuccess = await AuthenticateAsync();
            
            if (reAuthSuccess)
            {
                _logger.LogInformation("Re-authentication successful, retrying item fetch...");
                _consecutiveEmptyResults = 0; // Reset counter after re-auth
                items = await GetItemsInternalAsync();
                
                if (items.Count > 0)
                {
                    _lastSuccessfulFetch = DateTime.UtcNow;
                }
                else
                {
                    // If we had items before and still get 0 after successful re-auth, something is wrong
                    if (hadPreviousSuccess)
                    {
                        _logger.LogError("Re-authentication succeeded but still no items found - this is abnormal after having {LastFetch}. Possible CLI issue.", _lastSuccessfulFetch);
                        throw new InvalidOperationException(
                            $"Re-authentication succeeded but vault returned 0 items after having items at {_lastSuccessfulFetch:o}. " +
                            "This indicates a persistent CLI session or vault access issue.");
                    }
                    
                    _logger.LogWarning("Re-authentication succeeded but no items found - vault may be legitimately empty (first run)");
                }
            }
            else
            {
                _logger.LogError("Re-authentication failed after detecting session expiration");
                
                // If we previously had items and re-auth fails, throw to trigger retry
                if (hadPreviousSuccess)
                {
                    throw new InvalidOperationException(
                        $"Session expired (had {_lastSuccessfulFetch:o}) and re-authentication failed. " +
                        "Throwing to trigger resilience policies.");
                }
            }
        }
        else
        {
            _logger.LogInformation("Not re-authenticating yet - this appears to be the first sync and vault may be legitimately empty");
        }
        
        return items;
    }

    private async Task<List<VaultwardenItem>> GetItemsInternalAsync()
    {
        try
        {
            // Validate session before fetching
            if (string.IsNullOrWhiteSpace(_sessionToken))
            {
                _logger.LogWarning("No session token available - cannot fetch items");
                return new List<VaultwardenItem>();
            }

            // Sync vault to get latest changes from server
            var syncSuccess = await SyncVaultAsync();
            if (!syncSuccess)
            {
                _logger.LogWarning("Vault sync failed - attempting to list items anyway");
            }

            var arguments = $"list items --raw{GetSessionArgs()}{GetOrganizationArgs()}{GetFolderArgs()}{GetCollectionArgs()}";
            
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo);

            process.Start();
            
            // Read output and error streams asynchronously BEFORE waiting for exit
            // This prevents deadlock if the process fills the output buffer
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            // Wait for all tasks (output reading + process exit) with timeout
            var exitTask = process.WaitForExitAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(120)); // Increased timeout
            
            // Wait for either all tasks to complete OR timeout
            var allTasks = Task.WhenAll(outputTask, errorTask, exitTask);
            var completedTask = await Task.WhenAny(allTasks, timeoutTask);

            string output;
            string error;
            
            if (completedTask == timeoutTask)
            {
                _logger.LogError("bw list items timed out after 120 seconds");
                
                // Try to read partial output before killing
                var partialOutput = outputTask.IsCompleted ? await outputTask : "(output not available)";
                var partialError = errorTask.IsCompleted ? await errorTask : "(error not available)";
                _logger.LogError("Partial stdout: {Stdout}", !string.IsNullOrEmpty(partialOutput) ? partialOutput.Substring(0, Math.Min(500, partialOutput.Length)) : "(empty)");
                _logger.LogError("Partial stderr: {Stderr}", !string.IsNullOrEmpty(partialError) ? partialError.Substring(0, Math.Min(500, partialError.Length)) : "(empty)");
                
                var gcMemoryAfter = GC.GetTotalMemory(false);
                _logger.LogError("Memory at timeout: {MemoryMB} MB", gcMemoryAfter / 1024 / 1024);
                try
                {
                    _logger.LogWarning("Attempting to kill hung bw process...");
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to kill bw process");
                }
                return new List<VaultwardenItem>();
            }

            // All tasks completed successfully
            await exitTask;
            output = await outputTask;
            error = await errorTask;

            if (process.ExitCode != 0)
            {
                _logger.LogError("Failed to retrieve items (exit {Code}). stderr: {Stderr} | stdout: {Stdout}",
                    process.ExitCode, error, output);
                return new List<VaultwardenItem>();
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                _logger.LogWarning("bw list items returned empty output (exit 0 but no data)");
                _logger.LogWarning("Session token was: {HasToken} (len: {Len})", 
                    !string.IsNullOrWhiteSpace(_sessionToken),
                    _sessionToken?.Length ?? 0);
                _logger.LogWarning("stderr: {Stderr}", error ?? "(empty)");
                
                // This should not happen - exit 0 with empty output suggests session issue
                // Mark as not authenticated to trigger re-auth on next call
                _isAuthenticated = false;
                return new List<VaultwardenItem>();
            }

            List<VaultwardenItem> items;
            try
            {
                items = JsonSerializer.Deserialize<List<VaultwardenItem>>(output, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<VaultwardenItem>();
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
            }

            // Apply optional folder filter
            var resolvedFolderId = await ResolveFolderIdAsync();
            if (!string.IsNullOrWhiteSpace(resolvedFolderId))
            {
                items = items.Where(i => string.Equals(i.FolderId, resolvedFolderId, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // Apply optional collection filter
            var resolvedCollectionId = await ResolveCollectionIdAsync(resolvedOrgId);
            if (!string.IsNullOrWhiteSpace(resolvedCollectionId))
            {
                items = items.Where(i => i.CollectionIds != null && i.CollectionIds.Contains(resolvedCollectionId, StringComparer.OrdinalIgnoreCase)).ToList();
            }
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to logout from Vaultwarden");
            _isAuthenticated = false;
            _sessionToken = null;
        }
    }

    private async Task<bool> SyncVaultAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_sessionToken))
            {
                _logger.LogWarning("Cannot sync vault - no session token available");
                return false;
            }

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
                return false;
            }

            await exitTask; // Ensure we get the actual exit code

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("bw sync returned non-zero exit code {ExitCode}: {Error}", process.ExitCode, error);
                
                // Check for authentication-related errors that would invalidate the session
                if (error != null && (error.Contains("not logged in") || error.Contains("locked") || 
                    error.Contains("session") || error.Contains("authentication")))
                {
                    _logger.LogWarning("Detected authentication issue in bw sync - session may have been invalidated");
                    _isAuthenticated = false;
                    _sessionToken = null;
                    return false;
                }
                
                return false;
            }
            else
            {
                _logger.LogInformation("Vault synced successfully with server");
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "bw sync failed with exception (continuing)");
            return false;
        }
    }

    /// <summary>
    /// Validates ServerUrl to prevent command injection attacks.
    /// Only allows HTTPS URLs without dangerous shell metacharacters.
    /// </summary>
    private static bool IsValidServerUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Must be a valid absolute URL
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // Only allow HTTPS (not HTTP or other schemes)
        if (uri.Scheme != "https")
            return false;

        // Block shell metacharacters that could be used for command injection
        var dangerousChars = new[] { ";", "`", "$", "&", "|", "\n", "\r", "'", "\"", "<", ">", "(", ")" };
        if (dangerousChars.Any(url.Contains))
            return false;

        return true;
    }

    private void ApplyCommonEnv(ProcessStartInfo startInfo)
    {
        try
        {
            // Set consistent data directory for bw CLI to ensure session state persists
            if (!string.IsNullOrWhiteSpace(_config.DataDirectory))
            {
                // Ensure the directory exists
                Directory.CreateDirectory(_config.DataDirectory);
                startInfo.Environment["BITWARDENCLI_APPDATA_DIR"] = _config.DataDirectory;
            }
            
            if (!string.IsNullOrWhiteSpace(_config.ClientId))
            {
                startInfo.Environment["BW_CLIENTID"] = _config.ClientId!;
            }
            if (!string.IsNullOrWhiteSpace(_config.ClientSecret))
            {
                startInfo.Environment["BW_CLIENTSECRET"] = _config.ClientSecret!;
            }
            // Set BW_SESSION environment variable for newer bw CLI versions
            if (!string.IsNullOrWhiteSpace(_sessionToken))
            {
                startInfo.Environment["BW_SESSION"] = _sessionToken!;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply bw environment variables");
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