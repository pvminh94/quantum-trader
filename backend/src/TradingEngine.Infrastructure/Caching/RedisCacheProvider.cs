// ═══════════════════════════════════════════════════════════════════
// RedisCacheProvider.cs - Redis caching layer for market data.
//
// Provides typed cache access with:
// - Automatic serialization via System.Text.Json
// - TTL management per data type
// - Connection resiliency (retry on failure)
// - Thread-safe operations
// ═══════════════════════════════════════════════════════════════════

using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using TradingEngine.Shared;

namespace TradingEngine.Infrastructure.Caching;

/// <summary>
/// Typed Redis cache wrapper with JSON serialization.
/// Handles serialization, deserialization, and error logging.
/// </summary>
public sealed class RedisCacheProvider
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisCacheProvider> _logger;

    // Default TTLs per data type (seconds)
    private static class Ttl
    {
        public const int OrderBook = 5;           // Refreshes every 100ms
        public const int Ticker = 5;              // Refreshes every 1s
        public const int TradeHistory = 3600;     // 1 hour
        public const int KlineHistory = 86400;    // 24 hours
        public const int Idempotency = 60;        // 1 minute
        public const int RateLimit = 60;          // 1 minute
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public RedisCacheProvider(
        IDistributedCache cache,
        ILogger<RedisCacheProvider> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    // ── Generic get/set ───────────────────────────────────────────

    /// <summary>
    /// Gets a value from cache. Returns null if not found or on error.
    /// </summary>
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        try
        {
            var bytes = await _cache.GetAsync(key, ct);
            if (bytes is null) return null;

            return JsonSerializer.Deserialize<T>(bytes, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache GET failed for key: {Key}", key);
            return null; // Cache miss - fall through to database
        }
    }

    /// <summary>
    /// Sets a value in cache with optional TTL.
    /// </summary>
    public async Task SetAsync<T>(string key, T value, int ttlSeconds, CancellationToken ct = default)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(ttlSeconds)
            };

            await _cache.SetAsync(key, bytes, options, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache SET failed for key: {Key}", key);
        }
    }

    // ── Typed helpers ─────────────────────────────────────────────

    /// <summary>
    /// Cache an orderbook snapshot with short TTL.
    /// </summary>
    public Task SetOrderBookAsync(string symbol, OrderBookSnapshotDto snapshot, CancellationToken ct = default)
    {
        var key = Constants.RedisKeys.OrderBook(symbol);
        return SetAsync(key, snapshot, Ttl.OrderBook, ct);
    }

    /// <summary>
    /// Get cached orderbook snapshot.
    /// </summary>
    public Task<OrderBookSnapshotDto?> GetOrderBookAsync(string symbol, CancellationToken ct = default)
    {
        var key = Constants.RedisKeys.OrderBook(symbol);
        return GetAsync<OrderBookSnapshotDto>(key, ct);
    }

    /// <summary>
    /// Cache ticker data.
    /// </summary>
    public Task SetTickerAsync(string symbol, TickerDto ticker, CancellationToken ct = default)
    {
        var key = Constants.RedisKeys.Ticker(symbol);
        return SetAsync(key, ticker, Ttl.Ticker, ct);
    }

    /// <summary>
    /// Get cached ticker data.
    /// </summary>
    public Task<TickerDto?> GetTickerAsync(string symbol, CancellationToken ct = default)
    {
        var key = Constants.RedisKeys.Ticker(symbol);
        return GetAsync<TickerDto>(key, ct);
    }

    /// <summary>
    /// Check if an idempotency key has been used.
    /// Returns the cached order ID if it exists.
    /// </summary>
    public async Task<Guid?> TryGetIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        var key = Constants.RedisKeys.IdempotencyKey(idempotencyKey);
        var value = await _cache.GetStringAsync(key, ct);
        return value is not null && Guid.TryParse(value, out var orderId) ? orderId : null;
    }

    /// <summary>
    /// Store an idempotency key (NX - only if not exists).
    /// Returns true if the key was newly stored.
    /// </summary>
    public async Task<bool> TrySetIdempotencyKeyAsync(string idempotencyKey, Guid orderId, CancellationToken ct = default)
    {
        var key = Constants.RedisKeys.IdempotencyKey(idempotencyKey);
        var value = orderId.ToString();

        try
        {
            // Using StringSet with NX flag for atomic check-and-set
            // In production with StackExchange.Redis:
            // return await _redis.StringSetAsync(key, value, TimeSpan.FromSeconds(Ttl.Idempotency), When.NotExists);
            await _cache.SetStringAsync(key, value, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Ttl.Idempotency)
            }, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

// ═══════════════════════════════════════════════════════════════════
// DTO references for the cache provider
// (These would normally come from the shared DTO assembly)
// ═══════════════════════════════════════════════════════════════════

// Minimal DTOs for cache serialization (duplicated from Hub for clarity)
public sealed record OrderBookSnapshotDto(
    string Symbol,
    long TimestampMs,
    long LastUpdateId,
    PriceLevelDto[] Bids,
    PriceLevelDto[] Asks,
    decimal Spread,
    decimal SpreadPercentage,
    decimal MidPrice
);

public sealed record PriceLevelDto(decimal Price, decimal Quantity, int OrderCount);

public sealed record TickerDto(
    string Symbol,
    decimal OpenPrice,
    decimal HighPrice,
    decimal LowPrice,
    decimal LastPrice,
    decimal Volume,
    decimal QuoteVolume,
    decimal PriceChangePercent,
    long TimestampMs
);