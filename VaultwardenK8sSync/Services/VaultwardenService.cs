using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

public class VaultwardenService : IVaultwardenService
{
    private readonly ILogger<VaultwardenService> _logger;
    private readonly VaultwardenSettings _config;
    private bool _isAuthenticated = false;
    private string? _sessionToken;

    public VaultwardenService(ILogger<VaultwardenService> logger, VaultwardenSettings config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task<bool> AuthenticateAsync()
    {
        try
        {
            _logger.LogInformation("Authenticating with Vaultwarden (API key)...");
            await LogBwVersionAsync();
            await LogBwStatusAsync("pre-login");

            // Logout first to ensure clean state
            await LogoutAsync();

            // Set the server URL first
            if (!string.IsNullOrEmpty(_config.ServerUrl))
            {   
                var setServerResult = await SetServerUrlAsync();
                if (!setServerResult)
                {
                    _logger.LogError("Failed to set server URL: {ServerUrl}", _config.ServerUrl);
                    return false;
                }
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
            _logger.LogInformation("Configuring server: {ServerUrl}", _config.ServerUrl);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = $"config server {_config.ServerUrl}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo, includeSession: false);

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
                _logger.LogError("bw config server timed out after 30 seconds");
                try { process.Kill(); } catch { }
                return false;
            }
            
            await exitTask; // Ensure we get the actual exit code
            
            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode == 0)
            {
                _logger.LogDebug("Server URL set");
                return true;
            }

            _logger.LogError("Failed to set server URL: {Error}", error);
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
        ApplyCommonEnv(process.StartInfo, includeSession: false);

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
            _isAuthenticated = true;
            _logger.LogInformation("Authenticated (API key). Output length: {Len}", string.IsNullOrEmpty(stdout) ? 0 : stdout.Length);
            
            // Unlock the vault
            return await UnlockVaultAsync();
        }

        _logger.LogError("Authentication failed (exit {Code}). stderr: {Stderr} | stdout: {Stdout}", process.ExitCode, stderr, stdout);
        return false;
    }

    // Password login removed

    private async Task<bool> UnlockVaultAsync()
    {
        try
        {
            _logger.LogInformation("Unlocking vault...");

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
            ApplyCommonEnv(process.StartInfo, includeSession: false);

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
                _sessionToken = token;
                _logger.LogInformation("Vault unlocked (session token length: {Len})", len);
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
                _logger.LogInformation("bw version: {Version}", output.Trim());
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
                    Arguments = "status --raw",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo, includeSession: true);

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
            _logger.LogDebug("Ensuring Vaultwarden vault is synced...");
            await SyncVaultAsync();

            _logger.LogInformation("Fetching items from Vaultwarden...");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = "list items --raw",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo, includeSession: true);

            if (string.IsNullOrWhiteSpace(_sessionToken))
            {
                _logger.LogWarning("BW_SESSION is not set before 'bw list items'. This may cause prompts or failures.");
            }

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
                _logger.LogError("bw list items timed out after 60 seconds");
                try { process.Kill(); } catch { }
                return new List<VaultwardenItem>();
            }
            
            await exitTask; // Ensure we get the actual exit code
            
            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                _logger.LogError("Failed to retrieve items: {Error}", error);
                return new List<VaultwardenItem>();
            }

            var items = JsonSerializer.Deserialize<List<VaultwardenItem>>(output, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<VaultwardenItem>();

            // Apply optional organization filter
            var resolvedOrgId = await ResolveOrganizationIdAsync();
            if (!string.IsNullOrWhiteSpace(resolvedOrgId))
            {
                items = items.Where(i => string.Equals(i.OrganizationId, resolvedOrgId, StringComparison.OrdinalIgnoreCase)).ToList();
                _logger.LogInformation("Retrieved {Count} items from Vaultwarden (filtered to organization {OrgId})", items.Count, resolvedOrgId);
            }

            // Apply optional folder filter
            var resolvedFolderId = await ResolveFolderIdAsync();
            if (!string.IsNullOrWhiteSpace(resolvedFolderId))
            {
                items = items.Where(i => string.Equals(i.FolderId, resolvedFolderId, StringComparison.OrdinalIgnoreCase)).ToList();
                _logger.LogInformation("Filtered to folder {FolderId}: {Count} items remain", resolvedFolderId, items.Count);
            }

            // Apply optional collection filter (item.CollectionIds contains zero or more collections)
            var resolvedCollectionId = await ResolveCollectionIdAsync(resolvedOrgId);
            if (!string.IsNullOrWhiteSpace(resolvedCollectionId))
            {
                items = items.Where(i => i.CollectionIds != null && i.CollectionIds.Contains(resolvedCollectionId, StringComparer.OrdinalIgnoreCase)).ToList();
                _logger.LogInformation("Filtered to collection {CollectionId}: {Count} items remain", resolvedCollectionId, items.Count);
            }

            _logger.LogInformation("Retrieved {Count} items from Vaultwarden after all filters", items.Count);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve items from Vaultwarden");
            return new List<VaultwardenItem>();
        }
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
                    Arguments = "list organizations --raw",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo, includeSession: true);

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
                    Arguments = "list folders --raw",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo, includeSession: true);

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
                    Arguments = "list collections --raw",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo, includeSession: true);

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

    private class OrganizationInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

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
                    Arguments = $"get item {id} --raw",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo, includeSession: true);

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
                    Arguments = $"get password {id} --raw",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo, includeSession: true);

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
                
                if (process.ExitCode != 0)
                {
                    _logger.LogWarning("bw logout returned non-zero exit code {Code}: {Error}", process.ExitCode, error);
                }
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
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = "sync --raw",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ApplyCommonEnv(process.StartInfo, includeSession: true);

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

    private void ApplyCommonEnv(ProcessStartInfo startInfo, bool includeSession = true)
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
            if (includeSession && !string.IsNullOrWhiteSpace(_sessionToken))
            {
                startInfo.Environment["BW_SESSION"] = _sessionToken!;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to apply bw environment variables");
        }
    }
} 