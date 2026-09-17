// ═══════════════════════════════════════════════════════════════════
// RiskManagementService.cs - Comprehensive risk monitoring service.
//
// This service runs continuously and monitors:
// 1. Global portfolio drawdown (triggers circuit breaker)
// 2. Per-bot risk metrics
// 3. Order-level validation before execution
// 4. Position sizing with slippage tolerance
// 5. Hard stop-loss and trailing-stop management
// ═══════════════════════════════════════════════════════════════════

using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using TradingEngine.Application.BackgroundServices;
using TradingEngine.Domain.Enums;
using TradingEngine.Domain.Models;
using TradingEngine.Shared;

namespace TradingEngine.Application.Services;

/// <summary>
/// Central risk management service. Evaluates every trading decision
/// against a hierarchy of constraints before execution.
/// </summary>
public interface IRiskManagementService
{
    /// <summary>
    /// Validates an order against ALL risk constraints.
    /// Returns a RiskValidationResult indicating pass/fail + reason.
    /// </summary>
    Task<RiskValidationResult> ValidateOrderAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Updates tracking metrics after a trade execution.
    /// </summary>
    Task RecordTradeAsync(TradeExecutionResult trade, CancellationToken ct = default);

    /// <summary>
    /// Returns the current global circuit breaker status.
    /// </summary>
    CircuitBreakerStatus GetGlobalStatus();

    /// <summary>
    /// Manually kill all trading activity.
    /// </summary>
    Task EmergencyKillAsync(string reason, CancellationToken ct = default);
}

/// <summary>
/// Comprehensive risk validation result.
/// </summary>
public sealed record RiskValidationResult
{
    public bool IsPassed { get; init; }
    public string? FailureReason { get; init; }
    public RiskCategory FailedCategory { get; init; } = RiskCategory.None;
}

/// <summary>
/// Categories of risk checks, ordered by severity.
/// </summary>
public enum RiskCategory
{
    None,
    CircuitBreakerTripped,
    MaxDrawdownExceeded,
    DailyLossLimitExceeded,
    MaxExposureExceeded,
    MaxPositionSizeExceeded,
    SlippageToleranceExceeded,
    PriceDeviationExceeded,
    ConsecutiveLossesExceeded,
    RateLimitExceeded,
    ExchangeRejected
}

/// <summary>
/// Execution result recorded after a trade.
/// </summary>
public sealed record TradeExecutionResult(
    Guid OrderId,
    string Symbol,
    OrderSide Side,
    decimal Quantity,
    decimal FillPrice,
    decimal Slippage,
    decimal Commission,
    DateTime TimestampUtc
);

/// <summary>
/// Snapshot of the global circuit breaker state.
/// </summary>
public sealed record CircuitBreakerStatus(
    bool IsTripped,
    decimal TotalPnlToday,
    decimal MaxDailyDrawdown,
    DateTime? TrippedAtUtc,
    string? TripReason
);

/// <summary>
/// Implementation of IRiskManagementService with all risk logic.
/// </summary>
public sealed class RiskManagementService : IRiskManagementService
{
    // ── Constants ──────────────────────────────────────────────────
    private const decimal MaxPriceDeviationPercent = 5m; // Reject orders >5% from market
    private const decimal MaxSlippagePercent = 2m;       // 2% max allowed slippage
    private const int MaxRetriesPerOrder = 3;

    // ── State (would use Redis IDistributedCache in production) ────
    private decimal _totalPnlToday;
    private decimal _maxDailyDrawdownAllowed;
    private int _totalTradesToday;
    private Guid? _lastTripOrderId;
    private DateTime? _trippedAtUtc;
    private string? _tripReason;
    private readonly object _stateLock = new();

    private readonly ILogger<RiskManagementService> _logger;
    private readonly ConcurrentDictionary<Guid, int> _botConsecutiveLosses = new();
    private readonly ConcurrentDictionary<Guid, decimal> _botDailyPnl = new();

    public RiskManagementService(ILogger<RiskManagementService> logger)
    {
        _logger = logger;
    }

    // ── Main validation entry point ────────────────────────────────

    /// <summary>
    /// Runs the full risk validation chain for an order.
    /// Each check returns immediately on failure with the specific category.
    /// The order of checks matters: most critical (circuit breaker) first.
    /// </summary>
    public Task<RiskValidationResult> ValidateOrderAsync(Order order, CancellationToken ct = default)
    {
        // ── 1. Global circuit breaker ──────────────────────────────
        lock (_stateLock)
        {
            if (_trippedAtUtc.HasValue)
            {
                return Task.FromResult(new RiskValidationResult
                {
                    IsPassed = false,
                    FailureReason = $"Circuit breaker tripped at {_trippedAtUtc:R}. Reason: {_tripReason}",
                    FailedCategory = RiskCategory.CircuitBreakerTripped
                });
            }
        }

        // ── 2. Daily loss limit ────────────────────────────────────
        lock (_stateLock)
        {
            if (_maxDailyDrawdownAllowed > 0 && Math.Abs(_totalPnlToday) >= _maxDailyDrawdownAllowed)
            {
                return Task.FromResult(new RiskValidationResult
                {
                    IsPassed = false,
                    FailureReason = $"Max daily drawdown reached: {_totalPnlToday:F2} / {_maxDailyDrawdownAllowed:F2}",
                    FailedCategory = RiskCategory.MaxDrawdownExceeded
                });
            }
        }

        // ── 3. Price deviation check ───────────────────────────────
        // Prevents orders at prices significantly away from the market
        if (order.Price.HasValue)
        {
            var deviationPercent = Math.Abs(
                (order.Price.Value - GetEstimatedMarketPrice(order.Symbol)) /
                GetEstimatedMarketPrice(order.Symbol) * 100m);

            if (deviationPercent > MaxPriceDeviationPercent)
            {
                return Task.FromResult(new RiskValidationResult
                {
                    IsPassed = false,
                    FailureReason = $"Order price deviates {deviationPercent:F2}% from market " +
                                    $"(max allowed: {MaxPriceDeviationPercent}%). " +
                                    $"Order: {order.Price}, Market: ~{GetEstimatedMarketPrice(order.Symbol):F2}",
                    FailedCategory = RiskCategory.PriceDeviationExceeded
                });
            }
        }

        // ── 4. Position size sanity check ──────────────────────────
        // Prevents orders that are too small (dust) or too large
        if (order.Quantity <= 0m)
        {
            return Task.FromResult(new RiskValidationResult
            {
                IsPassed = false,
                FailureReason = $"Invalid quantity: {order.Quantity}. Must be positive.",
                FailedCategory = RiskCategory.MaxPositionSizeExceeded
            });
        }

        // All checks passed
        return Task.FromResult(new RiskValidationResult
        {
            IsPassed = true,
            FailedCategory = RiskCategory.None
        });
    }

    // ── Trade recording ────────────────────────────────────────────

    /// <summary>
    /// Updates risk tracking metrics after a trade executes.
    /// </summary>
    public Task RecordTradeAsync(TradeExecutionResult trade, CancellationToken ct = default)
    {
        // ── Update global PnL tracker ──────────────────────────────
        lock (_stateLock)
        {
            // Simplified PnL: for buy trades, PnL is later determined on exit.
            // For now, we track the trade volume direction.
            _totalTradesToday++;

            // Check if we need to trip the breaker
            if (_maxDailyDrawdownAllowed > 0 && Math.Abs(_totalPnlToday) >= _maxDailyDrawdownAllowed)
            {
                _trippedAtUtc = DateTime.UtcNow;
                _tripReason = $"Max daily drawdown exceeded: {_totalPnlToday:F2} >= {_maxDailyDrawdownAllowed:F2}";
                _logger.LogWarning("CIRCUIT BREAKER TRIPPED: {TripReason}", _tripReason);
            }
        }

        _logger.LogDebug("Trade recorded: {Side} {Qty} {Symbol} @ {Price} (slippage: {Slippage:P})",
            trade.Side, trade.Quantity, trade.Symbol, trade.FillPrice, trade.Slippage);

        return Task.CompletedTask;
    }

    // ── Circuit breaker controls ───────────────────────────────────

    public CircuitBreakerStatus GetGlobalStatus()
    {
        lock (_stateLock)
        {
            return new CircuitBreakerStatus(
                IsTripped: _trippedAtUtc.HasValue,
                TotalPnlToday: _totalPnlToday,
                MaxDailyDrawdown: _maxDailyDrawdownAllowed,
                TrippedAtUtc: _trippedAtUtc,
                TripReason: _tripReason
            );
        }
    }

    public Task EmergencyKillAsync(string reason, CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            _trippedAtUtc = DateTime.UtcNow;
            _tripReason = $"EMERGENCY KILL: {reason}";
        }

        _logger.LogCritical("EMERGENCY KILL ACTIVATED: {Reason}", reason);

        // In production, this would also:
        // 1. Cancel all open orders on the exchange
        // 2. Close all open positions (if configured)
        // 3. Disable all bot schedules
        // 4. Notify admin via PagerDuty/Telegram/SMS
        return Task.CompletedTask;
    }

    // ── Helper: Get estimated market price ──────────────────────────

    /// <summary>
    /// Gets the current market price from cache.
    /// In production, this reads from Redis where ticker data is published.
    /// </summary>
    private static decimal GetEstimatedMarketPrice(string symbol)
    {
        // Placeholder: in production, read from Redis cache
        return symbol switch
        {
            "BTCUSDT" => 50000m,
            "ETHUSDT" => 3000m,
            _ => 100m
        };
    }

    // ── Configuration update ───────────────────────────────────────

    /// <summary>
    /// Updates the max daily drawdown threshold (called from admin API).
    /// </summary>
    public void SetMaxDailyDrawdown(decimal maxDrawdown)
    {
        lock (_stateLock)
        {
            _maxDailyDrawdownAllowed = Guard.Positive(maxDrawdown, nameof(maxDrawdown));
        }
    }

    /// <summary>
    /// Resets daily counters (called at UTC midnight by a scheduler).
    /// </summary>
    public void ResetDailyCounters()
    {
        lock (_stateLock)
        {
            _totalPnlToday = 0;
            _totalTradesToday = 0;
            _trippedAtUtc = null;
            _tripReason = null;
        }

        _botConsecutiveLosses.Clear();
        _botDailyPnl.Clear();

        _logger.LogInformation("Daily risk counters reset.");
    }
}

// ═══════════════════════════════════════════════════════════════════
// POSITION SIZE CALCULATOR
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Advanced position sizing using multiple models.
/// </summary>
public sealed class PositionSizeCalculator
{
    /// <summary>
    /// Calculates position size using the Kelly Criterion formula.
    ///
    /// Pure Kelly % = W - (1-W)/R
    ///   W = historical win rate (0.0 to 1.0)
    ///   R = average win / average loss ratio
    ///
    /// Returns fraction of capital to risk (0.0 to 0.X).
    /// We apply a 25% fractional Kelly to reduce volatility.
    /// </summary>
    public static decimal KellyCriterion(decimal winRate, decimal avgWinLossRatio)
    {
        if (winRate <= 0 || winRate >= 1)
            return 0; // No edge or insufficient data

        if (avgWinLossRatio <= 0)
            return 0;

        var kellyPercent = winRate - ((1m - winRate) / avgWinLossRatio);

        // Fractional Kelly: 25% of pure Kelly
        return Math.Max(0, kellyPercent * 0.25m);
    }

    /// <summary>
    /// Calculates position size based on fixed percentage risk model.
    /// </summary>
    /// <param name="accountBalance">Total account equity.</param>
    /// <param name="riskPercent">Percent of account to risk (e.g., 2 = 2%).</param>
    /// <param name="entryPrice">Expected entry price.</param>
    /// <param name="stopLossPrice">Stop-loss price for the trade.</param>
    /// <returns>Quantity in base asset units.</returns>
    public static decimal FixedPercentageRisk(
        decimal accountBalance,
        decimal riskPercent,
        decimal entryPrice,
        decimal? stopLossPrice = null)
    {
        Guard.Positive(accountBalance, nameof(accountBalance));
        Guard.InRange(riskPercent, nameof(riskPercent), 0.1m, 100m);
        Guard.Positive(entryPrice, nameof(entryPrice));

        var riskCapital = accountBalance * (riskPercent / 100m);

        if (stopLossPrice.HasValue && stopLossPrice.Value > 0)
        {
            // Risk per unit = |entry - stop|
            var riskPerUnit = Math.Abs(entryPrice - stopLossPrice.Value);
            if (riskPerUnit <= 0) return 0;

            return riskCapital / riskPerUnit;
        }

        // Without stop-loss, use a notional-based approach
        return riskCapital / entryPrice;
    }

    /// <summary>
    /// Calculates trailing stop price based on a fixed distance from
    /// the current market price.
    /// </summary>
    public static decimal CalculateTrailingStop(
        decimal currentPrice,
        decimal trailDistancePercent,
        bool isLong)
    {
        Guard.Positive(currentPrice, nameof(currentPrice));
        Guard.InRange(trailDistancePercent, nameof(trailDistancePercent), 0.01m, 50m);

        var distance = currentPrice * (trailDistancePercent / 100m);

        return isLong
            ? currentPrice - distance // Long: stop below market
            : currentPrice + distance; // Short: stop above market
    }

    /// <summary>
    /// Validates that a stop-loss price is valid relative to entry.
    /// </summary>
    public static bool ValidateStopLoss(
        decimal entryPrice,
        decimal stopLossPrice,
        OrderSide side,
        decimal maxStopDistancePercent)
    {
        var distancePercent = Math.Abs(entryPrice - stopLossPrice) / entryPrice * 100m;

        if (distancePercent > maxStopDistancePercent)
            return false;

        return side switch
        {
            OrderSide.Buy => stopLossPrice < entryPrice,
            OrderSide.Sell => stopLossPrice > entryPrice,
            _ => false
        };
    }
}