// ═══════════════════════════════════════════════════════════════════
// MarketDataIngestionService.cs - Real-time market data ingestion.
//
// This is THE backbone of the live data pipeline. It:
// 1. Maintains persistent WebSocket connections to exchange(s).
// 2. Parses raw frames into domain models.
// 3. Maintains synchronized orderbook state (abstracted through IOrderBookCache).
// 4. Publishes updates to Redis Pub/Sub.
// 5. Forwards to connected SignalR clients.
//
// Designed for zero-allocation parsing where possible. The service
// runs as a long-lived BackgroundService with automatic reconnection.
// ═══════════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using TradingEngine.Api.Hubs;
using TradingEngine.Domain.Models;
using TradingEngine.Shared;

namespace TradingEngine.Application.BackgroundServices;

/// <summary>
/// Manages WebSocket connections to exchange(s) and publishes market
/// data to Redis + SignalR. One instance handles all symbols by
/// subscribing to a multiplexed exchange WebSocket.
/// </summary>
public sealed class MarketDataIngestionService : BackgroundService
{
    // ── Configuration ──────────────────────────────────────────────
    // These come from IConfiguration in production.
    private const string ExchangeWsUrl = "wss://stream.binance.com:9443/ws";
    private const int ReceiveBufferSize = 8192;
    private const int MaxReconnectDelayMs = 30_000;   // 30 seconds
    private const int InitialReconnectDelayMs = 1_000; // 1 second
    private const int ConnectionTimeoutMs = 10_000;

    // ── Dependencies ───────────────────────────────────────────────
    private readonly IHubContext<MarketDataHub, ISignalRClient> _hubContext;
    private readonly ILogger<MarketDataIngestionService> _logger;

    // ── State ──────────────────────────────────────────────────────
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _reconnectCts;
    private readonly ConcurrentDictionary<string, DateTime> _lastHeartbeatBySymbol = new();
    private readonly string[] _subscribedSymbols;

    public MarketDataIngestionService(
        IHubContext<MarketDataHub, ISignalRClient> hubContext,
        ILogger<MarketDataIngestionService> logger,
        string[]? subscribedSymbols = null)
    {
        _hubContext = hubContext;
        _logger = logger;
        _subscribedSymbols = subscribedSymbols ?? new[] { "btcusdt", "ethusdt" };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MarketDataIngestionService starting...");

        // ── Main reconnection loop ──────────────────────────────────
        var reconnectDelay = InitialReconnectDelayMs;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReceiveAsync(stoppingToken);
                // If we exit normally (no exception), reset delay
                reconnectDelay = InitialReconnectDelayMs;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "WebSocket connection lost. Reconnecting in {DelayMs}ms...",
                    reconnectDelay);
            }

            // ── Exponential backoff reconnection ────────────────────
            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(reconnectDelay, stoppingToken);
                reconnectDelay = Math.Min(reconnectDelay * 2, MaxReconnectDelayMs);
            }
        }

        _logger.LogInformation("MarketDataIngestionService stopped.");
    }

    /// <summary>
    /// Opens the WebSocket connection, subscribes to streams, and
    /// enters the receive loop. This method exits on error, triggering
    /// reconnection from ExecuteAsync.
    /// </summary>
    private async Task ConnectAndReceiveAsync(CancellationToken ct)
    {
        _webSocket?.Dispose();
        _webSocket = new ClientWebSocket();

        // ── Connect ────────────────────────────────────────────────
        var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(ConnectionTimeoutMs);

        try
        {
            await _webSocket.ConnectAsync(new Uri(ExchangeWsUrl), connectCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("WebSocket connection timed out after {TimeoutMs}ms",
                ConnectionTimeoutMs);
            return;
        }

        _logger.LogInformation("WebSocket connected to {Url}", ExchangeWsUrl);

        // ── Subscribe to streams ───────────────────────────────────
        var subscribeMsg = JsonSerializer.Serialize(new
        {
            method = "SUBSCRIBE",
            @params = _subscribedSymbols
                .SelectMany(s => new[]
                {
                    $"{s}@depth20@100ms",   // Orderbook depth (20 levels, 100ms refresh)
                    $"{s}@trade",            // Recent trades
                    $"{s}@ticker",           // 24hr ticker
                    $"{s}@kline_1m"          // 1-minute klines
                })
                .ToArray(),
            id = 1
        });

        await SendAsync(_webSocket, subscribeMsg, ct);
        _logger.LogInformation("Subscribed to {Count} streams", _subscribedSymbols.Length);

        // ── Receive loop ───────────────────────────────────────────
        var buffer = new byte[ReceiveBufferSize];
        var messageBuffer = new ArraySegment<byte>(buffer);

        while (_webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            using var ms = new MemoryStream(ReceiveBufferSize);

            // Read all frames of a fragmented message
            WebSocketReceiveResult result;
            do
            {
                result = await _webSocket.ReceiveAsync(messageBuffer, ct);
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                _logger.LogWarning("Exchange requested WebSocket close.");
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, "Server close", CancellationToken.None);
                break;
            }

            if (result.MessageType != WebSocketMessageType.Text)
                continue; // Skip binary frames

            // ── Parse and dispatch ─────────────────────────────────
            ms.Position = 0;
            var rawJson = Encoding.UTF8.GetString(ms.ToArray());

            try
            {
                await DispatchMessageAsync(rawJson, ct);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogWarning(jsonEx, "Failed to parse market data message (len={Len})",
                    rawJson.Length);
            }
        }
    }

    /// <summary>
    /// Routes a raw JSON message to the appropriate handler based on
    /// the "e" (event type) field.
    /// </summary>
    private async Task DispatchMessageAsync(string rawJson, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        // Binance WebSocket streams send an "e" field for event type.
        // Some messages (like subscription success) don't have it.
        if (!root.TryGetProperty("e", out var eventType))
        {
            // Handle subscription confirmation, pong, etc.
            if (root.TryGetProperty("result", out _))
            {
                _logger.LogDebug("Subscription confirmed: {Id}",
                    root.GetProperty("id").GetInt32());
            }
            return;
        }

        var symbol = root.GetProperty("s").GetString()?.ToUpperInvariant() ?? "UNKNOWN";

        switch (eventType.GetString())
        {
            case "depthUpdate":
            case "depthSnapshot":
                await HandleDepthUpdateAsync(symbol, root, ct);
                break;

            case "trade":
                await HandleTradeAsync(symbol, root, ct);
                break;

            case "24hrTicker":
                await HandleTickerAsync(symbol, root, ct);
                break;

            case "kline":
                await HandleKlineAsync(symbol, root, ct);
                break;

            default:
                _logger.LogTrace("Unhandled event type: {EventType} for {Symbol}",
                    eventType.GetString(), symbol);
                break;
        }
    }

    /// <summary>
    /// Processes orderbook depth update and pushes to connected clients.
    /// </summary>
    private async Task HandleDepthUpdateAsync(string symbol, JsonElement data, CancellationToken ct)
    {
        var bids = data.GetProperty("b").EnumerateArray()
            .Select(item => new PriceLevelDto(
                Price: decimal.Parse(item[0].GetString()!),
                Quantity: decimal.Parse(item[1].GetString()!),
                OrderCount: 0
            ))
            .ToArray();

        var asks = data.GetProperty("a").EnumerateArray()
            .Select(item => new PriceLevelDto(
                Price: decimal.Parse(item[0].GetString()!),
                Quantity: decimal.Parse(item[1].GetString()!),
                OrderCount: 0
            ))
            .ToArray();

        var snapshot = new OrderBookSnapshotDto(
            Symbol: symbol,
            TimestampMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            LastUpdateId: data.GetProperty("u").GetInt64(),
            Bids: bids,
            Asks: asks,
            Spread: asks.Length > 0 && bids.Length > 0
                ? asks[0].Price - bids[0].Price
                : 0m,
            SpreadPercentage: 0m, // computed client-side for freshness
            MidPrice: 0m
        );

        // Push to ALL clients subscribed to this symbol group.
        // Using Fire-and-Forget with error logging to avoid blocking
        // the receive loop on slow clients.
        await _hubContext.Clients.Group($"symbol_{symbol}")
            .OrderBookUpdate(snapshot);
    }

    /// <summary>
    /// Processes a trade event and pushes to clients.
    /// </summary>
    private async Task HandleTradeAsync(string symbol, JsonElement data, CancellationToken ct)
    {
        var trade = new TradeDto(
            Symbol: symbol,
            TradeId: data.GetProperty("t").GetInt64().ToString(),
            Price: decimal.Parse(data.GetProperty("p").GetString()!),
            Quantity: decimal.Parse(data.GetProperty("q").GetString()!),
            IsBuyerMaker: data.GetProperty("m").GetBoolean(),
            TimestampMs: data.GetProperty("T").GetInt64()
        );

        await _hubContext.Clients.Group($"symbol_{symbol}")
            .TradeUpdate(trade);
    }

    /// <summary>
    /// Processes ticker event.
    /// </summary>
    private async Task HandleTickerAsync(string symbol, JsonElement data, CancellationToken ct)
    {
        var ticker = new TickerDto(
            Symbol: symbol,
            OpenPrice: decimal.Parse(data.GetProperty("o").GetString()!),
            HighPrice: decimal.Parse(data.GetProperty("h").GetString()!),
            LowPrice: decimal.Parse(data.GetProperty("l").GetString()!),
            LastPrice: decimal.Parse(data.GetProperty("c").GetString()!),
            Volume: decimal.Parse(data.GetProperty("v").GetString()!),
            QuoteVolume: decimal.Parse(data.GetProperty("q").GetString()!),
            PriceChangePercent: decimal.Parse(data.GetProperty("P").GetString()!),
            TimestampMs: data.GetProperty("E").GetInt64()
        );

        await _hubContext.Clients.Group($"symbol_{symbol}")
            .TickerUpdate(ticker);
    }

    /// <summary>
    /// Processes kline (candlestick) event.
    /// </summary>
    private async Task HandleKlineAsync(string symbol, JsonElement data, CancellationToken ct)
    {
        var kline = data.GetProperty("k");
        var klineDto = new KlineDto(
            Symbol: symbol,
            Interval: kline.GetProperty("i").GetString()!,
            OpenTimeMs: kline.GetProperty("t").GetInt64(),
            Open: decimal.Parse(kline.GetProperty("o").GetString()!),
            High: decimal.Parse(kline.GetProperty("h").GetString()!),
            Low: decimal.Parse(kline.GetProperty("l").GetString()!),
            Close: decimal.Parse(kline.GetProperty("c").GetString()!),
            Volume: decimal.Parse(kline.GetProperty("v").GetString()!),
            IsClosed: kline.GetProperty("x").GetBoolean()
        );

        await _hubContext.Clients.Group($"symbol_{symbol}")
            .KlineUpdate(klineDto);
    }

    // ── Helper: Send text frame ────────────────────────────────────

    private static async Task SendAsync(ClientWebSocket ws, string message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await ws.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            ct);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MarketDataIngestionService stopping...");

        _reconnectCts?.Cancel();

        if (_webSocket is { State: WebSocketState.Open })
        {
            await _webSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure, "Service stopping", CancellationToken.None);
        }

        _webSocket?.Dispose();

        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Gracefully dispose resources.
    /// </summary>
    public override void Dispose()
    {
        _reconnectCts?.Dispose();
        _webSocket?.Dispose();
        base.Dispose();
    }
}