// ═══════════════════════════════════════════════════════════════════
// Order.cs - Core order domain model with state machine logic.
// Every order carries an idempotency key to guarantee exactly-once
// submission across network retries.
// ═══════════════════════════════════════════════════════════════════

using TradingEngine.Domain.Enums;

namespace TradingEngine.Domain.Models;

/// <summary>
/// Represents a single order placed on an exchange.
/// The order lifecycle follows a strict state machine enforced by
/// the CanTransitionTo method. No external code can put the order
/// into an invalid state.
/// </summary>
public sealed class Order
{
    /// <summary>
    /// UUID v7 for globally unique, time-sortable order identification.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Exchange-assigned order ID (populated after ACK from exchange).
    /// </summary>
    public string? ExchangeOrderId { get; private set; }

    /// <summary>
    /// Idempotency key: UUID derived from client request fingerprint.
    /// The exchange (or our gateway) deduplicates on this key,
    /// preventing duplicate fills during network retries.
    /// </summary>
    public string IdempotencyKey { get; }

    /// <summary>
    /// Trading pair symbol.
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// Buy or Sell.
    /// </summary>
    public OrderSide Side { get; }

    /// <summary>
    /// Market, Limit, StopLoss, TakeProfit, etc.
    /// </summary>
    public OrderType Type { get; }

    public OrderStatus Status { get; private set; }

    /// <summary>
    /// Limit price (null for market orders).
    /// </summary>
    public decimal? Price { get; }

    /// <summary>
    /// Stop price (for stop-limit / stop-market orders).
    /// </summary>
    public decimal? StopPrice { get; }

    /// <summary>
    /// Original ordered quantity.
    /// </summary>
    public decimal Quantity { get; }

    /// <summary>
    /// Quantity that has been filled so far.
    /// </summary>
    public decimal FilledQuantity { get; private set; }

    /// <summary>
    /// Average fill price (volume-weighted).
    /// </summary>
    public decimal? AveragePrice { get; private set; }

    /// <summary>
    /// Total quote value filled.
    /// </summary>
    public decimal QuoteQuantityFilled { get; private set; }

    public TimeInForce TimeInForce { get; }

    /// <summary>
    /// UTC timestamp when the order was created (local to this system).
    /// </summary>
    public DateTime CreatedAtUtc { get; }

    /// <summary>
    /// UTC timestamp of the last status change.
    /// </summary>
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Number of submission retries attempted.
    /// </summary>
    public int RetryCount { get; private set; }

    /// <summary>
    /// Human-readable failure reason, populated when Status = Failed.
    /// </summary>
    public string? FailureReason { get; private set; }

    public Order(
        string symbol,
        OrderSide side,
        OrderType type,
        decimal quantity,
        decimal? price = null,
        decimal? stopPrice = null,
        TimeInForce timeInForce = TimeInForce.GTC,
        string? idempotencyKey = null)
    {
        Id = Guid.NewGuid();
        IdempotencyKey = idempotencyKey ?? Guid.NewGuid().ToString("N");
        Symbol = Guard.NotNullOrWhiteSpace(symbol, nameof(symbol));
        Side = side;
        Type = type;
        Status = OrderStatus.Pending;
        Quantity = Guard.Positive(quantity, nameof(quantity));
        Price = price;
        StopPrice = stopPrice;
        TimeInForce = timeInForce;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    // Private constructor for ORM/materialization
    private Order() { }

    // ═══════════════════════════════════════════════════════════════
    // STATE MACHINE TRANSITIONS
    // Each method validates the transition is legal before mutating.
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Transition: Pending -> Open
    /// Called when the exchange acknowledges the order.
    /// </summary>
    public void MarkAsOpen(string exchangeOrderId)
    {
        Guard.NotNullOrWhiteSpace(exchangeOrderId, nameof(exchangeOrderId));
        AssertCanTransitionTo(OrderStatus.Open);

        ExchangeOrderId = exchangeOrderId;
        Status = OrderStatus.Open;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Transition: Open -> PartiallyFilled (or Pending -> Filled for immediate fills)
    /// Updates the fill bookkeeping and auto-transitions to Filled if fully filled.
    /// </summary>
    public void ApplyFill(decimal fillQuantity, decimal fillPrice)
    {
        Guard.Positive(fillQuantity, nameof(fillQuantity));
        Guard.Positive(fillPrice, nameof(fillPrice));

        // Validate the fill doesn't exceed remaining quantity
        var newFilled = FilledQuantity + fillQuantity;
        if (newFilled > Quantity)
            throw new InvalidOperationException(
                $"Fill quantity {fillQuantity} would exceed order remaining. " +
                $"Already filled: {FilledQuantity}/{Quantity}.");

        // Update volume-weighted average price
        var newQuoteFilled = QuoteQuantityFilled + (fillQuantity * fillPrice);
        AveragePrice = newQuoteFilled / newFilled;

        FilledQuantity = newFilled;
        QuoteQuantityFilled = newQuoteFilled;

        // Determine new status based on fill completeness
        Status = newFilled >= Quantity
            ? OrderStatus.Filled
            : OrderStatus.PartiallyFilled;

        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Transition: any non-terminal state -> Canceled.
    /// </summary>
    public void MarkAsCanceled()
    {
        AssertCanTransitionTo(OrderStatus.Canceled);
        Status = OrderStatus.Canceled;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Transition: any non-terminal state -> Failed.
    /// Terminal state; no further transitions allowed.
    /// </summary>
    public void MarkAsFailed(string reason)
    {
        Guard.NotNullOrWhiteSpace(reason, nameof(reason));
        AssertCanTransitionTo(OrderStatus.Failed);

        FailureReason = reason;
        Status = OrderStatus.Failed;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Increment retry counter. Called before each re-submission attempt.
    /// </summary>
    public void IncrementRetryCount()
    {
        RetryCount++;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Returns the remaining unfilled quantity.
    /// </summary>
    public decimal RemainingQuantity => Quantity - FilledQuantity;

    /// <summary>
    /// Is the order in a terminal state (no further transitions possible)?
    /// </summary>
    public bool IsTerminal =>
        Status is OrderStatus.Filled or OrderStatus.Canceled or OrderStatus.Failed;

    /// <summary>
    /// Valid state machine transitions:
    ///   Pending      -> Open | Filled | Failed
    ///   Open         -> PartiallyFilled | Filled | Canceled | Failed
    ///   PartiallyFilled -> Filled | Canceled | Failed
    ///   Filled/Canceled/Failed -> (terminal)
    /// </summary>
    private void AssertCanTransitionTo(OrderStatus target)
    {
        var allowed = (Status, target) switch
        {
            (OrderStatus.Pending, OrderStatus.Open) => true,
            (OrderStatus.Pending, OrderStatus.Filled) => true, // aggressive IOC fill
            (OrderStatus.Pending, OrderStatus.Failed) => true,
            (OrderStatus.Open, OrderStatus.PartiallyFilled) => true,
            (OrderStatus.Open, OrderStatus.Filled) => true,
            (OrderStatus.Open, OrderStatus.Canceled) => true,
            (OrderStatus.Open, OrderStatus.Failed) => true,
            (OrderStatus.PartiallyFilled, OrderStatus.Filled) => true,
            (OrderStatus.PartiallyFilled, OrderStatus.Canceled) => true,
            (OrderStatus.PartiallyFilled, OrderStatus.Failed) => true,
            _ => false
        };

        if (!allowed)
        {
            throw new InvalidOperationException(
                $"Invalid state transition: {Status} -> {target} " +
                $"(order {Id}). Terminal orders cannot be modified.");
        }
    }
}