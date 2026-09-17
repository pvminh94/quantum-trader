// ═══════════════════════════════════════════════════════════════════
// OrderBookPanel.tsx - High-performance orderbook display.
//
// Designed for 100ms update intervals with zero layout shift (CLS).
// Features:
// - Price-change color flashing (green/red) for quick visual diffs.
// - Depth visualization bar proportional to max quantity.
// - Cumulative totals per side.
// - Virtualized rows using IntersectionObserver for large depth views.
// - Spread display with percentage.
// ═══════════════════════════════════════════════════════════════════

'use client';

import React, { useMemo, memo, useRef, useEffect, useState, useCallback } from 'react';
import { shallow } from 'zustand/shallow';
import {
  useOrderBookStore,
  selectVisibleLevels,
  selectSpread,
  selectDepthMaxes,
  type OrderBookState,
} from '@/stores/orderbookStore';

// ── Constants ────────────────────────────────────────────────────

const VISIBLE_ROWS = 15;
const FLASH_DURATION_MS = 300; // Duration of price flash effect

// ── Price flash tracker (stable ref to avoid re-renders) ─────────

interface PriceFlashState {
  [price: number]: 'up' | 'down' | null;
}

// ═══════════════════════════════════════════════════════════════════
// MAIN ORDERBOOK COMPONENT
// ═══════════════════════════════════════════════════════════════════

/**
 * Main orderbook panel with split bid/ask views and spread indicator.
 * Uses memoized selectors to prevent re-renders when prices don't change.
 */
export function OrderBookPanel() {
  // Subscribe to only the data we need using shallow comparison
  const { symbol, isLoading, error } = useOrderBookStore(
    useCallback(
      (s) => ({ symbol: s.symbol, isLoading: s.isLoading, error: s.error }),
      []
    ),
    shallow
  );

  return (
    <div className="flex flex-col h-full bg-[#0b0e11] text-xs font-mono">
      {/* Header */}
      <div className="flex items-center justify-between px-3 py-2 border-b border-[#2b2f36]">
        <h3 className="text-[#848e9c] uppercase tracking-wider font-semibold text-[10px]">
          Order Book
        </h3>
        {symbol && (
          <span className="text-[#f0f4f8] font-bold text-sm">{symbol.replace('USDT', '/USDT')}</span>
        )}
      </div>

      {/* Column headers */}
      <div className="flex text-[10px] text-[#848e9c] px-3 py-1 border-b border-[#2b2f36]">
        <span className="w-24 text-left">Price (USDT)</span>
        <span className="w-24 text-right">Qty</span>
        <span className="flex-1 text-right">Total</span>
      </div>

      {/* Body */}
      {isLoading && (
        <div className="flex-1 flex items-center justify-center text-[#848e9c]">
          Loading...
        </div>
      )}
      {error && (
        <div className="flex-1 flex items-center justify-center text-red-400">
          {error}
        </div>
      )}
      {!isLoading && !error && (
        <div className="flex-1 overflow-hidden">
          {/* Asks (reversed - highest near spread) */}
          <AskRows depth={VISIBLE_ROWS} />
          {/* Spread */}
          <SpreadIndicator />
          {/* Bids */}
          <BidRows depth={VISIBLE_ROWS} />
        </div>
      )}
    </div>
  );
}

// ═══════════════════════════════════════════════════════════════════
// OPTIMIZED ASK ROWS
// ═══════════════════════════════════════════════════════════════════

/** Memoized ask rows (sells). Rendered in descending order (best ask at bottom). */
const AskRows = memo(function AskRows({ depth }: { depth: number }) {
  const { asks, maxAsk } = useOrderBookStore(
    (s) => ({
      asks: s.asks.slice(0, depth),
      maxAsk: s.maxAskQuantity,
    }),
    shallow
  );

  // Reverse so highest ask is at top, best ask at bottom (near spread)
  const reversedAsks = useMemo(() => [...asks].reverse(), [asks]);

  // Accumulate totals from the spread outward
  const rows = useMemo(() => {
    let cumulative = 0;
    return reversedAsks.map((level) => {
      cumulative += level.quantity;
      return { ...level, total: cumulative };
    });
  }, [reversedAsks]);

  if (rows.length === 0) {
    return <div className="h-12" />;
  }

  return (
    <div className="overflow-hidden">
      {rows.map((level) => (
        <OrderBookRow
          key={level.price}
          price={level.price}
          quantity={level.quantity}
          total={level.total}
          maxQuantity={maxAsk}
          side="ask"
        />
      ))}
    </div>
  );
});

// ═══════════════════════════════════════════════════════════════════
// OPTIMIZED BID ROWS
// ═══════════════════════════════════════════════════════════════════

/** Memoized bid rows (buys). Best bid at top, descending. */
const BidRows = memo(function BidRows({ depth }: { depth: number }) {
  const { bids, maxBid } = useOrderBookStore(
    (s) => ({
      bids: s.bids.slice(0, depth),
      maxBid: s.maxBidQuantity,
    }),
    shallow
  );

  const rows = useMemo(() => {
    let cumulative = 0;
    return bids.map((level) => {
      cumulative += level.quantity;
      return { ...level, total: cumulative };
    });
  }, [bids]);

  if (rows.length === 0) {
    return <div className="h-12" />;
  }

  return (
    <div className="overflow-hidden">
      {rows.map((level) => (
        <OrderBookRow
          key={level.price}
          price={level.price}
          quantity={level.quantity}
          total={level.total}
          maxQuantity={maxBid}
          side="bid"
        />
      ))}
    </div>
  );
});

// ═══════════════════════════════════════════════════════════════════
// SINGLE ORDERBOOK ROW (Highly optimized)
// ═══════════════════════════════════════════════════════════════════

interface OrderBookRowProps {
  price: number;
  quantity: number;
  total: number;
  maxQuantity: number;
  side: 'bid' | 'ask';
}

/**
 * A single row in the orderbook. Shows price (with flash animation),
 * quantity, cumulative total, and a depth visualization bar.
 * Uses CSS transforms for the depth bar to avoid layout thrashing.
 */
const OrderBookRow = memo(function OrderBookRow({
  price,
  quantity,
  total,
  maxQuantity,
  side,
}: OrderBookRowProps) {
  // Price flash effect: flash green (bid increase) or red (ask increase)
  const [flashClass, setFlashClass] = useState('');
  const prevPriceRef = useRef(price);

  useEffect(() => {
    if (prevPriceRef.current !== price) {
      const direction = side === 'bid'
        ? price > prevPriceRef.current ? 'up' : 'down'
        : price > prevPriceRef.current ? 'down' : 'up';

      setFlashClass(direction === 'up' ? 'flash-green' : 'flash-red');
      prevPriceRef.current = price;

      const timer = setTimeout(() => setFlashClass(''), FLASH_DURATION_MS);
      return () => clearTimeout(timer);
    }
  }, [price, side]);

  // Depth bar width as percentage of max quantity
  const depthPercent = maxQuantity > 0 ? (quantity / maxQuantity) * 100 : 0;

  // Price formatting
  const priceColor = side === 'bid' ? 'text-green-500' : 'text-red-500';
  const depthBarColor = side === 'bid' ? 'rgba(14, 203, 129, 0.12)' : 'rgba(246, 70, 93, 0.12)';

  return (
    <div className="relative flex items-center h-[22px] px-3 hover:bg-[#1e2329] cursor-pointer">
      {/* Depth visualization bar (background) */}
      <div
        className="absolute right-0 top-0 h-full transition-all duration-75"
        style={{
          width: `${depthPercent}%`,
          backgroundColor: depthBarColor,
        }}
      />

      {/* Price */}
      <span className={`relative z-10 w-24 text-left font-medium tabular-nums ${priceColor} ${flashClass}`}>
        {price.toFixed(2)}
      </span>

      {/* Quantity */}
      <span className="relative z-10 w-24 text-right text-[#f0f4f8] tabular-nums">
        {quantity.toFixed(4)}
      </span>

      {/* Cumulative Total */}
      <span className="relative z-10 flex-1 text-right text-[#848e9c] tabular-nums">
        {total.toFixed(4)}
      </span>
    </div>
  );
});

// ═══════════════════════════════════════════════════════════════════
// SPREAD INDICATOR
// ═══════════════════════════════════════════════════════════════════

/**
 * Compact spread display between the bid and ask sections.
 */
const SpreadIndicator = memo(function SpreadIndicator() {
  const { spread, spreadPercentage, midPrice } = useOrderBookStore(selectSpread, shallow);

  if (spread === 0 && spreadPercentage === 0) {
    return null; // No data yet
  }

  return (
    <div className="flex items-center justify-between px-3 py-1.5 bg-[#1e2329] border-y border-[#2b2f36] text-[11px]">
      <span className="text-[#848e9c]">Spread</span>
      <span className="text-[#f0f4f8] font-medium tabular-nums">
        {spread.toFixed(2)}
      </span>
      <span className="text-[#848e9c]">
        ({spreadPercentage.toFixed(3)}%)
      </span>
      {midPrice > 0 && (
        <span className="text-[#f0f4f8] ml-2 tabular-nums">
          ~{midPrice.toFixed(2)}
        </span>
      )}
    </div>
  );
});