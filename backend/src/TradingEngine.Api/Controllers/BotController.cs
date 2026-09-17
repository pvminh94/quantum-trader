// ═══════════════════════════════════════════════════════════════════
// BotController.cs - REST API for bot management.
//
// Endpoints:
//   POST   /api/v1/bot              - Start a new bot
//   DELETE /api/v1/bot/{id}          - Stop a running bot
//   POST   /api/v1/bot/kill          - Emergency stop ALL bots
//   GET    /api/v1/bot               - List all bots (running + configured)
//   GET    /api/v1/bot/{id}          - Get bot details + telemetry
//   PUT    /api/v1/bot/{id}/config   - Update bot configuration
// ═══════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradingEngine.Application.BackgroundServices;
using TradingEngine.Application.Services;

namespace TradingEngine.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public sealed class BotController : ControllerBase
{
    private readonly BotExecutionEngineService _botEngine;
    private readonly IRiskManagementService _riskService;
    private readonly ILogger<BotController> _logger;

    public BotController(
        BotExecutionEngineService botEngine,
        IRiskManagementService riskService,
        ILogger<BotController> logger)
    {
        _botEngine = botEngine;
        _riskService = riskService;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/v1/bot
    /// Start a new bot instance with the given configuration.
    /// </summary>
    [HttpPost]
    public IActionResult StartBot([FromBody] StartBotRequest request)
    {
        var config = new BotConfig
        {
            Name = request.Name,
            Symbol = request.Symbol,
            ScanIntervalMs = request.ScanIntervalMs,
            UseEmaCrossover = request.UseEmaCrossover,
            FastEmaPeriod = request.FastEmaPeriod,
            SlowEmaPeriod = request.SlowEmaPeriod,
            UseRsi = request.UseRsi,
            RsiPeriod = request.RsiPeriod,
            RsiOversoldThreshold = request.RsiOversoldThreshold,
            RsiOverboughtThreshold = request.RsiOverboughtThreshold,
            RiskMode = request.RiskMode,
            RiskPerTradePercent = request.RiskPerTradePercent,
            AccountBalance = request.AccountBalance,
            MaxConsecutiveLosses = request.MaxConsecutiveLosses,
            MaxDailyLoss = request.MaxDailyLoss,
            MaxDailyTrades = request.MaxDailyTrades,
            MaxExposure = request.MaxExposure,
            MaxDrawdownPercent = request.MaxDrawdownPercent
        };

        if (!_botEngine.StartBot(config))
        {
            return Conflict(new
            {
                error = "bot_start_failed",
                message = $"Bot '{request.Name}' could not be started. " +
                          "It may already be running, or the circuit breaker is tripped."
            });
        }

        _logger.LogInformation("Bot started: {Name} ({Symbol})", request.Name, request.Symbol);

        return Ok(new
        {
            id = config.Id,
            name = config.Name,
            symbol = config.Symbol,
            status = "running"
        });
    }

    /// <summary>
    /// DELETE /api/v1/bot/{id}
    /// Stop a running bot.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public IActionResult StopBot(Guid id)
    {
        if (!_botEngine.StopBot(id))
        {
            return NotFound(new
            {
                error = "bot_not_found",
                message = $"Bot with ID {id} was not found or already stopped."
            });
        }

        return Ok(new { status = "stopped", bot_id = id });
    }

    /// <summary>
    /// POST /api/v1/bot/kill
    /// Emergency stop ALL running bots.
    /// </summary>
    [HttpPost("kill")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> KillAllBots()
    {
        _logger.LogCritical("EMERGENCY KILL requested by {User}",
            User.Identity?.Name ?? "unknown");

        await _riskService.EmergencyKillAsync(
            $"Manual kill by {User.Identity?.Name ?? "unknown"}", CancellationToken.None);

        _botEngine.StopAllBots();

        return Ok(new
        {
            status = "kill_accepted",
            message = "All bots stopped. Circuit breaker tripped.",
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// GET /api/v1/bot
    /// List all managed bots with their status.
    /// </summary>
    [HttpGet]
    public IActionResult GetBots()
    {
        var activeCount = _botEngine.ActiveBotCount;
        var circuitBreaker = _riskService.GetGlobalStatus();

        return Ok(new
        {
            active_bot_count = activeCount,
            circuit_breaker = circuitBreaker,
            timestamp = DateTime.UtcNow
        });
    }
}

// ═══════════════════════════════════════════════════════════════════
// REQUEST MODELS
// ═══════════════════════════════════════════════════════════════════

public sealed record StartBotRequest(
    string Name,
    string Symbol,
    int ScanIntervalMs = 1_000,
    bool UseEmaCrossover = true,
    int FastEmaPeriod = 9,
    int SlowEmaPeriod = 21,
    bool UseRsi = false,
    int RsiPeriod = 14,
    decimal RsiOversoldThreshold = 30m,
    decimal RsiOverboughtThreshold = 70m,
    TradingEngine.Application.BackgroundServices.RiskMode RiskMode = RiskMode.FixedPercentage,
    decimal RiskPerTradePercent = 2m,
    decimal AccountBalance = 10_000m,
    int MaxConsecutiveLosses = 5,
    decimal MaxDailyLoss = 500m,
    int MaxDailyTrades = 50,
    decimal MaxExposure = 5_000m,
    decimal MaxDrawdownPercent = 15m
);