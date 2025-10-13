using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;
using VaultwardenK8sSync.Database;
using VaultwardenK8sSync.Database.Repositories;
using VaultwardenK8sSync.Services;

namespace VaultwardenK8sSync.Tests;

public class DatabaseIntegrationTests : IDisposable
{
    private readonly SyncDbContext _context;
    private readonly ISyncLogRepository _syncLogRepository;
    private readonly ISecretStateRepository _secretStateRepository;
    private readonly IDatabaseLoggerService _dbLogger;
    private readonly string _testDbPath;

    public DatabaseIntegrationTests()
    {
        // Create a unique test database
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_sync_{Guid.NewGuid()}.db");
        
        var options = new DbContextOptionsBuilder<SyncDbContext>()
            .UseSqlite($"Data Source={_testDbPath}")
            .Options;
        
        _context = new SyncDbContext(options);
        _context.Database.EnsureCreated();
        
        _syncLogRepository = new SyncLogRepository(_context);
        _secretStateRepository = new SecretStateRepository(_context);
        
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<DatabaseLoggerService>();
        
        _dbLogger = new DatabaseLoggerService(
            logger,
            _context,
            _syncLogRepository,
            _secretStateRepository
        );
    }

    [Fact]
    public async Task StartSyncLog_CreatesNewSyncLogEntry()
    {
        // Arrange & Act
        var syncLogId = await _dbLogger.StartSyncLogAsync("Test Sync", 10);

        // Assert
        Assert.True(syncLogId > 0, "SyncLogId should be greater than 0");
        
        var syncLog = await _syncLogRepository.GetByIdAsync(syncLogId);
        Assert.NotNull(syncLog);
        Assert.Equal("InProgress", syncLog.Status);
        Assert.Equal(10, syncLog.TotalItems);
    }

    [Fact]
    public async Task UpdateSyncProgress_UpdatesSyncLogEntry()
    {
        // Arrange
        var syncLogId = await _dbLogger.StartSyncLogAsync("Test Sync", 10);

        // Act
        await _dbLogger.UpdateSyncProgressAsync(syncLogId, 5, 2, 1, 2, 0);

        // Assert
        var syncLog = await _syncLogRepository.GetByIdAsync(syncLogId);
        Assert.NotNull(syncLog);
        Assert.Equal(5, syncLog.ProcessedItems);
        Assert.Equal(2, syncLog.CreatedSecrets);
        Assert.Equal(1, syncLog.UpdatedSecrets);
        Assert.Equal(2, syncLog.SkippedSecrets);
        Assert.Equal(0, syncLog.FailedSecrets);
    }

    [Fact]
    public async Task CompleteSyncLog_MarksSyncLogAsCompleted()
    {
        // Arrange
        var syncLogId = await _dbLogger.StartSyncLogAsync("Test Sync", 10);
        await _dbLogger.UpdateSyncProgressAsync(syncLogId, 10, 5, 3, 2, 0);

        // Act
        await _dbLogger.CompleteSyncLogAsync(syncLogId, "Success", null);

        // Assert
        var syncLog = await _syncLogRepository.GetByIdAsync(syncLogId);
        Assert.NotNull(syncLog);
        Assert.Equal("Success", syncLog.Status);
        Assert.NotNull(syncLog.EndTime);
        Assert.True(syncLog.DurationSeconds > 0);
    }

    [Fact]
    public async Task CompleteSyncLog_WithError_StoresErrorMessage()
    {
        // Arrange
        var syncLogId = await _dbLogger.StartSyncLogAsync("Test Sync", 10);

        // Act
        await _dbLogger.CompleteSyncLogAsync(syncLogId, "Failed", "Test error message");

        // Assert
        var syncLog = await _syncLogRepository.GetByIdAsync(syncLogId);
        Assert.NotNull(syncLog);
        Assert.Equal("Failed", syncLog.Status);
        Assert.Equal("Test error message", syncLog.ErrorMessage);
    }

    [Fact]
    public async Task UpsertSecretState_CreatesNewSecretState()
    {
        // Act
        await _dbLogger.UpsertSecretStateAsync(
            "test-namespace",
            "test-secret",
            "item-123",
            "Test Item",
            "Active",
            3,
            null
        );

        // Assert
        var secrets = await _secretStateRepository.GetByNamespaceAsync("test-namespace");
        Assert.Single(secrets);
        
        var secret = secrets[0];
        Assert.Equal("test-namespace", secret.Namespace);
        Assert.Equal("test-secret", secret.SecretName);
        Assert.Equal("item-123", secret.VaultwardenItemId);
        Assert.Equal("Test Item", secret.VaultwardenItemName);
        Assert.Equal("Active", secret.Status);
        Assert.Equal(3, secret.DataKeysCount);
        Assert.Null(secret.LastError);
    }

    [Fact]
    public async Task UpsertSecretState_UpdatesExistingSecretState()
    {
        // Arrange
        await _dbLogger.UpsertSecretStateAsync(
            "test-namespace",
            "test-secret",
            "item-123",
            "Test Item",
            "Active",
            3,
            null
        );

        // Act - Update the same secret
        await _dbLogger.UpsertSecretStateAsync(
            "test-namespace",
            "test-secret",
            "item-123",
            "Test Item Updated",
            "Active",
            5,
            null
        );

        // Assert
        var secrets = await _secretStateRepository.GetByNamespaceAsync("test-namespace");
        Assert.Single(secrets); // Should still be just one record
        
        var secret = secrets[0];
        Assert.Equal("Test Item Updated", secret.VaultwardenItemName);
        Assert.Equal(5, secret.DataKeysCount);
    }

    [Fact]
    public async Task GetStatistics_ReturnsCorrectCounts()
    {
        // Arrange
        var syncLogId1 = await _dbLogger.StartSyncLogAsync("Test Sync 1", 10);
        await _dbLogger.UpdateSyncProgressAsync(syncLogId1, 10, 5, 3, 2, 0);
        await _dbLogger.CompleteSyncLogAsync(syncLogId1, "Success", null);

        var syncLogId2 = await _dbLogger.StartSyncLogAsync("Test Sync 2", 8);
        await _dbLogger.UpdateSyncProgressAsync(syncLogId2, 8, 2, 1, 4, 1);
        await _dbLogger.CompleteSyncLogAsync(syncLogId2, "Failed", "Test error");

        // Act
        var stats = await _syncLogRepository.GetStatisticsAsync();

        // Assert
        Assert.Equal(2, (int)stats["totalSyncs"]);
        Assert.Equal(1, (int)stats["successfulSyncs"]);
        Assert.Equal(1, (int)stats["failedSyncs"]);
    }

    public void Dispose()
    {
        _context?.Dispose();
        
        // Clean up test database
        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }
}
