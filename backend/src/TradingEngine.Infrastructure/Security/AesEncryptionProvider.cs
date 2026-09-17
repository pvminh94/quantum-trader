// ═══════════════════════════════════════════════════════════════════
// AesEncryptionProvider.cs - AES-256 encryption for exchange API keys.
//
// API keys are encrypted at rest in PostgreSQL. They are decrypted
// only in memory at runtime when needed for exchange communication.
// Uses AES-256-GCM for authenticated encryption.
// ═══════════════════════════════════════════════════════════════════

using System.Security.Cryptography;

namespace TradingEngine.Infrastructure.Security;

/// <summary>
/// Provides AES-256-GCM encryption/decryption services for sensitive data.
/// Thread-safe: uses a new instance per operation (no shared state).
/// </summary>
public sealed class AesEncryptionProvider : IDisposable
{
    // ── Configuration ──────────────────────────────────────────────
    // In production, the master key comes from Azure Key Vault / AWS KMS
    // environment variable, or a secure key management service.
    // NEVER hardcode the master key in source control.
    private const string MASTER_KEY_ENV_VAR = "TRADING_ENGINE_MASTER_KEY";

    private readonly byte[] _masterKey;
    private readonly ILogger<AesEncryptionProvider> _logger;

    // GCM tag size in bytes (128-bit tag)
    private const int TAG_SIZE = 16;
    // Nonce size in bytes (96-bit nonce for GCM)
    private const int NONCE_SIZE = 12;

    public AesEncryptionProvider(ILogger<AesEncryptionProvider> logger)
    {
        _logger = logger;

        // ── Load master key from environment ───────────────────────
        var keyBase64 = Environment.GetEnvironmentVariable(MASTER_KEY_ENV_VAR)
            ?? throw new InvalidOperationException(
                $"Environment variable '{MASTER_KEY_ENV_VAR}' is not set. " +
                "Must be a 32-byte (256-bit) key encoded in Base64.");

        try
        {
            _masterKey = Convert.FromBase64String(keyBase64);

            if (_masterKey.Length != 32)
                throw new InvalidOperationException(
                    $"Master key must be 32 bytes (256 bits). Got {_masterKey.Length} bytes.");
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Master key is not valid Base64: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Encrypts plaintext using AES-256-GCM.
    /// Returns Base64-encoded string: nonce + ciphertext + tag.
    /// </summary>
    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return string.Empty;

        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NONCE_SIZE];
        var tag = new byte[TAG_SIZE];
        var ciphertext = new byte[plainBytes.Length];

        // Generate cryptographically secure random nonce
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_masterKey, TAG_SIZE);

        aes.Encrypt(nonce, plainBytes, ciphertext, tag);

        // Combine: nonce (12) + ciphertext (N) + tag (16)
        var result = new byte[NONCE_SIZE + plainBytes.Length + TAG_SIZE];
        Buffer.BlockCopy(nonce, 0, result, 0, NONCE_SIZE);
        Buffer.BlockCopy(ciphertext, 0, result, NONCE_SIZE, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, NONCE_SIZE + ciphertext.Length, TAG_SIZE);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypts a Base64-encoded ciphertext produced by Encrypt().
    /// Returns the original plaintext.
    /// </summary>
    public string Decrypt(string ciphertextBase64)
    {
        if (string.IsNullOrEmpty(ciphertextBase64))
            return string.Empty;

        var combined = Convert.FromBase64String(ciphertextBase64);

        if (combined.Length < NONCE_SIZE + TAG_SIZE + 1)
            throw new CryptographicException("Ciphertext is too short or corrupted.");

        // Extract components
        var nonce = new byte[NONCE_SIZE];
        var tag = new byte[TAG_SIZE];
        var ciphertext = new byte[combined.Length - NONCE_SIZE - TAG_SIZE];

        Buffer.BlockCopy(combined, 0, nonce, 0, NONCE_SIZE);
        Buffer.BlockCopy(combined, NONCE_SIZE, ciphertext, 0, ciphertext.Length);
        Buffer.BlockCopy(combined, NONCE_SIZE + ciphertext.Length, tag, 0, TAG_SIZE);

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_masterKey, TAG_SIZE);

        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return System.Text.Encoding.UTF8.GetString(plaintext);
    }

    /// <summary>
    /// Securely wipe the master key from memory.
    /// </summary>
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_masterKey);
    }
}

// ═══════════════════════════════════════════════════════════════════
// ApiKeyManager.cs - Manages exchange API key lifecycle.
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Manages the lifecycle of exchange API credentials:
/// - Encrypted storage in PostgreSQL
/// - In-memory cache with TTL
/// - Validation on insertion
/// - Rotation support
/// </summary>
public sealed class ApiKeyManager
{
    private readonly AesEncryptionProvider _encryption;
    private readonly ILogger<ApiKeyManager> _logger;

    // In-memory cache: userId -> (apiKey, apiSecret, expiresAt)
    private static readonly ConcurrentDictionary<Guid, (string ApiKey, string ApiSecret, DateTime ExpiresAt)> _cache = new();

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public ApiKeyManager(
        AesEncryptionProvider encryption,
        ILogger<ApiKeyManager> logger)
    {
        _encryption = encryption;
        _logger = logger;
    }

    /// <summary>
    /// Encrypts and stores API credentials.
    /// In production, persists to PostgreSQL via repository.
    /// </summary>
    public async Task StoreCredentialsAsync(Guid userId, string apiKey, string apiSecret, CancellationToken ct = default)
    {
        Guard.NotNullOrWhiteSpace(apiKey, nameof(apiKey));
        Guard.NotNullOrWhiteSpace(apiSecret, nameof(apiSecret));

        var encryptedKey = _encryption.Encrypt(apiKey);
        var encryptedSecret = _encryption.Encrypt(apiSecret);

        // In production: await _repository.SaveCredentialsAsync(userId, encryptedKey, encryptedSecret, ct);
        _logger.LogInformation("API credentials stored for user {UserId}", userId);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Retrieves decrypted API credentials from cache or database.
    /// </summary>
    public async Task<(string ApiKey, string ApiSecret)> GetCredentialsAsync(Guid userId, CancellationToken ct = default)
    {
        // Check cache
        if (_cache.TryGetValue(userId, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
        {
            return (cached.ApiKey, cached.ApiSecret);
        }

        // In production: load from PostgreSQL
        // var (encryptedKey, encryptedSecret) = await _repository.GetCredentialsAsync(userId, ct);
        // For now, return placeholder
        var encryptedKey = _encryption.Encrypt("placeholder-api-key");
        var encryptedSecret = _encryption.Encrypt("placeholder-api-secret");

        var apiKey = _encryption.Decrypt(encryptedKey);
        var apiSecret = _encryption.Decrypt(encryptedSecret);

        // Update cache
        _cache[userId] = (apiKey, apiSecret, DateTime.UtcNow.Add(CacheTtl));

        return (apiKey, apiSecret);
    }

    /// <summary>
    /// Removes cached credentials (e.g., on key rotation or revoke).
    /// </summary>
    public void InvalidateCache(Guid userId)
    {
        _cache.TryRemove(userId, out _);
        _logger.LogInformation("API key cache invalidated for user {UserId}", userId);
    }
}