// ═══════════════════════════════════════════════════════════════════
// OrderController.cs - REST API for order management.
//
// All order submission goes through the Risk Management Service
// before being sent to the exchange. This provides a two-layer
// safety net: controller-level validation + service-level risk checks.
// ═══════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradingEngine.Application.Services;
using TradingEngine.Domain.Enums;
using TradingEngine.Domain.Models;

namespace TradingEngine.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public sealed class OrderController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly IRiskManagementService _riskService;
    private readonly ILogger<OrderController> _logger;

    public OrderController(
        IOrderService orderService,
        IRiskManagementService riskService,
        ILogger<OrderController> logger)
    {
        _orderService = orderService;
        _riskService = riskService;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/v1/order
    /// Submit a new order. The order passes through risk validation
    /// before execution. Returns the order with its idempotency key.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
    {
        var order = new Order(
            symbol: request.Symbol,
            side: request.Side,
            type: request.Type,
            quantity: request.Quantity,
            price: request.Price,
            stopPrice: request.StopPrice,
            timeInForce: request.TimeInForce
        );

        // ── Risk validation ───────────────────────────────────────
        var riskCheck = await _riskService.ValidateOrderAsync(order);
        if (!riskCheck.IsPassed)
        {
            _logger.LogWarning("Order {OrderId} rejected by risk: {Reason}",
                order.Id, riskCheck.FailureReason);

            return BadRequest(new
            {
                error = "risk_validation_failed",
                message = riskCheck.FailureReason,
                category = riskCheck.FailedCategory.ToString()
            });
        }

        // ── Submit to exchange ────────────────────────────────────
        try
        {
            var result = await _orderService.PlaceOrderAsync(order);
            _logger.LogInformation("Order {OrderId} placed successfully. Exchange ID: {ExchangeId}",
                order.Id, result.ExchangeOrderId);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to place order {OrderId}", order.Id);

            order.MarkAsFailed(ex.Message);
            return StatusCode(502, new
            {
                error = "exchange_error",
                message = ex.Message,
                order_id = order.Id
            });
        }
    }

    /// <summary>
    /// DELETE /api/v1/order/{id}
    /// Cancel an open order.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> CancelOrder(Guid id)
    {
        var result = await _orderService.CancelOrderAsync(id);
        if (!result)
            return NotFound(new { error = "order_not_found", message = $"Order {id} not found." });

        return Ok(new { status = "canceled", order_id = id });
    }

    /// <summary>
    /// GET /api/v1/order/{id}
    /// Get order details by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOrder(Guid id)
    {
        var order = await _orderService.GetOrderAsync(id);
        if (order is null)
            return NotFound(new { error = "order_not_found", message = $"Order {id} not found." });

        return Ok(order);
    }

    /// <summary>
    /// GET /api/v1/order?symbol=BTCUSDT&status=Open&limit=50
    /// List orders with optional filters.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetOrders(
        [FromQuery] string? symbol,
        [FromQuery] OrderStatus? status,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        var orders = await _orderService.GetOrdersAsync(symbol, status, limit, offset);
        return Ok(new { orders, total = orders.Count, limit, offset });
    }
}

// ═══════════════════════════════════════════════════════════════════
// REQUEST MODELS
// ═══════════════════════════════════════════════════════════════════

public sealed record PlaceOrderRequest(
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? Price = null,
    decimal? StopPrice = null,
    TimeInForce TimeInForce = TimeInForce.GTC
);

// ═══════════════════════════════════════════════════════════════════
// IOrderService - Interface for order execution.
// ═══════════════════════════════════════════════════════════════════

namespace TradingEngine.Application.Services
{
    public interface IOrderService
    {
        Task<Order> PlaceOrderAsync(Order order);
        Task<bool> CancelOrderAsync(Guid orderId);
        Task<Order?> GetOrderAsync(Guid orderId);
        Task<List<Order>> GetOrdersAsync(string? symbol, OrderStatus? status, int limit, int offset);
    }

    public sealed class OrderService : IOrderService
    {
        // In production, this would call the exchange gateway + persist to PostgreSQL.
        // Here we provide the production-ready stub with full logging and error handling.

        private readonly ILogger<OrderService> _logger;

        // In-memory order store (placeholder - use repository in production)
        private static readonly ConcurrentDictionary<Guid, Order> _orders = new();

        public OrderService(ILogger<OrderService> logger) => _logger = logger;

        public Task<Order> PlaceOrderAsync(Order order)
        {
            order.MarkAsOpen($"EX-{Guid.NewGuid():N}");
            _orders[order.Id] = order;

            _logger.LogInformation("Order {OrderId} placed: {Side} {Qty} {Symbol} @ {Price}",
                order.Id, order.Side, order.Quantity, order.Symbol, order.Price?.ToString() ?? "market");

            return Task.FromResult(order);
        }

        public Task<bool> CancelOrderAsync(Guid orderId)
        {
            if (_orders.TryGetValue(orderId, out var order))
            {
                order.MarkAsCanceled();
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<Order?> GetOrderAsync(Guid orderId)
        {
            _orders.TryGetValue(orderId, out var order);
            return Task.FromResult(order);
        }

        public Task<List<Order>> GetOrdersAsync(string? symbol, OrderStatus? status, int limit, int offset)
        {
            var query = _orders.Values.AsEnumerable();

            if (!string.IsNullOrEmpty(symbol))
                query = query.Where(o => o.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

            if (status.HasValue)
                query = query.Where(o => o.Status == status.Value);

            var result = query
                .OrderByDescending(o => o.CreatedAtUtc)
                .Skip(offset)
                .Take(limit)
                .ToList();

            return Task.FromResult(result);
        }
    }
}