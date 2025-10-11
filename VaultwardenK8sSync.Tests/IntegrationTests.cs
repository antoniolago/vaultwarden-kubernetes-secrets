using Microsoft.Extensions.Logging;
using Moq;
using VaultwardenK8sSync.Models;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Configuration;
using Xunit;
using System.Reflection;
using FieldInfo = VaultwardenK8sSync.Models.FieldInfo;

namespace VaultwardenK8sSync.Tests;

[Collection("Integration Tests")]
[Trait("Category", "Integration")]
public class IntegrationTests
{
    private readonly SyncService _syncService;
    private readonly Mock<ILogger<SyncService>> _loggerMock;
    private readonly Mock<IVaultwardenService> _vaultwardenServiceMock;
    private readonly Mock<IKubernetesService> _kubernetesServiceMock;
    private readonly Mock<IMetricsService> _metricsServiceMock;
    private readonly SyncSettings _syncConfig;

    public IntegrationTests()
    {
        _loggerMock = new Mock<ILogger<SyncService>>();
        _vaultwardenServiceMock = new Mock<IVaultwardenService>();
        _kubernetesServiceMock = new Mock<IKubernetesService>();
        _metricsServiceMock = new Mock<IMetricsService>();
        _syncConfig = new SyncSettings();
        
        _syncService = new SyncService(
            _loggerMock.Object,
            _vaultwardenServiceMock.Object,
            _kubernetesServiceMock.Object,
            _metricsServiceMock.Object,
            _syncConfig);
    }

    // Helper methods to access private methods for testing
    private async Task<Dictionary<string, string>> ExtractSecretDataAsync(VaultwardenItem item)
    {
        var method = typeof(SyncService).GetMethod("ExtractSecretDataAsync", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (Dictionary<string, string>)await (Task<Dictionary<string, string>>)method!.Invoke(_syncService, new object[] { item })!;
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "Case Preservation")]
    public async Task ExtractSecretDataAsync_WithUpperCaseCustomFields_ShouldPreserveCase()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1, // Login
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "SMTP_PASSWORD", Value = "smtp123", Type = 0 },
                new FieldInfo { Name = "API_KEY", Value = "api123", Type = 0 },
                new FieldInfo { Name = "DATABASE_HOST", Value = "localhost", Type = 0 },
                new FieldInfo { Name = "JWT_SECRET", Value = "jwt123", Type = 0 },
                new FieldInfo { Name = "REDIS_PASSWORD", Value = "redis123", Type = 0 },
                new FieldInfo { Name = "POSTGRES_DB", Value = "mydb", Type = 0 },
                new FieldInfo { Name = "MYSQL_ROOT_PASSWORD", Value = "mysql123", Type = 0 },
                new FieldInfo { Name = "MONGODB_URI", Value = "mongodb://localhost", Type = 0 },
                new FieldInfo { Name = "ELASTICSEARCH_PASSWORD", Value = "es123", Type = 0 },
                new FieldInfo { Name = "KAFKA_BROKER_PASSWORD", Value = "kafka123", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        Assert.True(result.ContainsKey("SMTP_PASSWORD"), "Should contain SMTP_PASSWORD key");
        Assert.True(result.ContainsKey("API_KEY"), "Should contain API_KEY key");
        Assert.True(result.ContainsKey("DATABASE_HOST"), "Should contain DATABASE_HOST key");
        Assert.True(result.ContainsKey("JWT_SECRET"), "Should contain JWT_SECRET key");
        Assert.True(result.ContainsKey("REDIS_PASSWORD"), "Should contain REDIS_PASSWORD key");
        Assert.True(result.ContainsKey("POSTGRES_DB"), "Should contain POSTGRES_DB key");
        Assert.True(result.ContainsKey("MYSQL_ROOT_PASSWORD"), "Should contain MYSQL_ROOT_PASSWORD key");
        Assert.True(result.ContainsKey("MONGODB_URI"), "Should contain MONGODB_URI key");
        Assert.True(result.ContainsKey("ELASTICSEARCH_PASSWORD"), "Should contain ELASTICSEARCH_PASSWORD key");
        Assert.True(result.ContainsKey("KAFKA_BROKER_PASSWORD"), "Should contain KAFKA_BROKER_PASSWORD key");

        // Verify values
        Assert.Equal("smtp123", result["SMTP_PASSWORD"]);
        Assert.Equal("api123", result["API_KEY"]);
        Assert.Equal("localhost", result["DATABASE_HOST"]);
        Assert.Equal("jwt123", result["JWT_SECRET"]);
        Assert.Equal("redis123", result["REDIS_PASSWORD"]);
        Assert.Equal("mydb", result["POSTGRES_DB"]);
        Assert.Equal("mysql123", result["MYSQL_ROOT_PASSWORD"]);
        Assert.Equal("mongodb://localhost", result["MONGODB_URI"]);
        Assert.Equal("es123", result["ELASTICSEARCH_PASSWORD"]);
        Assert.Equal("kafka123", result["KAFKA_BROKER_PASSWORD"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "Case Preservation")]
    public async Task ExtractSecretDataAsync_WithMixedCaseCustomFields_ShouldPreserveCase()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1, // Login
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "SmtpPassword", Value = "smtp123", Type = 0 },
                new FieldInfo { Name = "ApiKey", Value = "api123", Type = 0 },
                new FieldInfo { Name = "DatabaseHost", Value = "localhost", Type = 0 },
                new FieldInfo { Name = "JwtSecret", Value = "jwt123", Type = 0 },
                new FieldInfo { Name = "RedisPassword", Value = "redis123", Type = 0 },
                new FieldInfo { Name = "PostgresDb", Value = "mydb", Type = 0 },
                new FieldInfo { Name = "MysqlRootPassword", Value = "mysql123", Type = 0 },
                new FieldInfo { Name = "MongodbUri", Value = "mongodb://localhost", Type = 0 },
                new FieldInfo { Name = "ElasticsearchPassword", Value = "es123", Type = 0 },
                new FieldInfo { Name = "KafkaBrokerPassword", Value = "kafka123", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        Assert.True(result.ContainsKey("SmtpPassword"), "Should contain SmtpPassword key");
        Assert.True(result.ContainsKey("ApiKey"), "Should contain ApiKey key");
        Assert.True(result.ContainsKey("DatabaseHost"), "Should contain DatabaseHost key");
        Assert.True(result.ContainsKey("JwtSecret"), "Should contain JwtSecret key");
        Assert.True(result.ContainsKey("RedisPassword"), "Should contain RedisPassword key");
        Assert.True(result.ContainsKey("PostgresDb"), "Should contain PostgresDb key");
        Assert.True(result.ContainsKey("MysqlRootPassword"), "Should contain MysqlRootPassword key");
        Assert.True(result.ContainsKey("MongodbUri"), "Should contain MongodbUri key");
        Assert.True(result.ContainsKey("ElasticsearchPassword"), "Should contain ElasticsearchPassword key");
        Assert.True(result.ContainsKey("KafkaBrokerPassword"), "Should contain KafkaBrokerPassword key");

        // Verify values
        Assert.Equal("smtp123", result["SmtpPassword"]);
        Assert.Equal("api123", result["ApiKey"]);
        Assert.Equal("localhost", result["DatabaseHost"]);
        Assert.Equal("jwt123", result["JwtSecret"]);
        Assert.Equal("redis123", result["RedisPassword"]);
        Assert.Equal("mydb", result["PostgresDb"]);
        Assert.Equal("mysql123", result["MysqlRootPassword"]);
        Assert.Equal("mongodb://localhost", result["MongodbUri"]);
        Assert.Equal("es123", result["ElasticsearchPassword"]);
        Assert.Equal("kafka123", result["KafkaBrokerPassword"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "Case Preservation")]
    public async Task ExtractSecretDataAsync_WithLowerCaseCustomFields_ShouldPreserveCase()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1, // Login
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "smtp_password", Value = "smtp123", Type = 0 },
                new FieldInfo { Name = "api_key", Value = "api123", Type = 0 },
                new FieldInfo { Name = "database_host", Value = "localhost", Type = 0 },
                new FieldInfo { Name = "jwt_secret", Value = "jwt123", Type = 0 },
                new FieldInfo { Name = "redis_password", Value = "redis123", Type = 0 },
                new FieldInfo { Name = "postgres_db", Value = "mydb", Type = 0 },
                new FieldInfo { Name = "mysql_root_password", Value = "mysql123", Type = 0 },
                new FieldInfo { Name = "mongodb_uri", Value = "mongodb://localhost", Type = 0 },
                new FieldInfo { Name = "elasticsearch_password", Value = "es123", Type = 0 },
                new FieldInfo { Name = "kafka_broker_password", Value = "kafka123", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        Assert.True(result.ContainsKey("smtp_password"), "Should contain smtp_password key");
        Assert.True(result.ContainsKey("api_key"), "Should contain api_key key");
        Assert.True(result.ContainsKey("database_host"), "Should contain database_host key");
        Assert.True(result.ContainsKey("jwt_secret"), "Should contain jwt_secret key");
        Assert.True(result.ContainsKey("redis_password"), "Should contain redis_password key");
        Assert.True(result.ContainsKey("postgres_db"), "Should contain postgres_db key");
        Assert.True(result.ContainsKey("mysql_root_password"), "Should contain mysql_root_password key");
        Assert.True(result.ContainsKey("mongodb_uri"), "Should contain mongodb_uri key");
        Assert.True(result.ContainsKey("elasticsearch_password"), "Should contain elasticsearch_password key");
        Assert.True(result.ContainsKey("kafka_broker_password"), "Should contain kafka_broker_password key");

        // Verify values
        Assert.Equal("smtp123", result["smtp_password"]);
        Assert.Equal("api123", result["api_key"]);
        Assert.Equal("localhost", result["database_host"]);
        Assert.Equal("jwt123", result["jwt_secret"]);
        Assert.Equal("redis123", result["redis_password"]);
        Assert.Equal("mydb", result["postgres_db"]);
        Assert.Equal("mysql123", result["mysql_root_password"]);
        Assert.Equal("mongodb://localhost", result["mongodb_uri"]);
        Assert.Equal("es123", result["elasticsearch_password"]);
        Assert.Equal("kafka123", result["kafka_broker_password"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "Case Preservation")]
    public async Task ExtractSecretDataAsync_WithHyphenatedCustomFields_ShouldPreserveCase()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1, // Login
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "SMTP-PASSWORD", Value = "smtp123", Type = 0 },
                new FieldInfo { Name = "API-KEY", Value = "api123", Type = 0 },
                new FieldInfo { Name = "DATABASE-HOST", Value = "localhost", Type = 0 },
                new FieldInfo { Name = "JWT-SECRET", Value = "jwt123", Type = 0 },
                new FieldInfo { Name = "REDIS-PASSWORD", Value = "redis123", Type = 0 },
                new FieldInfo { Name = "smtp-password", Value = "smtp456", Type = 0 },
                new FieldInfo { Name = "api-key", Value = "api456", Type = 0 },
                new FieldInfo { Name = "database-host", Value = "remotehost", Type = 0 },
                new FieldInfo { Name = "jwt-secret", Value = "jwt456", Type = 0 },
                new FieldInfo { Name = "redis-password", Value = "redis456", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        Assert.True(result.ContainsKey("SMTP-PASSWORD"), "Should contain SMTP-PASSWORD key");
        Assert.True(result.ContainsKey("API-KEY"), "Should contain API-KEY key");
        Assert.True(result.ContainsKey("DATABASE-HOST"), "Should contain DATABASE-HOST key");
        Assert.True(result.ContainsKey("JWT-SECRET"), "Should contain JWT-SECRET key");
        Assert.True(result.ContainsKey("REDIS-PASSWORD"), "Should contain REDIS-PASSWORD key");
        Assert.True(result.ContainsKey("smtp-password"), "Should contain smtp-password key");
        Assert.True(result.ContainsKey("api-key"), "Should contain api-key key");
        Assert.True(result.ContainsKey("database-host"), "Should contain database-host key");
        Assert.True(result.ContainsKey("jwt-secret"), "Should contain jwt-secret key");
        Assert.True(result.ContainsKey("redis-password"), "Should contain redis-password key");

        // Verify values
        Assert.Equal("smtp123", result["SMTP-PASSWORD"]);
        Assert.Equal("api123", result["API-KEY"]);
        Assert.Equal("localhost", result["DATABASE-HOST"]);
        Assert.Equal("jwt123", result["JWT-SECRET"]);
        Assert.Equal("redis123", result["REDIS-PASSWORD"]);
        Assert.Equal("smtp456", result["smtp-password"]);
        Assert.Equal("api456", result["api-key"]);
        Assert.Equal("remotehost", result["database-host"]);
        Assert.Equal("jwt456", result["jwt-secret"]);
        Assert.Equal("redis456", result["redis-password"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "Case Preservation")]
    public async Task ExtractSecretDataAsync_WithDottedCustomFields_ShouldPreserveCase()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1, // Login
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "SMTP.PASSWORD", Value = "smtp123", Type = 0 },
                new FieldInfo { Name = "API.KEY", Value = "api123", Type = 0 },
                new FieldInfo { Name = "DATABASE.HOST", Value = "localhost", Type = 0 },
                new FieldInfo { Name = "JWT.SECRET", Value = "jwt123", Type = 0 },
                new FieldInfo { Name = "REDIS.PASSWORD", Value = "redis123", Type = 0 },
                new FieldInfo { Name = "smtp.password", Value = "smtp456", Type = 0 },
                new FieldInfo { Name = "api.key", Value = "api456", Type = 0 },
                new FieldInfo { Name = "database.host", Value = "remotehost", Type = 0 },
                new FieldInfo { Name = "jwt.secret", Value = "jwt456", Type = 0 },
                new FieldInfo { Name = "redis.password", Value = "redis456", Type = 0 },
                new FieldInfo { Name = "app.config.database.host", Value = "config123", Type = 0 },
                new FieldInfo { Name = "service.auth.jwt.secret", Value = "auth123", Type = 0 },
                new FieldInfo { Name = "api.gateway.key", Value = "gateway123", Type = 0 },
                new FieldInfo { Name = "redis.cache.password", Value = "cache123", Type = 0 },
                new FieldInfo { Name = "postgres.primary.password", Value = "primary123", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        Assert.True(result.ContainsKey("SMTP.PASSWORD"), "Should contain SMTP.PASSWORD key");
        Assert.True(result.ContainsKey("API.KEY"), "Should contain API.KEY key");
        Assert.True(result.ContainsKey("DATABASE.HOST"), "Should contain DATABASE.HOST key");
        Assert.True(result.ContainsKey("JWT.SECRET"), "Should contain JWT.SECRET key");
        Assert.True(result.ContainsKey("REDIS.PASSWORD"), "Should contain REDIS.PASSWORD key");
        Assert.True(result.ContainsKey("smtp.password"), "Should contain smtp.password key");
        Assert.True(result.ContainsKey("api.key"), "Should contain api.key key");
        Assert.True(result.ContainsKey("database.host"), "Should contain database.host key");
        Assert.True(result.ContainsKey("jwt.secret"), "Should contain jwt.secret key");
        Assert.True(result.ContainsKey("redis.password"), "Should contain redis.password key");
        Assert.True(result.ContainsKey("app.config.database.host"), "Should contain app.config.database.host key");
        Assert.True(result.ContainsKey("service.auth.jwt.secret"), "Should contain service.auth.jwt.secret key");
        Assert.True(result.ContainsKey("api.gateway.key"), "Should contain api.gateway.key key");
        Assert.True(result.ContainsKey("redis.cache.password"), "Should contain redis.cache.password key");
        Assert.True(result.ContainsKey("postgres.primary.password"), "Should contain postgres.primary.password key");

        // Verify values
        Assert.Equal("smtp123", result["SMTP.PASSWORD"]);
        Assert.Equal("api123", result["API.KEY"]);
        Assert.Equal("localhost", result["DATABASE.HOST"]);
        Assert.Equal("jwt123", result["JWT.SECRET"]);
        Assert.Equal("redis123", result["REDIS.PASSWORD"]);
        Assert.Equal("smtp456", result["smtp.password"]);
        Assert.Equal("api456", result["api.key"]);
        Assert.Equal("remotehost", result["database.host"]);
        Assert.Equal("jwt456", result["jwt.secret"]);
        Assert.Equal("redis456", result["redis.password"]);
        Assert.Equal("config123", result["app.config.database.host"]);
        Assert.Equal("auth123", result["service.auth.jwt.secret"]);
        Assert.Equal("gateway123", result["api.gateway.key"]);
        Assert.Equal("cache123", result["redis.cache.password"]);
        Assert.Equal("primary123", result["postgres.primary.password"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "Regression")]
    public async Task ExtractSecretDataAsync_RegressionTest_SMTP_PASSWORD_ShouldRemainUpperCase()
    {
        // This is the specific case reported by the user
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1, // Login
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "SMTP_PASSWORD", Value = "smtp123", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        Assert.True(result.ContainsKey("SMTP_PASSWORD"), "Should contain SMTP_PASSWORD key");
        Assert.Equal("smtp123", result["SMTP_PASSWORD"]);
        Assert.NotEqual("smtp_password", result.Keys.FirstOrDefault(k => k == "SMTP_PASSWORD")); // Should NOT be lowercase
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "Metadata Fields")]
    public async Task ExtractSecretDataAsync_WithMetadataFields_ShouldExcludeThem()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1, // Login
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 },
                new FieldInfo { Name = "secret-name", Value = "my-secret", Type = 0 },
                new FieldInfo { Name = "secret-key-password", Value = "db_password", Type = 0 },
                new FieldInfo { Name = "secret-key-username", Value = "db_user", Type = 0 },
                new FieldInfo { Name = "ignore-field", Value = "field1,field2", Type = 0 },
                new FieldInfo { Name = "SMTP_PASSWORD", Value = "smtp123", Type = 0 },
                new FieldInfo { Name = "API_KEY", Value = "api123", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert - Metadata fields should be excluded
        Assert.False(result.ContainsKey("namespaces"), "Should not contain namespaces key");
        Assert.False(result.ContainsKey("secret-name"), "Should not contain secret-name key");
        Assert.False(result.ContainsKey("secret-key-password"), "Should not contain secret-key-password key");
        Assert.False(result.ContainsKey("secret-key-username"), "Should not contain secret-key-username key");
        Assert.False(result.ContainsKey("ignore-field"), "Should not contain ignore-field key");

        // Assert - Regular fields should be included
        Assert.True(result.ContainsKey("SMTP_PASSWORD"), "Should contain SMTP_PASSWORD key");
        Assert.True(result.ContainsKey("API_KEY"), "Should contain API_KEY key");

        // Verify values
        Assert.Equal("smtp123", result["SMTP_PASSWORD"]);
        Assert.Equal("api123", result["API_KEY"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "Username Password Keys")]
    public async Task ExtractSecretDataAsync_WithCustomUsernamePasswordKeys_ShouldUseThem()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "Test Item",
            Type = 1, // Login
            Login = new LoginInfo
            {
                Username = "testuser",
                Password = "testpass"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 },
                new FieldInfo { Name = "secret-key-password", Value = "DB_PASSWORD", Type = 0 },
                new FieldInfo { Name = "secret-key-username", Value = "DB_USER", Type = 0 },
                new FieldInfo { Name = "SMTP_PASSWORD", Value = "smtp123", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        Assert.True(result.ContainsKey("DB_USER"), "Should contain DB_USER key");
        Assert.True(result.ContainsKey("DB_PASSWORD"), "Should contain DB_PASSWORD key");
        Assert.True(result.ContainsKey("SMTP_PASSWORD"), "Should contain SMTP_PASSWORD key");

        // Verify values
        Assert.Equal("testuser", result["DB_USER"]);
        Assert.Equal("testpass", result["DB_PASSWORD"]);
        Assert.Equal("smtp123", result["SMTP_PASSWORD"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "SSH Keys")]
    public async Task ExtractSecretDataAsync_WithSSHKeyItem_ShouldPreserveCase()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "SSH Key",
            Type = 5, // SSH Key
            SshKey = new SshKeyInfo
            {
                PrivateKey = "-----BEGIN PRIVATE KEY-----\nMIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQC7VJTUt9Us8cKB\n-----END PRIVATE KEY-----",
                PublicKey = "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQC7VJTUt9Us8cKB",
                Fingerprint = "SHA256:abcdef1234567890"
            },
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 },
                new FieldInfo { Name = "secret-key-password", Value = "SSH_PRIVATE_KEY", Type = 0 },
                new FieldInfo { Name = "SSH_PUBLIC_KEY", Value = "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQC7VJTUt9Us8cKB", Type = 0 },
                new FieldInfo { Name = "SSH_FINGERPRINT", Value = "SHA256:abcdef1234567890", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        Assert.True(result.ContainsKey("SSH_PRIVATE_KEY"), "Should contain SSH_PRIVATE_KEY key");
        Assert.True(result.ContainsKey("SSH_PUBLIC_KEY"), "Should contain SSH_PUBLIC_KEY key");
        Assert.True(result.ContainsKey("SSH_FINGERPRINT"), "Should contain SSH_FINGERPRINT key");

        // Verify values
        Assert.Contains("-----BEGIN PRIVATE KEY-----", result["SSH_PRIVATE_KEY"]);
        Assert.Equal("ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQC7VJTUt9Us8cKB", result["SSH_PUBLIC_KEY"]);
        Assert.Equal("SHA256:abcdef1234567890", result["SSH_FINGERPRINT"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "Secure Notes")]
    public async Task ExtractSecretDataAsync_WithSecureNote_ShouldPreserveCase()
    {
        // Arrange
        var item = new VaultwardenItem
        {
            Id = "test-id",
            Name = "API Configuration",
            Type = 2, // Secure Note
            Notes = "This is a secure note with API configuration",
            Fields = new List<FieldInfo>
            {
                new FieldInfo { Name = "namespaces", Value = "production", Type = 0 },
                new FieldInfo { Name = "API_ENDPOINT", Value = "https://api.example.com", Type = 0 },
                new FieldInfo { Name = "API_VERSION", Value = "v1", Type = 0 },
                new FieldInfo { Name = "API_TIMEOUT", Value = "30", Type = 0 }
            }
        };

        // Act
        var result = await ExtractSecretDataAsync(item);

        // Assert
        Assert.True(result.ContainsKey("API_ENDPOINT"), "Should contain API_ENDPOINT key");
        Assert.True(result.ContainsKey("API_VERSION"), "Should contain API_VERSION key");
        Assert.True(result.ContainsKey("API_TIMEOUT"), "Should contain API_TIMEOUT key");

        // Verify values
        Assert.Equal("https://api.example.com", result["API_ENDPOINT"]);
        Assert.Equal("v1", result["API_VERSION"]);
        Assert.Equal("30", result["API_TIMEOUT"]);
        Assert.Equal("This is a secure note with API configuration", result["API-Configuration"]); // Default key from item name
    }
}
