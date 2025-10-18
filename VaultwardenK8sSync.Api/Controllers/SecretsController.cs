using Microsoft.AspNetCore.Mvc;
using VaultwardenK8sSync.Database.Models;
using VaultwardenK8sSync.Database.Repositories;
using VaultwardenK8sSync.Services;

namespace VaultwardenK8sSync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SecretsController : ControllerBase
{
    private readonly ISecretStateRepository _repository;
    private readonly IKubernetesService _kubernetesService;
    private readonly ILogger<SecretsController> _logger;

    public SecretsController(
        ISecretStateRepository repository, 
        IKubernetesService kubernetesService,
        ILogger<SecretsController> logger)
    {
        _repository = repository;
        _kubernetesService = kubernetesService;
        _logger = logger;
    }

    /// <summary>
    /// Get all synced secrets
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<SecretState>>> GetAll()
    {
        try
        {
            var secrets = await _repository.GetAllAsync();
            return Ok(secrets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving secrets");
            return StatusCode(500, "Error retrieving secrets");
        }
    }

    /// <summary>
    /// Get active secrets only
    /// </summary>
    [HttpGet("active")]
    public async Task<ActionResult<List<SecretState>>> GetActive()
    {
        try
        {
            var secrets = await _repository.GetActiveSecretsAsync();
            return Ok(secrets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active secrets");
            return StatusCode(500, "Error retrieving active secrets");
        }
    }

    /// <summary>
    /// Get secrets by namespace
    /// </summary>
    [HttpGet("namespace/{namespaceName}")]
    public async Task<ActionResult<List<SecretState>>> GetByNamespace(string namespaceName)
    {
        try
        {
            var secrets = await _repository.GetByNamespaceAsync(namespaceName);
            return Ok(secrets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving secrets for namespace {Namespace}", namespaceName);
            return StatusCode(500, "Error retrieving secrets");
        }
    }

    /// <summary>
    /// Get secret by namespace and name
    /// </summary>
    [HttpGet("namespace/{namespaceName}/name/{secretName}")]
    public async Task<ActionResult<SecretState>> GetByNamespaceAndName(string namespaceName, string secretName)
    {
        try
        {
            var secret = await _repository.GetByNamespaceAndNameAsync(namespaceName, secretName);
            if (secret == null)
                return NotFound();
            
            return Ok(secret);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving secret {Namespace}/{Name}", namespaceName, secretName);
            return StatusCode(500, "Error retrieving secret");
        }
    }

    /// <summary>
    /// Get secrets by namespace and status
    /// </summary>
    [HttpGet("namespace/{namespaceName}/status/{status}")]
    public async Task<ActionResult<List<SecretState>>> GetByNamespaceAndStatus(string namespaceName, string status)
    {
        try
        {
            var allSecrets = await _repository.GetByNamespaceAsync(namespaceName);
            var filtered = allSecrets.Where(s => s.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
            return Ok(filtered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving secrets for namespace {Namespace} with status {Status}", namespaceName, status);
            return StatusCode(500, "Error retrieving secrets");
        }
    }

    /// <summary>
    /// Get data keys for a specific secret
    /// Always returns actual key names from Kubernetes secret
    /// </summary>
    [HttpGet("{namespaceName}/{secretName}/keys")]
    public async Task<ActionResult<List<string>>> GetSecretDataKeys(string namespaceName, string secretName)
    {
        try
        {
            var secret = await _repository.GetByNamespaceAndNameAsync(namespaceName, secretName);
            if (secret == null)
            {
                _logger.LogWarning("Secret {Namespace}/{Name} not found in database", namespaceName, secretName);
                return NotFound($"Secret {namespaceName}/{secretName} not found");
            }

            // Always try to fetch actual keys from Kubernetes (regardless of status)
            try
            {
                // Initialize Kubernetes client if needed
                await _kubernetesService.InitializeAsync();
                
                var secretData = await _kubernetesService.GetSecretDataAsync(namespaceName, secretName);
                if (secretData != null && secretData.Count > 0)
                {
                    var keys = secretData.Keys.ToList();
                    _logger.LogInformation("Returning {Count} actual Kubernetes keys for secret {Namespace}/{Name}", keys.Count, namespaceName, secretName);
                    return Ok(keys);
                }
                else
                {
                    _logger.LogWarning("Secret {Namespace}/{Name} not found in Kubernetes or has no data", namespaceName, secretName);
                    return NotFound($"Secret {namespaceName}/{secretName} not found in Kubernetes");
                }
            }
            catch (Exception k8sEx)
            {
                _logger.LogError(k8sEx, "Could not fetch actual keys from Kubernetes for {Namespace}/{Name}", namespaceName, secretName);
                return StatusCode(500, $"Error fetching keys from Kubernetes: {k8sEx.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving data keys for secret {Namespace}/{Name}", namespaceName, secretName);
            return StatusCode(500, "Error retrieving data keys");
        }
    }
}
