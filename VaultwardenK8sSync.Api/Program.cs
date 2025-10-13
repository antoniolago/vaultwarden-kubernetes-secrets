using Microsoft.EntityFrameworkCore;
using VaultwardenK8sSync;
using VaultwardenK8sSync.Database;
using VaultwardenK8sSync.Database.Repositories;
using VaultwardenK8sSync.Configuration;
using VaultwardenK8sSync.Services;
using VaultwardenK8sSync.Infrastructure;
using System.Diagnostics;

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

// Add Vaultwarden service and dependencies
var appSettings = AppSettings.FromEnvironment();
builder.Services.AddSingleton(appSettings.Vaultwarden);
builder.Services.AddScoped<IProcessFactory, ProcessFactory>();
builder.Services.AddScoped<IProcessRunner, ProcessRunner>();
builder.Services.AddScoped<IVaultwardenService, VaultwardenService>();

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
builder.Services.AddSingleton(new AuthenticationConfig { Token = authToken });
builder.Services.AddScoped<TokenAuthenticationMiddleware>();

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<SyncDbContext>();

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowDashboard");
app.UseMiddleware<TokenAuthenticationMiddleware>();
app.UseAuthorization();
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
