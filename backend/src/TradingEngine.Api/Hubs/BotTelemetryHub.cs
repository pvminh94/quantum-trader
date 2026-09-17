// ═══════════════════════════════════════════════════════════════════
// BotTelemetryHub.cs - SignalR hub for bot telemetry streaming.
//
// Connected clients (the dashboard UI) receive real-time updates on:
// - Bot status transitions (started, stopped, paused)
// - Trade execution events
// - PnL updates
// - Error/warning logs
//
// Separate from MarketDataHub to allow different scaling policies:
// Bot telemetry is per-user; market data is broadcast per-symbol.
// ═══════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.SignalR;
using TradingEngine.Api.Hubs;

/// <summary>
/// Client interface for bot telemetry.
/// </summary>
public interface IBotTelemetryClient
{
    /// <summary>
    /// Called when a bot's state changes or a log entry is produced.
    /// </summary>
    Task BotTelemetryUpdate(BotTelemetryDto telemetry);
}

/// <summary>
/// SignalR hub for bot telemetry. Clients join the "all" group to
/// receive all bot updates. Future: per-bot groups for filtering.
/// </summary>
public sealed class BotTelemetryHub : Hub<IBotTelemetryClient>
{
    private readonly ILogger<BotTelemetryHub> _logger;

    public BotTelemetryHub(ILogger<BotTelemetryHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        // All connected clients receive bot telemetry
        _logger.LogDebug("BotTelemetry client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("BotTelemetry client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}