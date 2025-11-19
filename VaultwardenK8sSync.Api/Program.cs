using Microsoft.EntityFrameworkCore;
using VaultwardenK8sSync;
using VaultwardenK8sSync.Database;
using VaultwardenK8sSync.Database.Repositories;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Infrastructure;
using VaultwardenK8sSync.Policies;
using VaultwardenK8sSync.Api.Converters;
using System.Diagnostics;
using System.Threading.RateLimiting;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Ensure DateTime values are serialized with UTC timezone indicator ('Z')
        // This fixes the "Last Sync" column timezone display issue in the dashboard
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        // Set DateTime format to include timezone
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { 
        Title = "Vaultwarden K8s Sync API", 
        Version = "v1",
        Description = "API for monitoring and managing Vaultwarden to Kubernetes secret synchronization"
    });
});

// Configure database
var dbPath = builder.Configuration.GetValue<string>("DatabasePath") ?? "/data/sync.db";

// Ensure database directory exists
var dbDirectory = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
{
    Directory.CreateDirectory(dbDirectory);
}

builder.Services.AddDbContext<SyncDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Register repositories
builder.Services.AddScoped<ISyncLogRepository, SyncLogRepository>();
builder.Services.AddScoped<ISecretStateRepository, SecretStateRepository>();
builder.Services.AddScoped<IVaultwardenItemRepository, VaultwardenItemRepository>();

// Add HTTP client with resilience policies
builder.Services.AddHttpClient("VaultwardenClient")
    .AddPolicyHandler(ResiliencePolicies.GetRetryPolicy())
    .AddPolicyHandler(ResiliencePolicies.GetCircuitBreakerPolicy())
    .AddPolicyHandler(ResiliencePolicies.GetTimeoutPolicy());

// Add Vaultwarden and Kubernetes services
var appSettings = AppSettings.FromEnvironment();
builder.Services.Configure<AppSettings>(options =>
{
    options.Vaultwarden = appSettings.Vaultwarden;
    options.Kubernetes = appSettings.Kubernetes;
    options.Sync = appSettings.Sync;
    options.Logging = appSettings.Logging;
    options.Metrics = appSettings.Metrics;
    options.Webhook = appSettings.Webhook;
});
builder.Services.AddSingleton(appSettings.Vaultwarden);
builder.Services.AddSingleton(appSettings.Kubernetes);
// Make these singleton to work with VaultwardenService singleton
builder.Services.AddSingleton<IProcessFactory, ProcessFactory>();
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
// Make VaultwardenService singleton to preserve authentication state across requests
builder.Services.AddSingleton<IVaultwardenService, VaultwardenService>();
// Make KubernetesService singleton to preserve client connection across requests
builder.Services.AddSingleton<IKubernetesService, KubernetesService>();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowDashboard", policy =>
    {
        var dashboardUrl = builder.Configuration.GetValue<string>("DashboardUrl") ?? "http://localhost:3000";
        
        if (builder.Environment.IsDevelopment())
        {
            // Allow any origin in development for easier testing
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            // Strict CORS in production
            policy.WithOrigins(dashboardUrl)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

// Add WebSocket support
var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromMinutes(2)
};
builder.Services.AddSingleton(webSocketOptions);

// Add authentication
var authToken = Environment.GetEnvironmentVariable("AUTH_TOKEN") ?? "";
var loginlessMode = builder.Configuration.GetValue<bool>("LoginlessMode", false);

// If no token in environment and not in loginless mode, try to load from Kubernetes secret
if (string.IsNullOrEmpty(authToken) && !loginlessMode)
{
    try
    {
        var kubernetesService = new KubernetesService(
            new Microsoft.Extensions.Logging.LoggerFactory().CreateLogger<KubernetesService>(),
            appSettings.Kubernetes
        );
        
        if (await kubernetesService.InitializeAsync())
        {
            var secretNamespace = Environment.GetEnvironmentVariable("APP_NAMESPACE") 
                ?? "vaultwarden-kubernetes-secrets";
            var secretName = "vaultwarden-kubernetes-secrets-token";
            
            if (await kubernetesService.SecretExistsAsync(secretNamespace, secretName))
            {
                var secretData = await kubernetesService.GetSecretDataAsync(secretNamespace, secretName);
                if (secretData != null && secretData.ContainsKey("token"))
                {
                    authToken = secretData["token"];
                    Console.WriteLine($"✅ Loaded auth token from Kubernetes secret {secretName} in namespace {secretNamespace}");
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️  Could not load auth token from Kubernetes: {ex.Message}");
    }
}

builder.Services.AddSingleton(new AuthenticationConfig 
{ 
    Token = authToken,
    LoginlessMode = loginlessMode
});
builder.Services.AddScoped<TokenAuthenticationMiddleware>();

// Log authentication mode on startup
if (loginlessMode)
{
    Console.WriteLine("⚠️  LOGINLESS_MODE is enabled - API authentication is DISABLED");
}
else if (string.IsNullOrEmpty(authToken))
{
    Console.WriteLine("⚠️  No AuthToken configured - API authentication is DISABLED");
}
else
{
    Console.WriteLine("🔒 API authentication is enabled");
}

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<SyncDbContext>();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    // Global rate limiter with authentication-aware logic
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        // Get IP address for rate limiting partition
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        // Stricter limits for authentication endpoints (5 req/min)
        // More relaxed for other endpoints (100 req/min)
        var isAuthEndpoint = context.Request.Path.StartsWithSegments("/api") && 
                            !context.Request.Path.StartsWithSegments("/health");
        
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"{ipAddress}:{isAuthEndpoint}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = isAuthEndpoint ? 20 : 100,  // Stricter for API endpoints
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = isAuthEndpoint ? 0 : 10  // No queueing for API endpoints
            });
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        await context.HttpContext.Response.WriteAsync("⚠️ Rate limit exceeded. Please try again later.", token);
    };
});

var app = builder.Build();

// Ensure database is created and migrated
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
    await db.Database.EnsureCreatedAsync();
    
    // Clean up orphaned InProgress sync logs from crashed/killed sync service
    try
    {
        var orphanedLogs = db.SyncLogs.Where(s => s.Status == "InProgress").ToList();
        if (orphanedLogs.Any())
        {
            Console.WriteLine($"🧹 Cleaning up {orphanedLogs.Count} orphaned InProgress sync logs...");
            foreach (var log in orphanedLogs)
            {
                log.Status = "Failed";
                log.EndTime = DateTime.UtcNow;
                log.ErrorMessage = "Sync was interrupted (service stopped/crashed before completion)";
                log.DurationSeconds = log.EndTime.HasValue 
                    ? (log.EndTime.Value - log.StartTime).TotalSeconds 
                    : 0;
            }
            await db.SaveChangesAsync();
            Console.WriteLine($"✅ Cleaned up {orphanedLogs.Count} orphaned sync logs");
        }
    }
    catch (Exception cleanupEx)
    {
        Console.WriteLine($"⚠️  Warning: Could not clean up orphaned sync logs: {cleanupEx.Message}");
    }
    
    // Migrate schema: Create VaultwardenItems table if it doesn't exist
    try
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        
        // Check if VaultwardenItems table exists
        using (var checkCmd = connection.CreateCommand())
        {
            checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='VaultwardenItems';";
            var tableExists = await checkCmd.ExecuteScalarAsync();
            
            if (tableExists == null)
            {
                Console.WriteLine("⚙️  Creating VaultwardenItems table...");
                using var createCmd = connection.CreateCommand();
                createCmd.CommandText = @"
                    CREATE TABLE VaultwardenItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ItemId TEXT NOT NULL UNIQUE,
                        Name TEXT NOT NULL,
                        FolderId TEXT,
                        OrganizationId TEXT,
                        OrganizationName TEXT,
                        Owner TEXT,
                        FieldCount INTEGER NOT NULL DEFAULT 0,
                        FieldNamesJson TEXT,
                        Notes TEXT,
                        LastFetched TEXT NOT NULL,
                        HasNamespacesField INTEGER NOT NULL DEFAULT 0,
                        NamespacesJson TEXT
                    );
                    CREATE UNIQUE INDEX IX_VaultwardenItems_ItemId ON VaultwardenItems (ItemId);
                    CREATE INDEX IX_VaultwardenItems_LastFetched ON VaultwardenItems (LastFetched);
                    CREATE INDEX IX_VaultwardenItems_HasNamespacesField ON VaultwardenItems (HasNamespacesField);
                ";
                await createCmd.ExecuteNonQueryAsync();
                Console.WriteLine("✅ VaultwardenItems table created");
            }
        }
        
        // Migrate SyncLogs table: Add sync configuration columns if they don't exist
        try
        {
            using var checkSyncConfigCmd = connection.CreateCommand();
            checkSyncConfigCmd.CommandText = "PRAGMA table_info(SyncLogs);";
            var reader = await checkSyncConfigCmd.ExecuteReaderAsync();
            var columns = new List<string>();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1)); // Column name is at index 1
            }
            await reader.CloseAsync();
            
            if (!columns.Contains("SyncIntervalSeconds"))
            {
                Console.WriteLine("⚙️  Adding SyncIntervalSeconds column to SyncLogs...");
                using var addIntervalCmd = connection.CreateCommand();
                addIntervalCmd.CommandText = "ALTER TABLE SyncLogs ADD COLUMN SyncIntervalSeconds INTEGER NOT NULL DEFAULT 0;";
                await addIntervalCmd.ExecuteNonQueryAsync();
                Console.WriteLine("✅ SyncIntervalSeconds column added");
            }
            
            if (!columns.Contains("ContinuousSync"))
            {
                Console.WriteLine("⚙️  Adding ContinuousSync column to SyncLogs...");
                using var addContinuousCmd = connection.CreateCommand();
                addContinuousCmd.CommandText = "ALTER TABLE SyncLogs ADD COLUMN ContinuousSync INTEGER NOT NULL DEFAULT 0;";
                await addContinuousCmd.ExecuteNonQueryAsync();
                Console.WriteLine("✅ ContinuousSync column added");
            }
        }
        catch (Exception migEx)
        {
            Console.WriteLine($"⚠️  Warning: Could not add sync config columns: {migEx.Message}");
        }
        
        await connection.CloseAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️  Warning: Could not migrate database schema: {ex.Message}");
    }
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowDashboard");

// Enable WebSockets
app.UseWebSockets();

// Rate limiting
app.UseRateLimiter();

app.UseMiddleware<TokenAuthenticationMiddleware>();
app.UseAuthorization();

// Prometheus metrics endpoint
app.MapMetrics();

app.MapControllers();
app.MapHealthChecks("/health");

// Resource monitoring endpoint (async to avoid blocking)
app.MapGet("/api/system/resources", async () =>
{
    var process = Process.GetCurrentProcess();
    var cpuUsage = await GetCpuUsageAsync(process);
    
    return Results.Ok(new
    {
        cpuUsagePercent = cpuUsage,
        memoryMB = process.WorkingSet64 / 1024.0 / 1024.0,
        threadCount = process.Threads.Count,
        timestamp = DateTime.UtcNow
    });
});

static async Task<double> GetCpuUsageAsync(Process process)
{
    var startTime = DateTime.UtcNow;
    var startCpuUsage = process.TotalProcessorTime;
    await Task.Delay(100); // Non-blocking delay
    var endTime = DateTime.UtcNow;
    var endCpuUsage = process.TotalProcessorTime;
    var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
    var totalMsPassed = (endTime - startTime).TotalMilliseconds;
    var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
    return cpuUsageTotal * 100;
}

app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
