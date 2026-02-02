using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using k8s;
using k8s.Models;
using Spectre.Console;
using Xunit;

namespace VaultwardenK8sSync.E2ETests.Infrastructure;

/// <summary>
/// E2E test fixture that manages the test environment lifecycle:
/// - Kind cluster creation
/// - Vaultwarden deployment
/// - Test user setup
/// - Operator deployment
/// </summary>
public class E2ETestFixture : IAsyncLifetime
{
    public const string ClusterName = "vks-e2e";
    public const string VaultwardenNamespace = "vaultwarden";
    public const string OperatorNamespace = "vaultwarden-kubernetes-secrets";
    public const string TestNamespace1 = "test-ns-1";
    public const string TestNamespace2 = "test-ns-2";
    public const string TestNamespace3 = "test-ns-3";
    
    public const string TestEmail = "e2e-test@vaultwarden.local";
    public const string TestMasterPassword = "MasterPassword123";
    
    private readonly string _projectRoot;
    private bool _clusterCreated;
    
    public IKubernetes? KubernetesClient { get; private set; }
    public string VaultwardenUrl { get; private set; } = "https://localhost:30443";
    public TestCredentials? Credentials { get; private set; }
    public List<TestResult> TestResults { get; } = new();
    
    public E2ETestFixture()
    {
        // Find project root (where VaultwardenK8sSync.sln is)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VaultwardenK8sSync.sln")))
        {
            dir = dir.Parent;
        }
        _projectRoot = dir?.FullName ?? throw new InvalidOperationException("Could not find project root");
    }
    
    public async Task InitializeAsync()
    {
        AnsiConsole.Write(new FigletText("E2E Tests").Color(Color.Blue));
        AnsiConsole.MarkupLine("[blue]Vaultwarden Kubernetes Secrets - E2E Test Suite[/]");
        AnsiConsole.WriteLine();
        
        await SetupCluster();
        await DeployVaultwarden();
        await SetupTestUser();
        await CreateTestItems();
        await DeployOperator();
        
        AnsiConsole.MarkupLine("[green]✓ E2E environment ready[/]");
        AnsiConsole.WriteLine();
    }
    
    public async Task DisposeAsync()
    {
        if (_clusterCreated && Environment.GetEnvironmentVariable("E2E_KEEP_CLUSTER") != "true")
        {
            AnsiConsole.MarkupLine("[yellow]Cleaning up cluster...[/]");
            await RunCommand("kind", $"delete cluster --name {ClusterName}");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Keeping cluster for debugging (E2E_KEEP_CLUSTER=true)[/]");
        }
    }
    
    private async Task SetupCluster()
    {
        await AnsiConsole.Status()
            .StartAsync("Setting up Kind cluster...", async ctx =>
            {
                // Check prerequisites
                ctx.Status("Checking prerequisites...");
                await CheckPrerequisites();
                
                // Delete existing cluster if exists
                ctx.Status("Removing existing cluster if any...");
                await RunCommand("kind", $"delete cluster --name {ClusterName}", throwOnError: false);
                
                // Create cluster config
                var clusterConfig = @"
kind: Cluster
apiVersion: kind.x-k8s.io/v1alpha4
nodes:
- role: control-plane
  extraPortMappings:
  - containerPort: 30443
    hostPort: 30443
    protocol: TCP
";
                var configPath = Path.Combine(Path.GetTempPath(), "kind-config.yaml");
                await File.WriteAllTextAsync(configPath, clusterConfig);
                
                // Create cluster
                ctx.Status("Creating Kind cluster...");
                await RunCommand("kind", $"create cluster --name {ClusterName} --config {configPath}");
                _clusterCreated = true;
                
                // Setup kubectl context
                ctx.Status("Setting kubectl context...");
                await RunCommand("kubectl", $"config use-context kind-{ClusterName}");
                
                // Wait for cluster to be ready
                ctx.Status("Waiting for cluster to be ready...");
                await RunCommand("kubectl", "wait --for=condition=Ready nodes --all --timeout=60s");
                
                // Create namespaces
                ctx.Status("Creating namespaces...");
                var namespaces = new[] { VaultwardenNamespace, OperatorNamespace, TestNamespace1, TestNamespace2, TestNamespace3 };
                foreach (var ns in namespaces)
                {
                    await RunCommand("kubectl", $"create namespace {ns} --dry-run=client -o yaml | kubectl apply -f -", useShell: true);
                }
                
                // Initialize Kubernetes client
                var config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
                KubernetesClient = new Kubernetes(config);
            });
        
        AnsiConsole.MarkupLine("[green]✓ Kind cluster ready[/]");
    }
    
    private async Task DeployVaultwarden()
    {
        await AnsiConsole.Status()
            .StartAsync("Deploying Vaultwarden...", async ctx =>
            {
                // Generate self-signed TLS certificate for Vaultwarden
                ctx.Status("Generating TLS certificate...");
                var certDir = Path.Combine(Path.GetTempPath(), "vks-e2e-certs");
                Directory.CreateDirectory(certDir);
                var keyPath = Path.Combine(certDir, "key.pem");
                var certPath = Path.Combine(certDir, "cert.pem");
                
                await RunCommand("openssl", 
                    $"req -x509 -newkey rsa:4096 -keyout {keyPath} -out {certPath} -days 1 -nodes " +
                    "-subj \"/CN=vaultwarden.vaultwarden.svc.cluster.local\" " +
                    "-addext \"subjectAltName=DNS:vaultwarden,DNS:vaultwarden.vaultwarden,DNS:vaultwarden.vaultwarden.svc,DNS:vaultwarden.vaultwarden.svc.cluster.local,DNS:localhost,IP:127.0.0.1\"");
                
                // Create TLS secret in Kubernetes
                ctx.Status("Creating TLS secret...");
                await RunCommand("kubectl", 
                    $"create secret tls vaultwarden-tls -n {VaultwardenNamespace} " +
                    $"--cert={certPath} --key={keyPath} --dry-run=client -o yaml | kubectl apply -f -", useShell: true);
                
                ctx.Status("Applying Vaultwarden manifests...");
                var manifestPath = Path.Combine(_projectRoot, "tests", "e2e", "manifests", "vaultwarden.yaml");
                await RunCommand("kubectl", $"apply -f {manifestPath}");
                
                ctx.Status("Waiting for Vaultwarden pod...");
                await RunCommand("kubectl", $"wait --for=condition=Ready pod -l app=vaultwarden -n {VaultwardenNamespace} --timeout=180s");
                
                ctx.Status("Waiting for Vaultwarden API...");
                await WaitForUrl($"{VaultwardenUrl}/api/alive", TimeSpan.FromSeconds(30), ignoreSslErrors: true);
            });
        
        AnsiConsole.MarkupLine("[green]✓ Vaultwarden deployed[/]");
    }
    
    private byte[]? _encryptionKey;
    private byte[]? _macKey;
    
    private HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        return new HttpClient(handler);
    }
    
    private async Task SetupTestUser()
    {
        await AnsiConsole.Status()
            .StartAsync("Setting up test user...", async ctx =>
            {
                using var client = CreateHttpClient();
                const int kdfIterations = 600000;
                var masterKey = DeriveKey(TestMasterPassword, TestEmail.ToLowerInvariant(), kdfIterations);
                
                // Try to login first (user might already exist)
                ctx.Status("Attempting login via API...");
                string? accessToken = null;
                string? encryptedKey = null;
                
                try
                {
                    (accessToken, encryptedKey) = await LoginViaApi(client, TestEmail, TestMasterPassword);
                }
                catch
                {
                    // User doesn't exist, need to register
                    ctx.Status("Registering new user via API...");
                    var symKey = GenerateEncryptionKey();
                    await RegisterUserViaApi(TestEmail, TestMasterPassword, symKey);
                    
                    // Store the key we just created
                    _encryptionKey = symKey[..32];
                    _macKey = symKey[32..];
                    
                    // Now login
                    ctx.Status("Logging in after registration...");
                    await Task.Delay(500);
                    (accessToken, encryptedKey) = await LoginViaApi(client, TestEmail, TestMasterPassword);
                }
                
                if (string.IsNullOrEmpty(accessToken))
                {
                    throw new InvalidOperationException("Failed to obtain access token");
                }
                
                // If we didn't just register, decrypt the key from the server
                if (_encryptionKey == null && !string.IsNullOrEmpty(encryptedKey))
                {
                    var symKey = DecryptSymmetricKey(encryptedKey, masterKey);
                    _encryptionKey = symKey[..32];
                    _macKey = symKey[32..];
                }
                
                // Get API key for operator authentication
                ctx.Status("Getting API key...");
                var (clientId, clientSecret) = await GetApiKeyViaApi(client, accessToken, TestMasterPassword);
                
                Credentials = new TestCredentials
                {
                    Email = TestEmail,
                    MasterPassword = TestMasterPassword,
                    SessionKey = accessToken,
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    VaultwardenUrl = VaultwardenUrl,
                    EncryptionKey = _encryptionKey,
                    MacKey = _macKey
                };
            });
        
        AnsiConsole.MarkupLine("[green]✓ Test user ready[/]");
    }
    
    private async Task<(string accessToken, string? encryptedKey)> LoginViaApi(HttpClient client, string email, string masterPassword)
    {
        const int kdfIterations = 600000;
        
        // Derive keys
        var masterKey = DeriveKey(masterPassword, email.ToLowerInvariant(), kdfIterations);
        var masterPasswordHash = Convert.ToBase64String(DeriveKey(masterKey, masterPassword, 1));
        
        // Get token
        var tokenUrl = $"{VaultwardenUrl}/identity/connect/token";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = email,
            ["password"] = masterPasswordHash,
            ["scope"] = "api offline_access",
            ["client_id"] = "cli",
            ["deviceType"] = "8",
            ["deviceIdentifier"] = Guid.NewGuid().ToString(),
            ["deviceName"] = "e2e-test"
        });
        
        var response = await client.PostAsync(tokenUrl, content);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Login failed: {response.StatusCode} - {error}");
        }
        
        var tokenData = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = tokenData.GetProperty("access_token").GetString() 
            ?? throw new InvalidOperationException("No access token in response");
        
        // Get the user's encrypted key
        string? encryptedKey = null;
        if (tokenData.TryGetProperty("Key", out var keyProp))
        {
            encryptedKey = keyProp.GetString();
        }
        
        return (accessToken, encryptedKey);
    }
    
    private async Task<(string clientId, string clientSecret)> GetApiKeyViaApi(HttpClient client, string accessToken, string masterPassword)
    {
        const int kdfIterations = 600000;
        var masterKey = DeriveKey(masterPassword, TestEmail.ToLowerInvariant(), kdfIterations);
        var masterPasswordHash = Convert.ToBase64String(DeriveKey(masterKey, masterPassword, 1));
        
        var apiKeyUrl = $"{VaultwardenUrl}/api/accounts/api-key";
        
        client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        
        var payload = new { masterPasswordHash };
        var response = await client.PostAsJsonAsync(apiKeyUrl, payload);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to get API key: {response.StatusCode} - {error}");
        }
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var apiKeyData = JsonSerializer.Deserialize<JsonElement>(responseContent);
        
        // Vaultwarden returns "ApiKey" (PascalCase) not "apiKey"
        string? apiKey = null;
        if (apiKeyData.TryGetProperty("ApiKey", out var apiKeyProp))
            apiKey = apiKeyProp.GetString();
        else if (apiKeyData.TryGetProperty("apiKey", out var apiKeyPropLower))
            apiKey = apiKeyPropLower.GetString();
        
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException($"No apiKey in response: {responseContent}");
        
        // Get user ID from profile
        var profileUrl = $"{VaultwardenUrl}/api/accounts/profile";
        var profileResponse = await client.GetAsync(profileUrl);
        if (!profileResponse.IsSuccessStatusCode)
        {
            var error = await profileResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to get profile: {profileResponse.StatusCode} - {error}");
        }
        
        var profileContent = await profileResponse.Content.ReadAsStringAsync();
        var profileData = JsonSerializer.Deserialize<JsonElement>(profileContent);
        
        // Try different property names for user ID
        string? userId = null;
        if (profileData.TryGetProperty("Id", out var idProp))
            userId = idProp.GetString();
        else if (profileData.TryGetProperty("id", out var idPropLower))
            userId = idPropLower.GetString();
        else if (profileData.TryGetProperty("_id", out var idPropUnderscore))
            userId = idPropUnderscore.GetString();
        
        if (string.IsNullOrEmpty(userId))
            throw new InvalidOperationException($"No id in profile response: {profileContent}");
        
        return ($"user.{userId}", apiKey);
    }
    
    private async Task RegisterUserViaApi(string email, string masterPassword, byte[] symKey)
    {
        using var client = CreateHttpClient();
        
        var registerUrl = $"{VaultwardenUrl}/api/accounts/register";
        
        // Use default KDF settings (PBKDF2 with 600000 iterations is the Bitwarden default)
        const int kdf = 0; // PBKDF2
        const int kdfIterations = 600000;
        
        // Derive keys using PBKDF2
        var masterKey = DeriveKey(masterPassword, email.ToLowerInvariant(), kdfIterations);
        var masterPasswordHash = DeriveKey(masterKey, masterPassword, 1);
        var protectedKey = ProtectSymmetricKey(symKey, masterKey);
        
        // Build registration payload
        var registerPayload = new
        {
            email,
            masterPasswordHash = Convert.ToBase64String(masterPasswordHash),
            masterPasswordHint = (string?)null,
            name = "E2E Test User",
            key = protectedKey,
            kdf,
            kdfIterations
        };
        
        var response = await client.PostAsJsonAsync(registerUrl, registerPayload);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to register user: {response.StatusCode} - {error}");
        }
    }
    
    private static byte[] DeriveKey(string password, string salt, int iterations)
    {
        using var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(
            password, 
            System.Text.Encoding.UTF8.GetBytes(salt), 
            iterations, 
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32);
    }
    
    private static byte[] DeriveKey(byte[] key, string data, int iterations)
    {
        using var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(
            System.Text.Encoding.UTF8.GetBytes(data),
            key,
            iterations,
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32);
    }
    
    private static byte[] GenerateEncryptionKey()
    {
        var key = new byte[64]; // 32 bytes encryption key + 32 bytes mac key
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        return key;
    }
    
    private static string ProtectSymmetricKey(byte[] encKey, byte[] masterKey)
    {
        // AES-256-CBC encryption with HMAC
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = masterKey;
        aes.GenerateIV();
        aes.Mode = System.Security.Cryptography.CipherMode.CBC;
        aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
        
        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(encKey, 0, encKey.Length);
        
        // Format: 2.{iv}|{encrypted} (type 2 = AES-256-CBC)
        return $"2.{Convert.ToBase64String(aes.IV)}|{Convert.ToBase64String(encrypted)}";
    }
    
    private static byte[] DecryptSymmetricKey(string encryptedKey, byte[] masterKey)
    {
        // Parse format: 2.{iv}|{encrypted}
        var parts = encryptedKey.Split('.');
        if (parts.Length != 2 || parts[0] != "2")
            throw new InvalidOperationException("Invalid encrypted key format");
        
        var dataParts = parts[1].Split('|');
        if (dataParts.Length < 2)
            throw new InvalidOperationException("Invalid encrypted key data format");
        
        var iv = Convert.FromBase64String(dataParts[0]);
        var encrypted = Convert.FromBase64String(dataParts[1]);
        
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = masterKey;
        aes.IV = iv;
        aes.Mode = System.Security.Cryptography.CipherMode.CBC;
        aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
        
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
    }
    
    private string EncryptString(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null!;
        if (_encryptionKey == null || _macKey == null)
            throw new InvalidOperationException("Encryption keys not set");
        
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = _encryptionKey;
        aes.GenerateIV();
        aes.Mode = System.Security.Cryptography.CipherMode.CBC;
        aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
        
        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);
        
        // Compute HMAC
        using var hmac = new System.Security.Cryptography.HMACSHA256(_macKey);
        var macData = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, macData, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, macData, aes.IV.Length, encrypted.Length);
        var mac = hmac.ComputeHash(macData);
        
        // Format: 2.{iv}|{encrypted}|{mac}
        return $"2.{Convert.ToBase64String(aes.IV)}|{Convert.ToBase64String(encrypted)}|{Convert.ToBase64String(mac)}";
    }
    
    private async Task CreateTestItems()
    {
        if (Credentials == null) throw new InvalidOperationException("Credentials not set");
        
        await AnsiConsole.Status()
            .StartAsync("Creating test vault items...", async ctx =>
            {
                using var client = CreateHttpClient();
                client.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Credentials.SessionKey);
                
                var items = GetTestItemDefinitions();
                
                foreach (var item in items)
                {
                    ctx.Status($"Creating: {item.Name}...");
                    try
                    {
                        await CreateVaultItemViaApi(client, item);
                        AnsiConsole.MarkupLine($"  [green]✓[/] {item.Name}");
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"  [red]✗[/] {item.Name}: {ex.Message}");
                    }
                }
            });
        
        AnsiConsole.MarkupLine("[green]✓ Test items created[/]");
    }
    
    private async Task CreateVaultItemViaApi(HttpClient client, TestItemDefinition item)
    {
        var cipherUrl = $"{VaultwardenUrl}/api/ciphers";
        
        // Build cipher object with encrypted fields (Bitwarden requires client-side encryption)
        var cipher = new Dictionary<string, object?>
        {
            ["type"] = (int)item.Type,
            ["name"] = EncryptString(item.Name),
            ["notes"] = EncryptString(item.Notes),
            ["favorite"] = false,
            ["reprompt"] = 0
        };
        
        if (item.Type == ItemType.Login)
        {
            cipher["login"] = new Dictionary<string, object?>
            {
                ["username"] = EncryptString(item.Username),
                ["password"] = EncryptString(item.Password),
                ["uris"] = item.Uris?.Select(u => new { uri = EncryptString(u), match = (int?)null }).ToArray()
            };
        }
        else if (item.Type == ItemType.SecureNote)
        {
            cipher["secureNote"] = new { type = 0 };
        }
        
        if (item.CustomFields?.Count > 0)
        {
            cipher["fields"] = item.CustomFields.Select(f => new
            {
                name = EncryptString(f.Key),
                value = EncryptString(f.Value),
                type = 0 // Text
            }).ToArray();
        }
        
        var response = await client.PostAsJsonAsync(cipherUrl, cipher);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to create item: {response.StatusCode} - {error}");
        }
    }
    
    private async Task DeployOperator()
    {
        if (Credentials == null) throw new InvalidOperationException("Credentials not set");
        
        await AnsiConsole.Status()
            .StartAsync("Deploying operator...", async ctx =>
            {
                // Build Docker image
                ctx.Status("Building operator Docker image...");
                await RunCommand("docker", $"build -f {_projectRoot}/VaultwardenK8sSync/Dockerfile -t vaultwarden-kubernetes-secrets:e2e-test {_projectRoot}");
                
                // Load into kind
                ctx.Status("Loading image into Kind...");
                await RunCommand("kind", $"load docker-image vaultwarden-kubernetes-secrets:e2e-test --name {ClusterName}");
                
                // Create secrets
                ctx.Status("Creating operator secrets...");
                await RunCommand("kubectl", 
                    $"create secret generic vaultwarden-kubernetes-secrets -n {OperatorNamespace} " +
                    $"--from-literal=BW_CLIENTID={Credentials.ClientId} " +
                    $"--from-literal=BW_CLIENTSECRET={Credentials.ClientSecret} " +
                    $"--from-literal=VAULTWARDEN__MASTERPASSWORD={Credentials.MasterPassword} " +
                    $"--from-literal=BW_PASSWORD={Credentials.MasterPassword} " +
                    "--dry-run=client -o yaml | kubectl apply -f -", useShell: true);
                
                // Install via Helm
                ctx.Status("Installing operator via Helm...");
                await RunCommand("helm", 
                    $"upgrade --install vks-e2e {_projectRoot}/charts/vaultwarden-kubernetes-secrets " +
                    $"--namespace {OperatorNamespace} " +
                    "--set image.repository=vaultwarden-kubernetes-secrets " +
                    "--set image.tag=e2e-test " +
                    "--set image.pullPolicy=Never " +
                    $"--set env.config.VAULTWARDEN__SERVERURL=https://vaultwarden.{VaultwardenNamespace}.svc.cluster.local:443 " +
                    "--set env.config.SYNC__DRYRUN=false " +
                    "--set env.config.SYNC__SYNCINTERVALSECONDS=10 " +
                    "--set env.config.SYNC__CONTINUOUSSYNC=true " +
                    "--set env.config.SYNC__DELETEORPHANS=true " +
                    $"--set env.config.KUBERNETES__DEFAULTNAMESPACE={TestNamespace1} " +
                    "--set env.config.NODE_TLS_REJECT_UNAUTHORIZED=0 " +
                    "--set api.enabled=false " +
                    "--set dashboard.enabled=false " +
                    "--wait --timeout 2m");
                
                // Wait for operator
                ctx.Status("Waiting for operator...");
                await RunCommand("kubectl", 
                    $"wait --for=condition=Ready pod -l app.kubernetes.io/name=vaultwarden-kubernetes-secrets -n {OperatorNamespace} --timeout=90s");
            });
        
        AnsiConsole.MarkupLine("[green]✓ Operator deployed[/]");
    }
    
    private List<TestItemDefinition> GetTestItemDefinitions()
    {
        return new List<TestItemDefinition>
        {
            // 1. Basic login (default namespace)
            new()
            {
                Name = "test-basic-login",
                Type = ItemType.Login,
                Username = "testuser",
                Password = "testpassword123",
                Uris = new[] { "https://example.com" },
                CustomFields = new() { ["namespaces"] = TestNamespace1 }
            },
            // 2. Custom secret name
            new()
            {
                Name = "my-app-credentials",
                Type = ItemType.Login,
                Username = "appuser",
                Password = "apppassword456",
                CustomFields = new() { ["secret-name"] = "custom-app-secret", ["namespaces"] = TestNamespace1 }
            },
            // 3. Multi-namespace
            new()
            {
                Name = "multi-namespace-secret",
                Type = ItemType.Login,
                Username = "multiuser",
                Password = "multipass789",
                CustomFields = new() { ["namespaces"] = $"{TestNamespace1},{TestNamespace2},{TestNamespace3}" }
            },
            // 4. Custom key names
            new()
            {
                Name = "custom-keys-login",
                Type = ItemType.Login,
                Username = "keyuser",
                Password = "keypass000",
                CustomFields = new()
                {
                    ["secret-key-username"] = "db_user",
                    ["secret-key-password"] = "db_password",
                    ["namespaces"] = TestNamespace1
                }
            },
            // 5. Extra custom fields
            new()
            {
                Name = "extra-fields-login",
                Type = ItemType.Login,
                Username = "extrauser",
                Password = "extrapass111",
                CustomFields = new()
                {
                    ["api_key"] = "sk-abc123xyz",
                    ["api_endpoint"] = "https://api.example.com",
                    ["environment"] = "production",
                    ["namespaces"] = TestNamespace1
                }
            },
            // 6. Annotations and labels
            new()
            {
                Name = "annotated-secret",
                Type = ItemType.Login,
                Username = "annotateduser",
                Password = "annotatedpass222",
                CustomFields = new()
                {
                    ["secret-annotation"] = "description=Test secret with annotations",
                    ["secret-label"] = "team=platform\nenvironment=test",
                    ["namespaces"] = TestNamespace1
                }
            },
            // 7. Secure note
            new()
            {
                Name = "test-secure-note",
                Type = ItemType.SecureNote,
                Notes = "This is a secure note content.\nWith multiple lines.",
                CustomFields = new() { ["config_data"] = "{\"key\": \"value\"}", ["namespaces"] = TestNamespace1 }
            },
            // 8. Merge test items
            new()
            {
                Name = "merge-source-1",
                Type = ItemType.Login,
                Username = "merge_user_1",
                Password = "merge_pass_1",
                CustomFields = new()
                {
                    ["secret-name"] = "merged-secret",
                    ["secret-key-username"] = "first_user",
                    ["secret-key-password"] = "first_pass",
                    ["namespaces"] = TestNamespace1
                }
            },
            new()
            {
                Name = "merge-source-2",
                Type = ItemType.Login,
                Username = "merge_user_2",
                Password = "merge_pass_2",
                CustomFields = new()
                {
                    ["secret-name"] = "merged-secret",
                    ["secret-key-username"] = "second_user",
                    ["secret-key-password"] = "second_pass",
                    ["namespaces"] = TestNamespace1
                }
            },
            // 9. Hidden field
            new()
            {
                Name = "hidden-field-login",
                Type = ItemType.Login,
                Username = "hiddenuser",
                Password = "hiddenpass333",
                CustomFields = new() { ["hidden_api_token"] = "hidden-token-value", ["namespaces"] = TestNamespace1 }
            },
            // 10. Orphan test (no namespace - used for orphan cleanup testing)
            new()
            {
                Name = "orphan-test-item",
                Type = ItemType.Login,
                Username = "orphanuser",
                Password = "orphanpass444",
                CustomFields = new() { ["namespaces"] = TestNamespace1 }
            }
        };
    }
    
    private async Task CheckPrerequisites()
    {
        // bw CLI no longer needed - we use direct API calls
        var tools = new[] { "docker", "kind", "kubectl", "helm" };
        foreach (var tool in tools)
        {
            try
            {
                await RunCommand("which", tool);
            }
            catch
            {
                throw new InvalidOperationException($"Required tool '{tool}' is not installed");
            }
        }
        
        // Check Docker is running
        await RunCommand("docker", "info");
    }
    
    private async Task<string> RunCommand(string command, string args, bool throwOnError = true, 
        bool useShell = false, Dictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        if (useShell)
        {
            psi.FileName = "/bin/bash";
            psi.Arguments = $"-c \"{command} {args}\"";
        }
        else
        {
            psi.FileName = command;
            psi.Arguments = args;
        }
        
        if (env != null)
        {
            foreach (var kvp in env)
            {
                psi.Environment[kvp.Key] = kvp.Value;
            }
        }
        
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {command}");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        
        if (throwOnError && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command failed: {command} {args}\n{error}");
        }
        
        return output;
    }
    
    private async Task WaitForUrl(string url, TimeSpan timeout, bool ignoreSslErrors = false)
    {
        using var handler = new HttpClientHandler();
        if (ignoreSslErrors)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        using var client = new HttpClient(handler);
        var deadline = DateTime.UtcNow + timeout;
        
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // Ignore and retry
            }
            await Task.Delay(1000);
        }
        
        throw new TimeoutException($"URL {url} did not become available within {timeout}");
    }
    
    public void RecordTest(string name, bool passed, string message, int durationMs)
    {
        TestResults.Add(new TestResult
        {
            Name = name,
            Status = passed ? "passed" : "failed",
            DurationMs = durationMs,
            Message = message
        });
    }
}

public class TestCredentials
{
    public required string Email { get; set; }
    public required string MasterPassword { get; set; }
    public required string SessionKey { get; set; }
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
    public required string VaultwardenUrl { get; set; }
    public byte[]? EncryptionKey { get; set; }
    public byte[]? MacKey { get; set; }
}

public class TestResult
{
    public required string Name { get; set; }
    public required string Status { get; set; }
    public required int DurationMs { get; set; }
    public required string Message { get; set; }
}

public class TestItemDefinition
{
    public required string Name { get; set; }
    public ItemType Type { get; set; } = ItemType.Login;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string[]? Uris { get; set; }
    public string? Notes { get; set; }
    public Dictionary<string, string>? CustomFields { get; set; }
}

public enum ItemType
{
    Login = 1,
    SecureNote = 2,
    Card = 3,
    Identity = 4
}
