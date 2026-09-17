// ═══════════════════════════════════════════════════════════════════
// Constants.cs - Cross-cutting constants for the trading system.
// ═══════════════════════════════════════════════════════════════════

namespace TradingEngine.Shared;

/// <summary>
/// Global constants used across the trading engine.
/// </summary>
public static class Constants
{
    // ── Symbols ───────────────────────────────────────────────────
    public static class Symbols
    {
        public const string DefaultSymbol = "BTCUSDT";
        public const string DefaultQuote = "USDT";
        public const string BtcUsdt = "BTCUSDT";
        public const string EthUsdt = "ETHUSDT";
    }

    // ── Timing ───────────────────────────────────────────────────
    public static class Timing
    {
        /// <summary>Default heartbeat interval for exchange WebSocket.</summary>
        public const int WebSocketHeartbeatMs = 30_000;

        /// <summary>Max time to wait for exchange ACK before marking order as failed.</summary>
        public const int OrderAckTimeoutMs = 5_000;

        /// <summary>Bot minimum scan interval (1 second).</summary>
        public const int MinBotScanIntervalMs = 1_000;

        /// <summary>UTC midnight reset for daily counters.</summary>
        public static readonly TimeSpan DailyResetTime = new(0, 0, 0); // 00:00 UTC
    }

    // ── Limits ───────────────────────────────────────────────────
    public static class Limits
    {
        /// <summary>Max orders per second per user (rate limiting).</summary>
        public const int MaxOrdersPerSecond = 5;

        /// <summary>Max open orders per user.</summary>
        public const int MaxOpenOrders = 200;

        /// <summary>Max concurrent bot instances per user.</summary>
        public const int MaxBotsPerUser = 20;

        /// <summary>Minimum order quantity (to avoid dust).</summary>
        public const decimal MinOrderQuantity = 0.00001m;

        /// <summary>Max price deviation from market (%) for order validation.</summary>
        public const decimal MaxPriceDeviationPercent = 5m;
    }

    // ── Error Codes ──────────────────────────────────────────────
    public static class ErrorCodes
    {
        public const string InsufficientFunds = "order.insufficient_funds";
        public const string OrderNotFound = "order.not_found";
        public const string OrderRejected = "order.rejected";
        public const string RateLimited = "rate_limit.exceeded";
        public const string CircuitBreakerTripped = "risk.circuit_breaker_tripped";
        public const string InvalidPrice = "validation.invalid_price";
        public const string InvalidQuantity = "validation.invalid_quantity";
        public const string ExchangeUnavailable = "exchange.unavailable";
    }

    // ── Redis Keys ───────────────────────────────────────────────
    public static class RedisKeys
    {
        public static string OrderBook(string symbol) => $"orderbook:{symbol.ToUpperInvariant()}";
        public static string Ticker(string symbol) => $"ticker:{symbol.ToUpperInvariant()}";
        public static string Trades(string symbol) => $"trades:{symbol.ToUpperInvariant()}";
        public static string Klines(string symbol, string interval) => $"klines:{symbol.ToUpperInvariant()}:{interval}";
        public static string UserSession(string userId) => $"session:{userId}";
        public static string IdempotencyKey(string key) => $"idempotency:{key}";
        public static string RateLimit(string ip) => $"ratelimit:{ip}";
    }

    // ── SignalR Hub URLs ─────────────────────────────────────────
    public static class SignalR
    {
        public const string MarketDataHub = "/hubs/market-data";
        public const string BotTelemetryHub = "/hubs/bot-telemetry";
    }
}