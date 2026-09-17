// ═══════════════════════════════════════════════════════════════════
// BotExecutionEngineService.cs - The core bot execution loop.
//
// Architecture:
// - One BackgroundService that manages N bot instances concurrently.
// - Each bot runs as an independent loop iteration within the main loop.
// - Bots are not multi-threaded per-instance; they are cooperatively
//   scheduled in a single async loop to avoid thread-safety issues.
// - For true multi-pair parallelism, scale horizontally by running
//   multiple instances of this service (one per exchange/pair group).
//
// The loop iteration per bot:
//   1. Fetch latest market data from Redis cache (sub-millisecond).
//   2. Run strategy indicators (TA-Lib or custom).
//   3. Evaluate entry/exit conditions.
//   4. Calculate position size (Kelly Criterion / % risk).
//   5. Validate risk constraints.
//   6. Submit order with idempotency key.
//   7. Update state machine.
// ═══════════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using TradingEngine.Api.Hubs;
using TradingEngine.Domain.Enums;
using TradingEngine.Domain.Models;
using TradingEngine.Shared;

namespace TradingEngine.Application.BackgroundServices;

/// <summary>
/// Drives the trading bot execution loop. Each bot configuration
/// manager produces signals and this service executes them.
/// </summary>
public sealed class BotExecutionEngineService : BackgroundService
{
    // ── Bot scan interval in milliseconds ──────────────────────────
    // Configurable per bot, but default for high-frequency: 1 second.
    private const int DefaultScanIntervalMs = 1_000;

    private readonly IHubContext<BotTelemetryHub, IBotTelemetryClient> _telemetryHub;
    private readonly ILogger<BotExecutionEngineService> _logger;

    /// <summary>
    /// Tracks running bot instances: BotId -> BotExecutionContext.
    /// Thread-safe for concurrent enumeration and updates.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, BotExecutionContext> _activeBots = new();

    /// <summary>
    /// Global circuit breaker state.
    /// </summary>
    private readonly CircuitBreakerState _circuitBreaker = new();

    public BotExecutionEngineService(
        IHubContext<BotTelemetryHub, IBotTelemetryClient> telemetryHub,
        ILogger<BotExecutionEngineService> logger)
    {
        _telemetryHub = telemetryHub;
        _logger = logger;
    }

    /// <summary>
    /// Starts a bot with the given configuration. Returns false if
    /// the bot is already running or the circuit breaker is tripped.
    /// </summary>
    public bool StartBot(BotConfig config)
    {
        if (_circuitBreaker.IsTripped)
        {
            _logger.LogWarning("Cannot start bot {BotId}: circuit breaker is tripped.", config.Id);
            return false;
        }

        if (!_activeBots.TryAdd(config.Id, new BotExecutionContext(config)))
        {
            _logger.LogWarning("Bot {BotId} is already running.", config.Id);
            return false;
        }

        _logger.LogInformation("Bot started: {BotName} (ID: {BotId})", config.Name, config.Id);
        return true;
    }

    /// <summary>
    /// Stops a running bot. Returns false if the bot was not found.
    /// </summary>
    public bool StopBot(Guid botId)
    {
        if (!_activeBots.TryRemove(botId, out var ctx))
            return false;

        ctx.CancellationTokenSource.Cancel();
        _logger.LogInformation("Bot stopped: {BotName} (ID: {BotId})", ctx.Config.Name, botId);
        return true;
    }

    /// <summary>
    /// Stops ALL running bots. Used by the global kill switch.
    /// </summary>
    public void StopAllBots()
    {
        var botIds = _activeBots.Keys.ToArray();
        foreach (var id in botIds)
            StopBot(id);

        _logger.LogWarning("All bots stopped ({Count} total).", botIds.Length);
    }

    /// <summary>
    /// Returns the current count of active bot instances.
    /// </summary>
    public int ActiveBotCount => _activeBots.Count;

    // ═══════════════════════════════════════════════════════════════
    // MAIN EXECUTION LOOP
    // ═══════════════════════════════════════════════════════════════

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BotExecutionEngineService starting...");

        var mainTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(200)); // 5Hz main loop

        try
        {
            while (await mainTimer.WaitForNextTickAsync(stoppingToken))
            {
                // ── Global risk check ──────────────────────────────
                if (_circuitBreaker.IsTripped)
                {
                    // Circuit breaker is on: stop all bots immediately
                    StopAllBots();
                    _circuitBreaker.Reset(); // Will re-trip next cycle if condition persists
                    continue;
                }

                // ── Iterate over all active bots ───────────────────
                // Using ToArray snapshot to avoid modification during iteration
                foreach (var (botId, ctx) in _activeBots.ToArray())
                {
                    if (ctx.CancellationTokenSource.IsCancellationRequested)
                    {
                        _activeBots.TryRemove(botId, out _);
                        continue;
                    }

                    try
                    {
                        await ExecuteBotIterationAsync(ctx, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Bot {BotId} iteration failed", botId);
                        await PublishTelemetryAsync(ctx, "error", $"Iteration failed: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        finally
        {
            mainTimer.Dispose();
            StopAllBots();
            _logger.LogInformation("BotExecutionEngineService stopped.");
        }
    }

    /// <summary>
    /// Single iteration of a bot's strategy loop.
    /// </summary>
    private async Task ExecuteBotIterationAsync(BotExecutionContext ctx, CancellationToken ct)
    {
        var config = ctx.Config;

        // ── 1. Fetch market data from cache ────────────────────────
        // In production this reads from Redis via IMarketDataService.
        var marketData = await GetMarketDataAsync(config.Symbol, ct);

        // ── 2. Check scan interval ─────────────────────────────────
        var elapsedMs = (DateTime.UtcNow - ctx.LastScanAtUtc).TotalMilliseconds;
        if (elapsedMs < config.ScanIntervalMs)
            return; // Not time yet for this bot's scan interval

        ctx.LastScanAtUtc = DateTime.UtcNow;

        // ── 3. Evaluate signals ────────────────────────────────────
        var signal = EvaluateStrategy(config, marketData);

        // ── 4. No signal → skip ────────────────────────────────────
        if (signal == SignalType.None)
            return;

        // ── 5. Per-bot risk check ──────────────────────────────────
        if (!ValidateBotRisk(ctx))
        {
            await PublishTelemetryAsync(ctx, "risk_blocked",
                $"Risk check failed. Daily loss limit: {ctx.DailyLoss:F4}");
            return;
        }

        // ── 6. Calculate position size ─────────────────────────────
        var positionSize = CalculatePositionSize(config, marketData, signal);

        // ── 7. Validate final position ─────────────────────────────
        if (positionSize <= 0)
            return;

        // ── 8. Build and submit order ──────────────────────────────
        var orderType = signal == SignalType.Buy ? OrderSide.Buy : OrderSide.Sell;
        var order = new Order(
            symbol: config.Symbol,
            side: orderType,
            type: OrderType.Market, // Default to market for speed
            quantity: positionSize,
            timeInForce: TimeInForce.IOC
        );

        // In production, this calls IOrderService.PlaceOrderAsync
        await SubmitOrderAsync(order, ct);

        // ── 9. Update bot state ────────────────────────────────────
        ctx.LastOrderId = order.Id;
        ctx.TradesToday++;
        ctx.LastSignal = signal;

        await PublishTelemetryAsync(ctx, "order_placed",
            $"{signal} {positionSize:F4} {config.Symbol} @ market");
    }

    // ═══════════════════════════════════════════════════════════════
    // STRATEGY EVALUATION (Indicator-based)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Evaluates the configured strategy against current market data.
    /// Returns Buy, Sell, or None.
    /// </summary>
    private static SignalType EvaluateStrategy(BotConfig config, MarketDataSnapshot data)
    {
        // ── Example: EMA crossover strategy ────────────────────────
        // In production, this would use TA-Lib for actual calculations.
        // This is a simplified illustrative implementation.

        if (data.FastEma.HasValue && data.SlowEma.HasValue)
        {
            // Fast EMA crosses above Slow EMA = Buy signal
            if (data.FastEma.Value > data.SlowEma.Value &&
                data.PreviousFastEma <= data.PreviousSlowEma)
                return SignalType.Buy;

            // Fast EMA crosses below Slow EMA = Sell signal
            if (data.FastEma.Value < data.SlowEma.Value &&
                data.PreviousFastEma >= data.PreviousSlowEma)
                return SignalType.Sell;
        }

        // ── RSI oversold / overbought ──────────────────────────────
        if (config.UseRsi && data.Rsi.HasValue)
        {
            if (data.Rsi.Value < config.RsiOversoldThreshold)
                return SignalType.Buy;

            if (data.Rsi.Value > config.RsiOverboughtThreshold)
                return SignalType.Sell;
        }

        return SignalType.None;
    }

    // ═══════════════════════════════════════════════════════════════
    // POSITION SIZING (Kelly Criterion)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Calculates position size using the Kelly Criterion or fixed % risk.
    ///
    /// Kelly % = (W - (1-W) / (R)) where:
    ///   W = historical win rate
    ///   R = average win / average loss (payoff ratio)
    ///
    /// Returns the quantity to trade (in base asset units).
    /// </summary>
    private static decimal CalculatePositionSize(BotConfig config, MarketDataSnapshot data, SignalType signal)
    {
        // ── Fixed percentage risk model (conservative) ─────────────
        if (config.RiskMode == RiskMode.FixedPercentage)
        {
            var riskCapital = config.AccountBalance * config.RiskPerTradePercent / 100m;

            // For a market order, use the current price to size quantity
            var entryPrice = signal == SignalType.Buy ? data.BestAsk : data.BestBid;
            if (entryPrice <= 0) return 0;

            return riskCapital / entryPrice;
        }

        // ── Kelly Criterion model ──────────────────────────────────
        if (config.RiskMode == RiskMode.KellyCriterion && config.WinRate > 0)
        {
            var kellyFraction = config.WinRate -
                                (1m - config.WinRate) / config.PayoffRatio;

            // Kelly can be very aggressive; we use "Fractional Kelly"
            // (25%-50% of pure Kelly) to reduce volatility
            var conservativeKelly = Math.Max(0, kellyFraction * 0.25m);

            var riskCapital = config.AccountBalance * conservativeKelly;
            var entryPrice = signal == SignalType.Buy ? data.BestAsk : data.BestBid;
            if (entryPrice <= 0) return 0;

            return riskCapital / entryPrice;
        }

        return 0;
    }

    // ═══════════════════════════════════════════════════════════════
    // RISK VALIDATION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates per-bot risk constraints.
    /// Returns false if any limit is exceeded (order should NOT be placed).
    /// </summary>
    private static bool ValidateBotRisk(BotExecutionContext ctx)
    {
        var config = ctx.Config;

        // Max consecutive losses
        if (config.MaxConsecutiveLosses > 0 && ctx.ConsecutiveLosses >= config.MaxConsecutiveLosses)
            return false;

        // Max daily loss
        if (config.MaxDailyLoss > 0 && ctx.DailyLoss >= config.MaxDailyLoss)
            return false;

        // Max daily trades
        if (config.MaxDailyTrades > 0 && ctx.TradesToday >= config.MaxDailyTrades)
            return false;

        // Max position exposure
        if (config.MaxExposure > 0 && ctx.CurrentExposure >= config.MaxExposure)
            return false;

        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    // MARKET DATA FETCH (Cache)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Fetches the latest market data snapshot from the cache layer.
    /// In production, this reads from Redis via IMarketDataService.
    /// </summary>
    private static ValueTask<MarketDataSnapshot> GetMarketDataAsync(string symbol, CancellationToken ct)
    {
        // Placeholder: in production this queries Redis for:
        // - Latest price, volume
        // - Pre-computed indicators (EMA, RSI, MACD)
        // - Best bid/ask from cached orderbook
        return ValueTask.FromResult(new MarketDataSnapshot
        {
            Symbol = symbol,
            LastPrice = 50000m,
            BestBid = 49990m,
            BestAsk = 50010m,
            FastEma = 49980m,
            SlowEma = 49800m,
            PreviousFastEma = 49850m,
            PreviousSlowEma = 49750m,
            Rsi = 52.5m
        });
    }

    /// <summary>
    /// Placeholder order submission. In production, this goes through
    /// IOrderService which manages idempotency, retries, and exchange
    /// communication.
    /// </summary>
    private async Task SubmitOrderAsync(Order order, CancellationToken ct)
    {
        // Production implementation would:
        // 1. Generate idempotency key
        // 2. Call exchange REST API
        // 3. Handle response (accept/reject)
        // 4. Persist order to PostgreSQL
        // 5. Push update via SignalR

        await Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════
    // TELEMETRY PUBLISHING
    // ═══════════════════════════════════════════════════════════════

    private async Task PublishTelemetryAsync(BotExecutionContext ctx, string eventType, string message)
    {
        try
        {
            await _telemetryHub.Clients.All.BotTelemetryUpdate(new BotTelemetryDto(
                BotId: ctx.Config.Id,
                BotName: ctx.Config.Name,
                Status: eventType,
                Message: message,
                Pnl: ctx.TotalPnl,
                WinRate: ctx.TotalTrades > 0 ? (decimal)ctx.WinningTrades / ctx.TotalTrades * 100m : null,
                TradesToday: ctx.TradesToday,
                TimestampUtc: DateTime.UtcNow
            ));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish telemetry for bot {BotId}", ctx.Config.Id);
        }
    }
}

// ═══════════════════════════════════════════════════════════════════
// SUPPORTING TYPES
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Bot execution context maintained in-memory for the lifetime of a bot run.
/// </summary>
public sealed class BotExecutionContext
{
    public BotConfig Config { get; }
    public CancellationTokenSource CancellationTokenSource { get; } = new();

    // ── Runtime state ──────────────────────────────────────────────
    public DateTime LastScanAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? LastOrderId { get; set; }
    public SignalType LastSignal { get; set; } = SignalType.None;

    // ── Performance tracking ───────────────────────────────────────
    public int TradesToday { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int ConsecutiveLosses { get; set; }
    public decimal DailyLoss { get; set; }
    public decimal TotalPnl { get; set; }
    public decimal CurrentExposure { get; set; }

    public BotExecutionContext(BotConfig config)
    {
        Config = config;
    }
}

/// <summary>
/// Configuration for a single bot instance.
/// In production, stored in PostgreSQL and loaded on service start.
/// </summary>
public sealed class BotConfig
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Unnamed Bot";
    public string Symbol { get; init; } = "BTCUSDT";
    public int ScanIntervalMs { get; init; } = 1_000;

    // Strategy parameters
    public bool UseEmaCrossover { get; init; } = true;
    public int FastEmaPeriod { get; init; } = 9;
    public int SlowEmaPeriod { get; init; } = 21;
    public bool UseRsi { get; init; }
    public int RsiPeriod { get; init; } = 14;
    public decimal RsiOversoldThreshold { get; init; } = 30m;
    public decimal RsiOverboughtThreshold { get; init; } = 70m;

    // Risk parameters
    public RiskMode RiskMode { get; init; } = RiskMode.FixedPercentage;
    public decimal RiskPerTradePercent { get; init; } = 2m; // 2% per trade
    public decimal AccountBalance { get; init; } = 10_000m;
    public decimal WinRate { get; init; } = 0.55m; // 55% historical win rate for Kelly
    public decimal PayoffRatio { get; init; } = 1.5m; // Avg win / avg loss

    // Hard limits
    public int MaxConsecutiveLosses { get; init; } = 5;
    public decimal MaxDailyLoss { get; init; } = 500m; // $500
    public int MaxDailyTrades { get; init; } = 50;
    public decimal MaxExposure { get; init; } = 5_000m; // $5k max position
    public decimal MaxDrawdownPercent { get; init; } = 15m; // 15% max drawdown
}

/// <summary>
/// Risk calculation modes.
/// </summary>
public enum RiskMode
{
    FixedPercentage,
    KellyCriterion
}

/// <summary>
/// Trading signal types produced by strategy evaluation.
/// </summary>
public enum SignalType
{
    None,
    Buy,
    Sell
}

/// <summary>
/// In-memory snapshot of market data used for strategy evaluation.
/// Fields are populated from the Redis cache layer in production.
/// </summary>
public sealed class MarketDataSnapshot
{
    public string Symbol { get; init; } = string.Empty;
    public decimal LastPrice { get; init; }
    public decimal BestBid { get; init; }
    public decimal BestAsk { get; init; }

    // Pre-computed indicators (computed by indicator engine, cached in Redis)
    public decimal? FastEma { get; init; }
    public decimal? SlowEma { get; init; }
    public decimal PreviousFastEma { get; init; }
    public decimal PreviousSlowEma { get; init; }
    public decimal? Rsi { get; init; }
    public decimal? MacdLine { get; init; }
    public decimal? SignalLine { get; init; }
}

/// <summary>
/// Global circuit breaker state. Tracks portfolio-level risk.
/// </summary>
public sealed class CircuitBreakerState
{
    private volatile bool _tripped;
    private readonly object _lock = new();

    public bool IsTripped => _tripped;

    /// <summary>
    /// Trip the breaker (e.g., when max daily drawdown exceeded).
    /// </summary>
    public void Trip()
    {
        lock (_lock) _tripped = true;
    }

    /// <summary>
    /// Reset the breaker (e.g., after manual override or next day).
    /// </summary>
    public void Reset()
    {
        lock (_lock) _tripped = false;
    }

    /// <summary>
    /// Evaluates whether the current portfolio state should trip.
    /// Called periodically by RiskMonitorService.
    /// </summary>
    public void Evaluate(decimal totalPnl, decimal maxDailyLoss)
    {
        if (Math.Abs(totalPnl) >= maxDailyLoss)
            Trip();
    }
}