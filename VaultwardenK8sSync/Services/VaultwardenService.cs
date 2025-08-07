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

    public VaultwardenService(ILogger<VaultwardenService> logger, VaultwardenSettings config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task<bool> AuthenticateAsync()
    {
        try
        {
            _logger.LogInformation("Authenticating with Vaultwarden...");

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

            if (true)
            {
                return await AuthenticateWithApiKeyAsync();
            }
            else
            {
                return await AuthenticateWithPasswordAsync();
            }
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
            _logger.LogInformation("Setting Bitwarden server URL: {ServerUrl}", _config.ServerUrl);

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

            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("Successfully set server URL");
                return true;
            }

            var error = await process.StandardError.ReadToEndAsync();
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

        process.Start();
        // await process.StandardInput.WriteLineAsync(_config.ClientId);
        // await process.StandardInput.WriteLineAsync(_config.ClientSecret);
        // await process.StandardInput.FlushAsync();
        // process.StandardInput.Close();

        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            _isAuthenticated = true;
            _logger.LogInformation("Successfully authenticated with API key");
            
            // Unlock the vault
            return await UnlockVaultAsync();
        }

        var error = await process.StandardError.ReadToEndAsync();
        _logger.LogError("Authentication failed: {Error}", error);
        return false;
    }

    private async Task<bool> AuthenticateWithPasswordAsync()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bw",
                Arguments = $"login {_config.Email} --raw",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.StandardInput.WriteLineAsync(_config.MasterPassword);
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();

        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            _isAuthenticated = true;
            _logger.LogInformation("Successfully authenticated with password");
            
            // Unlock the vault
            return await UnlockVaultAsync();
        }

        var error = await process.StandardError.ReadToEndAsync();
        _logger.LogError("Authentication failed: {Error}", error);
        return false;
    }

    private async Task<bool> UnlockVaultAsync()
    {
        try
        {
            _logger.LogInformation("Unlocking Vaultwarden vault...");

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

            process.Start();
            await process.StandardInput.WriteLineAsync(_config.MasterPassword);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();

            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("Successfully unlocked vault");
                return true;
            }

            var error = await process.StandardError.ReadToEndAsync();
            _logger.LogError("Failed to unlock vault: {Error}", error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unlock vault");
            return false;
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
            _logger.LogInformation("Retrieving items from Vaultwarden...");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "bw",
                    Arguments = "list items --raw",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            
            try
            {
                await process.StandardInput.WriteLineAsync(_config.MasterPassword);
                await process.StandardInput.FlushAsync();
                process.StandardInput.Close();
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Could not write to StandardInput: {Error}", ex.Message);
                // Continue anyway - the process might not need input
            }
            // Read output and error streams asynchronously with timeout
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();
            
            // Wait for process to exit with timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(exitTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                _logger.LogError("Process timed out after 30 seconds");
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

            _logger.LogInformation("Retrieved {Count} items from Vaultwarden", items.Count);
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve items from Vaultwarden");
            return new List<VaultwardenItem>();
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
                _logger.LogError("Process timed out after 30 seconds for password of item {Id}", id);
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
            await process.WaitForExitAsync();

            _isAuthenticated = false;
            _logger.LogInformation("Logged out from Vaultwarden");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to logout from Vaultwarden");
        }
    }
} 