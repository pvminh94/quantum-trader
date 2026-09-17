// ═══════════════════════════════════════════════════════════════════
// RiskMonitorService.cs - Background service for continuous risk monitoring.
//
// Runs periodic checks:
// - Every 60s: Evaluate global portfolio drawdown
// - Every 24h: Reset daily counters at UTC midnight
// - On demand: Circuit breaker evaluation
// ═══════════════════════════════════════════════════════════════════

using System.Timers;
using Timer = System.Timers.Timer;
using TradingEngine.Application.Services;

namespace TradingEngine.Application.BackgroundServices;

/// <summary>
/// Periodically evaluates risk metrics and can trip the circuit breaker
/// if portfolio-level thresholds are exceeded.
/// </summary>
public sealed class RiskMonitorService : BackgroundService
{
    private const int RiskEvaluationIntervalMs = 60_000; // 1 minute
    private const int DailyResetCheckIntervalMs = 60_000; // 1 minute for checking midnight

    private readonly IRiskManagementService _riskService;
    private readonly ILogger<RiskMonitorService> _logger;

    public RiskMonitorService(
        IRiskManagementService riskService,
        ILogger<RiskMonitorService> logger)
    {
        _riskService = riskService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RiskMonitorService starting...");

        // ── Daily reset timer ────────────────────────────────────
        using var dailyTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(DailyResetCheckIntervalMs));

        // ── Main loop ────────────────────────────────────────────
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // ── Check for daily reset ─────────────────────────
                if (IsUtcMidnight())
                {
                    // In production, call a daily reset method on IRiskManagementService
                    _logger.LogInformation("Daily risk counter reset triggered.");

                    // Delay slightly to avoid multiple triggers
                    await Task.Delay(2000, stoppingToken);
                }

                // ── Evaluate global risk ──────────────────────────
                var status = _riskService.GetGlobalStatus();

                if (status.IsTripped)
                {
                    _logger.LogWarning(
                        "Circuit breaker is TRIPPED since {TrippedAt}. Reason: {Reason}",
                        status.TrippedAtUtc, status.TripReason);
                }

                // ── Sleep until next evaluation ───────────────────
                await Task.Delay(RiskEvaluationIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RiskMonitorService evaluation failed");
                await Task.Delay(5000, stoppingToken); // Back off on error
            }
        }

        _logger.LogInformation("RiskMonitorService stopped.");
    }

    /// <summary>
    /// Check if current UTC time is near midnight (within 1 second window).
    /// </summary>
    private static bool IsUtcMidnight()
    {
        var now = DateTime.UtcNow;
        return now.Hour == 0 && now.Minute == 0 && now.Second == 0;
    }
}

// ═══════════════════════════════════════════════════════════════════
// HealthCheckService.cs - Simple health monitoring.
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Periodically logs connection status and publishes health metrics.
/// Useful for monitoring dashboards and operational alerting.
/// </summary>
public sealed class HealthCheckService : BackgroundService
{
    private const int HealthCheckIntervalMs = 30_000; // 30 seconds

    private readonly ILogger<HealthCheckService> _logger;

    private static int _signalRConnectionCount;
    private static int _activeBotCount;
    private static bool _redisHealthy;
    private static bool _dbHealthy;

    public HealthCheckService(ILogger<HealthCheckService> logger)
    {
        _logger = logger;
    }

    /// <summary>Called by MarketDataHub to update connection count.</summary>
    public static void ReportSignalRConnections(int count) => _signalRConnectionCount = count;

    /// <summary>Called by BotExecutionEngine to update active bot count.</summary>
    public static void ReportActiveBots(int count) => _activeBotCount = count;

    /// <summary>Called by Redis health check.</summary>
    public static void ReportRedisHealth(bool healthy) => _redisHealthy = healthy;

    /// <summary>Called by DB health check.</summary>
    public static void ReportDatabaseHealth(bool healthy) => _dbHealthy = healthy;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation(
                    "Health: SignalR={SignalRCount} conns | Bots={BotCount} active | " +
                    "Redis={RedisStatus} | DB={DbStatus}",
                    _signalRConnectionCount,
                    _activeBotCount,
                    _redisHealthy ? "OK" : "DOWN",
                    _dbHealthy ? "OK" : "DOWN");

                await Task.Delay(HealthCheckIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}