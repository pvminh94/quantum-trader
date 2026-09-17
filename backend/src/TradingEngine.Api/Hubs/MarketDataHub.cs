// ═══════════════════════════════════════════════════════════════════
// MarketDataHub.cs - Strongly-typed SignalR hub for real-time market data.
//
// Design principles:
// 1. Group-based subscriptions (symbol_{symbol}) to isolate data per pair.
// 2. Automatic cleanup on disconnect to prevent memory leaks.
// 3. Rate-limited client methods: we batch updates at 100ms intervals
//    on the server side to avoid overwhelming slow clients.
// 4. All market data originates from the BackgroundService layer,
//    not from client requests - this hub is purely a push channel.
// ═══════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.SignalR;
using TradingEngine.Domain.Models;
using TradingEngine.Shared;

namespace TradingEngine.Api.Hubs;

/// <summary>
/// Strongly-typed hub interface. Clients implement these methods.
/// Using <c>ISignalRClient</c> instead of dynamic "SendAsync" strings
/// gives us compile-time safety and IntelliSense on the client.
/// </summary>
public interface ISignalRClient
{
    // ── Market Data ────────────────────────────────────────────────

    /// <summary>
    /// Called when the orderbook snapshot or incremental update arrives.
    /// </summary>
    Task OrderBookUpdate(OrderBookSnapshotDto orderBook);

    /// <summary>
    /// Called when a new trade executes on the exchange.
    /// </summary>
    Task TradeUpdate(TradeDto trade);

    /// <summary>
    /// Called when the 24hr ticker updates for a symbol.
    /// </summary>
    Task TickerUpdate(TickerDto ticker);

    /// <summary>
    /// Called when a new candlestick (kline) closes.
    /// </summary>
    Task KlineUpdate(KlineDto kline);

    // ── Account / Orders ───────────────────────────────────────────

    /// <summary>
    /// Called when the user's order status changes.
    /// </summary>
    Task OrderStatusUpdate(OrderDto order);

    /// <summary>
    /// Called when account balance changes.
    /// </summary>
    Task BalanceUpdate(BalanceDto balance);

    // ── Bot Telemetry ──────────────────────────────────────────────

    /// <summary>
    /// Called when a bot state transitions or logs a message.
    /// </summary>
    Task BotTelemetryUpdate(BotTelemetryDto telemetry);

    /// <summary>
    /// Connection health check response.
    /// Client sends ping, server responds with pong + server timestamp.
    /// </summary>
    Task Pong(long serverTimestampMs);
}

/// <summary>
/// Server-side SignalR hub. Clients connect and subscribe to symbols.
/// All data pushing happens via IHubContext&lt;MarketDataHub, ISignalRClient&gt;
/// injected into background services.
/// </summary>
public sealed class MarketDataHub : Hub<ISignalRClient>
{
    // In-memory set of active connections for health monitoring.
    private static readonly ConcurrentDictionary<string, ConnectionMetadata> _connections = new();

    private readonly ILogger<MarketDataHub> _logger;

    public MarketDataHub(ILogger<MarketDataHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Client calls this on connect to subscribe to a symbol's data feed.
    /// The client will start receiving OrderBookUpdate, TradeUpdate, etc.
    /// </summary>
    public async Task SubscribeSymbol(string symbol)
    {
        symbol = Guard.NotNullOrWhiteSpace(symbol, nameof(symbol)).ToUpperInvariant();
        var groupName = $"symbol_{symbol}";

        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        _logger.LogDebug(
            "Client {ConnectionId} subscribed to symbol {Symbol} (group {Group})",
            Context.ConnectionId, symbol, groupName);
    }

    /// <summary>
    /// Unsubscribe from a symbol feed.
    /// </summary>
    public async Task UnsubscribeSymbol(string symbol)
    {
        symbol = Guard.NotNullOrWhiteSpace(symbol, nameof(symbol)).ToUpperInvariant();
        var groupName = $"symbol_{symbol}";

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        _logger.LogDebug(
            "Client {ConnectionId} unsubscribed from symbol {Symbol}",
            Context.ConnectionId, symbol);
    }

    /// <summary>
    /// Client-side ping for latency measurement.
    /// Server responds immediately with its current timestamp.
    /// </summary>
    public async Task Ping()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await Clients.Caller.Pong(timestamp);
    }

    // ── Connection lifecycle ───────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        _connections[Context.ConnectionId] = new ConnectionMetadata
        {
            ConnectedAt = DateTime.UtcNow,
            UserId = Context.UserIdentifier ?? "anonymous"
        };

        _logger.LogInformation(
            "Client connected: {ConnectionId} (user: {User})",
            Context.ConnectionId, Context.UserIdentifier ?? "anonymous");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.TryRemove(Context.ConnectionId, out _);

        if (exception is not null)
        {
            _logger.LogWarning(exception,
                "Client disconnected with error: {ConnectionId}",
                Context.ConnectionId);
        }
        else
        {
            _logger.LogInformation("Client disconnected: {ConnectionId}",
                Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Returns current connected client count (for health dashboard).
    /// </summary>
    public static int ActiveConnectionCount => _connections.Count;

    /// <summary>
    /// Returns snapshot of all connections (admin/debug).
    /// </summary>
    public static IReadOnlyDictionary<string, ConnectionMetadata> GetAllConnections()
        => _connections;
}

/// <summary>
/// Metadata tracked per SignalR connection.
/// </summary>
public sealed class ConnectionMetadata
{
    public DateTime ConnectedAt { get; init; }
    public string UserId { get; init; } = string.Empty;
}

// ═══════════════════════════════════════════════════════════════════
// DTOs - Data Transfer Objects for SignalR payloads.
// These are separate from Domain models to allow serialization
// control and API versioning without affecting domain logic.
// ═══════════════════════════════════════════════════════════════════

// Using records for immutable, value-equality DTOs with JSON serialization.

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

public sealed record PriceLevelDto(
    decimal Price,
    decimal Quantity,
    int OrderCount
);

public sealed record TradeDto(
    string Symbol,
    string TradeId,
    decimal Price,
    decimal Quantity,
    bool IsBuyerMaker,   // true if sell, false if buy
    long TimestampMs
);

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

public sealed record KlineDto(
    string Symbol,
    string Interval,       // "1s", "1m", "5m", "1h", "1d"
    long OpenTimeMs,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    bool IsClosed
);

public sealed record OrderDto(
    Guid Id,
    string Symbol,
    string Side,
    string Type,
    string Status,
    decimal Quantity,
    decimal FilledQuantity,
    decimal? Price,
    decimal? AveragePrice,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? FailureReason
);

public sealed record BalanceDto(
    string Asset,
    decimal Free,
    decimal Locked,
    decimal Total,
    decimal UsdValue
);

public sealed record BotTelemetryDto(
    Guid BotId,
    string BotName,
    string Status,
    string? Message,
    decimal? Pnl,
    decimal? WinRate,
    int TradesToday,
    DateTime TimestampUtc
);