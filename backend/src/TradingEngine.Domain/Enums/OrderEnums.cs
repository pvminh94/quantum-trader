// ═══════════════════════════════════════════════════════════════════
// OrderEnums.cs - Enumerations for the order domain.
// Using strong enums to prevent invalid states at compile time.
// ═══════════════════════════════════════════════════════════════════

namespace TradingEngine.Domain.Enums;

/// <summary>
/// Order side: Buy or Sell.
/// </summary>
public enum OrderSide
{
    Buy = 0,
    Sell = 1
}

/// <summary>
/// Order type determines execution semantics.
/// </summary>
public enum OrderType
{
    /// <summary>Execute immediately at the best available price.</summary>
    Market = 0,

    /// <summary>Execute only at the specified price or better.</summary>
    Limit = 1,

    /// <summary>Convert to a market order when the stop price is reached.</summary>
    StopMarket = 2,

    /// <summary>Place a limit order when the stop price is reached.</summary>
    StopLimit = 3,

    /// <summary>Take-Profit limit order.</summary>
    TakeProfitLimit = 4,

    /// <summary>Take-Profit market order.</summary>
    TakeProfitMarket = 5,

    /// <summary>Trailing stop order (price follows market at a fixed distance).</summary>
    TrailingStopMarket = 6
}

/// <summary>
/// Current lifecycle status of the order.
/// </summary>
public enum OrderStatus
{
    /// <summary>Awaiting exchange acknowledgment.</summary>
    Pending = 0,

    /// <summary>Live on the exchange orderbook.</summary>
    Open = 1,

    /// <summary>Partially filled; remaining quantity still open.</summary>
    PartiallyFilled = 2,

    /// <summary>Fully filled.</summary>
    Filled = 3,

    /// <summary>Canceled by user or system.</summary>
    Canceled = 4,

    /// <summary>Permanently failed. See FailureReason for details.</summary>
    Failed = 5
}

/// <summary>
/// Time-in-force instructions for order execution.
/// </summary>
public enum TimeInForce
{
    /// <summary>Good-Til-Canceled: order stays active until filled or canceled.</summary>
    GTC = 0,

    /// <summary>Immediate-Or-Cancel: fill immediately what's possible, cancel rest.</summary>
    IOC = 1,

    /// <summary>Fill-Or-Kill: fill entirely or cancel entirely.</summary>
    FOK = 2,

    /// <summary>Good-Til-Date: order expires at a specified time.</summary>
    GTD = 3,

    /// <summary>Day order: expires at end of the trading session.</summary>
    DAY = 4
}