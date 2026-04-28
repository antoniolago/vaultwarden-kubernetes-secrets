using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using Xunit;

namespace VaultwardenK8sSync.Tests;

public class WebhookServiceTests
{
    private readonly Mock<ILogger<WebhookService>> _loggerMock;
    private readonly Mock<IVaultwardenService> _vaultwardenServiceMock;
    private readonly Mock<IKubernetesService> _kubernetesServiceMock;
    private readonly Mock<ISyncService> _syncServiceMock;
    private readonly Mock<IMetricsService> _metricsServiceMock;
    private readonly WebhookSettings _webhookSettings;
    private readonly WebhookService _webhookService;

    public WebhookServiceTests()
    {
        _loggerMock = new Mock<ILogger<WebhookService>>();
        _vaultwardenServiceMock = new Mock<IVaultwardenService>();
        _kubernetesServiceMock = new Mock<IKubernetesService>();
            _kubernetesServiceMock.Setup(x => x.IsInitialized).Returns(true);
        _syncServiceMock = new Mock<ISyncService>();
        _metricsServiceMock = new Mock<IMetricsService>();
        _webhookSettings = new WebhookSettings { Secret = "test-secret" };

        _webhookService = new WebhookService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _syncServiceMock.Object,
            _metricsServiceMock.Object,
            _webhookSettings);
    }

    #region ValidateSignature

    [Fact]
    public void ValidateSignature_WithValidSignature_ReturnsTrue()
    {
        var payload = "{\"item_id\":\"123\"}";
        var secret = "test-secret";
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        var signature = BitConverter.ToString(hash).Replace("-", "").ToLower();

        var settings = new WebhookSettings { Secret = secret };
        var service = new WebhookService(
            _loggerMock.Object, _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object, _syncServiceMock.Object,
            _metricsServiceMock.Object, settings);

        var result = service.ValidateSignature(payload, signature);

        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateSignature_WithInvalidSignature_ReturnsFalse()
    {
        var result = _webhookService.ValidateSignature("test-payload", "invalid-signature");
        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateSignature_WithNoSecret_AllowsRequest()
    {
        var settings = new WebhookSettings { Secret = null };
        var service = new WebhookService(
            _loggerMock.Object, _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object, _syncServiceMock.Object,
            _metricsServiceMock.Object, settings);

        var result = service.ValidateSignature("test-payload", "any-signature");

        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateSignature_WithNoSignature_ReturnsFalse()
    {
        var result = _webhookService.ValidateSignature("test-payload", "");
        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateSignature_WithSha256Prefix_HandlesPrefix()
    {
        var payload = "test-payload";
        var secret = "test-secret";
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        var signature = "sha256=" + BitConverter.ToString(hash).Replace("-", "").ToLower();

        var result = _webhookService.ValidateSignature(payload, signature);

        result.Should().BeTrue();
    }

    #endregion

    #region ProcessWebhookAsync

    [Fact]
    public async Task ProcessWebhookAsync_ItemCreated_TriggersSync()
    {
        var webhookEvent = new WebhookEvent
        {
            EventType = WebhookEventTypes.ItemCreated,
            ItemId = "item-123"
        };

        var item = new VaultwardenItem
        {
            Id = "item-123",
            Name = "test-item",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };
        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        
        _syncServiceMock.Setup(x => x.SyncNamespaceAsync("default"))
            .ReturnsAsync(true);

        var result = await _webhookService.ProcessWebhookAsync(webhookEvent);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessWebhookAsync_ItemDeleted_TriggersFullSync()
    {
        var webhookEvent = new WebhookEvent
        {
            EventType = WebhookEventTypes.ItemDeleted,
            ItemId = "item-123"
        };

        var syncSummary = new SyncSummary
        {
            OverallSuccess = true,
            TotalItemsFromVaultwarden = 10
        };
        _syncServiceMock.Setup(x => x.SyncAsync()).ReturnsAsync(syncSummary);

        var result = await _webhookService.ProcessWebhookAsync(webhookEvent);

        result.Success.Should().BeTrue();
        _syncServiceMock.Verify(x => x.SyncAsync(), Times.Once);
    }

    [Fact]
    public async Task ProcessWebhookAsync_UnknownEvent_ReturnsFalse()
    {
        var webhookEvent = new WebhookEvent
        {
            EventType = "unknown_event",
            ItemId = "item-123"
        };

        var result = await _webhookService.ProcessWebhookAsync(webhookEvent);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unknown event type");
    }

    [Fact]
    public async Task ProcessWebhookAsync_SyncFails_ReturnsError()
    {
        var webhookEvent = new WebhookEvent
        {
            EventType = WebhookEventTypes.ItemCreated,
            ItemId = "item-123"
        };

        var item = new VaultwardenItem
        {
            Id = "item-123",
            Name = "test-item",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "default", Type = 0 }
            }
        };
        _vaultwardenServiceMock.Setup(x => x.GetItemsAsync())
            .ReturnsAsync(new List<VaultwardenItem> { item });
        
        _syncServiceMock.Setup(x => x.SyncNamespaceAsync("default"))
            .ReturnsAsync(false);

        var result = await _webhookService.ProcessWebhookAsync(webhookEvent);

        result.Success.Should().BeFalse();
    }

    #endregion
}