using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VaultwardenK8sSync.Models;

namespace VaultwardenK8sSync.Services;

public class VaultwardenService : IVaultwardenService
{
    private readonly ILogger<VaultwardenService> _logger;
    private readonly VaultwardenConfig _config;
    private bool _isAuthenticated = false;

    public VaultwardenService(ILogger<VaultwardenService> logger, VaultwardenConfig config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task<bool> AuthenticateAsync()
    {
        try
        {
            _logger.LogInformation("Authenticating with Vaultwarden...");

            if (_config.UseApiKey && !string.IsNullOrEmpty(_config.ApiKey))
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

    private async Task<bool> AuthenticateWithApiKeyAsync()
    {
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
        await process.StandardInput.WriteLineAsync(_config.ApiKey);
        await process.StandardInput.WriteLineAsync(_config.ClientSecret ?? "");
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();

        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            _isAuthenticated = true;
            _logger.LogInformation("Successfully authenticated with API key");
            return true;
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
            return true;
        }

        var error = await process.StandardError.ReadToEndAsync();
        _logger.LogError("Authentication failed: {Error}", error);
        return false;
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
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
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
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
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
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
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