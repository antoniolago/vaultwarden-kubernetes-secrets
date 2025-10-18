using Microsoft.EntityFrameworkCore;
using VaultwardenK8sSync;
using VaultwardenK8sSync.Database;
using VaultwardenK8sSync.Database.Repositories;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Infrastructure;
using VaultwardenK8sSync.Policies;
using System.Diagnostics;
using System.Threading.RateLimiting;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
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

// Add authentication
var authToken = builder.Configuration.GetValue<string>("AuthToken") ?? "";
var loginlessMode = builder.Configuration.GetValue<bool>("LoginlessMode", false);
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
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10
            }));

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
