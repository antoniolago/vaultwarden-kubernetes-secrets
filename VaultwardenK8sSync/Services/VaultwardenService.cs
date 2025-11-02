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
            _logger.LogDebug("Authenticating with Vaultwarden (API key)...");
            _logger.LogInformation("Using bw CLI data directory: {DataDir}", _config.DataDirectory);
            await LogBwVersionAsync();
            await LogBwStatusAsync("pre-login");

            // Set the server URL first (logout first if needed for server change)
            if (!string.IsNullOrEmpty(_config.ServerUrl))
            {
                // Validate ServerUrl to prevent command injection
                if (!IsValidServerUrl(_config.ServerUrl))
                {
                    _logger.LogError("Invalid or potentially dangerous ServerUrl format: {ServerUrl}", _config.ServerUrl);
                    return false;
                }
                // Try to set server URL, if it fails due to existing session, logout and retry
                var setServerResult = await SetServerUrlAsync();
                if (!setServerResult)
                {
                    _logger.LogDebug("Server URL change requires logout - logging out first");
                    await LogoutAsync();
                    await Task.Delay(Constants.Delays.PostCommandDelayMs);
                    
                    setServerResult = await SetServerUrlAsync();
                    if (!setServerResult)
                    {
                        _logger.LogError("Failed to set server URL even after logout: {ServerUrl}", _config.ServerUrl);
                        return false;
                    }
                }

                // Give the CLI time to write server config to disk
                await Task.Delay(Constants.Delays.PostCommandDelayMs);
                await LogBwStatusAsync("post-server-config");
            }

            var ok = await AuthenticateWithApiKeyAsync();
            await LogBwStatusAsync("post-login");
            
            if (ok)
            {
                // Verify session token is available
                if (string.IsNullOrWhiteSpace(_sessionToken))
                {
                    _logger.LogError("❌ AUTHENTICATION FAILED: Login completed but no session token available");
                    _logger.LogError("   This will cause all subsequent commands to fail");
                    _logger.LogError("   This indicates a bw CLI state issue or unlock failure");
                    return false;
                }
                _logger.LogInformation("✅ Authentication successful with session token (length: {Len})", _sessionToken.Length);
            }
            else
            {
                _logger.LogError("❌ AUTHENTICATION FAILED: API key login or vault unlock failed");
                _logger.LogError("   Check the detailed error messages above for the specific failure");
            }
            
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ AUTHENTICATION FAILED: Exception during authentication");
            _logger.LogError("   Exception type: {Type}", ex.GetType().Name);
            _logger.LogError("   Exception message: {Msg}", ex.Message);
            _logger.LogError("   Stack trace: {Stack}", ex.StackTrace);
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
            _logger.LogError("❌ API KEY LOGIN FAILED: ClientId and ClientSecret are required");
            _logger.LogError("   Required environment variables:");
            _logger.LogError("   - BW_CLIENTID (currently: {Status})", string.IsNullOrEmpty(_config.ClientId) ? "NOT SET" : "SET");
            _logger.LogError("   - BW_CLIENTSECRET (currently: {Status})", string.IsNullOrEmpty(_config.ClientSecret) ? "NOT SET" : "SET");
            return false;
        }

        _logger.LogDebug("Starting API key login (ClientId configured: {HasId}, ClientSecret configured: {HasSecret})", 
            !string.IsNullOrEmpty(_config.ClientId), !string.IsNullOrEmpty(_config.ClientSecret));

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
            _logger.LogError("❌ API KEY LOGIN FAILED: bw login timed out after 60 seconds");
            _logger.LogError("   Possible causes:");
            _logger.LogError("   - Network issues connecting to Vaultwarden server");
            _logger.LogError("   - Vaultwarden server URL incorrect: {ServerUrl}", _config.ServerUrl ?? "(not set)");
            _logger.LogError("   - Vaultwarden server is down or unresponsive");
            try { process.Kill(); } catch { }
            return false;
        }

        await exitTask;
        var stdout = await outputTask;
        var stderr = await errorTask;

        // Check if already logged in - if so, logout and retry
        if (process.ExitCode != 0 && stderr?.Contains("You are already logged in") == true)
        {
            _logger.LogDebug("Already logged in - logging out and retrying");
            await LogoutAsync();
            await Task.Delay(Constants.Delays.PostCommandDelayMs);
            
            // Retry login
            return await AuthenticateWithApiKeyAsync();
        }
        
        // Check for "Expected user never made active" error - bw CLI race condition
        if (process.ExitCode != 0 && stderr?.Contains("Expected user never made active") == true)
        {
            _logger.LogWarning("⚠️  bw CLI race condition detected - waiting and retrying authentication");
            await Task.Delay(2000); // Wait 2 seconds for CLI to stabilize
            await LogoutAsync();
            await Task.Delay(Constants.Delays.PostCommandDelayMs);
            
            // Retry login
            return await AuthenticateWithApiKeyAsync();
        }

        if (process.ExitCode == 0)
        {
            // Log exactly what the login command returned
            _logger.LogDebug("bw login stdout: '{Stdout}'", stdout ?? "null");
            _logger.LogDebug("bw login stderr: '{Stderr}'", stderr ?? "null");

            // Give the CLI extra time to write authentication state to disk and initialize user
            // bw CLI 2025.10.0 needs more time to make user active
            _logger.LogDebug("Waiting for bw CLI to stabilize after login...");
            await Task.Delay(Constants.Delays.PostUnlockDelayMs + 1000);
            await LogBwStatusAsync("post-api-login");

            // For API key authentication, always check vault status and unlock if needed
            _logger.LogInformation("✅ API key login succeeded - now checking vault status and unlocking...");
            var unlockSuccess = await TestVaultAccessAsync();
            
            // Only set authenticated flag AFTER successful unlock
            if (unlockSuccess)
            {
                _isAuthenticated = true;
                _logger.LogInformation("✅ Authentication and unlock completed successfully");
            }
            else
            {
                _isAuthenticated = false;
                _logger.LogError("❌ API key login succeeded but vault unlock failed");
                _logger.LogError("   See unlock error messages above for details");
            }
            
            return unlockSuccess;
        }

        // Login failed - provide detailed diagnostics
        _logger.LogError("❌ API KEY LOGIN FAILED: bw login returned exit code {Code}", process.ExitCode);
        _logger.LogError("   stderr: {Stderr}", string.IsNullOrEmpty(stderr) ? "(empty)" : stderr);
        _logger.LogError("   stdout: {Stdout}", string.IsNullOrEmpty(stdout) ? "(empty)" : stdout);
        
        // Analyze the error
        if (!string.IsNullOrEmpty(stderr))
        {
            if (stderr.Contains("Invalid credentials") || stderr.Contains("authentication"))
            {
                _logger.LogError("   ⚠️  DIAGNOSIS: Invalid API key credentials");
                _logger.LogError("   Action required:");
                _logger.LogError("   1. Verify BW_CLIENTID and BW_CLIENTSECRET are correct");
                _logger.LogError("   2. Regenerate API key in Vaultwarden if needed");
                _logger.LogError("   3. Ensure the API key has not been revoked");
            }
            else if (stderr.Contains("server") || stderr.Contains("network") || stderr.Contains("connection"))
            {
                _logger.LogError("   ⚠️  DIAGNOSIS: Network or server connection issue");
                _logger.LogError("   Action required:");
                _logger.LogError("   1. Verify VAULTWARDEN__SERVERURL is correct: {ServerUrl}", _config.ServerUrl ?? "(not set)");
                _logger.LogError("   2. Check network connectivity to Vaultwarden server");
                _logger.LogError("   3. Verify Vaultwarden server is running and accessible");
            }
        }
        
        _isAuthenticated = false;
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

            // Verify master password is configured
            if (string.IsNullOrWhiteSpace(_config.MasterPassword))
            {
                _logger.LogError("❌ UNLOCK FAILED: Master password is not configured");
                _logger.LogError("   Required environment variable: VAULTWARDEN__MASTERPASSWORD");
                _logger.LogError("   Please set this variable and restart the service");
                return false;
            }

            _logger.LogDebug("Master password is configured (length: {Len} characters)", _config.MasterPassword.Length);

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
                _logger.LogDebug("Using session token for unlock (length: {Len})", _sessionToken.Length);
            }
            else
            {
                _logger.LogWarning("No session token available before unlock attempt");
            }

            _logger.LogDebug("Starting bw unlock process...");
            process.Start();
            
            // Write password to stdin (original working approach)
            try
            {
                await process.StandardInput.WriteLineAsync(_config.MasterPassword);
                await process.StandardInput.FlushAsync();
                process.StandardInput.Close();
                _logger.LogDebug("Master password written to bw stdin successfully");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError("❌ UNLOCK FAILED: Could not write master password to bw stdin");
                _logger.LogError("   Error: {Msg}", ex.Message);
                _logger.LogError("   This usually indicates the bw process died immediately or stdin is closed");
                return false;
            }

            // Read output/error concurrently with timeout
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
            var completedTask = await Task.WhenAny(exitTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                _logger.LogError("❌ UNLOCK FAILED: bw unlock timed out after 60 seconds");
                _logger.LogError("   The vault unlock process hung or is taking too long");
                _logger.LogError("   Possible causes:");
                _logger.LogError("   - Network issues connecting to Vaultwarden server");
                _logger.LogError("   - Vaultwarden server is slow or unresponsive");
                _logger.LogError("   - The vault is in an inconsistent state");
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
                    
                    // Set BW_SESSION for both current process and user environment
                    // ApplyCommonEnv will copy this to each child process
                    Environment.SetEnvironmentVariable("BW_SESSION", token);
                    Environment.SetEnvironmentVariable("BW_SESSION", token, EnvironmentVariableTarget.Process);
                    
                    _logger.LogInformation("✅ Vault unlocked successfully - session token captured (length: {Len})", len);
                    await LogBwStatusAsync("post-unlock");
                    return true;
                }
                else
                {
                    _logger.LogError("❌ UNLOCK FAILED: Process succeeded but no session token returned");
                    _logger.LogError("   stdout: '{Stdout}'", string.IsNullOrEmpty(stdOut) ? "(empty)" : stdOut);
                    _logger.LogError("   stderr: '{Stderr}'", string.IsNullOrEmpty(stdErr) ? "(empty)" : stdErr);
                    _logger.LogError("   This indicates the unlock command completed but didn't return a token");
                    
                    // Try to verify vault is accessible by checking status
                    await Task.Delay(Constants.Delays.PostUnlockDelayMs);
                    await LogBwStatusAsync("post-unlock-no-token");
                    
                    _logger.LogError("   Cannot proceed without session token");
                    return false;
                }
            }

            // Unlock failed - provide detailed diagnostics
            _logger.LogError("❌ UNLOCK FAILED: bw unlock returned exit code {Code}", process.ExitCode);
            _logger.LogError("   stderr: {Stderr}", string.IsNullOrEmpty(stdErr) ? "(empty)" : stdErr);
            _logger.LogError("   stdout: {Stdout}", string.IsNullOrEmpty(stdOut) ? "(empty)" : stdOut);
            
            // Analyze the error and provide specific guidance
            if (!string.IsNullOrEmpty(stdErr))
            {
                if (stdErr.Contains("Invalid master password"))
                {
                    _logger.LogError("   ⚠️  DIAGNOSIS: The master password is incorrect");
                    _logger.LogError("   Action required:");
                    _logger.LogError("   1. Verify the password in VAULTWARDEN__MASTERPASSWORD matches your Vaultwarden account");
                    _logger.LogError("   2. Check for special characters that might need escaping");
                    _logger.LogError("   3. Ensure there are no trailing newlines or spaces in the password");
                    _logger.LogError("   4. Try logging in manually: echo 'your-password' | bw unlock --raw");
                }
                else if (stdErr.Contains("not logged in"))
                {
                    _logger.LogError("   ⚠️  DIAGNOSIS: The vault is not logged in");
                    _logger.LogError("   This should not happen - authentication completed but vault not logged in");
                    _logger.LogError("   This indicates a bw CLI state issue");
                }
                else if (stdErr.Contains("locked") || stdErr.Contains("Vault is locked"))
                {
                    _logger.LogError("   ⚠️  DIAGNOSIS: The vault is locked (this is expected before unlock)");
                    _logger.LogError("   But unlock failed for an unknown reason");
                }
                else
                {
                    _logger.LogError("   ⚠️  DIAGNOSIS: Unknown unlock error");
                    _logger.LogError("   The error message above may provide clues");
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ UNLOCK FAILED: Exception during vault unlock");
            _logger.LogError("   Exception type: {Type}", ex.GetType().Name);
            _logger.LogError("   Exception message: {Msg}", ex.Message);
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
            _logger.LogDebug("=== GetItemsInternalAsync START ===");
            
            // Validate session before fetching
            if (string.IsNullOrWhiteSpace(_sessionToken))
            {
                _logger.LogWarning("No session token available - cannot fetch items");
                return new List<VaultwardenItem>();
            }

            // Sync vault to get latest changes from server
            // This is critical for detecting updates made to secrets in Vaultwarden
            _logger.LogDebug("Syncing vault to fetch latest changes from server...");
            var syncSuccess = await SyncVaultAsync();
            if (!syncSuccess)
            {
                _logger.LogWarning("Vault sync failed or had issues - attempting to list items anyway");
                // Don't fail completely - try to list items even if sync failed
            }
            else
            {
                _logger.LogDebug("Vault sync completed successfully");
            }

            _logger.LogDebug("Fetching items from Vaultwarden...");
            _logger.LogDebug("Session token for list items: {HasToken} (length: {Len})",
                !string.IsNullOrWhiteSpace(_sessionToken),
                string.IsNullOrWhiteSpace(_sessionToken) ? 0 : _sessionToken!.Length);

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

            if (string.IsNullOrWhiteSpace(_sessionToken))
            {
                _logger.LogDebug("No session token available for 'bw list items'. This may cause prompts or failures.");
            }
            else
            {
                _logger.LogDebug("Using --session parameter for 'bw list items'");
            }

            // Log command for debugging (without exposing the actual session token)
            var safeArgs = arguments.Contains("--session") 
                ? arguments.Substring(0, arguments.IndexOf("--session")) + "--session [REDACTED]" 
                : arguments;
            _logger.LogDebug("Executing command: bw {Args}", safeArgs);
            _logger.LogDebug("Starting 'bw list items' process...");

            process.Start();
            _logger.LogDebug("Process started, waiting for completion...");
            
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
            await exitTask; // Ensure we get the actual exit code
            output = await outputTask;
            error = await errorTask;
            
            _logger.LogDebug("'bw list items' process completed with exit code: {ExitCode}", process.ExitCode);
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
            _logger.LogDebug("Logged out from Vaultwarden");
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
            _logger.LogDebug("Starting vault sync...");
            
            // Check if we have a session token - sync requires it
            if (string.IsNullOrWhiteSpace(_sessionToken))
            {
                _logger.LogWarning("Cannot sync vault - no session token available. Vault may not be unlocked.");
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

            _logger.LogDebug("bw sync: session token available = {HasToken} (length: {Len})", 
                !string.IsNullOrWhiteSpace(_sessionToken), _sessionToken?.Length ?? 0);
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
                    // Mark as not authenticated to trigger re-auth on next call
                    _isAuthenticated = false;
                    _sessionToken = null;
                    return false;
                }
                
                // Non-auth related error - log but don't invalidate session
                _logger.LogDebug("Sync failed but continuing with current session");
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
                _logger.LogTrace("Using bw data directory: {DataDir}", _config.DataDirectory);
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