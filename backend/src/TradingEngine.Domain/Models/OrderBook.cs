// ═══════════════════════════════════════════════════════════════════
// OrderBook.cs - Domain model representing a full depth orderbook
// Key design: Immutable snapshots with partial-update support.
// Low-latency: Uses arrays instead of dictionaries for price levels
// to minimize GC pressure during high-frequency updates.
// ═══════════════════════════════════════════════════════════════════

namespace TradingEngine.Domain.Models;

/// <summary>
/// Represents a single price level in the orderbook (aggregated by price).
/// </summary>
public readonly struct PriceLevel
{
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public int OrderCount { get; init; }

    /// <summary>
    /// Returns the notional value (Price * Quantity) for this level.
    /// Used for depth visualization and cumulative calculations.
    /// </summary>
    public decimal Notional => Price * Quantity;
}

/// <summary>
/// Immutable snapshot of the complete orderbook at a point in time.
/// Designed for zero-copy sharing across the SignalR pipeline.
/// Uses arrays to allow struct iteration without allocations.
/// </summary>
public sealed class OrderBook
{
    private static readonly PriceLevel[] EmptyLevels = Array.Empty<PriceLevel>();

    /// <summary>
    /// Trading pair symbol (e.g., "BTCUSDT").
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// Unix timestamp in milliseconds (from exchange).
    /// </summary>
    public long TimestampMs { get; }

    /// <summary>
    /// Last update ID from the exchange. Used for sequence validation
    /// to ensure no missed updates or out-of-order processing.
    /// </summary>
    public long LastUpdateId { get; }

    /// <summary>
    /// Sorted bids (best bid at index 0, descending prices).
    /// </summary>
    public PriceLevel[] Bids { get; }

    /// <summary>
    /// Sorted asks (best ask at index 0, ascending prices).
    /// </summary>
    public PriceLevel[] Asks { get; }

    /// <summary>
    /// Best bid price (fast access without array indexing).
    /// </summary>
    public decimal BestBid => Bids.Length > 0 ? Bids[0].Price : 0m;

    /// <summary>
    /// Best ask price.
    /// </summary>
    public decimal BestAsk => Asks.Length > 0 ? Asks[0].Price : 0m;

    /// <summary>
    /// Spread = BestAsk - BestBid (absolute).
    /// </summary>
    public decimal Spread => BestAsk - BestBid;

    /// <summary>
    /// Spread as a percentage of the mid price.
    /// </summary>
    public decimal SpreadPercentage
    {
        get
        {
            var mid = (BestAsk + BestBid) / 2m;
            return mid > 0 ? (Spread / mid) * 100m : 0m;
        }
    }

    /// <summary>
    /// Mid price (BestBid + BestAsk) / 2.
    /// </summary>
    public decimal MidPrice => (BestAsk + BestBid) / 2m;

    public OrderBook(
        string symbol,
        long timestampMs,
        long lastUpdateId,
        PriceLevel[]? bids,
        PriceLevel[]? asks)
    {
        Symbol = Guard.NotNullOrWhiteSpace(symbol, nameof(symbol));
        TimestampMs = Guard.Positive(timestampMs, nameof(timestampMs));
        LastUpdateId = Guard.Positive(lastUpdateId, nameof(lastUpdateId));

        // Defensive copy: we own our arrays after construction
        Bids = bids ?? EmptyLevels;
        Asks = asks ?? EmptyLevels;
    }

    /// <summary>
    /// Compute cumulative bid/ask totals up to a given depth level.
    /// Returns (bidTotal, askTotal).
    /// Used for market impact estimation.
    /// </summary>
    public (decimal bidLiquidity, decimal askLiquidity) GetLiquidityAtDepth(int depthLevels)
    {
        var bidTotal = 0m;
        var askTotal = 0m;

        var bidLimit = Math.Min(depthLevels, Bids.Length);
        for (var i = 0; i < bidLimit; i++)
            bidTotal += Bids[i].Quantity;

        var askLimit = Math.Min(depthLevels, Asks.Length);
        for (var i = 0; i < askLimit; i++)
            askTotal += Asks[i].Quantity;

        return (bidTotal, askTotal);
    }

    /// <summary>
    /// Validates internal consistency of the orderbook.
    /// Throws if invariants are violated (e.g., crossed book).
    /// </summary>
    public void Validate()
    {
        if (Bids.Length > 1)
        {
            for (var i = 1; i < Bids.Length; i++)
            {
                if (Bids[i].Price >= Bids[i - 1].Price)
                    throw new InvalidOperationException(
                        $"Bids not in descending order at index {i}: {Bids[i].Price} >= {Bids[i - 1].Price}");
            }
        }

        if (Asks.Length > 1)
        {
            for (var i = 1; i < Asks.Length; i++)
            {
                if (Asks[i].Price <= Asks[i - 1].Price)
                    throw new InvalidOperationException(
                        $"Asks not in ascending order at index {i}: {Asks[i].Price} <= {Asks[i - 1].Price}");
            }
        }

        // Ensure book is not crossed (best bid >= best ask would mean crossed)
        if (BestBid >= BestAsk && Bids.Length > 0 && Asks.Length > 0)
        {
            throw new InvalidOperationException(
                $"Crossed orderbook detected: best bid {BestBid} >= best ask {BestAsk}");
        }
    }
}